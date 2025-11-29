using EfCore.FastExtensions.SqlServer.Extensions;
using EfCore.FastExtensions.SqlServer.Tests.Configs;
using EfCore.FastExtensions.SqlServer.Tests.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;

namespace EfCore.FastExtensions.SqlServer.Tests;
public class JoinDeleteTests
{

    class TestDb : DbContext
    {
        public DbSet<User> Users => Set<User>();
        public DbSet<Country> Countries => Set<Country>();
        public DbSet<State> States => Set<State>();
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Country>().HasKey(c => c.Id);

            modelBuilder.Entity<User>()
                .HasOne(u => u.State)  
                .WithMany()    
                .HasForeignKey(u => u.StateId)
                .OnDelete(DeleteBehavior.Restrict); // prevent cascade delete (optional)

            modelBuilder.Entity<State>()
                .HasOne(u => u.Country)      
                .WithMany(c => c.States) 
                .HasForeignKey(u => u.CountryId)
                .OnDelete(DeleteBehavior.Restrict); // prevent cascade delete (optional)
        }

        public TestDb(DbContextOptions<TestDb> options) : base(options) { }
    }

    private async Task<TestDb> CreateDbAsync()
    {
        var connectionString = DbConfig.GetSqlServerConnectionString() ?? throw new KeyNotFoundException("Connection string not found.");

        var options = new DbContextOptionsBuilder<TestDb>()
            // put your local connection string here:
            .UseSqlServer(connectionString)
            .EnableSensitiveDataLogging()
            .Options;

        var db = new TestDb(options);
        await db.Database.EnsureCreatedAsync();

        return db;
    }

    [Fact]
    public async Task ExecuteDelete_Deletes_Only_Matching_Rows()
    {
        var db = await CreateDbAsync();
        try
        {
            await GenerateTestDataAsync(db);

            // Delete user whose StateCode == AB
            var deleted = await db.Users
                .Include(u => u.State)
                .ThenInclude(s => s.Country)
                .Where(u => u.State!.StateCode == "AB")
                .ExecuteDeleteJoinAsync(CancellationToken.None);

            db.ChangeTracker.Clear();

            var users = await db.Users.OrderBy(u => u.Id).ToListAsync();

            // Expect: 1 user deleted
            Assert.Equal(1, deleted);
            Assert.Single(users);
            Assert.Equal("Old2", users[0].Region); // the Florida user remains
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
            await db.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExecuteDelete_Uses_Navigations_From_Where_Clause()
    {
        var db = await CreateDbAsync();

        try
        {
            await GenerateTestDataAsync(db);

            // Delete users whose Country.Code == "US"
            var deleted = await db.Users
                .Include(u => u.State)
                .ThenInclude(s => s.Country)
                .Where(u => u.State!.Country!.Code == "US")
                .ExecuteDeleteJoinAsync(CancellationToken.None);

            db.ChangeTracker.Clear();

            var users = await db.Users.ToListAsync();

            // Both users belong to Country=US → both deleted
            Assert.Equal(2, deleted);
            Assert.Empty(users);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
            await db.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExecuteDelete_Where_Uses_Navigations_Not_In_Set()
    {
        var db = await CreateDbAsync();

        try
        {
            await GenerateTestDataAsync(db);

            // WHERE uses deep navigation:
            //     u.State.Country.Name == "North America"
            //
            // This ensures JOIN tree must detect navigations from WHERE only,
            // exactly like UPDATE tests do.

            var deleted = await db.Users
                .Where(u => u.State!.Country!.Name == "North America" &&
                            u.State.StateCode == "FL")
                .ExecuteDeleteJoinAsync();

            db.ChangeTracker.Clear();

            var users = await db.Users.OrderBy(u => u.Id).ToListAsync();

            Assert.Equal(1, deleted);
            Assert.Single(users);
            Assert.Equal("Old1", users[0].Region);  // Alabama user remains
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
            await db.DisposeAsync();
        }
    }


    private static async Task GenerateTestDataAsync(TestDb db)
    {
        var country = new Country
        {
            Code = "US",
            Name = "North America",
            States = new Collection<State>()
            {
                new State { StateCode = "AB", StateName = "Alabama" },
                new State { StateCode = "FL", StateName = "Florida" }
            }
        };
        db.Countries.Add(country);
        await db.SaveChangesAsync();
        db.Users.Add(new User { Region = "Old1", StateId = country.States.First().Id });
        db.Users.Add(new User { Region = "Old2", StateId = country.States.Last().Id });
        await db.SaveChangesAsync();
    }
}