using Microsoft.EntityFrameworkCore;
using CRM.Api.Data;
using CRM.Api.Models;

namespace CRM.Api.Services
{
    public class SlaCheckerService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public SlaCheckerService(IServiceScopeFactory scopeFactory)
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

                var breachedTickets = await db.Tickets
                    .Include(t => t.Customer)
                    .Include(t => t.Agent)
                    .Include(t => t.Technician)
                    .Where(t => !t.SlaBreached
                        && t.SlaDeadline < DateTime.UtcNow
                        && (t.ResolvedAt == null || t.ResolvedAt > t.SlaDeadline))
                    .ToListAsync(stoppingToken);

                foreach (var ticket in breachedTickets)
                {
                    ticket.SlaBreached = true;

                    var companyId = ticket.Customer?.CompanyId;
                    if (companyId.HasValue)
                    {
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

                        if (ticket.Customer is not null)
                        {
                            await notificationService.CreateAsync(
                                ticket.Customer.Id,
                                $"Your ticket #{ticket.Id} '{ticket.Subject}' is taking longer than expected to resolve. Our team is on it.");

                            if (!string.IsNullOrEmpty(ticket.Customer.Email))
                            {
                                _ = emailService.SendSlaBreachedCustomerAsync(
                                    ticket.Customer.Email,
                                    ticket.Customer.Name ?? "there",
                                    ticket.Subject ?? "Ticket",
                                    ticket.Id);
                            }
                        }

                        foreach (var recipient in recipients.DistinctBy(r => r.Id))
                        {
                            await notificationService.CreateAsync(
                                recipient.Id,
                                $"Ticket #{ticket.Id} '{ticket.Subject}' has breached its SLA deadline.");

                            if (!string.IsNullOrEmpty(recipient.Email))
                            {
                                _ = emailService.SendSlaBreachedAsync(
                                    recipient.Email,
                                    recipient.Name ?? "there",
                                    ticket.Subject ?? "Ticket",
                                    ticket.Id);
                            }
                        }
                    }

                    ticket.SlaBreachedNotified = true;
                }

                if (breachedTickets.Any())
                    await db.SaveChangesAsync(stoppingToken);

                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }
}