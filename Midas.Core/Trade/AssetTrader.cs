using Midas.Core;
using Midas.Sources;
using Midas.FeedStream;
using Midas.Core.Common;
using System.Threading;
using System.Collections.Generic;
using MongoDB.Driver;
using System;
using MongoDB.Bson;
using System.Linq;
using Midas.Util;
using Midas.Core.Indicators;
using System.IO;
using Midas.Core.Util;
using System.Drawing;
using Midas.Core.Telegram;
using Midas.Trading;
using Midas.Core.Chart;
using System.Threading.Tasks;
using Midas.Core.Broker;
using System.Text;
using Midas.Core.Services;
using Midas.Core.Forecast;
using System.Collections.Concurrent;

namespace Midas.Core.Trade
{
    public class AssetTrader
    {
        private static string WEB_SOCKETURI = "wss://stream.binance.com:9443/ws";
        private string _asset;
        private CandleType _candleType;
        private LiveAssetFeedStream _stream;
        private RunParameters _params;
        private AssetParameters _assetParams;
        private int _timeOut;
        private FixedSizedQueue<Candle> _candleMovieRoll;
        private List<CalculatedIndicator> _indicators;
        private TradeOperationManager _manager;
        private string _myName;
        private InvestorService _myService;

        private ConcurrentDictionary<string, TradeOperation> _operationMap;

        public AssetTrader(InvestorService service, string asset, CandleType candleType, RunParameters @params, int timeOut, AssetParameters assetParams)
        {
            _assetParams = assetParams;
            _running = false;
            _myService = service;
            _asset = asset;
            _candleType = candleType;
            _params = @params;
            _timeOut = timeOut;
            _myName = String.Format("AssetTrader_{0}_{1}", Asset, _candleType);
            _indicators = _params.GetIndicators();
            _manager = TradeOperationManager.GetManager(this, _params.DbConString, assetParams.FundName, _params.BrokerName, _params.BrokerParameters, asset, candleType, _params.ExperimentName);

            _operationMap = new ConcurrentDictionary<string, TradeOperation>();

            //AssetPriceHub.InitAssetPair(asset);
        }

        public override string ToString()
        {
            return $"{_asset}:{_candleType} - {_assetParams}";
        }

        public double GetMAValue(string maName)
        {
            var allCandles = _candleMovieRoll.GetList();
            DateRange range = new DateRange(allCandles.First().OpenTime, allCandles.Last().OpenTime);

            var maSeries = _indicators.Where(i => i.Name == maName).FirstOrDefault();

            double? maValue = null;
            if (maSeries != null)
            {
                var maWindow = maSeries.TakeSnapShot(range);
                if (maWindow.Count() > 0)
                    maValue = maWindow.Last()?.AmountValue;
            }

            return (maValue == null ? 0 : (double)maValue);
        }

        private bool IsMAMovingUp(string maName)
        {
            var maList = GetMAList(maName);

            bool ret = false;
            if (maList.Count > 2)
            {
                var lastValue = maList[maList.Count - 1].CloseValue;
                var oneBeforeLastValue = maList[maList.Count - 2].CloseValue;

                ret = lastValue > oneBeforeLastValue;
            }

            return ret;
        }

        public List<IStockPointInTime> GetMAList(string maName)
        {
            var allCandles = _candleMovieRoll.GetList();
            DateRange range = new DateRange(allCandles.First().OpenTime, allCandles.Last().OpenTime);
            List<IStockPointInTime> result = null;

            var maSeries = _indicators.Where(i => i.Name == maName).FirstOrDefault();

            if (maSeries != null)
            {
                var maWindow = maSeries.TakeSnapShot(range);
                if (maWindow.Count() > 0)
                    result = maWindow.ToList();
            }

            return result;
        }

