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
    [Route("api/dashboard")]
    public class DashboardController : BaseController
    {
        private readonly AppDbContext _db;

        public DashboardController(AppDbContext db, RoleService roleService)
            : base(roleService)
        {
            _db = db;
        }

        // GET /api/dashboard/stats — cs_agent, admin, customer
        [HttpGet("stats")]
        public async Task<IActionResult> GetStats([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
        {
            var (role, companyId) = await GetCurrentUserRoleAndCompany();

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var effectiveStartDate = startDate.HasValue
                ? DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc)
                : (DateTime?)null;
            var effectiveEndDate = endDate.HasValue
                ? DateTime.SpecifyKind(endDate.Value, DateTimeKind.Utc)
                : (DateTime?)null;

            var roleIds = await _db.Roles
                .Where(r => r.CompanyId == companyId)
                .ToDictionaryAsync(r => r.RoleName.ToLower(), r => r.Id);

            if (role == "customer")
            {

                var totalCsAgent = await _db.Profiles.AsNoTracking().CountAsync(p => p.RoleId == roleIds.GetValueOrDefault("cs_agent", 0) && p.CompanyId == companyId);
                var totalTechnician = await _db.Profiles.AsNoTracking().CountAsync(p => p.RoleId == roleIds.GetValueOrDefault("technician", 0) && p.CompanyId == companyId);
                var activeTicket = await _db.Tickets.AsNoTracking().CountAsync(t => t.CustomerId == userId && t.Status != "Solved"
                    && (!effectiveStartDate.HasValue || t.CreatedAt >= effectiveStartDate) && (!effectiveEndDate.HasValue || t.CreatedAt <= effectiveEndDate));
                var solvedTicket = await _db.Tickets.AsNoTracking().CountAsync(t => t.CustomerId == userId && t.Status == "Solved"
                    && (!effectiveStartDate.HasValue || t.CreatedAt >= effectiveStartDate) && (!effectiveEndDate.HasValue || t.CreatedAt <= effectiveEndDate));
                var totalMyTicket = activeTicket + solvedTicket;

                var customerStatusCounts = await _db.Tickets
                    .AsNoTracking()
                    .Where(t => t.CustomerId == userId
                        && (!effectiveStartDate.HasValue || t.CreatedAt >= effectiveStartDate)
                        && (!effectiveEndDate.HasValue || t.CreatedAt <= effectiveEndDate))
                    .GroupBy(t => t.Status)
                    .Select(g => new { Status = g.Key, Count = g.Count() })
                    .ToListAsync();

                var customerSolved = customerStatusCounts.FirstOrDefault(s => s.Status == "Solved")?.Count ?? 0;
                var customerProgress = customerStatusCounts.FirstOrDefault(s => s.Status == "Progress")?.Count ?? 0;
                var customerWaiting = customerStatusCounts.FirstOrDefault(s => s.Status == "Waiting")?.Count ?? 0;

                var customerAvgResponseTimeSec = await _db.Tickets
                    .AsNoTracking()
                    .Where(t => t.CustomerId == userId && t.ResponseTimeSec != null
                        && (!effectiveStartDate.HasValue || t.ResolvedAt >= effectiveStartDate)
                        && (!effectiveEndDate.HasValue || t.ResolvedAt <= effectiveEndDate))
                    .AverageAsync(t => (double?)t.ResponseTimeSec);

                var customerAvgResolutionTimeSec = await _db.Tickets
                    .AsNoTracking()
                    .Where(t => t.CustomerId == userId && t.Status == "Solved" && t.ResolutionTimeSec != null
                        && (!effectiveStartDate.HasValue || t.ResolvedAt >= effectiveStartDate)
                        && (!effectiveEndDate.HasValue || t.ResolvedAt <= effectiveEndDate))
                    .AverageAsync(t => (double?)t.ResolutionTimeSec);

                return Ok(new DashboardStats
                {
                    TotalCsAgent = totalCsAgent,
                    TotalTechnician = totalTechnician,
                    TotalMyTicket = totalMyTicket,
                    ActiveTicket = activeTicket,
                    SolvedTicket = solvedTicket,
                    TicketByStatus = new TicketByStatus
                    {
                        Solved = customerSolved,
                        Progress = customerProgress,
                        Waiting = customerWaiting
                    },
                    MyAvgResponseTime = customerAvgResponseTimeSec,
                    MyAvgResolutionTime = customerAvgResolutionTimeSec
                });
            }

            if (role == "technician")
            {
                var activeTicket = await _db.Tickets.AsNoTracking().CountAsync(t => t.TechnicianId == userId && t.Status != "Solved"
                    && (!effectiveStartDate.HasValue || t.CreatedAt >= effectiveStartDate) && (!effectiveEndDate.HasValue || t.CreatedAt <= effectiveEndDate));
                var solvedTicket = await _db.Tickets.AsNoTracking().CountAsync(t => t.TechnicianId == userId && t.Status == "Solved"
                    && (!effectiveStartDate.HasValue || t.CreatedAt >= effectiveStartDate) && (!effectiveEndDate.HasValue || t.CreatedAt <= effectiveEndDate));
                var totalMyTicket = activeTicket + solvedTicket;

                var technicianPriorityCounts = await _db.Tickets
                    .AsNoTracking()
                    .Where(t => t.TechnicianId == userId && t.Priority != null
                        && (!effectiveStartDate.HasValue || t.CreatedAt >= effectiveStartDate) && (!effectiveEndDate.HasValue || t.CreatedAt <= effectiveEndDate))
                    .GroupBy(t => t.Priority!.PriorityName)
                    .Select(g => new { Priority = g.Key, Count = g.Count() })
                    .ToListAsync();

                var technicianLow = technicianPriorityCounts.FirstOrDefault(p => p.Priority == "Low")?.Count ?? 0;
                var technicianNormal = technicianPriorityCounts.FirstOrDefault(p => p.Priority == "Normal")?.Count ?? 0;
                var technicianHigh = technicianPriorityCounts.FirstOrDefault(p => p.Priority == "High")?.Count ?? 0;
                var technicianCritical = technicianPriorityCounts.FirstOrDefault(p => p.Priority == "Critical")?.Count ?? 0;

                var technicianStatusCounts = await _db.Tickets
                    .AsNoTracking()
                    .Where(t => t.TechnicianId == userId)
                    .GroupBy(t => t.Status)
                    .Select(g => new { Status = g.Key, Count = g.Count() })
                    .ToListAsync();

                var technicianSolved = technicianStatusCounts.FirstOrDefault(s => s.Status == "Solved")?.Count ?? 0;
                var technicianProgress = technicianStatusCounts.FirstOrDefault(s => s.Status == "Progress")?.Count ?? 0;
                var technicianWaiting = technicianStatusCounts.FirstOrDefault(s => s.Status == "Waiting")?.Count ?? 0;

                var technicianIntentCounts = await _db.Tickets
                    .AsNoTracking()
                    .Where(t => t.TechnicianId == userId)
                    .GroupBy(t => t.Intent != null ? t.Intent.IntentName : null)
                    .Select(g => new { Intent = g.Key, Count = g.Count() })
                    .ToListAsync();

                var technicianTicketByIntent = technicianIntentCounts
                    .GroupBy(x => x.Intent ?? "Unclassified")
                    .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));

                return Ok(new DashboardStats
                {
                    TotalMyTicket = totalMyTicket,
                    ActiveTicket = activeTicket,
                    SolvedTicket = solvedTicket,
                    TicketByPriority = new TicketByPriority
                    {
                        Low = technicianLow,
                        Normal = technicianNormal,
                        High = technicianHigh,
                        Critical = technicianCritical
                    },
                    TicketByStatus = new TicketByStatus
                    {
                        Solved = technicianSolved,
                        Progress = technicianProgress,
                        Waiting = technicianWaiting
                    },
                    TicketByIntent = technicianTicketByIntent
                });
            }

            if (role == "admin")
            {
                // ADMIN

                var profileCounts = await _db.Profiles
                    .AsNoTracking()
                    .Where(p => p.CompanyId == companyId)
                    .GroupBy(p => p.RoleId)
                    .Select(g => new { RoleId = g.Key, Count = g.Count() })
                    .ToListAsync();

                var ticketCounts = await _db.Tickets
                    .AsNoTracking()
                    .Where(t => t.Customer!.CompanyId == companyId
                        && (!effectiveStartDate.HasValue || t.CreatedAt >= effectiveStartDate) && (!effectiveEndDate.HasValue || t.CreatedAt <= effectiveEndDate))
                    .GroupBy(t => t.Status)
                    .Select(g => new { Status = g.Key, Count = g.Count() })
                    .ToListAsync();

                var priorityCounts = await _db.Tickets
                    .AsNoTracking()
                    .Where(t => t.Priority != null && t.Customer!.CompanyId == companyId
                        && (!effectiveStartDate.HasValue || t.CreatedAt >= effectiveStartDate) && (!effectiveEndDate.HasValue || t.CreatedAt <= effectiveEndDate))
                    .GroupBy(t => t.Priority!.PriorityName)
                    .Select(g => new { Priority = g.Key, Count = g.Count() })
                    .ToListAsync();

                var intentCounts = await _db.Tickets
                    .AsNoTracking()
                    .Where(t => t.Customer!.CompanyId == companyId
                        && (!effectiveStartDate.HasValue || t.CreatedAt >= effectiveStartDate) && (!effectiveEndDate.HasValue || t.CreatedAt <= effectiveEndDate))
                    .GroupBy(t => t.Intent != null ? t.Intent.IntentName : null)
                    .Select(g => new { Intent = g.Key, Count = g.Count() })
                    .ToListAsync();

                var ticketByIntent = intentCounts
                    .GroupBy(x => x.Intent ?? "Unclassified")
                    .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));

                var slaBreachedCount = await _db.Tickets
                    .AsNoTracking()
                    .CountAsync(t => t.Customer!.CompanyId == companyId
                        && t.SlaBreached
                        && t.Status != "Solved");

                var notifyMinutes = 30;
                var company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId);
                if (company?.SlaConfig is not null)
                {
                    try
                    {
                        var config = System.Text.Json.JsonSerializer.Deserialize<SlaRulesConfigDto>(
                            company.SlaConfig,
                            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (config is not null && config.EnableSlaMonitoring)
                            notifyMinutes = config.NotifyBeforeBreachedMinutes;
                    }
                    catch { }
                }

                var candidateTickets = await _db.Tickets
                    .AsNoTracking()
                    .Where(t => t.Customer!.CompanyId == companyId
                        && t.SlaDeadline != null
                        && !t.SlaBreached
                        && t.SlaDeadline > DateTime.UtcNow)
                    .Select(t => t.SlaDeadline!.Value)
                    .ToListAsync();

                var slaAlmostBreachedCount = candidateTickets
                    .Count(deadline => (deadline - DateTime.UtcNow).TotalMinutes <= notifyMinutes);

                var csAgentRoleId = roleIds.GetValueOrDefault("cs_agent", 0);
                var technicianRoleId = roleIds.GetValueOrDefault("technician", 0);
                var customerRoleId = roleIds.GetValueOrDefault("customer", 0);

                var totalCsAgentFull = profileCounts.FirstOrDefault(p => p.RoleId == csAgentRoleId)?.Count ?? 0;
                var totalTechnicianFull = profileCounts.FirstOrDefault(p => p.RoleId == technicianRoleId)?.Count ?? 0;
                var totalCustomer = profileCounts.FirstOrDefault(p => p.RoleId == customerRoleId)?.Count ?? 0;
                var totalTicket = ticketCounts.Sum(t => t.Count);

                var solved = ticketCounts.FirstOrDefault(t => t.Status == "Solved")?.Count ?? 0;
                var progress = ticketCounts.FirstOrDefault(t => t.Status == "Progress")?.Count ?? 0;
                var waiting = ticketCounts.FirstOrDefault(t => t.Status == "Waiting")?.Count ?? 0;

                var low = priorityCounts.FirstOrDefault(p => p.Priority == "Low")?.Count ?? 0;
                var normal = priorityCounts.FirstOrDefault(p => p.Priority == "Normal")?.Count ?? 0;
                var high = priorityCounts.FirstOrDefault(p => p.Priority == "High")?.Count ?? 0;
                var critical = priorityCounts.FirstOrDefault(p => p.Priority == "Critical")?.Count ?? 0;

                return Ok(new DashboardStats
                {
                    TotalCsAgent = totalCsAgentFull,
                    TotalTechnician = totalTechnicianFull,
                    TotalCustomer = totalCustomer,
                    TotalTicket = totalTicket,
                    TicketByStatus = new TicketByStatus { Solved = solved, Progress = progress, Waiting = waiting },
                    TicketByPriority = new TicketByPriority { Low = low, Normal = normal, High = high, Critical = critical },
                    TicketByIntent = ticketByIntent,
                    SlaBreachedCount = slaBreachedCount,
                    SlaAlmostBreachedCount = slaAlmostBreachedCount
                });
            }

            // CS AGENT

            var csProfileCounts = await _db.Profiles
                .AsNoTracking()
                .Where(p => p.CompanyId == companyId)
                .GroupBy(p => p.RoleId)
                .Select(g => new { RoleId = g.Key, Count = g.Count() })
                .ToListAsync();

            var myTicketCounts = await _db.Tickets
                .AsNoTracking()
                .Where(t => t.AgentId == userId
                    && (!effectiveStartDate.HasValue || t.CreatedAt >= effectiveStartDate) && (!effectiveEndDate.HasValue || t.CreatedAt <= effectiveEndDate))
                .GroupBy(t => t.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            var myPriorityCounts = await _db.Tickets
                .AsNoTracking()
                .Where(t => t.Priority != null && t.AgentId == userId
                    && (!effectiveStartDate.HasValue || t.CreatedAt >= effectiveStartDate) && (!effectiveEndDate.HasValue || t.CreatedAt <= effectiveEndDate))
                .GroupBy(t => t.Priority!.PriorityName)
                .Select(g => new { Priority = g.Key, Count = g.Count() })
                .ToListAsync();

            var myIntentCounts = await _db.Tickets
                .AsNoTracking()
                .Where(t => t.AgentId == userId
                    && (!effectiveStartDate.HasValue || t.CreatedAt >= effectiveStartDate) && (!effectiveEndDate.HasValue || t.CreatedAt <= effectiveEndDate))
                .GroupBy(t => t.Intent != null ? t.Intent.IntentName : null)
                .Select(g => new { Intent = g.Key, Count = g.Count() })
                .ToListAsync();

            var myTicketByIntent = myIntentCounts
                .GroupBy(x => x.Intent ?? "Unclassified")
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));

            var myAvgResponseTimeSec = await _db.Tickets
                .AsNoTracking()
                .Where(t => t.AgentId == userId && t.ResponseTimeSec != null
                    && (!effectiveStartDate.HasValue || t.ResolvedAt >= effectiveStartDate) && (!effectiveEndDate.HasValue || t.ResolvedAt <= effectiveEndDate))
                .AverageAsync(t => (double?)t.ResponseTimeSec);

            var myAvgResolutionTimeSec = await _db.Tickets
                .AsNoTracking()
                .Where(t => t.AgentId == userId && t.Status == "Solved" && t.ResolutionTimeSec != null
                    && (!effectiveStartDate.HasValue || t.ResolvedAt >= effectiveStartDate) && (!effectiveEndDate.HasValue || t.ResolvedAt <= effectiveEndDate))
                .AverageAsync(t => (double?)t.ResolutionTimeSec);

            var myCsAgentRoleId = roleIds.GetValueOrDefault("cs_agent", 0);
            var myTechnicianRoleId = roleIds.GetValueOrDefault("technician", 0);

            var totalCsAgentForCsAgent = csProfileCounts.FirstOrDefault(p => p.RoleId == myCsAgentRoleId)?.Count ?? 0;
            var totalTechnicianForCsAgent = csProfileCounts.FirstOrDefault(p => p.RoleId == myTechnicianRoleId)?.Count ?? 0;
            var myTotalTicket = myTicketCounts.Sum(t => t.Count);

            var mySolved = myTicketCounts.FirstOrDefault(t => t.Status == "Solved")?.Count ?? 0;
            var myProgress = myTicketCounts.FirstOrDefault(t => t.Status == "Progress")?.Count ?? 0;
            var myWaiting = myTicketCounts.FirstOrDefault(t => t.Status == "Waiting")?.Count ?? 0;

            var myLow = myPriorityCounts.FirstOrDefault(p => p.Priority == "Low")?.Count ?? 0;
            var myNormal = myPriorityCounts.FirstOrDefault(p => p.Priority == "Normal")?.Count ?? 0;
            var myHigh = myPriorityCounts.FirstOrDefault(p => p.Priority == "High")?.Count ?? 0;
            var myCritical = myPriorityCounts.FirstOrDefault(p => p.Priority == "Critical")?.Count ?? 0;

            return Ok(new DashboardStats
            {
                TotalCsAgent = totalCsAgentForCsAgent,
                TotalTechnician = totalTechnicianForCsAgent,
                TotalTicket = myTotalTicket,
                TicketByStatus = new TicketByStatus { Solved = mySolved, Progress = myProgress, Waiting = myWaiting },
                TicketByPriority = new TicketByPriority { Low = myLow, Normal = myNormal, High = myHigh, Critical = myCritical },
                TicketByIntent = myTicketByIntent,
                MyAvgResponseTime = myAvgResponseTimeSec,
                MyAvgResolutionTime = myAvgResolutionTimeSec
            });
        }
        // GET /api/dashboard/ticket-trend
        [HttpGet("ticket-trend")]
        public async Task<IActionResult> GetTicketTrend([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
        {
            var (role, companyId) = await GetCurrentUserRoleAndCompany();
            if (role != "admin" && role != "cs_agent") return Forbid();

            // Default to "this month" if no filter passed
            var effectiveStart = startDate.HasValue
                ? DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc)
                : DateTime.SpecifyKind(new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1), DateTimeKind.Utc);

            var effectiveEnd = endDate.HasValue
                ? DateTime.SpecifyKind(endDate.Value, DateTimeKind.Utc)
                : effectiveStart.AddMonths(1).AddDays(-1);

            var daySpan = (effectiveEnd - effectiveStart).TotalDays;
            string granularity = daySpan <= 7 ? "day" : daySpan <= 60 ? "week" : "month";

            var ticketDates = await _db.Tickets
                .AsNoTracking()
                .Where(t => t.Customer!.CompanyId == companyId
                    && t.CreatedAt >= effectiveStart && t.CreatedAt <= effectiveEnd)
                .Select(t => t.CreatedAt)
                .ToListAsync();

            Func<DateTime, string> bucketKeySelector = granularity switch
            {
                "day" => d => d.ToString("yyyy-MM-dd"),
                "week" => d => $"{System.Globalization.ISOWeek.GetYear(d)}-W{System.Globalization.ISOWeek.GetWeekOfYear(d):D2}",
                "month" => d => d.ToString("yyyy-MM"),
                _ => d => d.ToString("yyyy-MM-dd")
            };

            var buckets = ticketDates
                .GroupBy(bucketKeySelector)
                .Select(g => new { Label = g.Key, Count = g.Count() })
                .OrderBy(x => x.Label)
                .ToList();

            return Ok(new { granularity, data = buckets });
        }
        public class TrendBucket
        {
            public DateTime Label { get; set; }
            public int Count { get; set; }
        }

        // GET /api/dashboard/tickets-preview
        [HttpGet("tickets-preview")]
        public async Task<IActionResult> GetTicketsPreview(
            [FromQuery] string? status,
            [FromQuery] string? priority,
            [FromQuery] string? intentKey,
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate)
        {
            var (role, companyId) = await GetCurrentUserRoleAndCompany();
            if (role != "admin" && role != "cs_agent" && role != "technician" && role != "customer") return Forbid();

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var effectiveStart = startDate.HasValue
                ? DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc)
                : (DateTime?)null;
            var effectiveEnd = endDate.HasValue
                ? DateTime.SpecifyKind(endDate.Value, DateTimeKind.Utc)
                : (DateTime?)null;

            var query = _db.Tickets.AsNoTracking().AsQueryable();

            // Scope by role — technician sees only their own, admin/cs_agent see company-wide
            query = role switch
            {
                "technician" => query.Where(t => t.TechnicianId == userId),
                "customer" => query.Where(t => t.CustomerId == userId),
                _ => query.Where(t => t.Customer!.CompanyId == companyId), // admin, cs_agent
            };

            if (!string.IsNullOrEmpty(status))
                query = query.Where(t => t.Status!.ToLower() == status.ToLower());

            if (!string.IsNullOrEmpty(priority))
                query = query.Where(t => t.Priority!.PriorityName!.ToLower() == priority.ToLower());

            if (!string.IsNullOrEmpty(intentKey))
            {
                if (intentKey == "Unclassified")
                    query = query.Where(t => t.Intent == null);
                else
                    query = query.Where(t => t.Intent!.IntentName == intentKey);
            }

            if (effectiveStart.HasValue)
                query = query.Where(t => t.CreatedAt >= effectiveStart);

            if (effectiveEnd.HasValue)
                query = query.Where(t => t.CreatedAt <= effectiveEnd);

            var isCustomer = role == "customer";

            var tickets = await query
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new
                {
                    id = t.Id,
                    subject = t.Subject,
                    status = t.Status,
                    priority = isCustomer ? null : t.Priority!.PriorityName,
                    intent = isCustomer ? null : (t.Intent != null ? t.Intent.IntentName : "Unclassified"),
                    createdAt = t.CreatedAt
                })
                .ToListAsync();

            return Ok(tickets);
        }
    }
}
