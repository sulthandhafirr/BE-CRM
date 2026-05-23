using CRM.Api.Data;
using CRM.Api.Models;

namespace CRM.Api.Services
{
    public class NotificationService
    {
        private readonly AppDbContext _db;

        public NotificationService(AppDbContext db)
        {
            _db = db;
        }

        public async Task CreateAsync(Guid userId, string message)
        {
            var notification = new Notification
            {
                UserId = userId,
                Message = message,
                IsRead = false,
                CreatedAt = DateTime.UtcNow,
            };

            _db.Notifications.Add(notification);
            await _db.SaveChangesAsync();
        }
    }
}