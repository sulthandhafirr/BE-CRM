using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Api.Models
{
    [Table("tiers", Schema = "public")]
    public class Tier
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("tier")]
        public string? TierName { get; set; }

        [Column("company_id")]
        public int CompanyId { get; set; }

        [Column("color")]
        public string Color { get; set; } = "#6b7280";

        [Column("level")]
        public int Level { get; set; }

        [ForeignKey("CompanyId")]
        public Company? Company { get; set; }
    }

    [Table("profile_tier", Schema = "public")]
    public class ProfileTier
    {
        [Key, Column("profile_id")]
        public Guid ProfileId { get; set; }

        [Column("tier_id")]
        public int TierId { get; set; }

        public Tier? Tier { get; set; }
        public Profile? Profile { get; set; }
    }
}