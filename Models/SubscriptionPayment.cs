using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Api.Models
{
    [Table("subscription_payment", Schema = "public")]
    public class SubscriptionPayment
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Column("company_id")]
        public int CompanyId { get; set; }

        [ForeignKey("CompanyId")]
        public Company? Company { get; set; }

        [Column("subscription_plan")]
        public string SubscriptionPlan { get; set; } = null!;

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

        [Column("subscription_start")]
        public DateTime? SubscriptionStart { get; set; }

        [Column("subscription_end")]
        public DateTime? SubscriptionEnd { get; set; }
    }
}
