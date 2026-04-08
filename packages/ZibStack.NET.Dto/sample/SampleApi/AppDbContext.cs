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
        // Auto-generated entity configs (Required, MaxLength from validation attrs)
        modelBuilder.ApplyGeneratedConfigurations();
    }
}
