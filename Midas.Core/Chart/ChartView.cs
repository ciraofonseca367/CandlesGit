using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

using Midas.Core.Common;

namespace Midas.Core.Chart
{

    public enum SeriesType
    {
        Candle,
        Line,
        Bar,
        Forecast
    }

    public enum DrawAmountStyle
    {
        AbsoluteFromZero,
        Relative
    }

    public class Serie
    {
        public Serie()
        {
            AmountStyle = DrawAmountStyle.Relative;
            Color = Color.Black;
            PointsInTime = new List<IStockPointInTime>();
            Frameble = true;

            RelativeXPos = 0.7;
        }

        public bool IncludeInPrediction
        {
            get;
            set;
        }

        public string Name
        {
            get;
            set;
        }

        public Color Color
        {
            get;
            set;
        }

        public SeriesType Type
        {
            get;
            set;
        }

        public DrawAmountStyle AmountStyle
        {
            get;
            set;
        }

        public List<IStockPointInTime> PointsInTime
        {
            get;
            set;
        }

        public double Compare(Serie s)
        {
            var diff1 = Math.Abs(((s.PointsInTime.Last().AmountValue - this.PointsInTime.Last().AmountValue) / this.PointsInTime.Last().AmountValue) * 100);
            var diff2 = Math.Abs(((s.PointsInTime.First().AmountValue - this.PointsInTime.First().AmountValue) / this.PointsInTime.First().AmountValue) * 100);
            return Math.Max(diff1, diff2);
        }

        public bool Frameble
        {
            get;
            set;
        }
        public double RelativeXPos { get; set; }
        public int LineSize { get; set; }
    }

    public class ViewPort
    {
        private static int POINT_SIZE = 3;
        private bool ARROW_ENABLED = true;

        private double _minAmount, _maxAmount;
        private double _minTimeStamp, _maxTimpStamp;

        private double _Width, _Height;

        private double _periodRange, _amountRange;

        private int _offSetX, _offSetY;

        private Bitmap _canvas;
        private Graphics _painter;

        public double MinAmount { get => _minAmount; }

        public double MaxAmount { get => _maxAmount; }

        public ViewPort(
            Bitmap canvas,
            double minAmount, double maxAmount,
            double minTimeStamp, double maxTimeStamp,
            int width, int height,
            int offSetX, int offSetY
        )
        {
            _minAmount = minAmount;
            _maxAmount = maxAmount;
            _minTimeStamp = minTimeStamp;
            _maxTimpStamp = maxTimeStamp;
            _Width = width;
            _Height = height;

            _offSetX = offSetX;
            _offSetY = offSetY;

            _periodRange = _maxTimpStamp - _minTimeStamp;
            _amountRange = _maxAmount - MinAmount;
            if (_amountRange == 0)
                _amountRange = _maxAmount; //This usually happens when we have just on point in the series

            _canvas = canvas;

            _painter = Graphics.FromImage(_canvas);
        }

        internal void DrawLineSeries(Serie s, Color c)
        {
            IStockPointInTime previousPoint = null;
            int size = 50;

            var maxPY = _minAmount;
            var minPY = _maxAmount;

            if (s.PointsInTime.Count > 0)
            {
                maxPY = s.PointsInTime.Max(p => p.AmountValue);
                minPY = s.PointsInTime.Min(p => p.AmountValue);
            }

            if (ARROW_ENABLED && (maxPY < _minAmount || minPY > _maxAmount))
            {
                var x = Convert.ToInt32(_Width * s.RelativeXPos);

                double theAmount;
                if (maxPY < _minAmount) // we are veeeeery away from this line from bellow
                {
                    theAmount = _minAmount;
                    var translateMin = Translate(0, theAmount);

                    _painter.FillPolygon(new SolidBrush(c), new Point[] {
                        new Point(x-size, translateMin.y),
                        new Point(x+size, translateMin.y),
                        new Point(x, translateMin.y+size)
                    });
                }
                else  // we are veeeeery away from this line from above
                {
                    theAmount = _maxAmount;
                    var translateMax = Translate(0, theAmount);

                    _painter.FillPolygon(new SolidBrush(c), new Point[] {
                        new Point(x-size, translateMax.y+size+5),
                        new Point(x+size, translateMax.y+size+5),
                        new Point(x, translateMax.y+5)
                    });
                }
            }
            else
            {
                foreach (var p in s.PointsInTime)
                {
                    if (previousPoint != null)
                        this.DrawLine(previousPoint, p, c, s.LineSize);

                    previousPoint = p;
                }
            }

        }

