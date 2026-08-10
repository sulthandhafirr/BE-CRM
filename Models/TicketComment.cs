using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Api.Models
{
    [Table("ticket_comment", Schema = "public")]
    public class TicketComment
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [ForeignKey("TicketId")]
        public Ticket? Ticket { get; set; }

        [Column("ticket_id")]
        public long TicketId { get; set; }

        [ForeignKey("SenderId")]
        public Profile? Sender { get; set; }

        [Column("sender_id")]
        public Guid? SenderId { get; set; }

        [Column("message")]
        public string? Message { get; set; }

        [Column("created_at")]
        public DateTime? CreatedAt { get; set; }
    }
}