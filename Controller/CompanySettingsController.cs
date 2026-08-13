using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using CRM.Api.Data;
using CRM.Api.Models;
using CRM.Api.Services;
using Supabase.Storage;

namespace CRM.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/company/settings")]
    public class CompanySettingsController : BaseController
    {
        private readonly CompanyService _companyService;
        private readonly AppDbContext _db;
        private readonly Supabase.Client _supabase;
        private readonly RegistrationService _registrationService;

        private const string LOGO_BUCKET = "company-logos";

        public CompanySettingsController(
            CompanyService companyService,
            RoleService roleService,
            AppDbContext db,
            Supabase.Client supabase,
            RegistrationService registrationService)
            : base(roleService)
        {
            _companyService = companyService;
            _db = db;
            _supabase = supabase;
            _registrationService = registrationService;
        }

        /// <summary>
        /// GET /api/company/settings — Get company settings for the current user's company
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetSettings()
        {
            var (role, companyId) = await GetCurrentUserRoleAndCompany();
            if (companyId is null) return Unauthorized("User is not associated with a company.");

            var settings = await _companyService.GetSettingsAsync(companyId.Value);
            if (settings is null) return NotFound("Company not found.");

            return Ok(settings);
        }

        [HttpGet("subscription")]
        public async Task<IActionResult> GetSubscription()
        {
            var (_, companyId) = await GetCurrentUserRoleAndCompany();
            if (companyId is null) return Unauthorized("User is not associated with a company.");

            var subscription = await _db.Companies
                .AsNoTracking()
                .Where(c => c.Id == companyId.Value)
                .Select(c => new
                {
                    plan = c.SubscriptionPlan,
                    status = c.SubscriptionStatus,
                    subscriptionEnd = c.SubscriptionEnd,
                    trialUse = c.TrialUse,
                    cancelAtPeriodEnd = c.CancelAtPeriodEnd,
                })
                .FirstOrDefaultAsync();

            return subscription is null ? NotFound("Company not found.") : Ok(subscription);
        }

        [HttpGet("subscription/payments")]
        public async Task<IActionResult> GetSubscriptionPayments()
        {
            if (await GetCurrentUserRole() != "admin") return Forbid();

            var (_, companyId) = await GetCurrentUserRoleAndCompany();
            if (companyId is null) return Unauthorized("User is not associated with a company.");

            var payments = await _db.SubscriptionPayments
                .AsNoTracking()
                .Where(p => p.CompanyId == companyId.Value)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new
                {
                    id = p.Id,
                    subscriptionPlan = p.SubscriptionPlan,
                    amount = p.Amount,
                    status = p.Status,
                    midtransOrderId = p.MidtransOrderId,
                    midtransTransactionId = p.MidtransTransactionId,
                    paymentMethod = p.PaymentMethod,
                    createdAt = p.CreatedAt,
                    paidAt = p.PaidAt,
                    subscriptionStart = p.SubscriptionStart,
                    subscriptionEnd = p.SubscriptionEnd,
                })
                .ToListAsync();

            return Ok(payments);
        }

        [HttpPost("subscription/cancel")]
        public async Task<IActionResult> CancelSubscription()
        {
            if (await GetCurrentUserRole() != "admin") return Forbid();

            try
            {
                var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
                return Ok(await _registrationService.CancelSubscriptionAsync(userId));
            }
            catch (RegistrationException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
        }

        [HttpPost("subscription/reactivate")]
        public async Task<IActionResult> ReactivateSubscription()
        {
            if (await GetCurrentUserRole() != "admin") return Forbid();

            try
            {
                var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
                return Ok(await _registrationService.ReactivateSubscriptionAsync(userId));
            }
            catch (RegistrationException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
        }

        /// <summary>
        /// PUT /api/company/settings — Update company settings for the current user's company
        /// </summary>
        [HttpPut]
        public async Task<IActionResult> UpdateSettings([FromBody] CompanySettingsDto dto)
        {
            var (role, companyId) = await GetCurrentUserRoleAndCompany();
            if (companyId is null) return Unauthorized("User is not associated with a company.");

            var updated = await _companyService.UpdateSettingsAsync(companyId.Value, dto);
            if (updated is null) return NotFound("Company not found.");

            return Ok(updated);
        }

        /// <summary>
        /// POST /api/company/settings/logo — Upload company logo to Supabase Storage
        /// </summary>
        [HttpPost("logo")]
        [RequestSizeLimit(5 * 1024 * 1024)] // 5 MB max
        public async Task<IActionResult> UploadLogo(IFormFile file)
        {
            if (file is null || file.Length == 0)
                return BadRequest("No file provided.");

            var (role, companyId) = await GetCurrentUserRoleAndCompany();
            if (companyId is null) return Unauthorized("User is not associated with a company.");

            // Validate file type
            var allowedTypes = new[] { "image/png", "image/jpeg", "image/webp", "image/gif" };
            if (!allowedTypes.Contains(file.ContentType))
                return BadRequest("Only PNG, JPEG, WebP, and GIF files are allowed.");

            await _supabase.InitializeAsync();

            // Ensure bucket exists (public bucket)
            try
            {
                await _supabase.Storage.CreateBucket(LOGO_BUCKET, new BucketUpsertOptions { Public = true });
            }
            catch
            {
                // Bucket likely already exists — ignore
            }

            // Generate unique file path: companyId/timestamp_ext
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var filePath = $"{companyId}/{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{ext}";

            // Read file bytes
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var bytes = ms.ToArray();

            // Upload to Supabase Storage
            var bucket = _supabase.Storage.From(LOGO_BUCKET);
            await bucket.Upload(bytes, filePath);

            // Get public URL
            var publicUrl = bucket.GetPublicUrl(filePath);

            // Update company logo_url in database
            var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId);
            if (company is not null)
            {
                company.LogoUrl = publicUrl;
                await _db.SaveChangesAsync();
            }

            return Ok(new { logoUrl = publicUrl });
        }

        /// <summary>
        /// GET /api/company/settings/ticket-status — Get ticket status configuration
        /// </summary>
        [HttpGet("ticket-status")]
        public async Task<IActionResult> GetTicketStatusConfig()
        {
            var (role, companyId) = await GetCurrentUserRoleAndCompany();
            if (companyId is null) return Unauthorized("User is not associated with a company.");

            var config = await _companyService.GetTicketStatusConfigAsync(companyId.Value);
            if (config is null) return NotFound("Company not found.");

            return Ok(config);
        }

        /// <summary>
        /// PUT /api/company/settings/ticket-status — Update ticket status configuration
        /// </summary>
        [HttpPut("ticket-status")]
        public async Task<IActionResult> UpdateTicketStatusConfig([FromBody] TicketStatusConfigDto dto)
        {
            var (role, companyId) = await GetCurrentUserRoleAndCompany();
            if (companyId is null) return Unauthorized("User is not associated with a company.");

            var updated = await _companyService.UpdateTicketStatusConfigAsync(companyId.Value, dto);
            if (updated is null) return NotFound("Company not found.");

            return Ok(updated);
        }

        /// <summary>
        /// GET /api/company/settings/sla — Get SLA rules configuration
        /// </summary>
        [HttpGet("sla")]
        public async Task<IActionResult> GetSlaConfig()
        {
            var (role, companyId) = await GetCurrentUserRoleAndCompany();
            if (companyId is null) return Unauthorized("User is not associated with a company.");

            var config = await _companyService.GetSlaConfigAsync(companyId.Value);
            if (config is null) return NotFound("Company not found.");

            return Ok(config);
        }

        /// <summary>
        /// PUT /api/company/settings/sla — Update SLA rules configuration
        /// </summary>
        [HttpPut("sla")]
        public async Task<IActionResult> UpdateSlaConfig([FromBody] SlaRulesConfigDto dto)
        {
            var (role, companyId) = await GetCurrentUserRoleAndCompany();
            if (companyId is null) return Unauthorized("User is not associated with a company.");

            var updated = await _companyService.UpdateSlaConfigAsync(companyId.Value, dto);
            if (updated is null) return NotFound("Company not found.");

            return Ok(updated);
        }

        /// <summary>
        /// GET /api/company/settings/export-schedule — Get automatic export schedule configuration
        /// </summary>
        [HttpGet("export-schedule")]
        public async Task<IActionResult> GetExportScheduleConfig()
        {
            var (role, companyId) = await GetCurrentUserRoleAndCompany();
            if (companyId is null) return Unauthorized("User is not associated with a company.");

            var config = await _companyService.GetExportScheduleConfigAsync(companyId.Value);
            if (config is null) return NotFound("Company not found.");

            return Ok(config);
        }

        /// <summary>
        /// PUT /api/company/settings/export-schedule — Update automatic export schedule configuration
        /// </summary>
        [HttpPut("export-schedule")]
        public async Task<IActionResult> UpdateExportScheduleConfig([FromBody] ExportScheduleConfigDto dto)
        {
            var (role, companyId) = await GetCurrentUserRoleAndCompany();
            if (companyId is null) return Unauthorized("User is not associated with a company.");

            var updated = await _companyService.UpdateExportScheduleConfigAsync(companyId.Value, dto);
            if (updated is null) return NotFound("Company not found.");

            return Ok(updated);
        }
    }
}
