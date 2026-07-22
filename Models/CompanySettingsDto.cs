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
}
