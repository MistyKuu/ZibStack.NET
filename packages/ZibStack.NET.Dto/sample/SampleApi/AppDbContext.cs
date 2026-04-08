using Microsoft.EntityFrameworkCore;
using ZibStack.NET.EntityFramework;
using ZibStack.NET.Dto.Sample.Models;

namespace ZibStack.NET.Dto.Sample;

[GenerateCrudStores]
public class AppDbContext : DbContext
{
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Team> Teams => Set<Team>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Player>(e =>
        {
            e.HasKey(p => p.Id);
            e.OwnsOne(p => p.Address);
        });

        modelBuilder.Entity<Team>(e =>
        {
            e.HasKey(t => t.Id);
        });
    }
}
