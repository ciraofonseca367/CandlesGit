using System;
using System.Collections.Generic;
using System.Linq;
using Midas.Core.Common;
using Midas.Sources;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Midas.Core
{
    public class CandlesGateway
    {
        public static FeedStream.AssetFeedStream GetCandles(
            string asset,
            DateRange range,
            CandleType candleType)
        {
            AssetSource source = MapAssetToSource(asset, range);
            FeedStream.AssetFeedStream stream = null;
            stream = source.GetFeedStream(asset, range, candleType);
            return stream;
        }

        //We will create a generic AssetSource Later, instead of just FileSources
        private static AssetSource MapAssetToSource(string asset, DateRange range)
        {
            if(range.End == DateTime.MaxValue)
            {
                return new LiveAssetSource();
            }
            else
                return new FileAssetSource();
        }

        public static List<Candle> MapCandles(string dbConString, string asset, CandleType queryType,CandleType sourceType, DateRange range)
        {
            DateTime current = range.Start;
            DateRange currentRange = null;
            List<Candle> ret = new List<Candle>();

            MongoClient client = new MongoClient(dbConString);

            if(queryType == sourceType)
            {
                ret = GetCandlesFromDb(client, asset, sourceType);
            }
            else
            {
                current = Candle.GetValidMilestone(current, queryType);

                while(current < range.End)
                {
                    DateTime close = current.AddMinutes(Convert.ToInt32(queryType)-1);
                    close = close.AddSeconds(59);

                    currentRange = new DateRange(current,close);

                    var candles = GetCandlesFromDbMapping(currentRange, client, asset, sourceType); 
                    
                    var candle = Candle.Reduce(candles);
                    if(candle != null)
                        ret.Add(candle);

                    current = current.AddMinutes(Convert.ToInt32(queryType));
                }
            }

            return ret;
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