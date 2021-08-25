using System;
using System.Dynamic;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Midas.Core.Trade;
using Midas.Trading;

namespace Midas.Core.NewTests
{
    [TestClass]
    public class TradeAggregationTest
    {
        [TestMethod]
        public void PriceDirection_Test()
        {
            TradeLogger trades = new TradeLogger();

            var now = DateTime.Now;

            trades.AddTrade(now.AddMinutes(-20), 850);
            trades.AddTrade(now.AddMinutes(-20).AddSeconds(20), 950);
            trades.AddTrade(now.AddMinutes(-15), 900);
            trades.AddTrade(now.AddMinutes(-15).AddSeconds(20), 1000);
            trades.AddTrade(now.AddMinutes(-10), 1000);
            trades.AddTrade(now.AddMinutes(-10).AddSeconds(20), 1080);
            trades.AddTrade(now.AddMinutes(-5), 1100);
            trades.AddTrade(now.AddMinutes(-5).AddSeconds(20), 1200);

            trades.AddTrade(now, 1200);
            trades.AddTrade(now, 1190);
            trades.AddTrade(now, 1180);
            trades.AddTrade(now, 1210);
            trades.AddTrade(now, 1250);
            trades.AddTrade(now, 1210);
            trades.AddTrade(now, 1220);
            trades.AddTrade(now, 1190);

            Assert.IsTrue(trades.GetDirection(new TimeSpan(0,3,0)) == PriceDirection.SomeWhatSteady);
        }

        [TestMethod]
        public void SoftCompare_Test()
        {
            TradeLogger trades = new TradeLogger();

            var now = DateTime.Now;

            trades.AddTrade(now.AddMinutes(-20), 850);
            trades.AddTrade(now.AddMinutes(-20).AddSeconds(20), 950);
            trades.AddTrade(now.AddMinutes(-15), 900);
            trades.AddTrade(now.AddMinutes(-15).AddSeconds(20), 1000);
            trades.AddTrade(now.AddMinutes(-10), 1000);
            trades.AddTrade(now.AddMinutes(-10).AddSeconds(20), 1080);
            trades.AddTrade(now.AddMinutes(-5), 1100);
            trades.AddTrade(now.AddMinutes(-5).AddSeconds(20), 1200);

            trades.AddTrade(now, 1200);
            trades.AddTrade(now, 1190);
            trades.AddTrade(now, 1180);
            trades.AddTrade(now, 1210);
            trades.AddTrade(now, 1250);
            trades.AddTrade(now, 1210);
            trades.AddTrade(now, 1220);
            trades.AddTrade(now, 1160);

            Assert.IsTrue(trades.SoftCompare(1160, 1150, 0.75) == CompareType.Equal);
            Assert.IsTrue(trades.SoftCompare(1160, 950, 0.75) == CompareType.LessThan);
            Assert.IsTrue(trades.SoftCompare(1160, 1170, 0.75) == CompareType.GreatherThan);
        }        
    }
}

