using System;
using Midas.Core.Common;
using Midas.FeedStream;

namespace Midas.Sources
{
    public abstract class AssetSource
    {
        public void Dispose()
        {
            
        }

        public abstract AssetFeedStream GetFeedStream(
            string asset,
            DateRange range,
            CandleType type
        );
    }
}