using EfCore.FastExtensions.SqlServer.Accessors;
using EfCore.FastExtensions.SqlServer.Builders;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace EfCore.FastExtensions.SqlServer.Extensions;

public static class JoinDeleteExtensions
{
    public static async Task<int> ExecuteDeleteJoinAsync<TEntity>(
        this IQueryable<TEntity> query,
        CancellationToken cancellationToken = default)
    where TEntity : class
    {
        var db = DbContextAccessor.GetDbContextFromQuery(query);
        var sql = GetDeleteSql(query, db);

        return await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }


    private static string GetDeleteSql<TEntity>(
            IQueryable<TEntity> query,
            DbContext db)
            where TEntity : class
    {
        // 1. Extract WHERE expression (same as update)
        var whereLambda = WhereClauseBuilder.ExtractWhereExpression(query.Expression);
        var whereExpressions = whereLambda != null
            ? new[] { whereLambda.Body }                 // Expression, not lambda wrapper
            : Array.Empty<Expression>();

        // 2. Build join tree (based only on WHERE expressions)
        var joinRoot = JoinTreeBuilder.BuildJoinTree<TEntity>(db, whereExpressions!);

        // 3. Build FROM + JOIN clause
        var fromClause = FromClauseBuilder.BuildFromClause(joinRoot);

        // 4. Build WHERE clause
        var whereClause = WhereClauseBuilder.BuildWhereClause(query, joinRoot, db);

        var rootAlias = joinRoot.TableAlias;

        return $@"
DELETE {rootAlias}
FROM {fromClause}
WHERE {whereClause};
".Trim();
    }

}