using System;
using System.Threading;
using System.Threading.Tasks;
using Midas.Core.Common;

using MongoDB.Driver;
using MongoDB.Bson;

using Midas.Core.Broker;
using System.Dynamic;
using Newtonsoft.Json.Linq;
using Midas.Core.Util;
using MongoDB.Bson.Serialization.Attributes;
using Midas.Core.Trade;
using System.Linq;
using Midas.Core.Telegram;
using System.Collections.Generic;
using System.Net.Http.Headers;
using Midas.Core.Indicators;
using DateTimeUtil;
using System.Text;
using System.Collections.Concurrent;
using Midas.Core;

namespace Midas.Trading
{
    public class TradeOperation : ILogger
    {
        private int TIMEOUT_BUY = 200000;
        private int TIMEOUT_SELL = 60000;

        private float MAKER_SELL_DISCOUNT = 0.03f;

        private float TARGET_GAIN = 0.5f;

        private bool TEST_MODE = false;

        private FundSlot _fundSlot;

        private double _stopLossMarker;
        private double _softStopLossMarker;
        private DateTime _softStopTime;

        private double _lastMaxValue;

        private TradeOperationState _state;

        private Candle _lastCandle;
        private DateTime _exitDate, _entryDate, _forecastDate;
        private DateTime _exitDateInUtc;
        private DateTime _lastLongPrediction;

        private double _priceEntry;

        private MongoClient _dbClient;

        private BsonObjectId _myId;
        private string _myStrId;

        private Broker _broker;
        private TradeOperationManager _myMan;

        private string _asset;
        private CandleType _candleType;

        private DateTime _lastUpdate;

        private double _lastMA;

        private List<TraceEntry> _logs;

        private double _firstStopLossRate;
        private string _modelName;
        private double _transactionAmount;

        private ConcurrentBag<BrokerOrder> _orders;

        private PurchaseStepManager _stepMan;

        private MatchMaker _matchMaker;

        private int _sequentialId;

        private ConcurrentDictionary<string, BrokerOrder> _buyToSellMap;

        public TradeOperation(TradeOperationManager man, string connectionString, string brokerType, dynamic config)
        {
            _myMan = man;
            _dbClient = new MongoClient(connectionString);

            _broker = Broker.GetBroker(brokerType, config, this);

            _entryDateInUTC = DateTime.MinValue;

            TIMEOUT_BUY = Convert.ToInt32(config.TIMEOUT_BUY);
            TIMEOUT_SELL = Convert.ToInt32(config.TIMEOUT_SELL);
            MAKER_SELL_DISCOUNT = Convert.ToSingle(config.MAKER_SELL_DISCOUNT);
            TARGET_GAIN = Convert.ToSingle(config.TARGET_GAIN);

            _exitDateStart = DateTime.MinValue;

            _softStopTime = DateTime.MinValue;

            _logs = new List<TraceEntry>();
            _orders = new ConcurrentBag<BrokerOrder>();
            _sequentialId = 0;
            _buyToSellMap = new ConcurrentDictionary<string, BrokerOrder>();

            TEST_MODE = RunParameters.GetInstance().IsTesting;
        }

        private void Log(string module, string description)
        {
            DateTime now = DateTime.UtcNow;


            _logs.Add(new TraceEntry()
            {
                Title = module,
                Description = description
            });

            TraceAndLog.StaticLog(module, description);
        }

        public string GetNextOrderId()
        {
            Interlocked.Increment(ref _sequentialId);
            return $"{this._myStrId}{_sequentialId}";
        }

        public bool IsClassic()
        {
            return GetGain() > 2 && OperationDurationInPeriods > 6;
        }

        public DateTime LastCloseDate
        {
            get
            {
                return _lastCandle == null ? DateTime.MinValue : _lastCandle.CloseTime;
            }
        }

