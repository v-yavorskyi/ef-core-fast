using System;
using System.Collections.Generic;
using System.Text;

namespace EfCore.FastExtensions.Tests.Configs;

internal static class DbConfig
{
    public static string? GetSqlServerConnectionString()
    {
        var dbName = "EfCoreFastTests_" + Guid.NewGuid().ToString("N");
        return  $"Server=(localdb)\\mssqllocaldb;Database={dbName};Trusted_Connection=True;MultipleActiveResultSets=true";
    }
}
