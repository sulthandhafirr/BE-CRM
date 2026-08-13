using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CRM.Api.Data;
using CRM.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CRM.Api.Services
{
    public class RegistrationService
    {
        private static readonly HashSet<string> PaidPlans = new(StringComparer.OrdinalIgnoreCase)
        {
            "monthly", "yearly"
        };

        private readonly AppDbContext _db;
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly RoleManagementService _roleManagementService;
        private readonly PaymentService _paymentService;

        public RegistrationService(
            AppDbContext db,
            IConfiguration config,
            IHttpClientFactory httpClientFactory,
            RoleManagementService roleManagementService,
            PaymentService paymentService)
        {
            _db = db;
            _config = config;
            _httpClientFactory = httpClientFactory;
            _roleManagementService = roleManagementService;
            _paymentService = paymentService;
        }

        public async Task<RegisterResponse> RegisterAsync(RegisterRequest request)
        {
            var email = request.Email.Trim().ToLowerInvariant();
            var companyCode = request.CompanyCode.Trim();
            var plan = request.SubscriptionPlan.Trim().ToLowerInvariant();

            if (plan != "trial" && !PaidPlans.Contains(plan))
                throw new RegistrationException("Invalid subscription plan.", 400);

            if (await _db.Profiles.AnyAsync(p => p.Email != null && p.Email.ToLower() == email))
                throw new RegistrationException("Email already exists.", 409);

            if (await _db.Companies.AnyAsync(c => c.CompanyCode != null && c.CompanyCode.ToUpper() == companyCode.ToUpper()))
                throw new RegistrationException("Company code already exists.", 409);

            if (plan == "trial" && await HasUsedTrialAsync(email))
                throw new RegistrationException("Free Trial has already been used for this email.", 409);

            decimal? amount = null;
            if (PaidPlans.Contains(plan))
            {
                var amountKey = plan == "monthly" ? "Registration:MonthlyAmount" : "Registration:YearlyAmount";
                amount = _config.GetValue<decimal?>(amountKey);
                if (!amount.HasValue || amount <= 0)
                    throw new RegistrationException($"{amountKey} is not configured.", 400);
            }

            var authUserId = await CreateAuthUserAsync(email, request.Password);
            var companyId = 0;

            try
            {
                var strategy = _db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _db.Database.BeginTransactionAsync();

                    var now = DateTime.UtcNow;
                    var company = new Company
                    {
                        CompanyName = request.CompanyName.Trim(),
                        CompanyCode = companyCode,
                        SupportEmail = email,
                        SubscriptionPlan = plan,
                        SubscriptionStatus = plan == "trial" ? "trial" : "pending",
                        SubscriptionEnd = plan == "trial"
                            ? now.AddDays(_config.GetValue("Registration:TrialDays", 14))
                            : null,
                        TrialUse = plan == "trial",
                    };

                    _db.Companies.Add(company);
                    await _db.SaveChangesAsync();
                    companyId = company.Id;

                    await _roleManagementService.SeedSystemRolesAsync(company.Id);
                    var adminRole = await _db.Roles.FirstAsync(r => r.CompanyId == company.Id && r.RoleName == "admin");

                    _db.Profiles.Add(new Profile
                    {
                        Id = authUserId,
                        Email = email,
                        Name = request.FullName.Trim(),
                        RoleId = adminRole.Id,
                        CompanyId = company.Id,
                        CreatedAt = now,
                    });
                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();
                });

                if (plan == "trial")
                {
                    return new RegisterResponse
                    {
                        CompanyId = companyId,
                        RequiresPayment = false,
                        SubscriptionPlan = plan,
                        SubscriptionStatus = "trial",
                        SubscriptionEnd = DateTime.UtcNow.AddDays(_config.GetValue("Registration:TrialDays", 14)),
                    };
                }

                var payment = await _paymentService.CreateSubscriptionPaymentAsync(companyId, plan, amount!.Value);
                return new RegisterResponse
                {
                    CompanyId = companyId,
                    RequiresPayment = true,
                    SubscriptionPlan = plan,
                    SubscriptionStatus = "pending",
                    SnapToken = payment.Token,
                    RedirectUrl = payment.RedirectUrl,
                };
            }
            catch
            {
                await CleanupPendingAsync(companyId, authUserId);
                throw;
            }
        }

        public async Task<object?> GetStatusAsync(int companyId, string companyCode)
        {
            var company = await _db.Companies.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == companyId
                    && c.CompanyCode != null
                    && c.CompanyCode.ToUpper() == companyCode.Trim().ToUpper());

            if (company == null)
                return null;

            return new
            {
                companyId = company.Id,
                subscriptionPlan = company.SubscriptionPlan,
                subscriptionStatus = company.SubscriptionStatus,
                subscriptionEnd = company.SubscriptionEnd,
                cancelAtPeriodEnd = company.CancelAtPeriodEnd,
            };
        }

        public async Task<object> CancelSubscriptionAsync(Guid userId)
        {
            var company = await GetCompanyForUserAsync(userId);
            if (company == null)
                throw new RegistrationException("Company not found.", 404);

            if (company.SubscriptionStatus != "active"
                || !company.SubscriptionEnd.HasValue
                || company.SubscriptionEnd <= DateTime.UtcNow)
                throw new RegistrationException("Only an active subscription can be cancelled.", 409);

            if (company.CancelAtPeriodEnd)
                throw new RegistrationException("Subscription is already scheduled for cancellation.", 409);

            company.CancelAtPeriodEnd = true;
            await _db.SaveChangesAsync();
            return ToSubscriptionResponse(company);
        }

        public async Task<object> ReactivateSubscriptionAsync(Guid userId)
        {
            var company = await GetCompanyForUserAsync(userId);
            if (company == null)
                throw new RegistrationException("Company not found.", 404);

            if (company.SubscriptionStatus != "active"
                || !company.SubscriptionEnd.HasValue
                || company.SubscriptionEnd <= DateTime.UtcNow)
                throw new RegistrationException("Only an active, unexpired subscription can be reactivated.", 409);

            if (!company.CancelAtPeriodEnd)
                throw new RegistrationException("Subscription is not scheduled for cancellation.", 409);

            company.CancelAtPeriodEnd = false;
            await _db.SaveChangesAsync();
            return ToSubscriptionResponse(company);
        }

        private async Task<Company?> GetCompanyForUserAsync(Guid userId)
        {
            return await _db.Profiles
                .Where(p => p.Id == userId)
                .Select(p => p.Company)
                .FirstOrDefaultAsync();
        }

        private static object ToSubscriptionResponse(Company company) => new
        {
            plan = company.SubscriptionPlan,
            status = company.SubscriptionStatus,
            subscriptionEnd = company.SubscriptionEnd,
            trialUse = company.TrialUse,
            cancelAtPeriodEnd = company.CancelAtPeriodEnd,
        };

        public async Task<RegisterResponse> CreateRenewalPaymentAsync(Guid userId, string plan)
        {
            plan = plan.Trim().ToLowerInvariant();
            if (!PaidPlans.Contains(plan))
                throw new RegistrationException("Only monthly and yearly renewal are available.", 400);

            var profile = await _db.Profiles
                .Include(p => p.Company)
                .FirstOrDefaultAsync(p => p.Id == userId);
            var company = profile?.Company;
            if (company == null)
                throw new RegistrationException("Company not found.", 404);

            var now = DateTime.UtcNow;
            var isExpired = company.SubscriptionStatus is "expired" or "pending"
                || company.SubscriptionEnd.HasValue && company.SubscriptionEnd <= now;
            if (!isExpired)
                throw new RegistrationException("Subscription renewal is not required.", 409);

            var amountKey = plan == "monthly" ? "Registration:MonthlyAmount" : "Registration:YearlyAmount";
            var amount = _config.GetValue<decimal?>(amountKey);
            if (!amount.HasValue || amount <= 0)
                throw new RegistrationException($"{amountKey} is not configured.", 400);

            company.SubscriptionStatus = "pending";
            await _db.SaveChangesAsync();

            try
            {
                var payment = await _paymentService.CreateRenewalPaymentAsync(company.Id, plan, amount.Value);
                return new RegisterResponse
                {
                    CompanyId = company.Id,
                    RequiresPayment = true,
                    SubscriptionPlan = plan,
                    SubscriptionStatus = "pending",
                    SnapToken = payment.Token,
                    RedirectUrl = payment.RedirectUrl,
                };
            }
            catch
            {
                company.SubscriptionStatus = "expired";
                await _db.SaveChangesAsync();
                throw;
            }
        }

        public async Task<bool> IsSubscriptionBlockedAsync(Guid userId)
        {
            var company = await _db.Profiles
                .Where(p => p.Id == userId)
                .Select(p => p.Company)
                .FirstOrDefaultAsync();

            if (company == null || company.SubscriptionStatus == "pending")
                return company != null;

            if (company.SubscriptionStatus == "expired"
                || company.SubscriptionEnd.HasValue && company.SubscriptionEnd <= DateTime.UtcNow)
            {
                if (company.SubscriptionStatus != "expired")
                {
                    company.SubscriptionStatus = "expired";
                    await _db.SaveChangesAsync();
                }
                return true;
            }

            return false;
        }

        public async Task HandlePaymentAsync(
            string orderId,
            string transactionStatus,
            string? transactionId = null,
            string? paymentMethod = null)
        {
            var parts = orderId.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || !string.Equals(parts[0], "CRM", StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(parts[2], out var companyId))
                return;

            var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId);
            if (company == null)
                return;

            var subscriptionPayment = await _db.SubscriptionPayments
                .FirstOrDefaultAsync(p => p.MidtransOrderId == orderId);
            if (subscriptionPayment != null)
            {
                subscriptionPayment.MidtransTransactionId = transactionId ?? subscriptionPayment.MidtransTransactionId;
                subscriptionPayment.PaymentMethod = paymentMethod ?? subscriptionPayment.PaymentMethod;
            }

            var isRenewal = string.Equals(parts[1], "REN", StringComparison.OrdinalIgnoreCase);
            var isRegistration = string.Equals(parts[1], "REG", StringComparison.OrdinalIgnoreCase);
            if ((!isRenewal && !isRegistration) || company.SubscriptionStatus != "pending")
                return;

            var plan = isRenewal && parts.Length >= 4 ? parts[3] : company.SubscriptionPlan;
            if (plan is not ("monthly" or "yearly"))
                return;

            if (transactionStatus is "settlement" or "capture")
            {
                var subscriptionStart = DateTime.UtcNow;
                var subscriptionEnd = plan == "yearly"
                    ? subscriptionStart.AddYears(1)
                    : subscriptionStart.AddMonths(1);

                if (subscriptionPayment != null)
                {
                    subscriptionPayment.Status = "paid";
                    subscriptionPayment.PaidAt = subscriptionStart;
                    subscriptionPayment.SubscriptionStart = subscriptionStart;
                    subscriptionPayment.SubscriptionEnd = subscriptionEnd;
                }

                company.SubscriptionStatus = "active";
                company.SubscriptionPlan = plan;
                company.SubscriptionEnd = subscriptionEnd;
                company.CancelAtPeriodEnd = false;
                await _db.SaveChangesAsync();
                return;
            }

            if (transactionStatus == "pending")
            {
                if (subscriptionPayment != null)
                {
                    subscriptionPayment.Status = "pending";
                    await _db.SaveChangesAsync();
                }
                return;
            }

            if (transactionStatus is "expire" or "deny" or "cancel" or "failure")
            {
                if (subscriptionPayment != null)
                    subscriptionPayment.Status = "failed";

                if (isRenewal)
                {
                    company.SubscriptionStatus = "expired";
                    await _db.SaveChangesAsync();
                }
                else
                {
                    await CleanupPendingAsync(company.Id, null);
                }
            }
        }

        private async Task<bool> HasUsedTrialAsync(string email)
        {
            return await _db.Companies.AnyAsync(c => c.SupportEmail != null && c.SupportEmail.ToLower() == email && c.TrialUse)
                || await _db.Profiles.AnyAsync(p => p.Email != null && p.Email.ToLower() == email && p.Company != null && p.Company.TrialUse);
        }

        private async Task<Guid> CreateAuthUserAsync(string email, string password)
        {
            var url = _config["Supabase:Url"] ?? throw new InvalidOperationException("Supabase URL is not configured.");
            var serviceKey = _config["Supabase:ServiceKey"] ?? throw new InvalidOperationException("Supabase service key is not configured.");
            var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{url.TrimEnd('/')}/auth/v1/admin/users")
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    email,
                    password,
                    email_confirm = true,
                }), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("apikey", serviceKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceKey);

            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new RegistrationException("Unable to create account: " + ExtractError(body), (int)response.StatusCode);

            using var document = JsonDocument.Parse(body);
            return document.RootElement.GetProperty("id").GetGuid();
        }

        public async Task CleanupPendingAsync(int companyId, Guid? knownAuthUserId)
        {
            if (companyId > 0)
            {
                var authUserIds = await _db.Profiles
                    .Where(p => p.CompanyId == companyId)
                    .Select(p => p.Id)
                    .ToListAsync();
                var profileIds = authUserIds;

                await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                     {
                         await using var transaction = await _db.Database.BeginTransactionAsync();
                         _db.SubscriptionPayments.RemoveRange(await _db.SubscriptionPayments.Where(p => p.CompanyId == companyId).ToListAsync());
                         _db.Profiles.RemoveRange(await _db.Profiles.Where(p => p.CompanyId == companyId).ToListAsync());
                         _db.RolePermissions.RemoveRange(await _db.RolePermissions.Where(p => p.Role!.CompanyId == companyId).ToListAsync());
                         _db.Roles.RemoveRange(await _db.Roles.Where(r => r.CompanyId == companyId).ToListAsync());
                         var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId);
                         if (company != null)
                             _db.Companies.Remove(company);
                         await _db.SaveChangesAsync();
                         await transaction.CommitAsync();
                     });

                foreach (var authUserId in profileIds)
                    await DeleteAuthUserAsync(authUserId);
            }

            if (knownAuthUserId.HasValue && !await _db.Profiles.AnyAsync(p => p.Id == knownAuthUserId.Value))
                await DeleteAuthUserAsync(knownAuthUserId.Value);
        }

        private async Task DeleteAuthUserAsync(Guid userId)
        {
            try
            {
                var url = _config["Supabase:Url"];
                var serviceKey = _config["Supabase:ServiceKey"];
                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(serviceKey)) return;

                var client = _httpClientFactory.CreateClient();
                using var request = new HttpRequestMessage(HttpMethod.Delete, $"{url.TrimEnd('/')}/auth/v1/admin/users/{userId}");
                request.Headers.Add("apikey", serviceKey);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceKey);
                await client.SendAsync(request);
            }
            catch
            {
                // The database cleanup still prevents the account from accessing the CRM.
            }
        }

        private static string ExtractError(string body)
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                foreach (var key in new[] { "msg", "message", "error_description", "error" })
                    if (document.RootElement.TryGetProperty(key, out var value))
                        return value.GetString() ?? "Request failed.";
            }
            catch { }
            return "Request failed.";
        }
    }

    public sealed class RegistrationException : Exception
    {
        public int StatusCode { get; }

        public RegistrationException(string message, int statusCode)
            : base(message) => StatusCode = statusCode;
    }
}
