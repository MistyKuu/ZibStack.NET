using Microsoft.EntityFrameworkCore;
using ZibStack.NET.EntityFramework;
using SampleApi.Models;

namespace SampleApi;

[GenerateCrudStores]
public class AppDbContext : DbContext
{
    public DbSet<Product> Products => Set<Product>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyGeneratedConfigurations();
    }
}
