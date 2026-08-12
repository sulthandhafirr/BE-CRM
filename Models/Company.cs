using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Api.Models
{
    [Table("company", Schema = "public")]
    public class Company
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("company_name")]
        public string? CompanyName { get; set; }

        [Column("company_code")]
        public string? CompanyCode { get; set; }

        [Column("support_email")]
        public string? SupportEmail { get; set; }

        [Column("phone_number")]
        public string? PhoneNumber { get; set; }

        [Column("timezone")]
        public string? Timezone { get; set; }

        [Column("working_days")]
        public string[]? WorkingDays { get; set; }

        [Column("working_hours_start")]
        public string? WorkingHoursStart { get; set; }

        [Column("working_hours_end")]
        public string? WorkingHoursEnd { get; set; }

        [Column("logo_url")]
        public string? LogoUrl { get; set; }

        [Column("ticket_status_config", TypeName = "jsonb")]
        public string TicketStatusConfig { get; set; } = "{}";

        [Column("sla_config", TypeName = "jsonb")]
        public string SlaConfig { get; set; } = "{}";

        [Column("export_schedule_config", TypeName = "jsonb")]
        public string ExportScheduleConfig { get; set; } = "{}";
    }
}