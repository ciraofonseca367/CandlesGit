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

namespace Midas.Trading
{
    public class TradeOperation
    {
        private double STOP_LOSS = 0.99 / 100;

        private double MIN_TARGET_PROFIT = 0.7 / 100;

        private int TIMEOUT_BUY = 200000;
        private int TIMEOUT_SELL = 60000;

        private double WINDOW_SIZE_SECONDS_9 = 5 * 60 * 9;

        private double WINDOW_SIZE_SECONDS_12 = 5 * 60 * 12;

        private double TARGET1 = 0.9;

        private double TARGET2 = 0.9;

        private double TARGET3 = 0.9;

        private double _fund;
        private double _priceEntryDesired;
        private double _priceEntryReal;

        private double _priceExitReal;

        private double _stopLossMarker;

        private double _softStopLossMarker;

        private double _lastMaxValue;

        private TradeOperationState _state;

        private MovingStrenghIndex _msi;

        private Candle _lastCandle;
        private double _lowerBound;
        private double _upperBound;
        private DateTime _exitDate, _entryDate, _forecastDate;
        private DateTime _lastLongPrediction;

        private MongoClient _dbClient;

        private double _priceExitDesired;

        private BsonObjectId _myId;
        private string _myStrId;

        private Broker _broker;
        private TradeOperationManager _myMan;

        private double _currentWindowSize;

        private double _storedAverage;

        private double _stopLoss;

        public TradeOperation(TradeOperationManager man, string connectionString, dynamic config)
        {
            _myMan = man;
            _dbClient = new MongoClient(connectionString);

            _currentWindowSize = WINDOW_SIZE_SECONDS_9;

            _broker = Broker.GetBroker("Binance", config);

            STOP_LOSS = Convert.ToDouble(config.STOP_LOSS);
            MIN_TARGET_PROFIT = Convert.ToDouble(config.MIN_TARGET_PROFIT);
            TIMEOUT_BUY = Convert.ToInt32(config.TIMEOUT_BUY);
            TIMEOUT_SELL = Convert.ToInt32(config.TIMEOUT_SELL);

            _stopLoss = STOP_LOSS; //default value
        }

        public TradeOperation(TradeOperationManager man, double fund, double lowerBound, double upperBound, DateTime forecastPeriod, string connectionString, dynamic config) : this(man, connectionString, (JObject)config)
        {
            var objId = ObjectId.GenerateNewId(DateTime.Now);
            _myStrId = objId.ToString();
            _myId = new BsonObjectId(objId);

            _lastMaxValue = -1;

            _fund = fund;

            ChangeState(TradeOperationState.Initial);

            _exitDate = DateTime.MaxValue;
            _priceEntryReal = 0;
            _priceExitReal = 0;
            _forecastDate = forecastPeriod;

            int windowSize = Convert.ToInt32(config.MSI_WINDOW_SIZE_SECONDS);

            _msi = new MovingStrenghIndex(windowSize);

            this._lowerBound = lowerBound;
            this._upperBound = upperBound;
        }

        public TradeOperation(TradeOperationDto state, double fund, TradeOperationManager man, string connectionString, dynamic config) : this(man, connectionString, (JObject)config)
        {
            _fund = fund;
            _priceEntryReal = state.PriceEntryReal;
            _priceEntryDesired = state.PriceEntryDesired;
            _priceExitDesired = state.PriceExitDesired;
            _priceExitReal = state.PriceExitReal;
            _stopLossMarker = state.StopLossMarker;
            _softStopLossMarker = state.SoftStopLossMarker;
            _state = state.State;
            _lowerBound = state.LowerBound;
            _upperBound = state.UpperBound;
            _myId = (BsonObjectId)state._id;
            _myStrId = _myId.ToString();
            _storedAverage = state.Average;

            _lastMaxValue = -1;

            GetInitialStopLoss();

            _exitDate = state.ExitDate;
            _entryDate = state.EntryDate;
            _forecastDate = state.ForecastDate;
            _lastMaxValue = state.MaxValue;


            int windowSize = Convert.ToInt32(config.MSI_WINDOW_SIZE_SECONDS);

            _msi = new MovingStrenghIndex(windowSize);

            _msi.ResetState(state.LastValue, state.EntryDate);

            _lastCandle = new Candle()
            {
                PointInTime_Open = DateTime.Now,
                PointInTime_Close = DateTime.Now.AddMinutes(5),
                AmountValue = state.LastValue
            };

        }

