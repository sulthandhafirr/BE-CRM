using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CRM.Api.Models;
using CRM.Api.Data;

namespace CRM.Api.Controllers
{
    [ApiController]
    [Route("api/users")]
    [Authorize]
    public class UserController : ControllerBase
    {
        private readonly AppDbContext _context;

        public UserController(AppDbContext context)
        {
            _context = context;
        }

        // GET /api/users?role_id=2
        [HttpGet]
        public async Task<IActionResult> GetUsersByRole([FromQuery] int role_id)
        {
            var users = await _context.Profiles
                .Where(p => p.RoleId == role_id)
                .OrderBy(p => p.Name)
                .Select(p => new
                {
                    id       = p.Id,
                    name     = p.Name,
                    email    = p.Email,
                    position = p.Position,
                })
                .ToListAsync();

            return Ok(users);
        }
    }
}