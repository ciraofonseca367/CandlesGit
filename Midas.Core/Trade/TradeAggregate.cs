using System;
using System.Collections.Generic;
using System.Linq;
using Midas.Core.Common;
using Midas.Util;

namespace Midas.Core.Trade
{
    public class TradeAggregation
    {
        private Trade _first;
        private Trade _last;
        private Trade _highest;
        private Trade _lowest;
        private double _volume;

        private CandleType _candleType;

        public TradeAggregation(CandleType candleType)
        {
            _first = Trade.GetNull();
            _last = Trade.GetNull();
            _highest = Trade.GetNull();
            _highest.Amount = Double.MinValue;
            _lowest = Trade.GetNull();
            _lowest.Amount = Double.MaxValue;
            _volume = 0;
            _candleType = candleType;
        }

        public void AddTrade(Trade d)
        {
            if(d.Quantity == 0)
                d.Quantity = 1;

            _volume += d.Quantity;
            _last = d;
            if(_first.IsNull)
                _first = d;

            if(_highest.Amount <= d.Amount)
                _highest.Amount = d.Amount;

            if(_lowest.Amount >= d.Amount)
                _lowest.Amount = d.Amount;
        }


        public Candle GetCandle()
        {
            var range = GetRange(_first.Date);
            return new Candle()
            {
                OpenValue = _first.Amount,
                CloseValue = _last.Amount,
                PointInTime_Open = range.Item1,
                PointInTime_Close = range.Item2,
                LowestValue = _lowest.Amount,
                HighestValue = _highest.Amount,
                Volume = _volume
            };
        }
        private Tuple<DateTime,DateTime, string> GetRange(DateTime now)
        {
            return GetRange(now, _candleType);
        }

        public static Tuple<DateTime,DateTime, string> GetRange(DateTime now, CandleType candleType)
        {
            var candleMinutes = Convert.ToDouble(candleType);
            var span = (now - DateTime.MinValue);

            double div = Convert.ToDouble(Convert.ToInt32(span.TotalMinutes)) / candleMinutes;
            double remainder = div - Convert.ToInt32(div);

            DateTime minDate;
            DateTime maxDate;

            if(remainder == 0)
            {
                minDate = now.AddMinutes(candleMinutes*-1);
                maxDate = now.AddSeconds(-1);
            }
            else
            {
                minDate = now.AddMinutes(Convert.ToInt32(remainder * candleMinutes) * -1).AddMinutes(1);
                maxDate = minDate.AddSeconds((candleMinutes*60)-1);
            }

            minDate = new DateTime(minDate.Year,minDate.Month, minDate.Day, minDate.Hour, minDate.Minute,0,DateTimeKind.Utc);
            maxDate = new DateTime(maxDate.Year,maxDate.Month, maxDate.Day, maxDate.Hour, maxDate.Minute,0,DateTimeKind.Utc);

            string hash = minDate.ToString("yyyyMMddhhmmss");

            return new Tuple<DateTime,DateTime, string>(
                minDate,
                maxDate,
                hash
            );            
        }  
    }

    public class TradeLogger
    {
        private Dictionary<string, TradeAggregation> _trades;
        private TradeAggregation _last;
        private Trade _lastTrade;

        private FixedSizedQueue<Trade> _pureTrades;

        private CandleType _candleType;

        public TradeLogger(CandleType candleType)
        {
            _trades = new Dictionary<string, TradeAggregation>(11);
            _pureTrades = new FixedSizedQueue<Trade>(3000);
            _candleType = candleType;
        }

        public TradeLogger() : this(CandleType.MIN5)
        {
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
            var range = TradeAggregation.GetRange(d.Date,_candleType);

            _trades.TryGetValue(range.Item3, out current);
            if(current == null)
            {
                current = new TradeAggregation(_candleType);
                _trades.Add(range.Item3, current);
            }

            _last = current;
            _lastTrade = d;

            current.AddTrade(d);
            _pureTrades.Enqueue(d);
        }

        public Candle GetCurrent()
        {
            return _last.GetCandle();
        }

