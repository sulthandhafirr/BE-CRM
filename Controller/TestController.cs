using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CRM.Api.Services;

namespace CRM.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TestController : BaseController
    {
        public TestController(RoleService roleService)
            : base(roleService)
        {
        }

        [Authorize]
        [HttpGet("ping")]
        public IActionResult Ping()
        {
            return Ok(new { message = "Backend is alive 🚀" });
        }

        [Authorize]
        [HttpGet("my-role")]
        public async Task<IActionResult> GetMyRole()
        {
            var role = await GetCurrentUserRole();
            return Ok(new { role });
        }
    }
}