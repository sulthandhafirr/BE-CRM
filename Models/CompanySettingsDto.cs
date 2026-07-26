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
        public string? TicketNumberFormat { get; set; }
        public string? DateFormat { get; set; }
        public string? LogoUrl { get; set; }
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
        public string? TicketNumberFormat { get; set; }
        public string? DateFormat { get; set; }
        public string? LogoUrl { get; set; }
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
}
