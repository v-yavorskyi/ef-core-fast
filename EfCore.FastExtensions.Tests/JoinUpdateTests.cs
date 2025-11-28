using EfCore.FastExtensions.SqlServer.Extensions;
using EfCore.FastExtensions.Tests.Configs;
using EfCore.FastExtensions.Tests.Models;
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

namespace EfCore.FastExtensions.Tests;
public class JoinUpdateTests
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
    public async Task ExecuteUpdate_Updates_Field_Correctly()
    {
        // arrange
        var db = await CreateDbAsync();
        try
        {
            await GenerateTestDataAsync(db);

            // act
            var affected = await db.Users
                .Include(x => x.State)
                .ThenInclude(x => x.Country)
                .Where(u => u.Id == 1 && u.State.Country.Id > 0)
                    .ExecuteUpdateJoinAsync(
                        x => x.SetProperty(p => p.Region, p => (p.State!.StateName.ToUpper()))
                    );

            db.ChangeTracker.Clear(); // caller explicitly chose this feature


            // assert
            var updated = await db.Users.Take(2).ToListAsync();
            Assert.Equal(1, affected);
            Assert.Equal("ALABAMA", updated[0].Region);
            Assert.Equal("Old2", updated[1].Region);

        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
            await db.DisposeAsync();
        }
    }
    [Fact]
    public async Task ExecuteUpdate_Uses_Navigations_From_Where_Clause()
    {
        // arrange
        var db = await CreateDbAsync();
        try
        {
            await GenerateTestDataAsync(db);

            // We intentionally use a WHERE clause that references deep navigations:
            // u.State.Country.Code == "US"
            //
            // BUT the SET expression does NOT reference State or Country!
            //
            // This test ensures the join tree discovers navigations from WHERE, not only SET.

            var affected = await db.Users
                .Include(u => u.State)
                .ThenInclude(s => s.Country)
                .Where(u => u.State!.Country!.Code == "US" && u.State.StateCode == "AB")
                .ExecuteUpdateJoinAsync(builder =>
                    builder.SetProperty(p => p.Region, p => "UPDATED")
                );

            db.ChangeTracker.Clear();

            // assert
            var users = await db.Users.OrderBy(u => u.Id).ToListAsync();

            Assert.Equal(1, affected);               // only first user
            Assert.Equal("UPDATED", users[0].Region); // AB (Alabama)
            Assert.Equal("Old2", users[1].Region);    // FL untouched
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