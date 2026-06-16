using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CRM.Api.Data;
using CRM.Api.Models;

namespace CRM.Api.Controllers
{
    [ApiController]
    [Route("api/tiers")]
    public class TierController : ControllerBase
    {
        private readonly AppDbContext _db;

        public TierController(AppDbContext db)
        {
            _db = db;
        }

        // GET /api/tiers
        [HttpGet]
        public async Task<IActionResult> GetAllTiers()
        {
            var tiers = await _db.Tiers
                .OrderBy(t => t.Id)
                .Select(t => new { t.Id, t.TierName })
                .ToListAsync();

            return Ok(tiers);
        }

        // GET /api/tiers/profile/{profileId}
        // Returns the tier assigned to a specific profile (used to hydrate dropdown on load)
        [HttpGet("profile/{profileId:guid}")]
        public async Task<IActionResult> GetProfileTier(Guid profileId)
        {
            var profileTier = await _db.ProfileTiers
                .Where(pt => pt.ProfileId == profileId)
                .Include(pt => pt.Tier)
                .Select(pt => new
                {
                    profileId = pt.ProfileId,
                    tierId = pt.TierId,
                    tierName = pt.Tier != null ? pt.Tier.TierName : null,
                })
                .FirstOrDefaultAsync();

            if (profileTier is null)
                return Ok(null); // no tier assigned — not an error

            return Ok(profileTier);
        }

        // PUT /api/tiers/profile/{profileId}
        [HttpPut("profile/{profileId:guid}")]
        public async Task<IActionResult> SetProfileTier(Guid profileId, [FromBody] SetTierRequest request)
        {
            var tierExists = await _db.Tiers.AnyAsync(t => t.Id == request.TierId);
            if (!tierExists)
                return NotFound(new { message = $"Tier with id {request.TierId} not found." });

            var profileExists = await _db.Profiles.AnyAsync(p => p.Id == profileId);
            if (!profileExists)
                return NotFound(new { message = $"Profile with id {profileId} not found." });

            var existing = await _db.ProfileTiers
                .AsNoTracking()                              // ← pakai AsNoTracking
                .FirstOrDefaultAsync(pt => pt.ProfileId == profileId);

            if (existing is not null)
            {
                await _db.ProfileTiers
                    .Where(pt => pt.ProfileId == profileId)
                    .ExecuteUpdateAsync(s => s.SetProperty(pt => pt.TierId, request.TierId));
            }
            else
            {
                _db.ProfileTiers.Add(new ProfileTier
                {
                    ProfileId = profileId,
                    TierId = request.TierId,
                });
                await _db.SaveChangesAsync();
            }

            return Ok(new { profileId, tierId = request.TierId });
        }

        // DELETE /api/tiers/profile/{profileId}
        [HttpDelete("profile/{profileId:guid}")]
        public async Task<IActionResult> RemoveProfileTier(Guid profileId)
        {
            var existing = await _db.ProfileTiers
                .FirstOrDefaultAsync(pt => pt.ProfileId == profileId);

            if (existing is null)
                return NotFound(new { message = "No tier assigned to this profile." });

            _db.ProfileTiers.Remove(existing);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }

    public class SetTierRequest
    {
        public int TierId { get; set; }
    }
}