        internal void Signal(TradeType trend)
        {
            if(trend == TradeType.Long && _lastCandle != null)
            {
                if(GetCurrentMaxGain() < 0.9)
                    _lastLongPrediction = _lastCandle.OpenTime;
            }
        }

        private void PersistRunner()
        {
            var taskUp = Task.Run(() =>
            {
                while (this.IsIn || this.State == TradeOperationState.Initial)
                {
                    this.Persist();
                    Thread.Sleep(1000);
                }
            });
        }

        public async Task<bool> EnterAsync(double desiredEntryPrice, DateTime point, double atr)
        {
            bool ret = false;
            if (_state == TradeOperationState.Initial)
            {
                var stopLossAtr = (atr / desiredEntryPrice);
                _stopLoss = stopLossAtr * 1.3;

                var changeLock = ChangeState(TradeOperationState.WaitingIn);
                if (changeLock)
                {
                    _priceEntryDesired = desiredEntryPrice;
                    _entryDate = point;
                    _lastLongPrediction = _entryDate;

                    var resBuy = await MarketBuyAsync(desiredEntryPrice, PriceBias.Urgent);
                    if (resBuy)
                    { //Buy and wait for order confirmation
                        ChangeState(TradeOperationState.In);
                        ret = true;

                        PersistRunner();

                        _lastMaxValue = _priceEntryReal;

                        TraceAndLog.StaticLog("TradeOperation", "StopLoss % em :"+_stopLoss);
                        _stopLossMarker = GetInitialStopLoss();
                        TraceAndLog.StaticLog("TradeOperation", "StopLoss em :"+_stopLossMarker);

                        if (_myMan != null)
                            _myMan.SendMessage(null, "Entrando FORTE! - " + this.ToString());
                    }
                    else
                    {
                        ChangeState(TradeOperationState.FailedIn);
                    }
                }
            }
            else
            {
                throw new ArgumentException("Trying to enter an operation in " + _state + " state.");
            }

            return ret;
        }

        public async Task<bool> ExitAsync(bool hardStopped)
        {
            bool ret = false;
            _priceExitDesired = _lastCandle.CloseValue;
            if (_state == TradeOperationState.In)
            {
                var changeLock = ChangeState(TradeOperationState.WaitingOut);
                if (changeLock)
                {
                    var resSell = await MarketSellAsync(_priceExitDesired, (hardStopped ? PriceBias.Urgent : PriceBias.Optmistic));
                    if (resSell)
                    {
                        ret = true;

                        if (_priceExitReal > _priceEntryReal)
                            ChangeState(TradeOperationState.Profit);
                        else
                            ChangeState(TradeOperationState.Stopped);

                        _exitDate = _lastCandle.CloseTime;


                        string prefix = _state == TradeOperationState.Profit ? "\U0001F4B0" : "\U0001F612";

                        if (_myMan != null)
                            _myMan.SendMessage(null, prefix + " - Concluindo operação - " + this.ToString());
                    }
                    else
                    {
                        _myMan.SendMessage(null, "CRITICAL ERROR: FailedOut");
                        ChangeState(TradeOperationState.FailedOut);
                    }
                }
            }

            return ret;
        }

        private double GetInitialStopLoss()
        {
            return _priceEntryReal * (1 - _stopLoss);
        }

        private bool _lastStrenghCheck = false;

        public bool IsForceActive
        {
            get
            {
                return _lastStrenghCheck;
            }
        }

        internal bool ShouldStopByStrengh()
        {
            bool shouldStop = false;
            bool mustStop = false;

            if (LastLongSignalDurationInPeriods >= 12) //Timeout da operação, desde o inicio ou último sinal de long
            {
                shouldStop = true;
                _currentWindowSize = WINDOW_SIZE_SECONDS_12;
            }
            
            _lastStrenghCheck = shouldStop;

            if (shouldStop && LastValue != -1 && LastValue < _msi.GetMovingAverage(_currentWindowSize) && _msi.IsStable())
                mustStop = true;

            return mustStop;
        }

        public double LastValue
        {
            get
            {
                return (_lastCandle == null ? -1 : _lastCandle.CloseValue);
            }
        }

