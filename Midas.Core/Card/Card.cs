using System.IO;
using System;
using System.Collections.Generic;
using System.Drawing;
using Midas.Core.Common;
using System.Text;
using System.Linq;
using Midas.Core.Chart;
using Newtonsoft.Json;
using Midas.Core.Util;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Net.Http;
using System.Threading;

namespace Midas.Core.Card
{
    public class Card
    {
        public static DateTime AngelBirth = new DateTime(2008, 01, 01);


        private DateRange _range;
        private Bitmap _img;
        private CandleFaceImage _cfImg;
        private IStockPointInTime[] _allCandles;

        private List<Indicators.CalculatedIndicator> _indicators;

        private static DateTime _lastOperationEnd = DateTime.MinValue;

        private RunParameters _params;
        private DateTime _beginWindow, _endWindow, _futureDate;

        private double _ratr;

        public Card(
                CandleFaceImage img, IStockPointInTime[] allCandles, DateTime timeStamp,
                DateTime beginWindow, DateTime endWindow, DateTime futureDate,
                RunParameters @params, Candle lastLongCandle)
        {
            this._range = new DateRange(Card.AngelBirth, timeStamp);
            this._img = img.RawImage;
            this._cfImg = img;
            this._allCandles = allCandles;

            this._beginWindow = beginWindow;
            this._endWindow = endWindow;
            this._futureDate = futureDate;

            _indicators = @params.Indicators;
            _params = @params;

            //SetLastLong(lastLongCandle);

            _ratr = GetRATR();
        }

        private DateRange CardRange
        {
            get
            {
                return new DateRange(_beginWindow, _endWindow);
            }
        }

        public double GetWindowVariation()
        {
            var windowCandles = GetWindowCandles();

            var diff = Math.Abs(windowCandles.Max(c => c.HighestValue) - windowCandles.Min(c => c.LowestValue));
            var firstCandle = GetFirstCandle();

            return (diff / firstCandle.CloseValue) * 100;
        }

        public bool HasRecentPeakVolume_byAvg()
        {
            var avgVolumes = _params.Indicators.Where(i => i.Name == "MA144")?.FirstOrDefault()?.TakeSnapShot(CardRange);

            var lastAvgVolume = avgVolumes.Last().AmountValue;

            var windowCandles = GetWindowCandles();

            var twentyPercentLast = windowCandles.TakeLast(4).ToList();

            bool inPeek = false;
            twentyPercentLast.ForEach(c =>
            {
                var ratio = c.Volume / lastAvgVolume;

                if (ratio > 2)
                    inPeek = true;
            });

            return inPeek;
        }

        private List<IStockPointInTime> GetWindowCandles()
        {
            return _allCandles.Where(c => c.PointInTime_Open >= _beginWindow && c.PointInTime_Open <= _endWindow).ToList();
        }

        private void SetLastLong(Candle lastLong)
        {
            var ratr = GetRATR();
            var lastCandle = GetLastCandle();

            Candle currentLastLong = null;

            if ((lastCandle.OpenTime - lastLong.OpenTime).TotalMinutes == Convert.ToInt32(_params.CandleType))
            {
                currentLastLong = lastLong;
            }
        }

        private double GetRATR()
        {
            var ATR = _params.Indicators.Where(i => i.Name == "ATR").FirstOrDefault();
            double ratr = 0;
            if (ATR != null)
            {
                var last = (ATR.TakeSnapShot(CardRange).LastOrDefault());
                var lastCandle = GetLastCandle();
                if (last != null && lastCandle != null)
                    ratr = (last.CloseValue / lastCandle.CloseValue) * 100;
            }

            return ratr;
        }

        private double GetAvgValue(string name)
        {
            var Ind = _params.Indicators.Where(i => i.Name == name).FirstOrDefault();
            double ret = 0;
            if (Ind != null)
            {
                var last = (Ind.TakeSnapShot(CardRange).LastOrDefault());
                if (last != null)
                    ret = last.AmountValue;
            }

            return ret;
        }

        private Candle GetFirstCandle()
        {
            return (Candle)_allCandles.First();
        }

        public Candle GetLastCandle()
        {
            return (Candle)_allCandles.Where(c => c.PointInTime_Close.ToString("yyyy-MM-dd HH:mm") == _endWindow.ToString("yyyy-MM-dd HH:mm")).FirstOrDefault();
        }

        public Candle GetLastCandlePlusOne()
        {
            var endWindowPlusOne = _endWindow.AddMinutes(5);
            return (Candle)_allCandles.Where(c => c.PointInTime_Open.ToString("yyyy-MM-dd HH:mm") == endWindowPlusOne.ToString("yyyy-MM-dd HH:mm")).FirstOrDefault();
        }