        internal void DrawBarSeries(Serie s, Color c)
        {
            foreach (var p in s.PointsInTime)
            {
                this.DrawBar(p, c);
            }
        }

        internal void DrawCandleSeries(Serie s)
        {
            if (s.PointsInTime.Count > 0)
            {
                s.PointsInTime.ForEach(p =>
                {
                    var candle = (Candle)p;
                    Color candleColor = (candle.Direction == CandleDirection.Up ? Color.Green : Color.DarkRed);
                    this.DrawCandle(candle, candleColor);
                });
            }
        }

        internal void DrawForecastSeries(Serie s)
        {
            if (s.PointsInTime.Count > 0)
            {
                s.PointsInTime.ForEach(p =>
                {
                    if (p.AmountValue > -1)
                    {
                        var forecastCandle = p;
                        this.DrawForecast(forecastCandle, s.Color);
                    }
                });
            }
        }

        internal void DrawForecast(IStockPointInTime point, Color c)
        {
            TradeOperationCandle opc = (TradeOperationCandle)point;

            var timeStampOpen = InSeconds(point.PointInTime_Open);
            var timeStampClose = InSeconds(point.PointInTime_Close);
            var c1 = Translate(timeStampOpen, opc.LowerBound);
            var c2 = Translate(timeStampClose, opc.UpperBound);

            var transparentColor = Color.FromArgb(200, c);
            Pen p = new Pen(transparentColor, 5);
            p.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;

            Pen slp = new Pen(Color.Red, 5);

            Pen sslp = new Pen(Color.PaleVioletRed, 5);

            Pen stlp = new Pen(Color.LightGreen, 5);

            var gain = opc.Gain;
            Color gainColor = (gain >= 0 ? Color.Green : Color.Red);

            var highBodyY = (c1.y > c2.y ? c1.y : c2.y);
            var lowBodyY = (c2.y < c1.y ? c2.y : c1.y);
            var bodySize = highBodyY - lowBodyY;

            Brush b = new SolidBrush(transparentColor);

            _painter.DrawRectangle(p, c1.x + 1, lowBodyY, (c2.x - c1.x) - 1, bodySize);

            var priceLine = Translate(timeStampOpen, opc.AmountValue);

            _painter.DrawLine(p, c1.x + 1, priceLine.y, c2.x, priceLine.y);

            var stopLine = Translate(timeStampOpen, opc.StopLossMark);
            _painter.DrawLine(slp, c1.x + 1, stopLine.y, c2.x, stopLine.y);

            var softStopLine = Translate(timeStampOpen, opc.SoftStopLossMark);
            _painter.DrawLine(sslp, c1.x + 1, softStopLine.y, c2.x, softStopLine.y);

            if (opc.StrenghMark > 0)
            {
                var strenghLine = Translate(timeStampOpen, opc.StrenghMark);
                _painter.DrawLine(stlp, c1.x + 1, strenghLine.y, c2.x, strenghLine.y);
            }

            _painter.DrawString(
                String.Format("{0}: {1:0.0000} %", opc.State, gain),
                new Font("Arial", 12),
                new SolidBrush(gainColor),
                c1.x + 5,
                lowBodyY + 10
            );
        }