        public TradeOperation(TradeOperationManager man, FundSlot fund, DateTime forecastPeriod, string connectionString, dynamic config,
        string asset, CandleType candleType, string brokerType) : this(man, connectionString, brokerType, (JObject)config)
        {
            var objId = ObjectId.GenerateNewId(DateTime.Now);
            _myStrId = objId.ToString();
            _myId = new BsonObjectId(objId);

            _lastMaxValue = -1;

            _fundSlot = fund;

            _stepMan = new PurchaseStepManager(_fundSlot.SlotAmount, config.PurchaseSteps);
            ChangeState(TradeOperationState.Initial);

            _exitDate = DateTime.MaxValue;
            _forecastDate = forecastPeriod;

            _asset = asset;
            _candleType = candleType;

            int windowSize = Convert.ToInt32(config.MSI_WINDOW_SIZE_SECONDS);

            if (!TEST_MODE)
                _matchMaker = new MatchMaker(_asset);
        }

        public TradeOperation(TradeOperationDto state, FundSlot fundSlot, TradeOperationManager man, string connectionString, string brokerType, dynamic config) : this(man, connectionString, brokerType, (JObject)config)
        {
            _fundSlot = fundSlot;
            _softStopLossMarker = state.SoftStopLossMarker;
            _state = state.State;

            _myId = ObjectId.Parse(state._id.ToString());
            _myStrId = _myId.ToString();

            _stopLossMarker = state.StopLossMarker;

            _asset = state.Asset;
            _candleType = state.CandleType;
            _modelName = state.ModelName;
            _lastUpdate = state.LastUpdate;

            _exitDate = state.ExitDate;
            _entryDate = state.EntryDate;
            _entryDateInUTC = DateTime.UtcNow;
            _lastLongPrediction = _entryDate;
            _forecastDate = state.ForecastDate;
            _lastMaxValue = state.MaxValue;
            _transactionAmount = state.Amount_USD;

            _lastCandle = new Candle()
            {
                PointInTime_Open = DateTime.UtcNow,
                PointInTime_Close = DateTime.UtcNow.AddMinutes(5),
                AmountValue = state.LastValue
            };

            TradeRunner();
        }

        internal void Signal(TrendType trend)
        {
            // if ((trend == TrendType.LONG || trend == TrendType.DOUBLE_LONG) && _lastCandle != null)
            // {
            //     if (GetCurrentMaxGain() <= 0.5)
            //     {
            //         _lastLongPrediction = _lastCandle.OpenTime;
            //         if (LastValue < PriceEntryAverage)
            //             _stopLossMarker = GetStopLoss(_lastCandle.AmountValue, _firstStopLossRate);
            //     }
            // }
        }

        public double PriceEntryAverage
        {
            get
            {
                var buyOrders = _orders.Where(o => o.Direction == OrderDirection.BUY && o.CalculatedStatus == BrokerOrderStatus.FILLED);
                return buyOrders.Sum(o => o.Quantity * o.CalculatedAverageValue) / buyOrders.Sum(o => o.Quantity);
            }
        }

        public double PriceExitAverage
        {
            get
            {
                var sellOrders = _orders.Where(o => o.Direction == OrderDirection.SELL && o.CalculatedStatus == BrokerOrderStatus.FILLED);
                if (sellOrders.Count() > 0)
                    return sellOrders.Sum(o => o.Quantity * o.CalculatedAverageValue) / sellOrders.Sum(o => o.Quantity);
                else
                {
                    return 0;
                }
            }
        }

        private double _entryRAtr;
        private DateTime _entryDateInUTC;
        private DateTime _exitDateStart;
        private DateTime _exitDateStartInUtc;

        public void Enter(double price, DateTime point, double ratr, string modelName = "")
        {
            _modelName = modelName;
            if (_state == TradeOperationState.Initial)
            {
                double stopLoss = ratr * _myMan.Trader.AssetParams.AtrStopLoss;
                _entryRAtr = ratr;
                //double stopLoss = 0.01;
                _firstStopLossRate = stopLoss;

                _entryDate = point;
                _entryDateInUTC = DateTime.UtcNow;
                _priceEntry = price;
                _lastMaxValue = _priceEntry;
                _lastLongPrediction = _entryDate;

                Console.WriteLine($"Starting operation, SL: {stopLoss*100:0.00}%");

                _stopLossMarker = GetStopLoss(price, stopLoss);

                ChangeState(TradeOperationState.In);
                TradeRunner();

                if (_matchMaker != null)
                    _matchMaker.Start();
            }
            else
            {
                throw new ArgumentException("Trying to enter an operation in " + _state + " state.");
            }
        }

