using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

using Midas.Core.Common;
using Midas.Core.Util;

namespace Midas.Core.Chart
{
    public class DashView
    {
        private int _Width, _Height;
        public DashView(int width, int height)
        {
            _Width = width;
            _Height = height;
            _views = new List<ViewFrame>();
        }

        private List<ViewFrame> _views;

        public ChartView AddChartFrame(float relativeRowSize)
        {
            float currentHeight = 0;
            if(_views.Count > 0)
                currentHeight = _views.Sum(v => v.Height);

            float offSetY = _Height - (_Height * (1 - (currentHeight/100)));
            float height = _Height * (relativeRowSize/100);
            
            var ret = new ChartView(
                _Width,
                Convert.ToInt32(height),
                0,
                Convert.ToInt32(offSetY)
            );

            _views.Add(new ViewFrame()
            {
                ChartView = ret,
                Height = relativeRowSize
            });

            return ret;
        }

        public Bitmap GetImage()
        {
            bool sym = false;

            var ret = GetImage(ref sym);
            if(!sym)
                ret = null;

            return ret;
        }

        public Bitmap GetImage(ref bool isSimmetric)
        {
            return GetImagePlus(ref isSimmetric).RawImage;
        }

        public CandleFaceImage GetImagePlus(ref bool isSimmetric)
        {
            isSimmetric = false;
            List<int> allStatus = new List<int>();

            CandleFaceImage cfImage = new CandleFaceImage();

            var dashCanvas = new Bitmap(_Width,_Height);

            var g = Graphics.FromImage(dashCanvas);

            //Paint the full canvas as blank
            g.FillRectangle(new SolidBrush(Color.White), 0, 0, _Width, _Height);

            _views.Reverse();
            
            foreach(var v in _views)
            {
                //TODO: Ir fazendo merge dos Dictionaries de features
                var value = 0;
                if(v.ChartView.Draw(dashCanvas))
                    value = 1;

                allStatus.Add(value);
            }

            isSimmetric = (allStatus.Count == allStatus.Sum()); //Set it to false when it is not symmetric

            cfImage.RawImage = dashCanvas;
            cfImage.FeatureList = null;

            return cfImage;
        }
    }

    public class ViewFrame
    {
        public ChartView ChartView
        {
            get;
            set;
        }

        public float Height
        {
            get;
            set;
        }
    }

    public class CandleFaceImage
    {
        public Bitmap RawImage
        {
            get;set;
        }

        public Dictionary<DateTime,List<FeatureCoordinate>> FeatureList
        {
            get;set;
        }
    }
}