
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using Midas.Util;

namespace Midas.Core.Forecast
{
    public interface IForecast
    {
        Task<List<PredictionResult>> PredictAsync(Bitmap image,string asset, float scoreThreshold, double currentValue, DateTime currentTime);

        List<PredictionResult> Predict(Bitmap image,string asset, float scoreThreshold, double currentValue, DateTime currentTime);
        Task<Prediction> GetPredictionAsync(Bitmap image, string asset, double currentValue, DateTime currentTime);
    }

    public class ForecastFactory
    {
        public static IForecast GetForecaster(string id, string url)
        {
            switch(id)
            {
                case "ThirtyPeriodForecast":
                    return new ThirtyPeriodForecast(url);
                case "FaceLabelForecast":
                    return new FaceLabelForecast(url);
                case "TestForecast":
                    return new TestForecast();
                default:
                    throw new NotImplementedException(id + " - Not implemented");
            }
        }
    }
}