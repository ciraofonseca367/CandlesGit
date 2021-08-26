
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using Midas.Util;

namespace Midas.Core.Forecast
{
    public interface IForecast
    {
        Task<List<PredictionResult>> PredictAsync(Bitmap image, float scoreThreshold, double currentValue, DateTime currentTime);

        List<PredictionResult> Predict(Bitmap image, float scoreThreshold, double currentValue, DateTime currentTime);
    }

    public class ForecastFactory
    {
        public static IForecast GetForecaster(string id)
        {
            switch(id)
            {
                case "ThirtyPeriodForecast":
                    return new ThirtyPeriodForecast();
                case "TestForecast":
                    return new TestForecast();
                default:
                    throw new NotImplementedException(id + " - Not implemented");
            }
        }

        public static IForecast GetDefaultForecaster()
        {
            return new ThirtyPeriodForecast();
        }
    }
}