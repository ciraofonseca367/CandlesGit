using System;
using System.Net.Http;
using System.Net;
using System.Threading.Tasks;
using System.Drawing;
using System.IO;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Net.Http.Headers;
using Midas.Core.Common;
using System.Linq;
using Midas.Core.Util;

namespace Midas.Util
{

    public class RestPredictionClient
    {
        private static bool DEBUG_firstcall = true;

        private const string predictionServer = "http://vps32867.publiccloud.com.br";
        private HttpClient _httpClient;
        public RestPredictionClient()
        {
            _httpClient = new HttpClient();

            _httpClient.BaseAddress = new Uri(predictionServer);
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "ya29.c.Kp8BBQgiwKQ8DKdsgDT-Hu0oQe53uz9WFI3ZchggizJpeVRuvIj5CGNNhpPZgJaW7fklPqM7Zwr2yTem7H2uSm2XGsn63fTtgnYfPnk55hCVubfXGkykmHQ6J8wC6LOZE9I1EbVg7qXOp8ey5DFbNBXrHiOBrohm7MZ0Q646hIxszo1ouegyqgPXskitBxdqzLzZys-k-YquFqlKOLxeP0qJ");
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public async Task<List<PredictionResult>> PredictAsync(Bitmap image, float scoreThreshold, double currentValue, DateTime currentTime, string filter = null)
        {
            List<PredictionResult> previewTags = null;
            if (scoreThreshold > 0)
            {
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

                TraceAndLog.GetInstance().LogTraceHttpAction("RestPredictionClient", "POST", _httpClient.DefaultRequestHeaders, res.Headers, url, jsonResponse);

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
                            if (Convert.ToDouble(scores[i]) >= scoreThreshold && label != "ND")
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
                                        prediction.RatioLowerBound = 0.75/100;
                                        prediction.RatioUpperBound = 1.25/100;
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
            }
            else
            {
                previewTags = new List<PredictionResult>();
                if (RestPredictionClient.DEBUG_firstcall) //In the debug mode we force the first prediction the rest will retorn blank
                {
                    previewTags.Add(new PredictionResult()
                    {
                        Tag = "LONG",
                        FromAmount = currentValue,
                        Score = 0.75f,
                        CreationDate = DateTime.Now,
                        DateRange = new DateRange(currentTime, currentTime.AddMinutes((1 * 5))),
                        RatioLowerBound = 0.005f,
                        RatioUpperBound = 0.01f,
                        LowerBound = currentValue * (1 + 0.005),
                        UpperBound = currentValue * (1 + 0.01)
                    });

                    RestPredictionClient.DEBUG_firstcall = false;
                }
            }

            var tags = new List<PredictionResult>();

            if (filter != null)
            {
                previewTags = previewTags.Where(t => t.Tag == filter).ToList();
            }

            if (previewTags.Count > 0)
                tags.AddRange(previewTags.OrderByDescending(t => t.Score));

            return tags;
        }

        public List<PredictionResult> Predict(Bitmap image, float scoreThreshold, string filter = null)
        {
            return Predict(image, scoreThreshold, 0, DateTime.Now, filter);
        }

        public List<PredictionResult> Predict(Bitmap image, float scoreThreshold, double currentValue, DateTime currentTime, string filter = null)
        {
            var handle = PredictAsync(image, scoreThreshold, currentValue, currentTime, filter);
            if (handle.Wait(5000))
                return handle.Result;
            else
                throw new TimeoutException("Waiting of prediction more then 10000");
        }

        private PredictionResult ParsePredictionResult(float score, string tag, double currentValue, DateTime currentDate)
        {
            PredictionResult ret = null;

            try
            {
                tag = tag.Replace("_", "-");
                string[] parts = tag.Split('-');

                int minutesAhead = (Convert.ToInt32(parts[0]) - 50) * 5;

                double lowerBound = 0, upperBound = 0;

                switch (parts[1])
                {
                    case "Minus08Plus":
                        lowerBound = -8.01;
                        upperBound = Int32.MaxValue;
                        break;
                    case "Minus05TO08":
                        lowerBound = -5.01;
                        upperBound = -8;
                        break;
                    case "Minus03TO05":
                        lowerBound = -3.01;
                        upperBound = -5;
                        break;
                    case "Minus02TO03":
                        lowerBound = -2.01;
                        upperBound = -3;
                        break;
                    case "Minus01TO02":
                        lowerBound = -1.01;
                        upperBound = -2;
                        break;
                    case "MinusHalfTO01":
                        lowerBound = -0.501;
                        upperBound = -1;
                        break;
                    case "MinusDOT1TOHalf":
                        lowerBound = -0.101;
                        upperBound = -0.5;
                        break;
                    case "PlusDOT1TOHalf":
                        lowerBound = 0.1;
                        upperBound = 0.5;
                        break;
                    case "PlusHalfTO01":
                        lowerBound = 0.501;
                        upperBound = 1;
                        break;
                    case "Plus01TO02":
                        lowerBound = 1.01;
                        upperBound = 2;
                        break;
                    case "Plus02TO03":
                        lowerBound = 2.01;
                        upperBound = 3;
                        break;
                    case "Plus03TO05":
                        lowerBound = 3.01;
                        upperBound = 5;
                        break;
                    case "Plus05TO08":
                        lowerBound = 5.01;
                        upperBound = 8;
                        break;
                    case "Plus08Plus":
                        lowerBound = 8.01;
                        upperBound = Int32.MaxValue;
                        break;
                }

                lowerBound = lowerBound / 100;
                upperBound = upperBound / 100;

                ret = new PredictionResult()
                {
                    Tag = tag,
                    FromAmount = currentValue,
                    Score = score,
                    CreationDate = DateTime.Now,
                    DateRange = new DateRange(currentDate, currentDate.AddMinutes(minutesAhead + 1)),
                    RatioLowerBound = lowerBound,
                    RatioUpperBound = upperBound,
                    LowerBound = currentValue * (1 + lowerBound),
                    UpperBound = currentValue * (1 + upperBound)
                };
            }
            catch { }

            return ret;
        }

        private string TransformImageToBase64(Bitmap image)
        {
            MemoryStream ms = new MemoryStream();
            image.Save(ms, System.Drawing.Imaging.ImageFormat.Gif);
            ms.Flush();
            ms.Position = 0;

            return Convert.ToBase64String(ms.ToArray());
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
