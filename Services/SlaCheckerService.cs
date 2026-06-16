using Microsoft.EntityFrameworkCore;
using CRM.Api.Data;

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

                var breachedTickets = await db.Tickets
                    .Where(t => !t.SlaBreached
                        && t.SlaDeadline < DateTime.UtcNow
                        && (t.ResolvedAt == null || t.ResolvedAt > t.SlaDeadline))
                    .ToListAsync(stoppingToken);

                foreach (var ticket in breachedTickets)
                {
                    ticket.SlaBreached = true;
                }

                if (breachedTickets.Any())
                    await db.SaveChangesAsync(stoppingToken);

                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }
}