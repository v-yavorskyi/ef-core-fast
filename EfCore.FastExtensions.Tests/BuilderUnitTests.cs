using EfCore.FastExtensions.SqlServer.Builders;
using EfCore.FastExtensions.Tests.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EfCore.FastExtensions.Tests;

public class BuilderUnitTests
{
    [Fact]
    public void SqlParameterAccumulator_AddsPlaceholdersAndDbNull()
    {
        var accumulator = new SqlParameterAccumulator();

        var firstPlaceholder = accumulator.Add(42);
        var secondPlaceholder = accumulator.Add(null);

        Assert.Equal("{0}", firstPlaceholder);
        Assert.Equal("{1}", secondPlaceholder);
        Assert.Equal(new object?[] { 42, DBNull.Value }, accumulator.ToArray());
    }

    [Fact]
    public void ExtractWhereExpressions_Returns_All_Predicates_In_Order()
    {
        IQueryable<User> queryable = new List<User>().AsQueryable();

        var query = queryable
            .Where(u => u.Id > 0)
            .Where(u => u.Region == "South");

        var predicates = WhereClauseBuilder.ExtractWhereExpressions(query.Expression);

        Assert.Collection(predicates,
            first => Assert.Equal("u.Id > 0", first.Body.ToString()),
            second => Assert.Equal("u.Region == \"South\"", second.Body.ToString()));
    }

    [Fact]
    public void BuildJoinTree_Uses_All_NavigationExpressions()
    {
        using var context = new SqliteTestDb(CreateOptions());

        Expression<Func<User, bool>> whereExpr = u => u.State!.Country!.Code == "US";
        Expression<Func<User, string>> valueExpr = u => u.State!.StateName;

        var joinRoot = JoinTreeBuilder.BuildJoinTree<User>(context, new Expression[] { whereExpr.Body, valueExpr.Body });

        var stateNode = Assert.Single(joinRoot.Children);
        Assert.Equal("t_State", stateNode.TableAlias);

        var countryNode = Assert.Single(stateNode.Children);
        Assert.Equal("t_State_Country", countryNode.TableAlias);
    }

    [Fact]
    public void BuildWhereClause_Translates_NavigationPredicates_With_Parameters()
    {
        using var context = new SqliteTestDb(CreateOptions());
        var parameterBag = new SqlParameterAccumulator();

        var query = context.Users
            .Where(u => u.Region == "South")
            .Where(u => u.State!.StateCode == "FL");

        var whereExpressions = WhereClauseBuilder.ExtractWhereExpressions(query.Expression)
            .Select(lambda => lambda.Body)
            .ToList();

        var joinRoot = JoinTreeBuilder.BuildJoinTree<User>(context, whereExpressions);

        var clause = WhereClauseBuilder.BuildWhereClause(query, joinRoot, context, parameterBag);

        Assert.Equal("t.Region = {0} AND t_State.StateCode = {1}", clause);
        Assert.Equal(new object?[] { "South", "FL" }, parameterBag.ToArray());
    }

    private static DbContextOptions<SqliteTestDb> CreateOptions()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<SqliteTestDb>()
            .UseSqlite(connection)
            .Options;

        using var context = new SqliteTestDb(options);
        context.Database.EnsureCreated();

        return options;
    }

    private class SqliteTestDb : DbContext
    {
        public SqliteTestDb(DbContextOptions<SqliteTestDb> options) : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<State> States => Set<State>();
        public DbSet<Country> Countries => Set<Country>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().ToTable("Users");
            modelBuilder.Entity<State>().ToTable("States");
            modelBuilder.Entity<Country>().ToTable("Countries");

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<State>().HasKey(s => s.Id);
            modelBuilder.Entity<Country>().HasKey(c => c.Id);

            modelBuilder.Entity<User>()
                .HasOne(u => u.State)
                .WithMany()
                .HasForeignKey(u => u.StateId);

            modelBuilder.Entity<State>()
                .HasOne(s => s.Country)
                .WithMany(c => c.States)
                .HasForeignKey(s => s.CountryId);
        }
    }
}
