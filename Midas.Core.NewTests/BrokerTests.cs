using System;
using System.Diagnostics;
using System.Dynamic;
using System.Threading;
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

            Broker.Broker broker = Broker.Broker.GetBroker("Binance",config, null);
            DateTime start = DateTime.Now;

            double currentPrice = broker.GetPriceQuote("BTCUSDT");            

            Debug.WriteLine("Preço atual: "+currentPrice.ToString("0.0000"));

            string orderId = DateTime.Now.Ticks.ToString();
            string asset = "BTCUSDT";
            var task = broker.SmartOrderAsync(orderId, asset, OrderDirection.BUY, amount, 180000, currentPrice, PriceBias.Optmistic);
            task.Wait(180000);
            Broker.BrokerOrder order = task.Result;

            Debug.WriteLine(order.Status);
            Debug.WriteLine(order.InError);
            Debug.WriteLine(order.AverageValue.ToString("0.0000"));
            Debug.WriteLine("MarketOrder Buy Avg: "+order.AverageValue.ToString());
            Debug.WriteLine("Tempo: "+ (DateTime.Now - start).TotalSeconds);

            Assert.IsTrue(order.InError);

            string orderIdB = DateTime.Now.Ticks.ToString()+"B";
            var task2 = broker.SmartOrderAsync(orderIdB, asset, OrderDirection.SELL, amount, 60000, order.AverageValue, PriceBias.Optmistic);
            task2.Wait(60000);
            Broker.BrokerOrder order2 = task2.Result;

            Debug.WriteLine(order2.AverageValue.ToString("0.0000"));
            Debug.WriteLine(order2.Status);
            Debug.WriteLine(order2.InError);
            Debug.WriteLine("MarketOrder Sell Avg: "+order2.AverageValue.ToString());

            Debug.WriteLine("Tempo: "+ (DateTime.Now - start).TotalSeconds);

            Assert.IsTrue(order2.Status == "FILLED");
        }

        [TestMethod]
        public void BrokerCreateSmartMarketOrder_ComCancel()
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

            Broker.Broker broker = Broker.Broker.GetBroker("Binance",config, null);
            DateTime start = DateTime.Now;

            double currentPrice = broker.GetPriceQuote("BTCUSDT");            

            Debug.WriteLine("Preço atual: "+currentPrice.ToString("0.0000"));

            string orderId = DateTime.Now.Ticks.ToString();
            string asset = "BTCUSDT";
            var task = broker.SmartOrderAsync(orderId, asset, OrderDirection.BUY, amount, 180000, currentPrice, PriceBias.Optmistic);
            task.Wait(180000);
            Broker.BrokerOrder order = task.Result;

            Debug.WriteLine(order.Status);
            Debug.WriteLine(order.InError);
            Debug.WriteLine(order.AverageValue.ToString("0.0000"));
            Debug.WriteLine("MarketOrder Buy Avg: "+order.AverageValue.ToString());
            Debug.WriteLine("Tempo: "+ (DateTime.Now - start).TotalSeconds);

            Assert.IsTrue(order.InError);

            string orderIdB = DateTime.Now.Ticks.ToString()+"B";
            var task2 = broker.SmartOrderAsync(orderIdB, asset, OrderDirection.SELL, amount, 60000, order.AverageValue, PriceBias.Optmistic);
            task2.Wait(60000);
            Broker.BrokerOrder order2 = task2.Result;

            Debug.WriteLine(order2.AverageValue.ToString("0.0000"));
            Debug.WriteLine(order2.Status);
            Debug.WriteLine(order2.InError);
            Debug.WriteLine("MarketOrder Sell Avg: "+order2.AverageValue.ToString());

            Debug.WriteLine("Tempo: "+ (DateTime.Now - start).TotalSeconds);

            Assert.IsTrue(order2.Status == "FILLED");
        }



        [TestMethod]
        public void TestCancelOrder()
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

            Broker.Broker broker = Broker.Broker.GetBroker("Binance",config, null);
            DateTime start = DateTime.Now;

            double currentPrice = broker.GetPriceQuote(asset);            

            Debug.WriteLine("Preço atual: "+currentPrice.ToString("0.0000"));

            string orderId = DateTime.Now.Ticks.ToString();
            var order = broker.LimitOrder(orderId, asset, OrderDirection.BUY, amount, 180000, currentPrice*0.9);

            var status = broker.OrderStatus(orderId, asset, 5000);

            Assert.IsTrue(status.Status == "NEW");

            Assert.IsTrue(broker.CancelOrder(orderId, asset, 10000));
        }    

        [TestMethod]
        public void TestCancelAllOrder()
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

            Broker.Broker broker = Broker.Broker.GetBroker("Binance",config, null);
            DateTime start = DateTime.Now;

            double currentPrice = broker.GetPriceQuote(asset);            

            Debug.WriteLine("Preço atual: "+currentPrice.ToString("0.0000"));

            string orderId = DateTime.Now.Ticks.ToString();
            var order = broker.LimitOrder(orderId, asset, OrderDirection.BUY, amount, 180000, currentPrice*0.9);

            var status = broker.OrderStatus(orderId, asset, 5000);

            Assert.IsTrue(status.Status == "NEW");

            broker.CancelAllOpenOrdersAsync(asset, 10000);
        }            
    }
}

