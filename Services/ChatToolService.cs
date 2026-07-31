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

        public async Task<string> GetTicketsAsync(string scope, Guid userId, string? role, int? companyId, string? startDate, string? endDate, string? status)
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

            if (DateTime.TryParse(startDate, out var start))
                query = query.Where(t => t.CreatedAt >= DateTime.SpecifyKind(start, DateTimeKind.Utc));
            if (DateTime.TryParse(endDate, out var end))
                query = query.Where(t => t.CreatedAt <= DateTime.SpecifyKind(end.AddDays(1).AddSeconds(-1), DateTimeKind.Utc));

            if (!string.IsNullOrEmpty(status))
                query = query.Where(t => t.Status == status);

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
        public async Task<string> GetTicketStatsAsync(string metric, string? period, Guid userId, string? role, int? companyId, string? startDate, string? endDate)
        {
            DateTime? start = null;
            DateTime? end = null;
            var today = DateTime.UtcNow.Date;

            switch (period)
            {
                case "today":
                    start = today;
                    break;
                case "yesterday":
                    start = today.AddDays(-1);
                    end = today.AddSeconds(-1);
                    break;
                case "this_week":
                    start = today.AddDays(-(int)today.DayOfWeek);
                    break;
                case "last_week":
                    var thisWeekStart = today.AddDays(-(int)today.DayOfWeek);
                    start = thisWeekStart.AddDays(-7);
                    end = thisWeekStart.AddSeconds(-1);
                    break;
                case "this_month":
                    start = DateTime.SpecifyKind(new DateTime(today.Year, today.Month, 1), DateTimeKind.Utc);
                    break;
                case "last_month":
                    var thisMonthStart = DateTime.SpecifyKind(new DateTime(today.Year, today.Month, 1), DateTimeKind.Utc);
                    start = thisMonthStart.AddMonths(-1);
                    end = thisMonthStart.AddSeconds(-1);
                    break;
                case "this_year":
                    start = DateTime.SpecifyKind(new DateTime(today.Year, 1, 1), DateTimeKind.Utc);
                    break;
                case "last_year":
                    var thisYearStart = DateTime.SpecifyKind(new DateTime(today.Year, 1, 1), DateTimeKind.Utc);
                    start = thisYearStart.AddYears(-1);
                    end = thisYearStart.AddSeconds(-1);
                    break;
            }

            if (DateTime.TryParse(startDate, out var customStart))
                start = DateTime.SpecifyKind(customStart, DateTimeKind.Utc);
            if (DateTime.TryParse(endDate, out var customEnd))
                end = DateTime.SpecifyKind(customEnd.AddDays(1).AddSeconds(-1), DateTimeKind.Utc);

            var query = _db.Tickets.AsNoTracking().AsQueryable();

            query = role switch
            {
                "customer" => query.Where(t => t.CustomerId == userId),
                "technician" => query.Where(t => t.TechnicianId == userId),
                _ => query.Where(t => t.Customer!.CompanyId == companyId)
            };

            if (start.HasValue)
                query = query.Where(t => t.CreatedAt >= start);
            if (end.HasValue)
                query = query.Where(t => t.CreatedAt <= end);

            switch (metric)
            {
                case "status_breakdown":
                    var statusCounts = await query.GroupBy(t => t.Status)
                        .Select(g => new { Status = g.Key, Count = g.Count() })
                        .ToListAsync();
                    return JsonSerializer.Serialize(statusCounts);

                case "priority_breakdown":
                    var priorityCounts = await query.Where(t => t.Priority != null)
                        .GroupBy(t => t.Priority!.PriorityName)
                        .Select(g => new { Priority = g.Key, Count = g.Count() })
                        .ToListAsync();
                    return JsonSerializer.Serialize(priorityCounts);

                case "solved_count":
                    var solved = await query.CountAsync(t => t.Status == "Solved");
                    return JsonSerializer.Serialize(new { solved });

                case "active_count":
                    var active = await query.CountAsync(t => t.Status != "Solved");
                    return JsonSerializer.Serialize(new { active });

                default:
                    return JsonSerializer.Serialize(new { error = "Unknown metric" });
            }
        }
        public async Task<string> ExecuteToolAsync(string funcName, string? funcArgsJson, Guid userId, string? role, int? companyId)
        {
            return funcName switch
            {
                "get_ticket_status" => await GetTicketStatusAsync(
                    JsonDocument.Parse(funcArgsJson!).RootElement.GetProperty("ticket_id").GetString() ?? "", companyId),
                "get_tickets" => await GetTicketsAsync(
                    JsonDocument.Parse(funcArgsJson!).RootElement.GetProperty("scope").GetString() ?? "mine",
                    userId, role, companyId,
                    TryGetOptionalString(funcArgsJson, "start_date"),
                    TryGetOptionalString(funcArgsJson, "end_date"),
                    TryGetOptionalString(funcArgsJson, "status")),
                "get_users" => await GetUsersAsync(TryGetOptionalString(funcArgsJson, "role"), companyId),
                "get_customer_tier" => await GetCustomerTierAsync(TryGetOptionalString(funcArgsJson, "customer_name"), userId, role, companyId),
                "get_ticket_stats" => await GetTicketStatsAsync(
                    JsonDocument.Parse(funcArgsJson!).RootElement.GetProperty("metric").GetString() ?? "",
                    TryGetOptionalString(funcArgsJson, "period"), userId, role, companyId,
                    TryGetOptionalString(funcArgsJson, "start_date"),
                    TryGetOptionalString(funcArgsJson, "end_date")),

                // Unknown tool
                _ => JsonSerializer.Serialize(new { error = "Unknown tool" })
            };
        }
    }
}