        public bool WaitOperationToFinish(int timeout)
        {
            int sleepTime = 100;
            int elapsedTime = 0;
            while (IsIn || elapsedTime > timeout)
            {
                Thread.Sleep(sleepTime);
                elapsedTime += sleepTime;
            }

            bool ret = !IsIn;

            return ret;
        }

        public async void CloseOperationAsync(bool hard = true)
        {
            await AskToCloseOperation(hard);
        }

        private void CancelAllOpenOrders()
        {
            foreach (var o in _orders.Where(o => o.IsPending))
            {
                _broker.CancelOrder(o.OrderId, _asset, TIMEOUT_BUY);
            }
        }

        private async Task AskToCloseOperation(bool hardStopped = true)
        {
            if (_state == TradeOperationState.In)
            {
                if (ChangeState(TradeOperationState.WaitingOut))
                {
                    var amountLeftToSell = GetAmountLeftToSell();

                    CancelAllOpenOrders();

                    if (amountLeftToSell > 0)
                    {
                        BrokerOrder sellOrder = null;
                        sellOrder = await AddMarketOrderAsync(OrderDirection.SELL, amountLeftToSell, TIMEOUT_SELL, LastValue, LastValue, LastCloseDate);

                        if (!sellOrder.InError)
                        {
                            if (_exitDateStart == DateTime.MinValue)
                            {
                                _exitDateStart = _lastCandle.CloseTime;
                                _exitDateStartInUtc = DateTime.UtcNow;
                            }
                        }
                    }
                }
            }

        }

        private double GetStopLoss(double price, double stopLoss)
        {
            return price * (1 - stopLoss);
        }

        public double LastMaxGain
        {
            get
            {
                if (_lastMaxValue > 0)
                    return ((_lastMaxValue - PriceEntryAverage) / _lastMaxValue) * 100;
                else
                    return 0;
            }
        }

        internal bool ShouldStopByMovingAverage()
        {
            bool shouldStop = false;
            bool mustStop = false;

            var ma = _myMan.Trader.GetMAValue("MA6");
            if (OperationDurationInPeriods > 6) //Timeout da operação, desde o inicio ou último sinal de long
            {
                //if(LastValue > PriceEntryAverage)
                    shouldStop = true;
            }

            if (shouldStop)
            {
                _lastMA = ma;

                if (shouldStop && LastValue != -1 && _myMan.TradeLogger.SoftCompare(ma, LastValue, _myMan.Trader.AssetParams.AvgCompSoftness) == CompareType.LessThan)
                    mustStop = true;
            }

            return mustStop;
        }

        internal bool ShouldStopByTime()
        {
            return OperationDurationInPeriods >= 6;
        }

        public double LastValue
        {
            get
            {
                return (_lastCandle == null ? 0 : _lastCandle.CloseValue);
            }
        }

        public double StoredAverage
        {
            get
            {
                return _lastMA;
            }
        }

        public double EntrySpreadAverage
        {
            get
            {
                if (_orders.Count > 0)
                {
                    var avgEntrySpread = _orders
                    .Where(o => o.Direction == OrderDirection.BUY)
                    .Average(o => ((o.CalculatedAverageValue - o.DesiredPrice) / o.DesiredPrice) * 100);

                    return avgEntrySpread;
                }
                else
                {
                    return 0;
                }
            }
        }

        public double ExitSpreadAverage
        {
            get
            {
                if (_orders.Count > 0)
                {
                    var sellOrders = _orders
                    .Where(o => o.Direction == OrderDirection.SELL && o.CalculatedStatus == BrokerOrderStatus.FILLED);

                    double avgExitSpread = 0;
                    if (sellOrders.Count() > 0)
                    {
                        avgExitSpread = sellOrders
                        .Average(o => ((o.CalculatedAverageValue - o.DesiredPrice) / o.DesiredPrice) * 100);
                    }

                    return avgExitSpread;
                }
                else
                {
                    return 0;
                }
            }
        }

        public TimeSpan OperationDuration
        {
            get
            {
                if (_lastCandle != null)
                {
                    return _lastCandle.OpenTime - EntryDate;
                }
                else
                {
                    return new TimeSpan(0, 0, 0);
                }
            }
        }

        public TimeSpan OperationDurationUTC
        {
            get
            {
                return DateTime.UtcNow - _entryDateInUTC;
            }
        }