        private ForecastResult GetCompleteForecast(double presentClosing, DateTime presentTime)
        {
            if (presentClosing == 0)
                throw new ArgumentException("Present closing Zero");

            StringBuilder sb = new StringBuilder();

            var futureRange = new DateRange(_endWindow.AddMinutes(5), _futureDate);

            var clip = _allCandles.Where(c => futureRange.IsInside(c.PointInTime_Open)).ToList();
            clip.ForEach(c =>
            {
                c.Periodo = Convert.ToInt32((c.PointInTime_Open - presentTime).TotalMinutes / 5);
                sb.Append((((c.CloseValue - presentClosing) / c.CloseValue) * 100).ToString("0.00") + ";");
            });

            string allValues = sb.ToString();
            if (allValues.Length > 0)
                allValues = allValues.Substring(0, allValues.Length - 1);

            double highestValue = 0, lowestValue = 0, lowest = 0, highest = 0;
            uint periods = 0, periodsLowest = 0;
            double futureClosing = 0;

            ForecastResult res = new ForecastResult()
            {
                LowestDifFerence = 0,
                HighestDifference = 0,
                ForecastLong = 0,
                ForecastShort = 0,
                TimeToGetHigh = 0,
                TimeToGetLow = 0,
                AllValues = "",
                FinalClosing = 0,
                ClippedCandles = new List<IStockPointInTime>(),
                StartValue = 0,
            };

            if (clip.Count() > 0)
            {
                highestValue = clip.Max(c => c.HighestValue);
                lowestValue = clip.Min(c => c.LowestValue);

                var pureCandles = _allCandles.Where(c => futureRange.IsInside(c.PointInTime_Open)).ToList();
                pureCandles.ForEach(c =>
                {
                    c.Periodo = Convert.ToInt32((c.PointInTime_Open - presentTime).TotalMinutes / 5);
                    sb.Append((((c.CloseValue - presentClosing) / c.CloseValue) * 100).ToString("0.00") + ";");
                });

                lowest = ((lowestValue - presentClosing) / presentClosing) * 100;
                highest = ((highestValue - presentClosing) / presentClosing) * 100;

                futureClosing = ((clip.Last().CloseValue - presentClosing) / presentClosing) * 100;

                var timeHighest = clip.Where(c => c.HighestValue == highestValue).FirstOrDefault().PointInTime_Open;
                var timeLowest = clip.Where(c => c.LowestValue == lowestValue).FirstOrDefault().PointInTime_Open;

                periods = Convert.ToUInt32((timeHighest - presentTime).TotalMinutes / 5);
                periodsLowest = Convert.ToUInt32((timeLowest - presentTime).TotalMinutes / 5);

                res = new ForecastResult()
                {
                    LowestDifFerence = lowest,
                    HighestDifference = highest,
                    ForecastLong = highest,
                    ForecastShort = lowest,
                    TimeToGetHigh = periods,
                    TimeToGetLow = periodsLowest,
                    AllValues = allValues,
                    FinalClosing = futureClosing,
                    ClippedCandles = clip,
                    PureCandles = pureCandles,
                    StartValue = presentClosing
                };
            }

            return res;
        }

        private ForecastResult GetCompleteForecastOnAnAverage(DateTime presentTime, string averageName)
        {
            double presentClosing;

            StringBuilder sb = new StringBuilder();

            var futureRange = new DateRange(presentTime, _futureDate);

            var indicator = _indicators.Where(i => i.Name == averageName).FirstOrDefault();
            if (indicator == null)
                throw new ArgumentException("There is no average: " + averageName);

            var clip = indicator.TakeSnapShot(futureRange).ToList();

            ForecastResult res = new ForecastResult()
            {
                LowestDifFerence = 0,
                HighestDifference = 0,
                ForecastLong = 0,
                ForecastShort = 0,
                TimeToGetHigh = 0,
                TimeToGetLow = 0,
                AllValues = "",
                FinalClosing = 0,
                ClippedCandles = new List<IStockPointInTime>(),
                StartValue = 0,
            };

            if (clip.Count() > 0)
            {
                presentClosing = clip.First().CloseValue;
                clip.ForEach(c =>
                {
                    c.Periodo = Convert.ToInt32((c.PointInTime_Open - presentTime).TotalMinutes / 5);
                    sb.Append((((c.CloseValue - presentClosing) / c.CloseValue) * 100).ToString("0.00") + ";");
                });

                var pureCandles = _allCandles.Where(c => futureRange.IsInside(c.PointInTime_Open)).ToList();
                pureCandles.ForEach(c =>
                {
                    c.Periodo = Convert.ToInt32((c.PointInTime_Open - presentTime).TotalMinutes / 5);
                    sb.Append((((c.CloseValue - presentClosing) / c.CloseValue) * 100).ToString("0.00") + ";");
                });

                string allValues = sb.ToString();
                if (allValues.Length > 0)
                    allValues = allValues.Substring(0, allValues.Length - 1);

                double highestValue = 0, lowestValue = 0, lowest = 0, highest = 0;
                uint periods = 0, periodsLowest = 0;
                double futureClosing = 0;

                highestValue = clip.Max(c => c.HighestValue);
                lowestValue = clip.Min(c => c.LowestValue);

                lowest = ((lowestValue - presentClosing) / presentClosing) * 100;
                highest = ((highestValue - presentClosing) / presentClosing) * 100;

                futureClosing = ((clip.Last().CloseValue - presentClosing) / presentClosing) * 100;

                var timeHighest = clip.Where(c => c.HighestValue == highestValue).FirstOrDefault().PointInTime_Open;
                var timeLowest = clip.Where(c => c.LowestValue == lowestValue).FirstOrDefault().PointInTime_Open;

                periods = Convert.ToUInt32((timeHighest - presentTime).TotalMinutes / 5);
                periodsLowest = Convert.ToUInt32((timeLowest - presentTime).TotalMinutes / 5);

                res = new ForecastResult()
                {
                    LowestDifFerence = lowest,
                    HighestDifference = highest,
                    ForecastLong = highest,
                    ForecastShort = lowest,
                    TimeToGetHigh = periods,
                    TimeToGetLow = periodsLowest,
                    AllValues = allValues,
                    FinalClosing = futureClosing,
                    ClippedCandles = clip,
                    PureCandles = pureCandles,
                    StartValue = presentClosing
                };
            }

            return res;

        }

