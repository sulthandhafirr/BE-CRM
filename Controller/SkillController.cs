using CRM.Api.Data;
using CRM.Api.Models;
using CRM.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CRM.Api.Controllers
{

    [ApiController]
    [Route("api/skill")]
    [Authorize]
    public class SkillController : BaseController
    {
        private const int MaxSearchResults = 15;

        private readonly AppDbContext _context;

        public SkillController(AppDbContext context, RoleService roleService)
            : base(roleService)
        {
            _context = context;
        }

        private async Task<int?> GetCurrentCompanyIdAsync()
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var profile = await _context.Profiles.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == userId);
            return profile?.CompanyId;
        }


        [HttpGet("search")]
        public async Task<ActionResult<IEnumerable<SkillDto>>> Search([FromQuery] string query = "")
        {
            var companyId = await GetCurrentCompanyIdAsync();
            if (companyId == null)
                return Unauthorized(new { message = "Company not found for current user." });

            var normalizedQuery = query?.Trim().ToLower() ?? string.Empty;

            var skillsQuery = _context.Skills.AsNoTracking()
                .Where(s => s.CompanyId == companyId);

            if (!string.IsNullOrEmpty(normalizedQuery))
            {
                skillsQuery = skillsQuery.Where(s =>
                    s.SkillName != null && s.SkillName.ToLower().Contains(normalizedQuery));
            }

            var results = await skillsQuery
                .OrderBy(s => s.SkillName)
                .Take(MaxSearchResults)
                .Select(s => new SkillDto(s.Id, s.SkillName!))
                .ToListAsync();

            return Ok(results);
        }


        [HttpPost]
        public async Task<ActionResult<SkillDto>> Create([FromBody] CreateSkillRequest request)
        {
            var companyId = await GetCurrentCompanyIdAsync();
            if (companyId == null)
                return Unauthorized(new { message = "Company not found for current user." });

            var name = request?.Skill?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return BadRequest(new { message = "Nama skill wajib diisi." });
            }

            var existing = await _context.Skills
                .FirstOrDefaultAsync(s =>
                    s.CompanyId == companyId &&
                    s.SkillName != null && s.SkillName.ToLower() == name.ToLower());

            if (existing is not null)
            {
                return Ok(new SkillDto(existing.Id, existing.SkillName!));
            }

            var skill = new Skill { SkillName = name, CompanyId = companyId };
            _context.Skills.Add(skill);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(Search), new { query = skill.SkillName }, new SkillDto(skill.Id, skill.SkillName!));
        }
    }

    public record SkillDto(int Id, string Skill);

    public record CreateSkillRequest(string Skill);
}