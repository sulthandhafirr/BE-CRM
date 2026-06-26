using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using CRM.Api.Models;
using CRM.Api.Data;
using CRM.Api.Services; // ← tambah

namespace CRM.Api.Controllers
{
    [ApiController]
    [Route("api/users")]
    [Authorize]
    public class UserController : BaseController // ← ganti dari ControllerBase
    {
        private readonly AppDbContext _context;

        public UserController(AppDbContext context, RoleService roleService) // ← tambah roleService
            : base(roleService) // ← tambah
        {
            _context = context;
        }

        // GET /api/users?role_id=2
        [HttpGet]
        public async Task<IActionResult> GetUsersByRole([FromQuery] int role_id)
        {
            // Ambil company_id dari admin yang login
            var (_, companyId) = await GetCurrentUserRoleAndCompany();

            var users = await _context.Profiles
                .Where(p => p.RoleId == role_id && p.CompanyId == companyId) // ← tambah filter ini
                .OrderBy(p => p.Name)
                .Include(p => p.ProfileSkills)
                    .ThenInclude(ps => ps.Skill)
                .Include(p => p.ProfileTiers)
                    .ThenInclude(pt => pt.Tier)
                .Select(p => new
                {
                    id       = p.Id,
                    name     = p.Name,
                    email    = p.Email,
                    position = p.Position,
                    profile_skill = p.ProfileSkills.Select(ps => new
                    {
                        skills = new { skill = ps.Skill != null ? ps.Skill.SkillName : null }
                    }),
                    profile_tier = p.ProfileTiers.Select(pt => new
                    {
                        tier_id = pt.TierId,
                        tier    = new { tierName = pt.Tier != null ? pt.Tier.TierName : null }
                    })
                })
                .ToListAsync();

            return Ok(users);
        }
    }
}