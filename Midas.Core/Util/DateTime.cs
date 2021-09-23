using System;

namespace DateTimeUtil
{
    public class TimeSpanPlus
    {
        public static string ToString(TimeSpan span)
        {
            string ret = "";

            if(span.Days > 0)
            {
                ret += $"{span.Days}day";
                ret += span.Days == 1 ? String.Empty : "s";
                ret += " ";
            }

            if(span.Hours > 0)
            {
                ret += $"{span.Hours}hr";
                ret += span.Hours == 1 ? String.Empty : "s";
                ret += " ";
            }

            if(span.Minutes > 0)
            {
                ret += $"{span.Minutes}min";
                ret += span.Minutes == 1 ? String.Empty : "s";
                ret += " ";
            }

            return ret;
        }
    }
}