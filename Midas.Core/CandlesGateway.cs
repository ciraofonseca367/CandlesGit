using System;
using Midas.Core.Common;
using Midas.Sources;

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
    }
}