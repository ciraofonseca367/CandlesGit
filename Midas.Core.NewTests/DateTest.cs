using System;
using System.Dynamic;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Midas.Core.Common;
using Midas.Core.Trade;
using Midas.Trading;

namespace Midas.Core.NewTests
{
    [TestClass]
    public class DateTest
    {
        [TestMethod]
        public void MongoDbDateTest()
        {
            string conString = "mongodb+srv://admin:cI.(00.#ADM@midasstaging.yi35b.mongodb.net/CandlesFacesStaging?retryWrites=true&w=majority";

            Candle c = new Candle();
            c.OpenTime = Candle.FromTimeStamp(Convert.ToDouble("1499040000000"));
            c.OpenValue = 1000;
            c.HighestValue = 1000;
            c.LowestValue = 1000;
            c.CloseValue = 10000;
            c.Volume = 10;
            c.CloseTime = Candle.FromTimeStamp(Convert.ToDouble("1499644799999"));

            c.SaveOrUpdate(conString, "TestDates");

            var newCandle = Candle.LoadFromDb(conString, "TestDates", c.PointInTime_Open);


            Assert.IsTrue(newCandle != null);
        }      
    }
}

