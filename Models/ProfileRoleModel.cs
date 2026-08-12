using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Api.Models
{
    [Table("roles", Schema = "public")]
    public class Role
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("role")]
        public string? RoleName { get; set; }

        [Column("company_id")]
        public int CompanyId { get; set; }

        [ForeignKey("CompanyId")]
        public Company? Company { get; set; }

        [Column("is_system")]
        public bool IsSystem { get; set; }

        public RolePermission? RolePermission { get; set; }
        public ICollection<Profile> Profiles { get; set; } = new List<Profile>();
    }

    [Table("role_permissions", Schema = "public")]
    public class RolePermission
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("role_id")]
        public int RoleId { get; set; }

        [ForeignKey("RoleId")]
        public Role? Role { get; set; }

        [Column("permissions", TypeName = "jsonb")]
        public string Permissions { get; set; } = "{}";
    }

    [Table("skills", Schema = "public")]
    public class Skill
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("skill")]
        public string? SkillName { get; set; }

        [Column("company_id")]
        public int? CompanyId { get; set; }

        [ForeignKey("CompanyId")]
        public Company? Company { get; set; }
    }

    [Table("profile_skill", Schema = "public")]
    public class ProfileSkill
    {
        [Column("profile_id")]
        public Guid ProfileId { get; set; }

        [Column("skill_id")]
        public int SkillId { get; set; }

        public Skill? Skill { get; set; }
    }

    [Table("profile", Schema = "public")]
    public class Profile
    {
        [Key]
        [Column("id")]
        public Guid Id { get; set; }

        [Column("email")]
        public string? Email { get; set; }

        [Column("name")]
        public string? Name { get; set; }

        [Column("role_id")]
        public int RoleId { get; set; }

        public Role? Role { get; set; }

        [Column("Position")]
        public string? Position { get; set; }

        [Column("avatar_url")]
        public string? AvatarUrl { get; set; }

        [Column("company_id")]
        public int? CompanyId { get; set; }

        [ForeignKey("CompanyId")]
        public Company? Company { get; set; }

        [Column("created_at")]
        public DateTime? CreatedAt { get; set; }
        public ICollection<ProfileSkill> ProfileSkills { get; set; } = new List<ProfileSkill>();
        public ICollection<ProfileTier> ProfileTiers { get; set; } = new List<ProfileTier>();
    }
}
