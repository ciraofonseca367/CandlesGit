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

namespace Midas.Trading
{
    public class TradeOperation : ILogger
    {
        private double STOP_LOSS = 0.99 / 100;

        private double MIN_TARGET_PROFIT = 0.7 / 100;

        private int TIMEOUT_BUY = 200000;
        private int TIMEOUT_SELL = 60000;

        private double _fund;
        private double _priceEntryDesired;
        private double _priceEntryReal;

        private double _priceExitReal;

        private double _stopLossMarker;
        private double _softStopLossMarker;
        private DateTime _softStopTime;

        private double _lastMaxValue;

        private TradeOperationState _state;

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

        private string _asset;
        private CandleType _candleType;

        private DateTime _lastUpdate;

        private double _lastMA;

        private List<TraceEntry> _logs;

        private double _firstStopLossRate;

        public TradeOperation(TradeOperationManager man, string connectionString, string brokerType, dynamic config)
        {
            _myMan = man;
            _dbClient = new MongoClient(connectionString);

            _broker = Broker.GetBroker(brokerType, config, this);

            STOP_LOSS = Convert.ToDouble(config.STOP_LOSS);
            MIN_TARGET_PROFIT = Convert.ToDouble(config.MIN_TARGET_PROFIT);
            TIMEOUT_BUY = Convert.ToInt32(config.TIMEOUT_BUY);
            TIMEOUT_SELL = Convert.ToInt32(config.TIMEOUT_SELL);

            _softStopTime = DateTime.MinValue;

            _logs = new List<TraceEntry>();
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

        public TradeOperation(TradeOperationManager man, double fund, double lowerBound, double upperBound, DateTime forecastPeriod, string connectionString, dynamic config,
        string asset, CandleType candleType, string brokerType) : this(man, connectionString, brokerType, (JObject)config)
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

            _asset = asset;
            _candleType = candleType;

            int windowSize = Convert.ToInt32(config.MSI_WINDOW_SIZE_SECONDS);

            this._lowerBound = lowerBound;
            this._upperBound = upperBound;
        }

        public TradeOperation(TradeOperationDto state, double fund, TradeOperationManager man, string connectionString, string brokerType, dynamic config) : this(man, connectionString, brokerType, (JObject)config)
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

            _lastMaxValue = -1;

            GetInitialStopLoss(_priceEntryReal, 0.5);

            _exitDate = state.ExitDate;
            _entryDate = state.EntryDate;
            _forecastDate = state.ForecastDate;
            _lastMaxValue = state.MaxValue;

            int windowSize = Convert.ToInt32(config.MSI_WINDOW_SIZE_SECONDS);

            _lastCandle = new Candle()
            {
                PointInTime_Open = DateTime.Now,
                PointInTime_Close = DateTime.Now.AddMinutes(5),
                AmountValue = state.LastValue
            };
        }

        internal void Signal(TradeType trend)
        {
            if (trend == TradeType.Long && _lastCandle != null)
            {
                if (GetCurrentMaxGain() < 0.4)
                {
                    _lastLongPrediction = _lastCandle.OpenTime;
                    if (LastValue < _priceEntryReal)
                        _stopLossMarker = GetInitialStopLoss(_lastCandle.AmountValue, _firstStopLossRate);
                }
            }
        }

        private void PersistRunner()
        {
            var taskUp = Task.Run(() =>
            {
                while (this.IsIn || this.State == TradeOperationState.Initial)
                {
                    this.Persist();
                    Thread.Sleep(10000);

                    if ((DateTime.UtcNow - _lastUpdate).TotalSeconds > 30)
                    {
                        Console.WriteLine("Leaving operation by timeout");
                        var t = this.CloseOperation();
                        t.Wait(1000);
                    }
                }
            });
        }

