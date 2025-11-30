using EfCore.FastExtensions.SqlServer.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EfCore.FastExtensions.SqlServer.Tests;

public class BulkInsertTests
{
    private sealed class BulkInsertDbContext(DbContextOptions<BulkInsertDbContext> options) : DbContext(options)
    {
        public DbSet<BulkUser> Users => Set<BulkUser>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<BulkUser>().ToTable("BulkUsers");
            modelBuilder.Entity<BulkUser>().HasKey(u => u.Id);
            modelBuilder.Entity<BulkUser>()
                .Property(u => u.Id)
                .ValueGeneratedOnAdd();
        }
    }

    private sealed class BulkUser
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
    }

    private static BulkInsertDbContext CreateContext()
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

    [Fact]
    public async Task ExecuteBulkInsertAsync_Inserts_All_Entities_Across_Batches()
    {
        await using var db = CreateContext();

        var entities = new List<BulkUser>
        {
            new BulkUser { Name = "A", Region = "North" },
            new BulkUser { Name = "B", Region = "South" },
            new BulkUser { Name = "C", Region = "East" }
        };

        var affected = await db.Users.AsQueryable().ExecuteBulkInsertAsync(entities, batchSize: 2);

        Assert.Equal(entities.Count, affected);

        var stored = await db.Users.OrderBy(u => u.Name).ToListAsync();

        Assert.Collection(stored,
            u => { Assert.Equal("A", u.Name); Assert.Equal("North", u.Region); Assert.True(u.Id > 0); },
            u => { Assert.Equal("B", u.Name); Assert.Equal("South", u.Region); Assert.True(u.Id > 0); },
            u => { Assert.Equal("C", u.Name); Assert.Equal("East", u.Region); Assert.True(u.Id > 0); });
    }

    [Fact]
    public async Task ExecuteBulkInsertAsync_Returns_Zero_For_Empty_Input()
    {
        await using var db = CreateContext();

        var affected = await db.Users.AsQueryable().ExecuteBulkInsertAsync(new List<BulkUser>());

        Assert.Equal(0, affected);
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task ExecuteBulkInsertAsync_Skips_StoreGenerated_Property()
    {
        await using var db = CreateContext();

        var entity = new BulkUser { Name = "Generated", Region = "Central" };

        var affected = await db.Users.AsQueryable().ExecuteBulkInsertAsync(new List<BulkUser>() { entity });

        Assert.Equal(1, affected);

        var saved = await db.Users.SingleAsync();

        Assert.True(saved.Id > 0);
        Assert.Equal("Generated", saved.Name);
        Assert.Equal("Central", saved.Region);
    }
}