        private string GetForecastOnAnAverage(double currentValue, DateTime presentTime, string averageName)
        {
            string status = "";
            double limitToPredictLong = 1;
            double limitToPredictShort = -1;
            double limitToPredictZero = 0.5;

            status = "AVG_IGNORED";

            var stopLossShort = limitToPredictLong / 2;
            var stopLossLong = stopLossShort * -1;

            var goodRange = _ratr * 1.5;

            var forecastOnAverage = GetCompleteForecastOnAnAverage(presentTime, averageName);

            var forecastOnPrice = GetCompleteForecast(currentValue, presentTime);

            if (forecastOnAverage.GetHighestDifference(1, _params.ForecastWindow) <= limitToPredictZero &&
                forecastOnAverage.GetLowestDifferente(1, _params.ForecastWindow) >= limitToPredictZero * -1)
                status = "AVG_ZERO";


            if (forecastOnAverage.GetHighestDifference(1, _params.ForecastWindow) >= limitToPredictLong) //Se a média chegou até o objetivo
            {
                if (forecastOnAverage.GetLowestDifferente(1, Convert.ToInt32(_params.ForecastWindow / 2)) > 0 && //E se nos primeiros periodos(metade do forecast) não foi menor que zero, ou seja só subiu
                    forecastOnAverage.GetLowestDifferente(Convert.ToInt32(_params.ForecastWindow / 2), _params.ForecastWindow) > limitToPredictLong * 0.75 //E se depois da metade do forecast não caiu para menos de 75% do objetivo
                    )
                    status = "AVG_LONG";
            }


            if (forecastOnAverage.GetLowestDifferente(1, _params.ForecastWindow) <= limitToPredictShort)
            {
                if (forecastOnAverage.GetHighestDifference(1, Convert.ToInt32(_params.ForecastWindow / 2)) < 0 && //O inverso do acima
                    forecastOnAverage.GetHighestDifference(Convert.ToInt32(_params.ForecastWindow / 2), _params.ForecastWindow) < limitToPredictShort * 0.75 //
                    )
                    status = "AVG_SHORT";
            }

            return status;
        }

        private string GetForecastOnAnAverageAtr(double currentValue, DateTime presentTime, string averageName)
        {
            string status = "AVG_IGNORED";

            var range = new DateRange(_beginWindow, _endWindow);

            var goodRange = _ratr * 1.5;

            var forecastOnAverage = GetCompleteForecastOnAnAverage(presentTime, averageName);

            if (forecastOnAverage.GetHighestDifference(1, _params.ForecastWindow) <= goodRange / 3 &&
                forecastOnAverage.GetLowestDifferente(1, _params.ForecastWindow) >= (goodRange / 3) * -1)
                status = "AVG_ZERO";


            if (forecastOnAverage.GetHighestDifference(1, _params.ForecastWindow) >= goodRange)
            {
                if (forecastOnAverage.GetLowestDifferente(1, _params.ForecastWindow) > 0)
                    status = "AVG_LONG";
            }


            if (forecastOnAverage.GetLowestDifferente(1, _params.ForecastWindow) <= goodRange)
            {
                if (forecastOnAverage.GetHighestDifference(1, _params.ForecastWindow) < 0)
                    status = "AVG_SHORT";
            }

            return status;
        }

