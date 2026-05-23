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
        public async Task<IActionResult> GetStats()
        {
            var (role, companyId) = await GetCurrentUserRoleAndCompany();

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            if (role == "customer")
            {

                var totalCsAgent = await _db.Profiles.AsNoTracking().CountAsync(p => p.RoleId == 2 && p.CompanyId == companyId);
                var totalTechnician = await _db.Profiles.AsNoTracking().CountAsync(p => p.RoleId == 3 && p.CompanyId == companyId);
                var activeTicket = await _db.Tickets.AsNoTracking().CountAsync(t => t.CustomerId == userId && t.Status != "Solved");
                var solvedTicket = await _db.Tickets.AsNoTracking().CountAsync(t => t.CustomerId == userId && t.Status == "Solved");
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
                var activeTicket = await _db.Tickets.AsNoTracking().CountAsync(t => t.TechnicianId == userId && t.Status != "Solved");
                var solvedTicket = await _db.Tickets.AsNoTracking().CountAsync(t => t.TechnicianId == userId && t.Status == "Solved");
                var totalMyTicket = activeTicket + solvedTicket;

                return Ok(new DashboardStats
                {
                    TotalMyTicket = totalMyTicket,
                    ActiveTicket = activeTicket,
                    SolvedTicket = solvedTicket,
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
                .Where(t => t.Customer!.CompanyId == companyId)
                .GroupBy(t => t.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            var myAvgResponseTimeSec = await _db.Tickets
                .AsNoTracking()
                .Where(t => t.AgentId == userId && t.ResponseTimeSec != null)
                .AverageAsync(t => (double?)t.ResponseTimeSec);

            var myAvgResolutionTimeSec = await _db.Tickets
                .AsNoTracking()
                .Where(t => t.AgentId == userId && t.Status == "Solved" && t.ResolutionTimeSec != null)
                .AverageAsync(t => (double?)t.ResolutionTimeSec);
            
            var priorityCounts = await _db.Tickets
                .AsNoTracking()
                .Where(t => t.Priority != null && t.Customer!.CompanyId == companyId)
                .GroupBy(t => t.Priority!.PriorityName)
                .Select(g => new { Priority = g.Key, Count = g.Count() })
                .ToListAsync();

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
                MyAvgResponseTime = myAvgResponseTimeSec,
                MyAvgResolutionTime = myAvgResolutionTimeSec
            });
        }
    }
}