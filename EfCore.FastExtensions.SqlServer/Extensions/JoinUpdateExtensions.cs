using EfCore.FastExtensions.SqlServer.Accessors;
using EfCore.FastExtensions.SqlServer.Builders;
using EfCore.FastExtensions.SqlServer.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace EfCore.FastExtensions.SqlServer.Extensions;
public static class JoinUpdateExtensions
{

    public static async Task<int> ExecuteUpdateJoinAsync<TEntity>(
        this IQueryable<TEntity> query,
        Expression<Func<SetPropertyBuilder<TEntity>, SetPropertyBuilder<TEntity>>> updateExpression,
        CancellationToken cancellationToken = default)
    where TEntity : class
    {
        var db = DbContextAccessor.GetDbContextFromQuery(query);
        var sqlResult = GetUpdateJoinSqlScript(query, updateExpression, db);

        // OPTIONAL: include parameters later if needed
        // Here it's pure SQL execution
        return await db.Database.ExecuteSqlRawAsync(sqlResult.Sql, sqlResult.Parameters, cancellationToken);
    }


    private static (string Sql, object[] Parameters) GetUpdateJoinSqlScript<TEntity>(
    this IQueryable<TEntity> query,
    Expression<Func<SetPropertyBuilder<TEntity>, SetPropertyBuilder<TEntity>>> updateExpression,
    DbContext db)
    where TEntity : class
    {
        // 1. Extract SET operations
        var setBuilder = new SetPropertyBuilder<TEntity>();
        updateExpression.Compile()(setBuilder);
        var ops = setBuilder.Operations;

        // 2. Build join tree from VALUE expressions
        var valueExpressions = ops.Select(o => o.Value);
        var whereLambdas = WhereClauseBuilder.ExtractWhereExpressions(query.Expression);
        var whereExpressions = whereLambdas.Any()
            ? whereLambdas.Select(lambda => lambda.Body).ToList()
            : new List<Expression>();

        var parameters = new SqlParameterAccumulator();

        // 3. Combine all expressions that might reference navigations
        var allExpressions = valueExpressions
            .Select(v => v.Body)                         // Lambda → Body
            .Concat(whereExpressions)
            .ToList();
        var joinRoot = JoinTreeBuilder.BuildJoinTree<TEntity>(db, allExpressions);

        // 3. Build FROM / JOIN SQL from joinRoot (next step for you)
        var fromClause = FromClauseBuilder.BuildFromClause(joinRoot);

        // 4. Build SET clause using ops + aliases from joinRoot
        var setClause = SetClauseBuilder.BuildSetClauseFromOperations<TEntity>(ops, joinRoot, db, parameters);
        var whereClause = WhereClauseBuilder.BuildWhereClause(query, joinRoot, db, parameters);


        // 5. Compose final UPDATE statement
        var rootTableAlias = joinRoot.TableAlias;
        var rootTableName = joinRoot.TableName;

        var whereSql = string.IsNullOrWhiteSpace(whereClause)
            ? string.Empty
            : $"WHERE {whereClause}";

        var sql = $@"
UPDATE {rootTableAlias}
SET {setClause}
FROM {fromClause}
{whereSql};
";

        return (sql.Trim(), parameters.ToArray());
    }

}
