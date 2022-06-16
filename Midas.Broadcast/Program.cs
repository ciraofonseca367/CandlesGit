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
using Midas.Sources;
using Midas.Core.Broker;

namespace Midas.Broadcast
{
    public class Broadcast
    {
        private Thread _runner;
        private bool _running;
        private RunParameters _params;

        private MongoClient _mongoClient;

        private TradeOperationManager _manager = null;

        private static string WEB_SOCKETURI = "wss://stream.binance.com:9443/ws";

        public Broadcast(RunParameters parans)
        {
            _params = parans;
            _runner = new Thread(new ThreadStart(this.Runner));

            _mongoClient = new MongoClient(parans.DbConString);

            _manager = TradeOperationManager.GetManager(null, parans.DbConString, _params.BrokerName, parans.BrokerParameters, parans.Asset, parans.CandleType, "Broadcast");

        }

        public async Task SendMessage(string thread, string message)
        {
            if (thread == null)
                await TelegramBot.SendMessage(message);
            else
                await TelegramBot.SendMessageBuffered(thread, message);
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

        private List<TradeOperationDto> _activeOperations;

        public void Runner()
        {
            LiveAssetFeedStream liveStream = null;

            var runParams = _params;

            /*
            1. Corrigir aqui para utilizar a library nova
            2. Fazer mecanismo diference para iniciar ou não o encoder.
            */

            BinanceWebSocket sock = new BinanceWebSocket(
                WEB_SOCKETURI, 120000, _params.Asset, _params.CandleType
            );

            liveStream = sock.OpenAndSubscribe();

            if (_params.IsTesting)
            {
                Broker broker = Broker.GetBroker(_params.BrokerName, _params.BrokerParameters, null);
                var currentPriceTask = broker.GetPriceQuote(_params.Asset);
                currentPriceTask.Wait();

                InsertTestTradeOperation(currentPriceTask.Result);
            }

            if (liveStream != null)
            {
                _candleMovieRoll = new FixedSizedQueue<Candle>(runParams.WindowSize + 200);

                var cacheCandles = GetCachedCandles();
                foreach (var c in cacheCandles)
                {
                    _candleMovieRoll.Enqueue(c);
                    FeedIndicators(c, "Main");
                }

                var initialVolumes = cacheCandles.Select(p => new VolumeIndicator(p));
                foreach (var v in initialVolumes)
                    FeedIndicators(v, "Volume");

                VideoRecorder recorder = new VideoRecorder(_params);

                Bitmap imgToBroadcast = null;

                TraceAndLog.GetInstance().Log("Broadcast", $"Starting broadcast with {cacheCandles.Count()} cached candles");

                liveStream.OnNewCandle(async (id, previewsC, cc) =>
                {
                    var recentClosedCandle = previewsC;

                    //Feed the indicators
                    _candleMovieRoll.Enqueue(recentClosedCandle);
                    FeedIndicators(recentClosedCandle, "Main");
                    FeedIndicators(new VolumeIndicator(recentClosedCandle), "Volume");

                    await Task.CompletedTask;
                });

                liveStream.OnUpdate(async (id, message, cc) =>
                {
                    _lastCandle = cc;

                    var candlesToDraw = _candleMovieRoll.GetList().Union(new Candle[] { cc });
                    var volumes = candlesToDraw.Select(p => new VolumeIndicator(p));

                    //Default DateRange is the last Candle minus _params.WindowsSize in periods of the candle type configured(_params.CandleType)
                    var defaultRange = new DateRange(
                        candlesToDraw.Last().PointInTime_Open.AddMinutes(Convert.ToInt32(_params.CandleType) * _params.WindowSize * -1),
                        candlesToDraw.Last().PointInTime_Open);

                    DateRange range = defaultRange;
                    //Active operations DateRange
                    if (_activeOperations != null)
                    {
                        var minDate = _activeOperations.Min(op => op.EntryDate);
                        if (minDate < defaultRange.Start)
                            range = new DateRange(minDate, defaultRange.End);
                    }

                    List<VolumeIndicator> newVolumes = volumes.ToList(); //test

                    var predictionSerie = new Serie();
                    if (_activeOperations != null)
                    {
                        foreach (var operation in _activeOperations)
                        {
                            var forecastPoint = new TradeOperationCandle()
                            {

                                AmountValue = operation.PriceEntryReal,
                                LowerBound = operation.PriceEntryReal * 1.01,
                                UpperBound = operation.PriceEntryReal * 1.02,
                                Gain = operation.Gain,
                                ExitValue = operation.PriceExitReal,
                                StopLossMark = operation.StopLossMarker,
                                Volume = 0,
                                State = $"{operation.State.ToString()} - {operation.Strengh} ",
                                PointInTime_Open = operation.EntryDate,
                                PointInTime_Close = operation.ExitDate
                            };
                            forecastPoint.Orders = operation.Orders;

                            predictionSerie.PointsInTime.Add(forecastPoint);

                            //We need to add a corresponding volume point o keep the proporcion right
                            newVolumes.Add(new VolumeIndicator(forecastPoint));
                        }

                        predictionSerie.Name = "Predictions";
                        predictionSerie.Color = Color.LightBlue;
                        predictionSerie.Type = SeriesType.Forecast;
                    }

                    imgToBroadcast = GetImage(_params, candlesToDraw, newVolumes, range, predictionSerie, false);

                    await Task.Run(() =>
                    {
                        imgToBroadcast.Save("Live5.png", System.Drawing.Imaging.ImageFormat.Png);
                    });

                    if (!_params.IsTesting)
                    {
                        if (_activeOperations != null && _activeOperations.Count > 0)
                            _currentImage = imgToBroadcast;
                        else
                            _currentImage = null;
                    }

                });

                Task getReports = Task.Run(() =>
                {
                    while (_running)
                    {
                        //Get Stats
                        Log("Updating report");

                        var ops = _manager.SearchOperations(null, DateTime.Now.AddDays(-7));

                        var lastDayOps = ops.Where(op => op.EntryDate > DateTime.Now.AddDays(-1));

                        var resultLastDay = lastDayOps.Sum(op => (op.PriceExitReal - op.PriceEntryReal) / op.PriceEntryReal);
                        var resultWeek = ops.Sum(op => (op.PriceExitReal - op.PriceEntryReal) / op.PriceEntryReal);

                        _resultLastDay = resultLastDay * 100;
                        _resultLastWeek = resultWeek * 100;

                        //Get operations that started up until 3 days ago
                        var dbActiveOps = _manager.GetActiveStoredOperations(_params.Asset, _params.CandleType, "BroadCast", DateTime.UtcNow);

                        //Remove operations that ended longer then 1 hour ago
                        var reallyActiveOps = dbActiveOps.Where(op =>
                        {
                            DateTime exitDate;
                            if (op.State == TradeOperationState.In)
                                exitDate = op.ExitDate.AddHours(24);
                            else
                                exitDate = op.ExitDate;

                            return exitDate.ToUniversalTime().AddHours(1) > DateTime.UtcNow;
                        }).ToList();

                        if (reallyActiveOps.Count() > 0)
                            _activeOperations = dbActiveOps;
                        else
                            _activeOperations = null;

                        Thread.Sleep(1000 * 60 * 1);
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
            frameMap.Add("VoidC", dv.AddChartFrame(10));
            frameMap.Add("Main", dv.AddChartFrame(70));
            frameMap.Add("Volume", dv.AddChartFrame(20));

            frameMap["Main"].AddSerie(new Serie()
            {
                PointsInTime = candles.Where(c => range.IsInside(c.PointInTime_Open)).ToList(),
                Name = "Main"
            });

            if (prediction != null)
                frameMap["Main"].AddSerie(prediction);

            frameMap["Volume"].AddSerie(new Serie()
            {
                PointsInTime = volumes.Where(v => range.IsInside(v.PointInTime_Open)).ToList<IStockPointInTime>(),
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

        public void InsertTestTradeOperation(double currentValue)
        {
            var op = new TradeOperationDto();
            op.Asset = _params.Asset;
            op.ModelName = "BroadCastTest";
            op.Experiment = "BroadCastTest";

            op.LastUpdate = DateTime.Now;
            op.CandleType = Core.Common.CandleType.HOUR1;
            op.Amount = 1;
            op.Amount_USD = currentValue;
            op.EntryDate = DateTime.UtcNow;
            op.EntryDateUtc = DateTime.UtcNow;
            op.ExitDate = op.EntryDate.AddHours(12);
            op.ExitDateUtc = op.ExitDate;
            op.StopLossMarker = currentValue * (0.95);

            op.PriceEntryDesired = currentValue * (0.96);
            op.PriceEntryReal = op.PriceEntryDesired;
            op.PriceExitDesired = 0;
            op.PriceExitReal = 0;
            op.State = TradeOperationState.In;

            op.Orders = new List<Core.Broker.BrokerOrderDto>();
            op.Orders.Add(new Core.Broker.BrokerOrderDto()
            {
                AverageValue = currentValue,
                Quantity = 1,
                CreationDate = DateTime.UtcNow,
                Type = Core.Broker.OrderType.MARKET,
                OrderId = "Teste",
                Direction = Core.Broker.OrderDirection.BUY
            });

            op.Orders.Add(new Core.Broker.BrokerOrderDto()
            {
                AverageValue = currentValue * 1.003,
                Quantity = 1,
                CreationDate = DateTime.UtcNow.AddHours(2),
                Type = Core.Broker.OrderType.MARKET,
                OrderId = "Teste",
                Direction = Core.Broker.OrderDirection.BUY
            });

            var myDto = op;

            var objId = ObjectId.GenerateNewId(DateTime.Now);
            var myStrId = objId.ToString();
            var myId = new BsonObjectId(objId);

            op._id = myId;

            var dbClient = new MongoClient(_params.DbConString);

            var database = dbClient.GetDatabase("CandlesFaces");

            var dbCol = database.GetCollection<TradeOperationDto>("TradeOperations");

            dbCol.DeleteOne(
                item => item.Experiment == "BroadCastTest"
            );

            dbCol.InsertOne(myDto);

            Console.WriteLine("Inserted");
        }


        private IEnumerable<Candle> GetCachedCandles()
        {
            IEnumerable<Candle> ret = new List<Candle>();

            var periods = _params.WindowSize + 200;//200 é a maior média móvel
            var begin = DateTime.UtcNow.AddMinutes(periods * Convert.ToInt32(_params.CandleType) * -1);

            var asset = _params.Asset.Replace("USDT", "BUSD");

            var seconds = Convert.ToInt32(_params.CandleType);

            var lastCandles = CandlesGateway.GetCandlesFromRest(asset, _params.CandleType, new DateRange(begin, DateTime.UtcNow.AddMinutes(seconds * -1)));
            ret = lastCandles;

            return ret;
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

            if (itens.Count() > 0)
            {
                var items = itens.FirstOrDefault().GetValue("Items").AsBsonArray;
                string headLine = items.FirstOrDefault().AsBsonDocument.GetValue("Title").ToString();
                string description = items.FirstOrDefault().AsBsonDocument.GetValue("Description").ToString();

                return new
                {
                    Headline = headLine,
                    Description = description
                };
            }
            else
            {
                return new
                {
                    Headline = "-----",
                    Description = "---------"
                };
            }
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
            if (_encoder == null)
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
