using System.Net;
using System.IO;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using Midas.Core.Common;
using Midas.Core.Indicators;
using System.Reflection;
using System.Drawing;
using Midas.Core.Chart;
using Midas.Core.Trade;
using Midas.Core.Services;

namespace Midas.Core
{
    public class RunParameters
    {

        private static RunParameters _singleParams;
        private static object _lockSinc = new object();

        public static RunParameters CreateInstace(string[] ps)
        {
            if (_singleParams == null)
            {
                lock (_lockSinc)
                {
                    if (_singleParams == null)
                        _singleParams = new RunParameters(ps);
                }
            }

            return _singleParams;
        }

        public static RunParameters GetInstance()
        {
            return _singleParams;
        }
        public CandleType CandleType
        {
            get;
            private set;
        }

        public DateRange Range
        {
            get;
            private set;
        }

        public string Asset
        {
            get;
            private set;
        }

        public int WindowSize
        {
            get;
            private set;
        }

        public void WriteToConsole()
        {
            if (RunMode == RunModeType.Predict || RunMode == RunModeType.Create || RunMode == RunModeType.Label)
            {
                Console.WriteLine("Run Configuration as follows:");
                Console.WriteLine("{0,-20}: {1}", "RunMode", this.RunMode.ToString());
                Console.WriteLine("{0,-20}: {1}", "Expirement", this.ExperimentName.ToString());
                Console.WriteLine("{0,-20}: {1}", "CandleType", this.CandleType.ToString());
                Console.WriteLine("{0,-20}: {1}", "Range Start", this.Range.Start);
                Console.WriteLine("{0,-20}: {1}", "Range End", this.Range.End);
                Console.WriteLine("{0,-20}: {1}", "Asset", this.Asset.ToString());
                Console.WriteLine("{0,-20}: {1}", "WindowSize", this.WindowSize.ToString());
                Console.WriteLine("{0,-20}: {1}", "Forecast Window", String.Join(',', _forecastWindow));
                Console.WriteLine("{0,-20}: {1}", "Predict Average", AverageToForecast);
                Console.WriteLine("");
            }

            if (RunMode == RunModeType.LiveStream)
            {
                Console.WriteLine("Run Configuration as follows:");
                Console.WriteLine("{0,-20}: {1}", "RunMode", this.RunMode.ToString());
                Console.WriteLine("{0,-20}: {1}", "CandleType", this.CandleType.ToString());
                Console.WriteLine("{0,-20}: {1}", "Asset", this.Asset.ToString());
                Console.WriteLine("{0,-20}: {1}", "WindowSize", this.WindowSize.ToString());
                Console.WriteLine("");
                Console.WriteLine("Stream Config:");
                Console.WriteLine("{0,-20}: {1}", "URL", this.LiveStreamSiteUrl);
                Console.WriteLine("{0,-20}: {1}", "Key", this.LiveStreamKey);
                Console.WriteLine("{0,-20}: {1}", "FFMpeg Path", this.FFmpegBasePath);
            }

            if (RunMode == RunModeType.Gather)
            {
                Console.WriteLine("Run Configuration as follows:");
                Console.WriteLine("{0,-20}: {1}", "RunMode", this.RunMode.ToString());
                Console.WriteLine("{0,-20}: {1}", "CandleType", this.CandleType.ToString());
                Console.WriteLine("{0,-20}: {1}", "Asset", this.Asset.ToString());
                Console.WriteLine("{0,-20}: {1}", "DB Connection", this._dbConString);
                Console.WriteLine("");
            }

            if (RunMode == RunModeType.Invest)
            {
                Console.WriteLine("Run Configuration as follows:");
                Console.WriteLine("{0,-20}: {1}", "RunMode", this.RunMode.ToString());
                Console.WriteLine("{0,-20}: {1}", "Experiment", this.ExperimentName);
                Console.WriteLine("{0,-20}: {1}", "WindowSize", this.WindowSize.ToString());
                Console.WriteLine("{0,-20}: {1}", "ImgWidth", this.CardWidth);
                Console.WriteLine("{0,-20}: {1}", "ImgHeight", this.CardHeight);
                Console.WriteLine("{0,-20}: {1}", "DelayedTrigger", this.DelayedTriggerEnabled);
                Console.WriteLine("");
                Console.WriteLine("{0,-20}: {1}", "Model Avg", this.UrlAvgModel);
                Console.WriteLine("{0,-20}: {1}", "Model Price", this.UrlPriceModel);
            }
        }

