using System.Net;
using System.IO;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using Midas.Core.Card;
using System.Linq;
using MongoDB.Driver;
using Midas.Util;
using System.Drawing;
using Midas.Core.Common;
using Midas.Core;
using Midas.Core.Chart;
using System.Text;
using Google.Cloud.AutoML.V1;

namespace Midas
{

    public class ConsoleProgressBar
    {
        private float _size;
        public ConsoleProgressBar(float size)
        {
            _size = size;
        }

        public void setProgress(double value)
        {
            double fstatus = value / _size * 100;
            int status = Convert.ToInt32(fstatus);

            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Out.Write("[");
            for (int i = 0; i < 100; i++)
            {
                if (i <= status)
                    Console.Out.Write("=");
                else
                    Console.Out.Write(" ");
            }
            Console.Out.Write("] ");
            Console.Out.Write(fstatus.ToString("0.00"));
        }
    }

}