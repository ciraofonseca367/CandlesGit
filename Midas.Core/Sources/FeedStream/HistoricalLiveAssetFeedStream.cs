using System;
using System.Collections.Generic;
using Midas.Core.Common;
using System.Linq;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using Newtonsoft.Json;
using System.Text;
using System.Collections.Concurrent;
using Midas.Sources;
using Midas.Core.Trade;
using Midas.Core;
using Midas.Core.Util;
using System.Threading.Tasks;

namespace Midas.FeedStream
{
    public class HistoricalLiveAssetFeedStream : LiveAssetFeedStream
    {
        private TradeLogger _logger;
        private double _lastValue;

        public HistoricalLiveAssetFeedStream(BinanceWebSocket socket, string asset, CandleType streamCandleType, CandleType queryCandleType)
            : base(asset, streamCandleType, queryCandleType)
        {
            _state = MidasSocketState.Connected;

            _logger = new TradeLogger();

            Console.WriteLine("Stream: HistoricalLiveAssetStream");
        }

        public override void InitPrice(double initialPrice)
        {
            base.InitPrice(initialPrice);
            _lastValue = base._initPrice;
        }
        public override int BufferCount()
        {
            throw new NotImplementedException();
        }

        public override void OpenSocket(string asset)
        {
            //Do nothing
        }

        public DateRange DateRange
        {
            get;
            set;
        }

        public override Candle ParseCandle(string buffer)
        {
            throw new NotImplementedException();
        }

        public override Candle Peek()
        {
            throw new NotImplementedException();
        }

        public override Candle[] Read(int periods)
        {
            throw new NotImplementedException();
        }

        public override void Close(bool fromGC)
        {
            base.Close(fromGC);
        }

        public override void Dispose()
        {
            base.Dispose();
        }

        protected override async Task SocketRunner()
        {
            TraceAndLog.StaticLog("Historical", "Starting historical runner in 10s");

            Thread.Sleep(1000);

            var res = CandlesGateway.GetCandlesFromRest(
                _asset,
                base._queryCandleType,
                DateRange
            );

            string lastDay = String.Empty;

            var firstDay = res.First();

            Candle previous = null;
            foreach (var c in res)
            {
                var currentDay = c.PointInTime_Open.ToString("yyyy-MM-dd HH");

                var hours = (c.PointInTime_Open - firstDay.PointInTime_Open).TotalHours;

                if (previous != null)
                {
                    var seedCandle = new Candle()
                    {
                        PointInTime_Open = c.PointInTime_Open,
                        PointInTime_Close = c.PointInTime_Open,
                        OpenValue = c.OpenValue,
                        CloseValue = c.OpenValue,
                        LowestValue = c.OpenValue,
                        HighestValue = c.OpenValue
                    };

                    double diff;

                    if(c.Direction == CandleDirection.Up)
                    {
                        diff = c.HighestValue - c.LowestValue;
                        seedCandle.CloseValue = c.LowestValue;
                    }
                    else
                    {
                        diff = c.LowestValue - c.HighestValue;
                        seedCandle.CloseValue = c.HighestValue;
                    }

                    int factor = 25;

                    var amountFactor = diff / factor;
                    var secondsFactor = c.CandleAge.TotalSeconds / factor;

                    await _socketNew("Test", previous, seedCandle);

                    for(int i=1;i<=factor;i++)
                    {
                        var nc = (Candle) seedCandle.Clone();
                        nc.CloseValue += i*amountFactor;

                        if(c.Direction == CandleDirection.Up)
                            nc.HighestValue = nc.CloseValue;
                        else
                            nc.LowestValue = nc.CloseValue;

                        DateTime close = seedCandle.PointInTime_Open.AddSeconds(i*secondsFactor);
                        close = new DateTime(close.Year,close.Month,close.Day,close.Hour,close.Minute,0);

                        nc.PointInTime_Close = close;

                        await _socketUpdate("test", "test", nc);
                    }
                }

                if(lastDay != currentDay)
                {
                    lastDay = currentDay;
                    Console.WriteLine($"{base._asset}:{base._queryCandleType} - {lastDay}");
                }

                previous = c;
            }

            Console.WriteLine("End of stream...");
            await _socketEnd("Fim","Fim");

            _state = MidasSocketState.Closed;

        }

    }

}