        public int ForecastWindow
        {
            get
            {
                return _forecastWindow;
            }
        }

        public int MaxForecastWindow
        {
            get
            {
                return _forecastWindow;
            }
        }

        public string ExperimentName
        {
            get;
            private set;
        }
        public bool PreloadCandles { get; private set; }
        public string OutputDirectory
        {
            get;
            private set;
        }

        public string FFmpegBasePath
        {
            get;
            private set;
        }


        public string LiveStreamSiteUrl
        {
            get;
            private set;
        }

        public string LiveStreamKey
        {
            get;
            private set;
        }

        public int CardWidth
        {
            get;
            private set;
        }

        public int CardHeight
        {
            get;
            private set;
        }

        public bool IsTesting
        {
            get;
            private set;
        }

        public List<CalculatedIndicator> Indicators
        {
            get;
            private set;
        }

        public dynamic BrokerParameters
        {
            get;
            set;
        }

        public double StopLoss
        {
            get;
            set;
        }

        public string AverageVerification
        {
            get;
            set;
        }
        public string BrokerName
        {
            get;
            set;
        }

        public int AllowedConsecutivePredictions
        {
            get;
            set;
        }
        public RunModeType RunMode
        {
            get;
            private set;
        }
        public string DbConString { get => _dbConString; }

        public string OutputFile
        {
            get;
            set;
        }
        public string FundName { get; set; }

        public string OutputFileResults { get; set; }
        public bool DelayedTriggerEnabled { get; internal set; }
        public double IndecisionThreshold { get; private set; }
        public string TagFilter { get; internal set; }

        public string DbConStringCandles { get => _dbConStringCandles; }
        public string AverageToForecast { get; set; }
        public string CustomSearchKey { get; private set; }
        public string TelegramBotCode { get; private set; }
        public string Forecaster { get; internal set; }
        public string FeedStreamType { get; internal set; }
        public string UrlAvgModel { get; internal set; }
        public string UrlPriceModel { get; internal set; }
        public bool DrawShadow { get; private set; }
        public double MIN_PEEK_STRENGH { get; internal set; }
        public Dictionary<string, object> HyperParams { get => _hyperParams; set => _hyperParams = value; }
        public bool EnableLimitOrders { get; private set; }

        private string _dbConString;
        private string _dbConStringCandles;
        private int _forecastWindow;

        private Dictionary<string, object> _hyperParams;

        private dynamic _rootIndicators;

        public override string ToString()
        {
            string ret = "";

            ret = $"Start:{this.Range.Start.ToString("dd/MM/yyyy")} End:{this.Range.End.ToString("dd/MM/yyyy")}\n";

            foreach (var entry in _hyperParams)
            {
                ret += $"{entry.Key,20}:{entry.Value.ToString()} \n";
            }

            return ret;
        }

        public object GetHyperParam(string name)
        {
            return GetHyperParam(null, name);
        }
        public object GetHyperParam(string modelName, string name)
        {
            return GetHyperParam(null, modelName, name);
        }
        public object GetHyperParam(string asset, string modelName, string name)
        {
            string paramName;
            object outValue = null;
            if (asset != null && modelName != null)
            {
                paramName = $"{asset}-{modelName}-{name}";
                _hyperParams.TryGetValue(paramName, out outValue);
            }

            if (outValue == null)
            {
                if (modelName != null)
                {
                    paramName = $"{modelName}-{name}";
                    _hyperParams.TryGetValue(name, out outValue);
                }
            }

            if (outValue == null)
            {
                paramName = $"{name}";
                _hyperParams.TryGetValue(name, out outValue);
            }            

            return outValue;
        }

        public string GetOptionalHyperParam(string name)
        {
            return GetOptionalHyperParam(null, null, name);
        }
        public string GetOptionalHyperParam(string modelName, string name)
        {
            return GetOptionalHyperParam(modelName, name);
        }
        public string GetOptionalHyperParam(string asset, string modelName, string name)
        {
            string paramName = name;

            object outValue = GetHyperParam(asset, modelName, name);

            return (outValue == null ? null : outValue.ToString());
        }

        public string GetHyperParamAsString(string name)
        {
            return Convert.ToString(GetOptionalHyperParam(null, name));
        }