        private TimeSpan LastLongSignalDuration
        {
            get
            {
                if (_lastCandle != null)
                {
                    return _lastCandle.OpenTime - _lastLongPrediction;
                }
                else
                {
                    return new TimeSpan(0, 0, 0);
                }
            }
        }

        private int LastLongSignalDurationInPeriods
        {
            get
            {
                return Convert.ToInt32(LastLongSignalDuration.TotalMinutes / Convert.ToInt32(_candleType));
            }
        }

        public int OperationDurationInPeriods
        {
            get
            {
                return Convert.ToInt32(OperationDuration.TotalMinutes / Convert.ToInt32(_candleType));
            }
        }

        public DateTime ExitDate
        {
            get
            {
                return _exitDate == DateTime.MaxValue
                    ?
                    _forecastDate
                    :
                    _exitDate;
            }
        }

        public DateTime ExitDateInUtc
        {
            get
            {
                return _exitDateInUtc;
            }
        }

        public DateTime EntryDate { get { return _entryDate; } }

        public DateTime EntryDateInUTC { get { return _entryDateInUTC; } }

        public double StopLossMark { get { return _stopLossMarker; } }

        public TradeOperationState State { get => _state; }

        public override string ToString()
        {
            StringBuilder text = new StringBuilder();

            text.Append($"Asset:{_asset}:{_candleType.ToString()} - Gain: {GetGain().ToString("0.000")}%\nEntry: {this.PriceEntryAverage.ToString("0.00")}\n");
            text.Append($"Duration: {TimeSpanPlus.ToString(this.OperationDurationUTC)}\nLast Long:{TimeSpanPlus.ToString(LastLongSignalDuration)}\nEntry Spread: {EntrySpreadAverage.ToString("0.00")}%\n");
            text.Append($"State: {State}\nExit Spread: {this.ExitSpreadAverage.ToString("0.00")}%\n");
            text.Append($"EntryDate: {EntryDateInUTC.ToLocalTime().ToString("yyyy-MM-dd HH:mm")}\n");
            text.Append($"Strengh: {GetRelativeAmountPurchased(BrokerOrderStatus.NEW):0.0}/{GetRelativeAmountPurchased(BrokerOrderStatus.FILLED):0.0}/100.0");

            text.Append("\n\nOrders:\n");

            var copiesOrders = _orders.ToList();

            copiesOrders.ForEach(o =>
            {
                text.Append(o.ToString());
                text.AppendLine();
            });

            return text.ToString();
        }

        public double GetAmountPurchased()
        {
            var amountPurchased = _orders
            .Where(o => o.Direction == OrderDirection.BUY && o.CalculatedStatus == BrokerOrderStatus.FILLED)
            .Sum(o => o.Quantity);

            return amountPurchased;
        }

        public double GetAmountLeftToSell()
        {
            var amountPurchased = _orders
            .Where(o => o.Direction == OrderDirection.BUY && o.CalculatedStatus == BrokerOrderStatus.FILLED)
            .Sum(o => o.Quantity);

            var amountSold = _orders
            .Where(o => o.Direction == OrderDirection.SELL && o.CalculatedStatus == BrokerOrderStatus.FILLED)
            .Sum(o => o.Quantity);

            return amountPurchased - amountSold;
        }

        public double GetRelativeAmountPurchased(BrokerOrderStatus status)
        {
            var amountPurchased = _orders
            .Where(o => o.Direction == OrderDirection.BUY && o.CalculatedStatus == status)
            .Sum(o => o.Quantity);

            return (amountPurchased / _fundSlot.SlotAmount) * 100;
        }

        public string PurchaseStatusDescription
        {
            get
            {
                return $"Strengh: {GetRelativeAmountPurchased(BrokerOrderStatus.NEW):0.0}/{GetRelativeAmountPurchased(BrokerOrderStatus.FILLED):0.0}/100.0";
            }
        }

        private Task _houseKeepingTask;
        private Task _checkStatusTask;

