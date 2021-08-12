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
                Console.WriteLine("");
                Console.WriteLine("{0,-20}: {1}", "Score Threshold", String.Join(',', ScoreThreshold.ToString("0.00")));
                Console.WriteLine("{0,-20}: {1}", "StopLoss ", this.StopLoss);
                Console.WriteLine("{0,-20}: {1}", "Average Verification", this.AverageVerification);
                Console.WriteLine("{0,-20}: {1}", "Tag Filter", this.TagFilter);
                Console.WriteLine("{0,-20}: {1}", "Delayed Trigger", this.DelayedTriggerEnabled);
                Console.WriteLine("{0,-20}: {1}", "Indecision Threshold", this.IndecisionThreshold);
                Console.WriteLine("");
                Console.WriteLine("{0,-20}: {1}", "Target 1", this.Target1);
                Console.WriteLine("{0,-20}: {1}", "Target 2", this.Target2);
                Console.WriteLine("{0,-20}: {1}", "Target 3", this.Target3);
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
        public string BotToken { get; internal set; }
        public string AverageToForecast { get; set; }

        private string _dbConString;
        private string _dbConStringCandles;
        private int _forecastWindow;

        public RunParameters(string[] ps)
        {
            if (ps.Length == 0)
                throw new ArgumentException("Give me the name of the config file man!!!");

            OutputFile = "tabledResults.csv";
            DelayedTriggerEnabled = true;
            IndecisionThreshold = 0.4;

            BotToken = "1817976920:AAFwSV3rRDq2Cd8TGKwGRGoNhnHt4seJfU4";

            AverageToForecast = "MA6";

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

            CandleType = (CandleType)Enum.Parse(typeof(CandleType), stuff.CandleType.ToString(), true);
            start = Convert.ToDateTime(stuff.StartDate);
            end = Convert.ToDateTime(stuff.EndDate);
            Asset = Convert.ToString(stuff.Asset);
            ExperimentName = Convert.ToString(stuff.ExperimentName);

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

            if(stuff.Indicators != null)
                ParseIndicators(stuff.Indicators);

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

            if (RunMode == RunModeType.Invest)
            {
                if (String.IsNullOrEmpty(Asset) || String.IsNullOrEmpty(_dbConString) || String.IsNullOrEmpty(FundName) || BrokerParameters == null)
                    throw new ArgumentException("Missing arguments for RunMode Invest");
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
                AverageVerification = Convert.ToString(ps[5]);
                StopLoss = Convert.ToDouble(ps[6]);
                AllowedConsecutivePredictions = Convert.ToInt32(ps[7]);
                OutputFile = ExperimentName+".csv";
                if(ps.Length > 8)
                {
                    DelayedTriggerEnabled = Convert.ToBoolean(ps[8]);
                    IndecisionThreshold = Convert.ToDouble(ps[9]) / 100;
                    Target1 = Convert.ToDouble(ps[10]);
                    Target2 = Convert.ToDouble(ps[11]);
                    Target3 = Convert.ToDouble(ps[12]);
                    if(ps.Length > 13)
                        TagFilter = Convert.ToString(ps[13]);
                }

                //dotnet run -- runnerConfig.json 50 run_CandleFocus_OperationControl 20210725 20210729 None -0.5 6 false 20 1 1 1 LONG0102;
            }
        }

        private void ParseIndicators(dynamic rootArray)
        {
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

            Indicators = indicators;
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
}