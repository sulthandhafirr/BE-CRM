using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CRM.Api.Models;
using CRM.Api.Data;

namespace CRM.Api.Controllers
{
    [ApiController]
    [Route("api/users")]
    [Authorize]
    public class UserController : ControllerBase
    {
        private readonly AppDbContext _context;

        public UserController(AppDbContext context)
        {
            _context = context;
        }

        // GET /api/users?role_id=2
        [HttpGet]
        public async Task<IActionResult> GetUsersByRole([FromQuery] int role_id)
        {
            var users = await _context.Profiles
                .Where(p => p.RoleId == role_id)
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