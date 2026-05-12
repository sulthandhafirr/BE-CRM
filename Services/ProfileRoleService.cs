using CRM.Api.Data;
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
    }
}