        public async Task<bool> EnterAsync(double desiredEntryPrice, DateTime point, double atr)
        {
            bool ret = false;
            if (_state == TradeOperationState.Initial)
            {
                var stopLossAtr = (atr / desiredEntryPrice);
                var stopLoss = stopLossAtr * _myMan.Trader.AssetParams.AtrStopLoss;
                _firstStopLossRate = stopLoss;

                _entryDate = point;
                _priceEntryDesired = desiredEntryPrice;
                _lastLongPrediction = _entryDate;

                var changeLock = ChangeState(TradeOperationState.WaitingIn);
                if (changeLock)
                {
                    PriceBias bias = PriceBias.Urgent;
                    // switch (_myMan.GetPriceDirection())
                    // {
                    //     case PriceDirection.GoingDown:
                    //         bias = PriceBias.Normal;
                    //         break;
                    //     case PriceDirection.SomeWhatSteady:
                    //         bias = PriceBias.Normal;
                    //         break;
                    //     case PriceDirection.GoingUp:
                    //         bias = PriceBias.Urgent;
                    //         break;
                    // }

                    Console.WriteLine("Feeling: " + bias.ToString());

                    _stopLossMarker = GetInitialStopLoss(desiredEntryPrice, stopLoss);

                    var resBuy = await MarketBuyAsync(desiredEntryPrice, bias);
                    if (resBuy)
                    { //Buy and wait for order confirmation
                        ChangeState(TradeOperationState.In);
                        ret = true;

                        PersistRunner();

                        _lastMaxValue = _priceEntryReal;

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

        public async Task<bool> CloseOperation(bool hardStopped = true)
        {
            bool ret = false;
            _priceExitDesired = _lastCandle.CloseValue;
            if (_state == TradeOperationState.In)
            {
                var changeLock = ChangeState(TradeOperationState.WaitingOut);
                if (changeLock)
                {
                    var resSell = await MarketSellAsync(_priceExitDesired, (hardStopped ? PriceBias.Urgent : PriceBias.Normal));
                    if (resSell)
                    {
                        ret = true;

                        _exitDate = _lastCandle.CloseTime;

                        if (_priceExitReal > _priceEntryReal)
                            ChangeState(TradeOperationState.Profit);
                        else
                            ChangeState(TradeOperationState.Stopped);

                        _myMan.OperationFinished(_lastCandle);

                        TraceAndLog.StaticLog("TradeOperation", $"\nOperation: {this.ToString()}\n");

                        string prefix = _state == TradeOperationState.Profit ? TelegramEmojis.MoneyBag : TelegramEmojis.MeanSmirking;

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
        public async Task<bool> ExitAsync(bool hardStopped)
        {
            return await CloseOperation(hardStopped);
        }

        private double GetInitialStopLoss(double price, double stopLoss)
        {
            return price * (1 - stopLoss);
        }

        public double LastMaxGain
        {
            get
            {
                return ((_lastMaxValue - _priceEntryReal) / _lastMaxValue) * 100;
            }
        }

        internal bool ShouldStopByMovingAverage()
        {
            bool shouldStop = false;
            bool mustStop = false;

            // if(LastMaxGain >= 1 && OperationDurationInPeriods >= 5) //Para quando o aumento é alto logo no inicio
            // {
            //     maIndicator = _myMan.Trader.Indicators.Where(i => i.Name == "MA6").FirstOrDefault();
            //     if(LastValue > _priceEntryReal)
            //         shouldStop = true;                
            // }

            var ma12 = _myMan.Trader.GetMAValue("MA12");
            if(_lastMaxValue > ma12 * (1 + (0.2/100)) &&
                LastValue > _priceEntryReal
            )
                shouldStop = true;


            // if (LastLongSignalDurationInPeriods >= 12) //Timeout da operação, desde o inicio ou último sinal de long
            // {
            //     if (LastValue > _priceEntryReal)
            //         shouldStop = true;
            // }

            if (shouldStop)
            {
                var ma = ma12;
                _lastMA = ma;

                if (shouldStop && LastValue != -1 && _myMan.TradeLogger.SoftCompare(ma, LastValue, _myMan.Trader.AssetParams.AvgCompSoftness) == CompareType.LessThan)
                    mustStop = true;
            }

            //Tentativa de código para um StopLoss inteligente que até agora não deu certo
            // if (LastLongSignalDurationInPeriods >= 6 && LastMaxGain < 0.1)
            // {
            //     var maValue = _myMan.Trader.GetMAValue("MA6");

            //     var diff = ((_priceEntryReal - maValue) / _priceEntryReal) * 100;

            //     if (diff > 0.5)
            //     {
            //         if (maValue < _priceEntryReal)
            //             mustStop = true;
            //     }
            // }


            return mustStop;
        }

        public double LastValue
        {
            get
            {
                return (_lastCandle == null ? 0 : _lastCandle.CloseValue);
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
                return _lastMA;
            }
        }

        public double ExitValue
        {
            get
            {
                return _priceExitReal;
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

        public bool IsExpired()
        {
            bool ret = false;
            if (_lastCandle != null)
                ret = _lastCandle.PointInTime_Open > _forecastDate;

            return ret;
        }

        public override string ToString()
        {
            string data = $"Asset:{_asset}:{_candleType.ToString()} - Gain: {GetGain().ToString("0.000")}%\nEntry: {this.PriceEntry.ToString("0.00")}\n";
            data += $"Duration: {this.OperationDuration.TotalMinutes}\nLast Long:{this.LastLongSignalDuration.TotalMinutes}\nEntry Spread: {EntrySpread.ToString("0.00")}%\n";
            data += $"State: {State}\nExit Spread: {ExitSpread.ToString("0.00")}%\n";
            data += $"EntryDate: {EntryDate.ToString("yyyy-MM-dd HH:mm")}\n";

            return data;
        }

        public async void OnCandleUpdateAsync(Candle newCandle)
        {
            _lastCandle = newCandle;
            _lastUpdate = DateTime.UtcNow;

            if (_state == TradeOperationState.In)
            {
                if (newCandle.CloseValue > _lastMaxValue)
                {
                    // var ratioToMax = (newCandle.CloseValue - _lastMaxValue) / _lastMaxValue;
                    // _stopLossMarker = _stopLossMarker * (1 + ratioToMax);

                    _lastMaxValue = newCandle.CloseValue;
                }

                if (LastMaxGain > _myMan.Trader.AssetParams.GainSoftStopTrigger)
                {
                    if (_softStopTime == DateTime.MinValue)
                        _softStopTime = newCandle.OpenTime;

                    _softStopLossMarker = _priceEntryReal * (1 + ((LastMaxGain * _myMan.Trader.AssetParams.FollowPricePerc) / 100));
                }

                // if ((_lastMaxValue != -1 && _priceEntryReal > 0) && _lastMaxValue > GetAbsoluteHalfwayBound())
                //     _stopLossMarker = _priceEntryReal * (1 + (_lowerBound * 0.8));

                var mustStopByAvg = ShouldStopByMovingAverage();

                var mustStopBySoftStop = (//Trigger condition for the SoftStopLoss
                            newCandle.CloseValue < _softStopLossMarker &&
                            (newCandle.OpenTime - _softStopTime).TotalMinutes >= Convert.ToInt32(_candleType) //We trigger the SoftStop only after the candletype size in minutes, that the condition was triggered
                        );

                if (mustStopBySoftStop ||
                    _myMan.TradeLogger.SoftCompare(_stopLossMarker, newCandle.CloseValue, _myMan.Trader.AssetParams.StopLossCompSoftness) == CompareType.LessThan || //StopLoss
                    mustStopByAvg)
                {
                    bool hardStop = true;
                    // if (mustStopByAvg)
                    // {
                    //      hardStop = false;
                    // }

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
                if (_myMan != null && IsFailed)
                    _myMan.SendMessage(null, "STATE FAILURE: " + this.ToString());

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
                var order = await _broker.SmartOrderAsync(_myStrId + "b", _asset, OrderDirection.BUY, _fund, TIMEOUT_BUY, price, bias);
                if (!order.InError)
                {
                    _priceEntryReal = order.AverageValue;
                    ret = true;
                }
                else
                {
                    throw new Exception("Buy error - " + order.ErrorMsg);
                }
            }
            catch (Exception err)
            {
                TraceAndLog.StaticLog("TradeOperation", $"Error when buying, the current state will be maintained for subsequent attempts... BuyTimeOut:{TIMEOUT_BUY}" + err.ToString());
            }

            return ret;
        }

        private async Task<bool> MarketSellAsync(double price, PriceBias bias)
        {
            bool ret = false;

            try
            {
                var order = await _broker.SmartOrderAsync(_myStrId + "s", _asset, OrderDirection.SELL, _fund, TIMEOUT_SELL, price, bias);
                if (!order.InError)
                {
                    ret = true;
                    _priceExitReal = order.AverageValue;
                }
            }
            catch (Exception err)
            {
                TraceAndLog.StaticLog("TradeOperation", "Error when selling, the current state will be maintained for subsequent attempts... " + err.Message);
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
                compareValue = (_priceExitReal == 0 ? (LastValue) : _priceExitReal);
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
            return _lastMA;
        }

        public string Id
        {
            get
            {
                return _myStrId;
            }
        }

        private TradeOperationDto GetMyDto(bool withLogs)
        {
            var ret = new TradeOperationDto()
            {
                Asset = _asset,
                Experiment = _myMan.Experiment,
                LastUpdate = _lastUpdate,
                CandleType = _candleType,
                ExitDate = this.ExitDate.ToUniversalTime(),
                EntryDate = this.EntryDate.ToUniversalTime(),
                ForecastDate = this._forecastDate.ToUniversalTime(),
                LowerBound = this.LowerBound,
                UpperBound = this.UpperBound,
                AbsoluteLowerBound = this.GetAbsolutLowerBound(),
                AbsoluteUpperBound = this.GetAbsolutUpperBound(),
                StopLossMarker = this._stopLossMarker,
                SoftStopLossMarker = this._softStopLossMarker,
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
                TraceAndLog.StaticLog("Persist", err.Message);
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

        public DateTime ExitDate { get; set; }

        public DateTime EntryDate { get; set; }

        public double LowerBound { get; set; }
        public double UpperBound { get; set; }

        public double AbsoluteUpperBound { get; set; }
        public double AbsoluteLowerBound { get; set; }

        public double StopLossMarker { get; set; }

        public double Average { get; set; }

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