        public void Start()
        {
            _running = true;

            //RestoreOpIfAny();

            _candleMovieRoll = new FixedSizedQueue<Candle>(_params.WindowSize);

            //Init the previous candles from DB in the movieroll and indicators
            var cacheCandles = GetCachedCandles();
            foreach (var c in cacheCandles)
            {
                _candleMovieRoll.Enqueue(c);

                FeedIndicators(c, "Main");
            };

            var initialVolumes = cacheCandles.Select(p => new VolumeIndicator(p));
            foreach (var v in initialVolumes)
                FeedIndicators(v, "Volume");

            //Start the websocket thread
            _stream = GetLiveStream();
            if (_params.IsTesting)
            {
                Broker.Broker broker = Broker.Broker.GetBroker(_params.BrokerName, _params.BrokerParameters, null);
                _stream.InitPrice(broker.GetPriceQuote(this.Asset));
            }

            //Subscripbe to the candles events
            _stream.OnNewCandle(new SocketNewCancle(this.OnNewCandle));
            _stream.OnUpdate(new SocketEvent(this.OnCandleUpdate));
            _stream.OnUpdate(new SocketEvent(this.OnCandleUpdate_UpdateManager));
            _stream.OnNewInfo(this.NewInfo);

            _stream.OnSocketEnd((param1, param2) =>
            {
                _running = false;
                _myService.Running = false;
            });

            // if (_params.FeedStreamType != "Historical")
            // {
            //     _stream.OnUpdate((id, message, cc) =>
            //     {
            //         var img = GetSnapShot(cc);

            //         img.Save(
            //             Path.Combine(_params.OutputDirectory, _myName + "_LiveInvestor.png"),
            //             System.Drawing.Imaging.ImageFormat.Png
            //             );
            //     });
            // }


            TraceAndLog.StaticLog(_myName, String.Format("Starting runner with {0} cached candles", cacheCandles.Count()));
        }

        // private void RestoreOpIfAny()
        // {
        //     var op = _manager.RestoreState();

        //     if (op != null)
        //     {
        //         TraceAndLog.StaticLog(GetIdentifier(), $"Restored state: {op.ToString()}");
        //         TelegramBot.SendMessage($"Restored state: {op.ToString()}");
        //     }
        // }

        internal List<CalculatedIndicator> Indicators
        {
            get
            {
                return _indicators;
            }
        }

        public AssetParameters AssetParams { get => _assetParams; }
        public string Asset { get => _asset; }
        public CandleType CandleType { get => _candleType; }

        public void Stop(bool closeOp = true)
        {
            if (_running)
            {
                if (closeOp)
                    CloseAllOperationIfAny(true);

                _running = false;

                if (_stream != null)
                    _stream.Dispose();

                if (_manager != null)
                    _manager.Dispose();
            }
        }

        Bitmap _lastImg = null;
        private PredictionBox _predictionBox;

        public string SetPrediction()
        {
            SetPrediction(_lastCandle, true);
            string ret = String.Empty;

            if (_predictionBox != null)
            {
                ret = _predictionBox.GetTrend(0, 0).Item2;

                if (ret == "Alligator Open")
                {
                    _predictionBox.ForceNextPrediction = true;
                    _predictionBox.EntryAmount = _lastCandle.CloseValue;
                }
            }

            return ret;
        }

        private void SetPrediction(Candle current, bool preview)
        {
            _predictionBox = null;

            Candle recentClosedCandle = current;
            Candle[] candlesToDraw = null;
            if (!preview)
            {
                //Feed the indicators
                _candleMovieRoll.Enqueue(recentClosedCandle);
                FeedIndicators(recentClosedCandle, "Main");
                FeedIndicators(new VolumeIndicator(recentClosedCandle), "Volume");

                //Let us get a snapshot of the moment to predict the future shall we?
                candlesToDraw = _candleMovieRoll.GetList();
            }
            else
            {
                var union = new List<Candle>();
                union.AddRange(_candleMovieRoll.GetList());
                union.Add(recentClosedCandle);

                candlesToDraw = union.ToArray();
            }

            var volumes = candlesToDraw.Select(p => new VolumeIndicator(p));

            DateRange range = new DateRange(candlesToDraw.First().OpenTime, candlesToDraw.Last().OpenTime);

            if (_lastImg != null)
                _lastImg.Dispose();

            _lastImg = GetImage(_params, candlesToDraw, volumes, range, null, !preview, true);

            if (_lastImg != null)
            {
                _lastImg.Save(
                    Path.Combine(_params.OutputDirectory, _myName + "_LiveInvestor.png"),
                    System.Drawing.Imaging.ImageFormat.Png
                    );

                //Get a prediction
                string urlPriceModel = _params.UrlPriceModel;

                var priceForecaster = ForecastFactory.GetForecaster(_params.Forecaster, urlPriceModel);

                Prediction priceRes = null;
                try
                {
                    priceRes = priceForecaster.GetPrediction(_lastImg, _asset, current.AmountValue, current.OpenTime);
                }
                catch (Exception err)
                {
                    TraceAndLog.StaticLog("Prediction", "Erro when predicting: " + err.Message);
                }


                if (priceRes != null)
                    priceRes.CandleThatPredicted = current;

                DateTime previousDate = DateTime.MinValue;
                if (_predictionBox != null)
                    previousDate = _predictionBox.PredictionDate;
                var tempPredBox = new PredictionBox();

                tempPredBox.ByPrice = priceRes;
                tempPredBox.CandleThatPredicted = current;
                tempPredBox.SetPreviousPrediction(previousDate);

                _predictionBox = tempPredBox;

                var trend = _predictionBox.GetTrend(0, 0);
                // if (trend.Item2 == "Alligator Open" || trend.Item2 == "BottomReserval")
                // {
                //     var peekFactor = GetPeekFactor(current);
                //     if (peekFactor > _params.MIN_PEEK_STRENGH)
                //     {
                //         var imgToSend = GetImage(_params, candlesToDraw, volumes, range, null, !preview, false);
                //         TelegramBot.SendImage(imgToSend, $"Just got a high volume {trend.Item2}");
                //     }
                // }
            }
        }

