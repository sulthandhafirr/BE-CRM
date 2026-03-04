using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Api.Models
{
    [Table("priority", Schema = "public")]
    public class PriorityList
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("priority")]
        public string? PriorityName { get; set; }
    }
}