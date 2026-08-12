using System.Text.Json;
using System.Text.RegularExpressions;
using CRM.Api.Data;
using CRM.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CRM.Api.Services
{
    /// <summary>
    /// Background service that automatically exports the last 30 days of data
    /// (tickets, users, and/or a combined workbook) for every company that has
    /// enabled an export schedule, then emails the Excel file(s) to the
    /// configured recipients (falling back to the company support email and
    /// company admins). Runs once per minute and tracks the last successful
    /// send per company to avoid duplicates.
    /// </summary>
    public class ExportSchedulerService : BackgroundService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private static readonly string[] TicketHeaders =
        {
            "ID", "Created By", "Subject", "Description", "Chosen Priority", "Status",
            "First Response Time", "Resolved Time", "Created Date", "Assigned To", "Technician",
        };

        private static readonly string[] UserHeaders =
        {
            "Name", "Email", "Role", "Position", "Created Date",
        };

        private readonly IServiceScopeFactory _scopeFactory;

        public ExportSchedulerService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessSchedulesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ExportScheduler] Unexpected error: {ex.Message}");
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        private async Task ProcessSchedulesAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var excelService = scope.ServiceProvider.GetRequiredService<ExcelExportService>();
            var emailService = scope.ServiceProvider.GetRequiredService<EmailService>();
            var roleService = scope.ServiceProvider.GetRequiredService<RoleService>();

            var companies = await db.Companies
                .AsNoTracking()
                .ToListAsync(stoppingToken);

            foreach (var company in companies)
            {
                try
                {
                    await ProcessCompanyAsync(
                        db, excelService, emailService, roleService, company, stoppingToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ExportScheduler] Failed for company {company.Id} ({company.CompanyName}): {ex.Message}");
                }
            }
        }

        private async Task ProcessCompanyAsync(
            AppDbContext db,
            ExcelExportService excelService,
            EmailService emailService,
            RoleService roleService,
            Company company,
            CancellationToken stoppingToken)
        {
            var config = DeserializeConfig(company.ExportScheduleConfig);
            if (config is null || !config.Enabled) return;

            // Validate schedule values for the selected frequency
            if (!IsValidSchedule(config)) return;
            if (!TimeSpan.TryParse(config.Time, out var scheduledTime)) return;

            // Use the company local time to decide when to fire
            var localNow = ToCompanyLocal(DateTime.UtcNow, company.Timezone);
            if (!IsDueToday(localNow, config)) return;
            if (localNow.TimeOfDay < scheduledTime) return;

            var localDateKey = localNow.ToString("yyyy-MM-dd");
            if (config.LastSentDate == localDateKey) return; // already sent today

            // Data window: last 30 days (rolling month)
            var endDate = DateTime.UtcNow;
            var startDate = endDate.AddDays(-30);

            var tickets = await LoadTicketsAsync(db, company.Id, startDate, endDate, stoppingToken);
            var users = await LoadUsersAsync(db, company.Id, stoppingToken);

            var attachments = BuildAttachments(config, tickets, users, localNow, excelService);
            if (attachments.Count == 0)
            {
                Console.WriteLine($"[ExportScheduler] Company {company.Id}: nothing to export (no data selected).");
                return;
            }

            var recipients = await ResolveRecipientsAsync(roleService, company, config);
            if (recipients.Count == 0)
            {
                Console.WriteLine($"[ExportScheduler] Company {company.Id}: no recipients configured, skipping send.");
                return;
            }

            var period = $"{startDate:dd MMM yyyy} - {endDate:dd MMM yyyy}";
            var reportLabel = config.Frequency switch
            {
                "daily" => "daily",
                "weekly" => "weekly",
                _ => "monthly",
            };

            var sendResults = new List<bool>();
            foreach (var (email, name) in recipients)
            {
                sendResults.Add(await emailService.SendExportReportAsync(
                    email, name, company.CompanyName ?? "CRM", reportLabel, period, attachments));
            }

            // Only mark as sent when at least one recipient actually received it.
            // If SMTP is unavailable, the next tick retries automatically.
            if (!sendResults.Any(s => s))
            {
                Console.WriteLine($"[ExportScheduler] Company {company.Id}: email delivery failed for all recipients, will retry next tick.");
                return;
            }

            var tracked = await db.Companies.FirstOrDefaultAsync(c => c.Id == company.Id, stoppingToken);
            if (tracked is not null)
            {
                var updatedConfig = DeserializeConfig(tracked.ExportScheduleConfig) ?? new ExportScheduleConfigDto();
                updatedConfig.LastSentDate = localDateKey;
                tracked.ExportScheduleConfig = JsonSerializer.Serialize(updatedConfig, JsonOptions);
                await db.SaveChangesAsync(stoppingToken);
            }

            Console.WriteLine($"[ExportScheduler] Company {company.Id}: sent {attachments.Count} file(s) to {recipients.Count} recipient(s) for {period}.");
        }

        // ── Data loading ────────────────────────────────────────────────

        private static async Task<List<Ticket>> LoadTicketsAsync(
            AppDbContext db, int companyId, DateTime startDate, DateTime endDate, CancellationToken ct)
        {
            return await db.Tickets
                .AsNoTracking()
                .Include(t => t.Customer)
                .Include(t => t.Agent)
                .Include(t => t.Technician)
                .Include(t => t.UserChoosenPriority)
                .Where(t => t.Customer != null
                    && t.Customer.CompanyId == companyId
                    && t.CreatedAt >= startDate
                    && t.CreatedAt <= endDate)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync(ct);
        }

        private static async Task<List<Profile>> LoadUsersAsync(
            AppDbContext db, int companyId, CancellationToken ct)
        {
            return await db.Profiles
                .AsNoTracking()
                .Include(p => p.Role)
                .Where(p => p.CompanyId == companyId)
                .OrderBy(p => p.Name)
                .ToListAsync(ct);
        }

        // ── Excel generation ────────────────────────────────────────────

        private static List<(string FileName, byte[] Content)> BuildAttachments(
            ExportScheduleConfigDto config,
            List<Ticket> tickets,
            List<Profile> users,
            DateTime localNow,
            ExcelExportService excelService)
        {
            var attachments = new List<(string, byte[])>();
            // Monthly reports keep a month key, other frequencies use the full date
            var fileKey = config.Frequency == "monthly"
                ? localNow.ToString("yyyy-MM")
                : localNow.ToString("yyyy-MM-dd");

            if (config.IncludeTickets)
            {
                var sheet = BuildTicketSheet(tickets);
                attachments.Add(($"tickets_{fileKey}.xlsx", excelService.BuildWorkbook(sheet)));
            }

            if (config.IncludeUsers)
            {
                var sheet = BuildUserSheet(users);
                attachments.Add(($"users_{fileKey}.xlsx", excelService.BuildWorkbook(sheet)));
            }

            if (config.IncludeCombined)
            {
                attachments.Add((
                    $"tickets_users_{fileKey}.xlsx",
                    excelService.BuildWorkbook(BuildTicketSheet(tickets), BuildUserSheet(users))));
            }

            return attachments;
        }

        private static WorkbookSheet BuildTicketSheet(List<Ticket> tickets)
        {
            var rows = tickets
                .Select(t => new object?[]
                {
                    t.Id,
                    t.Customer?.Name ?? "-",
                    t.Subject ?? "-",
                    t.Description ?? "-",
                    t.UserChoosenPriority?.PriorityName ?? "-",
                    t.Status ?? "-",
                    FormatDate(t.FirstResponseAt),
                    FormatDate(t.ResolvedAt),
                    FormatDate(t.CreatedAt),
                    t.Agent?.Name ?? "-",
                    t.Technician?.Name ?? "-",
                })
                .ToList();

            return new WorkbookSheet("Tickets", TicketHeaders, rows);
        }

        private static WorkbookSheet BuildUserSheet(List<Profile> users)
        {
            var rows = users
                .Select(u => new object?[]
                {
                    u.Name ?? "-",
                    u.Email ?? "-",
                    u.Role?.RoleName ?? "-",
                    u.Position ?? "-",
                    FormatDate(u.CreatedAt),
                })
                .ToList();

            return new WorkbookSheet("Users", UserHeaders, rows);
        }

        // ── Schedule validation & matching ─────────────────────────────

        /// <summary>Validates the day values for the selected frequency.</summary>
        private static bool IsValidSchedule(ExportScheduleConfigDto config)
        {
            return config.Frequency switch
            {
                "daily" => true,
                "weekly" => config.DayOfWeek is >= 0 and <= 6,
                "monthly" => config.DayOfMonth is >= 1 and <= 31,
                _ => false,
            };
        }

        /// <summary>True when the given company-local date matches the schedule frequency.</summary>
        internal static bool IsDueToday(DateTime localNow, ExportScheduleConfigDto config)
        {
            return config.Frequency switch
            {
                "daily" => true,
                "weekly" => (int)localNow.DayOfWeek == config.DayOfWeek,
                "monthly" => localNow.Day == config.DayOfMonth,
                _ => false,
            };
        }

        // ── Recipients ──────────────────────────────────────────────────

        private static async Task<List<(string Email, string Name)>> ResolveRecipientsAsync(
            RoleService roleService,
            Company company,
            ExportScheduleConfigDto config)
        {
            var recipients = new List<(string Email, string Name)>();

            if (config.Recipients is { Count: > 0 })
            {
                foreach (var raw in config.Recipients)
                {
                    var email = raw.Trim();
                    if (IsValidEmail(email))
                    {
                        recipients.Add((email, company.CompanyName ?? "CRM"));
                    }
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(company.SupportEmail))
                {
                    recipients.Add((company.SupportEmail.Trim(), company.CompanyName ?? "CRM"));
                }

                var admins = await roleService.GetUsersByRoleAsync(company.Id, "admin");
                foreach (var admin in admins)
                {
                    if (!string.IsNullOrWhiteSpace(admin.Email))
                    {
                        recipients.Add((admin.Email.Trim(), admin.Name ?? "Admin"));
                    }
                }
            }

            return recipients
                .Where(r => !string.IsNullOrWhiteSpace(r.Email))
                .DistinctBy(r => r.Email.ToLowerInvariant())
                .ToList();
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static ExportScheduleConfigDto? DeserializeConfig(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<ExportScheduleConfigDto>(json, JsonOptions);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Converts a UTC instant to the company local time based on the
        /// stored timezone label (e.g. "WIB (UTC+7)"). Defaults to UTC+7.
        /// </summary>
        internal static DateTime ToCompanyLocal(DateTime utcNow, string? timezone)
        {
            var offsetMinutes = 7 * 60;
            if (!string.IsNullOrWhiteSpace(timezone))
            {
                var match = Regex.Match(timezone, @"UTC([+-]\d+)", RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var hours))
                {
                    offsetMinutes = hours * 60;
                }
                else if (timezone.Contains("WIB", StringComparison.OrdinalIgnoreCase))
                {
                    offsetMinutes = 7 * 60;
                }
                else if (timezone.Contains("WITA", StringComparison.OrdinalIgnoreCase))
                {
                    offsetMinutes = 8 * 60;
                }
                else if (timezone.Contains("WIT", StringComparison.OrdinalIgnoreCase))
                {
                    offsetMinutes = 9 * 60;
                }
            }

            return utcNow.AddMinutes(offsetMinutes);
        }

        private static string FormatDate(DateTime? date)
            => date?.ToString("dd/MM/yyyy HH:mm") ?? "-";

        private static bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }
    }
}
