using System.Text.Json;
using CRM.Api.Data;
using CRM.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CRM.Api.Services
{
    public class RoleManagementService
    {
        private readonly AppDbContext _db;

        private static readonly string[] SystemRoleNames =
            { "customer", "cs_agent", "technician", "admin", "ultrauser" };

        public RoleManagementService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<List<RoleResponse>> GetAllAsync(int companyId)
        {
            var roles = await _db.Roles
                .AsNoTracking()
                .Include(r => r.RolePermission)
                .Include(r => r.Profiles)
                .Where(r => r.CompanyId == companyId)
                .OrderBy(r => r.IsSystem ? 0 : 1)
                .ThenBy(r => r.RoleName)
                .ToListAsync();

            return roles.Select(MapToResponse).ToList();
        }

        public async Task<RoleResponse?> GetByIdAsync(int companyId, int id)
        {
            var role = await _db.Roles
                .AsNoTracking()
                .Include(r => r.RolePermission)
                .Include(r => r.Profiles)
                .FirstOrDefaultAsync(r => r.Id == id && r.CompanyId == companyId);

            return role is null ? null : MapToResponse(role);
        }

        public async Task<RoleResponse> CreateAsync(int companyId, RoleCreateDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Role))
                throw new ArgumentException("Role name is required.");

            var role = new Role
            {
                RoleName = dto.Role,
                CompanyId = companyId,
                IsSystem = false,
            };
            _db.Roles.Add(role);
            await _db.SaveChangesAsync();

            var permission = new RolePermission
            {
                RoleId = role.Id,
                Permissions = SerializePermissions(dto.Permissions),
            };
            _db.RolePermissions.Add(permission);
            await _db.SaveChangesAsync();

            role.RolePermission = permission;
            return MapToResponse(role);
        }

        public async Task<RoleResponse?> UpdateAsync(int companyId, int id, RoleUpdateDto dto)
        {
            var role = await _db.Roles
                .Include(r => r.RolePermission)
                .Include(r => r.Profiles)
                .FirstOrDefaultAsync(r => r.Id == id && r.CompanyId == companyId);

            if (role is null) return null;

            // Only allow renaming non-system roles
            if (!role.IsSystem && !string.IsNullOrWhiteSpace(dto.Role))
            {
                role.RoleName = dto.Role;
            }

            // Upsert permissions
            if (role.RolePermission is null)
            {
                role.RolePermission = new RolePermission
                {
                    RoleId = role.Id,
                    Permissions = SerializePermissions(dto.Permissions),
                };
                _db.RolePermissions.Add(role.RolePermission);
            }
            else
            {
                role.RolePermission.Permissions = SerializePermissions(dto.Permissions);
            }

            await _db.SaveChangesAsync();
            return MapToResponse(role);
        }

        public async Task<bool> DeleteAsync(int companyId, int id)
        {
            var role = await _db.Roles
                .FirstOrDefaultAsync(r => r.Id == id && r.CompanyId == companyId);

            if (role is null) return false;
            if (role.IsSystem) return false; // Cannot delete system roles

            _db.Roles.Remove(role);
            await _db.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// Seed 5 system roles for a newly registered company.
        /// Idempotent — skips roles that already exist.
        /// </summary>
        public async Task SeedSystemRolesAsync(int companyId)
        {
            foreach (var roleName in SystemRoleNames)
            {
                var exists = await _db.Roles
                    .AnyAsync(r => r.CompanyId == companyId && r.RoleName == roleName);

                if (!exists)
                {
                    _db.Roles.Add(new Role
                    {
                        RoleName = roleName,
                        CompanyId = companyId,
                        IsSystem = true,
                    });
                }
            }
            await _db.SaveChangesAsync();
        }

        private static RoleResponse MapToResponse(Role role)
        {
            return new RoleResponse
            {
                Id = role.Id,
                Role = role.RoleName ?? string.Empty,
                CompanyId = role.CompanyId,
                IsSystem = role.IsSystem,
                UserCount = role.Profiles?.Count ?? 0,
                Permissions = DeserializePermissions(role.RolePermission?.Permissions ?? "{}"),
            };
        }

        private static Dictionary<string, bool> DeserializePermissions(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, bool>>(json)
                    ?? new Dictionary<string, bool>();
            }
            catch
            {
                return new Dictionary<string, bool>();
            }
        }

        private static string SerializePermissions(Dictionary<string, bool> permissions)
        {
            return JsonSerializer.Serialize(permissions);
        }
    }
}
