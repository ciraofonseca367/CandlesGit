using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Midas.Core.Binance;
using Midas.Core.Broker;

namespace Midas.Core.Trade
{
    public class OrderWatcher : IDisposable
    {
        private ConcurrentDictionary<string, BrokerOrder> _orders;
        private TradeStreamWebSocket _tradeSocket;

        private bool _active;

        private Thread _collector;
        private Thread _processor;

        private string _symbol;

        private ConcurrentDictionary<string, TradeStreamItem> _buffer;

        public OrderWatcher(string binanceUri, string symbol)
        {
            _symbol = symbol;
            _orders = new ConcurrentDictionary<string, BrokerOrder>();
            _tradeSocket = new TradeStreamWebSocket(binanceUri, symbol, 10000);

            _collector = new Thread(new ThreadStart(this.OrderGarbageCollection));
            _processor = new Thread(new ThreadStart(this.Watcher));
            _buffer = new ConcurrentDictionary<string, TradeStreamItem>();
        }

        public void StartWatching()
        {
            _active = true;
            _tradeSocket.Open();
            _tradeSocket.OnNewTrade(this.OnNewTrade);
            _collector.Start();
            _processor.Start();
        }

        public void AddOrder(BrokerOrder order)
        {
            _orders[order.BrokerOrderId] = order;
        }

        public void Dispose()
        {
            _active = false;
            if (_tradeSocket != null)
                _tradeSocket.Dispose();
        }

        private void OnNewTrade(TradeStreamItem tradeItem)
        {
            _buffer.TryAdd(tradeItem.Id, tradeItem);
        }

        private void Watcher()
        {
            while (_active)
            {
                var toProcess = _buffer.Where(i => (DateTime.Now - i.Value.CreationDate).TotalSeconds > 5).ToList();

                toProcess.ForEach(tradeItem =>
                {
                    BrokerOrder order = null;

                    _orders.TryGetValue(tradeItem.Value.BuyerId, out order);
                    if (order == null)
                        _orders.TryGetValue(tradeItem.Value.SellerId, out order);

                    if (order != null)
                    {
                        order.AddTrade(tradeItem.Value);
                    }

                    TradeStreamItem item = null;
                    _buffer.TryRemove(tradeItem.Value.Id, out item);
                });

                Thread.Sleep(10);
            }
        }

        private void OrderGarbageCollection()
        {
            while (_active)
            {
                List<string> toRemove = new List<string>();

                foreach (var order in _orders)
                {
                    if (order.Value.CalculatedStatus == BrokerOrderStatus.FILLED)
                    {
                        toRemove.Add(order.Value.BrokerOrderId);
                    }
                }


                BrokerOrder removedOrder = null;
                toRemove.ForEach(oId => _orders.TryRemove(oId, out removedOrder));

                Thread.Sleep(5000);
            }
        }
    }
}