namespace CRM.Api.Models
{
    public class CompanySettingsDto
    {
        public string? CompanyName { get; set; }
        public string? SupportEmail { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Timezone { get; set; }
        public string[]? WorkingDays { get; set; }
        public string? WorkingHoursStart { get; set; }
        public string? WorkingHoursEnd { get; set; }
        public string? LogoUrl { get; set; }
        public int? PaymentDueDays { get; set; }
    }

    public class CompanySettingsResponse
    {
        public int Id { get; set; }
        public string? CompanyName { get; set; }
        public string? CompanyCode { get; set; }
        public string? SupportEmail { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Timezone { get; set; }
        public string[]? WorkingDays { get; set; }
        public string? WorkingHoursStart { get; set; }
        public string? WorkingHoursEnd { get; set; }
        public string? LogoUrl { get; set; }
        public int PaymentDueDays { get; set; } = 3;
        public DateTime CreatedAt { get; set; }
    }

    // ── Ticket Status Config ──────────────────────────────────────────

    public class TicketStatusItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = "blue";
        public bool Active { get; set; } = true;
    }

    public class TicketStatusConfigDto
    {
        public List<TicketStatusItem> Statuses { get; set; } = new();
        public bool AllowTicketReopen { get; set; } = true;
        public int AutoCloseTicketAfterDays { get; set; } = 7;
    }

    // ── SLA Rules Config ──────────────────────────────────────────────

    public class SlaRuleItem
    {
        public string Priority { get; set; } = string.Empty;
        public int FirstResponseHours { get; set; }
        public int ResolutionHours { get; set; }
    }

    public class SlaRulesConfigDto
    {
        public bool EnableSlaMonitoring { get; set; } = true;
        public int NotifyBeforeBreachedMinutes { get; set; } = 30;
        public List<SlaRuleItem> Rules { get; set; } = new();
    }

    // ── Export Schedule Config ─────────────────────────────────────────

    public class ExportScheduleConfigDto
    {
        /// <summary>"daily", "weekly", or "monthly".</summary>
        public string Frequency { get; set; } = "monthly";
        public bool Enabled { get; set; } = false;
        /// <summary>Day of month (1-31) used when Frequency is "monthly".</summary>
        public int DayOfMonth { get; set; } = 30;
        /// <summary>Day of week (0=Sunday … 6=Saturday) used when Frequency is "weekly".</summary>
        public int DayOfWeek { get; set; } = 1;
        public string Time { get; set; } = "08:00";
        public bool IncludeTickets { get; set; } = true;
        public bool IncludeUsers { get; set; } = true;
        public bool IncludeCombined { get; set; } = true;
        public List<string> Recipients { get; set; } = new();
        public string? LastSentDate { get; set; }
    }
}
