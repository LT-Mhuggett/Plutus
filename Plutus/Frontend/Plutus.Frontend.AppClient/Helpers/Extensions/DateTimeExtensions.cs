using System;
using System.Globalization;

namespace Plutus.Frontend.AppClient.Helpers.Extensions
{
    public static class DateTimeExtensions
    {
        public static DateTime StartOfWeek(this DateTime dt)
        {
            int diff = (7 + (dt.DayOfWeek - CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek)) % 7;
            return dt.AddDays(-1 * diff).Date;
        }
    }
}