        public double GetAtr()
        {
            var buffer = this._trades.Values
            .OrderBy(v => v.GetCandle().OpenTime)
            .Take(30)
            .Select(i => i.GetCandle());

            List<double> atrs = new List<double>();
            double atr = 0;

            Candle p = null;
            if (buffer.Count() > 1)
            {
                foreach (Candle c in buffer)
                {
                    if (p != null)
                    {
                        double diff1 = Math.Abs(c.HighestValue - p.LowestValue);
                        double diff2 = Math.Abs(c.HighestValue - p.CloseValue);
                        double diff3 = Math.Abs(c.LowestValue - p.CloseValue);

                        var numbers = new List<double>() { diff1, diff2, diff3 };

                        atrs.Add(numbers.Max());
                    }

                    p = c;
                }

                atr = atrs.Average();
            }

            return atr;      
        }

        public double GetMovingAverage(CandleType type, int periods)
        {
            var candles = ConvertGrouping(type);
            double avg = 0;
            
            if(candles.Count > 0)
                avg = candles
                .Take(periods)
                .Average(c => c.CloseValue);

            return avg;
        }

        public bool IsStable(CandleType type)
        {
            return ConvertGrouping(type).Count > 0;
        }

        public List<Candle> ConvertGrouping(CandleType type)
        {
            CandleType rootCandle = CandleType.MIN5;
            int ratio = Convert.ToInt32(type) / Convert.ToInt32(rootCandle);
            List<Candle> buffer = new List<Candle>(ratio);
            var grouped = new List<Candle>();
            

            var allCandles = this._trades.Values;
            if(allCandles.Count() > 0)
            {
                var candles = allCandles
                .OrderByDescending(v => v.GetCandle().OpenTime)
                .Select(i => i.GetCandle());

                foreach(var c in candles)
                {
                    if(ratio == 1)
                        grouped.Add(c);
                    else
                    {
                        buffer.Add(c);
                        if (buffer.Count == ratio)
                        {
                            grouped.Add(Candle.Reduce(buffer));
                            buffer.Clear();
                        }
                    }
                }
            }

            return grouped;
        }

        public PriceDirection GetDirection(TimeSpan comparePeriod, float ratrPercentage = 0.05f)
        {
            var rawBuffer = _pureTrades.GetList();
            var buffer = rawBuffer
            .Where(t => t.Date > rawBuffer.Last().Date.AddSeconds(comparePeriod.TotalSeconds*-1))
            .OrderBy(t => t.Date);

            var currentPrice = buffer.First().Amount;
            var atr = GetAtr();
            var ratr = atr / currentPrice;

            var equalRange = ratr * ratrPercentage;

            var minBand = currentPrice * (1-equalRange);
            var maxBand = currentPrice * (1+equalRange);

            var firstPrice = buffer.First().Amount;
            var lastPrice = buffer.Last().Amount;
            var avgPrice = buffer.Average(p => p.Amount);

            PriceDirection result = PriceDirection.None;
            if(avgPrice <= maxBand && avgPrice >= minBand)
                result = PriceDirection.SomeWhatSteady;
            
            if(lastPrice > avgPrice && lastPrice > maxBand)
                result = PriceDirection.GoingUp;
            
            if(lastPrice < avgPrice && lastPrice < minBand)
                result = PriceDirection.GoingDown;

            return result;
        }

        public CompareType SoftCompare(double amountA, double amountB, double softnessRatio)
        {
            var currentPrice = _lastTrade.Amount;
            var atr = GetAtr();
            var ratr = atr / currentPrice;

            var ratrSoft = ratr * softnessRatio;

            double softA = amountA * (1-ratrSoft);

            CompareType res = CompareType.Equal;
            if(amountB >= amountA)
                res = CompareType.GreatherThan;

            if(amountB < softA)
                res = CompareType.LessThan;
            
            return res;
        }
    }

    public enum PriceDirection
    {
        GoingUp,
        GoingDown,
        SomeWhatSteady,
        None
    }

    public enum CompareType
    {
        Equal,
        GreatherThan,
        LessThan
    }

    public class Trade
    {
        public double Amount
        {
            get;
            set;
        }
        public double Quantity
        {
            get;
            set;
        }

        public DateTime Date
        {
            get;
            set;
        }

        public static Trade GetNull()
        {
            return new Trade()
            {
                Amount = 0,
                Quantity = 1,
                Date = DateTime.MinValue
            };
        }

        public bool IsNull
        {
            get
            {
                return Amount == 0 && Date == DateTime.MinValue;
            }
        }
    }
}