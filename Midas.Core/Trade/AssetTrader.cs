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
        private StreamWriter _streamLog;
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

            AssetPriceHub.InitAssetPair(asset);
        }

        public override string ToString()
        {
            return $"{_asset}:{_candleType} - {_assetParams}";
        }

        public double GetMAValue(string maName)
        {
            var maSeries = _indicators.Where(i => i.Name == maName).FirstOrDefault();

            double? maValue = 0;
            if (maSeries != null)
                maValue = maSeries?.LastPoint?.AmountValue;

            return (maValue == null ? 0 : (double)maValue);
        }

        public double? GetFirstMAValue(string maName)
        {
            double? ret = 0;

            var indicator = _indicators.Where(i => i.Name == maName).FirstOrDefault();
            if (indicator != null)
            {
                ret = indicator.TakeSnapShot().FirstOrDefault()?.AmountValue;
            }

            return ret ?? 0;
        }

        public double GetDifferentFromPast(double currentAmount, DateTime min)
        {
            var candles = _candleMovieRoll.GetList();

            var last = candles.Where(c => c.OpenTime >= min).ToList();

            double diff = 0;
            if (last.Count > 0)
                diff = ((currentAmount - last.First().OpenValue) / last.First().OpenValue) * 100;

            return diff;
        }

        public void Start()
        {
            _running = true;

            //RestoreOpIfAny();

            //Open the streamlog for this trader
            _streamLog = new StreamWriter(
                File.Open(
                    Path.Combine(_params.OutputDirectory, String.Format("{0}_stream.log", _myName)),
                    FileMode.Create, FileAccess.Write, FileShare.Read
                    )
                );

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
                Console.WriteLine("Termininando em 5 segundos");
                Thread.Sleep(5000);

                var pairInfo = AssetPriceHub.GetTicker(_asset);
                pairInfo.DumpInfo();

                _manager.SlotManager.Dump();
            });

            if (_params.FeedStreamType != "Historical")
            {
                _stream.OnUpdate((id, message, cc) =>
                {

                    var img = GetSnapShot(cc);

                    img.Save(
                        Path.Combine(_params.OutputDirectory, _myName + "_LiveInvestor.png"),
                        System.Drawing.Imaging.ImageFormat.Png
                        );
                });
            }


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
                {
                    var currentOp = _manager.GetOneActiveOperation();
                    if (currentOp != null)
                    {
                        currentOp.CloseOperationAsync();
                    }
                }

                _running = false;

                if (_stream != null)
                    _stream.Dispose();

                if (_streamLog != null)
                    _streamLog.Dispose();
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
                ret = _predictionBox.ToString();
            }

            return ret;
        }

        private void SetPrediction(Candle current, bool preview)
        {
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

            _lastImg = GetImage(_params, candlesToDraw, volumes, range, null, !preview);

            if (_lastImg != null)
            {
                _lastImg.Save(
                    Path.Combine(_params.OutputDirectory, _myName + "_LiveInvestor.png"),
                    System.Drawing.Imaging.ImageFormat.Png
                    );

                //Get a prediction
                string urlAvgModel = _params.UrlAvgModel;
                string urlPriceModel = _params.UrlPriceModel;

                var avgForecaster = ForecastFactory.GetForecaster(_params.Forecaster, urlAvgModel);
                var priceForecaster = ForecastFactory.GetForecaster(_params.Forecaster, urlPriceModel);

                Prediction avgRes = null, priceRes = null;

                var t1 = Task.Run(() =>
                {
                    try
                    {

                        avgRes = avgForecaster.GetPrediction(_lastImg, current.AmountValue, current.OpenTime);
                    }
                    catch (Exception err)
                    {
                        TraceAndLog.StaticLog("Prediction", "Erro when predicting: " + err.Message);
                    }
                });
                var t2 = Task.Run(() =>
                {
                    try
                    {
                        priceRes = priceForecaster.GetPrediction(_lastImg, current.AmountValue, current.OpenTime);
                    }
                    catch (Exception err)
                    {
                        TraceAndLog.StaticLog("Prediction", "Erro when predicting: " + err.Message);
                    }
                });

                Task.WaitAll(t1, t2);

                if (avgRes != null)
                    avgRes.CandleThatPredicted = current;

                if (priceRes != null)
                    priceRes.CandleThatPredicted = current;

                //StorePrediction(_myName, predictions.Result);

                var tempPredBox = new PredictionBox(_assetParams.ScoreByAvg, _assetParams.ScoreByPrice);

                tempPredBox.ByPrice = priceRes;
                tempPredBox.ByAvg = avgRes;
                tempPredBox.CandleThatPredicted = current;

                _predictionBox = tempPredBox;
            }
        }

        public PredictionBox PredictionBox
        {
            get
            {
                return _predictionBox;
            }
        }

        public bool GoodToEnter()
        {
            var ma6 = GetMAValue("MA6");
            var atr = GetMAValue("ATR");
            bool goodToEnter = false;

            if (_predictionBox != null)
            {
                var ret = _predictionBox.GoodToEnter(ma6, atr);
                goodToEnter = ret.Item1;
            }

            return goodToEnter;
        }

        private void OnNewCandle(string id, Candle previous, Candle current)
        {
            //TraceAndLog.StaticLog(_myName, $"====== {previous.PointInTime_Open.ToString("yyyy-MM-dd HH:mm")} ======");

            try
            {
                SetPrediction(previous, false);

                if (_predictionBox != null)
                {
                    var currentTrend = _predictionBox.GetTrend(_assetParams.ScoreByPrice, _assetParams.ScoreByAvg);

                    _manager.Signal(currentTrend);

                    Console.WriteLine("Trend: " + currentTrend);
                    TelegramBot.SendMessage($"{GetIdentifier()} is " + currentTrend);
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
                AssetPriceHub.UpdatePrice(_asset, cc.CloseValue);
            }
            catch (Exception err)
            {
                TraceAndLog.GetInstance().Log(_myName, "Error invoking KlineObserver - " + err.ToString());
            }
        }

        private string _lastKey;

        private void OnCandleUpdate(string id, string message, Candle cc)
        {
            _lastCandle = cc;
            _lastPrice = cc.CloseValue;

            try
            {
                //If we ever need this code to get previews
                int minutes = (DateTime.UtcNow - cc.OpenTime).Minutes;

                // int rest = minutes % 15;
                // if (_lastKey != cc.GetCompareStamp() && rest == 0)
                //     SetPrediction(cc, true);

                _lastKey = cc.CloseTime.ToString("yyyy-MM-dd HH:") + minutes.ToString("00");

                var ma6 = GetMAValue("MA6");
                var ma200 = GetMAValue("MA200");
                double ln = GetMAValue("LN");
                double atr = GetMAValue("ATR");

                Tuple<bool, string> goodToEnterRes = null;

                if (_predictionBox != null)
                    goodToEnterRes = _predictionBox.GoodToEnter(ma6, ln);

                if (goodToEnterRes != null &&
                    goodToEnterRes.Item1) //Delayed Trigger buffer
                {
                    var currentCandle = cc;

                    bool delayedTriggerCheck = CheckDelayedTrigger(_predictionBox.CandleThatPredicted, currentCandle);

                    if (delayedTriggerCheck && !_operationMap.ContainsKey(_predictionBox.CandleThatPredicted.GetCompareStamp()))
                    {
                        try
                        {
                            var candlesToDraw = _candleMovieRoll.GetList();
                            DateRange range = new DateRange(candlesToDraw.First().PointInTime_Open, candlesToDraw.Last().PointInTime_Open);

                            _lastAtr = ln;

                            var op = _manager.SignalEnter(
                                cc.CloseValue,
                                cc.OpenTime,
                                cc.CloseTime.AddHours(3),
                                ln,
                                goodToEnterRes.Item2
                                );

                            if (op != null)
                            {
                                Console.WriteLine("Entrando pelo modelo - " + goodToEnterRes.Item2);
                                TelegramBot.SendMessage($"Iniciando operação {Telegram.TelegramEmojis.CrossedFingers} em {this.GetIdentifier()}, no modelo {goodToEnterRes.Item2}");
                            }

                            _operationMap[_predictionBox.CandleThatPredicted.GetCompareStamp()] = op;
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
                    currentCandle.CandleAge.TotalMinutes > 13 && currentCandle.GetIndecisionThreshold() > 0.2)
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

        public string GetSlotDump()
        {
            var price = AssetPriceHub.GetTicker(this._asset)?.LastPrice ?? 0;
            return _manager.SlotManager.DumpState(price);
        }

        internal void SaveSnapshot(Candle cc)
        {
            var outputDir = Directory.CreateDirectory(
                Path.Combine(_params.OutputDirectory, _params.ExperimentName)
                );

            var fileName = Path.Combine(outputDir.FullName, $"{GetIdentifier()}_{cc.OpenTime:yyyy-MM-dd HH-mm}");

            var img = GetSnapShot(cc);

            img.Save(fileName, System.Drawing.Imaging.ImageFormat.Png);
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

                            AmountValue = operation.PriceEntryAverage,
                            LowerBound = operation.PriceEntryAverage * (1 + (0.4 / 100)),
                            UpperBound = operation.PriceEntryAverage * (1 + (0.6 / 100)),
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

                    imgToBroadcast = GetImage(_params, candlesToDraw, newVolumes, range, predictionSerie, false);
                }
                else
                {
                    imgToBroadcast = GetImage(_params, candlesToDraw, volumes, range, null, false);
                }
            }

            return imgToBroadcast;
        }



        internal Bitmap GetImage(RunParameters runParams, IEnumerable<IStockPointInTime> candles, IEnumerable<IStockPointInTime> volumes, DateRange range, Serie prediction = null, bool onlySim = true)
        {
            Bitmap img = null;
            DashView dv = new DashView(runParams.CardWidth, runParams.CardHeight);

            var frameMap = new Dictionary<string, ChartView>();
            frameMap.Add("Blank", dv.AddChartFrame(30));
            frameMap.Add("Main", dv.AddChartFrame(40));
            frameMap.Add("Volume", dv.AddChartFrame(30));

            frameMap["Main"].AddSerie(new Serie()
            {
                PointsInTime = candles.ToList(),
                Name = "Main",
                DrawShadow = runParams.DrawShadow
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

                        if (s.Name != "MA144" && s.Name != "MA309")
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


        private void NewInfo(string id, string message)
        {
            try
            {
                _lastSocketUpdate = DateTime.Now;
                if (_params.IsTesting)
                    Console.WriteLine(id.ToUpper() + " - " + message);

                if (_streamLog != null)
                {
                    _streamLog.WriteLine(message);
                    _streamLog.Flush();
                }
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
            foreach(var op in allActiveOps)
            {
                haveSome = true;
                op.CloseOperationAsync(hard);
            }

            return haveSome;
        }


        internal string GetState()
        {
            string state = "";
            var allOps = _manager.GetAllActiveOperations();
            foreach (var op in allOps)
            {
                state += (op != null ? op.ToString() : $"{this.GetIdentifier()} has no operation");
                state += "\n|";
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
                    null, Asset, CandleType.MIN15, _candleType
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
        public Candle CandleThatPredicted { get; internal set; }
        public Prediction ByPrice { get; internal set; }
        public Prediction ByAvg { get; internal set; }

        private DateTime _predictionDate;


        private readonly float _scoreThresholdByAvg;
        private readonly float _scoreThresholdByPrice;

        public PredictionBox(float scoreThresholdByAvg, float scoreThresholdByPrice)
        {
            this._scoreThresholdByAvg = scoreThresholdByAvg;
            this._scoreThresholdByPrice = scoreThresholdByPrice;
            _predictionDate = DateTime.Now;
        }

        public Tuple<bool, string> GoodToEnter(double ma6, double atr)
        {
            var ret = false;
            var trend = GetTrend(_scoreThresholdByAvg, _scoreThresholdByPrice);
            var amount = CandleThatPredicted.CloseValue;
            string trendType = String.Empty;
            string connector;
            double compareAtr = atr * 0.75;

            if (trend == TrendType.LONG || trend == TrendType.DOUBLE_LONG)
            {

                if (ByAvg != null && GetDiffAmount1VsAmount2(amount, ma6) < compareAtr && ByAvg.ScoreLong >= _scoreThresholdByAvg)
                {
                    ret = true;
                    trendType = "Average";
                }

                if (ByPrice != null && GetDiffAmount1VsAmount2(amount, ma6) > compareAtr * -1 && ByPrice.ScoreLong >= _scoreThresholdByPrice)
                {
                    connector = (trendType == String.Empty) ? "" : " - ";
                    trendType += connector + "Price Trend";
                    ret = true;
                }
            }

            return new Tuple<bool, string>(ret, trendType);
        }

        private double GetDiffAmount1VsAmount2(double amount1, double amount2)
        {
            var diff = ((amount1 - amount2) / amount2) * 100;
            return diff;
        }

        public TrendType GetTrend(float scoreThresholdByAvg, float scoreThresholdByPrice)
        {
            TrendType retPrice = TrendType.NONE;
            TrendType retAvg = TrendType.NONE;
            TrendType ret = TrendType.NONE;

            if (ByPrice != null)
            {
                if (ByPrice.ScoreLong >= scoreThresholdByPrice)
                {
                    if (ByPrice.ScoreShort >= scoreThresholdByPrice)
                        retPrice = TrendType.LONG_SHORT_CONFLICT;
                    else
                        retPrice = TrendType.LONG;
                }

                if (ByPrice.ScoreShort >= scoreThresholdByPrice)
                {
                    retPrice = TrendType.SHORT;
                }
            }

            if (ByAvg != null)
            {
                if (ByAvg.ScoreLong >= scoreThresholdByAvg)
                {
                    if (ByAvg.ScoreShort >= scoreThresholdByPrice)
                        retAvg = TrendType.LONG_SHORT_CONFLICT;
                    else
                        retAvg = TrendType.LONG;
                }

                if (ByAvg.ScoreShort >= scoreThresholdByAvg)
                {
                    retAvg = TrendType.SHORT;
                }
            }

            if (retPrice == TrendType.LONG || retAvg == TrendType.LONG)
            {
                if (retPrice == TrendType.LONG && retAvg == TrendType.LONG)
                    ret = TrendType.DOUBLE_LONG;
                else
                    ret = TrendType.LONG;
            }

            if (retPrice == TrendType.SHORT || retAvg == TrendType.SHORT)
            {
                if (retPrice == TrendType.SHORT && retAvg == TrendType.SHORT)
                    ret = TrendType.DOUBLE_SHORT;
                else
                    ret = TrendType.SHORT;
            }


            return ret;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();

            var trend = GetTrend(_scoreThresholdByAvg, _scoreThresholdByPrice);
            sb.Append($"Gerada em: <b>{_predictionDate:dd/MM/yyyy HH:mm}</b>\n");
            sb.Append($"Trend: <b>{trend}</b>\n\n");

            sb.Append("ByAvg Model:\n");
            if (ByAvg != null)
            {
                int index = 1;
                ByAvg.AllPredictions.ForEach(p =>
                {
                    sb.AppendFormat("#{0} - {1} : {2:0.000}%\n", index, p.Tag, p.Score);
                    index++;
                });

                sb.AppendLine();
            }

            sb.Append("ByPrice Model:\n");
            if (ByPrice != null)
            {
                int index = 1;
                ByPrice.AllPredictions.ForEach(p =>
                {
                    sb.AppendFormat("#{0} - {1} : {2:0.000}%\n", index, p.Tag, p.Score);
                    index++;
                });

                sb.AppendLine();
            }

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