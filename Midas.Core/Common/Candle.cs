using System;
using System.Collections.Generic;
using System.Linq;
using Midas.Trading;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Midas.Core.Common
{
    public interface IStockPointInTime
    {
        public DateTime PointInTime
        {
            get;
            set;
        }

        public DateTime PointInTime_Open
        {
            get;
            set;
        }

        public DateTime PointInTime_Close
        {
            get;
            set;
        }

        public double AmountValue
        {
            get;
            set;
        }

        public double OpenValue
        {
            get;
        }

        public double CloseValue
        {
            get;
        }

        public double HighestValue
        {
            get;
        }

        public double LowestValue
        {
            get;
        }

        public double Volume
        {
            get;
            set;
        }

        public int Periodo
        {
            get;
            set;
        }

        public CandleDirection Direction
        {
            get;
            set;
        }

        public IStockPointInTime Clone();
    }

    public class Candle : IStockPointInTime
    {

        private MongoClient _mongoClient;

        public Candle()
        {
            _id = new BsonObjectId(ObjectId.GenerateNewId(DateTime.Now));
            _mongoClient = null;
        }

        public IStockPointInTime Clone()
        {
            return new Candle()
            {
                PointInTime_Open = this.PointInTime_Open,
                PointInTime_Close = this.PointInTime_Close,
                OpenValue = this.OpenValue,
                CloseValue = this.CloseValue,
                HighestValue = this.HighestValue,
                LowestValue = this.LowestValue,
                Volume = this.Volume
            };
        }

        public DateTime OpenTime;
        public double OpenValue;
        public double CloseValue;
        public double LowestValue;
        public double HighestValue;
        public double Volume
        {
            get;
            set;
        }

        public DateTime CloseTime;

        public object _id
        {
            get;
            set;
        }

        public DateTime PointInTime
        {
            get
            {
                return OpenTime;
            }
            set
            {
                OpenTime = value;
            }
        }

        public DateTime PointInTime_Open
        {
            get
            {
                return OpenTime;
            }
            set
            {
                OpenTime = value;
            }
        }

        public DateTime PointInTime_Close
        {
            get
            {
                return CloseTime;
            }
            set
            {
                CloseTime = value;
            }
        }

        public double AmountValue
        {
            get
            {
                return OpenValue;
            }
            set
            {
                OpenValue = value;
            }
        }

        double IStockPointInTime.OpenValue
        {
            get
            {
                return OpenValue;
            }
        }

        double IStockPointInTime.CloseValue
        {
            get
            {
                return CloseValue;
            }
        }


        //Coment do commit
        double IStockPointInTime.HighestValue
        {
            get
            {
                return HighestValue;
            }
        }

        double IStockPointInTime.LowestValue
        {
            get
            {
                return LowestValue;
            }
        }

        public CandleDirection Direction
        {
            get
            {
                return (OpenValue > CloseValue ? CandleDirection.Down : CandleDirection.Up);
            }
            set
            {

            }
        }

        public int Periodo
        {
            get;
            set;
        }


        public static Candle FromString(dynamic jsonCandle)
        {
            Candle c = new Candle();
            c.OpenTime = FromTimeStamp(Convert.ToDouble(jsonCandle[0].Value));
            c.OpenValue = Convert.ToDouble(jsonCandle[1].Value);
            c.HighestValue = Convert.ToDouble(jsonCandle[2].Value);
            c.LowestValue = Convert.ToDouble(jsonCandle[3].Value);
            c.CloseValue = Convert.ToDouble(jsonCandle[4].Value);
            c.Volume = Convert.ToDouble(jsonCandle[5].Value);
            c.CloseTime = FromTimeStamp(jsonCandle[6].Value);

            /*
                        [
            [
                1499040000000,      // Open time
                "0.01634790",       // Open
                "0.80000000",       // High
                "0.01575800",       // Low
                "0.01577100",       // Close
                "148976.11427815",  // Volume
                1499644799999,      // Close time
                "2434.19055334",    // Quote asset volume
                308,                // Number of trades
                "1756.87402397",    // Taker buy base asset volume
                "28.46694368",      // Taker buy quote asset volume
                "17928899.62484339" // Ignore.
            ]
            ]*/

            return c;
        }

        internal void Replace(Candle tmpCandle)
        {
            this.OpenTime = tmpCandle.OpenTime;
            this.CloseTime = tmpCandle.CloseTime;
            this.LowestValue = tmpCandle.LowestValue;
            this.AmountValue = tmpCandle.AmountValue;
            this.CloseValue = tmpCandle.CloseValue;
            this.HighestValue = tmpCandle.HighestValue;
            this.Volume = tmpCandle.Volume;
        }

        public static DateTime FromTimeStamp(double timeStamp)
        {
            System.DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc);
            dtDateTime = dtDateTime.AddMilliseconds(timeStamp);

            return dtDateTime;
        }

        public static Candle Reduce(List<Candle> candles)
        {
            Candle reducedCandle = null;
            if (candles.Count > 0)
            {
                foreach (var c in candles)
                {
                    if (reducedCandle == null)
                        reducedCandle = c;
                    else
                    {
                        if (reducedCandle.LowestValue > c.LowestValue)
                            reducedCandle.LowestValue = c.LowestValue;

                        if (reducedCandle.HighestValue < c.HighestValue)
                            reducedCandle.HighestValue = c.HighestValue;

                        reducedCandle.Volume += c.Volume;
                    }
                }

                reducedCandle.OpenValue = candles[0].OpenValue;
                reducedCandle.CloseValue = candles[candles.Count - 1].CloseValue;
                reducedCandle.OpenTime = candles.Min(c => c.OpenTime).ToUniversalTime();
                reducedCandle.CloseTime = candles.Max(c => c.CloseTime).ToUniversalTime();
            }

            return reducedCandle;
        }

        public override string ToString()
        {
            return String.Format("OpenTime: {0:dd/MM/yyyy HH:mm}, LastAmount: ${1:0.00}, Volume: {2:0.00}", this.OpenTime, this.CloseValue, this.Volume);
        }

        public string GetCompareStamp()
        {
            return OpenTime.ToString("yyyy-MM-dd HH:mm");
        }

        public bool Compare(Candle compareCandle)
        {
            return this.GetCompareStamp() == compareCandle.GetCompareStamp();
        }

        public double GetPureIndecisionThreshold()
        {
            var bodySize = CloseValue - OpenValue;
            var candleSize = HighestValue - LowestValue;
            var ratio = bodySize / candleSize;

            return ratio;
        }

        public TimeSpan CandleAge
        {
            get
            {
                return this.CloseTime - this.OpenTime;
            }
        }

        public int GetIndecisionThreshold()
        {
            var bodySize = CloseValue - OpenValue;
            var candleSize = HighestValue - LowestValue;
            var ratio = bodySize / candleSize;
            int threshould;

            var candleAge = DateTime.UtcNow - this.OpenTime;

            if (ratio > -0.1 && ratio < 0.1)
                threshould = 0;
            else if (ratio >= 0.1 && this.Direction == CandleDirection.Up)
                threshould = 1;
            else
                threshould = -1;

            /*Descomentar para testar alta
            threshould = 1;
            */

            return threshould;
        }

        public double GetCurrentValue()
        {
            return (Direction == CandleDirection.Up ? LowestValue : HighestValue);
        }

        public static bool IsMilestone(DateTime dateTime, CandleType type)
        {
            TimeSpan span = new TimeSpan(dateTime.Hour, dateTime.Minute, 0);

            int mod = Convert.ToInt32(span.TotalMinutes);
            if (span.TotalMinutes > 0)
            {
                var totalMinutes = span.TotalMinutes;

                var div = Convert.ToInt32(totalMinutes) % Convert.ToInt32(type);
            }

            return mod == 0;
        }

        public void SaveOrUpdate(string conString, string patternName)
        {
            if (_mongoClient == null)
                _mongoClient = new MongoClient(conString);

            var database = _mongoClient.GetDatabase("CandlesFaces");

            var dbCol = database.GetCollection<Candle>(patternName);

            var res = dbCol.Find(item => item.OpenTime == this.PointInTime_Open);
            if (res.CountDocuments() > 0)
                this._id = res.First()._id;

            var result = dbCol.ReplaceOne(
                item => item.PointInTime_Open == this.PointInTime_Open,
                this,
                new ReplaceOptions { IsUpsert = true });
        }

        public static Candle LoadFromDb(string conString, string collectionName, DateTime openValue)
        {
            var client = new MongoClient(conString);
            var database = client.GetDatabase("CandlesFaces");
            var dbCol = database.GetCollection<Candle>(collectionName);

            var filterBuilder1 = Builders<Candle>.Filter;
            var filterDefinition = new List<FilterDefinition<Candle>>();
            filterDefinition.Add(filterBuilder1.Eq(item => item.PointInTime_Open, openValue));

            var filter = filterBuilder1.And(filterDefinition.ToArray());

            var query = dbCol.Find(filter).ToList();

            return query.FirstOrDefault();
        }

        private DateTime _lastPersist = DateTime.MinValue;
        public void TimedSaveOrUpdate(string conString, string patternName, TimeSpan interval)
        {
            if ((DateTime.Now - _lastPersist) > interval)
            {
                SaveOrUpdate(conString, patternName);
                _lastPersist = DateTime.Now;
            }
        }

        public static DateTime GetValidMilestone(DateTime time, CandleType type)
        {
            var span = new TimeSpan(time.Day, time.Hour, time.Minute, 0);
            time = new DateTime(time.Year, time.Month, time.Day, time.Hour, time.Minute, 0, DateTimeKind.Utc);

            while ((span.TotalMinutes % Convert.ToInt32(type)) > 0)
            {
                time = time.AddMinutes(1);
                span = new TimeSpan(time.Hour, time.Minute, 0);
            }

            return time;
        }
    }



    public class TradeOperationCandle : IStockPointInTime
    {
        private double _entryAmount;
        private double _lowerBound, _upperBound;

        private double _stopLossMark;
        private double _gain;

        private string _state;

        public int Periodo
        {
            get;
            set;
        }

        public DateTime PointInTime
        {
            get;
            set;
        }
        public DateTime PointInTime_Open
        {
            get
            {
                return PointInTime;
            }
            set
            {
                PointInTime = value;
            }
        }
        public DateTime PointInTime_Close
        {
            get;
            set;
        }

        public double AmountValue
        {
            get
            {
                return _entryAmount;
            }
            set
            {
                _entryAmount = value;
            }
        }

        public double OpenValue
        {
            get
            {
                return _entryAmount;
            }
        }

        public double CloseValue
        {
            get
            {
                return _entryAmount;
            }
        }

        public double HighestValue
        {
            get
            {
                return _upperBound;
            }
        }

        public double LowestValue
        {
            get
            {
                return _lowerBound;
            }
        }

        public double Volume
        {
            get;
            set;
        }

        public double ExitValue
        {
            get;
            set;
        }

        public double LowerBound { get => _lowerBound; set => _lowerBound = value; }
        public double UpperBound { get => _upperBound; set => _upperBound = value; }
        public double StopLossMark { get => _stopLossMark; set => _stopLossMark = value; }

        public double Gain { get => _gain; set => _gain = value; }
        public string State { get => _state; set => _state = value; }
        public CandleDirection Direction
        { get; set; }
        public double SoftStopMark { get; internal set; }

        public IStockPointInTime Clone()
        {
            return null;
        }


    }


    public enum TradeType
    {
        Long,
        Short,
        None
    }

    public enum CandleDirection
    {
        Up,
        Down
    }
}