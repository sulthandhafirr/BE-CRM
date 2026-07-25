using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CRM.Api.Models;
using CRM.Api.Services;

namespace CRM.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/roles")]
    public class RoleManagementController : BaseController
    {
        private readonly RoleManagementService _roleManagementService;

        public RoleManagementController(
            RoleService roleService,
            RoleManagementService roleManagementService)
            : base(roleService)
        {
            _roleManagementService = roleManagementService;
        }

        /// <summary>
        /// GET /api/roles — List all roles for the current user's company
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var (_, companyId) = await GetCurrentUserRoleAndCompany();
            if (companyId is null) return Unauthorized("User is not associated with a company.");

            var roles = await _roleManagementService.GetAllAsync(companyId.Value);
            return Ok(roles);
        }

        /// <summary>
        /// GET /api/roles/{id} — Get a single role by ID
        /// </summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var (_, companyId) = await GetCurrentUserRoleAndCompany();
            if (companyId is null) return Unauthorized("User is not associated with a company.");

            var result = await _roleManagementService.GetByIdAsync(companyId.Value, id);
            if (result is null) return NotFound("Role not found.");

            return Ok(result);
        }

        /// <summary>
        /// POST /api/roles — Create a custom role for the current company
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] RoleCreateDto dto)
        {
            var (_, companyId) = await GetCurrentUserRoleAndCompany();
            if (companyId is null) return Unauthorized("User is not associated with a company.");

            if (string.IsNullOrWhiteSpace(dto.Role))
                return BadRequest("Role name is required.");

            var created = await _roleManagementService.CreateAsync(companyId.Value, dto);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }

        /// <summary>
        /// PUT /api/roles/{id} — Update role permissions (and name for custom roles)
        /// </summary>
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] RoleUpdateDto dto)
        {
            var (_, companyId) = await GetCurrentUserRoleAndCompany();
            if (companyId is null) return Unauthorized("User is not associated with a company.");

            var updated = await _roleManagementService.UpdateAsync(companyId.Value, id, dto);
            if (updated is null) return NotFound("Role not found.");

            return Ok(updated);
        }

        /// <summary>
        /// DELETE /api/roles/{id} — Delete a custom role (system roles cannot be deleted)
        /// </summary>
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var (_, companyId) = await GetCurrentUserRoleAndCompany();
            if (companyId is null) return Unauthorized("User is not associated with a company.");

            var deleted = await _roleManagementService.DeleteAsync(companyId.Value, id);
            if (!deleted) return NotFound("Role not found or cannot be deleted.");

            return NoContent();
        }

        /// <summary>
        /// POST /api/roles/seed — Seed system roles for the current company (idempotent)
        /// </summary>
        [HttpPost("seed")]
        public async Task<IActionResult> Seed()
        {
            var (_, companyId) = await GetCurrentUserRoleAndCompany();
            if (companyId is null) return Unauthorized("User is not associated with a company.");

            await _roleManagementService.SeedSystemRolesAsync(companyId.Value);
            return Ok(new { message = "System roles seeded." });
        }
    }
}
