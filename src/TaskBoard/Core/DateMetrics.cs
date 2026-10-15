using System;

namespace TaskBoard.Core
{
    public sealed class DateMetrics
    {
        private DateMetrics() { }

        public int WeekNumber { get; private set; }
        public int WeekYear { get; private set; }
        public double MonthRemainingPercent { get; private set; }
        public double YearRemainingPercent { get; private set; }

        public static DateMetrics From(DateTime localNow)
        {
            DateTime date = localNow.Date;
            int mondayBasedDay = ((int)date.DayOfWeek + 6) % 7;
            // Every ISO week belongs to the year containing its Thursday.
            // DateTime.MinValue/MaxValue also have in-range Thursdays.
            DateTime thursday = date.AddDays(3 - mondayBasedDay);
            int daysInMonth = DateTime.DaysInMonth(date.Year, date.Month);
            int daysInYear = DateTime.IsLeapYear(date.Year) ? 366 : 365;

            return new DateMetrics
            {
                WeekNumber = (thursday.DayOfYear - 1) / 7 + 1,
                WeekYear = thursday.Year,
                MonthRemainingPercent = 100.0 * (daysInMonth - date.Day + 1) / daysInMonth,
                YearRemainingPercent = 100.0 * (daysInYear - date.DayOfYear + 1) / daysInYear
            };
        }
    }
}
