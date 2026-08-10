using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using CRM.Api.Services;

namespace CRM.Api.Controllers
{
    [ApiController]
    [Route("api/recommendations")]
    public class TicketRecommendationController : BaseController
    {
        private readonly TicketRecommendationService _ticketRecommendationService;

        public TicketRecommendationController(TicketRecommendationService ticketRecommendationService, RoleService roleService) 
            : base(roleService)
        {
            _ticketRecommendationService = ticketRecommendationService;
        }

        // GET /api/recommendations - cs_agent only
        [HttpGet]
        public async Task<IActionResult> GetRecommendedTickets()
        {
            var (role, companyId) = await GetCurrentUserRoleAndCompany();
            if (role != "cs_agent") return Forbid();

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var recommendations = await _ticketRecommendationService.GetRecommendedTicketsAsync(userId, companyId);
            return Ok(recommendations);
        }
    }
}