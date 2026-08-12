using CRM.Api.Data;
using CRM.Api.Models;
using CRM.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace CRM.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UserManagementController : BaseController
    {
        private readonly AppDbContext _db;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;

        public UserManagementController(
            AppDbContext db,
            RoleService roleService,
            IHttpClientFactory httpClientFactory,
            IConfiguration config)
            : base(roleService)
        {
            _db = db;
            _httpClientFactory = httpClientFactory;
            _config = config;
        }

        [HttpPost("add")]
        public async Task<IActionResult> AddUser([FromBody] AddUserRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Email))
                return BadRequest(new AddUserResponse { Success = false, Message = "Name and email are required." });

            if (request.AuthUserId == Guid.Empty)
                return BadRequest(new AddUserResponse { Success = false, Message = "AuthUserId is required." });

            var adminId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var adminProfile = await _db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == adminId);
            if (adminProfile == null)
                return Unauthorized(new AddUserResponse { Success = false, Message = "Admin not found." });

            var role = await _db.Roles.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == request.RoleId && r.CompanyId == adminProfile.CompanyId);
            if (role == null)
                return BadRequest(new AddUserResponse { Success = false, Message = "Invalid role." });

            var emailExists = await _db.Profiles.AnyAsync(p => p.Email == request.Email);
            if (emailExists)
                return Conflict(new AddUserResponse { Success = false, Message = "Email already exists." });

            var skillIds = (request.SkillIds ?? new List<int>()).Distinct().ToList();
            if (skillIds.Count > 0)
            {
                // Scoped to the admin's company — a skill belonging to another
                // company must not be assignable here, even if the id exists.
                var existingSkillCount = await _db.Skills
                    .CountAsync(s => skillIds.Contains(s.Id) && s.CompanyId == adminProfile.CompanyId);
                if (existingSkillCount != skillIds.Count)
                    return BadRequest(new AddUserResponse { Success = false, Message = "One or more skills are invalid." });
            }

            // EF Core's retrying execution strategy (NpgsqlRetryingExecutionStrategy) forbids
            // manually-opened transactions, because it needs to be able to replay the whole
            // unit of work — including the BEGIN — if a transient failure occurs. So the
            // transaction must be started *inside* the strategy's callback, not around it.
            var strategy = _db.Database.CreateExecutionStrategy();

            AddUserResponse response = null!;
            int statusCode = 200;

            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _db.Database.BeginTransactionAsync();
                try
                {
                    var profile = new Profile
                    {
                        Id        = request.AuthUserId,
                        Email     = request.Email,
                        Name      = request.Name,
                        RoleId    = request.RoleId,
                        Position  = string.IsNullOrWhiteSpace(request.Position) ? null : request.Position,
                        CompanyId = adminProfile.CompanyId,
                        CreatedAt = DateTime.UtcNow,
                    };

                    _db.Profiles.Add(profile);
                    await _db.SaveChangesAsync();

                    if (skillIds.Count > 0)
                    {
                        var profileSkills = skillIds.Select(skillId => new ProfileSkill
                        {
                            ProfileId = profile.Id,
                            SkillId   = skillId,
                        });

                        _db.ProfileSkills.AddRange(profileSkills);
                        await _db.SaveChangesAsync();
                    }

                    await transaction.CommitAsync();

                    statusCode = 200;
                    response = new AddUserResponse
                    {
                        Success = true,
                        Message = "User created successfully.",
                        User = new AddedUserDto
                        {
                            Id        = profile.Id,
                            Name      = profile.Name ?? string.Empty,
                            Email     = profile.Email ?? string.Empty,
                            RoleId    = profile.RoleId,
                            Position  = profile.Position,
                            CompanyId = profile.CompanyId,
                        }
                    };
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    statusCode = 500;
                    response = new AddUserResponse { Success = false, Message = ex.Message };
                }
            });

            return StatusCode(statusCode, response);
        }

        public record UpdateProfilePositionRequest(string? Position);

        // PUT /api/usermanagement/{profileId}/position   { position: "..." }
        // Used by the pencil icon next to Position in the technician/CS-agent list.
        [HttpPut("{profileId:guid}/position")]
        public async Task<IActionResult> UpdateProfilePosition(Guid profileId, [FromBody] UpdateProfilePositionRequest request)
        {
            var adminId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var adminProfile = await _db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == adminId);
            if (adminProfile == null)
                return Unauthorized();

            var profile = await _db.Profiles
                .FirstOrDefaultAsync(p => p.Id == profileId && p.CompanyId == adminProfile.CompanyId);
            if (profile == null)
                return NotFound(new { message = "Profile not found." });

            profile.Position = string.IsNullOrWhiteSpace(request.Position) ? null : request.Position.Trim();
            await _db.SaveChangesAsync();

            return Ok(new { profileId = profile.Id, position = profile.Position });
        }

        public record AddProfileSkillRequest(int SkillId);

        [HttpPost("{profileId:guid}/skills")]
        public async Task<IActionResult> AddProfileSkill(Guid profileId, [FromBody] AddProfileSkillRequest request)
        {
            var adminId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var adminProfile = await _db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == adminId);
            if (adminProfile == null)
                return Unauthorized();

            var profile = await _db.Profiles.FirstOrDefaultAsync(p => p.Id == profileId && p.CompanyId == adminProfile.CompanyId);
            if (profile == null)
                return NotFound(new { message = "Profile not found." });

            // Scoped to the admin's company — prevents attaching a skill that
            // belongs to a different company/tenant to this profile.
            var skillExists = await _db.Skills
                .AnyAsync(s => s.Id == request.SkillId && s.CompanyId == adminProfile.CompanyId);
            if (!skillExists)
                return BadRequest(new { message = "Invalid skill." });

            var alreadyLinked = await _db.ProfileSkills
                .AnyAsync(ps => ps.ProfileId == profileId && ps.SkillId == request.SkillId);
            if (alreadyLinked)
                return Ok(new { message = "Skill already assigned." });

            _db.ProfileSkills.Add(new ProfileSkill { ProfileId = profileId, SkillId = request.SkillId });
            await _db.SaveChangesAsync();

            return Ok(new { message = "Skill added." });
        }

        [HttpDelete("{profileId:guid}/skills/{skillId:int}")]
        public async Task<IActionResult> RemoveProfileSkill(Guid profileId, int skillId)
        {
            var adminId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var adminProfile = await _db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == adminId);
            if (adminProfile == null)
                return Unauthorized();

            var profile = await _db.Profiles.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == profileId && p.CompanyId == adminProfile.CompanyId);
            if (profile == null)
                return NotFound(new { message = "Profile not found." });

            var link = await _db.ProfileSkills.FirstOrDefaultAsync(ps => ps.ProfileId == profileId && ps.SkillId == skillId);
            if (link == null)
                return NotFound(new { message = "Skill not assigned." });

            _db.ProfileSkills.Remove(link);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Skill removed." });
        }
    }
}
