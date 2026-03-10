using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using CRM.Api.Data;
using CRM.Api.Models;
using CRM.Api.Services;

namespace CRM.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/tickets")]
    public class TicketController : BaseController
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpClientFactory;

        public TicketController(AppDbContext db, RoleService roleService, IConfiguration config, IHttpClientFactory httpClientFactory)
            : base(roleService)
        {
            _db = db;
            _config = config;
            _httpClientFactory = httpClientFactory;
        }

        // GET /api/tickets — customer gets their own tickets
        [HttpGet]
        public async Task<IActionResult> GetMyTickets()
        {
            var role = await GetCurrentUserRole();
            if (role != "customer") return Forbid(); // Only customers

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var tickets = await _db.Tickets
                .Include(t => t.Priority)
                .Where(t => t.CustomerId == userId) // fetch only tickets of the logged-in customer
                .OrderByDescending(t => t.CreatedAt)
                .AsNoTracking()
                .Select(t => new
                {
                    id = t.Id,
                    subject = t.Subject,
                    description = t.Description,
                    status = t.Status,
                    // priority = t.Priority != null ? t.Priority.PriorityName : "Analyzing...",
                    handler = t.Agent != null ? t.Agent.Name : "Not assigned yet",
                    // slaDeadline = t.SlaDeadline,
                    // slaBreached = t.SlaBreached,
                    createdAt = t.CreatedAt,
                })
                .ToListAsync();

            return Ok(tickets);
        }

        // GET /api/tickets/upload-url - generate signed upload URLs for attachments
        [HttpPost("upload-url")]
        public async Task<IActionResult> GetUploadUrls([FromBody] UploadUrlRequest request)
        {
            var role = await GetCurrentUserRole();
            if (role != "customer") return Forbid();

            if (request.Files == null || request.Files.Count == 0)
                return BadRequest("Files are required");

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var ownsTicket = await _db.Tickets
                .AsNoTracking()
                .AnyAsync(t => t.Id == request.TicketId && t.CustomerId == userId);

            if (!ownsTicket)
                return NotFound("Ticket not found or access denied");

            var supabaseUrl = _config["Supabase:Url"];
            var serviceKey = _config["Supabase:ServiceKey"];
            var httpClient = _httpClientFactory.CreateClient();

            var results = new List<object>();

            foreach (var file in request.Files)
            {
                // var filePath = $"temp/{Guid.NewGuid()}_{file.FileName}";
                var filePath = $"{request.TicketId}/{Guid.NewGuid()}_{file.FileName}";
                var requestUrl = $"{supabaseUrl}/storage/v1/object/upload/sign/ticket-attachment/{filePath}";

                var requestBody = new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(new { expiresIn = 60 }),
                    System.Text.Encoding.UTF8,
                    "application/json"
                );

                var req = new HttpRequestMessage(HttpMethod.Post, requestUrl);
                req.Headers.Add("Authorization", $"Bearer {serviceKey}");
                req.Content = requestBody;

                var response = await httpClient.SendAsync(req);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode, new { message = "Failed to generate upload URL", detail = responseBody });

                var json = System.Text.Json.JsonDocument.Parse(responseBody);
                if (!json.RootElement.TryGetProperty("url", out var urlElement) ||
                    !json.RootElement.TryGetProperty("token", out var tokenElement))
                    return StatusCode(502, new { message = "Invalid response from storage service" });

                var signedUrl = urlElement.GetString();
                var token = tokenElement.GetString();

                if (string.IsNullOrWhiteSpace(signedUrl) || string.IsNullOrWhiteSpace(token))
                    return StatusCode(502, new { message = "Storage service returned empty URL/token" });

                results.Add(new
                {
                    signedUrl = $"{supabaseUrl}/storage/v1{signedUrl}",
                    filePath,
                    token,
                    fileName = file.FileName,
                });
            }

            return Ok(results);
        }

        // POST /api/tickets — customer creates a ticket
        [HttpPost]
        public async Task<IActionResult> CreateTicket([FromBody] CreateTicketRequest request)
        {
            var role = await GetCurrentUserRole();
            if (role != "customer") return Forbid();

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            // For now assign Medium priority
            // Replace this with AI logic later
            var priorityId = 2;
            var slaDeadline = DateTime.UtcNow.AddHours(24); // Normal = 24 hours

            var ticket = new Ticket
            {
                CustomerId = userId,
                Subject = request.Subject,
                Description = request.Description,
                Status = "Waiting",
                PriorityId = priorityId,
                SlaDeadline = slaDeadline,
                SlaBreached = false,
                CreatedAt = DateTime.UtcNow,
            };

            _db.Tickets.Add(ticket);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Ticket created successfully!", ticketId = ticket.Id });
        }

        [HttpPost("{ticketId}/attachments")]
        public async Task<IActionResult> SaveAttachments(long ticketId, [FromBody] List<AttachmentInfo> attachments)
        {
            var role = await GetCurrentUserRole();
            if (role != "customer") return Forbid();

            if (attachments == null || attachments.Count == 0)
                return BadRequest("Attachments are required");

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var ownsTicket = await _db.Tickets
                .AsNoTracking()
                .AnyAsync(t => t.Id == ticketId && t.CustomerId == userId);

            if (!ownsTicket)
                return NotFound("Ticket not found or access denied");

            var supabaseUrl = _config["Supabase:Url"];

            foreach (var attachment in attachments)
            {
                var fileUrl = $"{supabaseUrl}/storage/v1/object/public/ticket-attachment/{attachment.FilePath}";

                await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO public.ticket_attachment (ticket_id, file_url, file_name, file_size, uploaded_at)
            VALUES ({ticketId}, {fileUrl}, {attachment.FileName}, {attachment.FileSize}, {DateTime.UtcNow})");
            }

            return Ok(new { message = "Attachments saved!" });
        }

        public class AttachmentInfo
        {
            public string? FilePath { get; set; }
            public string? FileName { get; set; }
            public long FileSize { get; set; }
        }

        public class CreateTicketRequest
        {
            public string? Subject { get; set; }
            public string? Description { get; set; }
        }

        public class FileRequest
        {
            public string? FileName { get; set; }
            // public string? ContentType { get; set; }
        }

        public class UploadUrlRequest
        {
            public long TicketId { get; set; }
            public List<FileRequest> Files { get; set; } = new();
        }
    }
}