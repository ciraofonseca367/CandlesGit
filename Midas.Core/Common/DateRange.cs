using System;

namespace Midas.Core.Common
{
    public class DateRange
    {
        public DateRange(DateTime start, DateTime end)
        {
            Start = start;
            End = end;
        }

        public DateTime Start
        {
            get;
            set;
        }

        public DateTime End
        {
            get;
            set;
        }

        public TimeSpan GetSpan()
        {
            return End - Start;
        }

        public static DateRange GetDateRange(int year, int monthStart, int monthEnd)
        {
            return new DateRange(
                new DateTime(year,monthStart,1),
                new DateTime(year,monthEnd,DateTime.DaysInMonth(year,monthEnd), 23,59,59)
                );
        }

        public bool IsInside(DateTime pointInTime)
        {
            return (pointInTime >= this.Start && pointInTime <= this.End);
        }

        public bool IsOverlap(DateRange range)
        {
            return (range.Start >= this.Start && range.Start <= this.End) ||
                (range.End >= range.Start && range.End <= range.End);
        }

        public static DateRange GetInfiniteRange()
        {
            return new DateRange(DateTime.MaxValue, DateTime.MaxValue);
        }

        internal static Int64 InSeconds(DateTime pointInTime)
        {
            return Convert.ToInt64((pointInTime - DateTime.MinValue).TotalSeconds);
        }

        public override string ToString()
        {
            return Start.ToString("yyyy-MM-dd HH:mm") + " - " + End.ToString("yyyy-MM-dd HH:mm");
        }
    }
}