        public PredictionBox PredictionBox
        {
            get
            {
                return _predictionBox;
            }
        }

        private void OnNewCandle(string id, Candle previous, Candle current)
        {
            //TraceAndLog.StaticLog(_myName, $"====== {previous.PointInTime_Open.ToString("yyyy-MM-dd HH:mm")} ======");

            try
            {
                SetPrediction(previous, false);

                if (_predictionBox != null)
                {
                    var currentTrend = _predictionBox.GetTrend(_assetParams.ScoreByAvg, _assetParams.ScoreByPrice);
                    _predictionBox.EntryAmount = previous.CloseValue;

                    _manager.Signal(currentTrend.Item1);
                }


                var activeOps = _manager.GetAllActiveOperations();
                if(activeOps.Count() > 0)
                {
                    var currentImg = GetSnapshotForBot();
                    if(currentImg != null)
                    {
                        Console.WriteLine($"There are operations running on the pair {this.GetIdentifier()}");
                        TelegramBot.SendImage(currentImg, $"There are operations running on the pair {this.GetIdentifier()}");
                        TelegramBot.SendMessage("Follow me live on https://www.twitch.tv/cirofns");
                    }
                }
            }
            catch (Exception err)
            {
                TraceAndLog.StaticLog(_myName, "Predict Error - " + err.ToString());
            }
        }

        private Candle _lastCandle;
        private double _lastPrice;
        private double _lastAtr;
        private bool _running;

        private void OnCandleUpdate_UpdateManager(string id, string message, Candle cc)
        {
            try
            {
                _manager.OnCandleUpdate(cc);

                //AssetPriceHub.UpdatePrice(_asset, cc.CloseValue);
            }
            catch (Exception err)
            {
                TraceAndLog.GetInstance().Log(_myName, "Error invoking KlineObserver - " + err.ToString());
            }
        }

        private DateTime _lastEntrance;
        private DateTime _lastClose = DateTime.MinValue;

