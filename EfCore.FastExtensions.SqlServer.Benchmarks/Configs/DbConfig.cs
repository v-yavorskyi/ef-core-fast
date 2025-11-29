namespace EfCore.FastExtensions.SqlServer.Benchmarks.Configs;

internal static class DbConfig
{
    public static string? GetSqlServerConnectionString()
    {
        var dbName = "EfCoreFastBenchmarks_" + Guid.NewGuid().ToString("N");
        return $"Server=(localdb)\\mssqllocaldb;Database={dbName};Trusted_Connection=True;MultipleActiveResultSets=true";
    }
}
