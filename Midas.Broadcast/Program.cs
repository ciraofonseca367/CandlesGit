using System;
using System.Collections.Generic;
using System.Threading;
using Midas.Core;
using Midas.Core.Common;
using Midas.FeedStream;
using System.Linq;
using Midas.Util;
using MongoDB.Driver;
using MongoDB.Bson;
using Midas.Core.Chart;
using System.Drawing;
using System.IO;
using Midas.Core.Encoder;
using Midas.Core.Indicators;
using System.Threading.Tasks;
using Midas.Trading;
using Midas.Core.Telegram;
using Midas.Core.Util;

namespace Midas.Broadcast
{
    public class Broadcast : KlineRunner
    {
        private Thread _runner;
        private bool _running;
        private RunParameters _params;

        private MongoClient _mongoClient;

        private TradeOperationManager _manager = null;

        private NewCandleAction _klineObservers;

        private RunnerAction _runnerObservers;

        private double _lastHourDifference;
        private double _lastHourValue;
        private DateTime _lastHourUpdate;

        public Broadcast(RunParameters parans)
        {
            _params = parans;
            _runner = new Thread(new ThreadStart(this.Runner));

            _mongoClient = new MongoClient(parans.DbConString);

            _manager = TradeOperationManager.GetManager(null, parans.DbConString, parans.FundName, _params.BrokerName, parans.BrokerParameters, parans.Asset, parans.CandleType, "Broadcast");

            _lastHourUpdate = DateTime.MinValue;
            _lastHourDifference = 0;
            _lastHourValue = 0;
        }

        public void Subscribe(NewCandleAction action)
        {
            _klineObservers += action;
        }

        public void SubscribeToActions(RunnerAction action)
        {
            _runnerObservers += action;
        }

        private double UpdateLastHour(double amount)
        {
            double ret = -100;
            if (_lastHourUpdate == DateTime.MinValue)
            {
                _lastHourValue = amount;
                _lastHourUpdate = DateTime.Now;
            }

            if ((DateTime.Now - _lastHourUpdate).TotalMinutes > 60)
            {
                _lastHourDifference = ((amount - _lastHourValue) / amount) * 100;
                _lastHourValue = amount;
                ret = _lastHourDifference;

                _lastHourUpdate = DateTime.Now;
            }

            return ret;
        }

        public void SendMessage(string thread, string message)
        {
            if (thread == null)
                TelegramBot.SendMessage(message);
            else
                TelegramBot.SendMessageBuffered(thread, message);
        }

        public void Start()
        {
            _running = true;

            this._runner.Start();
        }

        public bool Running
        {
            get
            {
                return _running;
            }
        }

        public void Stop()
        {
            _running = false;

            DisposeResources();

            if (!_runner.Join(1000))
                throw new ApplicationException("Timeout waiting for the runner to stop");
        }

        private void DisposeResources()
        {
            TraceAndLog.GetInstance().Dispose();
        }

        private void FeedIndicators(IStockPointInTime point, string source)
        {
            foreach (var ind in _params.Indicators)
            {
                if (ind.Source == source)
                    ind.AddPoint(point);
            }
        }


        private Bitmap _currentImage;
        private Candle _lastCandle;

        private double _resultLastDay, _resultLastWeek;

        private FixedSizedQueue<Candle> _candleMovieRoll;

