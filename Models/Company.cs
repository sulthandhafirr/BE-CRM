using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Api.Models
{
    [Table("company", Schema = "public")]
    public class Company
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("company_name")]
        public string? CompanyName { get; set; }

        [Column("company_code")]
        public string? CompanyCode { get; set; }
    }
}