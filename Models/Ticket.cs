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
        // Navigation properties
        // public Profile? Customer { get; set; }
        // public Profile? Agent { get; set; }
        public PriorityList? Priority { get; set; }
    }
}