        private string GetForecastOnPriceTrend(double currentValue, DateTime presentTime)
        {
            string status = "IGNORED";

            var range = new DateRange(_beginWindow, _endWindow);
            var stopLossShort = _ratr / 2;
            var stopLossLong = stopLossShort * -1;

            var goodRange = 0.75;

            if (_ratr > 0)
            {

                var forecastOnPrice = GetCompleteForecast(currentValue, presentTime);

                if (forecastOnPrice.GetHighestCloseDifference(1, _params.ForecastWindow) <= goodRange / 2.7 &&
                    forecastOnPrice.GetLowestCloseDifferente(1, _params.ForecastWindow) >= (goodRange / 2.7) * -1)
                    status = "ZERO";

                if (forecastOnPrice.CountHighestDifference(currentValue, 1, _params.ForecastWindow, goodRange) >= 3)
                {
                    if (forecastOnPrice.GetLowestCloseDifferente(1, _params.ForecastWindow) > 0)
                        status = "LONG";
                }


                if (forecastOnPrice.CountLowestDifference(currentValue, 1, _params.ForecastWindow, goodRange * -1) >= 3)
                {
                    if (forecastOnPrice.GetHighestCloseDifference(1, _params.ForecastWindow / 2) < 0)
                        status = "SHORT";
                }

                // double diff = 0;
                // if(_lastLongCandle != null)
                //     diff = ((currentValue - _lastLongCandle.CloseValue) / _lastLongCandle.CloseValue) * 100;

                // if(diff > ratr)
                //     status = "IGNORED";       
            }
            else
            {
                status = "ERROR";
            }

            return status;
        }

        private string GetForecastOnPriceTrend_2(double currentValue, DateTime presentTime)
        {
            string status = "";
            double limitToPredictLong = 3;
            double limitToPredictShort = -3;
            double limitToPredictZero = 1;

            status = "ignored";

            var stopLossShort = limitToPredictLong / 2;
            var stopLossLong = stopLossShort * -1;

            var goodRange = _ratr * 1.5;

            var forecastOnPrice = GetCompleteForecast(currentValue, presentTime);

            if (forecastOnPrice.GetHighestDifference(1, _params.ForecastWindow) <= limitToPredictZero &&
                forecastOnPrice.GetLowestDifferente(1, _params.ForecastWindow) >= limitToPredictZero * -1)
                status = "ZERO";


            if (forecastOnPrice.GetHighestDifference(1, _params.ForecastWindow) >= limitToPredictLong) //Se a média chegou até o objetivo
            {
                if (forecastOnPrice.GetLowestDifferente(1, Convert.ToInt32(_params.ForecastWindow)) > -0.5) //E se nos primeiros periodos(metade do forecast) não foi menor que zero, ou seja só subiu
                    status = "L O N G";
            }


            if (forecastOnPrice.GetLowestDifferente(1, _params.ForecastWindow) <= limitToPredictShort)
            {
                if (forecastOnPrice.GetHighestDifference(1, Convert.ToInt32(_params.ForecastWindow)) < 0.5)
                    status = "S_H_T";
            }

            return status;
        }

        private string GetMultipleTags_Long(double currentValue, DateTime presentTime)
        {
            var forecastOnPrice = GetCompleteForecastOnAnAverage(presentTime, "MA24");
            var valuePoint1 = forecastOnPrice.AverageOrStop(-1, 24);
            string strPoint1 = valuePoint1.Item2.ToString("0.00");
            string strTime1 = (valuePoint1.Item1.TotalMinutes / 5).ToString("0");

            forecastOnPrice = GetCompleteForecastOnAnAverage(presentTime, "MA30");
            var valuePoint2 = forecastOnPrice.AverageOrStop(-1, 30);
            string strPoint2 = valuePoint2.Item2.ToString("0.00");
            string strTime2 = (valuePoint2.Item1.TotalMinutes / 5).ToString("0");

            forecastOnPrice = GetCompleteForecastOnAnAverage(presentTime, "MA36");
            var valuePoint3 = forecastOnPrice.AverageOrStop(-1, 36);
            string strPoint3 = valuePoint3.Item2.ToString("0.00");
            string strTime3 = (valuePoint3.Item1.TotalMinutes / 5).ToString("0");

            return $"{strPoint1}_{strTime1}_{strPoint2}_{strTime2}_{strPoint3}_{strTime3}_";
        }

        private string GetMultipleTags_Short(double currentValue, DateTime presentTime)
        {
            var forecastOnPrice = GetCompleteForecastOnAnAverage(presentTime, "MA6");
            var valuePoint1 = forecastOnPrice.AverageOrStop(1, 6);
            string strPoint1 = valuePoint1.Item2.ToString("0.00");
            string strTime1 = (valuePoint1.Item1.TotalMinutes / 5).ToString("0");

            forecastOnPrice = GetCompleteForecastOnAnAverage(presentTime, "MA12");
            var valuePoint2 = forecastOnPrice.AverageOrStop(1, 12);
            string strPoint2 = valuePoint2.Item2.ToString("0.00");
            string strTime2 = (valuePoint2.Item1.TotalMinutes / 5).ToString("0");

            forecastOnPrice = GetCompleteForecastOnAnAverage(presentTime, "MA24");
            var valuePoint3 = forecastOnPrice.AverageOrStop(1, 24);
            string strPoint3 = valuePoint3.Item2.ToString("0.00");
            string strTime3 = (valuePoint3.Item1.TotalMinutes / 5).ToString("0");

            return $"{strPoint1}_{strTime1}_{strPoint2}_{strTime2}_{strPoint3}_{strTime3}_";
        }

