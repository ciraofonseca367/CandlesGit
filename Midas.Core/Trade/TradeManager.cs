using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using Midas.Core.Broker;
using Midas.Core.Common;
using Midas.Core.Trade;
using Midas.Core.Util;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Midas.Trading
{
    public delegate void NewCandleAction(Candle c);

    public delegate void RunnerAction(Candle c, TradeOperationManager manager, string activity, Bitmap img);

    public class TradeOperationManager
    {
        private string _conString;
        private Broker _broker;
        private System.Timers.Timer _fundRefresher;
        private double _fund;
        private dynamic _brokerConfig;

        private string _fundAccountName;

        private KlineRunner _runner;

        private DateTime _lastAttempt;

        private static DateTime ANGEL_BIRTH = new DateTime(2021, 02, 24, 17, 0, 0);

        private static TradeOperationManager _singleManager = null;
        public static TradeOperationManager GetManager(string conString, string fundAccountName, dynamic brokerConfig, bool broadCastMode = false)
        {
            if (_singleManager == null)
                _singleManager = new TradeOperationManager(conString, fundAccountName, brokerConfig, broadCastMode);

            return _singleManager;
        }

        public TradeOperationManager(string conString, string fundAccountName, dynamic brokerConfig, bool broadCastMode = false)
        {
            _currentOperation = null;
            _allOperations = new List<TradeOperation>();
            _conString = conString;

            _broker = Broker.GetBroker("Binance", brokerConfig);

            _lastAttempt = ANGEL_BIRTH;

            GetFunds(fundAccountName);

            _fundRefresher = new System.Timers.Timer(60 * 1000);
            _fundRefresher.Elapsed += OnTimedEvent;
            _fundRefresher.AutoReset = true;
            _fundRefresher.Enabled = true;

            _fundAccountName = fundAccountName;
            _brokerConfig = brokerConfig;
        }

        public void RestoreState(bool isTesting)
        {
            List<TradeOperationDto> ops = null;
            if (!isTesting)
                ops = GetOpenOperations();
            else
            {
                ops = new List<TradeOperationDto>();
                DateTime forecastDate = DateTime.UtcNow.AddMinutes(5 * 5);
                double testLastValue = 30000;

                var objId = ObjectId.GenerateNewId(DateTime.Now);
                var myId = new BsonObjectId(objId);

                ops.Add(new TradeOperationDto()
                {
                    ExitDate = forecastDate,
                    EntryDate = DateTime.UtcNow.AddMinutes(5 * 2 * -1),
                    ForecastDate = forecastDate,
                    LowerBound = 0.005,
                    UpperBound = 0.01,
                    MaxValue = testLastValue,
                    AbsoluteLowerBound = testLastValue * (1 + 0.005),
                    AbsoluteUpperBound = testLastValue * (1 + 0.01),
                    StopLossMarker = testLastValue * 0.99,
                    PriceEntryDesired = testLastValue,
                    PriceEntryReal = testLastValue,
                    PriceExitDesired = 0,
                    PriceExitReal = 0,
                    LastValue = testLastValue,
                    State = TradeOperationState.In,
                    _id = myId
                });
            }
            if (ops.Count > 0)
            {
                if (ops.Count > 1)
                {
                    TraceAndLog.GetInstance().Log("Restore State", "Be aware, got more then one transaction in a IN State");
                }

                _currentOperation = new TradeOperation(ops.First(), _fund, this, _conString, _brokerConfig);
                _allOperations.Add(_currentOperation);
            }
        }

        private void OnTimedEvent(Object source, ElapsedEventArgs e)
        {
            GetFunds(_fundAccountName);
        }

        private void GetFunds(string fundAccountName)
        {
            var man = new FundsManager(_conString);
            _fund = man.GetFunds(fundAccountName).Amount;
        }

        public List<TradeOperationDto> GetOpenOperations()
        {
            var client = new MongoClient(_conString);
            var database = client.GetDatabase("CandlesFaces");
            var dbCol = database.GetCollection<TradeOperationDto>("TradeOperations");

            var query = dbCol.Find(item => item.State == TradeOperationState.In).ToList();

            return query.ToList();
        }

        public List<TradeOperationDto> SearchOperations(DateTime min)
        {
            var client = new MongoClient(_conString);
            var database = client.GetDatabase("CandlesFaces");
            var dbCol = database.GetCollection<TradeOperationDto>("TradeOperations");

            var filterBuilder1 = Builders<TradeOperationDto>.Filter;
            var filter = filterBuilder1.And(new FilterDefinition<TradeOperationDto>[]
            {
                filterBuilder1.Gte(item => item.EntryDate, min),
                filterBuilder1.Ne(item => item.PriceExitReal, 0)
            });

            var query = dbCol.Find(filter).ToList();

            return query.ToList();
        }        


        public void LoadCurrentOperations(DateTime minWindow)
        {
            var client = new MongoClient(_conString);
            var database = client.GetDatabase("CandlesFaces");
            var dbCol = database.GetCollection<TradeOperationDto>("TradeOperations");

            var filterBuilder1 = Builders<TradeOperationDto>.Filter;
            var filter = filterBuilder1.Gte(item => item.EntryDate, minWindow);

            var query = dbCol.Find(filter).ToList();

            _allOperations = GetStoredOperations(minWindow);

            _currentOperation = _allOperations.FirstOrDefault();
        }

        public List<TradeOperation> GetStoredOperations(DateTime minWindow)
        {
            var client = new MongoClient(_conString);

            var database = client.GetDatabase("CandlesFaces");
            var dbCol = database.GetCollection<TradeOperationDto>("TradeOperations");

            var filterBuilder1 = Builders<TradeOperationDto>.Filter;
            var filter = filterBuilder1.Gte(item => item.EntryDate, minWindow);

            var query = dbCol.Find(filter).ToList();

            List<TradeOperation> localAllTransactions;
            localAllTransactions = new List<TradeOperation>();
            query.ForEach(o =>
            {
                var op = new TradeOperation(o, _fund, this, _conString, _brokerConfig);
                localAllTransactions.Add(op);
            });

            return localAllTransactions;
        }        

        private TradeOperation _currentOperation;
        private List<TradeOperation> _allOperations;

        internal void SendMessage(string thread, string message)
        {
            //_runner.SendMessage(thread, message);
        }

        public void LoadOperations()
        {

        }

        public List<TradeOperation> GetOperations(DateTime validDate)
        {
            return _allOperations.Where(op => op.EntryDate > validDate).ToList();
        }
        public List<TradeOperation> GetOperationsThreadSafe(DateTime validDate)
        {
            lock(_allOperations)
            {
                return _allOperations.Where(op => op.EntryDate > validDate).ToList();
            }
        }
        public TradeOperation GetOneActiveOperation()
        {
            return _allOperations.OrderByDescending(op => op.EntryDate).FirstOrDefault(op => op.IsIn);
        }

        public void Signal(TradeType signal)
        {
            if (_currentOperation != null)
                _currentOperation.Signal(signal);
        }

        public async Task<TradeOperation> SignalEnterAsync(double value, double LowerBound, double upperBound, DateTime pointInTime, DateTime forecastPeriod, double atr)
        {
            TradeOperation ret = null;

            if (GetLastPredictionInPeriods() >= 2)
            {
                if (_currentOperation == null)
                {
                    _currentOperation = new TradeOperation(this, _fund, LowerBound, upperBound, forecastPeriod, _conString, _brokerConfig);
                    _allOperations.Add(_currentOperation);
                }
                else
                {
                    if (_currentOperation.IsCompleted)
                    {
                        _currentOperation = new TradeOperation(this, _fund, LowerBound, upperBound, forecastPeriod, _conString, _brokerConfig);
                        _allOperations.Add(_currentOperation);
                    }
                }
            }

            if (_currentOperation != null)
            {
                if (_currentOperation.State == TradeOperationState.Initial)
                {
                    await _currentOperation.EnterAsync(value, pointInTime, atr);
                    ret = _currentOperation;
                }
            }

            _lastAttempt = DateTime.Now;

            return ret;
        }

        public int GetLastPredictionInPeriods()
        {
            return Convert.ToInt32(Math.Round((DateTime.Now - _lastAttempt).TotalMinutes / 5));
        }

        public BrokerOrder ForceMarketSell()
        {
            var order = _broker.MarketOrder("EMERGENCYSELL", "BTCUSDT", OrderDirection.SELL, _fund, 60000);
            return order;
        }

        public void OnCandleUpdate(Candle c)
        {
            if (_currentOperation != null && _currentOperation.IsIn)
                _currentOperation.OnCandleUpdateAsync(c);
        }
    }

    public interface KlineRunner
    {
        void Subscribe(NewCandleAction action);
        void SendMessage(string thread, string message);
    }
}