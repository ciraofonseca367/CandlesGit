using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using Midas.Core.Common;
using Newtonsoft.Json;

namespace Midas.Core.Sources.RestSources
{
    public class BinanceRest
    {
        private string _asset;
        private CandleType _candleType;

        public BinanceRest(string asset, CandleType candleType)
        {
            _asset = asset;
            _candleType = candleType;
        }        

        public IEnumerable<Candle> GetCandles(DateRange range)
        {
            string formatUrl = "https://api.binance.com/api/v3/klines?symbol={0}&interval={1}&startTime={2}&endTime={3}";

            var daysIncrement = 5;

            DateTime current = range.Start;

            while(current < range.End)
            {
                var currentRange = new DateRange(current,current.AddMinutes(
                    Math.Min(daysIncrement*24*60, (range.End - current).TotalMinutes)
                    ));

                string url = String.Format(formatUrl,
                    _asset, CandleTypeConverter.Convert(_candleType), currentRange.GetStartInMilliseconds(), currentRange.GetEndInMilliseconds()
                );

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";

                string response;
                using(WebResponse webResponse = request.GetResponse())
                {
                    Stream webStream = webResponse.GetResponseStream();
                    StreamReader responseReader = new StreamReader(webStream);
                    response = responseReader.ReadToEnd();
                    responseReader.Close();
                }

                dynamic stuff = JsonConvert.DeserializeObject(response);

                List<Candle> candles = new List<Candle>(250);
                foreach (dynamic row in stuff)
                {
                    Candle c = Candle.FromString(row);

                    yield return c;
                }

                current = current.AddMinutes((daysIncrement*24*60)+1);
            }
        }
    }
}