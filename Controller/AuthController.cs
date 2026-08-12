using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using CRM.Api.Data;
using CRM.Api.Models;
using CRM.Api.Services;

namespace CRM.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/auth")]
    public class AuthController : BaseController
    {
        private readonly AppDbContext _db;
        private readonly RegistrationService _registrationService;
        private readonly IConfiguration _config;

        public AuthController(AppDbContext db, RoleService roleService, RegistrationService registrationService, IConfiguration config)
            : base(roleService)
        {
            _db = db;
            _registrationService = registrationService;
            _config = config;
        }

        [AllowAnonymous]
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            try
            {
                return Ok(await _registrationService.RegisterAsync(request));
            }
            catch (RegistrationException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [AllowAnonymous]
        [HttpGet("registration-status")]
        public async Task<IActionResult> GetRegistrationStatus([FromQuery] int companyId, [FromQuery] string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return BadRequest(new { message = "Company code is required." });

            var status = await _registrationService.GetStatusAsync(companyId, code);
            return status == null ? NotFound() : Ok(status);
        }

        [AllowAnonymous]
        [HttpGet("registration-plans")]
        public IActionResult GetRegistrationPlans()
        {
            return Ok(new
            {
                monthlyAmount = _config.GetValue<decimal?>("Registration:MonthlyAmount"),
                yearlyAmount = _config.GetValue<decimal?>("Registration:YearlyAmount"),
                trialDays = _config.GetValue("Registration:TrialDays", 14),
            });
        }

        [HttpGet("session-status")]
        public async Task<IActionResult> GetSessionStatus()
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var company = await _db.Profiles
                .Where(p => p.Id == userId)
                .Select(p => p.Company)
                .FirstOrDefaultAsync();

            if (company == null)
                return Forbid();

            if (company.SubscriptionStatus is "pending" or "expired"
                || company.SubscriptionEnd.HasValue && company.SubscriptionEnd <= DateTime.UtcNow)
                return Forbid();

            return Ok(new { companyId = company.Id, company.SubscriptionStatus });
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

            if (profile.Company.SubscriptionStatus is "pending" or "expired")
                return Forbid();

            if (profile.Company.SubscriptionEnd.HasValue && profile.Company.SubscriptionEnd <= DateTime.UtcNow)
            {
                profile.Company.SubscriptionStatus = "expired";
                await _db.SaveChangesAsync();
                return Forbid();
            }

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
