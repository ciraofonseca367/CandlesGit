using System.Net;
using System.IO;
using System;
using System.Collections.Generic;
using System.Drawing;
using Midas.Core.Common;
using System.Text;
using Google.Cloud.AutoML.V1;
using System.Linq;
using Midas.Util;
using Midas.Core;
using Midas.Core.Forecast;

namespace Midas.Core.Card
{
    public class Card
    {
        public static DateTime AngelBirth = new DateTime(2008, 01, 01);


        private DateRange _range;
        private Bitmap _img;
        private IStockPointInTime[] _allCandles;

        private List<Indicators.CalculatedIndicator> _indicators;

        private static DateTime _lastOperationEnd = DateTime.MinValue;
        private static Candle _lastTryLastCandle;

        private RunParameters _params;
        private static Candle _lastPrediction;

        private DateTime _beginWindow, _endWindow, _futureDate;

        public Card(
                Bitmap img, IStockPointInTime[] allCandles, DateTime timeStamp,
                DateTime beginWindow, DateTime endWindow, DateTime futureDate,
                List<Indicators.CalculatedIndicator> indicators, RunParameters @params)
        {
            this._range = new DateRange(Card.AngelBirth, timeStamp);
            this._img = img;
            this._allCandles = allCandles;

            this._beginWindow = beginWindow;
            this._endWindow = endWindow;
            this._futureDate = futureDate;

            _indicators = indicators;
            _params = @params;
        }

        private Candle GetFirstCandle()
        {
            return (Candle)_allCandles.First();
        }

        public Candle GetLastCandle()
        {
            return (Candle)_allCandles.Where(c => c.PointInTime_Open.ToString("yyyy-MM-dd HH:mm") == _endWindow.ToString("yyyy-MM-dd HH:mm")).FirstOrDefault();
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

            if (clip.Count() > 0)
            {
                highestValue = clip.Max(c => c.HighestValue);
                lowestValue = clip.Min(c => c.LowestValue);

                lowest = ((lowestValue - presentClosing) / presentClosing) * 100;
                highest = ((highestValue - presentClosing) / presentClosing) * 100;

                futureClosing = ((clip.Last().CloseValue - presentClosing) / presentClosing) * 100;

                var timeHighest = clip.Where(c => c.HighestValue == highestValue).FirstOrDefault().PointInTime_Open;
                var timeLowest = clip.Where(c => c.LowestValue == lowestValue).FirstOrDefault().PointInTime_Open;

                periods = Convert.ToUInt32((timeHighest - presentTime).TotalMinutes / 5);
                periodsLowest = Convert.ToUInt32((timeLowest - presentTime).TotalMinutes / 5);
            }

            return new ForecastResult()
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
                StartValue = presentClosing
            };
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

            var halfForecastWindow = Convert.ToInt32(_params.ForecastWindow/2);

            // var ATR = _params.Indicators.Where(i => i.Name == "ATR").First();
            // var range = new DateRange(_beginWindow, _endWindow);
            // var stopLossShort = ((ATR.TakeSnapShot(range).Last().CloseValue / currentValue) * 100);
            // var stopLossLong = stopLossShort *-1;

            var forecastOnAverage = GetCompleteForecastOnAnAverage(presentTime, averageName);

            status = "ND";

            if(forecastOnAverage.GetHighestDifference(1, _params.ForecastWindow) <= limitToPredictLong &&
            forecastOnAverage.GetLowestDifferente(1, _params.ForecastWindow) >= limitToPredictShort)
                status = "ZERO";
            

            if(forecastOnAverage.GetHighestDifference(1, _params.ForecastWindow) > limitToPredictLong &&
                forecastOnAverage.GetLowestDifferente(1,_params.ForecastWindow) > 0)
            {
                status = "LONG";
            }
            if(forecastOnAverage.GetLowestDifferente(1, _params.ForecastWindow) < limitToPredictShort &&
                forecastOnAverage.GetLowestDifferente(1,_params.ForecastWindow) < 0)
            {
                status = "SHORT";
            }


            return status;
        }


