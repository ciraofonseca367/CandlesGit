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
    public class FaceLabelForecast : IForecast
    {

        private string _predictionServer = "http://vps32867.publiccloud.com.br";
        private HttpClient _httpClient;
        public FaceLabelForecast(string predictionServer)
        {
            _predictionServer = predictionServer;
            _httpClient = new HttpClient();

            _httpClient.BaseAddress = new Uri(_predictionServer);
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
            List<PredictionResult> previewTags = new List<PredictionResult>();
            string payload = TransformImageToBase64(image);

            string ret = null;

            try
            {

                string url = $"/predict?identifier={asset}";
                var res = _httpClient.PostAsync(url, new StringContent(payload));
                if (res.Wait(5000))
                {
                    var predictions = await res.Result.Content.ReadAsStringAsync();
                        ret = predictions;

                    var prediction = new PredictionResult()
                    {
                        Tag = predictions,
                        Score = 1,
                        RatioLowerBound = 0.75 / 100,
                        RatioUpperBound = 1 / 100
                    };

                    prediction.DateRange = new DateRange(currentTime, currentTime.AddMinutes(10 * 5));
                    prediction.CreationDate = currentTime;
                    prediction.FromAmount = currentValue;
                    prediction.LowerBound = currentValue * (1 + prediction.RatioLowerBound);
                    prediction.UpperBound = currentValue * (1 + prediction.RatioUpperBound);                           

                    previewTags.Add(prediction);
                }
                else
                    Console.WriteLine("FaceLabel - Time the fuck out!");
            }
            catch (Exception err)
            {
                Console.WriteLine($"FaceLabel prediction error: {err.Message}");
            }

            return previewTags;
        }

        public List<PredictionResult> Predict(Bitmap image, string asset, float scoreThreshold, double currentValue, DateTime currentTime)
        {
            var t = this.PredictAsync(image,asset, scoreThreshold, currentValue, currentTime);

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

            var predictions = PredictAsync(image, asset,  0.1f, currentValue, currentTime);
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
}