        public string GetHyperParamAsString(string modelname, string name)
        {
            return Convert.ToString(GetOptionalHyperParam(modelname, name));
        }
        public string GetHyperParamAsString(string asset, string modelname, string name)
        {
            return Convert.ToString(GetOptionalHyperParam(asset, modelname, name));
        }

        public int GetHyperParamAsInt(string name)
        {
            return Convert.ToInt32(GetHyperParam(null, name));
        }

        public int GetHyperParamAsInt(string modelName, string name)
        {
            return Convert.ToInt32(GetHyperParam(modelName, name));
        }
        public int GetHyperParamAsInt(string asset, string modelName, string name)
        {
            return Convert.ToInt32(GetHyperParam(asset, modelName, name));
        }

        public double GetHyperParamAsDouble(string name)
        {
            return Convert.ToDouble(GetHyperParam(null, name));
        }

        public double GetHyperParamAsDouble(string modelName, string name)
        {
            return Convert.ToDouble(GetHyperParam(modelName, name));
        }

        public double GetHyperParamAsDouble(string asset, string modelName, string name)
        {
            object ret = GetHyperParam(asset, modelName, name);
            if(ret == null)
                throw new ArgumentException($"Param not found {asset}-{modelName}-{name}");

            return Convert.ToDouble(ret);
        }

        public float GetHyperParamAsFloat(string name)
        {
            return Convert.ToSingle(GetHyperParam(null, name));
        }

        public float GetHyperParamAsFloat(string modelName, string name)
        {
            return Convert.ToSingle(GetHyperParam(modelName, name));
        }

        public bool GetHyperParamAsBoolean(string name)
        {
            return Convert.ToBoolean(GetHyperParam(null, name));
        }

        public bool GetHyperParamAsBoolean(string modelName, string name)
        {
            return Convert.ToBoolean(GetHyperParam(modelName, name));
        }
        public bool GetHyperParamAsBoolean(string asset, string modelName, string name)
        {
            return Convert.ToBoolean(GetHyperParam(asset, modelName, name));
        }

        private dynamic _rootConfig;

