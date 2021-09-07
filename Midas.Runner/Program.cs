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

            var res = CandlesGateway.GetCandles(
                runParams.Asset,
                runParams.Range,
                runParams.CandleType
            );

            var outputDir = Directory.CreateDirectory(
                Path.Combine(runParams.OutputDirectory, runParams.ExperimentName)
                );

            PredictionReport report = null;
            if (runParams.RunMode == RunModeType.Predict)
                report = new PredictionReport();

            _indicators = runParams.Indicators;

            RoundRobinNumber robin = new RoundRobinNumber(10);

            using (var csvFile = File.Open(Path.Combine(outputDir.FullName, runParams.OutputFile), FileMode.Create, FileAccess.Write, FileShare.Read))
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
                var endWindow = beginWindow.AddMinutes(Convert.ToInt32(runParams.CandleType) * runParams.WindowSize);

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

                        if (candles.Count() >= runParams.WindowSize)
                        {

                            var wholeWindowRange = new DateRange(beginWindow, futureWindow);

                            var img = GetImage(runParams, candles, volumes, currentRange, true);

                            if (img != null)
                            {
                                var firstSecond = candles.ElementAt(0).PointInTime_Open;
                                var lastSecond = candles.ElementAt(candles.Count() - 1).PointInTime_Close;
                                var progressSpan = lastSecond - runParams.Range.Start;

                                progress.setProgress(progressSpan.TotalMinutes);

                                Card c = new Card(img, bufferCandles.ToArray(), firstSecond, beginWindow, endWindow, futureWindow, runParams.Indicators, runParams);
                                if (runParams.RunMode == RunModeType.Create)
                                {
                                    var tag = c.GetTag(candles.Last().CloseValue, candles.Last().PointInTime_Open, runParams.AverageToForecast);
                                    if (tag != "IIIIGNORED")
                                    {
                                        DirectoryInfo dirInfo = new DirectoryInfo(outputDir.FullName);
                                        var fileName = c.SaveToFile(dirInfo.FullName, tag);


                                        csvWriter.Write("gs://candlebucket/");
                                        csvWriter.Write(runParams.ExperimentName);
                                        csvWriter.Write("/");
                                        csvWriter.Write(fileName);
                                        csvWriter.Write(",");
                                        csvWriter.Write(tag);
                                        csvWriter.WriteLine();
                                    }
                                }
                                else
                                {
                                    PredictionReportItem predictionReportItem = null;
                                    try
                                    {
                                        predictionReportItem = c.WritePrediction(csvWriter, runParams.ScoreThreshold, c.GetFileName("PC"));
                                    }
                                    catch (Exception err)
                                    {
                                        Console.WriteLine("Prediction error: " + err.ToString());
                                    }

                                    if (predictionReportItem != null)
                                    {
                                        report.AddReportItem(predictionReportItem);

                                        var cpDirInfo = new DirectoryInfo(Path.Combine(outputDir.FullName, "CheckPredictions"));
                                        cpDirInfo.Create();

                                        var result = report.GetResult(runParams);
                                        Console.Write(String.Format("  {0:0.00}% PL, AR: {1:0.00}%, AT: {2:0.00}%,  {3} Entries", result.PAndL, result.SuccessRate, result.SuccessRateTarget, result.AllEntries.Count));

                                        var wholeCandles = bufferCandles.Where(c => wholeWindowRange.IsInside(c.PointInTime_Open));
                                        var volumesFull = wholeCandles.Select(p => new VolumeIndicator(p));

                                        var bigPicture = GetImage(runParams, wholeCandles, volumesFull, wholeWindowRange, false);

                                        c.SaveToFile(cpDirInfo.FullName, "PC");
                                        bigPicture.Save(Path.Combine(cpDirInfo.FullName, c.GetFileName("BP")));
                                    }
                                }
                            }
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
                        endWindow = beginWindow.AddMinutes(Convert.ToInt32(runParams.CandleType) * runParams.WindowSize);

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

        private static Bitmap GetImage(RunParameters runParams, IEnumerable<IStockPointInTime> candles, IEnumerable<IStockPointInTime> volumes, DateRange range, bool onlySim = true)
        {
            Bitmap img = null;
            DashView dv = new DashView(runParams.CardWidth, runParams.CardHeight);

            var frameMap = new Dictionary<string, ChartView>();
            frameMap.Add("Blanck", dv.AddChartFrame(30));
            frameMap.Add("Main", dv.AddChartFrame(40));
            frameMap.Add("Volume", dv.AddChartFrame(30));

            frameMap["Main"].AddSerie(new Serie()
            {
                PointsInTime = candles.ToList(),
                Name = "Main"
            });

            frameMap["Volume"].AddSerie(new Serie()
            {
                PointsInTime = volumes.ToList<IStockPointInTime>(),
                Name = "Volume",
                Color = Color.LightBlue,
                Type = SeriesType.Bar
            });

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

                        if (s.Name == "MA200")
                            s.RelativeXPos = 0.95;
                        else if (s.Name == "MA100")
                            s.RelativeXPos = 0.85;
                        else if (s.Name == "MA50")
                            s.RelativeXPos = 0.75;

                        if (s.Name != "MAVol144" && s.Name != "MMaxVol144" && s.Name != "MMinVol144")
                            s.Frameble = false;

                        frameMap[group.Key].AddSerie(s);
                    }
                }
            }

            bool isSim = false;
            img = dv.GetImage(ref isSim);

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