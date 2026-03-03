using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using CRM.Api.Data;
using CRM.Api.Services;

namespace CRM.Api.Controllers
{
    [ApiController]
    [Route("api/cities")]
    public class TestCityTempController : BaseController
    {
        private readonly AppDbContext _db;

        public TestCityTempController(AppDbContext db, RoleService roleService)
            : base(roleService)
        {
            _db = db;
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var role = await GetCurrentUserRole();
            if (role != "cs_agent") return Forbid();

            var data = await _db.TestCityTemp
                                .AsNoTracking()
                                .ToListAsync();

            return Ok(data);
        }
    }
}
