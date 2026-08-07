using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Api.Models
{
    [Table("ticket", Schema = "public")]
    public class Ticket
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Column("customer_id")]
        public Guid CustomerId { get; set; }

        [ForeignKey("CustomerId")]
        public Profile? Customer { get; set; }

        [Column("subject")]
        public string? Subject { get; set; }

        [Column("description")]
        public string? Description { get; set; }

        [Column("priority_id")]
        public int? PriorityId { get; set; }

        [ForeignKey("PriorityId")]
        public PriorityList? Priority { get; set; }

        // [Column("sentiment")]
        // public string? Sentiment { get; set; }

        // [Column("sentiment_confidence")]
        // public double? SentimentConfidence { get; set; }

        [Column("intent_id")]
        public long? IntentId { get; set; }

        [ForeignKey("IntentId")]
        public Intent? Intent { get; set; }

        [Column("intent_confidence")]
        public double? IntentConfidence { get; set; }

        [Column("urgency")]
        public string? Urgency { get; set; }

        [Column("urgency_confidence")]
        public double? UrgencyConfidence { get; set; }

        [Column("user_choosen_priority_id")]
        public int? UserChoosenPriorityId { get; set; }

        [ForeignKey("UserChoosenPriorityId")]
        public PriorityList? UserChoosenPriority { get; set; }

        [Column("status")]
        public string? Status { get; set; }

        [Column("sla_deadline")]
        public DateTime? SlaDeadline { get; set; }

        [Column("first_response_at")]
        public DateTime? FirstResponseAt { get; set; }

        [Column("resolved_at")]
        public DateTime? ResolvedAt { get; set; }

        [Column("sla_breached")]
        public bool SlaBreached { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("agent_id")]
        public Guid? AgentId { get; set; }

        [ForeignKey("AgentId")]
        public Profile? Agent { get; set; }

        [Column("technician_id")]
        public Guid? TechnicianId { get; set; }

        [ForeignKey("TechnicianId")]
        public Profile? Technician { get; set; }

        [Column("response_time_sec")]
        public int? ResponseTimeSec { get; set; }

        [Column("resolution_time_sec")]
        public int? ResolutionTimeSec { get; set; }

        [Column("sla_reminder_sent")]
        public bool SlaReminderSent { get; set; }

        [Column("sla_breached_notified")]
        public bool SlaBreachedNotified { get; set; }

        [Column("is_billable")]
        public bool IsBillable { get; set; }

        [Column("bill_amount")]
        public decimal? BillAmount { get; set; }
        // Navigation properties
        // public Profile? Customer { get; set; }
        // public Profile? Agent { get; set; }
        // public PriorityList? Priority { get; set; }
        public ICollection<TicketAttachment> Attachments { get; set; } = new List<TicketAttachment>();
        public ICollection<TicketComment> Comments { get; set; } = new List<TicketComment>();
        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    }
}