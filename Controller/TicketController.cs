using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using CRM.Api.Data;
using CRM.Api.Models;
using CRM.Api.Services;
using System.Text.Json.Serialization;

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
            if (role != "customer") return Forbid();

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var tickets = await _db.Tickets
                .Include(t => t.Priority)
                .Include(t => t.Agent)
                .Where(t => t.CustomerId == userId && t.Status != "Solved")
                .OrderByDescending(t => t.CreatedAt)
                .AsNoTracking()
                .Select(t => new
                {
                    id = t.Id,
                    subject = t.Subject,
                    description = t.Description,
                    status = t.Status,
                    handler = t.Agent != null ? t.Agent.Name : "Not assigned yet",
                    createdAt = t.CreatedAt,
                })
                .ToListAsync();

            return Ok(tickets);
        }

        // GET /api/tickets/history — customer gets their solved ticket history
        [HttpGet("history")]
        public async Task<IActionResult> GetMyTicketHistory()
        {
            var role = await GetCurrentUserRole();
            if (role != "customer") return Forbid();

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var tickets = await _db.Tickets
                .Include(t => t.Priority)
                .Where(t => t.CustomerId == userId && t.Status == "Solved")
                .OrderByDescending(t => t.ResolvedAt)
                .AsNoTracking()
                .Select(t => new
                {
                    id = t.Id,
                    subject = t.Subject,
                    description = t.Description,
                    status = t.Status,
                    handler = t.Agent != null ? t.Agent.Name : "Not assigned yet",
                    createdAt = t.CreatedAt,
                    resolvedAt = t.ResolvedAt,
                })
                .ToListAsync();

            return Ok(tickets);
        }

        [HttpGet("my-solved")]
        public async Task<IActionResult> GetMySolvedTickets()
        {
            var role = await GetCurrentUserRole();
            if (role != "cs_agent" && role != "admin") return Forbid();

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var tickets = await _db.Tickets
                .Include(t => t.Priority)
                .Include(t => t.Customer)
                .Include(t => t.Agent)
                .Include(t => t.Attachments)
                .Where(t => t.AgentId == userId && t.Status == "Solved")
                .OrderByDescending(t => t.ResolvedAt)
                .AsNoTracking()
                .Select(t => new
                {
                    id          = t.Id,
                    subject     = t.Subject,
                    description = t.Description,
                    status      = t.Status,
                    priority    = t.Priority != null ? t.Priority.PriorityName : null,
                    customer    = t.Customer != null ? t.Customer.Name : null,
                    solver      = t.Agent != null ? t.Agent.Name : null,
                    handler     = t.Agent != null ? t.Agent.Name : null,
                    createdAt   = t.CreatedAt,
                    resolvedAt  = t.ResolvedAt,
                    attachments = t.Attachments.Select(a => new
                    {
                        id         = a.Id,
                        fileName   = a.FileName,
                        fileSize   = a.FileSize,
                        uploadedAt = a.UploadedAt,
                    }).ToList(),
                })
                .ToListAsync();

            return Ok(tickets);
        }

        // GET /api/tickets/{id} — get ticket detiails (customer) maybe other roles
        [HttpGet("{id}")]
        public async Task<IActionResult> GetTicketById(long id)
        {
            var role = await GetCurrentUserRole();
            if (role != "customer") return Forbid();

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var ticket = await _db.Tickets
                .Include(t => t.Priority)
                .Include(t => t.Attachments)
                .Where(t => t.Id == id && t.CustomerId == userId)
                .AsNoTracking()
                .Select(t => new
                {
                    id = t.Id,
                    subject = t.Subject,
                    description = t.Description,
                    status = t.Status,
                    // priority = t.Priority != null ? t.Priority.PriorityName : "Analyzing...",
                    handler = t.Agent != null ? t.Agent.Name : "Not assigned yet",
                    createdAt = t.CreatedAt,
                    resolvedAt = t.ResolvedAt,
                    attachments = t.Attachments.Select(a => new
                    {
                        id = a.Id,
                        fileName = a.FileName,
                        fileUrl = a.FileUrl,
                        fileSize = a.FileSize,
                        uploadedAt = a.UploadedAt,
                    }).ToList()
                })
                .FirstOrDefaultAsync();

            if (ticket == null) return NotFound();

            return Ok(ticket);
        }

        // GET /api/tickets/{ticketId}/attachments/{attachmentId}/download-url — get signed download URL for an attachment
        [HttpGet("{ticketId}/attachments/{attachmentId}/download-url")]
        public async Task<IActionResult> GetDownloadUrl(long ticketId, long attachmentId)
        {
            // Izinkan semua role yang sudah login
            var attachment = await _db.TicketAttachments
                .FirstOrDefaultAsync(a => a.Id == attachmentId && a.TicketId == ticketId);

            if (attachment == null) return NotFound();

            var supabaseUrl = _config["Supabase:Url"];
            var serviceKey = _config["Supabase:ServiceKey"];
            var httpClient = _httpClientFactory.CreateClient();

            var filePath = attachment.FileUrl!.Replace($"{supabaseUrl}/storage/v1/object/public/ticket-attachment/", "");

            var requestUrl = $"{supabaseUrl}/storage/v1/object/sign/ticket-attachment/{filePath}";
            var requestBody = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(new { expiresIn = 300 }),
                System.Text.Encoding.UTF8,
                "application/json"
            );

            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            request.Headers.Add("Authorization", $"Bearer {serviceKey}");
            request.Content = requestBody;

            var response = await httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            var json = System.Text.Json.JsonDocument.Parse(responseBody);
            var signedUrl = json.RootElement.GetProperty("signedURL").GetString();

            return Ok(new { signedUrl = $"{supabaseUrl}/storage/v1{signedUrl}" });
        }

        // GET /api/tickets/all — agent/admin gets ALL tickets
        [HttpGet("all")]
        public async Task<IActionResult> GetAllTickets()
        {
            var role = await GetCurrentUserRole();
            if (role != "cs_agent" && role != "admin") return Forbid();

            var tickets = await _db.Tickets
                .Include(t => t.Priority)
                .Include(t => t.Customer)
                .Include(t => t.Agent)
                .Include(t => t.Attachments)   // ← tambah ini
                .OrderByDescending(t => t.CreatedAt)
                .AsNoTracking()
                .Select(t => new
                {
                    id          = t.Id,
                    subject     = t.Subject,
                    description = t.Description,
                    status      = t.Status,
                    priority    = t.Priority != null ? t.Priority.PriorityName : null,
                    customer    = t.Customer != null ? t.Customer.Name : null,
                    solver      = t.Agent != null ? t.Agent.Name : null,
                    createdAt   = t.CreatedAt,
                    attachments = t.Attachments.Select(a => new   // ← tambah ini
                    {
                        id         = a.Id,
                        fileName   = a.FileName,
                        fileSize   = a.FileSize,
                        uploadedAt = a.UploadedAt,
                    }).ToList(),
                })
                .ToListAsync();

            return Ok(tickets);
        }

        // PUT /api/tickets/{id} — agent updates a ticket (status, solver/agent)
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateTicket(long id, [FromBody] UpdateTicketRequest request)
        {
            var role = await GetCurrentUserRole();
            if (role != "cs_agent" && role != "admin") return Forbid();

            var ticket = await _db.Tickets.FindAsync(id);
            if (ticket == null) return NotFound("Ticket not found");

            if (request.Status != null)
                ticket.Status = request.Status;

            if (request.ResolvedAt.HasValue)
                ticket.ResolvedAt = request.ResolvedAt.Value;

            // ← FIX: simpan AgentId dari JWT jika takeAction (agentId dikirim sebagai true/flag)
            if (request.AgentId.HasValue)
                ticket.AgentId = request.AgentId.Value;

            await _db.SaveChangesAsync();

            // Reload agent name untuk dikembalikan ke frontend
            await _db.Entry(ticket).Reference(t => t.Agent).LoadAsync();

            return Ok(new
            {
                id         = ticket.Id,
                status     = ticket.Status,
                solver     = ticket.Agent != null ? ticket.Agent.Name : null,
                resolvedAt = ticket.ResolvedAt,
                resolved_at = ticket.ResolvedAt,
            });
        }

        // DELETE /api/tickets/{id} — agent/admin deletes a ticket
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTicket(long id)
        {
            var role = await GetCurrentUserRole();
            if (role != "cs_agent" && role != "admin") return Forbid();

            var ticket = await _db.Tickets.FindAsync(id);
            if (ticket == null) return NotFound("Ticket not found");

            _db.Tickets.Remove(ticket);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Ticket deleted successfully" });
        }

        // POST /api/tickets/upload-url — generate signed upload URLs (customer & agent)
        [HttpPost("upload-url")]
        public async Task<IActionResult> GetUploadUrls([FromBody] UploadUrlRequest request)
        {
            var role = await GetCurrentUserRole();

            // ← FIX: izinkan customer DAN agent
            if (role != "customer" && role != "cs_agent" && role != "admin")
                return Forbid();

            if (request.Files == null || request.Files.Count == 0)
                return BadRequest("Files are required");

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            // Validasi kepemilikan/akses ticket sesuai role
            bool hasAccess;
            if (role == "customer")
            {
                hasAccess = await _db.Tickets
                    .AsNoTracking()
                    .AnyAsync(t => t.Id == request.TicketId && t.CustomerId == userId);
            }
            else // cs_agent atau admin
            {
                hasAccess = await _db.Tickets
                    .AsNoTracking()
                    .AnyAsync(t => t.Id == request.TicketId);
            }

            if (!hasAccess)
                return NotFound("Ticket not found or access denied");

            var supabaseUrl = _config["Supabase:Url"];
            var serviceKey  = _config["Supabase:ServiceKey"];
            var httpClient  = _httpClientFactory.CreateClient();
            var results     = new List<object>();

            foreach (var file in request.Files)
            {
                var filePath   = $"{request.TicketId}/{Guid.NewGuid()}_{file.FileName}";
                var requestUrl = $"{supabaseUrl}/storage/v1/object/upload/sign/ticket-attachment/{filePath}";

                var requestBody = new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(new { expiresIn = 60 }),
                    System.Text.Encoding.UTF8,
                    "application/json"
                );

                var req = new HttpRequestMessage(HttpMethod.Post, requestUrl);
                req.Headers.Add("Authorization", $"Bearer {serviceKey}");
                req.Content = requestBody;

                var response     = await httpClient.SendAsync(req);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode, new { message = "Failed to generate upload URL", detail = responseBody });

                var json = System.Text.Json.JsonDocument.Parse(responseBody);
                if (!json.RootElement.TryGetProperty("url",   out var urlElement) ||
                    !json.RootElement.TryGetProperty("token", out var tokenElement))
                    return StatusCode(502, new { message = "Invalid response from storage service" });

                var signedUrl = urlElement.GetString();
                var token     = tokenElement.GetString();

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

            var priorityId = 2;
            var slaDeadline = DateTime.UtcNow.AddHours(24);

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

        // POST /api/tickets/{ticketId}/attachments — save attachment metadata after upload
        [HttpPost("{ticketId}/attachments")]
        public async Task<IActionResult> SaveAttachments(long ticketId, [FromBody] List<AttachmentInfo> attachments)
        {
            var role = await GetCurrentUserRole();

            // ← FIX: izinkan customer DAN agent
            if (role != "customer" && role != "cs_agent" && role != "admin")
                return Forbid();

            if (attachments == null || attachments.Count == 0)
                return BadRequest("Attachments are required");

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            // Validasi akses sesuai role
            bool hasAccess;
            if (role == "customer")
            {
                hasAccess = await _db.Tickets
                    .AsNoTracking()
                    .AnyAsync(t => t.Id == ticketId && t.CustomerId == userId);
            }
            else
            {
                hasAccess = await _db.Tickets
                    .AsNoTracking()
                    .AnyAsync(t => t.Id == ticketId);
            }

            if (!hasAccess)
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

        // POST /api/tickets/{id}/take-action — agent mengambil ticket untuk dirinya sendiri
        [HttpPost("{id}/take-action")]
        public async Task<IActionResult> TakeAction(long id)
        {
            var role = await GetCurrentUserRole();
            if (role != "cs_agent" && role != "admin") return Forbid();

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var ticket = await _db.Tickets.FindAsync(id);
            if (ticket == null) return NotFound("Ticket not found");

            ticket.AgentId = userId;
            ticket.Status  = "Progress";

            await _db.SaveChangesAsync();
            await _db.Entry(ticket).Reference(t => t.Agent).LoadAsync();

            return Ok(new
            {
                id     = ticket.Id,
                status = ticket.Status,
                solver = ticket.Agent != null ? ticket.Agent.Name : null,
            });
        }

        // POST /api/tickets/{ticketId}/comments
        [HttpPost("{ticketId}/comments")]
        public async Task<IActionResult> CreateComment(long ticketId, [FromBody] CreateTicketCommentRequest request)
        {
            var role = await GetCurrentUserRole();
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            if (string.IsNullOrWhiteSpace(request.Message))
                return BadRequest("Message is required");

            var ticket = await _db.Tickets
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == ticketId);

            if (ticket == null) return NotFound("Ticket not found");
            if (role == "customer" && ticket.CustomerId != userId) return Forbid();
            if ((role == "cs_agent" || role == "technician") && ticket.AgentId != userId) return Forbid();

            var comment = new TicketComment
            {
                TicketId  = ticketId,
                SenderId  = userId,
                Message   = request.Message.Trim(),
                CreatedAt = DateTime.UtcNow,
            };

            _db.TicketComments.Add(comment);
            await _db.SaveChangesAsync();

            return Created($"/api/tickets/{ticketId}/comments/{comment.Id}", new
            {
                id         = comment.Id,
                ticketId   = comment.TicketId,
                senderId   = comment.SenderId,
                senderName = (string?)null, // sender baru, belum di-load
                senderRole = role,          // ← pakai role dari JWT langsung
                message    = comment.Message,
                createdAt  = comment.CreatedAt,
            });
        }

        // GET /api/tickets/{ticketId}/comments
        [HttpGet("{ticketId}/comments")]
        public async Task<IActionResult> GetComments(long ticketId)
        {
            var role = await GetCurrentUserRole();
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var ticket = await _db.Tickets
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == ticketId);

            if (ticket == null) return NotFound("Ticket not found");
            if (role == "customer" && ticket.CustomerId != userId) return Forbid();
            if ((role == "cs_agent" || role == "technician") && ticket.AgentId != userId) return Forbid();

            var comments = await _db.TicketComments
                .Include(c => c.Sender)
                .Where(c => c.TicketId == ticketId)
                .OrderBy(c => c.CreatedAt)
                .AsNoTracking()
                .Select(c => new
                {
                    id         = c.Id,
                    ticketId   = c.TicketId,
                    senderId   = c.SenderId,
                    senderName = c.Sender != null ? c.Sender.Name : null,
                    message    = c.Message,
                    createdAt  = c.CreatedAt,
                })
                .ToListAsync();

            return Ok(comments);
        }

        // ── Request models ────────────────────────────────────────────────────

        public class UpdateTicketRequest
        {
            public string?   Status     { get; set; }
            public Guid?     AgentId    { get; set; }   // ← tambahkan ini
            [JsonPropertyName("resolvedAt")]
            public DateTime? ResolvedAt { get; set; }
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
        }

        public class UploadUrlRequest
        {
            public long TicketId { get; set; }
            public List<FileRequest> Files { get; set; } = new();
        }

        public class CreateTicketCommentRequest
        {
            public string? Message { get; set; }
        }
    }
}