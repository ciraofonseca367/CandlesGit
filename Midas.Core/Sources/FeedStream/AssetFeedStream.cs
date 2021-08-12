using System;

using Midas.Core.Common;

namespace Midas.FeedStream
{
    public abstract class AssetFeedStream : IDisposable
    {
        public abstract Candle[] Read(int periods);

        public abstract int BufferCount();

        public abstract Candle Peek();

        protected double _initPrice;
        public virtual void InitPrice(double initialPrice)
        {
            _initPrice = initialPrice;
        }

        internal DateTime FromTimeStamp(double timeStamp)
        {
            System.DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc);
            dtDateTime = dtDateTime.AddMilliseconds(timeStamp);

            return dtDateTime;
        }

        public virtual void Dispose()
        {
        }

    }
}