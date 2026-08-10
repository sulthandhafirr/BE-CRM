using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using CRM.Api.Services;

namespace CRM.Api.Controllers
{
    public class BaseController : ControllerBase
    {
        protected readonly RoleService _roleService;

        public BaseController(RoleService roleService)
        {
            _roleService = roleService;
        }

        protected async Task<string?> GetCurrentUserRole()
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            return await _roleService.GetRoleAsync(userId);
        }
        protected async Task<(string? Role, int? CompanyId)> GetCurrentUserRoleAndCompany()
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            return await _roleService.GetRoleAndCompanyAsync(userId);
        }
    }
}