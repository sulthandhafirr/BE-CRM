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

        private static string? TryGetOptionalString(string? json, string propertyName)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(propertyName, out var prop) ? prop.GetString() : null;
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
        public async Task<string> GetUsersAsync(string? role, int? companyId)
        {
            var query = _db.Profiles.AsNoTracking().Where(p => p.CompanyId == companyId);

            if (!string.IsNullOrEmpty(role))
                query = query.Where(p => p.Role!.RoleName == role);

            var users = await query
                .Select(p => new { p.Name, p.Position, RoleName = p.Role!.RoleName })
                .ToListAsync();

            return JsonSerializer.Serialize(users);
        }
        public async Task<string> GetCustomerTierAsync(string? customerName, Guid userId, string? role, int? companyId)
        {
            var isStaff = role is "cs_agent" or "technician" or "admin" or "ultrauser";

            if (isStaff)
            {
                if (string.IsNullOrWhiteSpace(customerName))
                    return JsonSerializer.Serialize(new { error = "Please provide the customer's name" });

                var matches = await _db.Profiles
                    .AsNoTracking()
                    .Where(p => p.CompanyId == companyId && p.Role!.RoleName == "customer" && p.Name!.ToLower().Contains(customerName.ToLower()))
                    .Select(p => new
                    {
                        p.Name,
                        TierName = p.ProfileTiers.Select(pt => pt.Tier!.TierName).FirstOrDefault()
                    })
                    .ToListAsync();

                if (matches.Count == 0)
                    return JsonSerializer.Serialize(new { error = "Customer not found" });

                if (matches.Count > 1)
                    return JsonSerializer.Serialize(new { error = "Multiple customers found with that name, please provide more detail" });

                return JsonSerializer.Serialize(matches[0]);
            }

            var ownProfile = await _db.Profiles
                .AsNoTracking()
                .Where(p => p.Id == userId)
                .Select(p => new
                {
                    p.Name,
                    TierName = p.ProfileTiers.Select(pt => pt.Tier!.TierName).FirstOrDefault()
                })
                .FirstOrDefaultAsync();

            if (ownProfile == null)
                return JsonSerializer.Serialize(new { error = "Profile not found" });

            var askingAboutSelf = string.IsNullOrWhiteSpace(customerName)
                || string.Equals(customerName.Trim(), ownProfile.Name, StringComparison.OrdinalIgnoreCase);

            if (!askingAboutSelf)
                return JsonSerializer.Serialize(new { error = "Sorry, that's personal information I can't share. I can only tell you your own tier." });

            return JsonSerializer.Serialize(ownProfile);
        }
        public async Task<string> ExecuteToolAsync(string funcName, string? funcArgsJson, Guid userId, string? role, int? companyId)
        {
            return funcName switch
            {
                "get_ticket_status" => await GetTicketStatusAsync(
                    JsonDocument.Parse(funcArgsJson!).RootElement.GetProperty("ticket_id").GetString() ?? "", companyId),
                "get_tickets" => await GetTicketsAsync(
                    JsonDocument.Parse(funcArgsJson!).RootElement.GetProperty("scope").GetString() ?? "mine", userId, role, companyId),
                "get_users" => await GetUsersAsync(TryGetOptionalString(funcArgsJson, "role"), companyId),
                "get_customer_tier" => await GetCustomerTierAsync(TryGetOptionalString(funcArgsJson, "customer_name"), userId, role, companyId),

                // Unknown tool
                _ => JsonSerializer.Serialize(new { error = "Unknown tool" })
            };
        }
    }
}