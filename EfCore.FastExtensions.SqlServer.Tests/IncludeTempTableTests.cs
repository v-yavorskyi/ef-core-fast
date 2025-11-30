using EfCore.FastExtensions.SqlServer.Enums;
using EfCore.FastExtensions.SqlServer.Extensions;
using EfCore.FastExtensions.SqlServer.Tests.Configs;
using EfCore.FastExtensions.SqlServer.Tests.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

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
    [Fact]
    public void IncludeTempTable_ThrowsWhenArgumentsNull()
    {
        Assert.Throws<ArgumentNullException>(() => ((IQueryable<User>)null!).IncludeTempTable<User, TempDto, int>(
            user => user.Id,
            Array.Empty<TempDto>(),
            dto => dto.UserId));

        Assert.Throws<ArgumentNullException>(() => Array.Empty<User>().AsQueryable().IncludeTempTable<User, TempDto, int>(
            null!,
            Array.Empty<TempDto>(),
            dto => dto.UserId));

        Assert.Throws<ArgumentNullException>(() => Array.Empty<User>().AsQueryable().IncludeTempTable<User, TempDto, int>(
            user => user.Id,
            null!,
            dto => dto.UserId));

        Assert.Throws<ArgumentNullException>(() => Array.Empty<User>().AsQueryable().IncludeTempTable<User, TempDto, int>(
            user => user.Id,
            Array.Empty<TempDto>(),
            null!));
    }

    [Fact]
    public async Task ExecuteSelectAsync_ThrowsWhenArgumentsNull()
    {
        Expression<Func<User, User>> selector = user => user;
        Func<IQueryable<TempDto>, IQueryable<TempDto>> tempSelector = temp => temp;
        Func<User, TempDto, User> resultSelector = (user, _) => user;

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => IncludeTempTableExtensions.ExecuteSelectAsync<User, TempDto, int, User, User>(
                null!, selector, resultSelector));

        await using var context = CreateSqlServerContext();
        var queryable = context.Users.IncludeTempTable<User, TempDto, int>(user => user.Id, Array.Empty<TempDto>(), dto => dto.UserId);

        await Assert.ThrowsAsync<ArgumentNullException>(() => queryable.ExecuteSelectAsync<User, TempDto, int, User, User>(
            null!, resultSelector));

        await Assert.ThrowsAsync<ArgumentNullException>(() => queryable.ExecuteSelectAsync<User, TempDto, int, User, User>(
            selector, null!));
    }

    [Fact]
    public async Task ExecuteSelectAsync_ReturnsEmptyWhenTempTableDataDoesNotMatch()
    {
        await using var context = CreateSqlServerContext();
        context.Users.Add(new User { Region = "One", State = new State() { StateCode = "AB", Country = new Country() } });
        await context.SaveChangesAsync();

        var tempDtos = new[] { new TempDto { UserId = 3, Label = "alpha" } };
        var tempQuery = context.Users.IncludeTempTable<User, TempDto, int>(user => user.Id, tempDtos, dto => dto.UserId, SqlJoinType.Inner);

        var results = await tempQuery.ExecuteSelectAsync(
            selector: user => user,
            resultSelector: (user, _) => user);

        Assert.Empty(results);
    }
}