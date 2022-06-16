using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using Midas.Core;
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

    public class TradeOperationManager : IDisposable
    {
        private string _conString;
        private string _brokerName;
        private Broker _broker;
        private dynamic _brokerConfig;

        private string _asset;
        private CandleType _candleType;
        private DateTime _lastAttempt;

        private static DateTime ANGEL_BIRTH = new DateTime(2021, 02, 24, 17, 0, 0);

        private static Dictionary<string, TradeOperationManager> _managers;

        private AssetTrader _trader;

        private TradeOperation _lastTrade;

        private string _experiment;

        private FundSlotManager _slotManager;

        static TradeOperationManager()
        {
            _managers = new Dictionary<string, TradeOperationManager>(11);
        }

        public static TradeOperationManager GetManager(AssetTrader trader, string conString, string brokerName, dynamic brokerConfig, string asset, CandleType candleType, string experiment)
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
                        man = new TradeOperationManager(trader, conString, brokerConfig, asset, candleType, experiment, brokerName);
                        _managers.Add(key, man);
                    }
                }
            }

            return man;
        }

        public TradeOperationManager(AssetTrader trader, string conString, dynamic brokerConfig, string asset, CandleType candleType, string experiment, string brokerName)
        {
            _currentOperation = null;
            _allOperations = new ConcurrentBag<TradeOperation>();
            _conString = conString;
            _brokerName = brokerName;

            _asset = asset;
            _candleType = candleType;
            _trader = trader;
            _experiment = experiment;


            _broker = Broker.GetBroker("Binance", brokerConfig);

            _lastAttempt = ANGEL_BIRTH;

            _brokerConfig = brokerConfig;

            if (trader != null)
            {
                Console.WriteLine($"Starting Trader with {RunParameters.GetInstance().GetHyperParamAsInt("NumberOfSlots")} slots");
                string endPoint = Convert.ToString(brokerConfig.WebSocket);

                if (RunParameters.GetInstance().EnableLimitOrders)
                {
                    Console.WriteLine("Start order watcher...");
                    _orderWatcher = new OrderWatcher(endPoint, _asset);
                    _orderWatcher.StartWatching();
                }
            }
        }

        // public TradeOperation RestoreState()
        // {
        //     List<TradeOperationDto> ops = null;
        //     ops = GetOpenOperations();
        //     TradeOperation state = null;

        //     if (ops.Count > 0)
        //     {
        //         if (ops.Count > 1)
        //         {
        //             TraceAndLog.GetInstance().Log("Restore State", "Be aware, got more then one transaction in a IN State");
        //         }

        //         _currentOperation = new TradeOperation(ops.First(), 0, this, _conString, _brokerName, _brokerConfig);
        //         state = _currentOperation;
        //         _allOperations.Add(_currentOperation);
        //     }

        //     return state;
        // }

        public bool SetNewFunds(double fund)
        {
            var operationRunning = GetOneActiveOperation();
            var ret = false;
            if(operationRunning == null)
            {
                _slotManager = new FundSlotManager(fund, RunParameters.GetInstance().GetHyperParamAsInt("NumberOfSlots"));
                ret = true;
            }

            return ret;
        }

        public AssetTrader Trader { get => _trader; }

        public string Experiment
        {
            get
            {
                return _experiment;
            }
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

        public List<TradeOperationDto> GetActiveStoredOperations(string asset, CandleType candleType, string experiment, DateTime relativeNow)
        {
            var query = InvestorService.SearchActiveOperations(_conString, asset, candleType, null, relativeNow);

            return query.ToList();
        }

        private TradeOperation _currentOperation;
        private ConcurrentBag<TradeOperation> _allOperations;
        private OrderWatcher _orderWatcher;

        internal async Task SendMessage(string thread, string message)
        {
            await TelegramBot.SendMessageBuffered(thread, message);
        }

        internal async Task SendImage(Bitmap img, string msg)
        {
            await TelegramBot.SendImage(img, msg);
        }

        public void LoadOperations()
        {

        }

        public List<TradeOperation> GetOperations(DateTime validDate)
        {
            return _allOperations.Where(op => op.EntryDate > validDate).ToList();
        }

        internal async Task OperationFinished(TradeOperation op, Candle cc, FundSlot slot)
        {
            _lastTrade = op;
            _slotManager.ReturnSlot(slot.Id);
            await _trader.SaveSnapshot(cc, op);
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
        public IEnumerable<TradeOperation> GetAllActiveOperations()
        {
            return _allOperations.OrderByDescending(op => op.EntryDate).Where(op => op.IsIn);
        }

        public IEnumerable<TradeOperation> GetLastRecentOperations()
        {
            return _allOperations
            .Where(o => o.IsIn)
            .OrderByDescending(op => op.EntryDate)
            .Take(10);
        }

        public void Signal(TrendType signal)
        {
            if (_currentOperation != null)
                _currentOperation.Signal(signal);
        }

        public async Task<TradeOperation> SignalEnter(double value, DateTime pointInTime, DateTime forecastPeriod, double ratr, string modelName, Bitmap image)
        {
            TradeOperation ret = null;


            if(_slotManager == null)
                throw new ArgumentException("Can't start operation with no fund set on me.");

            if (_allOperations.Count() > 50)
            {
                lock (_allOperations)
                {
                    if (_allOperations.Count() > 50)
                    {
                        var tmpIn = _allOperations.Where(o => o.IsIn);
                        _allOperations = new ConcurrentBag<TradeOperation>(tmpIn);
                        Console.WriteLine("CLEANING UP");
                    }
                }
            }


            var op = _allOperations.Where(op => op.ModelName == modelName && op.IsIn).FirstOrDefault();
            if (op == null)
            {

                var slot = _slotManager.TryGetSlot();
                if (slot != null)
                {
                    Console.WriteLine($"Starting OP with slot: {slot.SlotAmount}");
                    _currentOperation = new TradeOperation(this, slot, forecastPeriod, _conString, _brokerConfig, _asset, _candleType, _brokerName);
                    _allOperations.Add(_currentOperation);

                    await _currentOperation.Enter(value, pointInTime, ratr, modelName, image);
                    ret = _currentOperation;
                }
                else
                {
                    _slotManager.Dump();
                    Console.WriteLine("===== SEM SLOTS ======");
                }
            }
            else
            {
                Console.WriteLine($"===== Already one operation running on the model {modelName} ======");
            }


            _lastAttempt = DateTime.Now;

            var activeOps = _allOperations.Where(o => o.IsIn);

            Console.WriteLine("Operações ativas: " + activeOps.Count());

            return ret;
        }

        public async Task OnCandleUpdate(Candle c)
        {
            foreach (var op in _allOperations)
                await op.OnCandleUpdateAsync(c);
        }

        public void Dispose()
        {
            if(_orderWatcher != null)
                _orderWatcher.Dispose();
        }

        internal void WatchOrder(BrokerOrder order)
        {
            if(_orderWatcher != null)
                _orderWatcher.AddOrder(order);
        }
    }

    public interface KlineRunner
    {
        void Subscribe(NewCandleAction action);
        void SendMessage(string thread, string message);
    }
}