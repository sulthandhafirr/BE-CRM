using System.Text.Json;
using CRM.Api.Data;
using CRM.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CRM.Api.Services
{
    public class SlaReminderService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        public SlaReminderService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var notificationService = scope.ServiceProvider.GetRequiredService<NotificationService>();
                var emailService = scope.ServiceProvider.GetRequiredService<EmailService>();
                var roleService = scope.ServiceProvider.GetRequiredService<RoleService>();

                var candidates = await db.Tickets
                    .Include(t => t.Customer)
                    .Include(t => t.Agent)
                    .Include(t => t.Technician)
                    .Where(t => t.SlaDeadline != null
                        && !t.SlaReminderSent
                        && t.SlaDeadline > DateTime.UtcNow)
                    .ToListAsync(stoppingToken);

                foreach (var ticket in candidates)
                {
                    var companyId = ticket.Customer?.CompanyId;
                    if (!companyId.HasValue) continue;

                    var notifyMinutes = await GetNotifyBeforeBreachedMinutesAsync(db, companyId.Value, stoppingToken);
                    if (notifyMinutes is null) continue; // monitoring off or config missing

                    var remaining = ticket.SlaDeadline!.Value - DateTime.UtcNow;
                    if (remaining.TotalMinutes > notifyMinutes.Value) continue; // not in window yet

                    var recipients = new List<Profile>();

                    var admins = await roleService.GetUsersByRoleAsync(companyId.Value, "admin");
                    recipients.AddRange(admins);

                    if (ticket.Agent is not null)
                    {
                        recipients.Add(ticket.Agent);
                    }
                    else
                    {
                        var agents = await roleService.GetUsersByRoleAsync(companyId.Value, "cs_agent");
                        recipients.AddRange(agents);
                    }

                    if (ticket.Technician is not null)
                        recipients.Add(ticket.Technician);

                    foreach (var recipient in recipients.DistinctBy(r => r.Id))
                    {
                        await notificationService.CreateAsync(
                            recipient.Id,
                            $"Ticket #{ticket.Id} '{ticket.Subject}' is approaching its SLA deadline (breaches in {(int)remaining.TotalMinutes} minutes).");

                        if (!string.IsNullOrEmpty(recipient.Email))
                        {
                            _ = emailService.SendSlaWarningAsync(
                                recipient.Email,
                                recipient.Name ?? "there",
                                ticket.Subject ?? "Ticket",
                                ticket.Id,
                                (int)remaining.TotalMinutes);
                        }
                    }

                    ticket.SlaReminderSent = true;
                }

                if (candidates.Any(t => t.SlaReminderSent))
                    await db.SaveChangesAsync(stoppingToken);

                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); // 5 minutes
            }
        }

        private static async Task<int?> GetNotifyBeforeBreachedMinutesAsync(AppDbContext db, int companyId, CancellationToken cancellationToken)
        {
            var company = await db.Companies
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);

            if (company?.SlaConfig is null) return null;

            try
            {
                var config = JsonSerializer.Deserialize<SlaRulesConfigDto>(company.SlaConfig, JsonOptions);
                if (config is null || !config.EnableSlaMonitoring) return null;
                return config.NotifyBeforeBreachedMinutes;
            }
            catch
            {
                return null;
            }
        }
    }
}