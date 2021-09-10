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
            if(_singleParams == null)
            {
                lock(_lockSinc)
                {
                    if(_singleParams == null)
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
            if (RunMode == RunModeType.Predict || RunMode == RunModeType.Create)
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
                Console.WriteLine("{0,-20}: {1}", "Score Threshold", String.Join(',', ScoreThreshold.ToString("0.00")));
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
                Console.WriteLine("{0,-20}: {1}", "CandleType", this.CandleType.ToString());
                Console.WriteLine("{0,-20}: {1}", "Asset", this.Asset.ToString());
                Console.WriteLine("{0,-20}: {1}", "Score", this.ScoreThreshold);
                Console.WriteLine("{0,-20}: {1}", "Testing", this.IsTesting);
                Console.WriteLine("{0,-20}: {1}", "Experiment", this.ExperimentName);
                Console.WriteLine("{0,-20}: {1}", "Range Start", this.Range.Start);
                Console.WriteLine("{0,-20}: {1}", "Range End", this.Range.End);
                Console.WriteLine("{0,-20}: {1}", "WindowSize", this.WindowSize.ToString());                
                Console.WriteLine("{0,-20}: {1}", "DelayedTrigger", this.DelayedTriggerEnabled);
                Console.WriteLine("");
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

        public float ScoreThreshold
        {
            get;
            set;
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
            get 
            {
                return ScoreThreshold < 0;
            }
        }

        public bool DrawPrediction
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
        public double Target1 { get; internal set; }
        public double Target2 { get; internal set; }
        public double Target3 { get; internal set; }

        public string TagFilter { get; internal set; }

        public string DbConStringCandles { get => _dbConStringCandles; }
        public string AverageToForecast { get; set; }
        public string TelegramBotCode { get; private set; }
        public string Forecaster { get; internal set; }
        public string FeedStreamType { get; internal set; }

        private string _dbConString;
        private string _dbConStringCandles;
        private int _forecastWindow;

        private dynamic _rootIndicators;

        public RunParameters(string[] ps)
        {
            if (ps.Length == 0)
                throw new ArgumentException("Give me the name of the config file man!!!");

            OutputFile = "tabledResults.csv";
            DelayedTriggerEnabled = true;
            IndecisionThreshold = 0.4;

            TelegramBotCode = "1817976920:AAFwSV3rRDq2Cd8TGKwGRGoNhnHt4seJfU4";

            CandleType = CandleType.MIN5;
            DateTime start = DateTime.MinValue;
            DateTime end = DateTime.MinValue;

            string configFilePath = ps[0];
            string configuration = File.ReadAllText(configFilePath);

            dynamic stuff = JsonConvert.DeserializeObject(configuration);
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

            CandleType = (CandleType)Enum.Parse(typeof(CandleType), stuff.CandleType.ToString(), true);
            start = Convert.ToDateTime(stuff.StartDate);
            end = Convert.ToDateTime(stuff.EndDate);
            Asset = Convert.ToString(stuff.Asset);
            ExperimentName = Convert.ToString(stuff.ExperimentName);
            PreloadCandles = Convert.ToBoolean(stuff.PreloadCandles);

            FundName = Convert.ToString(stuff.FundName);
            BrokerParameters = stuff.BrokerParameters;

            ScoreThreshold = Convert.ToSingle(stuff.ScoreThreshold);

            FFmpegBasePath = Convert.ToString(stuff.FFmpegBasePath);
            LiveStreamSiteUrl = Convert.ToString(stuff.LiveStreamSiteUrl);
            LiveStreamKey = Convert.ToString(stuff.LiveStreamKey);

            if (stuff.ForecastWindow != null)
            {
                _forecastWindow = Convert.ToInt32(stuff.ForecastWindow) + 3;
            }

            WindowSize = Convert.ToInt32(stuff.WindowSize);
            OutputDirectory = Convert.ToString(stuff.OutputDirectory);
            CardWidth = Convert.ToInt32(stuff.CardWidth);
            CardHeight = Convert.ToInt32(stuff.CardHeight);

            if(stuff.DelayedTriggerEnabled != null)
                DelayedTriggerEnabled = Convert.ToBoolean(stuff.DelayedTriggerEnabled);

            DrawPrediction = Convert.ToBoolean(stuff.DrawPrediction);

            Range = new DateRange(start, end);

            if(stuff.StopLoss != null)
                StopLoss = Convert.ToDouble(stuff.StopLoss);
            if(stuff.AverageVerification != null)
                AverageVerification = Convert.ToString(stuff.AverageVerification);
            if(stuff.AllowedConsecutivePredictions != null)
                AllowedConsecutivePredictions = Convert.ToInt32(stuff.AllowedConsecutivePredictions);

            if(stuff.OutputFileResults != null)
                OutputFileResults = Convert.ToString(stuff.OutputFileResults);

            if (stuff.RunMode != null)
                RunMode = (RunModeType)Enum.Parse(typeof(RunModeType), stuff.RunMode.ToString());

            TelegramBotCode = Convert.ToString(stuff.TelegramBotCode);

            if(stuff.Indicators != null)
            {
                _rootIndicators = stuff.Indicators;
                Indicators = GetIndicators();
            }

            if(stuff.Assets != null)
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
            Target1 = 1;
            Target2 = 1;
            Target3 = 1;

            if(ps.Length > 1)
            {
                ScoreThreshold = Convert.ToSingle(Convert.ToSingle(ps[1]) / 100);
                ExperimentName = ps[2];
                Range.Start = DateTime.ParseExact(ps[3], "yyyyMMdd", null);
                Range.End = DateTime.ParseExact(ps[4], "yyyyMMdd", null);
                Range.End = Range.End.AddHours(23);
                Range.End = Range.End.AddMinutes(59);
                DelayedTriggerEnabled = Convert.ToBoolean(ps[5]);

                //dotnet run -- runnerConfig.json 50 run_CandleFocus_OperationControl 20210725 20210729 None -0.5 6 false 20 1 1 1 LONG0102;
            }
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
                    CandleType candleType = (CandleType) Enum.Parse(typeof(CandleType), Convert.ToString(metaAsset.CandleType));
                    float score = Convert.ToSingle(metaAsset.ScoreThreshold);
                    string fundName = Convert.ToString(metaAsset.FundName);

                    var assetParams = new AssetParameters();
                    assetParams.Score = score;
                    assetParams.FundName = fundName;
                    assetParams.AtrStopLoss = Convert.ToSingle(metaAsset.AtrStopLoss);
                    assetParams.AvgCompSoftness = Convert.ToSingle(metaAsset.AvgCompSoftness);
                    assetParams.StopLossCompSoftness = Convert.ToSingle(metaAsset.StopLossCompSoftness);

                    var trader = new AssetTrader(service, asset, candleType, this, 120000, assetParams);

                    traders.Add(asset+":"+candleType.ToString(), trader);
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
                    {
                        args.Add(arg.Replace("$WindowSize", this.WindowSize.ToString()));
                    }

                    Assembly a = Assembly.Load(Convert.ToString(metaIndicator.AssemblyName));

                    args.Add("500");
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
                    if(metaIndicator.Target == null)
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

        Gather
    }

    public class AssetParameters
    {
        public float Score { get; internal set; }
        public string FundName { get; internal set; }
        public float AtrStopLoss { get; internal set; }
        public float AvgCompSoftness { get; internal set; }
        public float StopLossCompSoftness { get; internal set; }

        public override string ToString()
        {
            return $"Score: {Score:0.00}, SL:{AtrStopLoss:0.00}";
        }
    }
}