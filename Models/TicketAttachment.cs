using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Api.Models
{
    [Table("ticket_attachment", Schema = "public")]
    public class TicketAttachment
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Column("ticket_id")]
        public long TicketId { get; set; }

        [Column("file_url")]
        public string? FileUrl { get; set; }

        [Column("file_name")]
        public string? FileName { get; set; }

        [Column("file_size")]
        public long FileSize { get; set; }

        [Column("uploaded_at")]
        public DateTime UploadedAt { get; set; }

        [ForeignKey("TicketId")]
        public Ticket? Ticket { get; set; }
    }
}