using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using CRM.Api.Data;
using CRM.Api.Services;

namespace CRM.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/auth")]
    public class AuthController : BaseController
    {
        private readonly AppDbContext _db;

        public AuthController(AppDbContext db, RoleService roleService)
            : base(roleService)
        {
            _db = db;
        }

        // GET /api/auth/verify-company?code=mcl
        [HttpGet("verify-company")]
        public async Task<IActionResult> VerifyCompany([FromQuery] string code)
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var profile = await _db.Profiles
                .Include(p => p.Company)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == userId);

            // no profile found
            if (profile == null)
                return NotFound("Profile not found");

            // company_id is null — not allowed
            if (profile.CompanyId == null || profile.Company == null)
                return Forbid();

            // company code mismatch
            if (!string.Equals(profile.Company.CompanyCode, code, StringComparison.OrdinalIgnoreCase))
                return Forbid();

            return Ok(new
            {
                companyId = profile.CompanyId,
                companyName = profile.Company.CompanyName
            });
        }
    }
}