        public void Runner()
        {
            LiveAssetFeedStream liveStream = null;

            var runParams = _params;

            try
            {
                liveStream = (LiveAssetFeedStream)CandlesGateway.GetCandles(
                    "BTCBUSD",
                    DateRange.GetInfiniteRange(),
                    runParams.CandleType
                );
            }
            catch (Exception err)
            {
                TraceAndLog.GetInstance().Log("Runner", "Open socket error: " + err.Message);
                _running = false;
            }

            if (liveStream != null)
            {
                _candleMovieRoll = new FixedSizedQueue<Candle>(runParams.WindowSize);

                var cacheCandles = GetCachedCandles();
                cacheCandles.ForEach(c =>
                {
                    _candleMovieRoll.Enqueue(c);

                    FeedIndicators(c, "Main");
                });

                var initialVolumes = cacheCandles.Select(p => new VolumeIndicator(p));
                foreach (var v in initialVolumes)
                    FeedIndicators(v, "Volume");

                VideoRecorder recorder = new VideoRecorder(_params);

                Bitmap imgToBroadcast = null;

                TraceAndLog.GetInstance().Log("Broadcast", String.Format("Starting broadcast with {0} cached candles", cacheCandles.Count));

                liveStream.OnNewCandle((id,previewsC, cc) =>
                {
                    var recentClosedCandle = previewsC;

                    //Feed the indicators
                    _candleMovieRoll.Enqueue(recentClosedCandle);
                    FeedIndicators(recentClosedCandle, "Main");
                    FeedIndicators(new VolumeIndicator(recentClosedCandle), "Volume");
                });

                liveStream.OnUpdate((id,message, cc) =>
                {
                    _lastCandle = cc;

                    var candlesToDraw = _candleMovieRoll.GetList().Union(new Candle[] { cc });
                    var volumes = candlesToDraw.Select(p => new VolumeIndicator(p));

                    DateRange range = new DateRange(candlesToDraw.First().PointInTime_Open, candlesToDraw.Last().PointInTime_Open);

                    var storedOperations = _manager.GetActiveStoredOperations(_params.Asset,_params.CandleType, "Live", DateTime.Now);
                    if (storedOperations != null && storedOperations.Count > 0)
                    {
                        storedOperations = new List<TradeOperation> { storedOperations.Last() };
                        List<VolumeIndicator> newVolumes = volumes.ToList(); //test

                        var predictionSerie = new Serie();
                        foreach (var operation in storedOperations)
                        {
                            var forecastPoint = new TradeOperationCandle()
                            {

                                AmountValue = operation.PriceEntry,
                                LowerBound = operation.GetAbsolutLowerBound(),
                                UpperBound = operation.GetAbsolutUpperBound(),
                                Gain = operation.GetGain(cc.CloseValue),
                                ExitValue = operation.StoredAverage,
                                StopLossMark = operation.StopLossMark,
                                Volume = 1,
                                State = operation.State.ToString(),
                                PointInTime_Open = operation.EntryDate,
                                PointInTime_Close = operation.ExitDate
                            };
                            if (!operation.IsIn)
                                forecastPoint.Gain = operation.GetGain(); 

                            predictionSerie.PointsInTime.Add(forecastPoint);

                            //We need to add a corresponding volume point o keep the proporcion right
                            newVolumes.Add(new VolumeIndicator(forecastPoint));
                        }

                        predictionSerie.Name = "Predictions";
                        predictionSerie.Color = Color.LightBlue;
                        predictionSerie.Type = SeriesType.Forecast;

                        var theOperation = storedOperations.Last();

                        imgToBroadcast = GetImage(_params, candlesToDraw, newVolumes, range, predictionSerie, false);

                        imgToBroadcast.Save("Live5.png", System.Drawing.Imaging.ImageFormat.Png);

                        _currentImage = imgToBroadcast;
                    }                    
                    else
                        _currentImage = null;
                  
                });

                Task getReports = Task.Run(() =>
                {
                    while (_running)
                    {
                        Log("Updating report");

                        var ops = _manager.SearchOperations(null, DateTime.Now.AddDays(-7));

                        var lastDayOps = ops.Where(op => op.EntryDate > DateTime.Now.AddDays(-1));

                        var resultLastDay = lastDayOps.Sum(op => (op.PriceExitReal - op.PriceEntryReal) / op.PriceEntryReal);
                        var resultWeek = ops.Sum(op => (op.PriceExitReal - op.PriceEntryReal) / op.PriceEntryReal);

                        _resultLastDay = resultLastDay * 100;
                        _resultLastWeek = resultWeek * 100;

                        Thread.Sleep(1000 * 60 * 5);
                    }
                });

                while (_running && liveStream.State != MidasSocketState.Closed)
                {
                    Thread.Sleep(25);
                    if (_currentImage != null)
                        recorder.FeedFrame(String.Empty, _currentImage, _lastCandle, _resultLastDay, _resultLastWeek);
                }
            }

            Console.WriteLine("Saindo...");

            _running = false;

            DisposeResources();

            if (liveStream != null)
                liveStream.Dispose();
        }

        private void Log(string msg)
        {
            Console.WriteLine(String.Format("{0:yyyy:MM:dd hh:mm} - {1}", DateTime.Now, msg));
        }




        private Bitmap GetImage(RunParameters runParams, IEnumerable<IStockPointInTime> candles, IEnumerable<IStockPointInTime> volumes, DateRange range, Serie prediction = null, bool onlySim = true)
        {
            Bitmap img = null;
            DashView dv = new DashView(runParams.CardWidth, runParams.CardHeight);

            var frameMap = new Dictionary<string, ChartView>();
            frameMap.Add("Main", dv.AddChartFrame(70));
            frameMap.Add("Volume", dv.AddChartFrame(30));

            frameMap["Main"].AddSerie(new Serie()
            {
                PointsInTime = candles.ToList(),
                Name = "Main",
            });

            if (prediction != null)
                frameMap["Main"].AddSerie(prediction);

            frameMap["Volume"].AddSerie(new Serie()
            {
                PointsInTime = volumes.ToList<IStockPointInTime>(),
                Name = "Volume",
                Color = Color.LightBlue,
                Type = SeriesType.Bar,
            });

            var grouped = runParams.Indicators.GroupBy(i => i.Target);
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

                        if (s.Name != "MA Volume Meio Dia" || s.Name != "Predictions")
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




        private List<Candle> GetCachedCandles()
        {
            var database = _mongoClient.GetDatabase("CandlesFaces");

            var dbCol = database.GetCollection<Candle>(String.Format("Klines_{0}_{1}", _params.Asset, _params.CandleType.ToString()));
            var itens = dbCol.Find(new BsonDocument()).ToList();

            //Filter only the candles for our windowsize
            var lastXCandles = itens
            .Where(i => i.CloseTime > DateTime.UtcNow.AddMinutes(_params.WindowSize * 20 * Convert.ToInt32(_params.CandleType) * -1))
            .OrderBy(i => i.OpenTime);

            return lastXCandles.ToList();
        }
    }

