using System;
using System.Collections.Generic;
using Midas.Core.Common;
using Midas.Util;
using System.Collections;
using System.Linq;

namespace Midas.Core.Indicators
{
    public class MovingAverageIndicator : CalculatedIndicator
    {

        public MovingAverageIndicator(string bufferSize, string windowSize) : base(bufferSize, windowSize)
        { }

        public MovingAverageIndicator(object[] args) : base(args)
        { }

        public override void AddFramePoint(IStockPointInTime point)
        {
            var buffer = base._historical.GetList();
            var ma = buffer.Average(p => p.CloseValue);

            var newPoint = new Indicator()
            {
                AmountValue = ma,
                PointInTime_Open = point.PointInTime_Open,
                PointInTime_Close = point.PointInTime_Close
            };

            base._currentWindow.Enqueue(newPoint);
        }

        public override void AddIdentifedFramePoint(IStockPointInTime point, string identifier)
        {

        }
    }

    public class ATRIndicator : CalculatedIndicator
    {
        public ATRIndicator(string bufferSize, string windowSize) : base(bufferSize, windowSize)
        { }

        public ATRIndicator(object[] args) : base(args)
        { }

        public override void AddFramePoint(IStockPointInTime point)
        {
            var buffer = base._historical.GetList();
            List<double> atrs = new List<double>();

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

                var atr = atrs.Average();

                var newPoint = new Indicator()
                {
                    AmountValue = atr,
                    PointInTime_Open = point.PointInTime_Open.AddMilliseconds(
                        (point.PointInTime_Close - point.PointInTime_Open).TotalMilliseconds / 2
                    ),

                    PointInTime_Close = point.PointInTime_Close
                };

                base._currentWindow.Enqueue(newPoint);
            }
        }

        public override void AddIdentifedFramePoint(IStockPointInTime point, string identifier)
        {

        }
    }
}