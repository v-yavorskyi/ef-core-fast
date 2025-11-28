using EfCore.FastExtensions.SqlServer.Accessors;
using EfCore.FastExtensions.SqlServer.Builders;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
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
        var sql = GetDeleteSql(query, db, out var parameters);

        return await db.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
    }


    private static string GetDeleteSql<TEntity>(
            IQueryable<TEntity> query,
            DbContext db,
            out object[] parameters)
            where TEntity : class
    {
        // 1. Extract WHERE expression (same as update)
        var whereLambdas = WhereClauseBuilder.ExtractWhereExpressions(query.Expression);
        var whereExpressions = whereLambdas.Any()
            ? whereLambdas.Select(lambda => lambda.Body).ToList()
            : new List<Expression>();

        var parameterBag = new SqlParameterAccumulator();

        // 2. Build join tree (based only on WHERE expressions)
        var joinRoot = JoinTreeBuilder.BuildJoinTree<TEntity>(db, whereExpressions!);

        // 3. Build FROM + JOIN clause
        var fromClause = FromClauseBuilder.BuildFromClause(joinRoot);

        // 4. Build WHERE clause
        var whereClause = WhereClauseBuilder.BuildWhereClause(query, joinRoot, db, parameterBag);

        var rootAlias = joinRoot.TableAlias;

        var whereSql = string.IsNullOrWhiteSpace(whereClause)
            ? string.Empty
            : $"WHERE {whereClause}";

        parameters = parameterBag.ToArray();

        return $@"
DELETE {rootAlias}
FROM {fromClause}
{whereSql};
".Trim();
    }

}