        private void TradeRunner()
        {
            _houseKeepingTask = Task.Run(() =>
            {
                try
                {
                    while (this.IsIn || this.State == TradeOperationState.Initial)
                    {
                        ProcessOperationStatus();

                        this.Persist();

                        Thread.Sleep(1000);
                    }

                    if (_matchMaker != null)
                        _matchMaker.Dispose();

                    Console.WriteLine("House keeping leaving...");
                }
                catch (Exception err)
                {
                    LogMessage("House Keeping", err.ToString());
                }
            });

            _checkStatusTask = Task.Run(() =>
            {
                try
                {
                    while (this.IsIn || this.State == TradeOperationState.Initial)
                    {
                        Thread.Sleep(STATUS_WAIT);
                        ProcessOrderStatus();
                    }

                    Console.WriteLine("Check status leaving...");
                }
                catch (Exception err)
                {
                    LogMessage("TradeRunner", err.ToString());
                }
            });
        }

        private BrokerOrder AddLimitOrder(OrderDirection direction, double qty, int timeout, double priceToExecute, double currentPrice, DateTime date)
        {
            var newOrder = _broker.LimitOrder(GetNextOrderId(), _asset, direction, qty, timeout, priceToExecute, currentPrice, date);

            ProcessOrderAdd(newOrder);

            return newOrder;
        }

        private async Task<BrokerOrder> AddLimitOrderAsync(OrderDirection direction, double qty, int timeout, double priceToExecute, double currentPrice, DateTime date)
        {
            var newOrder = await _broker.LimitOrderAsync(GetNextOrderId(), _asset, direction, qty, timeout, priceToExecute, currentPrice, date);

            ProcessOrderAdd(newOrder);

            return newOrder;
        }

        private async Task<BrokerOrder> AddMarketOrderAsync(OrderDirection direction, double qty, int timeout, double priceToExecute, double currentPrice, DateTime date)
        {
            var newOrder = await _broker.MarketOrderAsync(GetNextOrderId(), _asset, direction, qty, timeout, currentPrice, date, false);

            ProcessOrderAdd(newOrder);

            return newOrder;
        }

        private void ProcessOrderAdd(BrokerOrder newOrder)
        {
            if (!newOrder.InError)
            {
                _orders.Add(newOrder);
                if (!TEST_MODE && newOrder.Type == OrderType.LIMIT)
                    _myMan.OrderWatcher.AddOrder(newOrder);
            }
            else
                LogMessage("Order", "Error when setting limit orders to sell - " + newOrder.ErrorMsg);
        }


        private bool FailedEntryCheck()
        {
            bool ret = false;
            var confirmedOrders = _orders.Where(o => o.Direction == OrderDirection.BUY && o.CalculatedStatus == BrokerOrderStatus.FILLED);

            if (confirmedOrders.Count() == 0) // Não temos ordens confirmadas ainda
            {
                if (OperationDurationInPeriods > 15)
                    ret = true;
            }

            return ret;
        }

        private bool OperationClosedCheck()
        {
            bool ret = false;

            var quantityBought = _orders
            .Where(o => o.Direction == OrderDirection.BUY && o.CalculatedStatus == BrokerOrderStatus.FILLED)
            .Sum(o => o.Quantity);

            var quantitySold = _orders
            .Where(o => o.Direction == OrderDirection.SELL && o.CalculatedStatus == BrokerOrderStatus.FILLED)
            .Sum(o => o.Quantity);


            if (quantityBought > 0 && quantitySold > 0 && quantityBought.ToString("0.0000") == quantitySold.ToString("0.0000"))
            {
                ret = true;
            }

            return ret;
        }

        private static int STATUS_WAIT = 30000;
        private void ProcessOrderStatus()
        {
            var copiesOrders = _orders.OrderBy(o => o.CreationDate).ToList();
            foreach (var o in copiesOrders)
            {
                if (o.Status == BrokerOrderStatus.None || o.Status == BrokerOrderStatus.NEW || o.Status == BrokerOrderStatus.PARTIALLY_FILLED)
                {
                    try
                    {
                        var order = _broker.OrderStatus(o.OrderId, _asset, TIMEOUT_BUY);
                        if (!order.InError)
                        {
                            o.Status = order.Status;
                            o.RawStatus = order.RawStatus;
                            o.AverageValue = order.AverageValue;

                            if (order.Status == BrokerOrderStatus.FILLED)
                                Console.WriteLine($"Status orderId:{order.BrokerOrderId} - {order.RawStatus} - {order.Quantity} - {o.AverageValue}");
                        }
                        else
                        {
                            LogMessage("TradeRunner", "Broker error checking status - " + order.ErrorMsg);
                        }

                        Thread.Sleep(STATUS_WAIT);
                    }
                    catch (Exception err)
                    {
                        LogMessage("TradeRunner", "General error checking status - " + err.Message);
                    }
                }
            }
        }

