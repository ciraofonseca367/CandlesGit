using System.Net;
using System.IO;
using System;
using Midas.Core.Card;
using System.Linq;
using MongoDB.Driver;
using Midas.Util;
using System.Drawing;
using Midas.Core.Common;
using Midas.Core;
using Midas.Core.Chart;
using System.Collections.Generic;
using Midas.Core.Indicators;
using System.Drawing.Imaging;

namespace Midas
{
    public class RoundRobinNumber
    {
        private int _current;
        private int _number;
        public RoundRobinNumber(int number)
        {
            _current = 1;
            _number = number;
        }

        public int GetNext()
        {
            var ret = _current;
            if (_current == _number)
                _current = 1;

            _current++;

            return ret;
        }
    }


    class Program
    {
        private static List<CalculatedIndicator> _indicators;

        public static void Main(string[] args)
        {
            RunParameters.CreateInstace(args);

            var runParams = RunParameters.GetInstance();

            System.Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", "/Users/cironola/Downloads/candlesfaces-fdbef15d7ab2.json");

            runParams.WriteToConsole();

            var res = CandlesGateway.GetCandlesFromFile(
                runParams.Asset,
                runParams.Range,
                runParams.CandleType
            );

            var outputDir = Directory.CreateDirectory(
                Path.Combine(runParams.OutputDirectory, runParams.ExperimentName, runParams.Asset)
                );

            var rootOutputDir = new DirectoryInfo(Path.Combine(runParams.OutputDirectory, runParams.ExperimentName));

            PredictionReport report = null;
            if (runParams.RunMode == RunModeType.Predict)
                report = new PredictionReport();

            _indicators = runParams.Indicators;

            int imgSequenceCluster = 1;

            using (var csvFile = File.Open(Path.Combine(rootOutputDir.FullName, runParams.OutputFile), FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                StreamWriter csvWriter = new StreamWriter(csvFile);

                int runTotalMinutes = Convert.ToInt32(runParams.Range.GetSpan().TotalMinutes);
                var progress = new ConsoleProgressBar(runTotalMinutes);

                var candleMovieRoll = new FixedSizedQueue<IStockPointInTime>((runParams.WindowSize + runParams.MaxForecastWindow) * 5);

                IStockPointInTime[] inicialCandles = res.Read(runParams.WindowSize + runParams.MaxForecastWindow);
                if (inicialCandles == null || inicialCandles.Count() == 0)
                    throw new ArgumentException("It was not possible to read from the stream...");

                foreach (var c in inicialCandles)
                    FeedIndicators(c, "Main");

                var initialVolumes = inicialCandles.Select(p => new VolumeIndicator(p));

                foreach (var v in initialVolumes)
                    FeedIndicators(v, "Volume");

                var beginWindow = runParams.Range.Start;
                var endWindow = beginWindow.AddMinutes(Convert.ToInt32(runParams.CandleType) * runParams.WindowSize).AddSeconds(-1);

                if (inicialCandles.Length > 0)
                {
                    foreach (var point in inicialCandles)
                        candleMovieRoll.Enqueue(point);


                    bool readSomething = true;
                    while (readSomething)
                    {
                        var currentRange = new DateRange(beginWindow, endWindow);

                        var futureWindow = endWindow.AddMinutes(runParams.ForecastWindow * Convert.ToInt32(runParams.CandleType));

                        var bufferCandles = candleMovieRoll.GetList();
                        var candles = bufferCandles.Where(c => currentRange.IsInside(c.PointInTime_Open));
                        var volumes = candles.Select(p => new VolumeIndicator(p));

                        if (candles.Count() == runParams.WindowSize)
                        {
                            var wholeWindowRange = new DateRange(beginWindow, futureWindow);

                            var img = GetImage(runParams, candles, volumes, currentRange, true);

                            if (img != null)
                            {
                                var firstSecond = candles.ElementAt(0).PointInTime_Open;
                                var currentCandle = (Candle)candles.ElementAt(candles.Count() - 1);
                                var lastSecond = currentCandle.PointInTime_Close;
                                var progressSpan = lastSecond - runParams.Range.Start;

                                progress.setProgress(progressSpan.TotalMinutes);

                                Card c = new Card(img, bufferCandles.ToArray(), firstSecond, beginWindow, endWindow, futureWindow, runParams, currentCandle);
                                if (runParams.RunMode == RunModeType.Create || runParams.RunMode == RunModeType.Label)
                                {
                                    if (runParams.RunMode == RunModeType.Create)
                                    {
                                        //if(imgSequenceCluster % 5 == 0) //De cinco em cinco
                                        //{
                                            var tag = c.GetTag(candles.Last().CloseValue, candles.Last().PointInTime_Open, runParams.AverageToForecast);

                                            DirectoryInfo dirInfo = new DirectoryInfo(outputDir.FullName);
                                            var fileName = c.SaveFiles(dirInfo.FullName, tag, imgSequenceCluster);

                                            csvWriter.Write($"gs://candlebucket/{runParams.ExperimentName}/{runParams.Asset}/{fileName},{tag}");
                                            csvWriter.WriteLine();
                                        //}
                                    }
                                    else
                                    {
                                        var prediction = c.GetPrediction(img.RawImage);

                                        if(c.HasRecentPeakVolume_byAvg())
                                        {
                                            DirectoryInfo dirInfo = new DirectoryInfo( Path.Join(rootOutputDir.FullName, prediction) );
                                            dirInfo.Create();

                                            string name = c.GetFileName("","");

                                            img.RawImage.Save(Path.Combine(dirInfo.FullName, $"{name}_predict.gif"), ImageFormat.Gif );

                                            var fullImg = GetImage(runParams, candles, volumes, currentRange, true, false);

                                            fullImg.RawImage.Save(Path.Combine(dirInfo.FullName, $"{name}_full.gif"), ImageFormat.Gif );
                                        }
                                    }   
                                }
                                else
                                {
                                    var predictionData = c.GetPredictionData();

                                    if (predictionData != null)
                                    {
                                        csvWriter.Write(predictionData);
                                        csvWriter.WriteLine();
                                        csvWriter.Flush();

                                        var cpDirInfo = new DirectoryInfo(Path.Combine(outputDir.FullName, "CheckPredictions"));
                                        cpDirInfo.Create();

                                        var wholeCandles = bufferCandles.Where(c => wholeWindowRange.IsInside(c.PointInTime_Open));
                                        var volumesFull = wholeCandles.Select(p => new VolumeIndicator(p));

                                        var bigPicture = GetImage(runParams, wholeCandles, volumesFull, wholeWindowRange, false);

                                        img.RawImage.Save(Path.Combine(cpDirInfo.FullName, c.GetFileName("PP", runParams.Asset)));
                                        bigPicture.RawImage.Save(Path.Combine(cpDirInfo.FullName, c.GetFileName("BP", runParams.Asset)));
                                    }
                                }
                            }
                            
                            imgSequenceCluster++;
                        }

                        var oneCandle = res.Read(1);
                        if (oneCandle == null)
                            readSomething = false;
                        else
                        {
                            candleMovieRoll.Enqueue(oneCandle[0]);
                            FeedIndicators(oneCandle[0], "Main");
                            FeedIndicators(new VolumeIndicator(oneCandle[0]), "Volume");
                        }

                        beginWindow = beginWindow.AddMinutes(Convert.ToInt32(runParams.CandleType));
                        endWindow = beginWindow.AddMinutes(Convert.ToInt32(runParams.CandleType) * runParams.WindowSize).AddSeconds(-1);

                    }
                }
                else
                {
                    Console.WriteLine("No records found...");
                }

                csvWriter.Flush();
            }

            if (report != null)
            {
                using (var writer = new StreamWriter(File.Open(runParams.OutputFileResults, FileMode.Append, FileAccess.Write, FileShare.Read)))
                {
                    writer.WriteLine(report.ToCSVLine(runParams));
                }

                var result = report.GetResult(runParams);
                if (result.AllEntries.Count > 0)
                {
                    Console.WriteLine("====================");
                    Console.WriteLine($"Entradas: {result.AllEntries.Count}, SR: {result.SuccessRate}%, ST: {result.SuccessRateTarget}%, P&L: {result.PAndL}");
                    Console.WriteLine("====================");
                }
            }
        }

       public static CandleFaceImage GetImage(RunParameters runParams, IEnumerable<IStockPointInTime> candles, IEnumerable<IStockPointInTime> volumes, DateRange range, bool onlySim = true, bool forPrediction=true)
        {
            int cardWidth = runParams.CardWidth;
            int cardHeight = runParams.CardHeight;

            if(!forPrediction)
            {
                cardWidth = 1000;
                cardHeight = 1000;
            }

            DashView dv = new DashView(cardWidth, cardHeight);

            var frameMap = new Dictionary<string, ChartView>();
            if(forPrediction)
            {
                // frameMap.Add("VoidC", dv.AddChartFrame(20));
                // frameMap.Add("Main", dv.AddChartFrame(60));
                // frameMap.Add("VoidB", dv.AddChartFrame(20));

                frameMap.Add("VoidC", dv.AddChartFrame(20));
                frameMap.Add("Main", dv.AddChartFrame(50));
                frameMap.Add("Volume", dv.AddChartFrame(30));
            }
            else
            {
                frameMap.Add("VoidC", dv.AddChartFrame(20));
                frameMap.Add("Main", dv.AddChartFrame(50));
                frameMap.Add("Volume", dv.AddChartFrame(30));
            }

            frameMap["Main"].AddSerie(new Serie()
            {
                PointsInTime = candles.ToList(),
                Name = "Main",
                DrawShadow = !forPrediction
            });

            // if(!forPrediction)
            // {
                frameMap["Volume"].AddSerie(new Serie()
                {
                    PointsInTime = volumes.ToList<IStockPointInTime>(),
                    Name = "Volume",
                    Color = Color.LightBlue,
                    Type = SeriesType.Bar,
                });
            // }

            var grouped = _indicators.GroupBy(i => i.Target);
            foreach (var group in grouped)
            {
                if (frameMap.ContainsKey(group.Key))
                {
                    foreach (var ind in group)
                    {
                        Serie s = new Serie();
                        s.PointsInTime = ind.TakeSnapShot(range).ToList();
                        s.Name = ind.Name;
                        s.Color = ind.Color;
                        s.Type = ind.Type;
                        s.LineSize = ind.Size;
                        if(!forPrediction)
                            s.LineSize = s.LineSize * 3;


                        if (s.Name != "MA144")
                            s.Frameble = false;

                        frameMap[group.Key].AddSerie(s);
                    }
                }
            }

            bool isSim = false;
            var img = dv.GetImagePlus(ref isSim);

            if (onlySim && !isSim)
                img = null;

            return img;
        }

        private static void FeedIndicators(IStockPointInTime point, string source)
        {
            foreach (var ind in _indicators)
            {
                if (ind.Source == source)
                    ind.AddPoint(point);
            }
        }
    }
}