        private void OnCandleUpdate(string id, string message, Candle cc)
        {
            _lastCandle = cc;
            _lastPrice = cc.CloseValue;

            try
            {
                TrendType currentTrend = TrendType.NONE;
                if (_predictionBox != null)
                {
                    currentTrend = _predictionBox.GetTrend(_assetParams.ScoreByAvg, _assetParams.ScoreByPrice).Item1;
                }

                if (currentTrend == TrendType.LONG)
                {
                    var candlesToDraw = _candleMovieRoll.GetList();
                    DateRange range = new DateRange(candlesToDraw.First().PointInTime_Open, candlesToDraw.Last().PointInTime_Open);

                    double atr = GetMAValue("ATR");
                    double ratr = atr / cc.CloseValue;

                    var currentCandle = cc;

                    var peekFactor = GetPeekFactor(cc);
                    var variation = GetWindowVariation(cc);

                    if (_predictionBox.ForceNextPrediction ||
                        (
                            !_operationMap.ContainsKey(_predictionBox.CandleThatPredicted.GetCompareStamp()) && //To prevent generating the same signals from the same candle
                            peekFactor > _params.MIN_PEEK_STRENGH) //&& //Enter only if we are in the volume peak
                            // (cc.OpenTime - _lastEntrance).TotalHours > 3 && //Ignore signals from the same "moment"
                            // (cc.OpenTime - _lastClose).TotalHours > 3) //Ignore signals close the last close op
                            //variation > 1 && variation < 4)
                        )
                    {

                        _predictionBox.ForceNextPrediction = false;

                        var outputDir = Directory.CreateDirectory(
                            Path.Combine(_params.OutputDirectory, _params.ExperimentName)
                            );

                        try
                        {
                            _lastAtr = atr;
                            _lastEntrance = cc.OpenTime;

                            var currentTrendInBox = _predictionBox.GetTrend(_assetParams.ScoreByAvg, _assetParams.ScoreByPrice);
                            var op = _manager.SignalEnter(
                                _predictionBox.EntryAmount,
                                cc.OpenTime,
                                cc.CloseTime.AddHours(12),
                                ratr,
                                currentTrendInBox.Item2
                                );

                            _operationMap[_predictionBox.CandleThatPredicted.GetCompareStamp()] = op;

                            var fileName = Path.Combine(outputDir.FullName, $"{currentTrendInBox.Item2}-{GetIdentifier()}_{cc.OpenTime:yyyy-MM-dd HH-mm}_entry.png");

                            _lastImg.Save(fileName, System.Drawing.Imaging.ImageFormat.Png);


                            if (op != null)
                            {
                                TelegramBot.SendImage(_lastImg, $"Starting operation {Telegram.TelegramEmojis.CrossedFingers} on the pair {this.GetIdentifier()}");
                            }
                        }
                        catch (Exception err)
                        {
                            TraceAndLog.StaticLog("Main", "Error entering: " + err.ToString());
                        }

                    }
                }
            }
            catch (Exception err)
            {
                TraceAndLog.GetInstance().Log("Entrada Error", err.ToString());
            }

        }

        private bool Is200AvgUp()
        {
            var avg200List = GetMAList("MA200");

            if (avg200List != null)
                return avg200List.Last().AmountValue >= avg200List.First().AmountValue;
            else
                return true;
        }

        private bool IsPriceAboveAvg(string avgName)
        {
            var avg200List = GetMAList(avgName);
            var currentPrice = _lastPrice == 0 ? 0 : _lastPrice;

            if (avg200List != null)
                return currentPrice > avg200List.Last().AmountValue;
            else
                return false;
        }

        private double GetAvg6To200Spread()
        {
            var avg6 = GetMAValue("MA6");
            var avg200 = GetMAValue("MA200");

            return ((avg6 - avg200) / avg200) * 100;
        }

        private double GetPeekFactor(Candle currentCandle)
        {
            var candlesToDraw = _candleMovieRoll.GetList().Union(new Candle[] { currentCandle });
            var volumes = candlesToDraw.Select(p => new VolumeIndicator(p));

            var currentRange = new DateRange(candlesToDraw.First().PointInTime_Open, candlesToDraw.Last().PointInTime_Open);
            var avgVolumes = _indicators.Where(i => i.Name == "MA144")?.FirstOrDefault()?.TakeSnapShot(currentRange);

            double maxPeekFactor = -1;
            double lastAvgVolume = 0;
            if (avgVolumes.Count() > 0)
            {
                lastAvgVolume = avgVolumes.Last().AmountValue;

                var windowCandles = candlesToDraw;

                var twentyPercentLast = windowCandles.TakeLast(10).ToList();

                twentyPercentLast.ForEach(c =>
                {
                    var ratio = c.Volume / lastAvgVolume;

                    if (ratio > maxPeekFactor)
                        maxPeekFactor = ratio;
                });
            }

            //Mandar o PeekFactor por mensagem

            return maxPeekFactor;
        }

