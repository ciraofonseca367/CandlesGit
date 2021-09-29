using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using Midas.Core.Broker;
using Midas.Core.Common;
using Midas.Core.Services;
using Midas.Core.Telegram;
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
        private string _brokerName;
        private Broker _broker;
        private System.Timers.Timer _fundRefresher;
        private double _fund;
        private dynamic _brokerConfig;

        private string _fundAccountName;

        private string _asset;
        private CandleType _candleType;
        private DateTime _lastAttempt;

        private static DateTime ANGEL_BIRTH = new DateTime(2021, 02, 24, 17, 0, 0);

        private static Dictionary<string, TradeOperationManager> _managers;

        private TradeLogger _logger;
        private AssetTrader _trader;

        private TradeOperation _lastTrade;

        private string _experiment;



        static TradeOperationManager()
        {
            _managers = new Dictionary<string, TradeOperationManager>(11);
        }

        public static TradeOperationManager GetManager(AssetTrader trader, string conString, string fundAccountName,string brokerName, dynamic brokerConfig, string asset, CandleType candleType, string experiment)
        {
            TradeOperationManager man = null;

            string key = $"{asset}-{candleType.ToString()}";

            _managers.TryGetValue(key, out man);
            if (man == null)
            {
                lock (_managers)
                {
                    _managers.TryGetValue(key, out man);
                    if (man == null)
                    {
                        man = new TradeOperationManager(trader, conString, fundAccountName, brokerConfig, asset, candleType, experiment, brokerName);
                        _managers.Add(key, man);
                    }
                }
            }

            return man;
        }

        public TradeOperationManager(AssetTrader trader, string conString, string fundAccountName, dynamic brokerConfig, string asset, CandleType candleType, string experiment, string brokerName)
        {
            _currentOperation = null;
            _allOperations = new List<TradeOperation>();
            _conString = conString;
            _brokerName = brokerName;

            _asset = asset;
            _candleType = candleType;
            _trader = trader;
            _experiment = experiment;


            _broker = Broker.GetBroker("Binance", brokerConfig);

            _lastAttempt = ANGEL_BIRTH;

            _fundAccountName = fundAccountName;

            _logger = new TradeLogger();

            _fundRefresher = new System.Timers.Timer(60 * 1000);
            _fundRefresher.Elapsed += OnTimedEvent;
            _fundRefresher.AutoReset = true;
            _fundRefresher.Enabled = true;

            _brokerConfig = brokerConfig;

            GetFunds();
        }

        public TradeOperation RestoreState()
        {
            List<TradeOperationDto> ops = null;
            ops = GetOpenOperations();
            TradeOperation state = null;

            if (ops.Count > 0)
            {
                if (ops.Count > 1)
                {
                    TraceAndLog.GetInstance().Log("Restore State", "Be aware, got more then one transaction in a IN State");
                }

                _currentOperation = new TradeOperation(ops.First(), _fund, this, _conString, _brokerName,  _brokerConfig);
                state = _currentOperation;
                _allOperations.Add(_currentOperation);
            }

            return state;
        }

        private void OnTimedEvent(Object source, ElapsedEventArgs e)
        {
            GetFunds();
        }

        public TradeLogger TradeLogger
        {
            get
            {
                return _logger;
            }
        }

        public AssetTrader Trader { get => _trader; }

        public string Experiment
        {
            get
            {
                return _experiment;
            }
        }


        public PriceDirection GetPriceDirection()
        {
            return _logger.GetDirection(new TimeSpan(0,5,0));
        }

        internal void GetFunds()
        {
            var man = new FundsManager(_conString);
            _fund = man.GetFunds(_fundAccountName).Amount;
        }

        public double Funds
        {
            get
            {
                return _fund;
            }
        }

        public List<TradeOperationDto> GetOpenOperations()
        {
            return InvestorService.SearchOperations(_conString, null, _asset, _candleType, DateTime.MinValue, DateTime.UtcNow.AddHours(-12), TradeOperationState.In, false);
        }

        public List<TradeOperationDto> SearchOperations(string asset, DateTime min)
        {
            var client = new MongoClient(_conString);
            var database = client.GetDatabase("CandlesFaces");
            var dbCol = database.GetCollection<TradeOperationDto>("TradeOperations");

            var filterBuilder1 = Builders<TradeOperationDto>.Filter;
            var filter = filterBuilder1.And(new FilterDefinition<TradeOperationDto>[]
            {
                filterBuilder1.Gte(item => item.EntryDate, min),
                filterBuilder1.Ne(item => item.PriceExitReal, 0),
                filterBuilder1.Eq(item => item.Asset, asset)
            });

            var query = dbCol.Find(filter).ToList();

            return query.ToList();
        }

        public List<TradeOperation> GetActiveStoredOperations(string asset, CandleType candleType,string experiment, DateTime relativeNow)
        {
            var query = InvestorService.SearchActiveOperations(_conString, asset, candleType,experiment, relativeNow);

            List<TradeOperation> localAllTransactions;
            localAllTransactions = new List<TradeOperation>();
            query.ForEach(o =>
            {
                var op = new TradeOperation(o, _fund, this, _conString, _brokerName, _brokerConfig);
                localAllTransactions.Add(op);
            });

            return localAllTransactions;
        }        

        private TradeOperation _currentOperation;
        private List<TradeOperation> _allOperations;

        internal void SendMessage(string thread, string message)
        {
            TelegramBot.SendMessageBuffered(thread,message);
        }

        public void LoadOperations()
        {

        }

        public List<TradeOperation> GetOperations(DateTime validDate)
        {
            return _allOperations.Where(op => op.EntryDate > validDate).ToList();
        }

        internal void OperationFinished(TradeOperation op, Candle cc)
        {
            _lastTrade = op;
            _trader.SaveSnapshot(cc);
        }

        private bool IsBlocked(DateTime relativeNow)
        {
            bool blocked = false;
            if(_lastTrade != null)
            {
                if(_lastTrade.IsClassic())
                {
                    var howLongAgo = relativeNow - _lastTrade.ExitDate;
                    if(howLongAgo.TotalHours < 30)
                        blocked = true;
                }
            }

            return blocked;
        }

        public List<TradeOperation> GetOperationsThreadSafe(DateTime validDate)
        {
            lock (_allOperations)
            {
                return _allOperations.Where(op => op.EntryDate > validDate).ToList();
            }
        }
        public TradeOperation GetOneActiveOperation()
        {
            return _allOperations.OrderByDescending(op => op.EntryDate).FirstOrDefault(op => op.IsIn);
        }

        public void Signal(TrendType signal)
        {
            if (_currentOperation != null)
                _currentOperation.Signal(signal);
        }

        public TradeOperation SignalEnter(double value, DateTime pointInTime, DateTime forecastPeriod, double atr, string modelName)
        {
            TradeOperation ret = null;

            //if (!IsBlocked(pointInTime))
            //{
                if (_currentOperation == null)
                {
                    _currentOperation = new TradeOperation(this, _fund, forecastPeriod, _conString, _brokerConfig, _asset, _candleType, _brokerName);
                    _allOperations.Add(_currentOperation);
                }
                else
                {
                    if (_currentOperation.IsCompleted)
                    {
                        _currentOperation = new TradeOperation(this, _fund, forecastPeriod, _conString, _brokerConfig, _asset, _candleType, _brokerName);
                        _allOperations.Add(_currentOperation);
                    }
                }
            //}

            if (_currentOperation != null)
            {
                if (_currentOperation.State == TradeOperationState.Initial)
                {
                    _currentOperation.Enter(value, pointInTime, atr, modelName);
                    ret = _currentOperation;
                }
            }

            _lastAttempt = DateTime.Now;

            return ret;
        }

        public BrokerOrder ForceMarketSell()
        {
            var order = _broker.MarketOrder("EMERGENCYSELL", _asset, OrderDirection.SELL, _fund, 60000, 0, DateTime.UtcNow, false);
            return order;
        }

        public void OnCandleUpdate(Candle c)
        {
            _logger.AddTrade(c.OpenTime,c.AmountValue);
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