        private string GetForecastOnPrice(double currentValue, DateTime presentTime)
        {
            string status = "";

            var ATR = _params.Indicators.Where(i => i.Name == "ATR").First();
            var range = new DateRange(_beginWindow, _endWindow);
            var ratr = ((ATR.TakeSnapShot(range).Last().CloseValue / currentValue) * 100);
            var stopLossShort = ratr;
            var stopLossLong = stopLossShort * -1;

            var goodRange = ratr;

            status = "IGNORED";

            var forecastOnPrice = GetCompleteForecast(currentValue, presentTime);

            if (forecastOnPrice.HighestDifference <= goodRange / 2 &&
                forecastOnPrice.LowestDifFerence >= (goodRange / 2) * -1)
                status = "ZERO";


            if (forecastOnPrice.CountHighestDifference(1, _params.ForecastWindow, goodRange) >= 1)
            {
                if (forecastOnPrice.GetLowestDifferente(1, Convert.ToInt32(_params.ForecastWindow / 3)) > stopLossLong)
                    status = "LONG";
            }


            if (forecastOnPrice.CountLowestDifference(1, _params.ForecastWindow, goodRange * -1) >= 1)
            {
                if (forecastOnPrice.GetHighestDifference(1, Convert.ToInt32(_params.ForecastWindow / 3)) < stopLossShort)
                    status = "SHORT";
            }

            return status;
        }

        public Bitmap ResizeImage(Image image, int width, int height)
        {
            var destRect = new Rectangle(0, 0, width, height);
            var destImage = new Bitmap(width, height);

            destImage.SetResolution(image.HorizontalResolution, image.VerticalResolution);

            using (var graphics = Graphics.FromImage(destImage))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                using (var wrapMode = new ImageAttributes())
                {
                    wrapMode.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.DrawImage(image, destRect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, wrapMode);
                }
            }

            return destImage;
        }

        private static string TransformImageToBase64(Bitmap image)
        {
            MemoryStream ms = new MemoryStream();
            image.Save(ms, System.Drawing.Imaging.ImageFormat.Gif);
            ms.Flush();
            ms.Position = 0;

            return Convert.ToBase64String(ms.ToArray());
        }

        public string GetPrediction(Bitmap img)
        {

            string payload = TransformImageToBase64(img);

            var httpClient = new HttpClient();
            string ret = null;

            try
            {
                httpClient.BaseAddress = new Uri("http://10.0.0.188:81");

                string url = "/predict?identifier=label";
                var res = httpClient.PostAsync(url, new StringContent(payload));
                if (res.Wait(5000))
                {
                    var predictions = res.Result.Content.ReadAsStringAsync();
                    if (predictions.Wait(5000))
                    {
                        ret = predictions.Result;
                    }
                    else
                        Console.WriteLine("Timeout...");
                }
                else
                    Console.WriteLine("Time the fuck out!");

            }
            catch (Exception err)
            {
                Console.WriteLine(err.ToString());
            }

            return ret;

        }


        private static DateTime _lastPredictionTime = DateTime.MinValue;

        public string GetPredictionData()
        {
            string line = null;
            var response = GetPrediction(_img);

            if (response != null)
            {
                var cardRange = new DateRange(_beginWindow, _endWindow);
                var stopLossAtrAbs = _indicators.Where(i => i.Name == "ATR").First().TakeSnapShot(cardRange).Last().AmountValue;

                var peek = HasRecentPeakVolume_byAvg();
                Console.WriteLine($"{response} - {peek}");
                Console.WriteLine($"Wv: {GetWindowVariation():0.00}%");

                if (response == "JacOpen" && peek && GetWindowVariation() >= 2)
                {

                    var lastCandlePlusOne = GetLastCandlePlusOne();
                    var lastCandle = GetLastCandle();
                    var firstCandle = GetFirstCandle();

                    if ((firstCandle.OpenTime - _lastPredictionTime).TotalMinutes > 60)
                    {

                        var stopLossAtr = (stopLossAtrAbs / lastCandle.CloseValue) * 100;

                        double stopLoss = stopLossAtr * -0.5;

                        double presentClosing = lastCandle.CloseValue;

                        double result = 0;

                        var forecastInfo = this.GetCompleteForecast(lastCandle.CloseValue, lastCandle.OpenTime);
                        var forecastAvgInfo = this.GetCompleteForecastOnAnAverage(lastCandle.OpenTime, "MA6");

                        var res = forecastAvgInfo.AverageOrStop(-1, 6);
                        result = res.Item2;

                        line = $"{firstCandle.OpenTime:dd-MM hh:mm};{result:0.00};{forecastInfo.HighestDifference:0.00};{forecastInfo.LowestDifFerence:0.00};{forecastInfo.AllValues}";

                        _lastPredictionTime = firstCandle.OpenTime;
                    }

                }
            }

            return line;
        }



        public string GetTag(double closeValue, DateTime closeTime)
        {
            return this.GetForecastOnPriceTrend(closeValue, closeTime);
        }
        public string GetTag(double closeValue, DateTime closeTime, string averageName)
        {
            return GetMultipleTags_Long(closeValue, closeTime);
        }

