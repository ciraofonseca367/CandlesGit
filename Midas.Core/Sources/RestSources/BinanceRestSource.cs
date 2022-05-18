using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Midas.Core.Common;
using Newtonsoft.Json;

namespace Midas.Core.Sources.RestSources
{
    public class BinanceRest
    {
        private string _asset;
        private CandleType _candleType;

        private static HttpClient _client = new HttpClient();

        public BinanceRest(string asset, CandleType candleType)
        {
            _asset = asset;
            _candleType = candleType;
        }

        private string GetRawCandles(string symbol, string interval, long start, long end)
        {
            string formatUrl = "https://api.binance.com/api/v3/klines?symbol={0}&interval={1}&startTime={2}&endTime={3}";
            string url = String.Format(formatUrl,
                symbol, interval, start, end
            );            


            var dir = Directory.CreateDirectory("CandlesHistory");

            string ret;

            var file = $"CandlesHistory/symbol{symbol}_interval{interval}_startTime{start}_endTime{end}.txt";
            if(!File.Exists(file))
            {
                var responseTask = BinanceRest._client.GetStringAsync(url);
                if(responseTask.Wait(20000))
                {
                    ret = responseTask.Result;
                    File.WriteAllText(file, ret);
                }
                else
                    throw new TimeoutException("Timeout while waiting for candles");
            }
            else
            {
                ret = File.ReadAllText(file);
            }

            return ret;
        }

        public IEnumerable<Candle> GetCandles(DateRange range)
        {
            var daysIncrement = 1;

            DateTime current = range.Start;
            HttpClient client = new HttpClient();

            while(current < range.End)
            {
                var currentRange = new DateRange(current,current.AddMinutes(
                    Math.Min(daysIncrement*24*60, (range.End - current).TotalMinutes)
                    ));

                dynamic stuff = null;
                stuff = JsonConvert.DeserializeObject(GetRawCandles(
                    _asset,CandleTypeConverter.Convert(_candleType), currentRange.GetStartInMilliseconds(), currentRange.GetEndInMilliseconds()));

                foreach (dynamic row in stuff)
                {
                    Candle c = Candle.FromString(row);

                    yield return c;
                }

                current = current.AddMinutes((daysIncrement*24*60)+1);
            }

            client.Dispose();
        }
    }
}