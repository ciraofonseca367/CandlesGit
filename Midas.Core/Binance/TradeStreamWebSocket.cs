using System;
using Newtonsoft.Json;

namespace Midas.Core.Binance
{
    public delegate void NewTrade(TradeStreamItem item);

    public class TradeStreamWebSocket : GenericWebSocket
    {

        private NewTrade _newTrade;
        public void OnNewTrade(NewTrade newTraceHandler)
        {
            _newTrade += newTraceHandler;
        }        

        public TradeStreamWebSocket(string binanceUri, string asset, int timeout)
         : base(binanceUri, $"{asset.ToLower()}@trade", timeout)
        {
        }

        public override void OnData(string data)
        {
            if(_newTrade != null)
                _newTrade(TryParse(data));
        }

        public override void OnInfo(string info)
        {
            Console.WriteLine("Info: "+info);
        }
/*
{
  "e": "trade",     // Event type
  "E": 123456789,   // Event time
  "s": "BNBBTC",    // Symbol
  "t": 12345,       // Trade ID
  "p": "0.001",     // Price
  "q": "100",       // Quantity
  "b": 88,          // Buyer order ID
  "a": 50,          // Seller order ID
  "T": 123456785,   // Trade time
  "m": true,        // Is the buyer the market maker?
  "M": true         // Ignore
}


{"e":"trade","E":1634665431956,"s":"BTCUSDT","t":1105374158,"p":"63193.20000000","q":"0.00164000","b":7965633308,"a":7965633138,"T":1634665431955,"m":false,"M":true}
*/
        private TradeStreamItem TryParse(string buffer)
        {
            dynamic stuff = JsonConvert.DeserializeObject(buffer);

            var item = new TradeStreamItem();
            item.BuyerId = Convert.ToString(stuff["b"]);
            item.SellerId = Convert.ToString(stuff["a"]);
            item.Qty = Convert.ToDouble(stuff["q"]);
            item.Price = Convert.ToDouble(stuff["p"]);
            item.Symbol = Convert.ToString(stuff["s"]);
            
            return item;
        }

    }

    public class TradeStreamItem
    {
        public string BuyerId { get; internal set; }
        public string SellerId { get; internal set; }
        public double Qty { get; set; }
        public double Price { get;  set; }
        public string Symbol { get; internal set; }

        public string Id
        {
            get
            {
                return String.Concat(BuyerId, SellerId);
            }
        }

        public DateTime CreationDate { get; internal set; }

        public TradeStreamItem()
        {
            CreationDate = DateTime.Now;
        }

        public override string ToString()
        {
            return $"{BuyerId} - {SellerId} - Qdy:{Qty:0.0000} - Price:{Price:0.00} - Symbol:{Symbol}";
        }
    }
}