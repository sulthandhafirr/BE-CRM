using CRM.Api.Data;
using CRM.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CRM.Api.Controllers
{
    /// <summary>
    /// Endpoint untuk pencarian skill (autocomplete) dan pembuatan skill baru
    /// dari field "tag" di form tambah teknisi.
    /// </summary>
    [ApiController]
    [Route("api/skill")]
    public class SkillController : ControllerBase
    {
        private const int MaxSearchResults = 15;

        private readonly AppDbContext _context; // TODO: ganti "AppDbContext" sesuai nama DbContext-mu

        public SkillController(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// GET /api/skills/search?query=react
        /// Dipakai oleh SkillField saat user mengetik di form.
        /// </summary>
        [HttpGet("search")]
        public async Task<ActionResult<IEnumerable<SkillDto>>> Search([FromQuery] string query = "")
        {
            var normalizedQuery = query?.Trim().ToLower() ?? string.Empty;

            var skillsQuery = _context.Skills.AsNoTracking().AsQueryable();

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

        /// <summary>
        /// POST /api/skills  { "skillName": "React" }
        /// Idempotent: kalau skill dengan nama sama (case-insensitive) sudah ada,
        /// kembalikan yang sudah ada alih-alih membuat duplikat.
        /// Dipanggil AddUserForm untuk setiap tag `isNew` sebelum user teknisi dibuat.
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<SkillDto>> Create([FromBody] CreateSkillRequest request)
        {
            var name = request?.Skill?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return BadRequest(new { message = "Nama skill wajib diisi." });
            }

            var existing = await _context.Skills
                .FirstOrDefaultAsync(s => s.SkillName != null && s.SkillName.ToLower() == name.ToLower());

            if (existing is not null)
            {
                return Ok(new SkillDto(existing.Id, existing.SkillName!));
            }

            var skill = new Skill { SkillName = name };
            _context.Skills.Add(skill);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(Search), new { query = skill.SkillName }, new SkillDto(skill.Id, skill.SkillName!));
        }
    }

    // Nama properti sengaja "Skill" (bukan "SkillName") supaya hasil serialisasi JSON
    // camelCase-nya jadi { id, skill } — cocok dengan bentuk objek yang dipakai
    // SkillField.jsx (option.skill, o.id, dst).
    public record SkillDto(int Id, string Skill);

    public record CreateSkillRequest(string Skill);
}