        private bool HasRecentPeakVolumeOnly(Candle currentCandle)
        {
            var candlesToDraw = _candleMovieRoll.GetList().Union(new Candle[] { currentCandle });
            var volumes = candlesToDraw.Select(p => new VolumeIndicator(p));

            var volumeMax = volumes.Max(v => v.Volume);

            var currentRange = new DateRange(candlesToDraw.First().PointInTime_Open, candlesToDraw.Last().PointInTime_Open);

            bool inPeek = false;
            if (volumes.Count() > 40)
            {
                var fourLast = volumes.TakeLast(4).ToList();
                var first40 = volumes.Take(40).ToList();

                double fourLastMaxRelative = 0;
                fourLast.ForEach(c =>
                {
                    var ratio = c.Volume / volumeMax;
                    if (ratio > fourLastMaxRelative)
                        fourLastMaxRelative = ratio;
                });

                double first40MaxRelative = 0;
                first40.ForEach(c =>
                {
                    var ratio40 = c.Volume / volumeMax;
                    if (ratio40 > first40MaxRelative)
                        first40MaxRelative = ratio40;
                });

                var ratio = fourLastMaxRelative / first40MaxRelative;
                if (fourLastMaxRelative / first40MaxRelative > 2)
                    inPeek = true;
            }

            return inPeek;
        }

        public double GetWindowVariation(Candle cc)
        {
            var windowCandles = _candleMovieRoll.GetList().Union(new Candle[] { cc });

            var diff = Math.Abs(windowCandles.Max(c => c.HighestValue) - windowCandles.Min(c => c.LowestValue));
            var firstCandle = windowCandles.First();

            return (diff / firstCandle.CloseValue) * 100;
        }

        private double GetCurrentAtr(DateRange range)
        {
            var atrInd = _indicators.Where(i => i.Name == "ATR").First();

            return atrInd.TakeSnapShot(range).Last().AmountValue;
        }

        private bool CheckDelayedTrigger(Candle candleThatPredicted, Candle currentCandle)
        {
            bool ret = false;

            if (!_params.DelayedTriggerEnabled)
                ret = true;
            else
            {
                if (candleThatPredicted.Direction == CandleDirection.Down &&
                    currentCandle.CandleAge.TotalMinutes > 4 && currentCandle.GetIndecisionThreshold() > 0.2)
                    ret = true;
                else if (candleThatPredicted.Direction == CandleDirection.Up)
                    ret = true;
            }
            return ret;
        }

        public string GetParametersReport()
        {
            string configs = $"<b>== {this.Asset.ToUpper()} - {this._candleType.ToString()} = {_params.WindowSize.ToString()} W ==</b>\n";
            configs += $"Score Avg: {this.AssetParams.ScoreByAvg.ToString("0.00")}%\n";
            configs += $"Score Price: {this.AssetParams.ScoreByPrice.ToString("0.00")}%\n";
            configs += $"Fund: ${this._manager.Funds:0.0000}\n";
            configs += $"{_params.CardWidth} x {_params.CardHeight}\n";
            configs += $"Delay triggger {(_params.DelayedTriggerEnabled ? "ON" : "OFF")}";

            return configs;
        }

