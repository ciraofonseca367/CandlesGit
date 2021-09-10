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

namespace Midas.FeedStream
{
    public class TestLiveAssetFeedStream : LiveAssetFeedStream
    {
        private List<TestPoint> _timeline;
        private TradeLogger _logger;
        private double _lastValue;

        private bool _running;

        public TestLiveAssetFeedStream(BinanceWebSocket socket, string asset, CandleType streamCandleType, CandleType queryCandleType)
            : base(asset, streamCandleType, queryCandleType)
        {
            _state = MidasSocketState.Connected;
            _running = true;

            _timeline = new List<TestPoint>()
            {
                new TestPoint() { WaitDuration = new TimeSpan(0,0,1), Variation = 0.25, Volume = 1},
                new TestPoint() { WaitDuration = new TimeSpan(0,0,30), Variation = 0.3, Volume = 1},
                new TestPoint() { WaitDuration = new TimeSpan(0,0,30), Variation = 0.6, Volume = 1},
                new TestPoint() { WaitDuration = new TimeSpan(0,2,0), Variation = 0.2, Volume = 1},
                new TestPoint() { WaitDuration = new TimeSpan(0,2,0), Variation = 0.4, Volume = 1},
                new TestPoint() { WaitDuration = new TimeSpan(0,2,0), Variation = 0.7, Volume = 1},
                new TestPoint() { WaitDuration = new TimeSpan(0,2,0), Variation = -0.3, Volume = 1},
                new TestPoint() { WaitDuration = new TimeSpan(0,2,0), Variation = -0.1, Volume = 1},
                new TestPoint() { WaitDuration = new TimeSpan(0,2,0), Variation = 0.3, Volume = 1},
                new TestPoint() { WaitDuration = new TimeSpan(0,2,0), Variation = 0.3, Volume = 1},
            };

            Console.WriteLine("Stream: TestLiveAssetStream");

            _logger = new TradeLogger(base._queryCandleType);
        }

        public override void InitPrice(double initialPrice)
        {
            base.InitPrice(initialPrice);
            _lastValue = base._initPrice;
        }


        private void Expand()
        {
            var utcNow = DateTime.UtcNow;
            DateTime now = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, utcNow.Hour, utcNow.Minute, utcNow.Second, DateTimeKind.Utc);
            
            DateTime start = now;
            
            double acumVariation = 0;

            _timeline[0].Previous = new TestPoint()
            {
                WaitDuration = new TimeSpan(0,0,0),
                Variation = acumVariation,
                Volume = 0,
                Range = null,
                Previous = null,
                AcumVariation = 0
            };

            _timeline[0].Range = new DateRange(start, start.Add(_timeline[1].WaitDuration));
            
            DateTime lastDate = start;
            for(int i = 1;i< _timeline.Count;i++)
            {
                var c = _timeline[i];

                acumVariation += c.Variation;
                c.AcumVariation = acumVariation;
                c.Range = new DateRange(lastDate, lastDate.Add(c.WaitDuration));
                c.Previous = _timeline[i-1];

                lastDate = c.Range.End;
            }
        }


        private Candle HeartBeat()
        {
            var utcNow = DateTime.UtcNow;
            DateTime now = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, utcNow.Hour, utcNow.Minute, utcNow.Second, DateTimeKind.Utc);

            Random r = new Random();

            var point = _timeline.Where(p => p.Range.IsInside(now)).FirstOrDefault();

            var previousValue = _initPrice * (1+(point.Previous.AcumVariation/100));
            var currentValue = _initPrice * (1+(point.AcumVariation/100));

            var diff = currentValue - previousValue;

            var rand = diff * r.NextDouble();

            var transactionAmount = previousValue + rand;

            _logger.AddTrade(now, transactionAmount);

            return _logger.GetCurrent();
        }

        public override int BufferCount()
        {
            throw new NotImplementedException();
        }

        public override void OpenSocket(string asset)
        {
            //Do nothing
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
            Thread.Sleep(5000);

            Expand();
            
            Candle bufferCandle = null;
            Candle lastCandle = null;

            while (_running)
            {
                bufferCandle = HeartBeat();

                if (_socketInfo != null)
                    _socketInfo("testAsset", bufferCandle.ToString());

                if (_socketUpdate != null)
                    _socketUpdate("testAsset","Test", bufferCandle);

                //We've just changed candle, thus, we need to add the lastCandle to the internal buffer
                if (lastCandle == null || bufferCandle.OpenTime > lastCandle.OpenTime)
                {
                    if (lastCandle == null)
                        lastCandle = bufferCandle;

                    if (_socketNew != null)
                        _socketNew("testAsset",lastCandle, bufferCandle);
                }

                lastCandle = bufferCandle;

                Thread.Sleep(500);
            }

            _state = MidasSocketState.Closed;

        }

    }

    public class TestPoint
    {
        public TimeSpan WaitDuration
        {
            get;
            set;
        }

        public double Variation
        {
            get;
            set;
        }

        public double Volume
        {
            get;
            set;
        }

        public DateRange Range
        {
            get;
            set;
        }

        public TestPoint Previous
        {
            get;
            set;
        }
        public double AcumVariation { get; internal set; }
    }

}