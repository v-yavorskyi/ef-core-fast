using BenchmarkDotNet.Attributes;
using EfCore.FastExtensions.SqlServer.Benchmarks.Configs;
using EfCore.FastExtensions.SqlServer.Benchmarks.Models;
using EfCore.FastExtensions.SqlServer.Extensions;
using Microsoft.EntityFrameworkCore;

namespace EfCore.FastExtensions.SqlServer.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class JoinExtensionsBenchmarks
{
    private const int StatesCount = 20;
    private const int UsersPerState = 50;

    private DbContextOptions<BenchmarkDbContext> _options = null!;
    private string _connectionString = string.Empty;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _connectionString = DbConfig.GetSqlServerConnectionString()
            ?? throw new InvalidOperationException("SQL Server connection string was not provided.");

        _options = new DbContextOptionsBuilder<BenchmarkDbContext>()
            .UseSqlServer(_connectionString)
            .EnableSensitiveDataLogging()
            .Options;

        await using var db = new BenchmarkDbContext(_options);
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        await SeedDataAsync(db);
    }

    [IterationSetup]
    public void IterationSetup()
    {
        using var db = new BenchmarkDbContext(_options);

        // reset data to keep each iteration consistent
        DeleteIfExistsAsync(db).GetAwaiter().GetResult();
        SeedDataAsync(db).GetAwaiter().GetResult();
    }
    private async Task DeleteIfExistsAsync(BenchmarkDbContext db)
    {
        var sql = @"
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Users')
    DELETE FROM [Users];

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'States')
    DELETE FROM [States];

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Countries')
    DELETE FROM [Countries];
";

        await db.Database.ExecuteSqlRawAsync(sql);
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        await using var db = new BenchmarkDbContext(_options);
        await db.Database.EnsureDeletedAsync();
    }

    [Benchmark(Description = "Update using native EFCore method")]
    public async Task ExecuteUpdate_ViaSelectAndUpdateAsync()
    {
        await using var db = new BenchmarkDbContext(_options);

        var users = await db.Users
            .Include(u => u.State)
            .ThenInclude(s => s.Country)
            .Where(u => u.State!.Country!.Code == "US" && u.State.StateCode == "S10")
            .ToListAsync();

        users.ForEach(u => u.Region = u.State!.StateName);

        await db.SaveChangesAsync();
    }

    [Benchmark(Description = "ExecuteUpdateJoinAsync using navigation properties in SET and WHERE")]
    public async Task ExecuteUpdateJoinAsync_PublicExtensionAsync()
    {
        await using var db = new BenchmarkDbContext(_options);

        _ = await db.Users
            .Include(u => u.State)
            .ThenInclude(s => s.Country)
            .Where(u => u.State!.Country!.Code == "US" && u.State.StateCode == "S10")
            .ExecuteUpdateJoinAsync(builder =>
                builder.SetProperty(u => u.Region, u => u.State!.StateName));
    }

    [Benchmark(Description = "ExecuteDeleteJoinAsync using navigation properties in WHERE")]
    public async Task DeleteRow_EFCore_Async()
    {
        await using var db = new BenchmarkDbContext(_options);

        var users = await db.Users
            .Include(u => u.State)
            .ThenInclude(s => s.Country)
            .Where(u => u.State!.Country!.Name == "North America" && u.State.StateCode.StartsWith("S0"))
            .ToListAsync();

        db.Users.RemoveRange(users);

        await db.SaveChangesAsync();
    }

    [Benchmark(Description = "ExecuteDeleteJoinAsync using navigation properties in WHERE")]
    public async Task ExecuteDeleteJoinAsync_PublicExtensionAsync()
    {
        await using var db = new BenchmarkDbContext(_options);

        _ = await db.Users
            .Include(u => u.State)
            .ThenInclude(s => s.Country)
            .Where(u => u.State!.Country!.Name == "North America" && u.State.StateCode.StartsWith("S0"))
            .ExecuteDeleteJoinAsync();
    }

    private async Task SeedDataAsync(BenchmarkDbContext db)
    {
        var country = new Country
        {
            Code = "US",
            Name = "North America"
        };

        var states = Enumerable.Range(0, StatesCount)
            .Select(index => new State
            {
                StateCode = $"S{index:00}",
                StateName = $"State {index:00}",
                Country = country
            })
            .ToList();

        country.States = states;
        db.Countries.Add(country);
        await db.SaveChangesAsync();

        var users = new List<User>(StatesCount * UsersPerState);
        foreach (var state in states)
        {
            for (var i = 0; i < UsersPerState; i++)
            {
                users.Add(new User
                {
                    Region = $"Region {i:000}",
                    StateId = state.Id
                });
            }
        }

        await db.Users.AddRangeAsync(users);
        await db.SaveChangesAsync();
    }
}
