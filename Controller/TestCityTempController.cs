using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CRM.Api.Data;

namespace CRM.Api.Controllers
{
    [ApiController]
    [Route("api/cities")]
    public class TestCityTempController : ControllerBase
    {
        private readonly AppDbContext _db;

        public TestCityTempController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var data = await _db.TestCityTemp
                                .AsNoTracking()
                                // .Where(c => c.Temperature > 30)
                                .ToListAsync();

            return Ok(data);
        }
    }
}
