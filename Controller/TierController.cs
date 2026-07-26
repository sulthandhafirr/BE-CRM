using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CRM.Api.Data;
using CRM.Api.Models;
using System.Security.Claims;

namespace CRM.Api.Controllers
{
    [ApiController]
    [Route("api/tiers")]
    [Authorize]
    public class TierController : ControllerBase
    {
        private readonly AppDbContext _db;

        public TierController(AppDbContext db)
        {
            _db = db;
        }

        // Ambil userId dari token, lalu resolve company_id & role lewat tabel profile —
        // sama seperti pola yang sudah dipakai controller lain di aplikasi ini.
        private async Task<(int companyId, bool isAdmin)> GetCurrentUserContextAsync()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                               ?? User.FindFirst("sub")?.Value
                               ?? User.FindFirst("userId")?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                throw new UnauthorizedAccessException("User id tidak ditemukan pada token.");

            var profile = await _db.Profiles
                .Include(p => p.Role)
                .FirstOrDefaultAsync(p => p.Id == userId);

            if (profile is null || profile.CompanyId is null)
                throw new UnauthorizedAccessException("Company context tidak ditemukan untuk user ini.");

            var isAdmin = profile.Role?.IsSystem == true
                          || string.Equals(profile.Role?.RoleName, "Administrator", StringComparison.OrdinalIgnoreCase);

            return (profile.CompanyId.Value, isAdmin);
        }

        private static bool IsValidHexColor(string? color) =>
            !string.IsNullOrWhiteSpace(color) &&
            System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$");

        [HttpGet]
        public async Task<IActionResult> GetCompanyTiers()
        {
            var (companyId, _) = await GetCurrentUserContextAsync();

            var tiers = await _db.Tiers
                .Where(t => t.CompanyId == companyId)
                .OrderBy(t => t.Id)
                .Select(t => new { t.Id, t.TierName, t.Color })
                .ToListAsync();

            return Ok(tiers);
        }

        [HttpPost]
        public async Task<IActionResult> CreateTier([FromBody] CreateTierRequest request)
        {
            var (companyId, isAdmin) = await GetCurrentUserContextAsync();
            if (!isAdmin) return Forbid();

            var name = request.TierName?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest(new { message = "Nama tier tidak boleh kosong." });

            var color = IsValidHexColor(request.Color) ? request.Color! : "#6b7280";

            var exists = await _db.Tiers.AnyAsync(t =>
                t.CompanyId == companyId && t.TierName!.ToLower() == name.ToLower());
            if (exists)
                return Conflict(new { message = $"Tier '{name}' sudah ada di company Anda." });

            var tier = new Tier { TierName = name, CompanyId = companyId, Color = color };
            _db.Tiers.Add(tier);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetCompanyTiers), new { id = tier.Id },
                new { tier.Id, tier.TierName, tier.Color });
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateTier(int id, [FromBody] UpdateTierRequest request)
        {
            var (companyId, isAdmin) = await GetCurrentUserContextAsync();
            if (!isAdmin) return Forbid();

            var tier = await _db.Tiers.FirstOrDefaultAsync(t => t.Id == id && t.CompanyId == companyId);
            if (tier is null)
                return NotFound(new { message = "Tier tidak ditemukan pada company Anda." });

            var name = request.TierName?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest(new { message = "Nama tier tidak boleh kosong." });

            var duplicate = await _db.Tiers.AnyAsync(t =>
                t.CompanyId == companyId && t.Id != id && t.TierName!.ToLower() == name.ToLower());
            if (duplicate)
                return Conflict(new { message = $"Tier '{name}' sudah ada di company Anda." });

            tier.TierName = name;
            if (IsValidHexColor(request.Color))
                tier.Color = request.Color!;

            await _db.SaveChangesAsync();
            return Ok(new { tier.Id, tier.TierName, tier.Color });
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteTier(int id)
        {
            var (companyId, isAdmin) = await GetCurrentUserContextAsync();
            if (!isAdmin) return Forbid();

            var tier = await _db.Tiers.FirstOrDefaultAsync(t => t.Id == id && t.CompanyId == companyId);
            if (tier is null)
                return NotFound(new { message = "Tier tidak ditemukan pada company Anda." });

            var inUse = await _db.ProfileTiers.AnyAsync(pt => pt.TierId == id);
            if (inUse)
                return Conflict(new { message = "Tier masih digunakan oleh user, lepas assignment dulu sebelum menghapus." });

            _db.Tiers.Remove(tier);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpGet("profile/{profileId:guid}")]
        public async Task<IActionResult> GetProfileTier(Guid profileId)
        {
            var (companyId, _) = await GetCurrentUserContextAsync();

            var profileBelongs = await _db.Profiles.AnyAsync(p => p.Id == profileId && p.CompanyId == companyId);
            if (!profileBelongs) return Forbid();

            var profileTier = await _db.ProfileTiers
                .Where(pt => pt.ProfileId == profileId)
                .Include(pt => pt.Tier)
                .Select(pt => new
                {
                    profileId = pt.ProfileId,
                    tierId = pt.TierId,
                    tierName = pt.Tier != null ? pt.Tier.TierName : null,
                    tierColor = pt.Tier != null ? pt.Tier.Color : null,
                })
                .FirstOrDefaultAsync();
            return Ok(profileTier);
        }

        [HttpPut("profile/{profileId:guid}")]
        public async Task<IActionResult> SetProfileTier(Guid profileId, [FromBody] SetTierRequest request)
        {
            var (companyId, _) = await GetCurrentUserContextAsync();

            var profile = await _db.Profiles.FirstOrDefaultAsync(p => p.Id == profileId);
            if (profile is null)
                return NotFound(new { message = $"Profile with id {profileId} not found." });
            if (profile.CompanyId != companyId)
                return Forbid();

            var tier = await _db.Tiers.FirstOrDefaultAsync(t => t.Id == request.TierId);
            if (tier is null)
                return NotFound(new { message = $"Tier with id {request.TierId} not found." });
            if (tier.CompanyId != companyId)
                return Forbid();

            var existing = await _db.ProfileTiers.AsNoTracking()
                .FirstOrDefaultAsync(pt => pt.ProfileId == profileId);

            if (existing is not null)
            {
                await _db.ProfileTiers
                    .Where(pt => pt.ProfileId == profileId)
                    .ExecuteUpdateAsync(s => s.SetProperty(pt => pt.TierId, request.TierId));
            }
            else
            {
                _db.ProfileTiers.Add(new ProfileTier { ProfileId = profileId, TierId = request.TierId });
                await _db.SaveChangesAsync();
            }

            return Ok(new { profileId, tierId = request.TierId });
        }

        [HttpDelete("profile/{profileId:guid}")]
        public async Task<IActionResult> RemoveProfileTier(Guid profileId)
        {
            var (companyId, _) = await GetCurrentUserContextAsync();

            var profile = await _db.Profiles.FirstOrDefaultAsync(p => p.Id == profileId);
            if (profile is null || profile.CompanyId != companyId)
                return Forbid();

            var existing = await _db.ProfileTiers.FirstOrDefaultAsync(pt => pt.ProfileId == profileId);
            if (existing is null)
                return NotFound(new { message = "No tier assigned to this profile." });

            _db.ProfileTiers.Remove(existing);
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }

    public class SetTierRequest { public int TierId { get; set; } }
    public class CreateTierRequest
    {
        public string? TierName { get; set; }
        public string? Color { get; set; }
    }

    public class UpdateTierRequest
    {
        public string? TierName { get; set; }
        public string? Color { get; set; }
    }
}