using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Api.Models
{
    [Table("notification", Schema = "public")]
    public class Notification
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Column("user_id")] 
        public Guid UserId { get; set; }

        [ForeignKey("UserId")]
        public Profile? User { get; set; }

        [Column("message")]
        public string? Message { get; set; }

        [Column("is_read")]
        public bool IsRead { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }
    }
}