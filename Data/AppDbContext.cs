using Microsoft.EntityFrameworkCore;
using CRM.Api.Models;

namespace CRM.Api.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options) { }

        public DbSet<Profile> Profiles { get; set; } = null!;
        public DbSet<Role> Roles { get; set; } = null!;
        public DbSet<Skill> Skills { get; set; } = null!;
        public DbSet<ProfileSkill> ProfileSkills { get; set; } = null!;
        public DbSet<Ticket> Tickets { get; set; } = null!;
        public DbSet<Intent> Intents { get; set; } = null!;
        public DbSet<PriorityList> Priorities { get; set; } = null!;
        public DbSet<TicketAttachment> TicketAttachments { get; set; } = null!;
        public DbSet<TicketComment> TicketComments { get; set; } = null!;
        public DbSet<Company> Companies { get; set; } = null!;
        public DbSet<Notification> Notifications { get; set; } = null!;
        public DbSet<Tier> Tiers { get; set; } = null!;
        public DbSet<ProfileTier> ProfileTiers { get; set; } = null!;
        public DbSet<RolePermission> RolePermissions { get; set; } = null!;
        public DbSet<Payment> Payments { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ProfileSkill>()
                .HasKey(ps => new { ps.ProfileId, ps.SkillId });

            modelBuilder.Entity<ProfileTier>(entity =>
            {
                entity.HasKey(pt => pt.ProfileId);

                entity.HasOne(pt => pt.Tier)
                    .WithMany()
                    .HasForeignKey(pt => pt.TierId);

                entity.HasOne(pt => pt.Profile)
                    .WithMany(p => p.ProfileTiers)
                    .HasForeignKey(pt => pt.ProfileId);
            });
        }
    }
}