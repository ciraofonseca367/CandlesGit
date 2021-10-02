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

namespace Midas.Trading
{
    public class TradeOperation : ILogger
    {
        private int TIMEOUT_BUY = 200000;
        private int TIMEOUT_SELL = 60000;

        private double _fund;

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

        private List<BrokerOrder> _orders;

        private PurchaseStepManager _stepMan;

        public TradeOperation(TradeOperationManager man, string connectionString, string brokerType, dynamic config)
        {
            _myMan = man;
            _dbClient = new MongoClient(connectionString);

            _broker = Broker.GetBroker(brokerType, config, this);

            _entryDateInUTC = DateTime.MinValue;

            TIMEOUT_BUY = Convert.ToInt32(config.TIMEOUT_BUY);
            TIMEOUT_SELL = Convert.ToInt32(config.TIMEOUT_SELL);

            _softStopTime = DateTime.MinValue;

            _logs = new List<TraceEntry>();
            _orders = new List<BrokerOrder>();
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

        public TradeOperation(TradeOperationManager man, double fund, DateTime forecastPeriod, string connectionString, dynamic config,
        string asset, CandleType candleType, string brokerType) : this(man, connectionString, brokerType, (JObject)config)
        {
            var objId = ObjectId.GenerateNewId(DateTime.Now);
            _myStrId = objId.ToString();
            _myId = new BsonObjectId(objId);

            _lastMaxValue = -1;

            _fund = fund;

            _stepMan = new PurchaseStepManager(_fund, config.PurchaseSteps);
            ChangeState(TradeOperationState.Initial);

            _exitDate = DateTime.MaxValue;
            _forecastDate = forecastPeriod;

            _asset = asset;
            _candleType = candleType;

            int windowSize = Convert.ToInt32(config.MSI_WINDOW_SIZE_SECONDS);
        }

        public TradeOperation(TradeOperationDto state, double fund, TradeOperationManager man, string connectionString, string brokerType, dynamic config) : this(man, connectionString, brokerType, (JObject)config)
        {
            _fund = fund;
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
            if ((trend == TrendType.LONG || trend == TrendType.DOUBLE_LONG) && _lastCandle != null)
            {
                if (GetCurrentMaxGain() <= 0.5)
                {
                    _lastLongPrediction = _lastCandle.OpenTime;
                    if (LastValue < PriceEntryAverage)
                        _stopLossMarker = GetStopLoss(_lastCandle.AmountValue, _firstStopLossRate);
                }
            }
        }

        public double PriceEntryAverage
        {
            get
            {
                var buyOrders = _orders.Where(o => o.Direction == OrderDirection.BUY && o.Status == BrokerOrderStatus.FILLED);
                return buyOrders.Sum(o => o.Quantity * o.AverageValue) / buyOrders.Sum(o => o.Quantity);
            }
        }

        public double PriceExitAverage
        {
            get
            {
                var sellOrders = _orders.Where(o => o.Direction == OrderDirection.SELL && o.Status == BrokerOrderStatus.FILLED);
                if (sellOrders.Count() > 0)
                    return sellOrders.Sum(o => o.Quantity * o.AverageValue) / sellOrders.Sum(o => o.Quantity);
                else
                {
                    return 0;
                }
            }
        }

        private double _entryRAtr;
        private DateTime _entryDateInUTC;

        public void Enter(double price, DateTime point, double atr, string modelName = "")
        {
            _modelName = modelName;
            if (_state == TradeOperationState.Initial)
            {
                var stopLossAtr = (atr / price);
                var stopLoss = stopLossAtr * _myMan.Trader.AssetParams.AtrStopLoss;
                _entryRAtr = stopLoss;
                _firstStopLossRate = stopLoss;

                _entryDate = point;
                _entryDateInUTC = DateTime.UtcNow;
                _priceEntry = price;
                _lastMaxValue = _priceEntry;
                _lastLongPrediction = _entryDate;

                _stopLossMarker = GetStopLoss(price, stopLoss);

                ChangeState(TradeOperationState.In);
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

        public void CloseOperation()
        {
            var t = AskToCloseOperation(true);
            if (t.Wait(60000))
            {
                var statusTask = CheckOperationStatus();
                if (!statusTask.Wait(60000))
                    throw new TimeoutException("Timeout waiting TradeRunner to close OP");
            }
            else
            {
                throw new TimeoutException("Error waiting AskToCloseOperation");
            }
        }

        private async Task AskToCloseOperation(bool hardStopped = true)
        {
            if (_state == TradeOperationState.In)
            {
                var amountPurchased = GetAmountPurchased();
                if (amountPurchased > 0)
                {
                    var sellOrderTask = _broker.MarketOrderAsync(_myStrId + "S1", _asset, OrderDirection.SELL, amountPurchased, TIMEOUT_SELL, LastValue, LastCloseDate, false);
                    if (!sellOrderTask.Wait(1000))
                        await sellOrderTask;

                    if (!sellOrderTask.Result.InError)
                    {
                        var order = sellOrderTask.Result;
                        _orders.Add(order);

                        _exitDate = _lastCandle.CloseTime;
                        _exitDateInUtc = DateTime.UtcNow;
                    }
                    else
                    {
                        LogMessage("Order", "Error when buying - " + sellOrderTask.Result.ErrorMsg);
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
                    return ((_lastMaxValue - _priceEntry) / _lastMaxValue) * 100;
                else
                    return 0;
            }
        }

        internal bool ShouldStopByMovingAverage()
        {
            bool shouldStop = false;
            bool mustStop = false;

            var ma6 = _myMan.Trader.GetMAValue("MA6");
            if (LastLongSignalDurationInPeriods >= 6) //Timeout da operação, desde o inicio ou último sinal de long
            {
                if (LastValue > PriceEntryAverage)
                    shouldStop = true;
            }

            if (shouldStop)
            {
                var ma = ma6;
                _lastMA = ma;

                if (shouldStop && LastValue != -1 && _myMan.TradeLogger.SoftCompare(ma, LastValue, _myMan.Trader.AssetParams.AvgCompSoftness) == CompareType.LessThan)
                    mustStop = true;
            }

            return mustStop;
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
                    .Average(o => ((o.AverageValue - o.DesiredPrice) / o.DesiredPrice) * 100);

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
                    .Where(o => o.Direction == OrderDirection.SELL && o.Status == BrokerOrderStatus.FILLED);

                    double avgExitSpread = 0;
                    if (sellOrders.Count() > 0)
                    {
                        avgExitSpread = sellOrders
                        .Average(o => ((o.AverageValue - o.DesiredPrice) / o.DesiredPrice) * 100);
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
            string data = $"Asset:{_asset}:{_candleType.ToString()} - Gain: {GetGain().ToString("0.000")}%\nEntry: {this.PriceEntryAverage.ToString("0.00")}\n";
            data += $"Duration: {TimeSpanPlus.ToString(this.OperationDurationUTC)}\nLast Long:{TimeSpanPlus.ToString(LastLongSignalDuration)}\nEntry Spread: {EntrySpreadAverage.ToString("0.00")}%\n";
            data += $"State: {State}\nExit Spread: {this.ExitSpreadAverage.ToString("0.00")}%\n";
            data += $"EntryDate: {EntryDateInUTC.ToLocalTime().ToString("yyyy-MM-dd HH:mm")}\n";
            data += $"Strengh: {GetRelativeAmountPurchased():0.0}/100.0";

            return data;
        }

        public double GetAmountPurchased()
        {
            var amountPurchased = _orders
            .Where(o => o.Direction == OrderDirection.BUY && o.Status == BrokerOrderStatus.FILLED)
            .Sum(o => o.Quantity);

            return amountPurchased;
        }

        public double GetRelativeAmountPurchased()
        {
            var amountPurchased = _orders
            .Where(o => o.Direction == OrderDirection.BUY && o.Status == BrokerOrderStatus.FILLED)
            .Sum(o => o.Quantity);

            return (amountPurchased / _fund) * 100;
        }

        private void TradeRunner()
        {
            var taskUp = Task.Run(() =>
            {
                try
                {
                    while (this.IsIn || this.State == TradeOperationState.Initial)
                    {
                        var taskStatus = CheckOperationStatus();
                        if (!taskStatus.Wait(60000))
                            throw new ApplicationException("Timeout waiting for CheckOperationStatus");

                        this.Persist();

                        Thread.Sleep(1000);
                    }

                    Console.WriteLine("Trade runner leaving...");
                }
                catch (Exception err)
                {
                    LogMessage("TradeRunner", err.Message);
                }
            });
        }

        private bool FailedEntryCheck()
        {
            bool ret = false;
            var pendingPurchaseOrders = _orders.Where(o => o.Direction == OrderDirection.BUY && o.Status == BrokerOrderStatus.NEW);

            if (pendingPurchaseOrders.Count() > 0)
            {
                var ordersInTimeout = pendingPurchaseOrders.Where(p => p.WaitDuration(LastCloseDate).TotalMilliseconds > TIMEOUT_BUY);
                if (ordersInTimeout.Count() > 1)
                    ret = true;
            }

            return ret;
        }

        private bool FailedExitCheck()
        {
            bool ret = false;
            var pendingSellOrder = _orders.Where(o => o.Direction == OrderDirection.SELL && o.Status == BrokerOrderStatus.NEW).FirstOrDefault();

            if (pendingSellOrder != null)
            {
                if (pendingSellOrder.WaitDuration(LastCloseDate).TotalMilliseconds > TIMEOUT_SELL)
                    ret = true;
            }

            return ret;
        }

        private bool OperationClosedCheck()
        {
            bool ret = false;

            var quantityBought = _orders
            .Where(o => o.Direction == OrderDirection.BUY)
            .Sum(o => o.Quantity);

            var quantitySold = _orders
            .Where(o => o.Direction == OrderDirection.SELL)
            .Sum(o => o.Quantity);


            if (quantityBought > 0 && quantitySold > 0)
                ret = true;


            return ret;
        }

        private async Task CheckOperationStatus()
        {
            _orders.ForEach(o =>
            {
                if (o.Status == BrokerOrderStatus.None)
                {
                    try
                    {
                        var order = _broker.OrderStatus(o.OrderId, _asset, TIMEOUT_BUY);
                        if (!order.InError)
                        {
                            o.Status = order.Status;
                            o.RawStatus = order.RawStatus;

                            Console.WriteLine($"Status orderId:{order.BrokerOrderId} - {order.RawStatus} - {order.Quantity} - {o.AverageValue}");
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
            });

            var failed = FailedEntryCheck();
            if (failed)
            {
                ChangeState(TradeOperationState.FailedIn);
                await AskToCloseOperation(true);
            }

            if (OperationClosedCheck())
            {
                lock (this)
                {
                    if (this.IsIn)
                    {
                        _myMan.OperationFinished(this, _lastCandle);

                        if (PriceExitAverage >= PriceEntryAverage)
                            ChangeState(TradeOperationState.Profit);
                        else
                            ChangeState(TradeOperationState.Stopped);

                        string prefix = _state == TradeOperationState.Profit ? TelegramEmojis.MoneyBag : TelegramEmojis.MeanSmirking;

                        Console.WriteLine(this.ToString());
                        if (_myMan != null)
                            _myMan.SendMessage(this._asset, prefix + " - Concluindo operação - " + this.ToString());
                    }
                }
            }

            if (FailedExitCheck())
            {
                ChangeState(TradeOperationState.FailedOut);
            }
        }

        public async void OnCandleUpdateAsync(Candle newCandle)
        {
            _lastCandle = newCandle;
            _lastUpdate = DateTime.UtcNow;

            BrokerOrder order = null;

            try
            {

                if (_state == TradeOperationState.In)
                {
                    if (newCandle.CloseValue > _lastMaxValue)
                        _lastMaxValue = newCandle.CloseValue;

                    PurchaseStep currentStep = null;
                    if (_orders.Count == 0)
                        currentStep = _stepMan.GetFirstStep();
                    else
                        currentStep = _stepMan.GetStep(LastMaxGain);

                    if (currentStep != null)
                    {
                        currentStep.SetUsed();
                        try
                        {
                            var orderTask = _broker.MarketOrderAsync(_myStrId + $"B{currentStep.Number}", _asset, OrderDirection.BUY, currentStep.TotalUnits, TIMEOUT_BUY, LastValue, LastCloseDate, false);
                            if (!orderTask.Wait(500)) //If the the market order is fast enough we won't need to spin another thread
                                await orderTask;

                            if (!orderTask.Result.InError)
                            {
                                order = orderTask.Result;
                                _orders.Add(order);
                            }
                            else
                            {
                                LogMessage("Order", "Error when buying - " + orderTask.Result.ErrorMsg);
                            }
                        }
                        catch (Exception err)
                        {
                            LogMessage("Order", "Error when buying - " + err.Message);
                        }
                    }

                    if (LastMaxGain > _myMan.Trader.AssetParams.GainSoftStopTrigger)
                    {
                        double chasePerc = _myMan.Trader.AssetParams.FollowPricePerc;

                        _softStopLossMarker = _priceEntry * (1 + ((LastMaxGain * chasePerc) / 100));
                    }

                    var mustStopByAvg = ShouldStopByMovingAverage();

                    var mustStopBySoftStop = LastValue < _softStopLossMarker;

                    if (mustStopBySoftStop ||
                        _myMan.TradeLogger.SoftCompare(_stopLossMarker, LastValue, _myMan.Trader.AssetParams.StopLossCompSoftness) == CompareType.LessThan || //StopLoss
                        mustStopByAvg)
                    {
                        await AskToCloseOperation(true);
                    }

                    await CheckOperationStatus();
                }
            }
            catch (Exception err)
            {
                LogMessage("CandleUpder", $"Candle update error - {err.Message}");
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
            var percPurchased = GetAmountPurchased() / _fund;

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
                Amount = _fund,
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
