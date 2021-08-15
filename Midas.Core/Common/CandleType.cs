using System;

namespace Midas.Core.Common
{
    public enum CandleType
    {
        MIN1 = 1,
        MIN5 = 5,
        MIN15 = 15,
        MIN30 = 30,
        HOUR1 = 60,
        HOUR2 = 120,
        HOUR4 = 240,
        DAY1 = 1440,
        WEEK1 = 10080
    }

    public class CandleTypeConverter
    {
        public static string Convert(CandleType type)
        {
            switch(type)
            {
                case CandleType.MIN1:
                    return "1m";
                case CandleType.MIN5:
                    return "5m";
                case CandleType.MIN15:
                    return "15m";
                case CandleType.MIN30:
                    return "30m";
                case CandleType.HOUR1:
                    return "1h";
                case CandleType.HOUR2:
                    return "2h";
                case CandleType.HOUR4:
                    return "4h";
                case CandleType.DAY1:
                    return "1d";
                case CandleType.WEEK1:
                    return "1w";
                default:
                    throw new ArgumentException("Unknown type");
            }
        }
    }
}