        private string GetForecast(double currentValue, DateTime presentTime)
        {
            var forecast = GetCompleteForecast(currentValue, presentTime);

            double limitToPredictLongLevel1 = 1;
            double limitToPredictLongLevel2 = 2;
            double limitToPredictLongLevel3 = 3;
            double stopLossLong = -0.2;
            double limitToPredictShortLevel1 = -1;
            double limitToPredictShortLevel2 = -2;
            double limitToPredictShortLevel3 = -3;
            double stopLossShort = 0.2;

            string status = "";

            if (forecast.GetLowestDifferente(0, 15) > stopLossLong)
            {
                if (forecast.ForecastLong >= stopLossShort)
                    status = "LONG0001";
                if (forecast.ForecastLong >= limitToPredictLongLevel1)
                    status += ",LONG0102";
                if (forecast.ForecastLong >= limitToPredictLongLevel2)
                    status += ",LONG0203";
                if (forecast.ForecastLong >= limitToPredictLongLevel3)
                    status += ",LONG0304";

                if (String.IsNullOrEmpty(status))
                    status = "Zero";
            }
            else if (forecast.GetHighestDifference(0, 15) < stopLossShort)
            {
                if (forecast.ForecastLong <= stopLossLong)
                    status = "SHORT0001";
                if (forecast.ForecastShort <= limitToPredictShortLevel1)
                    status += ",SHORT0102";
                if (forecast.ForecastShort <= limitToPredictShortLevel2)
                    status += ",SHORT0203";
                if (forecast.ForecastShort <= limitToPredictShortLevel3)
                    status += ",SHORT0304";

                if (String.IsNullOrEmpty(status))
                    status = "Zero";
            }
            else if (forecast.ForecastLong <= limitToPredictLongLevel1 && forecast.ForecastShort >= limitToPredictShortLevel1)
                status = "CONFUSED";
            else
            {
                status = "ND";
            }

            return status;
        }

        public string GetTag(double closeValue, DateTime closeTime)
        {
            return this.GetForecast(closeValue, closeTime);
        }
        public string GetTag(double closeValue, DateTime closeTime, string averageName)
        {
            return this.GetForecastOnAnAverage(closeValue, closeTime, averageName);
        }

        public string GetFileName(string tag)
        {
            string fileName = String.Format("{0} {1:dd_MMM HH_mm_ss} {2}.gif",
                _range.GetSpan().TotalSeconds,
                _range.End,
                Truncate(tag, 100).Replace(",", "_")
            );

            return fileName;
        }