        internal void DrawCandle(Candle d, Color c)
        {
            var point = d;
            var timeStampOpen = InSeconds(point.PointInTime_Open);
            var timeStampClose = InSeconds(point.PointInTime_Close);
            var c1 = Translate(timeStampOpen, point.OpenValue);
            var c2 = Translate(timeStampClose, point.CloseValue);

            var s1 = Translate(timeStampOpen + (((timeStampClose - timeStampOpen) / 2) - 1), point.HighestValue);
            var s2 = Translate(timeStampOpen + (((timeStampClose - timeStampOpen) / 2) - 1), point.LowestValue);

            var highBodyY = (c1.y > c2.y ? c1.y : c2.y);
            var lowBodyY = (c2.y < c1.y ? c2.y : c1.y);
            var bodySize = (highBodyY - lowBodyY) + 1;

            Pen p = new Pen(c, 2);

            //Candle Body
            Brush b = new SolidBrush(c);

            _painter.FillRectangle(b, c1.x + 1, lowBodyY, (c2.x - c1.x) - 1, bodySize);

            //Upper Shaddow
            _painter.DrawLine(p, s1.x + 1, s1.y, s1.x + 1, highBodyY);

            //Lower Shaddow
            _painter.DrawLine(p, s2.x + 1, lowBodyY, s2.x + 1, s2.y);
        }
        private void DrawLine(IStockPointInTime pointA, IStockPointInTime pointB, Color c, int size)
        {
            var coordA = Translate(pointA);
            var coordB = Translate(pointB);

            var p = new Pen(new SolidBrush(c), size);
            _painter.DrawLine(p, coordA.x, coordA.y, coordB.x, coordB.y);
        }

        private void DrawBar(IStockPointInTime pointA, Color c)
        {
            var coordStart = Translate(pointA);
            IStockPointInTime pointB = pointA.Clone();
            pointB.PointInTime = pointA.PointInTime_Close; //
            pointB.AmountValue = 0;

            var coordEnd = Translate(pointB);

            _painter.FillRectangle(
                new SolidBrush(c),
                coordStart.x + 1,
                coordStart.y + 1,
                coordEnd.x - (coordStart.x + 1),
                coordStart.y - coordEnd.y
            );
        }

        private void DrawPoint(IStockPointInTime point, Color c)
        {
            var coord = Translate(point);

            _painter.DrawRectangle(new Pen(new SolidBrush(c)), coord.x, coord.y, POINT_SIZE, POINT_SIZE);
        }

        //STATE: Testar o DashView

        internal void DrawRectangle(Pen p, int x, int y, int width, int height)
        {
            var coord = Translate(x, y);
            _painter.DrawRectangle(p, coord.x, coord.y, width, height);
        }

        internal Coordinate Translate(IStockPointInTime point)
        {
            Int64 timeStamp = InSeconds(point.PointInTime);
            double amount = point.AmountValue;

            return Translate(timeStamp, amount);
        }

        internal Coordinate Translate(int x, int y)
        {
            Coordinate c;
            c.x = _offSetX + x;
            c.y = _offSetY + y;

            return c;
        }

        internal Coordinate Translate(Int64 timeStamp, double amount)
        {
            Coordinate c;
            if (timeStamp > 0)
            {
                c.x = Convert.ToInt32(
                    _Width * ((timeStamp - _minTimeStamp) / _periodRange)
                );
            }
            else
                c.x = 0;

            c.x = _offSetX + c.x;

            if (amount > 0)
            {
                c.y = Convert.ToInt32(
                    _Height * (1 - ((amount - MinAmount) / _amountRange))
                ) + _offSetY;
            }
            else
                c.y = 0;

            return c;
        }

        internal Int64 InSeconds(DateTime pointInTime)
        {
            return DateRange.InSeconds(pointInTime);
        }

    }

    public class ChartView
    {
        private int _Width;
        private int _Height;

        private int _offSetX, _offSetY;

        private List<Serie> _Series;

        public void AddSerie(Serie serie)
        {
            _Series.Add(serie);
        }

        public Serie GetSerie(string name)
        {
            return _Series.FirstOrDefault(s => s.Name == name);
        }

        public ChartView(
            int width,
            int height,
            int offSetX,
            int offSetY
        )
        {
            _Width = Convert.ToInt32(width * 0.995f);
            _Height = Convert.ToInt32(height * 0.99f);

            _offSetX = Convert.ToInt32(offSetX * 1.01);
            _offSetY = Convert.ToInt32(offSetY * 1.01);

            _Series = new List<Serie>();
        }

