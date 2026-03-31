using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
            // var role = await GetCurrentUserRole();
            // if (role != "cs_agent" && role != "admin" && role != "customer")
            //     return Forbid();

            // Sequential await — EF Core tidak support concurrent queries pada DbContext yang sama
            var totalCsAgent    = await _db.Profiles.AsNoTracking().CountAsync(p => p.RoleId == 2);
            var totalTechnician = await _db.Profiles.AsNoTracking().CountAsync(p => p.RoleId == 3);

            return Ok(new DashboardStats
            {
                TotalCsAgent    = totalCsAgent,
                TotalTechnician = totalTechnician,
            });
        }
    }
}