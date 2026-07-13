using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using CRM.Api.Data;

namespace CRM.Api.Services
{
    public class ChatToolService
    {
        private readonly AppDbContext _db;

        public ChatToolService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<string?> GetUserContextAsync(Guid userId, string? role)
        {
            var profile = await _db.Profiles
                .AsNoTracking()
                .Where(p => p.Id == userId)
                .Select(p => new
                {
                    p.Name,
                    p.Position,
                    CompanyName = p.Company!.CompanyName
                })
                .FirstOrDefaultAsync();

            if (profile == null) return null;

            var roleDisplay = role switch
            {
                "cs_agent" => "a Customer Service Agent",
                "technician" => "a Technician",
                "customer" => "a Customer",
                "admin" => "an Admin",
                "ultrauser" => "an Ultra User",
                _ => role
            };

            var positionText = !string.IsNullOrWhiteSpace(profile.Position)
                ? $" ({profile.Position})"
                : "";

            return $"You are talking to {profile.Name}, who is {roleDisplay}{positionText} at {profile.CompanyName}.";
        }

        public async Task<string> GetTicketStatusAsync(string ticketId, int? companyId)
        {
            if (!long.TryParse(ticketId, out var id))
                return JsonSerializer.Serialize(new { error = "Invalid ticket ID format" });

            var ticket = await _db.Tickets
                .AsNoTracking()
                .Where(t => t.Id == id && t.Customer!.CompanyId == companyId)
                .Select(t => new
                {
                    t.Id,
                    t.Subject,
                    t.Status,
                    Priority = t.Priority!.PriorityName,
                    t.SlaDeadline,
                    t.SlaBreached,
                    t.CreatedAt,
                    t.ResolvedAt,
                    AssignedAgentName = t.Agent != null ? t.Agent.Name : null
                })
                .FirstOrDefaultAsync();

            if (ticket == null)
                return JsonSerializer.Serialize(new { error = "Ticket not found" });

            return JsonSerializer.Serialize(ticket);
        }

        public async Task<string> GetTicketsAsync(string scope, Guid userId, string? role, int? companyId)
        {
            var query = _db.Tickets.AsNoTracking().Where(t => t.Customer!.CompanyId == companyId);

            query = scope switch
            {
                "mine" => role switch
                {
                    "customer" => query.Where(t => t.CustomerId == userId),
                    "cs_agent" => query.Where(t => t.AgentId == userId),
                    "technician" => query.Where(t => t.TechnicianId == userId),
                    _ => query.Where(t => t.CustomerId == userId)
                },
                "unassigned" => query.Where(t => t.AgentId == null && t.Status == "Waiting"),
                "others" => query.Where(t => t.AgentId != null && t.AgentId != userId),
                "all" => query,
                _ => query.Where(t => t.CustomerId == userId)
            };

            var tickets = await query
                .OrderByDescending(t => t.CreatedAt)
                .Take(10)
                .Select(t => new
                {
                    t.Id,
                    t.Subject,
                    t.Status,
                    Priority = role == "customer" ? null : t.Priority!.PriorityName,
                    t.CreatedAt,
                    AssignedAgentName = t.Agent != null ? t.Agent.Name : null
                })
                .ToListAsync();

            return JsonSerializer.Serialize(tickets);
        }
        public async Task<string> ExecuteToolAsync(string funcName, string? funcArgsJson, Guid userId, string? role, int? companyId)
        {
            return funcName switch
            {
                "get_ticket_status" => await GetTicketStatusAsync(
                    JsonDocument.Parse(funcArgsJson!).RootElement.GetProperty("ticket_id").GetString() ?? "", companyId),
                "get_tickets" => await GetTicketsAsync(
                    JsonDocument.Parse(funcArgsJson!).RootElement.GetProperty("scope").GetString() ?? "mine", userId, role, companyId),
                _ => JsonSerializer.Serialize(new { error = "Unknown tool" })
            };
        }
    }
}