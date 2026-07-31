using CRM.Api.Data;
using CRM.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CRM.Api.Services
{
    public class RoleService
    {
        private readonly AppDbContext _db;

        public RoleService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<string?> GetRoleAsync(Guid userId)
        {
            var profile = await _db.Profiles
                .Include(p => p.Role)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == userId);

            return profile?.Role?.RoleName;
        }
        public async Task<(string? Role, int? CompanyId)> GetRoleAndCompanyAsync(Guid userId)
        {
            var profile = await _db.Profiles
                .Include(p => p.Role)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == userId);

            return (profile?.Role?.RoleName, profile?.CompanyId);
        }
        public async Task<List<Profile>> GetUsersByRoleAsync(int companyId, string roleName)
        {
            return await _db.Profiles
                .Include(p => p.Role)
                .AsNoTracking()
                .Where(p => p.CompanyId == companyId && p.Role!.RoleName == roleName)
                .ToListAsync();
        }
    }
}