using System;
using System.Collections.Generic;
using System.Linq;

namespace Midas.Core.Common
{
    public class VolumeIndicator : IStockPointInTime
    {

        public IStockPointInTime Clone()
        {
            VolumeIndicator n = new VolumeIndicator(this);

            return n;
        }

        public int Periodo
        {
            get;
            set;
        }        

        public VolumeIndicator(IStockPointInTime c)
        {
            PointInTime_Open = c.PointInTime_Open;
            PointInTime_Close = c.PointInTime_Close;
            Volume = c.Volume;
            Direction = c.Direction;
        }

        public VolumeIndicator() {}

        private DateTime _pointInTime;

        public DateTime PointInTime
        {
            get
            {
                return _pointInTime;
            }
            set
            {
                _pointInTime = value;
            }
        }

        public DateTime PointInTime_Open
        {
            get
            {
                return _pointInTime;
            }
            set
            {
                _pointInTime = value;
            }
        }

        public DateTime PointInTime_Close
        {
            get;
            set;
        }
        double IStockPointInTime.AmountValue
        {
            get { return Volume; }
            set { Volume = value; }
        }

        public double Volume
        {
            get;
            set;
        }

        double IStockPointInTime.OpenValue { get => Volume; }
        double IStockPointInTime.CloseValue { get => Volume; }
        double IStockPointInTime.HighestValue { get => Volume; }
        double IStockPointInTime.LowestValue { get => Volume; }
        public CandleDirection Direction { get; set; }
    }


    public class Indicator : IStockPointInTime
    {

        public IStockPointInTime Clone()
        {
            Indicator n = new Indicator();
            n.PointInTime = this.PointInTime;
            n.PointInTime_Close = this.PointInTime_Close;
            n.AmountValue = this.AmountValue;

            return n;
        }

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
            get;
            set;
        }


        public double OpenValue
        {
            get
            {
                return AmountValue;
            }
        }

        public double CloseValue
        {
            get
            {
                return AmountValue;
            }
        }

        public double HighestValue
        {
            get
            {
                return AmountValue;
            }
        }

        public double LowestValue
        {
            get
            {
                return AmountValue;
            }
        }

        public double Volume
        {
            get;
            set;
        }
        public CandleDirection Direction { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    }

}