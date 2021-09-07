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
        private Candle _candleThatPredicted = null;
        private FixedSizedQueue<Candle> _candleMovieRoll;
        private List<CalculatedIndicator> _indicators;
        private TradeOperationManager _manager;
        private string _myName;
        private InvestorService _myService;

        private ConcurrentDictionary<string, Task<TradeOperation>> _operationMap;

        private IForecast _forecaster;

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

            _forecaster = ForecastFactory.GetForecaster(_params.Forecaster);
            _operationMap = new ConcurrentDictionary<string, Task<TradeOperation>>();
        }

        public void Start()
        {
            _running = true;

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
            cacheCandles.ForEach(c =>
            {
                _candleMovieRoll.Enqueue(c);

                FeedIndicators(c, "Main");
            });

            var initialVolumes = cacheCandles.Select(p => new VolumeIndicator(p));
            foreach (var v in initialVolumes)
                FeedIndicators(v, "Volume");

            //Start the websocket thread
            _stream = GetLiveStream();
            if(_params.IsTesting)
            {
                Broker.Broker broker = Broker.Broker.GetBroker("Binance",_params.BrokerParameters, null);
                _stream.InitPrice(broker.GetPriceQuote(this.Asset));
            }

            //Subscripbe to the candles events
            _stream.OnNewCandle(new SocketNewCancle(this.OnNewCandle));
            _stream.OnUpdate(new SocketEvent(this.OnCandleUpdate));
            _stream.OnUpdate(new SocketEvent(this.OnCandleUpdate_UpdateManager));
            _stream.OnNewInfo(this.NewInfo);

            if (_params.ScoreThreshold >= -1)
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


            TraceAndLog.StaticLog(_myName, String.Format("Starting runner with {0} cached candles", cacheCandles.Count));
        }

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

        public void Stop()
        {
            if(_running)
            {
                var currentOp = _manager.GetOneActiveOperation();
                if(currentOp != null)
                {
                    var closeTask = currentOp.CloseOperation();
                    closeTask.Wait(60000);
                }

                _running = false;

                if (_stream != null)
                    _stream.Dispose();                

                if (_streamLog != null)
                    _streamLog.Dispose();
            }
        }

        Bitmap _lastImg = null;
        private List<PredictionResult> _lastPrediction;

        private void OnNewCandle(string id, Candle previous, Candle current)
        {
            //TraceAndLog.StaticLog(_myName, $"====== {previous.PointInTime_Open.ToString("yyyy-MM-dd HH:mm")} ======");

            try
            {
                string currentTrend = "ZERO";

                //THE ESSENSE OF THIS PROCEDURE
                _candleThatPredicted = null;

                var recentClosedCandle = previous;

                //Feed the indicators
                _candleMovieRoll.Enqueue(recentClosedCandle);
                FeedIndicators(recentClosedCandle, "Main");
                FeedIndicators(new VolumeIndicator(recentClosedCandle), "Volume");

                //Let us get a snapshot of the moment to predict the future shall we?
                var candlesToDraw = _candleMovieRoll.GetList();
                var volumes = candlesToDraw.Select(p => new VolumeIndicator(p));

                DateRange range = new DateRange(candlesToDraw.First().OpenTime, candlesToDraw.Last().OpenTime);

                _lastImg = GetImage(_params, candlesToDraw, volumes, range, null, true);

                if (_lastImg != null)
                {
                    //Get a prediction
                    var predictions = _forecaster.PredictAsync(_lastImg, 0.1f, recentClosedCandle.CloseValue, recentClosedCandle.OpenTime);
                    if (predictions.Wait(10000))
                    {
                        StorePrediction(_myName, predictions.Result);

                        var result = predictions.Result;
                        _lastPrediction = result;
                        if (result.Count > 0)
                        {
                            var predictionLong = result.Where(p => p.Tag == "LONG").FirstOrDefault();
                            var predictionShort = result.Where(p => p.Tag == "SHORT").FirstOrDefault();
                            var predictionND = result.Where(p => p.Tag == "ZERO").FirstOrDefault();

                            int rankShort = 0;
                            int rankLong = 0;
                            double scoreLong = predictionLong == null ? 0 : predictionLong.Score;
                            for (int i = 0; i < result.Count; i++)
                            {
                                if (result[i].Tag == "SHORT")
                                    rankShort = i + 1;

                                if (result[i].Tag == "LONG")
                                    rankLong = i + 1;
                            }

                            if (rankLong == 1 && scoreLong >= AssetParams.Score)
                            {
                                currentTrend = "LONG";
                                
                                if (rankShort == 2 || rankShort == 1) //Neste caso temos LONG com score para entrar, mas temos Short maior que Long, ou maior que o ND.
                                {
                                    TelegramBot.SendMessageBuffered("AVISO", "Double Conflict LONG & SHORT");
                                }
                                else
                                {
                                    //THE ESSENSE OF THIS PROCEDURE
                                    _candleThatPredicted = recentClosedCandle;
                                    _manager.Signal(predictions.Result.FirstOrDefault().GetTrend());
                                }
                            }

                            if (currentTrend != "ZERO")
                                TelegramBot.SendImage(_lastImg, "Trend: " + currentTrend);
                        }
                    }
                    else
                    {
                        TraceAndLog.StaticLog(_myName, "Timeout waiting on a prediction!!!");
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
            }
            catch (Exception err)
            {
                TraceAndLog.GetInstance().Log(_myName, "Error invoking KlineObserver - " + err.Message);
            }
        }

        private void OnCandleUpdate(string id, string message, Candle cc)
        {
            _lastCandle = cc;
            _lastPrice = cc.CloseValue;

            try
            {
                if (_candleThatPredicted != null && (_params.IsTesting || cc.OpenTime > _candleThatPredicted.OpenTime)) //Delayed Trigger buffer
                {
                    var currentCandle = cc;

                    bool delayedTriggerCheck = CheckDelayedTrigger(_candleThatPredicted, currentCandle);

                    if (delayedTriggerCheck && !_operationMap.ContainsKey(_candleThatPredicted.GetCompareStamp()))
                    {
                        try
                        {
                            var candlesToDraw = _candleMovieRoll.GetList();
                            DateRange range = new DateRange(candlesToDraw.First().PointInTime_Open, candlesToDraw.Last().PointInTime_Open);
                            double atr = GetCurrentAtr(range);

                            _lastAtr = atr;

                            var prediction = _lastPrediction.FirstOrDefault();

                            var op = _manager.SignalEnterAsync(
                                cc.CloseValue,
                                prediction.RatioLowerBound,
                                prediction.RatioUpperBound,
                                cc.OpenTime,
                                prediction.DateRange.End,
                                atr
                                );

                            _operationMap[_candleThatPredicted.GetCompareStamp()] = op;

                            op.Wait(500);
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
                    currentCandle.CandleAge.TotalMinutes > 4 && currentCandle.GetIndecisionThreshold() > 0.3)
                    ret = true;
                else if (candleThatPredicted.Direction == CandleDirection.Up)
                    ret = true;
            }
            return ret;
        }

        public string GetParametersReport()
        {
            string configs = $"<b>== {this.Asset.ToUpper()} - {this._candleType.ToString()} = {_params.WindowSize.ToString()} W ==</b>\n";
            configs += $"Score: {this.AssetParams.Score.ToString("0.00")}%\n";
            configs += $"Fund: ${this._manager.Funds:0.0000}\n";
            configs += $"{_params.CardWidth} x {_params.CardHeight}\n";
            configs += $"Delay triggger {(_params.DelayedTriggerEnabled ? "ON" : "OFF")}";

            return configs;
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

                var currentOperation = _manager.GetOneActiveOperation();
                if (currentOperation != null)
                {
                    List<VolumeIndicator> newVolumes = volumes.ToList();

                    var operation = currentOperation;
                    var predictionSerie = new Serie();
                    var forecastPoint = new TradeOperationCandle()
                    {

                        AmountValue = operation.PriceEntry,
                        LowerBound = operation.GetAbsolutLowerBound(),
                        UpperBound = operation.GetAbsolutUpperBound(),
                        Gain = operation.GetGain(cc.CloseValue),
                        ExitValue = operation.ExitValue,
                        StopLossMark = operation.StopLossMark,
                        SoftStopMark = operation.SoftStopLossMarker,
                        Volume = 1,
                        State = operation.State.ToString(),
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
                TraceAndLog.StaticLog("NewInfo",err.ToString());
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

            if (_lastPrediction != null)
            {
                _lastPrediction.ForEach(p =>
                {
                    sb.AppendFormat("{0} : {1:0.000}%\n", p.Tag, p.Score);
                });
            }

            sb.AppendLine();

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

        public async Task<TradeOperation> CloseOperationIfAny()
        {
            TradeOperation op = _manager.GetOneActiveOperation();
            if(op != null)
                await op.CloseOperation(); 

            return op;
        }


        internal string GetState()
        {
            var op = _manager.GetOneActiveOperation();

            return (op != null ? op.ToString() : $"{this.GetIdentifier()} has no operation");
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

        private List<Candle> GetCachedCandles()
        {
            List<Candle> ret = new List<Candle>();

            if (_params.PreloadCandles)
            {
                var periods = _params.WindowSize + 200;//200 é a maior média móvel
                var begin = DateTime.UtcNow.AddMinutes(periods * Convert.ToInt32(_candleType) * -1);

                var asset = this.Asset.Replace("USDT", "BUSD");

                var lastCandles = CandlesGateway.MapCandles(
                    _params.DbConStringCandles,
                    asset,
                    this._candleType,
                    CandleType.MIN15,
                    new DateRange(begin, DateTime.UtcNow)
                );

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
}