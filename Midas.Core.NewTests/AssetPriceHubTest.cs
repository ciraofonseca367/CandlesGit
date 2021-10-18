using System;
using System.Dynamic;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Midas.Core.Broker;
using Midas.Core.Trade;
using Midas.Trading;

namespace Midas.Core.NewTests
{
    [TestClass]
    public class AssetPriceHubTest
    {
        [TestMethod]
        public void AssetPriceHub_Test1()
        {
            var pair = AssetPriceHub.InitAssetPair("BTCBUSD");

             var order = Broker.Broker.GetFakeOrder("111111",OrderDirection.BUY,OrderType.LIMIT, 10, 1000, DateTime.Now);
             pair.WatchOrder(order);

             order = Broker.Broker.GetFakeOrder("111112",OrderDirection.SELL,OrderType.LIMIT, 10, 2000, DateTime.Now);
             pair.WatchOrder(order);

             order = Broker.Broker.GetFakeOrder("111113",OrderDirection.SELL,OrderType.LIMIT, 10, 1000, DateTime.Now);
             pair.WatchOrder(order);

             pair.SetPrice(1500);              
             Thread.Sleep(3000);
             Console.WriteLine("Preço 1700");
             pair.SetPrice(1700);
             Thread.Sleep(3000);
             Console.WriteLine("Preço 1800");
             pair.SetPrice(1800);
             Thread.Sleep(3000);
             Console.WriteLine("Preço 1900");
             pair.SetPrice(1900);
             Thread.Sleep(3000);
             Console.WriteLine("Preço 2001");
             pair.SetPrice(2001);
             Thread.Sleep(3000);             
             Console.WriteLine("Preço 1400");
             pair.SetPrice(1400);
             Thread.Sleep(3000);             
             Console.WriteLine("Preço 1100");
             pair.SetPrice(1100);
             Thread.Sleep(3000);             
             Console.WriteLine("Preço 900");
             pair.SetPrice(900);
             Thread.Sleep(3000);             

             Thread.Sleep(120000);
        }
    }
}

