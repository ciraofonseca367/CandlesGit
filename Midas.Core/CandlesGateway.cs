using System;
using System.Collections.Generic;
using System.Linq;
using Midas.Core.Common;
using Midas.Core.Sources.RestSources;
using Midas.Sources;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Midas.Core
{
    public class CandlesGateway
    {
        public static FeedStream.AssetFeedStream GetCandlesFromFile(
            string asset,
            DateRange range,
            CandleType candleType)
        {
            AssetSource source = new FileAssetSource();
            FeedStream.AssetFeedStream stream = null;
            stream = source.GetFeedStream(asset, range, candleType);
            return stream;

        }

        public static IEnumerable<Candle> GetCandlesFromRest(string asset, CandleType queryType, DateRange range)
        {
            BinanceRest restCandles = new BinanceRest(asset, queryType);
            var ret = restCandles.GetCandles(range);

            return ret;
        }

        public static List<Candle> MapCandles(string dbConString, string asset, CandleType queryType, CandleType sourceType, DateRange range)
        {
            DateTime current = range.Start;
            DateRange currentRange = null;
            List<Candle> ret = new List<Candle>();

            MongoClient client = new MongoClient(dbConString);

            if (queryType == sourceType)
            {
                BinanceRest restCandles = new BinanceRest(asset, queryType);
                ret = restCandles.GetCandles(range).ToList();
            }
            else
            {
                current = Candle.GetValidMilestone(current, queryType);

                BinanceRest restCandles = new BinanceRest(asset, queryType);
                var cache = restCandles.GetCandles(range).ToList();

                while (current < range.End)
                {
                    DateTime close = current.AddMinutes(Convert.ToInt32(queryType) - 1);
                    close = close.AddSeconds(59);

                    currentRange = new DateRange(current, close);

                    var candles = GetCandlesFromMemory(currentRange, cache);
                    if (candles.Count == 0)
                        candles = GetCandlesFromDbMapping(currentRange, client, asset, sourceType);

                    if (candles.Count > 0)
                    {
                        var candle = Candle.Reduce(candles);
                        if (candle != null)
                            ret.Add(candle);
                    }

                    current = current.AddMinutes(Convert.ToInt32(queryType));
                }
            }

            return ret;
        }

        private static List<Candle> GetCandlesFromMemory(DateRange range, List<Candle> candles)
        {
            return candles.Where(c => range.IsInside(c.OpenTime)).ToList();
        }

        private static List<Candle> GetCandlesFromDbMapping(DateRange range, MongoClient client, string asset, CandleType type)
        {
            var database = client.GetDatabase("CandlesFaces");
            var dbCol = database.GetCollection<Candle>(
                String.Format("Klines_{0}_{1}", asset.ToUpper(), type.ToString())
            );

            var filterBuilder1 = Builders<Candle>.Filter;
            var filterDefinition = new List<FilterDefinition<Candle>>();
            filterDefinition.Add(filterBuilder1.Gte(item => item.OpenTime, range.Start));
            filterDefinition.Add(filterBuilder1.Lt(item => item.OpenTime, range.End));

            var filter = filterBuilder1.And(filterDefinition.ToArray());

            var query = dbCol.Find(filter).ToList();

            return query;
        }

        private static List<Candle> GetCandlesFromDb(MongoClient client, string asset, CandleType type)
        {
            var database = client.GetDatabase("CandlesFaces");
            var dbCol = database.GetCollection<Candle>(
                String.Format("Klines_{0}_{1}", asset.ToUpper(), type.ToString())
            );

            var itens = dbCol.Find(new BsonDocument()).ToList();

            //Filter only the candles for our windowsize
            var lastXCandles = itens
            .Where(i => i.CloseTime > DateTime.UtcNow.AddMinutes(230 * Convert.ToInt32(type) * -1))
            .OrderBy(i => i.OpenTime);

            return lastXCandles.ToList();
        }


    }
}