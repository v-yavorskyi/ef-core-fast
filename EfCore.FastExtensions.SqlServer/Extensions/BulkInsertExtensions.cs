using EfCore.FastExtensions.SqlServer.Accessors;
using Microsoft.EntityFrameworkCore;

namespace EfCore.FastExtensions.SqlServer.Extensions;
public static class BulkInsertExtensions
{

    public static async Task<int> ExecuteBulkInsertAsync<TEntity>(
        this IQueryable<TEntity> query,
        IEnumerable<TEntity> entities,
        int batchSize = 5000,
        CancellationToken cancellationToken = default)
    where TEntity : class
    {
        var db = DbContextAccessor.GetDbContextFromQuery(query);
        var sqlResult = GetInsertSqlScript(query, entities, batchSize, db);

        // OPTIONAL: include parameters later if needed
        // Here it's pure SQL execution
        return await db.Database.ExecuteSqlRawAsync(sqlResult.Sql, sqlResult.Parameters, cancellationToken);
    }


    private static (string Sql, object[] Parameters) GetInsertSqlScript<TEntity>(
        this IQueryable<TEntity> query,
        IEnumerable<TEntity> entities,
        int batchSize,
        DbContext db)
    where TEntity : class
    {
        throw new NotImplementedException();
    }

}