        private void ProcessOperationStatus()
        {
            //Analisar este código para se precisarmos trabalhar com ordem limit
            // var copiesOrders = _orders.OrderBy(o => o.CreationDate).ToList();
            // foreach (var o in copiesOrders)
            // {
            //     if (o.CalculatedStatus == BrokerOrderStatus.FILLED && o.Direction == OrderDirection.BUY)
            //     {
            //         if (!_buyToSellMap.ContainsKey(o.OrderId))
            //         {
            //             lock (_buyToSellMap)
            //             {
            //                 if (!_buyToSellMap.ContainsKey(o.OrderId))
            //                 {
            //                     var prices = new List<dynamic>();
            //                     int stepCount = 2;
            //                     prices.Add(
            //                     new {
            //                         Price = this._priceEntry * (1 + (0.3 / 100)),
            //                         OrderId = this.GetNextOrderId(),
            //                         Qty = o.Quantity / stepCount
            //                     });
            //                     prices.Add(new {
            //                         Price = this._priceEntry * (1 + (0.4 / 100)),
            //                         OrderId = this.GetNextOrderId(),
            //                         Qty = o.Quantity / stepCount
            //                     });
            //                     // prices.Add(new {
            //                     //     Price = this._priceEntry * (1 + (0.5 / 100)),
            //                     //     OrderId = this.GetNextOrderId(),
            //                     //     Qty = o.Quantity / stepCount
            //                     // });
            //                     // prices.Add(new {
            //                     //     Price = this._priceEntry * (1 + (0.6 / 100)),
            //                     //     OrderId = this.GetNextOrderId(),
            //                     //     Qty = o.Quantity / stepCount
            //                     // });

            //                     foreach(var buyStep in prices)
            //                     {
            //                         Console.WriteLine($"Sending order {buyStep.OrderId} @ {buyStep.Price:0.00}");

            //                         var sellOrder = AddLimitOrder(OrderDirection.SELL, buyStep.Qty, TIMEOUT_SELL, buyStep.Price, buyStep.Price, LastCloseDate);
            //                         if (!sellOrder.InError)
            //                             _buyToSellMap[o.OrderId] = sellOrder;
            //                     }
            //                 }
            //             }

            //         }
            //     }

            // };

            if (OperationClosedCheck())
            {
                ExecuteCloseOperationTasks();
            }

            if (FailedEntryCheck())
            {
                if (ChangeState(TradeOperationState.FailedIn))
                {
                    CancelAllOpenOrders();
                    ExecuteCloseOperationTasks(true);
                }
            }

            // if (FailedExitCheck())
            // {
            //     ChangeState(TradeOperationState.In);
            //     await AskToCloseOperation(true);
            // }
        }

        private void ExecuteCloseOperationTasks(bool hasFailed = false)
        {
            Console.WriteLine("Iniciando saida: \n");
            _myMan.OperationFinished(this, _lastCandle, _fundSlot);

            _exitDateInUtc = DateTime.UtcNow;
            _exitDate = LastCloseDate;

            string prefix = TelegramEmojis.PERSON_SHRUGGING;
            if (!hasFailed)
            {
                if (PriceExitAverage >= PriceEntryAverage)
                {
                    ChangeState(TradeOperationState.Profit);
                    prefix = TelegramEmojis.MoneyBag;
                }
                else
                {
                    ChangeState(TradeOperationState.Stopped);
                    prefix = TelegramEmojis.MeanSmirking;
                }
            }


            Console.WriteLine("Terminando saida\n" + this.ToString());
            if (_myMan != null)
                _myMan.SendMessage(this._asset, prefix + " - Concluindo operação - " + this.ToString());
        }