        private ViewPort GetViewPort(Bitmap canvas)
        {
            long minTotalSeconds = Int64.MaxValue;
            long maxTotalSeconds = Int64.MinValue;
            double minAmount = double.MaxValue;
            double maxAmount = double.MinValue;

            foreach (Serie s in _Series)
            {
                if (s.PointsInTime.Count() > 0 && s.Frameble)
                {
                    if (DateRange.InSeconds(s.PointsInTime[0].PointInTime) < minTotalSeconds)
                        minTotalSeconds = DateRange.InSeconds(s.PointsInTime[0].PointInTime_Open);

                    if (DateRange.InSeconds(s.PointsInTime[s.PointsInTime.Count - 1].PointInTime_Close) > maxTotalSeconds)
                        maxTotalSeconds = DateRange.InSeconds(s.PointsInTime[s.PointsInTime.Count - 1].PointInTime_Close);

                    if (s.AmountStyle == DrawAmountStyle.Relative)
                    {
                        var tmpMinAmount = s.PointsInTime.Min(p => p.LowestValue);
                        if (tmpMinAmount < minAmount)
                            minAmount = tmpMinAmount;
                    }
                    else
                        minAmount = 0;

                    var tmpMaxAmount = s.PointsInTime.Max(p => p.HighestValue);
                    if (tmpMaxAmount > maxAmount)
                        maxAmount = tmpMaxAmount;
                }
            }

            return new ViewPort(canvas, minAmount, maxAmount, minTotalSeconds, maxTotalSeconds, _Width, _Height, _offSetX, _offSetY);
        }

        public bool Draw(Bitmap canvas)
        {
            Color black = Color.Black;
            double priceLine = 0;

            List<int> counts = new List<int>();
            foreach (var serie in _Series)
            {
                counts.Add(serie.PointsInTime.Count);
            }

            if (counts.Count > 0)
            {

                ViewPort vp = GetViewPort(canvas);
                var d = Graphics.FromImage(canvas);

                var allSeriesOK = counts.Max() == counts.Average();

                var priceSerie = _Series.Where(s => s.Type == SeriesType.Candle);
                if (priceSerie.Count() > 0 && priceSerie.First().PointsInTime.Count() > 0)
                    priceLine = priceSerie.First().PointsInTime.Last().CloseValue;

                if (vp.MinAmount > 0 && priceLine > 0)
                {
                    //Draw .1 price lines 
                    double currentPrice = vp.MinAmount;
                    while (currentPrice < vp.MaxAmount)
                    {
                        var Dot01Coord = vp.Translate(-1, currentPrice);
                        Pen markerPen = new Pen(Color.LightGray);
                        markerPen.Width = 2;
                        //markerPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                        d.DrawLine(markerPen, 0, Dot01Coord.y, canvas.Width, Dot01Coord.y);

                        currentPrice += vp.MinAmount * 0.005;
                    }
                }

                //Draw Seriess
                foreach (var serie in _Series)
                {
                    if (serie.Type == SeriesType.Candle)
                    {
                        vp.DrawCandleSeries(serie);
                        if (serie.PointsInTime.Count > 0)
                            priceLine = serie.PointsInTime.Last().CloseValue;
                    }
                    else if (serie.Type == SeriesType.Line)
                        vp.DrawLineSeries(serie, serie.Color);
                    else if (serie.Type == SeriesType.Forecast)
                        vp.DrawForecastSeries(serie);
                    else
                        vp.DrawBarSeries(serie, serie.Color);
                }

                //Draw Price Line
                if (priceLine > 0)
                {
                    var priceLineCoord = vp.Translate(-1, priceLine);
                    Pen pricePen = new Pen(Color.Black);
                    pricePen.Width = 1;
                    d.DrawLine(pricePen, 0, priceLineCoord.y, canvas.Width, priceLineCoord.y);
                }

                return allSeriesOK;
            }
            else
                return true;
        }
    }

    public struct Coordinate
    {
        public int x;
        public int y;
    }
}