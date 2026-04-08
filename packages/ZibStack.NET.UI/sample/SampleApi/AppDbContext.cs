using Microsoft.EntityFrameworkCore;
using ZibStack.NET.EntityFramework;
using SampleApi.Models;

namespace SampleApi;

[GenerateCrudStores]
public class AppDbContext : DbContext
{
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Team> Teams => Set<Team>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyGeneratedConfigurations();
    }
}