        public RunParameters(string[] ps)
        {
            string configFilePath = null;
            configFilePath = ps[0];
            MIN_PEEK_STRENGH = 0.1;

            string timeStamp = DateTime.Now.ToString("yyyyMMddHHmmss");

            OutputFile = "tabledResults.csv";
            DelayedTriggerEnabled = true;
            IndecisionThreshold = 0.4;

            HyperParams = new Dictionary<string, object>();

            TelegramBotCode = "1817976920:AAFwSV3rRDq2Cd8TGKwGRGoNhnHt4seJfU4";

            CandleType = CandleType.MIN5;
            DateTime start = DateTime.MinValue;
            DateTime end = DateTime.MinValue;

            Console.WriteLine($"Reading config file {configFilePath}");

            string configuration = File.ReadAllText(configFilePath);

            dynamic stuff = JsonConvert.DeserializeObject(configuration);
            _rootConfig = stuff;
            if (stuff.RunMode == null)
            {
                throw new ArgumentException("Please provide a RunMode in the config file");
            }
            else
            {
                RunMode = (RunModeType)Enum.Parse(typeof(RunModeType), stuff.RunMode.ToString());

            }

            _dbConString = Convert.ToString(stuff.ConString);
            _dbConStringCandles = Convert.ToString(stuff.ConStringCandles);
            Forecaster = Convert.ToString(stuff.Forecaster);
            FeedStreamType = Convert.ToString(stuff.FeedStreamType);
            BrokerName = Convert.ToString(stuff.BrokerName);
            AverageToForecast = Convert.ToString(stuff.AverageToForecast);
            CustomSearchKey = Convert.ToString(stuff.CustomSearchKey);

            CandleType = (CandleType)Enum.Parse(typeof(CandleType), stuff.CandleType.ToString(), true);
            start = Convert.ToDateTime(stuff.StartDate);
            end = Convert.ToDateTime(stuff.EndDate);
            Asset = Convert.ToString(stuff.Asset);
            ExperimentName = Convert.ToString(stuff.ExperimentName);
            PreloadCandles = Convert.ToBoolean(stuff.PreloadCandles);

            UrlPriceModel = Convert.ToString(stuff.UrlPriceModel);
            UrlAvgModel = Convert.ToString(stuff.UrlAvgModel);

            FundName = Convert.ToString(stuff.FundName);
            BrokerParameters = stuff.BrokerParameters;

            FFmpegBasePath = Convert.ToString(stuff.FFmpegBasePath);
            LiveStreamSiteUrl = Convert.ToString(stuff.LiveStreamSiteUrl);
            LiveStreamKey = Convert.ToString(stuff.LiveStreamKey);

            if (stuff.ForecastWindow != null)
            {
                _forecastWindow = Convert.ToInt32(stuff.ForecastWindow);
            }

            WindowSize = Convert.ToInt32(stuff.WindowSize);
            OutputDirectory = Convert.ToString(stuff.OutputDirectory);
            CardWidth = Convert.ToInt32(stuff.CardWidth);
            CardHeight = Convert.ToInt32(stuff.CardHeight);

            if (stuff.DelayedTriggerEnabled != null)
                DelayedTriggerEnabled = Convert.ToBoolean(stuff.DelayedTriggerEnabled);

            DrawShadow = true;
            if (stuff.DrawShadow != null)
                DrawShadow = Convert.ToBoolean(stuff.DrawShadow);

            IsTesting = false;
            if (stuff.IsTesting != null)
                IsTesting = Convert.ToBoolean(stuff.IsTesting);

            EnableLimitOrders = false;
            if (stuff.EnableLimitOrders != null)
                EnableLimitOrders = Convert.ToBoolean(stuff.EnableLimitOrders);

            Range = new DateRange(start, end);

            if (stuff.StopLoss != null)
                StopLoss = Convert.ToDouble(stuff.StopLoss);
            if (stuff.AverageVerification != null)
                AverageVerification = Convert.ToString(stuff.AverageVerification);
            if (stuff.AllowedConsecutivePredictions != null)
                AllowedConsecutivePredictions = Convert.ToInt32(stuff.AllowedConsecutivePredictions);

            if (stuff.OutputFileResults != null)
                OutputFileResults = Convert.ToString(stuff.OutputFileResults);

            if (stuff.RunMode != null)
                RunMode = (RunModeType)Enum.Parse(typeof(RunModeType), stuff.RunMode.ToString());

            TelegramBotCode = Convert.ToString(stuff.TelegramBotCode);

            if (stuff.Indicators != null)
            {
                _rootIndicators = stuff.Indicators;
                Indicators = GetIndicators();
            }

            if (stuff.Assets != null)
                _rootAssets = stuff.Assets;


            if (RunMode == RunModeType.Predict || RunMode == RunModeType.Create)
            {
                if (CardWidth == 0 || CardHeight == 0 ||
                String.IsNullOrEmpty(ExperimentName) || end == DateTime.MinValue || start == DateTime.MinValue || _forecastWindow == 0 || WindowSize == 0)
                {
                    throw new ArgumentException("Missing arguments for RunMode Create or Predict");
                }
            }

            if (RunMode == RunModeType.LiveStream)
            {
                if (String.IsNullOrEmpty(Asset) || WindowSize == 0 || FFmpegBasePath == null)
                    throw new ArgumentException("Missing arguments for RunMode LiveStream");
            }

            if (RunMode == RunModeType.Gather)
            {
                if (String.IsNullOrEmpty(Asset) || String.IsNullOrEmpty(_dbConString))
                    throw new ArgumentException("Missing arguments for RunMode Gather");
            }

            AllowedConsecutivePredictions = 1;

            var hyperParams = stuff.HyperParams;
            if (hyperParams != null)
            {
                foreach (var hyperParam in hyperParams)
                {
                    if (hyperParam.Value.Type.ToString() == "Array")
                        HyperParams[hyperParam.Name] = hyperParam.Value;
                    else
                        HyperParams[hyperParam.Name] = hyperParam.Value.Value;
                }
            }

            if (ps.Length > 1)
            {
                if (RunMode == RunModeType.Invest)
                {
                    if (ps.Length > 1)
                    {
                        ExperimentName = ps[1];
                        Range.Start = DateTime.ParseExact(ps[2], "yyyyMMdd", null);
                        Range.End = DateTime.ParseExact(ps[3], "yyyyMMdd", null);
                        Range.End = Range.End.AddHours(23);
                        Range.End = Range.End.AddMinutes(59);
                    }

                    //dotnet run -- runnerConfig.json 50 run_CandleFocus_OperationControl 20210725 20210729 None -0.5 6 false 20 1 1 1 LONG0102;

                }
                else if (RunMode == RunModeType.Create || RunMode == RunModeType.Predict || RunMode == RunModeType.Label)
                {
                    ExperimentName = ps[1];
                    Asset = ps[2];
                    OutputFile = ps[3];
                }


                int counter = 1;
                foreach (string param in ps)
                {
                    var paramSplit = param.Split(":");
                    string paramVoid = $"param{counter}";

                    if (paramSplit.Length == 2)
                    {
                        HyperParams[paramSplit[0]] = paramSplit[1];
                    }
                    else
                    {
                        HyperParams[paramVoid] = paramSplit[0];
                    }
                    counter++;
                }
            }

            ExperimentName = timeStamp + "_" + ExperimentName;
        }

