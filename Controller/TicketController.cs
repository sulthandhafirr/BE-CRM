using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
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
        private readonly EmailService _emailService;
        private readonly NotificationService _notificationService;
        // private readonly SentimentAnalysisService _sentimentAnalysisService;
        private readonly IntentAnalysisService _intentAnalysisService;
        private readonly UrgencyAnalysisService _urgencyAnalysisService;
        private readonly PriorityEngineService _priorityEngineService;
        private readonly DuplicateDetectionService _duplicateDetectionService;

        public TicketController(
            AppDbContext db,
            RoleService roleService,
            EmailService emailService,
            NotificationService notificationService,
            // SentimentAnalysisService sentimentAnalysisService,
            IntentAnalysisService intentAnalysisService,
            UrgencyAnalysisService urgencyAnalysisService,
            PriorityEngineService priorityEngineService,
            DuplicateDetectionService duplicateDetectionService,   // ← tambahan
            IConfiguration config,
            IHttpClientFactory httpClientFactory)
            : base(roleService)
        {
            _db = db;
            _config = config;
            _httpClientFactory = httpClientFactory;
            _emailService = emailService;
            _notificationService = notificationService;
            // _sentimentAnalysisService = sentimentAnalysisService;
            _intentAnalysisService = intentAnalysisService;
            _urgencyAnalysisService = urgencyAnalysisService;
            _priorityEngineService = priorityEngineService;
            _duplicateDetectionService = duplicateDetectionService;   // ← tambahan
        }

        // GET /api/tickets — customer gets their own tickets
        [HttpGet]
        public async Task<IActionResult> GetMyTickets()
        {
            var role = await GetCurrentUserRole();
            if (role != "customer" && role != "technician") return Forbid();

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var query = _db.Tickets
                .Include(t => t.Priority)
                .Include(t => t.Agent)       // cs_agent
                .Include(t => t.Technician)  // technician
                .AsNoTracking()
                .Where(t => t.Status != "Solved");

            if (role == "customer")
                query = query.Where(t => t.CustomerId == userId);
            else
                query = query.Where(t => t.TechnicianId == userId);

            var tickets = await query
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new
                {
                    id = t.Id,
                    subject = t.Subject,
                    customer = t.Customer != null ? t.Customer.Name : null,
                    description = t.Description,
                    status = t.Status,
                    handler = t.Agent != null ? t.Agent.Name : "Not assigned yet",
                    technician = t.Technician != null ? t.Technician.Name : "-",
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

        // GET /api/tickets/my-solved — agent and technician gets their own solved tickets (performance)
        [HttpGet("my-solved")]
        public async Task<IActionResult> GetMySolvedTickets()
        {
            var (role, companyId) = await GetCurrentUserRoleAndCompany();
            if (role != "cs_agent" && role != "technician") return Forbid();

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var query = _db.Tickets
                .Include(t => t.Priority)
                .Include(t => t.Customer)
                // .Include(t => t.Agent)
                // .Include(t => t.Technician)
                .AsNoTracking()
                .Where(t => t.Status == "Solved" && t.Customer!.CompanyId == companyId);

            if (role == "cs_agent")
                query = query.Where(t => t.AgentId == userId);
            else
                query = query.Where(t => t.TechnicianId == userId);

            var tickets = await query
                // .Include(t => t.Priority)
                // .Include(t => t.Customer)
                // .Where(t => t.AgentId == userId && t.Status == "Solved")
                .OrderByDescending(t => t.ResolvedAt)
                // .AsNoTracking()
                .Select(t => new
                {
                    id = t.Id,
                    subject = t.Subject,
                    description = t.Description,
                    status = t.Status,
                    priority = t.Priority != null ? t.Priority.PriorityName : null,
                    customer = t.Customer != null ? t.Customer.Name : null,
                    createdAt = t.CreatedAt,
                    resolvedAt = t.ResolvedAt,
                    responseTimeSec = t.ResponseTimeSec,
                    resolutionTimeSec = t.ResolutionTimeSec
                })
                .ToListAsync();

            return Ok(tickets);
        }

        // GET /api/tickets/{id} — get ticket detiails (customer) maybe other roles
        [HttpGet("{id}")]
        public async Task<IActionResult> GetTicketById(long id)
        {
            var (role, companyId) = await GetCurrentUserRoleAndCompany();
            if (role != "customer" && role != "technician" && role != "cs_agent" && role != "admin")
                return Forbid();

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var query = _db.Tickets
                .Include(t => t.Priority)
                .Include(t => t.Customer)
                .Include(t => t.Agent)
                .Include(t => t.Attachments)
                .Include(t => t.Intent)
                .Where(t => t.Id == id)
                .AsNoTracking();

            // Filter berdasarkan role
            if (role == "customer")
                query = query.Where(t => t.CustomerId == userId);
            else if (role == "technician")
                query = query.Where(t => t.TechnicianId == userId);
            else // cs_agent & admin — bisa akses semua ticket dalam company
                query = query.Where(t => t.Customer!.CompanyId == companyId);

            var ticket = await query
                .Select(t => new
                {
                    id = t.Id,
                    subject = t.Subject,
                    description = t.Description,
                    status = t.Status,
                    priority = t.Priority != null ? t.Priority.PriorityName : null,
                    customer = t.Customer != null ? t.Customer.Name : null,
                    customerId = t.CustomerId,
                    solver = t.Agent != null ? t.Agent.Name : null,
                    technician = t.Technician != null ? t.Technician.Name : null,
                    handler = t.Agent != null ? t.Agent.Name : "Not assigned yet",
                    createdAt = t.CreatedAt,
                    resolvedAt = t.ResolvedAt,
                    solverId = t.Agent != null ? (Guid?)t.Agent.Id : null,
                    intent = t.Intent != null ? t.Intent.IntentName : null,
                    intentConfidence = t.IntentConfidence,
                    urgency = t.Urgency,
                    urgencyConfidence = t.UrgencyConfidence,
                    slaDeadline = t.SlaDeadline,
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

            var filePath = ExtractStorageFilePath(attachment.FileUrl, supabaseUrl);
            if (string.IsNullOrWhiteSpace(filePath))
                return BadRequest(new { message = "Attachment storage path is invalid" });

            var requestUrl = $"{supabaseUrl}/storage/v1/object/sign/ticket-attachment/{filePath}";
            var requestBody = new StringContent(
                JsonSerializer.Serialize(new { expiresIn = 300 }),
                System.Text.Encoding.UTF8,
                "application/json"
            );

            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            request.Headers.Add("Authorization", $"Bearer {serviceKey}");
            request.Content = requestBody;

            var response = await httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return StatusCode((int)response.StatusCode, new
                {
                    message = "Failed to generate download URL",
                    detail = responseBody
                });
            }

            using var json = JsonDocument.Parse(responseBody);
            var signedUrl = TryGetSignedUrl(json.RootElement);
            if (string.IsNullOrWhiteSpace(signedUrl))
            {
                return StatusCode(502, new
                {
                    message = "Invalid response from storage service",
                    detail = responseBody
                });
            }

            var normalizedSignedUrl = signedUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                      signedUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? signedUrl
                : $"{supabaseUrl}/storage/v1{signedUrl}";

            return Ok(new { signedUrl = normalizedSignedUrl });
        }

        // GET /api/tickets/all — agent/admin gets ALL tickets
        [HttpGet("all")]
        public async Task<IActionResult> GetAllTickets()
        {
            var (role, companyId) = await GetCurrentUserRoleAndCompany();
            if (role != "cs_agent" && role != "admin") return Forbid();

            var tickets = await _db.Tickets
                .Include(t => t.Priority)
                .Include(t => t.Customer)
                .Include(t => t.Agent)
                .Include(t => t.Attachments)
                .Include(t => t.Intent)
                .Where(t => t.Customer!.CompanyId == companyId)
                .OrderByDescending(t => t.CreatedAt)
                .AsNoTracking()
                .Select(t => new
                {
                    id = t.Id,
                    subject = t.Subject,
                    description = t.Description,
                    status = t.Status,
                    priority = t.Priority != null ? t.Priority.PriorityName : null,
                    customer = t.Customer != null ? t.Customer.Name : null,
                    solver = t.Agent != null ? t.Agent.Name : null,
                    technician = t.Technician != null ? t.Technician.Name : null,
                    createdAt = t.CreatedAt,
                    intent = t.Intent != null ? t.Intent.IntentName : null,
                    intentConfidence = t.IntentConfidence,
                    urgency = t.Urgency,
                    urgencyConfidence = t.UrgencyConfidence,
                    attachments = t.Attachments.Select(a => new
                    {
                        id = a.Id,
                        fileName = a.FileName,
                        fileSize = a.FileSize,
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
            var (role, companyId) = await GetCurrentUserRoleAndCompany();
            if (role != "cs_agent" && role != "admin") return Forbid();

            var ticket = await _db.Tickets
                .Include(t => t.Customer)
                .FirstOrDefaultAsync(t => t.Id == id && t.Customer!.CompanyId == companyId);
            if (ticket == null) return NotFound("Ticket not found");

            if (request.Status != null)
                ticket.Status = request.Status;

            if (request.ResolvedAt.HasValue)
                ticket.ResolvedAt = request.ResolvedAt.Value;

            if (request.AgentId.HasValue)
                ticket.AgentId = request.AgentId.Value;

            if (request.TechnicianId.HasValue)
                ticket.TechnicianId = request.TechnicianId.Value;

            // ── NEW: update priority by name ──
            if (request.Priority != null)
            {
                var priority = await _db.Priorities   // ← ganti dari _db.PriorityLists
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.PriorityName == request.Priority);
                if (priority != null)
                    ticket.PriorityId = priority.Id;
            }

            // ── NEW: update solver (agent) by name ──
            if (request.Solver != null)
            {
                if (request.Solver == "Unassigned" || request.Solver == "")
                {
                    ticket.AgentId = null;
                }
                else
                {
                    var agent = await _db.Profiles
                        .AsNoTracking()
                        .FirstOrDefaultAsync(p => p.Name == request.Solver && p.RoleId == 2);
                    if (agent != null)
                        ticket.AgentId = agent.Id;
                }
            }

            // ── NEW: update technician by name ──
            if (request.Technician != null)
            {
                if (request.Technician == "Unassigned" || request.Technician == "")
                {
                    ticket.TechnicianId = null;
                }
                else
                {
                    var tech = await _db.Profiles
                        .AsNoTracking()
                        .FirstOrDefaultAsync(p => p.Name == request.Technician && p.RoleId == 3);
                    if (tech != null)
                        ticket.TechnicianId = tech.Id;
                }
            }

            // Count ResolutionTime if ticket status change to "Solved"
            if (request.Status == "Solved" && ticket.ResolutionTimeSec == null)
            {
                var resolvedAt = request.ResolvedAt ?? DateTime.UtcNow;
                ticket.ResolvedAt = resolvedAt;
                ticket.ResolutionTimeSec = (int)(resolvedAt - ticket.CreatedAt).TotalSeconds;
            }

            await _db.SaveChangesAsync();

            await _db.Entry(ticket).Reference(t => t.Agent).LoadAsync();
            await _db.Entry(ticket).Reference(t => t.Technician).LoadAsync();
            await _db.Entry(ticket).Reference(t => t.Priority).LoadAsync();

            // notfication
            var customer = await _db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == ticket.CustomerId);

            if (request.Status == "Solved")
            {
                if (customer?.Email != null)
                    _ = _emailService.SendTicketResolvedAsync(customer.Email, customer.Name ?? "Customer", ticket.Subject ?? "Your Ticket", ticket.Id);

                if (ticket.TechnicianId.HasValue)
                {
                    var assignedTechnician = await _db.Profiles.AsNoTracking()
                        .FirstOrDefaultAsync(p => p.Id == ticket.TechnicianId.Value);

                    if (assignedTechnician?.Email != null)
                        _ = _emailService.SendTechnicianResolvedAsync(assignedTechnician.Email, assignedTechnician.Name ?? "Technician", ticket.Subject ?? "Your Ticket", ticket.Id);
                }

                await _notificationService.CreateAsync(ticket.CustomerId, $"Your ticket #{ticket.Id} '{ticket.Subject}' has been resolved.");
            }

            // technician assigned
            if (request.TechnicianId.HasValue)
            {
                if (customer?.Email != null)
                    _ = _emailService.SendTechnicianAssignedAsync(customer.Email, customer.Name ?? "Customer", ticket.Subject ?? "Your Ticket", ticket.Id);

                var assignedTechnician = await _db.Profiles.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == request.TechnicianId.Value);

                if (assignedTechnician?.Email != null)
                    _ = _emailService.SendAssignedToTechnicianAsync(assignedTechnician.Email, assignedTechnician.Name ?? "Technician", ticket.Subject ?? "Your Ticket", ticket.Id);

                await _notificationService.CreateAsync(ticket.CustomerId, $"A technician has been assigned to your ticket #{ticket.Id} '{ticket.Subject}'.");
                await _notificationService.CreateAsync(request.TechnicianId.Value, $"You have been assigned to ticket #{ticket.Id} '{ticket.Subject}'.");
            }

            if (request.Technician != null && request.Technician != "Unassigned" && request.Technician != "" && ticket.TechnicianId.HasValue)
            {
                await _notificationService.CreateAsync(ticket.TechnicianId.Value, $"You have been assigned to ticket #{ticket.Id} '{ticket.Subject}'.");
            }

            return Ok(new
            {
                id = ticket.Id,
                status = ticket.Status,
                priority = ticket.Priority != null ? ticket.Priority.PriorityName : null,
                solver = ticket.Agent != null ? ticket.Agent.Name : null,
                technician = ticket.Technician != null ? ticket.Technician.Name : null,
                resolvedAt = ticket.ResolvedAt,
                resolved_at = ticket.ResolvedAt,
            });
        }

        // DELETE /api/tickets/{id} — agent/admin deletes a ticket
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTicket(long id)
        {
            var (role, companyId) = await GetCurrentUserRoleAndCompany();
            if (role != "cs_agent" && role != "admin") return Forbid();

            var ticket = await _db.Tickets
                .Include(t => t.Customer)
                .FirstOrDefaultAsync(t => t.Id == id && t.Customer!.CompanyId == companyId);
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

            if (role != "customer" && role != "cs_agent" && role != "admin" && role != "technician")
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
            else if (role == "technician")
            {
                hasAccess = await _db.Tickets
                    .AsNoTracking()
                    .AnyAsync(t => t.Id == request.TicketId && t.TechnicianId == userId);
            }
            else
            {
                hasAccess = await _db.Tickets
                    .AsNoTracking()
                    .AnyAsync(t => t.Id == request.TicketId);
            }

            if (!hasAccess)
                return NotFound("Ticket not found or access denied");

            var supabaseUrl = _config["Supabase:Url"];
            var serviceKey = _config["Supabase:ServiceKey"];
            var httpClient = _httpClientFactory.CreateClient();
            var results = new List<object>();

            foreach (var file in request.Files)
            {
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
            var (role, companyId) = await GetCurrentUserRoleAndCompany();
            if (role != "customer") return Forbid();
            if (companyId is null) return Unauthorized("User is not associated with a company.");

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var ticketText = string.Join("\n\n", new[] { request.Subject, request.Description }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim()));

            // ── Intent Analysis ──
            var intentResult = await _intentAnalysisService.AnalyzeAsync(ticketText);

            // ── Urgency Analysis ──
            var urgencyResult = await _urgencyAnalysisService.AnalyzeAsync(ticketText);

            // ── Business Rules Engine ──
            var priorityResult = await _priorityEngineService.ResolvePriorityAsync(
                description: request.Description,
                customerId: userId,
                companyId: companyId.Value,
                intent: intentResult.Intent,
                intentConfidence: intentResult.Confidence,
                urgency: urgencyResult.Urgency,
                urgencyConfidence: urgencyResult.Confidence);

            var priority = await _db.Priorities
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PriorityName == priorityResult.PriorityName)
                ?? await _db.Priorities
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.PriorityName == "Normal");

            var slaDeadline = priorityResult.SlaResolutionHours.HasValue
                ? DateTime.UtcNow.AddHours(priorityResult.SlaResolutionHours.Value)
                : (DateTime?)null;

            if (slaDeadline.HasValue)
            {
                var company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId.Value);
                slaDeadline = CompanyTimeZoneHelper.AdjustForWorkingDays(slaDeadline.Value, company?.WorkingDays, company?.Timezone);
            }

            var priorityId = priority?.Id ?? 2;

            var intentEntity = await _db.Intents
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.IntentName == intentResult.Intent);

            var ticket = new Ticket
            {
                CustomerId = userId,
                Subject = request.Subject,
                Description = request.Description,
                Status = "Waiting",
                PriorityId = priorityId,
                IntentId = intentEntity?.Id,
                IntentConfidence = intentResult.Confidence,
                Urgency = urgencyResult.Urgency,
                UrgencyConfidence = urgencyResult.Confidence,
                UserChoosenPriorityId = request.UserPriorityId,
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
            if (role != "customer" && role != "cs_agent" && role != "admin" && role != "technician")
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
            else if (role == "technician")
            {
                hasAccess = await _db.Tickets
                    .AsNoTracking()
                    .AnyAsync(t => t.Id == ticketId && t.TechnicianId == userId);
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
            var (role, companyId) = await GetCurrentUserRoleAndCompany();
            if (role != "cs_agent" && role != "admin") return Forbid();

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var ticket = await _db.Tickets
                .Include(t => t.Customer)
                .FirstOrDefaultAsync(t => t.Id == id && t.Customer!.CompanyId == companyId);
            if (ticket == null) return NotFound("Ticket not found");

            ticket.AgentId = userId;
            ticket.Status = "Progress";

            // Hanya isi first_response_at jika belum pernah diisi sebelumnya
            if (ticket.FirstResponseAt == null)
            {
                ticket.FirstResponseAt = DateTime.UtcNow;
                ticket.ResponseTimeSec = (int)(ticket.FirstResponseAt.Value - ticket.CreatedAt).TotalSeconds; // Response time
            }

            await _db.SaveChangesAsync();
            await _db.Entry(ticket).Reference(t => t.Agent).LoadAsync();

            // email notfication
            var customer = await _db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == ticket.CustomerId);
            if (customer?.Email != null && customer?.Name != null)
                _ = _emailService.SendTicketAssignedAsync(customer.Email, customer.Name, ticket.Subject ?? "Your Ticket", ticket.Id);

            // web notification
            _ = _notificationService.CreateAsync(ticket.CustomerId, $"Your ticket #{ticket.Id} '{ticket.Subject}' has been assigned to an agent.");

            return Ok(new
            {
                id = ticket.Id,
                status = ticket.Status,
                solver = ticket.Agent != null ? ticket.Agent.Name : null,
                firstResponseAt = ticket.FirstResponseAt,
            });
        }

        // GET /api/tickets/technicians — get all technicians (cs_agent & admin)
        [HttpGet("technicians")]
        public async Task<IActionResult> GetTechnicians()
        {
            var (role, companyId) = await GetCurrentUserRoleAndCompany();
            if (role != "cs_agent" && role != "admin") return Forbid();

            var technicians = await _db.Profiles
                .Where(p => p.RoleId == 3 && p.CompanyId == companyId)
                .AsNoTracking()
                .Select(p => new
                {
                    id = p.Id,
                    name = p.Name,
                    email = p.Email,
                    position = p.Position,
                    skills = p.ProfileSkills
                        .Select(ps => ps.Skill!.SkillName)
                        .ToList()
                })
                .ToListAsync();

            return Ok(technicians);
        }

        // POST /api/tickets/{ticketId}/comments
        [HttpPost("{ticketId}/comments")]
        public async Task<IActionResult> CreateComment(long ticketId, [FromBody] CreateTicketCommentRequest request)
        {
            var (role, companyId) = await GetCurrentUserRoleAndCompany();
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            if (string.IsNullOrWhiteSpace(request.Message))
                return BadRequest("Message is required");

            var ticket = await _db.Tickets
                .Include(t => t.Customer)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == ticketId && t.Customer!.CompanyId == companyId);

            if (ticket == null) return NotFound("Ticket not found");

            var isAuthorized = role switch
            {
                "customer" => ticket.CustomerId == userId,
                "cs_agent" => ticket.AgentId == userId,
                "technician" => ticket.TechnicianId == userId,
                "admin" => true,
                _ => false
            };

            if (!isAuthorized) return Forbid();

            var comment = new TicketComment
            {
                TicketId = ticketId,
                SenderId = userId,
                Message = request.Message.Trim(),
                CreatedAt = DateTime.UtcNow,
            };

            _db.TicketComments.Add(comment);
            await _db.SaveChangesAsync();

            // email notification
            if (role != "customer")
            {
                var customer = await _db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == ticket.CustomerId);
                var sender = await _db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == userId);
                var senderRole = role == "cs_agent" ? "agent" : "technician";
                if (customer?.Email != null)
                    _ = _emailService.SendNewMessageAsync(
                        customer.Email,
                        customer.Name ?? "Customer",
                        ticket.Subject ?? "Your Ticket",
                        ticket.Id,
                        sender?.Name ?? "Support Agent",
                        request.Message
                    );

                if (role == "technician" && ticket.AgentId.HasValue)
                {
                    var agent = await _db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == ticket.AgentId.Value);
                    if (agent?.Email != null)
                        _ = _emailService.SendNewMessageAsync(
                            agent.Email,
                            agent.Name ?? "Support Agent",
                            ticket.Subject ?? "Your Ticket",
                            ticket.Id,
                            sender?.Name ?? "Technician",
                            request.Message
                        );
                }

                if (role == "cs_agent" && ticket.TechnicianId.HasValue)
                {
                    var technician = await _db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == ticket.TechnicianId.Value);
                    if (technician?.Email != null)
                        _ = _emailService.SendNewMessageAsync(
                            technician.Email,
                            technician.Name ?? "Technician",
                            ticket.Subject ?? "Your Ticket",
                            ticket.Id,
                            sender?.Name ?? "Support Agent",
                            request.Message
                        );
                }

                // customer web notification
                await _notificationService.CreateAsync(ticket.CustomerId, $"New message from {senderRole} on your ticket #{ticket.Id} '{ticket.Subject}'.");

                // if technician comments notify agent
                if (role == "technician" && ticket.AgentId.HasValue)
                    await _notificationService.CreateAsync(ticket.AgentId.Value, $"New message from technician on ticket #{ticket.Id} '{ticket.Subject}'.");

                // if agent comments notify technician
                if (role == "cs_agent" && ticket.TechnicianId.HasValue)
                    await _notificationService.CreateAsync(ticket.TechnicianId.Value, $"New message from agent on ticket #{ticket.Id} '{ticket.Subject}'.");
            }
            else
            {
                if (ticket.AgentId.HasValue)
                {
                    var agent = await _db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == ticket.AgentId.Value);
                    if (agent?.Email != null)
                        _ = _emailService.SendNewMessageAsync(
                            agent.Email,
                            agent.Name ?? "Support Agent",
                            ticket.Subject ?? "Your Ticket",
                            ticket.Id,
                            ticket.Customer?.Name ?? "Customer",
                            request.Message
                        );
                }

                if (ticket.TechnicianId.HasValue)
                {
                    var technician = await _db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == ticket.TechnicianId.Value);
                    if (technician?.Email != null)
                        _ = _emailService.SendNewMessageAsync(
                            technician.Email,
                            technician.Name ?? "Technician",
                            ticket.Subject ?? "Your Ticket",
                            ticket.Id,
                            ticket.Customer?.Name ?? "Customer",
                            request.Message
                        );
                }

                // agent/technician gets notification when customer comments
                if (ticket.AgentId.HasValue)
                    await _notificationService.CreateAsync(ticket.AgentId.Value, $"New message from customer on ticket #{ticket.Id} '{ticket.Subject}'.");

                if (ticket.TechnicianId.HasValue)
                    await _notificationService.CreateAsync(ticket.TechnicianId.Value, $"New message from customer on ticket #{ticket.Id} '{ticket.Subject}'.");
            }

            return Created($"/api/tickets/{ticketId}/comments/{comment.Id}", new
            {
                id = comment.Id,
                ticketId = comment.TicketId,
                senderId = comment.SenderId,
                senderName = (string?)null, // sender baru, belum di-load
                senderRole = role,          // ← pakai role dari JWT langsung
                message = comment.Message,
                createdAt = comment.CreatedAt,
            });
        }

        // GET /api/tickets/{ticketId}/comments
        [HttpGet("{ticketId}/comments")]
        public async Task<IActionResult> GetComments(long ticketId)
        {
            var (role, companyId) = await GetCurrentUserRoleAndCompany();
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var ticket = await _db.Tickets
                .Include(t => t.Customer)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == ticketId && t.Customer!.CompanyId == companyId);

            if (ticket == null) return NotFound("Ticket not found");

            var isAuthorized = role switch
            {
                "customer" => ticket.CustomerId == userId,
                "cs_agent" => true,
                "technician" => ticket.TechnicianId == userId,
                "admin" => true,
                _ => false
            };

            if (!isAuthorized) return Forbid();

            var comments = await _db.TicketComments
                .Include(c => c.Sender)
                    .ThenInclude(s => s!.Role)
                .Where(c => c.TicketId == ticketId)
                .OrderBy(c => c.CreatedAt)
                .AsNoTracking()
                .Select(c => new
                {
                    id = c.Id,
                    ticketId = c.TicketId,
                    senderId = c.SenderId,
                    senderName = c.Sender != null ? c.Sender.Name : null,
                    senderRole = c.Sender != null && c.Sender.Role != null ? c.Sender.Role.RoleName : null,
                    message = c.Message,
                    createdAt = c.CreatedAt,
                })
                .ToListAsync();

            return Ok(comments);
        }

        // GET /api/tickets/{id}/similar — cari tiket yang kemungkinan duplikat berdasarkan makna
        [HttpGet("{id}/similar")]
        public async Task<IActionResult> GetSimilarTickets(long id, double threshold = 0.80)
        {
            var (role, companyId) = await GetCurrentUserRoleAndCompany();
            if (role != "cs_agent" && role != "admin") return Forbid();

            var source = await _db.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
            if (source == null) return NotFound("Ticket not found");

            var sourceText = source.Description ?? "";

            var candidateTickets = await _db.Tickets
                .Where(t => t.Id != id
                        && t.Customer!.CompanyId == companyId
                        && t.CustomerId == source.CustomerId   // ← hanya sesama customer yang sama
                        && t.Status != "Solved")
                .Select(t => new { t.Id, t.Description })
                .ToListAsync();

            var candidates = candidateTickets
                .Select(t => new DuplicateCandidate(t.Id, t.Description ?? ""))
                .ToList();

            var result = await _duplicateDetectionService.CheckDuplicatesAsync(sourceText, candidates, threshold);
            var scoreMap = result.Matches.ToDictionary(m => m.Id, m => m.Score);

            var tickets = await _db.Tickets
                .Where(t => scoreMap.Keys.Contains(t.Id))
                .Include(t => t.Priority)
                .Include(t => t.Customer)   // ← tambahan
                .Include(t => t.Intent)
                .Select(t => new
                {
                    id = t.Id,
                    subject = t.Subject,
                    description = t.Description,
                    status = t.Status,
                    priority = t.Priority != null ? t.Priority.PriorityName : null,
                    createdAt = t.CreatedAt,
                    customerName = t.Customer != null ? t.Customer.Name : null,   // ← tambahan
                    intent = t.Intent != null ? t.Intent.IntentName : null,
                })
                .ToListAsync();

            var ranked = tickets
                .Select(t => new { t.id, t.subject, t.description, t.status, t.priority, t.createdAt, t.customerName, t.intent, similarityScore = scoreMap[t.id] })
                .OrderByDescending(t => t.similarityScore)
                .ToList();

            return Ok(new { duplicates = ranked, serviceAvailable = !result.IsFallback });
        }

        // GET /api/tickets/duplicate-counts — badge count semua tiket sekaligus (1 batch call, bukan N call)
        [HttpGet("duplicate-counts")]
        public async Task<IActionResult> GetDuplicateCounts(double threshold = 0.75)
        {
            var (role, companyId) = await GetCurrentUserRoleAndCompany();
            if (role != "cs_agent" && role != "admin") return Forbid();

            var openTickets = await _db.Tickets
                .Where(t => t.Customer!.CompanyId == companyId && t.Status != "Solved")
                .Select(t => new { t.Id, t.Description, t.CustomerId })
                .ToListAsync();

            var result = new Dictionary<long, int>();

            // ── Kelompokkan per customer, hitung duplikat HANYA dalam kelompok yang sama ──
            var groupedByCustomer = openTickets.GroupBy(t => t.CustomerId);

            foreach (var group in groupedByCustomer)
            {
                if (group.Count() < 2) continue;   // 1 tiket saja pasti tidak ada duplikat

                var candidates = group
                    .Select(t => new DuplicateCandidate(t.Id, t.Description ?? ""))
                    .ToList();

                var groupCounts = await _duplicateDetectionService.GetDuplicateCountsAsync(candidates, threshold);

                foreach (var kvp in groupCounts)
                    result[kvp.Key] = kvp.Value;
            }

            return Ok(result);
        }

        // ── Request models ────────────────────────────────────────────────────

        private static string? ExtractStorageFilePath(string? fileUrl, string? supabaseUrl)
        {
            if (string.IsNullOrWhiteSpace(fileUrl) || string.IsNullOrWhiteSpace(supabaseUrl))
                return null;

            var publicPath = "/storage/v1/object/public/ticket-attachment/";
            var prefix = $"{supabaseUrl.TrimEnd('/')}{publicPath}";

            if (fileUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return fileUrl[prefix.Length..];

            var index = fileUrl.IndexOf(publicPath, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
                return fileUrl[(index + publicPath.Length)..];

            return null;
        }

        private static string? TryGetSignedUrl(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            if (root.TryGetProperty("signedURL", out var signedUrlElement) && signedUrlElement.ValueKind == JsonValueKind.String)
                return signedUrlElement.GetString();

            if (root.TryGetProperty("signedUrl", out var signedUrlElement2) && signedUrlElement2.ValueKind == JsonValueKind.String)
                return signedUrlElement2.GetString();

            if (root.TryGetProperty("url", out var urlElement) && urlElement.ValueKind == JsonValueKind.String)
                return urlElement.GetString();

            if (root.TryGetProperty("signed_url", out var signedUrlElement3) && signedUrlElement3.ValueKind == JsonValueKind.String)
                return signedUrlElement3.GetString();

            return null;
        }

        public class UpdateTicketRequest
        {
            public string? Status { get; set; }
            public Guid? AgentId { get; set; }

            [JsonPropertyName("technicianId")]
            public Guid? TechnicianId { get; set; }

            [JsonPropertyName("resolvedAt")]
            public DateTime? ResolvedAt { get; set; }

            // ── NEW fields ──
            [JsonPropertyName("priority")]
            public string? Priority { get; set; }

            [JsonPropertyName("solver")]
            public string? Solver { get; set; }

            [JsonPropertyName("technician")]
            public string? Technician { get; set; }
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
            public int? UserPriorityId { get; set; }
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