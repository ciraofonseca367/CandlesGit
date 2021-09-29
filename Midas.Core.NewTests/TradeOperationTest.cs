using System;
using System.Dynamic;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Midas.Trading;

namespace Midas.Core.NewTests
{
    [TestClass]
    public class TradeOperationTest
    {
        [TestMethod]
        public void TradeOperation_Persist()
        {
            dynamic config = new ExpandoObject();

            string apiKey = "hUKcnDYlH154l6vYjWm5MYirJoaVLhXyEkyc3sMhaI2dQZh6EnxoH050alC3J6ax";
            string apiSecret = "5yTc5tiMDelKsJhMoz9oxTEeISPv0tLEi6g8aOie6UoPWxhKsYwJuWWT4FjPVMgj";
            string endPoint = "https://testnet.binance.vision";

            config.ApiKey = apiKey;
            config.ApiSecret = apiSecret;
            config.EndPoint = endPoint;
            config.Host = "testnet.binance.vision";

            // TradeOperation op = new TradeOperation(null, 0.001, 0.01,0.05, DateTime.Now.AddMinutes(60),
            // "mongodb+srv://admin:cI.(00.#ADM@midasstaging.yi35b.mongodb.net/CandlesFacesStaging?retryWrites=true&w=majority",
            // config, "BTCBUSD", Common.CandleType.MIN15
            // );

            TradeOperation op = null;

            op.OnCandleUpdateAsync(new Common.Candle()
            {
                AmountValue = 30000,
                CloseValue = 30500,
                PointInTime_Open = DateTime.Now,
                PointInTime_Close = DateTime.Now.AddMinutes(5)
            });

            Thread.Sleep(10);
            op.Enter(30500, DateTime.Now,10);

            Thread.Sleep(10);

            op.OnCandleUpdateAsync(new Common.Candle()
            {
                AmountValue = 20000,
                CloseValue = 20000,                
                PointInTime_Open = DateTime.Now,
                PointInTime_Close = DateTime.Now.AddMinutes(5)
            });


            op.Persist();
        }
    }
}

