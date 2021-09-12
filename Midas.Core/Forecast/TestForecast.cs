using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Midas.Core.Common;

namespace Midas.Core.Forecast
{
    public class TestForecast : IForecast
    {
        public TestForecast()
        {
            Console.WriteLine("Test forecast active");
        }
        public List<PredictionResult> Predict(Bitmap image, float scoreThreshold, double currentValue, DateTime currentTime)
        {
            var previewTags = new List<PredictionResult>();
            Console.WriteLine("Generating test prediction in 5 seconds...");
            
            Thread.Sleep(5000);

            var testResult = new PredictionResult()
            {
                Tag = "LONG",
                FromAmount = currentValue,
                Score = 0.99f,
                CreationDate = currentTime,
                DateRange = new DateRange(currentTime, currentTime.AddHours(3)),
                RatioLowerBound = 0.75/100,
                RatioUpperBound = 1.25/100
            };
            

            testResult.LowerBound = currentValue * (1 + testResult.RatioLowerBound);
            testResult.UpperBound = currentValue * (1 + testResult.RatioUpperBound);

            previewTags.Add(testResult);

            Console.WriteLine("Here is a prediction...");

            return previewTags;
        }

        public async Task<List<PredictionResult>> PredictAsync(Bitmap image, float scoreThreshold, double currentValue, DateTime currentTime)
        {
            List<PredictionResult> res = null;
            var t = Task<List<PredictionResult>>.Run(() => {
                res = Predict(image, scoreThreshold, currentValue, currentTime);
            });

            await t;

            return res;
        }
    }
}