        public async void OnCandleUpdateAsync(Candle newCandle)
        {
            _lastCandle = newCandle;
            _lastUpdate = DateTime.UtcNow;

            try
            {

                if (_state == TradeOperationState.In)
                {
                    if (newCandle.CloseValue > _lastMaxValue)
                        _lastMaxValue = newCandle.CloseValue;

                    PurchaseStep firstStepToExecute = null;
                    List<PurchaseStep> currentSteps = new List<PurchaseStep>();
                    lock (_stepMan)
                    {
                        if (_orders.Count == 0)
                        {
                            firstStepToExecute = _stepMan.GetFirstStep();
                            if(firstStepToExecute != null)
                                currentSteps.Add(firstStepToExecute);
                        }
                        else
                        {
                            currentSteps = _stepMan.GetSteps(LastMaxGain);
                            firstStepToExecute = currentSteps.FirstOrDefault();
                        }

                        currentSteps.ForEach(s => s.SetUsed());
                    }

                    if (firstStepToExecute != null)
                    {
                        try
                        {
                            double suggestedPrice = 0;
                            double totalUnits = currentSteps.Sum(s => s.TotalUnits); //The sum of all the units the price have outgrown so far

                            if (TEST_MODE)
                                suggestedPrice = LastValue;
                            else
                            {
                                //May be use this code if we want do send the first step a little bellow (Just for production use)
                                // if (firstStepToExecute.Number == 1)
                                //     suggestedPrice = LastValue * (1 - (0.03 / 100));
                                // else

                                suggestedPrice = _matchMaker.GetPurchasePrice(totalUnits);
                            }

                            if (suggestedPrice > 0)
                            {
                                //Enviar MarketOrder
                                await AddMarketOrderAsync(OrderDirection.BUY, totalUnits, TIMEOUT_BUY, suggestedPrice, LastValue, LastCloseDate);
                            }
                        }
                        catch (Exception err)
                        {
                            LogMessage("Order", "Error when buying - " + err.ToString());
                        }
                    }

                    if (LastMaxGain >= _myMan.Trader.AssetParams.GainSoftStopTrigger)
                    {
                        double chasePerc = LastMaxGain * _myMan.Trader.AssetParams.FollowPricePerc;;

                        chasePerc = Math.Max(chasePerc, 0.2);

                        _softStopLossMarker = PriceEntryAverage * (1 + (chasePerc / 100));
                    }

                    var mustStopBySoftStop = LastValue <= _softStopLossMarker;

                    var mustStopByAvg = ShouldStopByMovingAverage();

                    var mustStopByStopLoss = _myMan.TradeLogger.SoftCompare(_stopLossMarker, newCandle.CloseValue, _myMan.Trader.AssetParams.StopLossCompSoftness) == CompareType.LessThan;

                    if (//mustStopBySoftStop ||
                        mustStopByStopLoss || //StopLoss
                        mustStopByAvg)
                    {
                        await AskToCloseOperation(true);

                        Persist();
                    }

                    ProcessOperationStatus();
                }
            }
            catch (Exception err)
            {
                LogMessage("CandleUpder", $"Candle update error - {err.ToString()}");
            }
        }

        private bool ChangeState(TradeOperationState state)
        {
            bool ret = false;
            lock (this)
            {
                if (state != _state)
                {
                    _state = state;
                    ret = true;
                }
            }

            if (ret)
            {
                if (_myMan != null && IsFailed)
                    _myMan.SendMessage(null, "STATE FAILURE: " + this.ToString());

                if (state != TradeOperationState.Initial)
                    Persist();
            }

            return ret;
        }

        public double GetGain()
        {
            return GetGain(0);
        }

        public double GetGain(double closeValue)
        {
            double compareValue;
            if (closeValue == 0)
            {
                compareValue = (PriceExitAverage == 0 ? (LastValue) : PriceExitAverage);
            }
            else
            {
                compareValue = closeValue;
            }

            var gain = ((compareValue - PriceEntryAverage) / PriceEntryAverage) * 100;
            var percPurchased = GetAmountPurchased() / _fundSlot.SlotAmount;

            return gain * percPurchased;
        }

        public double GetCurrentMaxGain()
        {
            return ((_lastMaxValue - PriceEntryAverage) / PriceEntryAverage) * 100;
        }

