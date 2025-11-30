using EfCore.FastExtensions.SqlServer.Extensions;
using EfCore.FastExtensions.SqlServer.Tests.Configs;
using EfCore.FastExtensions.SqlServer.Tests.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EfCore.FastExtensions.SqlServer.Tests;

public class IncludeTempTableTests
{
    private sealed class BulkInsertDbContext(DbContextOptions<BulkInsertDbContext> options) : DbContext(options)
    {
        public DbSet<User> Users => Set<User>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().ToTable("Users");
            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<User>()
                .Property(u => u.Id)
                .ValueGeneratedOnAdd();
        }
    }
    private sealed class TempDto
    {
        public int UserId { get; set; }
        public string Label { get; set; } = string.Empty;
        public bool Active { get; set; }
    }

    private static BulkInsertDbContext CreateSqlLiteContext()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<BulkInsertDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new BulkInsertDbContext(options);
        context.Database.EnsureCreated();

        return context;
    }
    private static BulkInsertDbContext CreateSqlServerContext()
    {
        var connectionString = DbConfig.GetSqlServerConnectionString() ?? throw new KeyNotFoundException("Connection string not found.");

        var options = new DbContextOptionsBuilder<BulkInsertDbContext>()
            // put your local connection string here:
            .UseSqlServer(connectionString)
            .EnableSensitiveDataLogging()
            .Options;

        var db = new BulkInsertDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }



    [Fact]
    public async Task ExecuteSelectWithTempTableAsync_SqlServer()
    {
        await using var db = CreateSqlServerContext();

        db.Users.AddRange(
                new User { Region = "One", State = new State() { StateCode = "AB", Country = new Country() } },
                new User { Region = "Two", State = new State() { StateCode = "AB", Country = new Country() } },
                new User { Region = "Three", State = new State() { StateCode ="AB", Country = new Country() } });

        await db.SaveChangesAsync();

        var tempDtos = new[]
        {
                new TempDto { UserId = 1, Label = "alpha", Active = true },
                new TempDto { UserId = 3, Label = "beta", Active = true }
            };

        var results = await db.Users
            .IncludeTempTable(x => x.Id, tempDtos, dto => dto.UserId)
            .ExecuteSelectAsync((user, dto) => new { user.Id, dto.Label, user.Region });

        Assert.Collection(
            results.OrderBy(x => x.Id),
            first => Assert.Equal("One", first.Region),
            second => Assert.Equal("beta", second.Label));
    }
}