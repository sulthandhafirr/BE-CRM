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

        private static TimeZoneInfo ResolveTimeZone(string? timezoneLabel)
        {
            var ianaId = TimezoneToIana.GetValueOrDefault(timezoneLabel ?? "", "Asia/Jakarta");
            return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
        }

        public static DateTime ConvertToLocal(DateTime utcDateTime, string? timezoneLabel)
        {
            var tz = ResolveTimeZone(timezoneLabel);
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc), tz);
        }

        private static TimeSpan? ParseTimeOfDay(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (TimeSpan.TryParse(value, out var ts)) return ts; // handles "09:00"
            if (DateTime.TryParseExact(value, "hh:mm tt", null,
                    System.Globalization.DateTimeStyles.None, out var dt))
                return dt.TimeOfDay; // handles "09:00 AM"
            return null;
        }

        private static bool IsWorkingDay(DateTime localDate, string[] workingDays) =>
            workingDays.Contains(DayOfWeekToCode[localDate.DayOfWeek]);

        public static DateTime ComputeSlaDeadline(
            DateTime createdUtc,
            double resolutionHours,
            string[]? workingDays,
            string? workingHoursStart,
            string? workingHoursEnd,
            string? timezoneLabel)
        {
            var start = ParseTimeOfDay(workingHoursStart);
            var end = ParseTimeOfDay(workingHoursEnd);

            if (workingDays is null || workingDays.Length == 0 || start is null || end is null || end <= start)
                return createdUtc.AddHours(resolutionHours); // fallback no valid config

            var tz = ResolveTimeZone(timezoneLabel);
            var localCreated = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(createdUtc, DateTimeKind.Utc), tz);

            var cursor = localCreated;
            for (int i = 0; i < 8; i++)
            {
                if (!IsWorkingDay(cursor.Date, workingDays))
                {
                    cursor = cursor.Date.AddDays(1) + start.Value;
                    continue;
                }

                var dayStart = cursor.Date + start.Value;
                var dayEnd = cursor.Date + end.Value;

                if (cursor < dayStart) { cursor = dayStart; break; }
                if (cursor >= dayEnd) { cursor = cursor.Date.AddDays(1) + start.Value; continue; }
                break; // already inside working hours
            }

            var remaining = resolutionHours;
            for (int i = 0; i < 3650; i++)
            {
                if (!IsWorkingDay(cursor.Date, workingDays))
                {
                    cursor = cursor.Date.AddDays(1) + start.Value;
                    continue;
                }

                var dayEnd = cursor.Date + end.Value;
                var availableToday = (dayEnd - cursor).TotalHours;

                if (remaining <= availableToday)
                {
                    var resultLocal = cursor.AddHours(remaining);
                    return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(resultLocal, DateTimeKind.Unspecified), tz);
                }

                remaining -= availableToday;
                var nextDay = cursor.Date.AddDays(1);
                cursor = nextDay + start.Value;
            }

            return createdUtc.AddHours(resolutionHours);
        }
    }
}