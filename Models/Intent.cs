using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Api.Models
{
    [Table("intent", Schema = "public")]
    public class Intent
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Column("intent")]
        public string? IntentName { get; set; }
    }
}
