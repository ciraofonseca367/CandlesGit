using System;
using System.Collections.Generic;
using System.Linq;
using Midas.Core.Common;

namespace Midas.Core.Trade
{
    public class TradeAggregation
    {
        private List<Trade> _trades;
        public TradeAggregation()
        {
            _trades = new List<Trade>();
        }

        public void AddTrade(Trade d)
        {
            _trades.Add(d);
        }

        public Candle GetCandle()
        {
            if(_trades.Count > 0)
            {
                var range = Get5MinuteRange(_trades.First().Date);
                return new Candle()
                {
                    OpenValue = _trades[0].Amount,
                    CloseValue = _trades[_trades.Count-1].Amount,
                    PointInTime_Open = range.Item1,
                    PointInTime_Close = range.Item2,
                    LowestValue = _trades.Min(t => t.Amount),
                    HighestValue = _trades.Max(t => t.Amount),
                    Volume = _trades.Count
                };
            }
            else
                throw new ArgumentException("Trades array is empty");
        }

        public static Tuple<DateTime,DateTime, string> Get5MinuteRange(DateTime time)
        {
            int minMinute = GetMin5MinuteFrame(time.Minute);
            DateTime minDate = new DateTime(time.Year,time.Month, time.Day, time.Hour, minMinute,0);
            DateTime maxDate = minDate.Add(new TimeSpan(0,4,59));
            string hash = minDate.ToString("yyyyMMddhhmmss");

            return new Tuple<DateTime,DateTime, string>(
                minDate,
                maxDate,
                hash
            );
        }

        private static int GetMin5MinuteFrame(double minute)
        {
            int min = 0;

            if (minute / 5 <= 1)
                min = 0;
            else if (minute / 10 <= 1)
                min = 5;
            else if (minute / 15 <= 1)
                min = 5;
            else if (minute / 20 <= 1)
                min =10;
            else if (minute / 25 <= 1)
                min = 15;
            else if (minute / 30 <= 1)
                min = 20;
            else if (minute / 35 <= 1)
                min = 25;
            else if (minute / 40 <= 1)
                min = 35;
            else if (minute / 45 <= 1)
                min = 40;
            else if (minute / 50 <= 1)
                min = 45;
            else if (minute / 55 <= 1)
                min = 50;
            else if (minute / 60 <= 1)
                min = 55;

            return min;
        }        
    }

    public class TradeLogger
    {
        public Dictionary<string, TradeAggregation> _trades;

        public TradeLogger()
        {
            _trades = new Dictionary<string, TradeAggregation>(11);
        }

        public void AddTrade(DateTime time, double amount)
        {
            AddTrade(new Trade()
            {
                Amount = amount,
                Date = time
            });
        }

        public void AddTrade(Trade d)
        {
            TradeAggregation current = null;
            var range = TradeAggregation.Get5MinuteRange(d.Date);

            _trades.TryGetValue(range.Item3, out current);
            if(current == null)
            {
                current = new TradeAggregation();
                _trades.Add(range.Item3, current);
            }

            current.AddTrade(d);
        }

        public Candle GetCurrent()
        {
            var lastKey = _trades.Keys.OrderBy(k => k).Last();

            return _trades[lastKey].GetCandle();
        }
    }

    public class Trade
    {
        public double Amount
        {
            get;
            set;
        }

        public DateTime Date
        {
            get;
            set;
        }
    }
}