        private TradeOperationDto GetMyDto(bool withLogs)
        {
            var ret = new TradeOperationDto()
            {
                Asset = _asset,
                ModelName = _modelName,
                Experiment = _myMan.Experiment,
                LastUpdate = _lastUpdate,
                CandleType = _candleType,
                Amount = _fundSlot.SlotAmount,
                Amount_USD = _transactionAmount,
                ExitDate = this.ExitDate,
                EntryDate = this.EntryDate,
                ExitDateUtc = this.ExitDateInUtc,
                EntryDateUtc = this.EntryDateInUTC,
                ForecastDate = this._forecastDate,
                StopLossMarker = this._stopLossMarker,
                SoftStopLossMarker = this._softStopLossMarker,
                PriceEntryDesired = this.PriceEntryAverage,
                PriceEntryReal = this.PriceEntryAverage,
                PriceExitDesired = this.PriceExitAverage,
                PriceExitReal = this.PriceExitAverage,
                EntrySpreadAverage = this.EntrySpreadAverage,
                ExitSpreadAverage = this.ExitSpreadAverage,
                RATR = this._entryRAtr,
                MaxValue = this._lastMaxValue,
                LastValue = this.LastValue,
                Gain = this.GetGain(),
                _id = this._myId,
                State = this.State
            };

            if (withLogs)
                ret.Logs = this._logs;

            return ret;
        }

        public bool IsIn
        {
            get
            {
                return _state == TradeOperationState.In || _state == TradeOperationState.WaitingIn || _state == TradeOperationState.WaitingOut;
            }
        }

        private bool IsFailed
        {
            get
            {
                return _state == TradeOperationState.FailedIn || _state == TradeOperationState.FailedOut;
            }
        }

        public bool IsCompleted
        {
            get
            {
                return !IsIn;
            }
        }

        public double SoftStopLossMarker { get => _softStopLossMarker; }
        public void Persist(bool withLogs = true)
        {
            try
            {
                var myDto = this.GetMyDto(withLogs);

                var database = _dbClient.GetDatabase("CandlesFaces");

                var dbCol = database.GetCollection<TradeOperationDto>("TradeOperations");

                var result = dbCol.ReplaceOne(
                    item => item._id == myDto._id,
                    myDto,
                    new ReplaceOptions { IsUpsert = true });
            }
            catch (Exception err)
            {
                TraceAndLog.StaticLog("Persist", err.ToString());
            }
        }

        public void LogHttpCall(string action, HttpRequestHeaders headers, HttpResponseHeaders respHeaders, string completeUrl, string body)
        {
            TraceAndLog.GetInstance().LogTraceHttpAction("TradeOperation", action, headers, respHeaders, completeUrl, body);

            _logs.Add(new TraceEntry()
            {
                Title = String.Concat(action, " ", completeUrl),
                Description = body
            });
        }

        public void LogMessage(string module, string message)
        {
            _logs.Add(new TraceEntry()
            {
                Title = module,
                Description = message
            });


            TraceAndLog.StaticLog(module, message);
        }
    }


    public class TradeOperationDto
    {
        public object _id
        {
            get;
            set;
        }

        public List<TraceEntry> Logs
        {
            get;
            set;
        }

        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime ExitDate { get; set; }

        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime EntryDate { get; set; }

        public double StopLossMarker { get; set; }

        public double PriceEntryReal { get; set; }
        public double PriceEntryDesired { get; set; }

        public double PriceExitReal { get; set; }
        public double PriceExitDesired { get; set; }

        public double Gain { get; set; }

        public TradeOperationState State { get; set; }
        public DateTime ForecastDate { get; internal set; }
        public double LastValue { get; internal set; }
        public double MaxValue { get; internal set; }
        public double SoftStopLossMarker { get; internal set; }
        public string Asset { get; internal set; }
        public CandleType CandleType { get; internal set; }
        public string Experiment { get; internal set; }
        public DateTime LastUpdate { get; internal set; }
        public string ModelName { get; internal set; }
        public double Amount { get; internal set; }
        public double Amount_USD { get; internal set; }
        public double EntrySpreadAverage { get; internal set; }
        public double ExitSpreadAverage { get; internal set; }
        public DateTime ExitDateUtc { get; internal set; }
        public DateTime EntryDateUtc { get; internal set; }
        public double RATR { get; internal set; }
    }

    public enum TradeOperationState
    {
        Initial = 0,
        WaitingIn = 1,
        WaitingOut = 2,
        FailedIn = 3,
        In = 4,
        Stopped = 5,
        Profit = 6,

        FailedOut = 7,
        None = 8
    }
}