        dynamic _rootAssets = null;

        public Dictionary<string, AssetTrader> GetAssetTraders(InvestorService service)
        {
            var traders = new Dictionary<string, AssetTrader>();
            if (_rootAssets != null)
            {
                foreach (var metaAsset in _rootAssets)
                {
                    string asset = Convert.ToString(metaAsset.Asset);
                    CandleType candleType = (CandleType)Enum.Parse(typeof(CandleType), Convert.ToString(metaAsset.CandleType));
                    float scoreByAvg = Convert.ToSingle(metaAsset.ScoreThresholdByAvg);
                    float scoreByPrice = Convert.ToSingle(metaAsset.ScoreThresholdByPrice);
                    string fundName = Convert.ToString(metaAsset.FundName);

                    var assetParams = new AssetParameters();
                    assetParams.FundName = fundName;

                    var trader = new AssetTrader(service, asset, candleType, this, 120000, assetParams);

                    traders.Add(asset + ":" + candleType.ToString(), trader);
                }
            }

            return traders;
        }

        internal List<CalculatedIndicator> GetIndicators()
        {
            var rootArray = _rootIndicators;

            List<CalculatedIndicator> indicators = new List<CalculatedIndicator>();
            if (rootArray != null)
            {
                foreach (var metaIndicator in rootArray)
                {
                    var args = new List<string>();
                    foreach (string arg in metaIndicator.Params)
                        args.Add(arg);

                    Assembly a = Assembly.Load(Convert.ToString(metaIndicator.AssemblyName));

                    //Here we multiply the number because if the number is exactly the size of the window, due to the future buffer, the candles would start do disapper
                    //If Window=30 and Future=15 and if he had set windowsize=30 there will always be a 15 diference.
                    args.Add((this.WindowSize * 10).ToString());
                    CalculatedIndicator newInd = (CalculatedIndicator)a.CreateInstance(
                        metaIndicator.FullClassName.ToString(),
                        true,
                        new BindingFlags(),
                        null,
                        (object[])args.ToArray(),
                        null,
                        null
                    );

                    if (newInd == null)
                        throw new ArgumentException("Erro when loading indicador - " + metaIndicator.Name.ToString());

                    newInd.Source = Convert.ToString(metaIndicator.Source);
                    if (metaIndicator.Target == null)
                        newInd.Target = newInd.Source;
                    else
                        newInd.Target = Convert.ToString(metaIndicator.Target);

                    newInd.Name = Convert.ToString(metaIndicator.Name);
                    newInd.Color = Color.FromName(Convert.ToString(metaIndicator.Color));
                    newInd.Size = Convert.ToInt32(metaIndicator.Size);
                    newInd.Type = (SeriesType)Enum.Parse(typeof(SeriesType), Convert.ToString(metaIndicator.ChartType));

                    if (metaIndicator.IncludeInPrediction != null &&
                        Convert.ToBoolean(metaIndicator.IncludeInPrediction))
                        newInd.IncludeInPrediction = true;

                    indicators.Add(newInd);
                }
            }

            return indicators;
        }

        public string GetDirectoryName()
        {
            DateTime baseDate = new DateTime(2021, 02, 24);
            int runNumber = Convert.ToInt32((DateTime.Now - baseDate).TotalSeconds);

            //return String.Format("{0} {1} {2} {3:yyyy-MM-dd_HH-mm} {4:yyyy-MM-dd_HH-mm} {5} {6} {7}",
            return String.Format("{0}",
                ExperimentName
            );
        }
    }

    public enum RunModeType
    {
        Predict,
        Create,

        LiveStream,

        Invest,

        Gather,
        Label
    }

    public class AssetParameters
    {
        public string FundName { get; internal set; }

    }
}