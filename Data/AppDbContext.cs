using Microsoft.EntityFrameworkCore;
using CRM.Api.Models;

namespace CRM.Api.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options) {}

        public DbSet<TestCityTemp> TestCityTemp { get; set; }
        public DbSet<Profile> Profiles { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<Ticket> Tickets { get; set; }
        public DbSet<PriorityList> Priorities { get; set; }
    }
}