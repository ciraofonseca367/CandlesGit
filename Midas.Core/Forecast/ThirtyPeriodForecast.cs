using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Midas.Core.Common;
using Midas.Core.Util;
using Midas.Util;
using Newtonsoft.Json;

namespace Midas.Core.Forecast
{
    public class ThirtyPeriodForecast : IForecast
    {

        private string _predictionServer = "http://vps32867.publiccloud.com.br";
        private HttpClient _httpClient;
        public ThirtyPeriodForecast(string predictionServer)
        {
            _predictionServer = predictionServer;
            _httpClient = new HttpClient();

            _httpClient.BaseAddress = new Uri(_predictionServer);
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "ya29.c.Kp8BBQgiwKQ8DKdsgDT-Hu0oQe53uz9WFI3ZchggizJpeVRuvIj5CGNNhpPZgJaW7fklPqM7Zwr2yTem7H2uSm2XGsn63fTtgnYfPnk55hCVubfXGkykmHQ6J8wC6LOZE9I1EbVg7qXOp8ey5DFbNBXrHiOBrohm7MZ0Q646hIxszo1ouegyqgPXskitBxdqzLzZys-k-YquFqlKOLxeP0qJ");
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        private string TransformImageToBase64(Bitmap image)
        {
            MemoryStream ms = new MemoryStream();
            image.Save(ms, System.Drawing.Imaging.ImageFormat.Gif);
            ms.Flush();
            ms.Position = 0;

            return Convert.ToBase64String(ms.ToArray());
        }

        public async Task<List<PredictionResult>> PredictAsync(Bitmap image, string asset, float scoreThreshold, double currentValue, DateTime currentTime)
        {
            List<PredictionResult> previewTags = null;
            var payload = TransformImageToBase64(image);

            dynamic request = new
            {
                instances = new[]
                {
                        new {
                            image_bytes = new
                            {
                                b64 = payload
                            },
                            key = "1"
                        }
                    }
            };

            string url = "v1/models/default:predict";
            var res = await _httpClient.PostAsync(url, new StringContent(JsonConvert.SerializeObject(request)));

            var jsonResponse = await res.Content.ReadAsStringAsync();

            //TraceAndLog.GetInstance().LogTraceHttpAction("RestPredictionClient", "POST", _httpClient.DefaultRequestHeaders, res.Headers, url, jsonResponse);

            dynamic parsedResponse = JsonConvert.DeserializeObject(jsonResponse);

            previewTags = new List<PredictionResult>();

            if (parsedResponse.predictions.Count > 0)
            {

                var labels = parsedResponse.predictions[0].labels;
                var scores = parsedResponse.predictions[0].scores;

                for (int i = 0; i < labels.Count; i++)
                {
                    try
                    {
                        string label = Convert.ToString(labels[i]);
                        if (Convert.ToDouble(scores[i]) >= scoreThreshold)
                        {
                            var prediction = new PredictionResult();
                            prediction.CreationDate = currentTime;
                            prediction.FromAmount = currentValue;
                            prediction.Score = Convert.ToSingle(scores[i]);
                            prediction.Tag = label;
                            prediction.DateRange = new DateRange(currentTime, currentTime.AddMinutes(24 * 5));
                            if (label.StartsWith("LONG"))
                            {
                                if (label.Length == 4)
                                {
                                    prediction.RatioLowerBound = 0.75 / 100;
                                    prediction.RatioUpperBound = 1.25 / 100;
                                }
                                else
                                {
                                    prediction.RatioLowerBound = Convert.ToDouble(label.Substring(4, 2)) / 100;
                                    prediction.RatioUpperBound = Convert.ToDouble(label.Substring(6, 2)) / 100;
                                }
                            }
                            else if (label.StartsWith("SHORT"))
                            {
                                if (label.Length == 5)
                                {
                                    prediction.RatioLowerBound = -0.5 / 100;
                                    prediction.RatioUpperBound = 1 / 100;

                                }
                                else
                                {
                                    prediction.RatioLowerBound = Convert.ToDouble(label.Substring(5, 2)) / 100 * -1;
                                    prediction.RatioUpperBound = Convert.ToDouble(label.Substring(7, 2)) / 100 * -1;
                                }
                            }
                            else if (label == "ZERO")
                            {
                                prediction.RatioLowerBound = -0.02;
                                prediction.RatioUpperBound = +0.02;
                            }
                            else
                            {
                                prediction.RatioLowerBound = 0;
                                prediction.RatioUpperBound = 0;
                            }

                            prediction.LowerBound = currentValue * (1 + prediction.RatioLowerBound);
                            prediction.UpperBound = currentValue * (1 + prediction.RatioUpperBound);

                            previewTags.Add(prediction);

                        }
                    }
                    catch (Exception err)
                    {
                        Console.WriteLine("ERRRO: " + err.ToString());
                    }
                }
            }

            var tags = new List<PredictionResult>();

            if (previewTags.Count > 0)
                tags.AddRange(previewTags.OrderByDescending(t => t.Score));

            return tags;
        }

