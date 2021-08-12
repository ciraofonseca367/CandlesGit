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
using Midas.Core.Broker;
using Midas.Core.Util;
using Telegram.Bot;
using Telegram.Bot.Extensions.Polling;
using System.Text;

namespace Midas.Core.Services
{
    public class InvestorService : KlineRunner
    {
        private Thread _runner;
        private bool _running;
        private RunParameters _params;

        private MongoClient _mongoClient;

        private FixedSizedQueue<HangingPrediction> _hangingPredictions;

        private TradeOperationManager _manager = null;

        private NewCandleAction _klineObservers;

        private RunnerAction _runnerObservers;

        private StreamWriter _streamLog;

        private double _lastHourDifference;
        private double _lastHourValue;
        private DateTime _lastHourUpdate;

        public InvestorService(RunParameters parans)
        {
            _params = parans;
            _runner = new Thread(new ThreadStart(this.Runner));

            _mongoClient = new MongoClient(parans.DbConString);
            _hangingPredictions = new FixedSizedQueue<HangingPrediction>(10000);

            _manager = TradeOperationManager.GetManager(parans.DbConString, parans.FundName, parans.BrokerParameters);
            //_manager.RestoreState(_params.IsTesting);

            _streamLog = new StreamWriter(File.Open(Path.Combine(parans.OutputDirectory, "stream.log"), FileMode.Create, FileAccess.Write, FileShare.Read));

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

        private void UpdateLastHour(double amount)
        {
            bool send = false; ;
            if (_lastHourUpdate == DateTime.MinValue)
            {
                _lastHourValue = amount;
                _lastHourUpdate = DateTime.Now.AddHours(-2);
                send = true;
            }

            if ((DateTime.Now - _lastHourUpdate).TotalMinutes > 60)
            {
                _lastHourDifference = ((amount - _lastHourValue) / amount) * 100;
                _lastHourValue = amount;
                send = true;

                _lastHourUpdate = DateTime.Now;
            }

            if (send)
            {
                SendReport();
            }
        }

        private void SendReport()
        {
            var ops = _manager.SearchOperations(DateTime.Now.AddDays(-7));

            var lastDayOps = ops.Where(op => op.EntryDate > DateTime.Now.AddDays(-1));

            var resultLastDay = lastDayOps.Sum(op => (op.PriceExitReal - op.PriceEntryReal) / op.PriceEntryReal);
            var taxesLastDay = lastDayOps.Count() * 0.0012;
            var resultWeek = ops.Sum(op => (op.PriceExitReal - op.PriceEntryReal) / op.PriceEntryReal);
            var taxesLastWeek = ops.Count() * 0.0012;

            TelegramBot.SendMessage(String.Format("BTC Last hour update: {0:0.000}%", _lastHourDifference));

            if (DateTime.Now.Hour == 20 || DateTime.Now.Hour == 21 || _params.IsTesting)
            {
                TelegramBot.SendMessage(String.Format("DAILY REPORT:\nLast 24 hrs Gain: {0:0.000}% \nLast 24 hrs taxes:{1:0.00}\nLast 7 days Gain: {2:0.000}%\nLast 7 days taxes:{3:0.00}",
                resultLastDay * 100, resultWeek * 100, taxesLastDay * 100, taxesLastWeek * 100));
            }
        }

        internal string GetReport()
        {
            var ops = _manager.SearchOperations(DateTime.Now.AddDays(-7));

            var lastDayOps = ops.Where(op => op.EntryDate > DateTime.Now.AddDays(-1));

            var resultLastDay = lastDayOps.Sum(op => (op.PriceExitReal - op.PriceEntryReal) / op.PriceEntryReal);
            resultLastDay *= 100;
            var taxesLastDay = lastDayOps.Count() * -0.12;
            var resultWeek = ops.Sum(op => (op.PriceExitReal - op.PriceEntryReal) / op.PriceEntryReal);
            resultWeek *= 100;
            var taxesLastWeek = ops.Count() * -0.12;

            string message;

            message = $"Last 24 hrs P&L\n{resultLastDay.ToString("0.000")}% + {taxesLastDay.ToString("0.000")}% = {(resultLastDay + taxesLastDay).ToString("0.000")}% em {lastDayOps.Count()} entradas\n\n";
            message += $"Last 7 days P&L\n{resultWeek.ToString("0.000")}% + {taxesLastWeek.ToString("0.000")}% = {(resultWeek + taxesLastWeek).ToString("0.000")}% em {ops.Count()} entradas\n";

            return message;
        }

        internal Bitmap GetSnapshot()
        {
            if (_lastCandle != null)
                return GetSnapShot(_lastCandle);
            else
                return null;
        }

        internal string GetTextSnapShop()
        {
            StringBuilder sb = new StringBuilder(200);

            if(_lastPrediction != null)
            {
                _lastPrediction.ForEach(p => {
                    sb.AppendFormat("{0} : {1:0.000}%\n", p.Tag, p.Score);
                });
            }

            sb.AppendLine();

            var candlesToDraw = _candleMovieRoll.GetList();
            DateRange range = new DateRange(candlesToDraw.First().PointInTime_Open, candlesToDraw.Last().PointInTime_Open);
            double atr = GetCurrentAtr(range);

            if(_lastPrice > 0 && atr > 0)
            {
                sb.AppendLine("Price: $"+_lastPrice.ToString("0.00"));
                sb.AppendLine("ATR: $"+atr.ToString("0.00"));
                sb.AppendLine("RATR: "+((atr/_lastPrice)*100).ToString("0.000")+"%");
            }

            return sb.ToString();
        }

        internal string ForceMaketOrder()
        {
            string ret;

            try
            {
                var order = _manager.ForceMarketSell();
                if (order.InError)
                    ret = $"Order error: {order.ErrorMsg}";
                else
                    ret = $"SOLD! Avg: {order.AverageValue}";
            }
            catch (Exception err)
            {
                ret = $"Error selling: {err.Message}";
            }

            return ret;
        }

        public string GetBalanceReport()
        {
            BinanceBroker b = new BinanceBroker();
            b.SetParameters(_params.BrokerParameters);
            var price = b.GetPriceQuote("BTCUSDT");

            string emoticon = "\U00002705";
            string balanceReport = "BALANCE REPORT " + emoticon + "\n\n";

            var balances = b.AccountBalance(60000);
            balances.ForEach(b =>
            {
                if (b.TotalQuantity > 0.0001)
                {
                    if (b.Asset == "BTC")
                        b.TotalUSDAmount = b.TotalQuantity * price;
                    else if (b.Asset == "USDT")
                        b.TotalUSDAmount = b.TotalQuantity;
                    else if (b.Asset == "BUSD")
                        b.TotalUSDAmount = b.TotalQuantity;

                    balanceReport += String.Format("{0}: {1:0.0000} = ${2:0.00}\n", b.Asset, b.TotalQuantity, b.TotalUSDAmount);
                }
            });

            balanceReport += "\n";
            balanceReport += $"Total: ${balances.Sum(b => b.TotalUSDAmount).ToString("0.00")}";

            return balanceReport;
        }

        internal string GetState()
        {
            var op = _manager.GetOneActiveOperation();

            return (op != null ? op.ToString() : "No active operation");
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
            TelegramBot.SendMessage("Iniciando Investor...");

            _running = true;

            _manager.SetKlineRunner(this);

            this._runner.Start();

            if (true || !_params.IsTesting)
            {
                //Set up the Bot
                _candleBot = new CandleBot(this, _params);
                _candleBot.Start();
            }
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
            if (_streamLog != null)
                _streamLog.Dispose();

            TraceAndLog.GetInstance().Dispose();

            Console.WriteLine("Saindo...");

            _candleBot.Stop();
        }

        private void FeedIndicators(IStockPointInTime point, string source)
        {
            foreach (var ind in _params.Indicators)
            {
                if (ind.Source == source)
                    ind.AddPoint(point);
            }
        }

        private void NewInfo(string message)
        {
            if (_params.IsTesting)
                Console.WriteLine(message);

            if (_streamLog != null)
            {
                _streamLog.WriteLine(message);
                _streamLog.Flush();
            }
        }
        private Candle candleThatPredicted = null;
        private Candle _lastCandle = null;

        private FixedSizedQueue<Candle> _candleMovieRoll;

        private DateTime _lastOperationUpdate;

        private Bitmap _lastImage = null;
        private List<PredictionResult> _lastPrediction = null;
        private double _lastAtr = 0;
        private double _lastPrice = 0;
        private CandleBot _candleBot;

        public void Runner()
        {
            LiveAssetFeedStream liveStream = null;

            var runParams = _params;

            _lastOperationUpdate = DateTime.MinValue;

            try
            {
                liveStream = (LiveAssetFeedStream)CandlesGateway.GetCandles(
                    "btcusdt",
                    DateRange.GetInfiniteRange(),
                    CandleType.MIN5
                );

                if (_params.IsTesting)
                {
                    BinanceBroker b = new BinanceBroker();
                    b.SetParameters(_params.BrokerParameters);
                    var price = b.GetPriceQuote("BTCUSDT");
                    liveStream.InitPrice(price);
                }
            }
            catch (Exception err)
            {
                TraceAndLog.StaticLog("Main", err.ToString());
                _running = false;
            }

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

            Task<List<PredictionResult>> predictions = null;


            Bitmap img = null;
            string currentTrend = "None";

            TraceAndLog.GetInstance().Log("Runner", String.Format("Starting runner with {0} cached candles", cacheCandles.Count));

            liveStream.OnNewInfo(this.NewInfo);

            liveStream.OnNewCandle((previewsC, cc) =>
            {
                try
                {
                    TraceAndLog.GetInstance().Log("Runner", "====== A new candle has just been created ======");

                    currentTrend = "None";
                    candleThatPredicted = null;

                    var recentClosedCandle = previewsC;

                    //Feed the indicators
                    _candleMovieRoll.Enqueue(recentClosedCandle);
                    FeedIndicators(recentClosedCandle, "Main");
                    FeedIndicators(new VolumeIndicator(recentClosedCandle), "Volume");

                    //Let us get a snapshot of the moment to predict the future shall we?
                    var candlesToDraw = _candleMovieRoll.GetList();
                    var volumes = candlesToDraw.Select(p => new VolumeIndicator(p));

                    DateRange range = new DateRange(candlesToDraw.First().OpenTime, candlesToDraw.Last().OpenTime);

                    img = GetImage(runParams, candlesToDraw, volumes, range, null, false);
                    _lastImage = img;

                    //Get a prediction
                    RestPredictionClient predict = new RestPredictionClient();
                    float restThreshould = (_params.IsTesting ? -2f : 0.1f); //Se estivermos testando sempre pede uma previsão de testes, se não manda o minimo de 0.1 para vir todas as previsões classificadas e estudarmos aqui e filtrar abaixo
                    predictions = predict.PredictAsync(img, restThreshould, recentClosedCandle.CloseValue, recentClosedCandle.OpenTime);
                    if (predictions.Wait(10000))
                    {
                        StorePrediction(predictions.Result);

                        var result = predictions.Result;
                        _lastPrediction = result;
                        PredictionResult secondResult = null;
                        if (result.Count > 0)
                        {
                            if (result.Count() > 1)
                            {
                                secondResult = result[1];
                                if (result.First().Tag == "LONG" && secondResult.Tag == "SHORT")
                                {
                                    TelegramBot.SendMessageBuffered("AVISO", "Double Conflict LONG & SHORT");
                                    TraceAndLog.GetInstance().Log("Runner", "Double Conflict LONG & SHORT");
                                }
                            }

                            if (result.First().Tag.StartsWith("LONG") &&
                            (secondResult == null || (secondResult != null && secondResult.Tag != "SHORT")))
                            {
                                if (result.First().Score >= _params.ScoreThreshold) //Aqui realmente só setamos a "CANDLE DA PREVISÃO" se o score for o configurado
                                {
                                    candleThatPredicted = recentClosedCandle;
                                    foreach (var prediction in result)
                                    {
                                        TraceAndLog.GetInstance().Log("Runner", "Here is a prediction: " + prediction.ToString());
                                    }

                                    currentTrend = result.First().Tag;
                                    TelegramBot.SendImageBuffered("PredictImage", img, "Trend: " + currentTrend.ToString());
                                    _manager.Signal(predictions.Result.FirstOrDefault().GetTrend());
                                }
                            }
                        }
                    }
                    else
                        TraceAndLog.GetInstance().Log("Runner", "Timeout waiting on a prediction!!!");

                }
                catch (Exception err)
                {
                    TraceAndLog.GetInstance().Log("Predict Error", err.ToString());
                }

            });

            Task currentEnterTask = null;
            Task currentUpdateObserverTask = null;
            liveStream.OnUpdate((message, cc) =>
            {
                _lastCandle = cc;
                _lastPrice = cc.CloseValue;
                try
                {
                    if (candleThatPredicted != null && (_params.IsTesting || cc.OpenTime > candleThatPredicted.OpenTime)) //Delayed Trigger buffer
                    {
                        var currentCandle = cc;

                        if (currentTrend.StartsWith("LONG"))
                        {
                            if (
                                _params.IsTesting ||
                                (
                                    candleThatPredicted.Direction == CandleDirection.Down &&
                                    currentCandle.CandleAge.TotalSeconds >= 200 && currentCandle.GetPureIndecisionThreshold() >= _params.IndecisionThreshold
                                ) ||
                                candleThatPredicted.Direction == CandleDirection.Up
                            )
                            {
                                if (currentEnterTask == null || currentEnterTask.IsCompleted)
                                {
                                    currentEnterTask = Task.Run(() =>
                                    {
                                        try
                                        {
                                            var candlesToDraw = _candleMovieRoll.GetList();
                                            DateRange range = new DateRange(candlesToDraw.First().PointInTime_Open, candlesToDraw.Last().PointInTime_Open);
                                            double atr = GetCurrentAtr(range);

                                            _lastAtr = atr;

                                            Console.WriteLine("Entrando com ATR: " + atr);

                                            var prediction = predictions.Result.FirstOrDefault();
                                            var op = _manager.SignalEnterAsync(
                                                cc.CloseValue,
                                                prediction.RatioLowerBound,
                                                prediction.RatioUpperBound,
                                                cc.OpenTime,
                                                prediction.DateRange.End,
                                                atr
                                                );

                                            TelegramBot.SendMessageBuffered("Entrada", "Operação ativa! Veja ao vivo em: https://www.twitch.tv/cirofns");

                                            op.Wait(20000);
                                        }
                                        catch (Exception err)
                                        {
                                            TraceAndLog.StaticLog("Main", "Error entering: " + err.ToString());
                                        }
                                    });

                                }
                                else
                                {
                                    TraceAndLog.GetInstance().Log("Candle Update - Entrada", "There is already a task running, ignoring this update...");
                                }
                            }
                        }
                    }

                    if (currentUpdateObserverTask == null || currentUpdateObserverTask.IsCompleted)
                    {
                        currentUpdateObserverTask = Task.Run(() =>
                        {
                            try
                            {
                                _klineObservers.Invoke(cc);
                            }
                            catch (Exception err)
                            {
                                TraceAndLog.GetInstance().Log("Runner", "Error invoking KlineObserver - " + err.Message);
                            }
                        });
                    }
                    else
                    {
                        TraceAndLog.GetInstance().Log("Candle Update - Observer", "There is already a task running, ignoring this update...");
                    }

                    UpdateLastHour(cc.CloseValue);

                }
                catch (Exception err)
                {
                    TraceAndLog.GetInstance().Log("Entrada Error", err.ToString());
                }
            });

            if (_params.IsTesting)
            {
                liveStream.OnUpdate((message, cc) =>
                {
                    var imgToBroadcast = GetSnapShot(cc);
                    imgToBroadcast.Save(Path.Combine(_params.OutputDirectory, "LiveInvestor.png"), System.Drawing.Imaging.ImageFormat.Png);
                });
            }

            while (_running)
                Thread.Sleep(1000);

            if (liveStream != null)
                liveStream.Close();

        }

        private double GetCurrentAtr(DateRange range)
        {
            var atrInd = _params.Indicators.Where(i => i.Name == "ATR").First();

            return atrInd.TakeSnapShot(range).Last().AmountValue;
        }

        private Bitmap GetSnapShot(Candle cc)
        {
            var candlesToDraw = _candleMovieRoll.GetList().Union(new Candle[] { cc });
            var volumes = candlesToDraw.Select(p => new VolumeIndicator(p));
            Bitmap imgToBroadcast;

            DateRange range = new DateRange(candlesToDraw.First().PointInTime_Open, candlesToDraw.Last().PointInTime_Open);

            var storedOperations = _manager.GetStoredOperations(DateTime.UtcNow.AddMinutes(-5 * 30));
            if (storedOperations != null && storedOperations.Count > 0)
            {
                storedOperations = new List<TradeOperation> { storedOperations.Last() };
                List<VolumeIndicator> newVolumes = volumes.ToList();

                var predictionSerie = new Serie();
                foreach (var operation in storedOperations)
                {
                    var forecastPoint = new TradeOperationCandle()
                    {

                        AmountValue = operation.PriceEntry,
                        LowerBound = operation.GetAbsolutLowerBound(),
                        UpperBound = operation.GetAbsolutUpperBound(),
                        Gain = operation.GetGain(cc.CloseValue),
                        StrenghMark = operation.StoredAverage,
                        StopLossMark = operation.StopLossMark,
                        SoftStopLossMark = operation.SoftStopLossMark,
                        Volume = 1,
                        State = operation.State.ToString() + (operation.IsForceActive ? " FORCE" : String.Empty),
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
            }
            else
            {
                imgToBroadcast = GetImage(_params, candlesToDraw, volumes, range, null, false);
            }

            return imgToBroadcast;
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

                        if (s.Name != "MA Volume Meio Dia")
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
            var client = new MongoClient(_params.DbConStringCandles);

            var database = client.GetDatabase("CandlesFaces");

            var dbCol = database.GetCollection<Candle>(String.Format("Klines_{0}_{1}", _params.Asset, _params.CandleType.ToString()));
            var itens = dbCol.Find(new BsonDocument()).ToList();

            //Filter only the candles for our windowsize
            var lastXCandles = itens
            .Where(i => i.CloseTime > DateTime.UtcNow.AddMinutes(_params.WindowSize * 20 * Convert.ToInt32(_params.CandleType) * -1))
            .OrderBy(i => i.OpenTime);

            return lastXCandles.ToList();
        }

        private void StorePrediction(List<PredictionResult> res)
        {
            try
            {
                var rec = new PredictionRecord()
                {
                    UtcCreationDate = DateTime.UtcNow,
                    Predictions = res
                };

                var client = new MongoClient(_params.DbConString);

                var database = client.GetDatabase("CandlesFaces");

                var dbCol = database.GetCollection<PredictionRecord>("Predictions");

                dbCol.InsertOneAsync(rec);
            }
            catch (Exception err)
            {
                TraceAndLog.StaticLog("Store Prediction", err.Message);
            }
        }
    }

    public class PredictionRecord
    {
        public DateTime UtcCreationDate
        {
            get;
            set;
        }

        public List<PredictionResult> Predictions
        {
            get;
            set;    
        }
    }

    public class HangingPrediction
    {
        public PredictionResult Prediction
        {
            get;
            set;
        }

        public DateTime Timestamp
        {
            get;
            set;
        }

        public string GetComparisonStamp()
        {
            return Timestamp.ToString("yyyy-MM-dd hh:mm:ss");
        }

        public int ExpirationInMinutes
        {
            get;
            set;
        }

        public bool HasExpired(DateTime currentPoint)
        {
            return currentPoint > Timestamp.AddMinutes(ExpirationInMinutes);
        }
    }

}