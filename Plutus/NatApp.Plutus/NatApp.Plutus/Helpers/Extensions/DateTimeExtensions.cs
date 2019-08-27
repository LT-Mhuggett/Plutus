using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NatApp.Plutus.Helpers.Extensions
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
