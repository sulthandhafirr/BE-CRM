using CRM.Api.Data;
using CRM.Api.Models;
using CRM.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace CRM.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UserManagementController : BaseController
    {
        private readonly AppDbContext _db;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;

        public UserManagementController(
            AppDbContext db,
            RoleService roleService,
            IHttpClientFactory httpClientFactory,
            IConfiguration config)
            : base(roleService)
        {
            _db = db;
            _httpClientFactory = httpClientFactory;
            _config = config;
        }

        [HttpPost("add")]
        public async Task<IActionResult> AddUser([FromBody] AddUserRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Email))
                return BadRequest(new AddUserResponse { Success = false, Message = "Name and email are required." });

            if (request.AuthUserId == Guid.Empty)
                return BadRequest(new AddUserResponse { Success = false, Message = "AuthUserId is required." });

            if (request.RoleId < 1 || request.RoleId > 3)
                return BadRequest(new AddUserResponse { Success = false, Message = "Invalid role." });

            var adminId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var adminProfile = await _db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == adminId);
            if (adminProfile == null)
                return Unauthorized(new AddUserResponse { Success = false, Message = "Admin not found." });

            var emailExists = await _db.Profiles.AnyAsync(p => p.Email == request.Email);
            if (emailExists)
                return Conflict(new AddUserResponse { Success = false, Message = "Email already exists." });

            try
            {
                var profile = new Profile
                {
                    Id        = request.AuthUserId,
                    Email     = request.Email,
                    Name      = request.Name,
                    RoleId    = request.RoleId,
                    Position  = string.IsNullOrWhiteSpace(request.Position) ? null : request.Position,
                    CompanyId = adminProfile.CompanyId,
                };

                _db.Profiles.Add(profile);
                await _db.SaveChangesAsync();

                return Ok(new AddUserResponse
                {
                    Success = true,
                    Message = "User created successfully.",
                    User = new AddedUserDto
                    {
                        Id        = profile.Id,
                        Name      = profile.Name ?? string.Empty,
                        Email     = profile.Email ?? string.Empty,
                        RoleId    = profile.RoleId,
                        Position  = profile.Position,
                        CompanyId = profile.CompanyId,
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new AddUserResponse { Success = false, Message = ex.Message });
            }
        }
    }
}