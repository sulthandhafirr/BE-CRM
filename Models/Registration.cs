using System.ComponentModel.DataAnnotations;

namespace CRM.Api.Models
{
    public class RegisterRequest
    {
        [Required, StringLength(120)]
        public string FullName { get; set; } = string.Empty;

        [Required, EmailAddress, StringLength(320)]
        public string Email { get; set; } = string.Empty;

        [Required, MinLength(8), StringLength(128)]
        public string Password { get; set; } = string.Empty;

        [Required, StringLength(160)]
        public string CompanyName { get; set; } = string.Empty;

        [Required, StringLength(80)]
        public string CompanyCode { get; set; } = string.Empty;

        [Required]
        public string SubscriptionPlan { get; set; } = string.Empty;
    }

    public class RegisterResponse
    {
        public int CompanyId { get; set; }
        public bool RequiresPayment { get; set; }
        public string SubscriptionPlan { get; set; } = string.Empty;
        public string SubscriptionStatus { get; set; } = string.Empty;
        public DateTime? SubscriptionEnd { get; set; }
        public string? SnapToken { get; set; }
        public string? RedirectUrl { get; set; }
    }

    public class RenewalRequest
    {
        [Required]
        public string SubscriptionPlan { get; set; } = string.Empty;
    }
}
