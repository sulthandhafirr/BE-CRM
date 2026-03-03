using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Api.Models
{
    [Table("roles", Schema = "public")]
    public class Role
    {
        [Key] //Primary Key
        [Column("id")]
        public int Id { get; set; }

        [Column("role")]
        public string? RoleName { get; set; }
    }

    [Table("profile", Schema = "public")]
    public class Profile
    {
        [Key]
        [Column("id")]
        public Guid Id { get; set; } //Guid = UUID

        [Column("role_id")]
        public int RoleId { get; set; }

        public Role? Role { get; set; }
    }
}