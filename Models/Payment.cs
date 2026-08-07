using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Api.Models
{
    [Table("payment", Schema = "public")]
    public class Payment
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Column("ticket_id")]
        public long TicketId { get; set; }

        [ForeignKey("TicketId")]
        public Ticket? Ticket { get; set; }

        [Column("amount")]
        public decimal Amount { get; set; }

        [Column("status")]
        public string Status { get; set; } = "pending";

        [Column("midtrans_order_id")]
        public string MidtransOrderId { get; set; } = null!;

        [Column("midtrans_transaction_id")]
        public string? MidtransTransactionId { get; set; }

        [Column("payment_method")]
        public string? PaymentMethod { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("paid_at")]
        public DateTime? PaidAt { get; set; }
        
        [Column("due_date")]
        public DateTime? DueDate { get; set; }
    }
}