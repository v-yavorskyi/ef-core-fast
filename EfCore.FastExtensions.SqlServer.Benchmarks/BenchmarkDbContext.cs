using EfCore.FastExtensions.SqlServer.Benchmarks.Models;
using Microsoft.EntityFrameworkCore;

namespace EfCore.FastExtensions.SqlServer.Benchmarks;

public class BenchmarkDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<State> States => Set<State>();
    public DbSet<Country> Countries => Set<Country>();

    public BenchmarkDbContext(DbContextOptions<BenchmarkDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasKey(u => u.Id);
        modelBuilder.Entity<Country>().HasKey(c => c.Id);

        modelBuilder.Entity<User>()
            .HasOne(u => u.State)
            .WithMany(s => s.Users)
            .HasForeignKey(u => u.StateId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<State>()
            .HasOne(s => s.Country)
            .WithMany(c => c.States)
            .HasForeignKey(s => s.CountryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