        public string GetFileName(string tag, string asset)
        {
            // string fileName = String.Format("{0} {1} {2:dd_MMM_yyyy HH_mm} {3}",
            //     _range.GetSpan().TotalSeconds,
            //     asset,
            //     _range.End,
            //     Truncate(tag, 100).Replace(",", "_")
            // );

            string fileName = String.Format("{0:dd_MMM_yyyy HH_mm}",
                _range.End
            );

            //var fileName = $"{_range.GetSpan().TotalSeconds} {tag}";

            return fileName;
        }

        private string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) { return value; }

            return value.Substring(0, Math.Min(value.Length, maxLength));
        }

        public string SaveFiles(string uniqueDirectoryName, string tagName, int imgSequenceCluster)
        {
            string name = tagName;
            string fileName = $"{imgSequenceCluster:00}_{name}.gif";

            var dirPath = uniqueDirectoryName + "/" + _beginWindow.ToString("MM_yyyy");
            if (!Directory.Exists(dirPath))
                Directory.CreateDirectory(dirPath);

            using (FileStream fs = File.OpenWrite(Path.Combine(dirPath, fileName)))
            {
                this.writeImage(fs);
            }

            return fileName;
        }

        private void writeImage(Stream s)
        {
            _img.Save(s, System.Drawing.Imaging.ImageFormat.Gif);
            s.Flush();
            s.Position = 0;
        }

        private double GetAverageValue(string averageName)
        {
            var average = _params.Indicators.Where(i => i.Name == averageName).FirstOrDefault();
            double ret = 0;

            if (average != null)
            {
                var listPoints = average.TakeSnapShot(_range);
                ret = listPoints.Last().AmountValue;
            }
            else
            {
                throw new ArgumentException(String.Format("Average {0} not found", averageName));
            }

            return ret;
        }

        public bool AboveAverage(double firstPrice, double lastPrice)
        {

            bool ret = false;
            var fiftyAverage = _indicators.Where(i => i.Name == _params.AverageVerification).FirstOrDefault();
            if (fiftyAverage == null)
                ret = true;
            else
            {
                var listPoints = fiftyAverage.TakeSnapShot(_range);
                var avgPrice = listPoints.Last().AmountValue;
                if (lastPrice >= avgPrice)
                    ret = true;
            }

            return ret;
        }

        private string TagForecastOne(double forecast)
        {
            string tag = "Zero";
            if (forecast >= 0.5)
                tag = "Long";
            else if (forecast <= -0.5)
                tag = "Short";

            return tag;
        }

        private TagGroup TagForecastLayered(string forecastWindow, double forecast)
        {
            TagGroup tag = new TagGroup();
            tag.GroupName = forecastWindow;

            if (forecast <= -1)
            {
                if (forecast < -8)
                    tag.Tags.Add("Minus08Plus");
                if (forecast <= -5)
                    tag.Tags.Add("Minus05TO08");
                if (forecast <= -3)
                    tag.Tags.Add("Minus03TO05");
                if (forecast <= -2)
                    tag.Tags.Add("Minus02TO03");
                if (forecast <= -1)
                    tag.Tags.Add("Minus01TO02");
            }
            else
            {
                if (forecast >= 1)
                {
                    if (forecast >= 8)
                        tag.Tags.Add("Plus08Plus");
                    if (forecast >= 5)
                        tag.Tags.Add("Plus05TO08");
                    if (forecast >= 3)
                        tag.Tags.Add("Plus03TO05");
                    if (forecast >= 2)
                        tag.Tags.Add("Plus02TO03");
                    if (forecast >= 1)
                        tag.Tags.Add("Plus01TO02");
                }
                else
                {
                    tag.Tags.Add("Zero");
                }
            }

            return tag;
        }
    }

    public class PredictionReportItem
    {
        public string Tag { get; set; }
        public double Result { get; set; }

        public string Status { get; set; }

        public double HighestDifference { get; set; }

        public double LowestDifFerence { get; set; }

        public int LastPredictionDistance { get; set; }
        public string Picture { get; set; }

        public double RealResult { get; set; }
        public uint TimeToGetHigh { get; internal set; }
        public TimeSpan OperationDurantion { get; internal set; }
        public string StatusTarget { get; internal set; }
    }

    public class PredictionReport
    {
        private List<PredictionReportItem> _items;

        public PredictionReport()
        {
            _items = new List<PredictionReportItem>();
        }

        public void AddReportItem(PredictionReportItem item)
        {
            _items.Add(item);
        }

        public List<PredictionReportItem> GetEntries()
        {
            return _items;
        }

        public PredictionReportResult GetResult(RunParameters parans)
        {
            PredictionReportResult ret = new PredictionReportResult();

            double PAndL = 0;
            _items.ForEach(i =>
            {
                i.RealResult = i.Result;

                i.RealResult += Convert.ToDouble((-0.06 * 2));

                PAndL += i.RealResult;
            });

            ret.AllEntries = _items;
            ret.AverageTime = _items.Average(i => i.TimeToGetHigh);
            ret.PAndL = PAndL;
            ret.ExperimentName = parans.ExperimentName;
            var okCount = Convert.ToDouble(ret.AllEntries.Where(i => i.Status == "OK").Count());
            var total = Convert.ToDouble(_items.Count);
            var okCountTarget = Convert.ToDouble(ret.AllEntries.Where(i => i.StatusTarget == "OK").Count());

            ret.SuccessRate = (okCount / total) * 100;
            ret.SuccessRateTarget = (okCountTarget / total) * 100;

            return ret;
        }
    }

    public class PredictionReportResult
    {
        public List<PredictionReportItem> AllEntries { get; internal set; }
        public double PAndL { get; internal set; }

        public double SuccessRate { get; internal set; }

        public string ExperimentName { get; internal set; }
        public double AverageTime { get; internal set; }
        public double SuccessRateTarget { get; internal set; }
    }

    public struct ForecastResult
    {
        public int PeriodNumber
        {
            get;
            set;
        }

        public double LowestDifFerence
        {
            get;
            set;
        }

        public double HighestDifference
        {
            get;
            set;
        }

        public double StartValue { get; internal set; }
        public double ForecastLong { get; internal set; }
        public double ForecastShort { get; internal set; }
        public uint TimeToGetHigh { get; internal set; }
        public string AllValues { get; internal set; }
        public double FinalClosing { get; internal set; }
        public List<IStockPointInTime> ClippedCandles { get; internal set; }
        public uint TimeToGetLow { get; internal set; }
        public List<IStockPointInTime> PureCandles { get; internal set; }

        public double GetLowestDifferente(int beginPeriod, int endPeriod)
        {
            var lowestList = ClippedCandles.Where(c =>
            {
                return c.Periodo >= beginPeriod && c.Periodo <= endPeriod;
            });

            if (lowestList.Count() > 0)
            {
                var lowest = lowestList.Min(c => c.LowestValue);

                var diffLowest = ((lowest - StartValue) / StartValue) * 100;

                return diffLowest;
            }
            else
            {
                Console.WriteLine("OPA AQUI!!!");
                return 0;
            }

            //_allCandles.ToList().GetRange(comparePeriod + 3, periodsInTheFuture - 3);
        }

        public double GetLowestCloseDifferente(int beginPeriod, int endPeriod)
        {
            var list = ClippedCandles.Where(c =>
            {
                return c.Periodo >= beginPeriod && c.Periodo <= endPeriod;
            });

            if (list.Count() > 0)
            {
                var lowest = list.Min(c => c.CloseValue);

                var diffLowest = ((lowest - StartValue) / StartValue) * 100;

                return diffLowest;
            }
            else
            {
                Console.WriteLine("OPA AQUI!!!");
                return 0;
            }

            //_allCandles.ToList().GetRange(comparePeriod + 3, periodsInTheFuture - 3);
        }
        /*
        stop: If the value is less then zero we calculate de AvgOrStop considering a long position, else we consider the calculations
        as a short position
        */
        public Tuple<TimeSpan, double> AverageOrStop(double stop, int numPeriodos = 6)
        {
            double ret = 0;
            TimeSpan duration = new TimeSpan();

            if (ClippedCandles.Count() > 0)
            {
                DateTime start = PureCandles.First().PointInTime_Open;
                var startValue = PureCandles.First().CloseValue;

                for (int i = 0; i < PureCandles.Count(); i++)
                {
                    var candle = PureCandles[i];
                    var avg = ClippedCandles[i];
                    double value = 0;

                    if (avg.Periodo >= numPeriodos) //Depois de 6 periodos
                    {
                        if (stop < 0)
                        {
                            if (candle.CloseValue < avg.AmountValue)
                                value = avg.AmountValue;
                        }
                        else
                        {
                            if (candle.CloseValue > avg.AmountValue)
                                value = avg.AmountValue;
                        }
                    }

                    var diff = ((candle.LowestValue - startValue) / startValue) * 100;

                    if (stop < 0)
                    {
                        if (diff < stop)
                            value = startValue * (1 + (stop / 100));
                    }
                    else
                    {
                        if (diff > stop)
                            value = startValue * (1 + (stop / 100));
                    }

                    if (value != 0)
                    {
                        duration = candle.PointInTime_Open - start;
                        ret = ((value - startValue) / startValue) * 100;
                        break;
                    }
                }

                if (ret == 0)
                {
                    ret = 0;
                    duration = new TimeSpan(5, 0, 0);
                }
            }

            return new Tuple<TimeSpan, double>(duration, ret);
        }
        public Tuple<TimeSpan, double> TargetOrStop(double target, double stop)
        {
            double ret = 0;
            var startValue = StartValue;
            TimeSpan duration = new TimeSpan();

            DateTime start = ClippedCandles.First().PointInTime_Open;

            ClippedCandles.ForEach(c =>
            {
                if (ret == 0)
                {
                    var diff = ((c.CloseValue - startValue) / startValue) * 100;
                    if (diff >= target)
                        ret = diff; //sucess

                    if (diff < stop)
                        ret = stop; //stop

                    if (ret != 0)
                    {
                        duration = c.PointInTime_Open - start;
                    }
                }
            });

            return new Tuple<TimeSpan, double>(duration, ret);
        }

        public double GetHighestDifference(int beginPeriod, int endPeriod)
        {
            var maxList = ClippedCandles.Where(c =>
            {
                return c.Periodo >= beginPeriod && c.Periodo <= endPeriod;
            });

            if (maxList.Count() > 0)
            {
                var max = maxList.Max(c => c.HighestValue);

                var diffMax = ((max - StartValue) / StartValue) * 100;

                return diffMax;
            }
            else
            {
                return 0;
            }

        }

        public double GetHighestCloseDifference(int beginPeriod, int endPeriod)
        {
            var maxList = ClippedCandles.Where(c =>
            {
                return c.Periodo >= beginPeriod && c.Periodo <= endPeriod;
            });

            if (maxList.Count() > 0)
            {
                var max = maxList.Max(c => c.CloseValue);

                var diffMax = ((max - StartValue) / StartValue) * 100;

                return diffMax;
            }
            else
            {
                return 0;
            }

        }

        public int CountHighestDifference(double startValue, int beginPeriod, int endPeriod, double diff)
        {
            if (startValue == -1)
                startValue = this.StartValue;

            var maxList = ClippedCandles.Where(c =>
            {
                return c.Periodo >= beginPeriod && c.Periodo <= endPeriod;
            });

            if (maxList.Count() > 0)
            {
                var count = maxList.Count((c) =>
                {
                    var thisDiff = ((c.HighestValue - startValue) / startValue) * 100;

                    return thisDiff >= diff;
                });

                return count;
            }
            else
            {
                return 0;
            }
        }

        public int CountHighestDifference(int beginPeriod, int endPeriod, double diff)
        {
            return CountHighestDifference(this.StartValue, beginPeriod, endPeriod, diff);
        }

        public int CountLowestDifference(double startValue, int beginPeriod, int endPeriod, double diff)
        {
            if (startValue == -1)
                startValue = this.StartValue;

            var minList = ClippedCandles.Where(c =>
            {
                return c.Periodo >= beginPeriod && c.Periodo <= endPeriod;
            });

            if (minList.Count() > 0)
            {
                var count = minList.Count((c) =>
                {
                    var thisDiff = ((c.LowestValue - startValue) / startValue) * 100;

                    return thisDiff <= diff;
                });

                return count;
            }
            else
            {
                return 0;
            }
        }

        public int CountGreatherCloseDifference(double startValue, int beginPeriod, int endPeriod, double diff)
        {
            var list = ClippedCandles.Where(c =>
            {
                return c.Periodo >= beginPeriod && c.Periodo <= endPeriod;
            });

            if (list.Count() > 0)
            {
                var count = list.Count((c) =>
                {
                    var thisDiff = ((c.CloseValue - startValue) / startValue) * 100;

                    return thisDiff >= diff;
                });

                return count;
            }
            else
            {
                return 0;
            }
        }

        public int CountLesserCloseDifference(double startValue, int beginPeriod, int endPeriod, double diff)
        {
            var list = ClippedCandles.Where(c =>
            {
                return c.Periodo >= beginPeriod && c.Periodo <= endPeriod;
            });

            if (list.Count() > 0)
            {
                var count = list.Count((c) =>
                {
                    var thisDiff = ((c.CloseValue - startValue) / startValue) * 100;

                    return thisDiff <= diff;
                });

                return count;
            }
            else
            {
                return 0;
            }
        }


        public int CountLowestDifference(int beginPeriod, int endPeriod, double diff)
        {
            return CountLowestDifference(this.StartValue, beginPeriod, endPeriod, diff);
        }

        public double GetAverageDifference(int beginPeriod, int endPeriod)
        {
            var avgList = ClippedCandles.Where(c =>
            {
                return c.Periodo >= beginPeriod && c.Periodo <= endPeriod;
            });

            if (avgList.Count() > 0)
            {
                var avg = avgList.Average(c => c.CloseValue);

                var diffAvg = ((avg - StartValue) / avg) * 100;

                return diffAvg;
            }
            else
            {
                return 0;
            }

            //_allCandles.ToList().GetRange(comparePeriod + 3, periodsInTheFuture - 3);
        }
    }

    public class TagGroup
    {
        public string GroupName
        {
            get;
            set;
        }

        public List<string> Tags
        {
            get;
            set;
        }

        public TagGroup()
        {
            Tags = new List<string>();
        }

        public static string ToString(List<TagGroup> groups)
        {
            StringBuilder sb = new StringBuilder(50);

            foreach (var group in groups)
            {
                foreach (string tag in group.Tags)
                {
                    sb.AppendFormat("{0}-{1},", group.GroupName, tag);
                }
            }
            string ret = sb.ToString();
            if (ret.Length > 1)
                ret = ret.Substring(0, ret.Length - 1); //To remove that last stinking comma

            return ret;
        }
    }


    public class CardFeatures
    {

    }
}