        public List<PredictionResult> Predict(Bitmap image, string asset,float scoreThreshold, double currentValue, DateTime currentTime)
        {
            var t = this.PredictAsync(image, asset, scoreThreshold, currentValue, currentTime);

            if (t.Wait(20000))
                return t.Result;
            else
            {
                throw new TimeoutException("Error while waiting prediction");
            }
        }

        public Prediction GetPrediction(Bitmap image, string asset, double currentValue, DateTime currentTime)
        {
            Prediction predictionResult = null;

            var predictions = PredictAsync(image, asset, 0.1f, currentValue, currentTime);
            if (predictions.Wait(60000))
            {
                predictionResult = Prediction.ParseRawResult(predictions.Result);
            }
            else
            {
                throw new ApplicationException("Timeout waiting on a prediction!!!" + _predictionServer);
            }

            return predictionResult;
        }
    }

    public class Prediction
    {
        public string CurrentTrend { get; internal set; }
        public PredictionResult Long { get; internal set; }
        public PredictionResult Short { get; internal set; }
        public PredictionResult Zero { get; internal set; }
        public int RankShort { get; internal set; }
        public int RankLong { get; internal set; }
        public int RankZero { get; internal set; }

        public double ScoreShort
        {
            get
            {
                return Short == null ? 0 : Short.Score;
            }
        }

        public double ScoreLong
        {
            get
            {
                return Long == null ? 0 : Long.Score;
            }
        }

        public double ScoreZero
        {
            get
            {
                return Zero == null ? 0 : Zero.Score;
            }
        }

        public Candle CandleThatPredicted { get; internal set; }
        public List<PredictionResult> AllPredictions { get; internal set; }

        public bool Go(float scoreThreshold)
        {
            bool ret = false;

            if (RankLong == 1 && ScoreLong >= scoreThreshold)
                ret = true;

            return ret;
        }

        public string GetTrend()
        {
            return null;
        }

        public static Prediction ParseRawResult(List<PredictionResult> result)
        {
            Prediction predictionResult = null;
            if (result.Count > 0)
            {
                predictionResult = new Prediction();

                // var predictionLong = result.Where(p => p.Tag == "LONG").FirstOrDefault();
                // var predictionShort = result.Where(p => p.Tag == "SHORT").FirstOrDefault();
                // var predictionZero = result.Where(p => p.Tag == "ZERO").FirstOrDefault();

                // int rankShort = 0;
                // int rankLong = 0;
                // int rankZero = 0;
                // double scoreLong = predictionLong == null ? 0 : predictionLong.Score;
                // for (int i = 0; i < result.Count; i++)
                // {
                //     if (result[i].Tag == "SHORT")
                //         rankShort = i + 1;

                //     if (result[i].Tag == "LONG")
                //         rankLong = i + 1;

                //     if (result[i].Tag == "ZERO")
                //         rankZero = i + 1;
                // }

                // predictionResult.Long = predictionLong;
                // predictionResult.Short = predictionShort;
                // predictionResult.Zero = predictionZero;

                // predictionResult.RankShort = rankShort;
                // predictionResult.RankLong = rankLong;
                // predictionResult.RankZero = rankZero;

                // predictionResult.AllPredictions = result;

                predictionResult.CurrentTrend = result[0].Tag;
            }

            return predictionResult;
        }
    }


    public class PredictionResult
    {
        public double Score
        {
            get;
            set;
        }

        public string Tag
        {
            get;
            set;
        }

        public double RatioLowerBound
        {
            get;
            set;
        }

        public double RatioUpperBound
        {
            get;
            set;
        }

        public double LowerBound
        {
            get;
            set;
        }

        public double UpperBound
        {
            get;
            set;
        }

        public DateRange DateRange
        {
            get;
            set;
        }

        public DateTime CreationDate
        {
            get;
            set;
        }

        public double FromAmount
        {
            get;
            set;
        }

        public TradeType GetTrend()
        {
            return (RatioLowerBound < 0 ? TradeType.Short : TradeType.Long);
        }

        public override string ToString()
        {
            return String.Format("{0} to {1} in the next {2} 5 minute candles, CurrentAmount: {3}", RatioLowerBound, RatioUpperBound, Convert.ToInt32(DateRange.GetSpan().TotalMinutes / 5), FromAmount);
        }

    }

}