        public double EntrySpread
        {
            get
            {
                return ((_priceEntryReal - _priceEntryDesired) / _priceEntryDesired) * 100;
            }
        }

        public double StoredAverage
        {
            get
            {
                return _storedAverage;
            }
        }

        public double ExitSpread
        {
            get
            {
                return ((_priceExitReal - _priceExitDesired) / _priceExitDesired) * 100;
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
                return Convert.ToInt32(LastLongSignalDuration.TotalMinutes / 5);
            }
        }        

        public int OperationDurationInPeriodos
        {
            get
            {
                return Convert.ToInt32(OperationDuration.TotalMinutes / 5);
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
        public DateTime EntryDate { get { return _entryDate; } }
        public double PriceEntry
        {
            get
            {
                return (_priceEntryReal == 0 ? _priceEntryDesired : _priceEntryReal);
            }
        }
        public double LowerBound { get { return _lowerBound; } }
        public double UpperBound { get { return _upperBound; } }
        public double StopLossMark { get { return _stopLossMarker; } }

        public double SoftStopLossMark { get { return _softStopLossMarker; } }

        public TradeOperationState State { get => _state; }

        public double GetAbsolutLowerBound()
        {
            return PriceEntry * (1 + _lowerBound);
        }

        public double GetAbsolutUpperBound()
        {
            return PriceEntry * (1 + _upperBound);
        }

        public double GetAbsoluteHalfwayBound()
        {
            return _priceEntryReal * (1 + (MIN_TARGET_PROFIT));
        }

        public double GetStrenghIndex()
        {
            if (_msi != null)
                return _msi.GetMovingAverage(_currentWindowSize);
            else
            {
                return 0;
            }
        }

        public bool IsExpired()
        {
            bool ret = false;
            if(_lastCandle != null)
                ret = _lastCandle.PointInTime_Open > _forecastDate;

            return ret;
        }

        public override string ToString()
        {
            return String.Format("Gain: {0:0.00}%\nEntry: ${1:0.00}\nCurrent: ${2:0.00}\nDuration:{3:0.00}\nLast Long:{4:0.00}\nEntry Spread: {5:0.00}%\nState: {6}\nExitSpread: {7:0.00}%",
                GetGain(),
                _priceEntryReal,
                (_lastCandle == null ? 0 : _lastCandle.CloseValue),
                this.OperationDuration.TotalMinutes,
                this.LastLongSignalDuration.TotalMinutes,
                EntrySpread,
                State,
                ExitSpread
                );
        }

        public async void OnCandleUpdateAsync(Candle newCandle)
        {
            _lastCandle = newCandle;

            if (_state == TradeOperationState.In)
            {
                _msi.AddTrade(newCandle.CloseValue);

                if (newCandle.CloseValue > _lastMaxValue)
                {
                    // var ratioToMax = (newCandle.CloseValue - _lastMaxValue) / _lastMaxValue;
                    // _stopLossMarker = _stopLossMarker * (1 + ratioToMax);

                    _lastMaxValue = newCandle.CloseValue;
                }

                // if ((_lastMaxValue != -1 && _priceEntryReal > 0) && _lastMaxValue > GetAbsoluteHalfwayBound())
                //     _stopLossMarker = _priceEntryReal * (1 + (_lowerBound * 0.8));

                var mustStop = ShouldStopByStrengh();
                if (newCandle.CloseValue <= _stopLossMarker || newCandle.CloseValue < _softStopLossMarker || mustStop)
                {
                    bool hardStop = true;
                    if (mustStop || newCandle.CloseValue < _softStopLossMarker)
                    {
                        hardStop = false;
                    }

                    await ExitAsync(hardStop);
                }
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
                if (_myMan != null)
                    _myMan.SendMessage(null, "Change State: " + this.ToString());

                if (state != TradeOperationState.Initial)
                    Persist();
            }

            return ret;
        }

        private async Task<bool> MarketBuyAsync(double price, PriceBias bias)
        {
            bool ret = false;

            try
            {
                var order = await _broker.SmartOrderAsync(_myStrId + "b", "BTCUSDT", OrderDirection.BUY, _fund, TIMEOUT_BUY, price, bias);
                if (!order.InError)
                {
                    _priceEntryReal = order.AverageValue;
                    TraceAndLog.StaticLog("TradeOperation","Comprei! - " + order.BrokerOrderId);
                    ret = true;
                }
                else
                {
                    throw new Exception("Buy error - " + order.ErrorMsg);
                }
            }
            catch (Exception err)
            {
                TraceAndLog.StaticLog("TradeOperation",$"Error when buying, the current state will be maintained for subsequent attempts... BuyTimeOut:{TIMEOUT_BUY}" + err.ToString());
            }

            return ret;
        }

        private async Task<bool> MarketSellAsync(double price, PriceBias bias)
        {
            bool ret = false;

            try
            {
                var order = await _broker.SmartOrderAsync(_myStrId + "s", "BTCUSDT", OrderDirection.SELL, _fund, TIMEOUT_SELL, price, bias);
                if (!order.InError)
                {
                    ret = true;
                    TraceAndLog.StaticLog("TradeOperation","Vendi! - " + order.BrokerOrderId);
                    _priceExitReal = order.AverageValue;
                }
            }
            catch (Exception err)
            {
                TraceAndLog.StaticLog("TradeOperation","Error when selling, the current state will be maintained for subsequent attempts... " + err.Message);
            }

            return ret;
        }

        public double GetGain()
        {
            return GetGain(0);
        }

        public double GetDesiredStopLoss()
        {
            return _priceEntryDesired * (1 - STOP_LOSS);
        }

        public double GetGain(double closeValue)
        {
            double compareValue;
            if (closeValue == 0)
            {
                compareValue = (_priceExitReal == 0 ?
                    (_lastCandle == null ? 0 : _lastCandle.CloseValue) :
                    _priceExitReal);
            }
            else
            {
                compareValue = closeValue;
            }

            return ((compareValue - _priceEntryReal) / _priceEntryReal) * 100;
        }


        public double GetCurrentMaxGain()
        {
            return ((_lastMaxValue - _priceEntryReal) / _priceEntryReal) * 100;
        }

        public double GetStrengh()
        {
            _storedAverage = _msi.GetValue(_currentWindowSize);
            return _storedAverage;
        }

        public string Id
        {
            get
            {
                return _myStrId;
            }
        }

        private TradeOperationDto GetMyDto()
        {
            return new TradeOperationDto()
            {
                ExitDate = this.ExitDate,
                EntryDate = this.EntryDate,
                ForecastDate = this._forecastDate,
                Average = this.GetStrengh(),
                LowerBound = this.LowerBound,
                UpperBound = this.UpperBound,
                AbsoluteLowerBound = this.GetAbsolutLowerBound(),
                AbsoluteUpperBound = this.GetAbsolutUpperBound(),
                StopLossMarker = _stopLossMarker,
                SoftStopLossMarker = _softStopLossMarker,
                PriceEntryDesired = this._priceEntryDesired,
                PriceEntryReal = this._priceEntryReal,
                PriceExitDesired = this._priceExitDesired,
                PriceExitReal = this._priceExitReal,
                MaxValue = this._lastMaxValue,
                LastValue = this.LastValue,
                Gain = this.GetGain(),
                _id = this._myId,
                State = this.State
            };
        }

        public bool IsIn
        {
            get
            {
                return _state == TradeOperationState.In || _state == TradeOperationState.WaitingIn || _state == TradeOperationState.WaitingOut;
            }
        }

        public bool IsCompleted
        {
            get
            {
                return !IsIn;
            }
        }

        public void Persist()
        {
            try
            {
                var myDto = this.GetMyDto();

                var database = _dbClient.GetDatabase("CandlesFaces");

                var dbCol = database.GetCollection<TradeOperationDto>("TradeOperations");

                var result = dbCol.ReplaceOneAsync(
                    item => item._id == myDto._id,
                    myDto,
                    new ReplaceOptions { IsUpsert = true });
            }
            catch (Exception err)
            {
                TraceAndLog.StaticLog("Persist", err.Message);
            }
        }

    }


    public class TradeOperationDto
    {
        public object _id
        {
            get;
            set;
        }

        public DateTime ExitDate { get; set; }

        public DateTime EntryDate { get; set; }

        public double LowerBound { get; set; }
        public double UpperBound { get; set; }

        public double AbsoluteUpperBound { get; set; }
        public double AbsoluteLowerBound { get; set; }

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
        public double Average { get; internal set; }
        public double SoftStopLossMarker { get; internal set; }
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

        FailedOut
    }
}
