namespace CRM.Api.Services
{
    public static class CompanyTimeZoneHelper
    {
        private static readonly Dictionary<string, string> TimezoneToIana = new()
        {
            ["WIB (UTC+7)"] = "Asia/Jakarta",
            ["WITA (UTC+8)"] = "Asia/Makassar",
            ["WIT (UTC+9)"] = "Asia/Jayapura",
        };

        private static readonly Dictionary<DayOfWeek, string> DayOfWeekToCode = new()
        {
            [DayOfWeek.Monday] = "Mon",
            [DayOfWeek.Tuesday] = "Tue",
            [DayOfWeek.Wednesday] = "Wed",
            [DayOfWeek.Thursday] = "Thu",
            [DayOfWeek.Friday] = "Fri",
            [DayOfWeek.Saturday] = "Sat",
            [DayOfWeek.Sunday] = "Sun",
        };

        public static DateTime ConvertToLocal(DateTime utcDateTime, string? timezoneLabel)
        {
            var ianaId = TimezoneToIana.GetValueOrDefault(timezoneLabel ?? "", "Asia/Jakarta");
            var tz = TimeZoneInfo.FindSystemTimeZoneById(ianaId);
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc), tz);
        }

        public static DateTime AdjustForWorkingDays(DateTime utcDeadline, string[]? workingDays, string? timezoneLabel)
        {
            if (workingDays is null || workingDays.Length == 0)
                return utcDeadline;

            var adjusted = utcDeadline;
            for (int i = 0; i < 8; i++)
            {
                var localDay = ConvertToLocal(adjusted, timezoneLabel).DayOfWeek;
                var dayCode = DayOfWeekToCode[localDay];
                if (workingDays.Contains(dayCode))
                    return adjusted;
                adjusted = adjusted.AddDays(1);
            }
            return adjusted;
        }
    }
}