        private string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) { return value; }

            return value.Substring(0, Math.Min(value.Length, maxLength));
        }

        public string SaveToFile(string uniqueDirectoryName, string tagName)
        {
            string fileName = GetFileName(tagName);

            using (FileStream fs = File.OpenWrite(Path.Combine(uniqueDirectoryName, fileName)))
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

        public List<PredictionResult> GetPrediction(float scoreThreshold)
        {
            var client = ForecastFactory.GetDefaultForecaster();

            return client.Predict(_img, scoreThreshold,0, DateTime.Now);
        }

        public PredictionReportItem WritePrediction(StreamWriter output, float scoreThreshold, string fileName)
        {
            PredictionReportItem reportItem = null;
            double localTarget;

            string status = null;
            string statusTarget = null;
            if (AboveAverage(GetFirstCandle().CloseValue, GetLastCandle().CloseValue))
            {

                var client = ForecastFactory.GetDefaultForecaster();

                var response = client.Predict(_img, 0.1f, 0, DateTime.MinValue);

                if (response != null)
                {
                    var cardRange = new DateRange(_beginWindow, _endWindow);
                    var stopLossAtrAbs = _indicators.Where(i => i.Name == "ATR").First().TakeSnapShot(cardRange).Last().AmountValue;

                    float lowerBoundToPredict = 0;
                    float upperBoundToPredict = 0;

                    PredictionResult first = null;
                    if(response.Count() > 0)
                    {
                        first = response.First();
                    }

                    var predictionLong = response.Where(p => p.Tag == "LONG").FirstOrDefault();
                    var predictionShort = response.Where(p => p.Tag == "SHORT").FirstOrDefault();
                    var predictionZero = response.Where(p => p.Tag == "ZERO").FirstOrDefault();

                    int rankShort=0;
                    int rankLong = 0;
                    double scoreLong = predictionLong == null ? 0 : predictionLong.Score;
                    double scoreZero = predictionZero == null ? 0 : predictionZero.Score;
                    for(int i=0;i<response.Count;i++)
                    {
                        if(response[i].Tag == "SHORT")
                            rankShort = i+1;

                        if(response[i].Tag == "LONG")
                            rankLong = i+1;
                    }     

                    var diffLONG_ZERO = (Math.Abs(scoreLong - scoreZero) / 1)*100;

                    if (rankLong == 1 && predictionLong.Score >= _params.ScoreThreshold)
                    {
                        localTarget = response.Max(r => r.RatioUpperBound) * 0.75;

                        var lastCandlePlusOne = GetLastCandlePlusOne();
                        var lastCandle = GetLastCandle();
                        var hourAverageValue = GetAverageValue("MA12");

                        PredictionResult result2 = null;
                        double score2 = 0;
                        string tag2 = "";
                        if(response.Count() > 1)
                        {
                            result2 = response[1];
                            tag2 = result2.Tag;
                            score2 = result2.Score;
                        }

                        var stopLossAtr = (stopLossAtrAbs / lastCandle.CloseValue) * 100;

                        double stopLoss = stopLossAtr * -0.5;

                        // if (
                        //     !_params.DelayedTriggerEnabled ||
                        //     (_params.DelayedTriggerEnabled
                        //         &&
                        //         (
                        //             (currentValue <= hourAverageValue && lastCandlePlusOne.GetPureIndecisionThreshold() >= _params.IndecisionThreshold) ||
                        //             currentValue > hourAverageValue
                        //         )
                        //     )
                        // )

                        // if (
                        //     !_params.DelayedTriggerEnabled ||
                        //     (_params.DelayedTriggerEnabled &&
                        //         lastCandle.Direction == CandleDirection.Up ||                            
                        //         (lastCandle.Direction == CandleDirection.Down && lastCandlePlusOne.GetPureIndecisionThreshold() >= _params.IndecisionThreshold)
                        //     )
                        // )

                        if (
                            _params.DelayedTriggerEnabled == false ||
                            (_params.DelayedTriggerEnabled &&
                                (
                                    lastCandle.Direction == CandleDirection.Down &&
                                    lastCandlePlusOne.GetPureIndecisionThreshold() >= _params.IndecisionThreshold
                                ) ||
                            lastCandle.Direction == CandleDirection.Up
                            )
                        )
                        {

                            int lastPredictionDistance = -1;
                            if (_lastPrediction != null)
                                lastPredictionDistance = Convert.ToInt32((GetFirstCandle().PointInTime_Close - _lastPrediction.PointInTime_Close).TotalMinutes);

                            if ((score2 < 0.5) &&
                                (lastCandle.OpenTime > _lastOperationEnd && (lastPredictionDistance / 5)+1 >= _params.AllowedConsecutivePredictions)
                                )
                            {
                                lowerBoundToPredict = 0.5f;
                                upperBoundToPredict = 1.0f;

                                status = "OK";
                                statusTarget = "OK";

                                double presentClosing = 0;
                                if (_params.DelayedTriggerEnabled)
                                {
                                    if (lastCandle.Direction == CandleDirection.Down)
                                    {
                                        if (lastCandlePlusOne.GetPureIndecisionThreshold() >= _params.IndecisionThreshold)
                                            presentClosing = lastCandlePlusOne.CloseValue;

                                        // if(lastCandlePlusTwo.GetPureIndecisionThreshold() >= _params.IndecisionThreshold && presentClosing == 0)
                                        //     presentClosing = lastCandlePlusTwo.CloseValue;
                                    }
                                    else
                                        presentClosing = lastCandle.CloseValue;
                                }
                                else
                                {
                                    presentClosing = lastCandle.CloseValue;
                                }

                                double result = 0;
                                TimeSpan operationDuration;

                                var forecastInfoAvg = this.GetCompleteForecastOnAnAverage(lastCandle.OpenTime, "MA12");
                                var forecastInfo = this.GetCompleteForecast(lastCandle.CloseValue,lastCandle.OpenTime);

                                var maxAvg = forecastInfoAvg.ForecastLong;
                                var res = forecastInfoAvg.AverageOrStop(stopLoss);
                                result = res.Item2;

                                operationDuration = res.Item1;

                                _lastOperationEnd = lastCandle.CloseTime.Add(operationDuration);
                                // if(result > 1.1)
                                //     _lastOperationEnd.AddHours(4);

                                if (result < 0.12)
                                    status = "BAD";

                                if (result < _params.Target1)
                                    statusTarget = "BAD";

                                double lastPredictionLastThreshold = -100;

                                lastPredictionLastThreshold = lastCandlePlusOne.GetPureIndecisionThreshold();

                                reportItem = new PredictionReportItem()
                                {
                                    Tag = first.Tag,
                                    Result = result,
                                    Status = status,
                                    StatusTarget = statusTarget,
                                    HighestDifference = forecastInfo.HighestDifference,
                                    LowestDifFerence = forecastInfo.LowestDifFerence,
                                    Picture = fileName,
                                    TimeToGetHigh = forecastInfo.TimeToGetHigh,
                                    OperationDurantion = operationDuration
                                };

                                var line = String.Format(
                                    "{0};{1:0.0000000};{2:0.0000000};{3:0.0000000};{4};{5}; {6:0.0000000};{7:0.0000000}; {8}; {9: 0.0000000}; {10}; {11}; {12:0.00}; {13}; {14}, {15:0.000}, {16:0.000}, {17}, {18:0.000}",
                                    first.Tag, lowerBoundToPredict, upperBoundToPredict, result, status, statusTarget, forecastInfo.HighestDifference,
                                    forecastInfo.LowestDifFerence, 0, lastPredictionLastThreshold, fileName, _params.DelayedTriggerEnabled, _params.IndecisionThreshold, forecastInfo.TimeToGetHigh,
                                    forecastInfo.AllValues, stopLoss, score2, tag2, diffLONG_ZERO);

                                output.WriteLine(line);
                                output.Flush();
                            }

                            _lastPrediction = GetFirstCandle();

                        }

                    }
                }
            }

            _lastTryLastCandle = GetLastCandle();

            return reportItem;
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

            if (forecast <= -0.1)
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
                if (forecast <= -0.5)
                    tag.Tags.Add("MinusHalfTO01");
                if (forecast <= -0.1)
                    tag.Tags.Add("MinusDOT1TOHalf");
            }
            else
            {
                if (forecast >= 0.1)
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
                    if (forecast >= 0.5)
                        tag.Tags.Add("PlusHalfTO01");
                    if (forecast > 0.1)
                        tag.Tags.Add("PlusDOT1TOHalf");
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

        public string ToCSVLine(RunParameters parans)
        {
            var result = GetResult(parans);

            return $"{result.PAndL.ToString("0.0000")};{result.SuccessRate.ToString("0.0000")}; {result.AllEntries.Count()}; {result.ExperimentName}; {parans.ScoreThreshold};{parans.StopLoss}; {parans.AverageVerification}; {parans.Range.Start.ToString("yyyy/MM/dd")}; {parans.Range.End.ToString("yyyy/MM/dd")}; {parans.DelayedTriggerEnabled}; {parans.IndecisionThreshold}; {parans.Target1.ToString("0.00")}; {parans.Target2.ToString("0.00")}; {parans.Target3.ToString("0.00")};{result.SuccessRateTarget.ToString("0.0000")};{parans.TagFilter}";
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

                var diffLowest = ((lowest - StartValue) / lowest) * 100;

                return diffLowest;
            }
            else
            {
                Console.WriteLine("OPA AQUI!!!");
                return 0;
            }

            //_allCandles.ToList().GetRange(comparePeriod + 3, periodsInTheFuture - 3);
        }

        public Tuple<TimeSpan, double> AverageOrStop(double stop)
        {
            double ret = 0;
            var startValue = StartValue;
            TimeSpan duration = new TimeSpan();

            DateTime start = ClippedCandles.First().PointInTime_Open;

            for(int i=0;i<PureCandles.Count();i++)
            {
                var candle = PureCandles[i];
                var avg = ClippedCandles[i];
                double value = 0;

                if(candle.LowestValue < stop)
                {
                    value = stop;
                }                

                if(avg.Periodo > 12) //Depois de 12 periodos
                {
                    if(candle.CloseValue < avg.AmountValue)
                        value = avg.AmountValue;
                }

                if(value != 0)
                {
                    duration = candle.PointInTime_Open - start;
                    ret = ((value - startValue) / startValue) * 100;
                    break;
                }   
            }

            if(ret == 0)
            {
                ret = this.FinalClosing;
                duration = new TimeSpan(3,0,0);
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

                    if(ret != 0)
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

                var diffAvg = ((max - StartValue) / max) * 100;

                return diffAvg;
            }
            else
            {
                return 0;
            }

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

}