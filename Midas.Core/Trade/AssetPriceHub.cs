using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Midas.Core.Binance;
using Midas.Core.Broker;

namespace Midas.Core.Trade
{
    public class AssetPriceHub
    {
        private static ConcurrentDictionary<string, AssetPairMiniBroker> _tickers;

        static AssetPriceHub()
        {
            _tickers = new ConcurrentDictionary<string, AssetPairMiniBroker>();
        }

        public static AssetPairMiniBroker InitAssetPair(string identification)
        {
            var pair = new AssetPairMiniBroker(identification);
            _tickers[identification] = pair;

            return pair;
        }

        public static void UpdatePrice(string assetId, double newPrice)
        {
            var assetPairInfo = _tickers[assetId];

            assetPairInfo.SetPrice(newPrice);
        }

        public static AssetPairMiniBroker GetTicker(string assetId)
        {
            return _tickers[assetId];
        }

    }

    public class AssetPairMiniBroker
    {
        private double _lastPrice;

        private double _lastMaxPrice;

        private string _identification;

        private ConcurrentDictionary<string, BrokerOrder> _ordersBeingWatched;

        private ConcurrentDictionary<string, BrokerOrder> _processedOrders;

        public double LastPrice { get => _lastPrice; }
        public double LastMaxPrice { get => _lastMaxPrice; }

        public AssetPairMiniBroker(string identification)
        {
            _lastMaxPrice = Double.MinValue;
            _identification = identification;
            _ordersBeingWatched = new ConcurrentDictionary<string, BrokerOrder>();
            _processedOrders = new ConcurrentDictionary<string, BrokerOrder>();
            _lastPrice = -1;

            if (RunParameters.GetInstance().IsTesting)
            {
                Task.Run(() =>
                {
                    while (true)
                    {
                        List<BrokerOrder> toRemove = new List<BrokerOrder>();

                        foreach (var pair in _ordersBeingWatched)
                        {
                            if (_lastPrice != -1 && pair.Value.IsFilled(_lastPrice))
                            {
                                SetOrderFilled(pair.Value);

                                Console.WriteLine("FILLED - " + pair.Value.ToString());

                                toRemove.Add(pair.Value);

                                _processedOrders[pair.Key] = pair.Value;
                            }
                        }

                        BrokerOrder order = null;
                        toRemove.ForEach(o => _ordersBeingWatched.TryRemove(o.OrderId, out order));
                        Thread.Sleep(2);
                    }
                });
            }
        }

        private void SetOrderFilled(BrokerOrder order)
        {
            order.Status = BrokerOrderStatus.FILLED;
            order.RawStatus = "FILLED";

            order.AddTrade(new TradeStreamItem()
            {
                BuyerId = "TESTE",
                SellerId = "TESTE",
                Qdy = order.Quantity,
                Price = order.AverageValue
            });
        }

        public void SetPrice(double price)
        {
            _lastPrice = price;

            if (price > _lastMaxPrice)
                _lastMaxPrice = price;
        }

        public void WatchOrder(BrokerOrder order)
        {
            if (_lastPrice != -1 && order.IsFilled(_lastPrice))
            {
                SetOrderFilled(order);

                _processedOrders[order.OrderId] = order;

                Debug.WriteLine("FILLED" + order.ToString());
                Console.WriteLine("FILLED" + order.ToString());
            }
            else
            {
                _ordersBeingWatched[order.OrderId] = order;
            }
        }

        public void CancelOrder(string orderId)
        {
            BrokerOrder order = null;

            _ordersBeingWatched.TryGetValue(orderId, out order);
            if (order != null)
            {
                order.Status = BrokerOrderStatus.CANCELED;
                order.RawStatus = "CANCELED";

                _ordersBeingWatched.TryRemove(order.OrderId, out order);
                _processedOrders[order.OrderId] = order;

                Debug.WriteLine("CANCELLED: " + order.ToString());
                Console.WriteLine("CANCELLED: " + order.ToString());

            }
            else
            {
                throw new ArgumentException($"Couldn't find order {orderId} to cancel!");
            }
        }

        public BrokerOrder GetOrder(string orderId)
        {
            BrokerOrder order = null;
            _ordersBeingWatched.TryGetValue(orderId, out order);
            if (order == null)
                _processedOrders.TryGetValue(orderId, out order);

            if (order == null)
                throw new ArgumentException($"Order {orderId} not found to get status from");

            return order;
        }

        public void DumpInfo()
        {
            var newStatus = _ordersBeingWatched.Values.Count();
            var filledStatus = _processedOrders.Values.Where(o => o.Status == BrokerOrderStatus.FILLED).Count();
            var cancelledStatus = _processedOrders.Values.Where(o => o.Status == BrokerOrderStatus.CANCELED).Count();

            Console.WriteLine($"DUMP INFO: New: {newStatus}, Filled: {filledStatus}, cancelled: {cancelledStatus}");
        }
    }
}