    public class VideoRecorder
    {
        private DateTime _lastAction;
        private LiveEncoder _encoder;

        private RunParameters _params;
        private MongoClient _mongoClient;

        public VideoRecorder(RunParameters @params)
        {
            _lastAction = DateTime.MinValue;
            _params = @params;

            _mongoClient = new MongoClient(_params.DbConString);

            _lastFed = DateTime.MinValue;
            Thread t = new Thread(new ThreadStart(this.StopStartRunner));
            t.Start();
        }

        private dynamic GetLastNews()
        {
            var database = _mongoClient.GetDatabase("Sentiment");

            var dbCol = database.GetCollection<BsonDocument>("BTCTail");
            var itens = dbCol
            .Find(new BsonDocument())
            .Sort(new BsonDocument()
            {
                { "Created", -1 }
            })
            .Limit(1).ToList();

            var items = itens.FirstOrDefault().GetValue("Items").AsBsonArray;
            string headLine = items.FirstOrDefault().AsBsonDocument.GetValue("Title").ToString();
            string description = items.FirstOrDefault().AsBsonDocument.GetValue("Description").ToString();

            return new
            {
                Headline = headLine,
                Description = description
            };
        }

        private DateTime _lastFed;

        private void StopStartRunner()
        {
            while (true)
            {
                if ((DateTime.Now - _lastFed).TotalMinutes > 5)
                {
                    lock (this)
                    {
                        if (_encoder != null)
                        {
                            Console.WriteLine("Parando encoder...");
                            _encoder.Stop();
                            _encoder = null;
                        }
                    }
                }

                Thread.Sleep(1000);
            }
        }

        public void FeedFrame(string trendType, Bitmap img, Candle currentCandle, double resultLastDay, double resultLastWeek)
        {
            _lastFed = DateTime.Now;
            if (_params.IsTesting && _encoder == null)
            {
                _encoder = new LiveEncoder(
                    _params.FFmpegBasePath,
                    _params.LiveStreamSiteUrl,
                    _params.LiveStreamKey,
                    3,
                    System.Drawing.Imaging.ImageFormat.Png);

                Console.WriteLine("Iniciando ffmpeg");

                _encoder.Start();

                Thread.Sleep(500);
            }

            if (_encoder != null)
            {
                try
                {
                    string state;

                    state = String.Format("=== 24 hrs Performance: {0:0.000}% :: 7 days Performance: {1:0.000}% ===", resultLastDay, resultLastWeek);

                    var infoImage = new Bitmap(_params.CardWidth, 920);
                    Graphics g = Graphics.FromImage(infoImage);

                    g.DrawString("$ " + currentCandle.CloseValue.ToString("#,##0.00"), new Font("Arial", 12), new SolidBrush(Color.Orange), 5, 2);
                    g.DrawString(state, new Font("Arial", 12), new SolidBrush(Color.White), 300, 1);
                    g.DrawString(String.Format("TimeStamp: {0:hh:mm:ss}", DateTime.Now), new Font("Arial", 12), new SolidBrush(Color.Orange), 1000, 1);

                    SolidBrush greenBrush = new SolidBrush(Color.Green);

                    // Create point for upper-left corner of drawing.
                    float x = 10;
                    float y = 831;

                    g.DrawImage(img, 10, 20);

                    var lastUpdate = GetLastNews();

                    // Draw string to screen.
                    g.DrawString(lastUpdate.Headline, new Font("Arial", 20), greenBrush, x, y);
                    g.DrawString(lastUpdate.Description, new Font("Arial", 12), greenBrush, x, y + 30);

                    Console.Write(".");
                    infoImage.Save(Path.Combine(_params.OutputDirectory, "Broadcast.png"), System.Drawing.Imaging.ImageFormat.Png);

                    lock (this)
                    {
                        if (_encoder != null)
                            _encoder.SendImage(infoImage);
                    }
                }
                catch (Exception err)
                {
                    Console.WriteLine("Encoder error - " + err.ToString());
                }
            }
        }
    }

    class Program
    {
        private static Broadcast _caster;
        static void Main(string[] args)
        {
            ThreadPool.SetMaxThreads(100, 100);
            ThreadPool.SetMinThreads(6, 6);

            RunParameters runParams = RunParameters.CreateInstace(args);
            _caster = new Broadcast(runParams);

            System.Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", "/Users/cironola/Downloads/candlesfaces-fdbef15d7ab2.json");

            runParams.WriteToConsole();

            _caster.Start();

            while (_caster.Running)
            {
                string line = Console.ReadLine();
                if (line == "Exit")
                {
                    Console.WriteLine("Stopping...");
                    _caster.Stop();
                    Console.WriteLine("Stopped!");
                }
            }
        }
    }


}
