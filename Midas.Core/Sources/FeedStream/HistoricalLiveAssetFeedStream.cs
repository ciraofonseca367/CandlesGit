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

namespace Midas.FeedStream
{
    public class HistoricalLiveAssetFeedStream : LiveAssetFeedStream
    {
        private TradeLogger _logger;
        private double _lastValue;

        private bool _running;

        public HistoricalLiveAssetFeedStream(BinanceWebSocket socket, string asset, CandleType streamCandleType, CandleType queryCandleType)
            : base(asset, streamCandleType, queryCandleType)
        {
            _state = MidasSocketState.Connected;
            _running = true;

            _logger = new TradeLogger();
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
            _running = false;
        }

        protected override void SocketRunner()
        {
            TraceAndLog.StaticLog("Historical", "Starting historical runner in 10s");

            Thread.Sleep(10000);

            Console.WriteLine(DateRange.ToString());

            var res = CandlesGateway.GetCandles(
                _asset,
                DateRange,
                base._queryCandleType
            );

            string lastDay = String.Empty;

            Candle[] candles;
            Candle previous = null;
            while ((candles = res.Read(10)) != null && _running)
            {
                foreach (var c in candles)
                {
                    var currentDay = c.PointInTime_Open.ToString("yyyy-MM-dd HH");

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

                        var diff = c.CloseValue - c.OpenValue;
                        var amountFactor = diff / 100;
                        var secondsFactor = c.CandleAge.TotalSeconds / 100;

                        Random r = new Random();
                        Thread.Sleep(r.Next(50,150));

                        _socketNew("Test", previous, seedCandle);

                        for(int i=1;i<=100;i++)
                        {
                            var nc = (Candle) seedCandle.Clone();
                            nc.CloseValue += i*amountFactor;

                            if(c.Direction == CandleDirection.Up)
                                nc.HighestValue = nc.CloseValue;
                            else
                                nc.LowestValue = nc.CloseValue;

                            nc.PointInTime_Close = seedCandle.PointInTime_Open.AddSeconds(i*secondsFactor);

                            _socketUpdate("test", "test", nc);
                        }
                    }

                    if(lastDay != currentDay)
                    {
                        lastDay = currentDay;
                        Console.WriteLine($"{base._asset}:{base._queryCandleType} - {lastDay}");
                    }

                    previous = c;                    
                }
            }

            _state = MidasSocketState.Closed;

        }

    }

}