using System;
using System.Collections.Generic;
using System.Globalization;
using TaskBoard.Core;

namespace TaskBoard.Rendering
{
    /// <summary>Shared labels keep the screen, exported image and printed page consistent.</summary>
    public static class BoardLabels
    {
        private static readonly IReadOnlyList<string> dayNames = Array.AsReadOnly(
            new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" });

        public static IReadOnlyList<string> DayNames { get { return dayNames; } }

        public static string Date(DateTime date)
        {
            return date.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);
        }

        public static string Week(DateTime date)
        {
            DateMetrics metrics = DateMetrics.From(date);
            return metrics.WeekYear.ToString(CultureInfo.InvariantCulture) + " / W" +
                metrics.WeekNumber.ToString("00", CultureInfo.InvariantCulture);
        }

        public static string Remaining(DateTime localNow)
        {
            DateMetrics metrics = DateMetrics.From(localNow);
            return "Month left " + metrics.MonthRemainingPercent.ToString("0.0", CultureInfo.InvariantCulture) +
                "%   /   Year left " + metrics.YearRemainingPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }
    }
}
