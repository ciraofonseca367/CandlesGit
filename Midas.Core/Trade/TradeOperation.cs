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
using System.Drawing;
using System.IO;

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
        private double _limitStopLossMarker;
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
        private string _shortAsset;
        private CandleType _candleType;

        private DateTime _lastUpdate;

        private double _lastMA;

        private List<TraceEntry> _logs;

        private double _firstStopLossRate;
        private string _modelName;
        private double _transactionAmount;

        private ConcurrentBag<BrokerOrder> _orders;

        private PurchaseStepManager _stepMan;

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

            _softStopLossMarker = -1;
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

        public DateTime LastCloseDate
        {
            get
            {
                return _lastCandle == null ? DateTime.MinValue : _lastCandle.CloseTime;
            }
        }

        public string Id
        {
            get
            {
                return _myStrId;
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

            _exitDate = DateTime.MaxValue;
            _forecastDate = forecastPeriod;

            _asset = asset;
            _shortAsset = asset.Substring(0, 3);
            _candleType = candleType;

            int windowSize = Convert.ToInt32(config.MSI_WINDOW_SIZE_SECONDS);

            // if (!TEST_MODE)
            //     _matchMaker = new MatchMaker(_asset);
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
            _shortAsset = _asset.Substring(0, 3);
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
                return buyOrders.Sum(o => o.CalculatedExecutedQuantity * o.CalculatedAverageValue) / buyOrders.Sum(o => o.CalculatedExecutedQuantity);
            }
        }

        public double PriceEntry
        {
            get
            {
                return _priceEntry;
            }
        }

        public double PriceExitAverage
        {
            get
            {
                var sellOrders = _orders.Where(o => o.Direction == OrderDirection.SELL && o.CalculatedStatus == BrokerOrderStatus.FILLED);
                if (sellOrders.Count() > 0)
                    return sellOrders.Sum(o => o.CalculatedExecutedQuantity * o.CalculatedAverageValue) / sellOrders.Sum(o => o.CalculatedExecutedQuantity);
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

        private double _stopLossConfig;

        private Bitmap _myEnterShot;

        public async Task Enter(double price, DateTime point, double ratr, string modelName, System.Drawing.Bitmap image)
        {
            _myEnterShot = image;
            _modelName = modelName;
            if (_state == TradeOperationState.Initial)
            {
                _stepMan = new PurchaseStepManager(_fundSlot.SlotAmount, RunParameters.GetInstance().GetHyperParam(_shortAsset, modelName, "PurchaseSteps"));

                //double stopLoss = ratr * _myMan.Trader.AssetParams.AtrStopLoss;
                _entryRAtr = ratr;
                //double stopLoss = FIXED_STOPLOSS;
                var percAtr = RunParameters.GetInstance().GetHyperParamAsDouble(_shortAsset, _modelName, "AtrStopLoss");
                Console.WriteLine("Perc Atr: "+percAtr);
                _stopLossConfig = _entryRAtr * percAtr;
                _firstStopLossRate = _stopLossConfig;

                _entryDate = point;
                _entryDateInUTC = DateTime.UtcNow;
                _priceEntry = price;
                _lastMaxValue = _priceEntry;
                _lastLongPrediction = _entryDate;

                Console.WriteLine($"Starting operation, SL: {_stopLossConfig * 100:0.00}%");

                var baseStopLoss = GetStopLoss(price, _stopLossConfig);
                _limitStopLossMarker = baseStopLoss;
                _stopLossMarker = baseStopLoss * (1 - (_entryRAtr * 0.04));

                await ChangeState(TradeOperationState.In);
                TradeRunner();
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

        public async Task ForceGetOrderStatuses()
        {
            await ProcessOrderStatus();
        }

        public async Task CloseOperationAsync(bool hard = true)
        {
            await CloseOperation(hard);
        }

        private async Task CancelAllOpenOrders()
        {
            await _broker.CancelAllOpenOrdersAsync(_asset, TIMEOUT_BUY);

            await ProcessOrderStatus();
        }

        private void SaveShot()
        {
            string dir = Path.Join(RunParameters.GetInstance().OutputDirectory, RunParameters.GetInstance().ExperimentName);
            string label = this.GetGain() > 1 ? "Success" : "Failure";

            var dirLabel = Directory.CreateDirectory(
                Path.Join(dir, ModelName, label)
            );

            _myEnterShot.Save(dirLabel.FullName + "/" + this.LastCloseDate.ToString("yyyyMMddHHmm") + ".gif");
        }

        public async Task AskToSoftCloseOperation()
        {
            double amountLeftToSell;
            bool locked = true;

            lock (this)
            {
                if (!_softCloseSent)
                {
                    locked = false;
                    _softCloseSent = true;
                    amountLeftToSell = GetAmountLeftToSell();
                }
            }

            if (!locked)
            {
                await CancelAllOpenOrders();

                amountLeftToSell = GetAmountLeftToSell();

                var suggestedPrice = LastValue;
                var priceToSell = suggestedPrice * (1 + (_entryRAtr * 0.01));
                await AddLimitOrderAsync(OrderDirection.SELL, amountLeftToSell, TIMEOUT_SELL, priceToSell, LastValue, LastCloseDate);
            }
        }

        private async Task CloseOperation(bool hardStopped = true)
        {
            SaveShot();

            if (_state == TradeOperationState.In)
            {
                if (await ChangeState(TradeOperationState.WaitingOut))
                {
                    var amountLeftToSell = GetAmountLeftToSell();

                    await CancelAllOpenOrders();

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

        public double LastMaxGainRelative
        {
            get
            {
                if (_lastMaxValue > 0)
                    return ((_lastMaxValue - PriceEntryAverage) / _lastMaxValue) * 100;
                else
                    return 0;
            }
        }
        public double LastMaxGainAbs
        {
            get
            {
                if (_lastMaxValue > 0)
                    return ((_lastMaxValue - _priceEntry) / _lastMaxValue) * 100;
                else
                    return 0;
            }
        }



        //private static int AVG_STOP_NUMBER = 12;
        internal bool ShouldStopByMovingAverage(int avgNumber)
        {
            bool shouldStop = false;
            bool mustStop = false;

            int avg = avgNumber;

            var ma = _myMan.Trader.GetMAValue($"MA{avg}");
            if (OperationDurationInPeriods > avg) //Timeout da operação, desde o inicio ou último sinal de long
            {
                //if(LastValue > PriceEntryAverage)
                shouldStop = true;
            }

            if (shouldStop)
            {
                _lastMA = ma;

                if (shouldStop && LastValue != -1 && SoftCompare(ma, LastValue, RunParameters.GetInstance().GetHyperParamAsDouble(_shortAsset, _modelName, "AvgCompSoftness")) == CompareType.LessThan)
                    mustStop = true;
            }

            return mustStop;
        }

        public CompareType SoftCompare(double amountA, double amountB, double softnessRatio)
        {
            var currentPrice = LastValue;
            var atr = _myMan.Trader.GetMAValue("ATR");
            var ratr = atr / currentPrice;

            var ratrSoft = ratr * softnessRatio;

            double softA = amountA * (1 - ratrSoft);

            CompareType res = CompareType.Equal;
            if (amountB >= amountA)
                res = CompareType.GreatherThan;

            if (amountB < softA)
                res = CompareType.LessThan;

            return res;
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
                    double avgEntrySpread = 0;

                    var confirmedOrders = _orders
                    .Where(o => o.Direction == OrderDirection.BUY && !o.IsPending);

                    if (confirmedOrders.Count() > 0)
                    {
                        avgEntrySpread = confirmedOrders.Average(o => ((o.CalculatedAverageValue - o.DesiredPrice) / o.DesiredPrice) * 100);
                    }

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

            text.Append($"Model:{ModelName}\n");
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
            .Where(o => o.Direction == OrderDirection.BUY && (o.CalculatedStatus == BrokerOrderStatus.FILLED || o.CalculatedStatus == BrokerOrderStatus.PARTIALLY_FILLED))
            .Sum(o => o.CalculatedExecutedQuantity);

            return amountPurchased;
        }

        public double GetAmountLeftToSell()
        {
            var amountPurchased = _orders
            .Where(o => o.Direction == OrderDirection.BUY && (o.CalculatedStatus == BrokerOrderStatus.FILLED || o.CalculatedStatus == BrokerOrderStatus.PARTIALLY_FILLED))
            .Sum(o => o.CalculatedExecutedQuantity);

            var amountSold = _orders
            .Where(o => o.Direction == OrderDirection.SELL && (o.CalculatedStatus == BrokerOrderStatus.FILLED || o.CalculatedStatus == BrokerOrderStatus.PARTIALLY_FILLED))
            .Sum(o => o.CalculatedExecutedQuantity);

            return amountPurchased - amountSold;
        }

        public double GetRelativeAmountPurchased(BrokerOrderStatus status)
        {
            var amountPurchased = _orders
            .Where(o => o.Direction == OrderDirection.BUY && o.CalculatedStatus == status)
            .Sum(o => o.CalculatedExecutedQuantity);

            return (amountPurchased / _fundSlot.SlotAmount) * 100;
        }

        public string PurchaseStatusDescription
        {
            get
            {
                return $"Stgh: {GetRelativeAmountPurchased(BrokerOrderStatus.FILLED):0.0}/100.0";
            }
        }

        private Task _houseKeepingTask;

        private void TradeRunner()
        {
            _houseKeepingTask = Task.Run(async () =>
            {
                try
                {
                    while (this.IsIn || this.State == TradeOperationState.Initial)
                    {
                        await ProcessOperationStatus();

                        await ProcessOrderStatus();

                        await this.Persist();

                        Thread.Sleep(1000 * 60 * 1);
                    }

                    Console.WriteLine("House keeping leaving...");
                }
                catch (Exception err)
                {
                    LogMessage("House Keeping", err.ToString());
                }
            });
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
                if (newOrder.Type == OrderType.LIMIT)
                    _myMan.WatchOrder(newOrder);
            }
            else
                LogMessage("Order", "Error when setting limit orders to sell - " + newOrder.ErrorMsg);
        }


        private bool FailedEntryCheck()
        {
            bool ret = false;
            var confirmedOrders = _orders.Where(o => o.Direction == OrderDirection.BUY && (o.CalculatedStatus == BrokerOrderStatus.FILLED || o.CalculatedStatus == BrokerOrderStatus.PARTIALLY_FILLED));

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
            .Where(o => o.Direction == OrderDirection.BUY && (o.CalculatedStatus == BrokerOrderStatus.FILLED || o.CalculatedStatus == BrokerOrderStatus.PARTIALLY_FILLED))
            .Sum(o => o.CalculatedExecutedQuantity);

            var quantitySold = _orders
            .Where(o => o.Direction == OrderDirection.SELL && (o.CalculatedStatus == BrokerOrderStatus.FILLED || o.CalculatedStatus == BrokerOrderStatus.PARTIALLY_FILLED))
            .Sum(o => o.CalculatedExecutedQuantity);


            if (quantityBought > 0 && quantitySold > 0 && quantitySold.ToString("0.0000") == quantityBought.ToString("0.0000"))
                ret = true;

            return ret;
        }

        private async Task ProcessOrderStatus()
        {
            var copiesOrders = _orders.OrderBy(o => o.CreationDate).ToList();
            foreach (var o in copiesOrders)
            {
                if (o.Status == BrokerOrderStatus.None || o.Status == BrokerOrderStatus.NEW || o.Status == BrokerOrderStatus.PARTIALLY_FILLED)
                {
                    try
                    {
                        var order = await _broker.OrderStatusAsync(o.OrderId, _asset, TIMEOUT_BUY);
                        if (!order.InError)
                        {
                            o.Status = order.Status;
                            o.RawStatus = order.RawStatus;
                            o.AverageValue = order.AverageValue;

                            if (order.Status == BrokerOrderStatus.FILLED)
                                Console.WriteLine($"Status orderId:{order.BrokerOrderId} - {order.RawStatus} - {order.CalculatedExecutedQuantity} - {o.AverageValue}");
                        }
                        else
                        {
                            LogMessage("TradeRunner", "Broker error checking status - " + order.ErrorMsg);
                        }
                    }
                    catch (Exception err)
                    {
                        LogMessage("TradeRunner", "General error checking status - " + err.Message);
                    }
                }
            }
        }

        private async Task ProcessOperationStatus()
        {
            if (OperationClosedCheck())
            {
                await _myMan.OperationFinished(this, _lastCandle, _fundSlot);
                await ExecuteCloseOperationTasks();
            }
        }

        private async Task ExecuteCloseOperationTasks()
        {
            Console.WriteLine($"Iniciando saida: {this._myStrId}\n");

            _exitDateInUtc = DateTime.UtcNow;
            _exitDate = LastCloseDate;

            string prefix = TelegramEmojis.PERSON_SHRUGGING;
            if (PriceExitAverage >= PriceEntryAverage)
            {
                await ChangeState(TradeOperationState.Profit);
                prefix = TelegramEmojis.MoneyBag;
            }
            else
            {
                await ChangeState(TradeOperationState.Stopped);
                prefix = TelegramEmojis.MeanSmirking;
            }

            //Ask for a bund relance everytime we finish an peration
            var task = _myMan.Trader.Service.RebalanceFunds();
            Console.WriteLine("Relancing funds...");

            Console.WriteLine("Terminando saida\n" + this.ToString());
        }

        private bool _stepSell = false;
        private bool _softCloseSent = false;

        public async Task OnCandleUpdateAsync(Candle newCandle)
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
                            if (firstStepToExecute != null)
                                currentSteps.Add(firstStepToExecute);
                        }
                        else
                        {
                            currentSteps = _stepMan.GetSteps(LastMaxGainRelative);
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

                            var convertedUnits = Math.Round(totalUnits / LastValue, 4);

                            suggestedPrice = LastValue;
                            if (suggestedPrice > 0) //Na hora de comprar sempre mandamos um pouco com limit e outro pouco com MarketOrder
                            {
                                var firstAmount = Math.Round(convertedUnits / 2, 4);
                                var secondAmount = convertedUnits - firstAmount;
                                //Enviar MarketOrder
                                await AddMarketOrderAsync(OrderDirection.BUY, firstAmount, TIMEOUT_BUY, suggestedPrice, LastValue, LastCloseDate);

                                //Add a limit order discounting 1% of the entry RATR
                                await AddLimitOrderAsync(OrderDirection.BUY, secondAmount, TIMEOUT_BUY, suggestedPrice * (1 - (_entryRAtr * 0.01)), LastValue, LastCloseDate);
                            }
                        }
                        catch (Exception err)
                        {
                            LogMessage("Order", "Error when buying - " + err.ToString());
                        }
                    }

                    var softStopEnabled = RunParameters.GetInstance().GetHyperParamAsBoolean("SoftStopEnabled");

                    var mustStopBySoftStop = false;
                    var gainSoftStopTrigger = RunParameters.GetInstance().GetHyperParamAsDouble("GainSoftStopTrigger");
                    if (LastMaxGainAbs >= gainSoftStopTrigger)
                    {
                        double chasePerc = RunParameters.GetInstance().GetHyperParamAsDouble("FollowPricePerc");

                        _softStopLossMarker = _priceEntry * (1 + ((LastMaxGainAbs * chasePerc) / 100));
                    }

                    mustStopBySoftStop = LastValue <= _softStopLossMarker && softStopEnabled;

                    var mustStopByLimitStopLoss = LastValue <= _limitStopLossMarker;
                    var mustStopByStopLoss = LastValue <= _stopLossMarker;

                    var stepSellEnabled = RunParameters.GetInstance().GetHyperParamAsBoolean("StepSellEnabled");
                    if (stepSellEnabled)
                    {
                        var stepSellValue = RunParameters.GetInstance().GetHyperParamAsDouble("StepSellValue");
                        if (LastMaxGainAbs > stepSellValue && !_stepSell)
                        {
                            _stepSell = true;
                            var amountLeftToSell = GetAmountLeftToSell();
                            var percentage = RunParameters.GetInstance().GetHyperParamAsDouble("StepSellPercentage");
                            Console.WriteLine("Partial sell!");
                            await AddMarketOrderAsync(OrderDirection.SELL, amountLeftToSell * percentage, TIMEOUT_BUY, LastValue, LastValue, LastCloseDate);
                        }
                    }

                    var mustStopByMovingAvg = ShouldStopByMovingAverage(RunParameters.GetInstance().GetHyperParamAsInt(_shortAsset, this.ModelName, "Avg"));

                    if (mustStopByLimitStopLoss && !mustStopByStopLoss)
                        await AskToSoftCloseOperation();

                    if (mustStopBySoftStop ||
                        mustStopByMovingAvg ||
                        mustStopByStopLoss)
                    {
                        await _myMan.OperationFinished(this, _lastCandle, _fundSlot);
                        await CloseOperation(true);

                        await Persist();
                    }

                    await ProcessOperationStatus();
                }
            }
            catch (Exception err)
            {
                LogMessage("CandleUpder", $"Candle update error - {err.ToString()}");
            }
        }

        private async Task<bool> ChangeState(TradeOperationState state)
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
                    await _myMan.SendMessage(null, "STATE FAILURE: " + this.ToString());

                if (state != TradeOperationState.Initial)
                    await Persist();
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
                compareValue = (PriceExitAverage == 0 ? (LastValue) : PriceExitAverage);
            else
                compareValue = closeValue;


            var gain = ((compareValue - PriceEntryAverage) / PriceEntryAverage) * 100;
            var qtyPurchased = GetAmountPurchased();
            var qtyInBSB = qtyPurchased * compareValue;
            var percPurchased = qtyInBSB / _fundSlot.SlotAmount;

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
                ModelName = ModelName,
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
                MaxGain = this.LastMaxGainAbs,
                LastValue = this.LastValue,
                Gain = this.GetGain(),
                Strengh = this.PurchaseStatusDescription,
                _id = this._myId,
                State = this.State
            };
            ret.Orders = this._orders.Select(o => o.GetMyDto()).ToList();

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
        public string ModelName { get => _modelName; set => _modelName = value; }

        public async Task Persist(bool withLogs = true)
        {
            try
            {
                var myDto = this.GetMyDto(withLogs);

                var database = _dbClient.GetDatabase("CandlesFaces");

                var dbCol = database.GetCollection<TradeOperationDto>("TradeOperations");

                var result = await dbCol.ReplaceOneAsync(
                    item => item._id == myDto._id,
                    myDto,
                    new ReplaceOptions { IsUpsert = true });
            }
            catch (InvalidOperationException)
            {
                TraceAndLog.StaticLog("Persist", "Oooppsss duas threads escrevendo na coleção ao mesmo tempo");
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

        public List<BrokerOrderDto> Orders
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
        public DateTime ForecastDate { get; set; }
        public double LastValue { get; set; }
        public double MaxValue { get; set; }
        public double SoftStopLossMarker { get; set; }
        public string Asset { get; set; }
        public CandleType CandleType { get; set; }
        public string Experiment { get; set; }
        public DateTime LastUpdate { get; set; }
        public string ModelName { get; set; }
        public double Amount { get; set; }
        public double Amount_USD { get; set; }
        public double EntrySpreadAverage { get; set; }
        public double ExitSpreadAverage { get; set; }
        public DateTime ExitDateUtc { get; set; }
        public DateTime EntryDateUtc { get; set; }
        public double RATR { get; set; }
        public string Strengh { get; internal set; }
        public double MaxGain { get; internal set; }

        public double GetGain(double closeValue)
        {
            return ((closeValue - PriceEntryReal) / PriceEntryReal) * 100;
        }
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
