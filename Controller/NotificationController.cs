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
    [Route("api/notifications")]
    public class NotificationController : BaseController
    {
        private readonly AppDbContext _db;

        public NotificationController(AppDbContext db, RoleService roleService)
            : base(roleService)
        {
            _db = db;
        }

        // GET /api/notifications - get current user's notifications
        [HttpGet]
        public async Task<IActionResult> GetNotifications()
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var notifications = await _db.Notifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .AsNoTracking()
                .Select(n => new
                {
                    id = n.Id,
                    message = n.Message,
                    isRead = n.IsRead,
                    createdAt = n.CreatedAt,
                })
                .ToListAsync();

            return Ok(notifications);
        }

        // PUT /api/notifications/{id}/read - mark one as read
        [HttpPut("{id}/read")]
        public async Task<IActionResult> MarkAsRead(long id)
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var notification = await _db.Notifications
                .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);

            if (notification == null) return NotFound();

            notification.IsRead = true;
            await _db.SaveChangesAsync();

            return Ok(new { message = "Marked as read" });
        }

        // PUT /api/notifications/read-all - mark all as read
        [HttpPut("read-all")]
        public async Task<IActionResult> MarkAllAsRead()
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            await _db.Notifications
                .Where(n => n.UserId == userId && n.IsRead == false)
                .ExecuteUpdateAsync(n => n.SetProperty(p => p.IsRead, true));

            return Ok(new { message = "All marked as read" });
        }
    }
}