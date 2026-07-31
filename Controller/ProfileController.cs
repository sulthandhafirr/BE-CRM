using CRM.Api.Data;
using CRM.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CRM.Api.Controllers
{
    [ApiController]
    [Route("api/profile")]
    [Authorize]
    public class ProfileController : BaseController
    {
        private readonly AppDbContext _db;

        public ProfileController(AppDbContext db, RoleService roleService)
            : base(roleService)
        {
            _db = db;
        }

        // PUT /api/profile/{id} — update the current user's own profile (name)
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> UpdateProfile(Guid id, [FromBody] UpdateProfileRequest request)
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            // Users may only update their own profile
            if (id != userId)
                return Forbid();

            var profile = await _db.Profiles.FirstOrDefaultAsync(p => p.Id == userId);
            if (profile == null)
                return NotFound(new { message = "Profile not found." });

            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest(new { message = "Name is required." });

            profile.Name = request.Name.Trim();

            await _db.SaveChangesAsync();

            return Ok(new { id = profile.Id, name = profile.Name });
        }

        // ── Request models ────────────────────────────────────────────────────

        public class UpdateProfileRequest
        {
            public string? Name { get; set; }
        }
    }
}
