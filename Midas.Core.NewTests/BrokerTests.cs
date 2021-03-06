using System;
using System.Diagnostics;
using System.Dynamic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Midas.Core.Broker;
using Midas.Trading;

namespace Midas.Core.NewTests
{
    [TestClass]
    public class BrokerTest
    {
        [TestMethod]
        public void BrokerCreateSmartMarketOrder()
        {
            string apiKey = "hUKcnDYlH154l6vYjWm5MYirJoaVLhXyEkyc3sMhaI2dQZh6EnxoH050alC3J6ax";
            string apiSecret = "5yTc5tiMDelKsJhMoz9oxTEeISPv0tLEi6g8aOie6UoPWxhKsYwJuWWT4FjPVMgj";
            string endPoint = "https://testnet.binance.vision";
            string host = "testnet.binance.vision";

            double amount = 0.0024;

            dynamic config = new ExpandoObject();
            config.ApiKey = apiKey;
            config.ApiSecret = apiSecret;
            config.EndPoint = endPoint;
            config.Host = host;

            config.BIAS_DIFF_FORBUY = 0.9999;
            config.BIAS_DIFF_FORSELL = 1.0001;
            config.TIMEOUT_BUY = 200000;
            config.TIMEOUT_SELL = 60000;
            config.STOP_LOSS = 0.0099;
            config.MSI_WINDOW_SIZE_SECONDS = 900;

            Broker.Broker broker = Broker.Broker.GetBroker("Binance", config, null, null);
            DateTime start = DateTime.Now;

            //double currentPrice = broker.GetPriceQuote("BTCUSDT");            
            double currentPrice = 0; //Acertar unittest

            Debug.WriteLine("Preço atual: " + currentPrice.ToString("0.0000"));

            string orderId = DateTime.Now.Ticks.ToString();
            string asset = "BTCUSDT";
            var task = broker.SmartOrderAsync(orderId, asset, OrderDirection.BUY, amount, 180000, currentPrice, PriceBias.Optmistic, DateTime.Now);
            task.Wait(180000);
            Broker.BrokerOrder order = task.Result;

            Debug.WriteLine(order.RawStatus);
            Debug.WriteLine(order.InError);
            Debug.WriteLine(order.AverageValue.ToString("0.0000"));
            Debug.WriteLine("MarketOrder Buy Avg: " + order.AverageValue.ToString());
            Debug.WriteLine("Tempo: " + (DateTime.Now - start).TotalSeconds);

            Assert.IsTrue(order.InError);

            string orderIdB = DateTime.Now.Ticks.ToString() + "B";
            var task2 = broker.SmartOrderAsync(orderIdB, asset, OrderDirection.SELL, amount, 60000, order.AverageValue, PriceBias.Optmistic, DateTime.Now);
            task2.Wait(60000);
            Broker.BrokerOrder order2 = task2.Result;

            Debug.WriteLine(order2.AverageValue.ToString("0.0000"));
            Debug.WriteLine(order2.RawStatus);
            Debug.WriteLine(order2.InError);
            Debug.WriteLine("MarketOrder Sell Avg: " + order2.AverageValue.ToString());

            Debug.WriteLine("Tempo: " + (DateTime.Now - start).TotalSeconds);

            Assert.IsTrue(order2.RawStatus == "FILLED");
        }

        [TestMethod]
        public void BrokerCreateSmartMarketOrder_ComCancel()
        {
            Task.Run(async () =>
            {
                string apiKey = "hUKcnDYlH154l6vYjWm5MYirJoaVLhXyEkyc3sMhaI2dQZh6EnxoH050alC3J6ax";
                string apiSecret = "5yTc5tiMDelKsJhMoz9oxTEeISPv0tLEi6g8aOie6UoPWxhKsYwJuWWT4FjPVMgj";
                string endPoint = "https://testnet.binance.vision";
                string host = "testnet.binance.vision";

                double amount = 0.0024;

                dynamic config = new ExpandoObject();
                config.ApiKey = apiKey;
                config.ApiSecret = apiSecret;
                config.EndPoint = endPoint;
                config.Host = host;

                config.BIAS_DIFF_FORBUY = 1.1; //Para ficar muito facil comprar
                config.BIAS_DIFF_FORSELL = 1.1; //Para ficar impossivel de vender no SELL LIMIT e testar o market order e o cancel
                config.TIMEOUT_BUY = 200000;
                config.TIMEOUT_SELL = 60000;
                config.STOP_LOSS = 0.0099;
                config.MSI_WINDOW_SIZE_SECONDS = 900;

                Broker.Broker broker = Broker.Broker.GetBroker("Binance", config, null, null);
                DateTime start = DateTime.Now;

                double currentPrice = await broker.GetPriceQuote("BTCUSDT");

                Debug.WriteLine("Preço atual: " + currentPrice.ToString("0.0000"));

                string orderId = DateTime.Now.Ticks.ToString();
                string asset = "BTCUSDT";
                var task = broker.SmartOrderAsync(orderId, asset, OrderDirection.BUY, amount, 180000, currentPrice, PriceBias.Optmistic, DateTime.Now);
                task.Wait(180000);
                Broker.BrokerOrder order = task.Result;

                Debug.WriteLine(order.RawStatus);
                Debug.WriteLine(order.InError);
                Debug.WriteLine(order.AverageValue.ToString("0.0000"));
                Debug.WriteLine("MarketOrder Buy Avg: " + order.AverageValue.ToString());
                Debug.WriteLine("Tempo: " + (DateTime.Now - start).TotalSeconds);

                Assert.IsTrue(order.InError);

                string orderIdB = DateTime.Now.Ticks.ToString() + "B";
                var task2 = broker.SmartOrderAsync(orderIdB, asset, OrderDirection.SELL, amount, 60000, order.AverageValue, PriceBias.Optmistic, DateTime.Now);
                task2.Wait(60000);
                Broker.BrokerOrder order2 = task2.Result;

                Debug.WriteLine(order2.AverageValue.ToString("0.0000"));
                Debug.WriteLine(order2.RawStatus);
                Debug.WriteLine(order2.InError);
                Debug.WriteLine("MarketOrder Sell Avg: " + order2.AverageValue.ToString());

                Debug.WriteLine("Tempo: " + (DateTime.Now - start).TotalSeconds);

                Assert.IsTrue(order2.RawStatus == "FILLED");
            }).GetAwaiter().GetResult();
        }



