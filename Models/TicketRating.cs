using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Api.Models
{
    [Table("ticket_rating", Schema = "public")]
    public class TicketRating
    {
        [Key]
        [Column("id")]
        public Guid Id { get; set; }

        [Column("ticket_id")]
        public long TicketId { get; set; }

        [ForeignKey("TicketId")]
        public Ticket? Ticket { get; set; }

        // 1 - 5
        [Column("rate")]
        public long Rate { get; set; }

        [Column("rated_at")]
        public DateTime RatedAt { get; set; }

        [Column("message")]
        public string? Message { get; set; }
    }
}