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

            if (role == "customer")
            {

                var totalCsAgent = await _db.Profiles.AsNoTracking().CountAsync(p => p.RoleId == 2 && p.CompanyId == companyId);
                var totalTechnician = await _db.Profiles.AsNoTracking().CountAsync(p => p.RoleId == 3 && p.CompanyId == companyId);
                var activeTicket = await _db.Tickets.AsNoTracking().CountAsync(t => t.CustomerId == userId && t.Status != "Solved"
                    && (!effectiveStartDate.HasValue || t.CreatedAt >= effectiveStartDate) && (!effectiveEndDate.HasValue || t.CreatedAt <= effectiveEndDate));
                var solvedTicket = await _db.Tickets.AsNoTracking().CountAsync(t => t.CustomerId == userId && t.Status == "Solved"
                    && (!effectiveStartDate.HasValue || t.CreatedAt >= effectiveStartDate) && (!effectiveEndDate.HasValue || t.CreatedAt <= effectiveEndDate));
                var totalMyTicket = activeTicket + solvedTicket;

                return Ok(new DashboardStats
                {
                    TotalCsAgent = totalCsAgent,
                    TotalTechnician = totalTechnician,
                    TotalMyTicket = totalMyTicket,
                    ActiveTicket = activeTicket,
                    SolvedTicket = solvedTicket,
                    // TotalCustomer and TicketByStatus are omitted — will be null/0
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
                    }
                });
            }

            // CS AGENT

            // query: group profiles by role_id
            var profileCounts = await _db.Profiles
                .AsNoTracking()
                .Where(p => p.CompanyId == companyId)
                .GroupBy(p => p.RoleId)
                .Select(g => new { RoleId = g.Key, Count = g.Count() })
                .ToListAsync();

            // query: group tickets by status + total
            var ticketCounts = await _db.Tickets
                .AsNoTracking()
                .Where(t => t.Customer!.CompanyId == companyId
                    && (!effectiveStartDate.HasValue || t.CreatedAt >= effectiveStartDate) && (!effectiveEndDate.HasValue || t.CreatedAt <= effectiveEndDate))
                .GroupBy(t => t.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

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

            var totalCsAgentFull = profileCounts.FirstOrDefault(p => p.RoleId == 2)?.Count ?? 0;
            var totalTechnicianFull = profileCounts.FirstOrDefault(p => p.RoleId == 3)?.Count ?? 0;
            var totalCustomer = profileCounts.FirstOrDefault(p => p.RoleId == 1)?.Count ?? 0;
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
                TicketByStatus = new TicketByStatus
                {
                    Solved = solved,
                    Progress = progress,
                    Waiting = waiting
                },
                TicketByPriority = new TicketByPriority
                {
                    Low = low,
                    Normal = normal,
                    High = high,
                    Critical = critical
                },
                TicketByIntent = ticketByIntent,
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
    }
}