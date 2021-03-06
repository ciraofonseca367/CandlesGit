using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Midas.Core.Chart;
using Midas.Core.Common;
using Midas.Util;

namespace Midas.Core.Indicators
{
    public abstract class CalculatedIndicator
    {
        public CalculatedIndicator(string bufferSize, string windowSize) : this(new object[] {bufferSize, windowSize})
        {
        }

        public CalculatedIndicator(object[] args)
        {
            if(args.Length < 2)
                throw new ArgumentException("MA needs two arguments [0] = BufferSize [1] = WindowSize");
            else
            {
                _bufferSize = Convert.ToInt32(args[0]);
                _windowSize = Convert.ToInt32(args[1]);
            }

            _historical = new FixedSizedQueue<IStockPointInTime>(_bufferSize);

            _currentWindow = new FixedSizedQueue<IStockPointInTime>(_windowSize);

            IncludeInPrediction = true;
        }

        protected FixedSizedQueue<IStockPointInTime> _historical;
        protected FixedSizedQueue<IStockPointInTime> _currentWindow;        

        protected int _bufferSize, _windowSize;

        private IStockPointInTime _lastPoint;
        public string Source
        {
            get;
            set;
        }
        public string Name { get; set; }
        public Color Color { get; internal set; }
        public SeriesType Type { get; set; }
        public bool IncludeInPrediction { get; set; }
        public string Target { get; internal set; }

        public int Size { get; internal set; }
        public IStockPointInTime LastPoint { get => _lastPoint; }

        public virtual void AddPoint(IStockPointInTime point)
        {
            _historical.Enqueue(point);

            if(_historical.GetList().Length == _bufferSize)
            {
                _lastPoint = AddFramePoint(point);
            }
        }

        public abstract IStockPointInTime AddFramePoint(IStockPointInTime point);

        public virtual IEnumerable<IStockPointInTime> TakeSnapShot(DateRange range)
        {
            //Colocar aqui c??digo para pegar somente pelo Range.
            var ret = _currentWindow.GetList().Where(p => range.IsInside(p.PointInTime_Open));
            return ret;
        }
    }
}