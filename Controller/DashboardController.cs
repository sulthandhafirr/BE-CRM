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
            var role = await GetCurrentUserRole();

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            if (role == "customer")
            {

                var totalCsAgent = await _db.Profiles.AsNoTracking().CountAsync(p => p.RoleId == 2);
                var totalTechnician = await _db.Profiles.AsNoTracking().CountAsync(p => p.RoleId == 3);
                var totalMyTicket = await _db.Tickets.AsNoTracking().CountAsync(t => t.CustomerId == userId);

                return Ok(new DashboardStats
                {
                    TotalCsAgent = totalCsAgent,
                    TotalTechnician = totalTechnician,
                    TotalTicket = totalMyTicket
                    // TotalCustomer and TicketByStatus are omitted — will be null/0
                });
            }

            // query: group profiles by role_id
            var profileCounts = await _db.Profiles
                .AsNoTracking()
                .GroupBy(p => p.RoleId)
                .Select(g => new { RoleId = g.Key, Count = g.Count() })
                .ToListAsync();

            // query: group tickets by status + total
            var ticketCounts = await _db.Tickets
                .AsNoTracking()
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

            var totalCsAgentFull = profileCounts.FirstOrDefault(p => p.RoleId == 2)?.Count ?? 0;
            var totalTechnicianFull = profileCounts.FirstOrDefault(p => p.RoleId == 3)?.Count ?? 0;
            var totalCustomer = profileCounts.FirstOrDefault(p => p.RoleId == 1)?.Count ?? 0;
            var totalTicket = ticketCounts.Sum(t => t.Count);

            var solved = ticketCounts.FirstOrDefault(t => t.Status == "Solved")?.Count ?? 0;
            var progress = ticketCounts.FirstOrDefault(t => t.Status == "Progress")?.Count ?? 0;
            var waiting = ticketCounts.FirstOrDefault(t => t.Status == "Waiting")?.Count ?? 0;

            var low = await _db.Tickets.AsNoTracking().CountAsync(t => t.Priority!.PriorityName == "Low");
            var normal = await _db.Tickets.AsNoTracking().CountAsync(t => t.Priority!.PriorityName == "Normal");
            var high = await _db.Tickets.AsNoTracking().CountAsync(t => t.Priority!.PriorityName == "High");
            var critical = await _db.Tickets.AsNoTracking().CountAsync(t => t.Priority!.PriorityName == "Critical");

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