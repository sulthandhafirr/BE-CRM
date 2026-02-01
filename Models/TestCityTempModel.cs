using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Api.Models
{
    [Table("test_city_temp", Schema = "public")]
    public class TestCityTemp
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("city")]
        public string? City { get; set; }

        [Column("temperature")]
        public double Temperature { get; set; }
    }
}
