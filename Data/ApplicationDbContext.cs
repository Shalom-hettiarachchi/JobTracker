using JobTracker.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<JobApplication> JobApplications => Set<JobApplication>();

    public DbSet<ApplicationNote> Notes => Set<ApplicationNote>();

    public DbSet<Reminder> Reminders => Set<Reminder>();

    public DbSet<Contact> Contacts => Set<Contact>();

    public DbSet<ActivityEntry> Activities => Set<ActivityEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<JobApplication>(entity =>
        {
            entity.HasIndex(a => new { a.UserId, a.Stage });
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Reminder>().HasIndex(r => new { r.IsDone, r.DueAt });
        builder.Entity<ActivityEntry>().HasIndex(x => x.OccurredAt);
    }
}
