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
                throw new ArgumentException("MAI needs two arguments [0] = BufferSize [1] = WindowSize");
            else
            {
                _bufferSize = Convert.ToInt32(args[0]);
                _windowSize = Convert.ToInt32(args[1]);
            }

            if(_bufferSize > 0)
                _historical = new FixedSizedQueue<IStockPointInTime>(_bufferSize);
            else
                _historical = null;

            _currentWindow = new FixedSizedQueue<IStockPointInTime>(_windowSize);

            IncludeInPrediction = true;
        }

        protected FixedSizedQueue<IStockPointInTime> _historical;
        protected FixedSizedQueue<IStockPointInTime> _currentWindow;        

        protected int _bufferSize, _windowSize;
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

        public virtual void AddPoint(IStockPointInTime point)
        {
            if(_historical != null)
            {
                _historical.Enqueue(point);

                if(_historical.GetList().Length > (_bufferSize*0.95))
                {
                    AddFramePoint(point);
                }
            }
            else
                AddFramePoint(point);
        }

        public abstract void AddFramePoint(IStockPointInTime point);

        public abstract void AddIdentifedFramePoint(IStockPointInTime point, string identifier);

        public virtual IEnumerable<IStockPointInTime> TakeSnapShot(DateRange range)
        {
            //Colocar aqui código para pegar somente pelo Range.
            var ret = _currentWindow.GetList().Where(p => range.IsInside(p.PointInTime_Open));
            return ret;
        }

        public virtual IEnumerable<IStockPointInTime> TakeSnapShot()
        {
            var list = _currentWindow.GetList();
            return list;
        }        
    }
}