        [TestMethod]
        public void TestCancelOrder()
        {
            Task.Run(async () =>
            {
                string apiKey = "hUKcnDYlH154l6vYjWm5MYirJoaVLhXyEkyc3sMhaI2dQZh6EnxoH050alC3J6ax";
                string apiSecret = "5yTc5tiMDelKsJhMoz9oxTEeISPv0tLEi6g8aOie6UoPWxhKsYwJuWWT4FjPVMgj";
                string endPoint = "https://testnet.binance.vision";
                string host = "testnet.binance.vision";

                double amount = 0.0024;
                string asset = "BTCUSDT";

                dynamic config = new ExpandoObject();
                config.ApiKey = apiKey;
                config.ApiSecret = apiSecret;
                config.EndPoint = endPoint;
                config.Host = host;

                config.BIAS_DIFF_FORBUY = 0.9999;
                config.BIAS_DIFF_FORSELL = 1.0001;
                config.TIMEOUT_BUY = 200000;
                config.TIMEOUT_SELL = 60000;
                config.STOP_LOSS = 0.0099;
                config.MSI_WINDOW_SIZE_SECONDS = 900;

                Broker.Broker broker = Broker.Broker.GetBroker("Binance", config);
                DateTime start = DateTime.Now;

                double currentPrice = await broker.GetPriceQuote(asset);

                Debug.WriteLine("Preço atual: " + currentPrice.ToString("0.0000"));

                string orderId = DateTime.Now.Ticks.ToString();
                var order = await broker.LimitOrderAsync(orderId, asset, OrderDirection.BUY, amount, 180000, currentPrice * 0.9, currentPrice * 0.9, DateTime.Now);

                var status = await broker.OrderStatusAsync(orderId, asset, 5000);

                Assert.IsTrue(status.RawStatus == "NEW");

                Assert.IsTrue(broker.CancelOrder(orderId, asset, 10000));
            }).GetAwaiter().GetResult();
        }

        [TestMethod]
        public void TestCancelAllOrder()
        {
            Task.Run(async () =>
            {
                string apiKey = "hUKcnDYlH154l6vYjWm5MYirJoaVLhXyEkyc3sMhaI2dQZh6EnxoH050alC3J6ax";
                string apiSecret = "5yTc5tiMDelKsJhMoz9oxTEeISPv0tLEi6g8aOie6UoPWxhKsYwJuWWT4FjPVMgj";
                string endPoint = "https://testnet.binance.vision";
                string host = "testnet.binance.vision";

                double amount = 0.0024;
                string asset = "BTCUSDT";

                dynamic config = new ExpandoObject();
                config.ApiKey = apiKey;
                config.ApiSecret = apiSecret;
                config.EndPoint = endPoint;
                config.Host = host;

                config.BIAS_DIFF_FORBUY = 0.9999;
                config.BIAS_DIFF_FORSELL = 1.0001;
                config.TIMEOUT_BUY = 200000;
                config.TIMEOUT_SELL = 60000;
                config.STOP_LOSS = 0.0099;
                config.MSI_WINDOW_SIZE_SECONDS = 900;

                Broker.Broker broker = Broker.Broker.GetBroker("Binance", config, null, null);
                DateTime start = DateTime.Now;

                double currentPrice = await broker.GetPriceQuote(asset);

                Debug.WriteLine("Preço atual: " + currentPrice.ToString("0.0000"));

                string orderId = DateTime.Now.Ticks.ToString();
                var order = await broker.LimitOrderAsync(orderId, asset, OrderDirection.BUY, amount, 180000, currentPrice * 0.9, currentPrice * 0.9, DateTime.Now);

                var status = await broker.OrderStatusAsync(orderId, asset, 5000);

                Assert.IsTrue(status.RawStatus == "NEW");

                await broker.CancelAllOpenOrdersAsync(asset, 10000);

            }).GetAwaiter().GetResult();
        }
    }
}