        internal void SaveSnapshot(Candle cc, TradeOperation op)
        {
            _lastClose = cc.CloseTime;

            var outputDir = Directory.CreateDirectory(
                Path.Combine(_params.OutputDirectory, _params.ExperimentName)
                );

            string identifier = op.ModelName+"-"+this.GetIdentifier();
            var fileName = Path.Combine(outputDir.FullName, $"{identifier}_{cc.OpenTime:yyyy-MM-dd HH-mm}.png");

            var img = GetSnapShot(cc);

            if (_manager != null)
            {
                _manager.SendImage(img, $"That's how I looked! On {op.GetGain():0.000}% ");
            }

            using (img)
            {
                img.Save(fileName, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        internal string GetIdentifier()
        {
            return $"{Asset}-{_candleType.ToString()}";
        }

        internal string GetShortIdentifier()
        {
            return AssetTrader.GetShortIdentifier(this.Asset, this._candleType);
        }

        public static string GetShortIdentifier(string asset, CandleType candle)
        {
            return $"{asset.Substring(0, 3)}{Convert.ToInt32(candle)}";
        }


        internal Bitmap GetSnapShot(Candle cc)
        {
            var candlesToDraw = _candleMovieRoll.GetList().Union(new Candle[] { cc });
            var volumes = candlesToDraw.Select(p => new VolumeIndicator(p));
            Bitmap imgToBroadcast = null;

            if (candlesToDraw.Count() > 0)
            {

                DateRange range = new DateRange(candlesToDraw.First().PointInTime_Open, candlesToDraw.Last().PointInTime_Open);

                var operations = _manager.GetLastRecentOperations();
                if (operations != null)
                {
                    List<VolumeIndicator> newVolumes = volumes.ToList();

                    var predictionSerie = new Serie();
                    foreach (var op in operations)
                    {

                        var operation = op;

                        var forecastPoint = new TradeOperationCandle()
                        {

                            AmountValue = operation.PriceEntry,
                            LowerBound = operation.PriceEntry * (1 + (0.5 / 100)),
                            UpperBound = operation.PriceEntry * (1 + (0.75 / 100)),
                            Gain = operation.GetGain(cc.CloseValue),
                            ExitValue = operation.PriceExitAverage,
                            StopLossMark = operation.StopLossMark,
                            SoftStopMark = operation.SoftStopLossMarker,
                            Volume = 1,
                            State = $"{operation.State.ToString()} [{operation.PurchaseStatusDescription}] ",
                            PointInTime_Open = operation.EntryDate,
                            PointInTime_Close = operation.ExitDate
                        };
                        if (!operation.IsIn)
                            forecastPoint.Gain = operation.GetGain();

                        predictionSerie.PointsInTime.Add(forecastPoint);

                        newVolumes.Add(new VolumeIndicator(forecastPoint));

                        predictionSerie.Name = "Predictions";
                        predictionSerie.Color = Color.LightBlue;
                        predictionSerie.Type = SeriesType.Forecast;
                    }

                    imgToBroadcast = GetImage(_params, candlesToDraw, newVolumes, range, predictionSerie, false, false);
                }
                else
                {
                    imgToBroadcast = GetImage(_params, candlesToDraw, volumes, range, null, false, false);
                }
            }

            return imgToBroadcast;
        }



        internal Bitmap GetImage(RunParameters runParams, IEnumerable<IStockPointInTime> candles, IEnumerable<IStockPointInTime> volumes, DateRange range, Serie prediction = null, bool onlySim = true, bool forPrediction = true)
        {
            int cardWidth = runParams.CardWidth;
            int cardHeight = runParams.CardHeight;

            if (!forPrediction)
            {
                cardWidth = 1100;
                cardHeight = 900;
            }

            DashView dv = new DashView(cardWidth, cardHeight);

            var frameMap = new Dictionary<string, ChartView>();
            if (forPrediction)
            {
                frameMap.Add("VoidC", dv.AddChartFrame(20));
                frameMap.Add("Main", dv.AddChartFrame(60));
                frameMap.Add("VoidB", dv.AddChartFrame(20));
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

            if (prediction != null)
                frameMap["Main"].AddSerie(prediction);

            if (!forPrediction)
            {
                frameMap["Volume"].AddSerie(new Serie()
                {
                    PointsInTime = volumes.ToList<IStockPointInTime>(),
                    Name = "Volume",
                    Color = Color.LightBlue,
                    Type = SeriesType.Bar,
                    LineSize = 3
                });
            }

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
                        if (!forPrediction)
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

            return (img == null ? null : img.RawImage);
        }


        private void NewInfo(string id, string message)
        {
            try
            {
                _lastSocketUpdate = DateTime.Now;
                if (_params.IsTesting)
                    Console.WriteLine(id.ToUpper() + " - " + message);
            }
            catch (Exception err)
            {
                TraceAndLog.StaticLog("NewInfo", err.ToString());
            }
        }

        private DateTime _lastSocketUpdate = DateTime.MinValue;
        private void MonitoringRunner()
        {
            Task.Run(() =>
            {
                while (_running)
                {
                    Thread.Sleep(100);
                    var span = (DateTime.Now - _lastSocketUpdate);
                    if (span.TotalSeconds > 30)
                        TelegramBot.SendMessageBuffered("HearBeat Alert", $"{this.GetIdentifier()}:Atenção! Last heart beat was {span.TotalSeconds:0.00} seconds ago.");
                }
            });
        }


        internal string GetReport()
        {
            return _myService.GetReport(this.Asset, CandleType.None);
        }

        internal Bitmap GetSnapshotForBot()
        {
            if (_lastCandle != null)
                return GetSnapShot(_lastCandle);
            else
                return null;
        }

        internal string GetTextSnapShot()
        {
            StringBuilder sb = new StringBuilder(200);

            if (_predictionBox != null)
            {
                sb.Append(_predictionBox.ToString());
                sb.AppendLine();
            }

            var candlesToDraw = _candleMovieRoll.GetList();
            if (candlesToDraw.Count() > 0)
            {
                DateRange range = new DateRange(candlesToDraw.First().PointInTime_Open, candlesToDraw.Last().PointInTime_Open);
                double atr = GetCurrentAtr(range);

                if (_lastPrice > 0 && atr > 0)
                {
                    sb.AppendLine("Price: $" + _lastPrice.ToString("0.00"));
                    sb.AppendLine("ATR: $" + atr.ToString("0.00"));
                    sb.AppendLine("RATR: " + ((atr / _lastPrice) * 100).ToString("0.000") + "%");
                }
            }
            else
            {
                sb.Append("No candles here yet");
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

        public bool CloseAllOperationIfAny(bool hard = true)
        {
            var haveSome = false;
            var allActiveOps = _manager.GetAllActiveOperations();
            foreach (var op in allActiveOps)
            {
                haveSome = true;
                op.CloseOperationAsync(hard);
            }

            return haveSome;
        }


        internal string GetState()
        {
            var ops = _manager.GetAllActiveOperations();

            string state = "";
            foreach(var op in ops)
            {
                state += op.ToString();
                state += "\n\n";
            }

            return state;
        }

        private void FeedIndicators(IStockPointInTime point, string source)
        {
            foreach (var ind in _indicators)
            {
                if (ind.Source == source)
                    ind.AddPoint(point);
            }
        }

        private void StorePrediction(string myName, List<PredictionResult> res)
        {
            try
            {
                var rec = new PredictionRecord()
                {
                    UtcCreationDate = DateTime.UtcNow,
                    Predictions = res,
                    Identifier = myName
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

        private IEnumerable<Candle> GetCachedCandles()
        {
            IEnumerable<Candle> ret = new List<Candle>();

            if (_params.PreloadCandles)
            {
                var periods = _params.WindowSize + 200;//200 é a maior média móvel
                var begin = DateTime.UtcNow.AddMinutes(periods * Convert.ToInt32(_candleType) * -1);

                var asset = this.Asset.Replace("USDT", "BUSD");

                var seconds = Convert.ToInt32(_candleType);

                var lastCandles = CandlesGateway.GetCandlesFromRest(asset, this._candleType, new DateRange(begin, DateTime.UtcNow.AddMinutes(seconds * -1)));
                ret = lastCandles;
            }

            return ret;
        }

        private LiveAssetFeedStream GetLiveStream()
        {
            LiveAssetFeedStream ret;

            if (_params.FeedStreamType == "Live")
            {
                BinanceWebSocket sock = new BinanceWebSocket(
                    WEB_SOCKETURI, _timeOut, Asset, _candleType
                );

                ret = sock.OpenAndSubscribe();
            }
            else if (_params.FeedStreamType == "Historical")
            {
                ret = new HistoricalLiveAssetFeedStream(
                    null, Asset, _candleType, _candleType
                );

                ((HistoricalLiveAssetFeedStream)ret).DateRange = new DateRange(
                    _params.Range.Start,
                    _params.Range.End
                );
            }
            else if (_params.FeedStreamType == "Test")
            {
                ret = new TestLiveAssetFeedStream(
                    null, _params.Asset, _params.CandleType, _params.CandleType
                    );
            }
            else
            {
                throw new NotImplementedException(_params.FeedStreamType + " - Not implemented");
            }

            return ret;
        }
    }

    public class PredictionRecord
    {
        public DateTime UtcCreationDate
        {
            get;
            set;
        }

        public string Identifier
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

    public class PredictionBox
    {
        private string[] types = new string[] {"200_121_3_Long","160_59_3_Long","165_34_3_Long","190_121_3_Long","220_50_3_Long","200_37_3_Long","220_15_3_Long","190_130_3_Long",
              "400_362_3_Short", "600_171_3_Short" ,"400_274_3_Short", "600_309_1_Short","155_50_3_Short", "400_194_3_Short", "180_50_3_Short", "150_43_3_Short"
        };

        public Candle CandleThatPredicted { get; internal set; }
        public Prediction ByPrice { get; internal set; }
        public Prediction ByAvg { get; internal set; }

        private DateTime _predictionDate;

        private DateTime _previousPrediction;

        private TrendType _trendType;
        private string _trendDescription;


        private readonly float _scoreThresholdByAvg;
        private readonly float _scoreThresholdByPrice;

        public DateTime PreviousPrediction { get => _previousPrediction; }
        public DateTime PredictionDate { get => _predictionDate; }

        public bool ForceNextPrediction { get; set; }
        public double EntryAmount { get; internal set; }

        public PredictionBox(float scoreThresholdByAvg, float scoreThresholdByPrice)
        {
            this._scoreThresholdByAvg = scoreThresholdByAvg;
            this._scoreThresholdByPrice = scoreThresholdByPrice;
            _predictionDate = DateTime.Now;
        }

        public PredictionBox()
        {
            this._scoreThresholdByAvg = -1;
            this._scoreThresholdByPrice = 1;
            _predictionDate = DateTime.Now;
            ForceNextPrediction = false;
        }

        public void SetPreviousPrediction(DateTime time)
        {
            _previousPrediction = time;
        }

        //Estavamos usando este método quando tinhamos que chamar dois modelos ao mesmo tempo
        // public Tuple<bool, string> GoodToEnter(double ma6, double atr)
        // {
        //     var ret = false;
        //     var trend = GetTrend(_scoreThresholdByAvg, _scoreThresholdByPrice);
        //     var amount = CandleThatPredicted.CloseValue;
        //     string trendType = String.Empty;
        //     string connector;
        //     double compareAtr = atr * 0.75;

        //     if (trend == TrendType.LONG || trend == TrendType.DOUBLE_LONG)
        //     {
        //         // if (ByAvg != null && GetDiffAmount1VsAmount2(amount, ma6) < compareAtr && ByAvg.ScoreLong >= _scoreThresholdByAvg)
        //         // {
        //         //     ret = true;
        //         //     trendType = "Average";
        //         // }

        //         if (ByPrice != null && GetDiffAmount1VsAmount2(amount, ma6) > compareAtr * -1 && ByPrice.ScoreLong >= _scoreThresholdByPrice)
        //         {
        //             connector = (trendType == String.Empty) ? "" : " - ";
        //             trendType += connector + "Price Trend";
        //             ret = true;
        //         }
        //     }

        //     return new Tuple<bool, string>(ret, trendType);
        // }

        private double GetDiffAmount1VsAmount2(double amount1, double amount2)
        {
            var diff = ((amount1 - amount2) / amount2) * 100;
            return diff;
        }

        public Tuple<TrendType, string> GetTrend(float scoreThresholdByAvg, float scoreThresholdByPrice)
        {
            TrendType retPrice = TrendType.NONE;
            string trendDescription = null;

            var runParams = RunParameters.GetInstance();
            string entryFilter = null;
            runParams.NamedParams.TryGetValue("EntryFilter",out entryFilter);

            if (ByPrice != null)
            {
                if(ByPrice.CurrentTrend.EndsWith("Long"))
                {
                    retPrice = TrendType.LONG;
                    trendDescription = ByPrice.CurrentTrend;
                }
                else
                {
                    retPrice = TrendType.SHORT;
                    trendDescription = ByPrice.CurrentTrend;
                }

                if(entryFilter != null)
                {
                    if(trendDescription != entryFilter)
                    {
                        retPrice = TrendType.NONE;
                        trendDescription = trendDescription += "Ignored";
                    }
                }
            }

            _trendType = retPrice;
            _trendDescription = trendDescription;


            return new Tuple<TrendType, string>(retPrice, trendDescription);
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();

            var trend = GetTrend(_scoreThresholdByAvg, _scoreThresholdByPrice);
            sb.Append($"Gerada em: <b>{_predictionDate:dd/MM/yyyy HH:mm}</b>\n");
            sb.Append($"Trend: <b>{_trendDescription}</b>\n\n");

            return sb.ToString();
        }
    }

    public enum TrendType
    {
        LONG,
        DOUBLE_LONG,
        SHORT,

        DOUBLE_SHORT,

        NONE,
        LONG_SHORT_CONFLICT
    }
}