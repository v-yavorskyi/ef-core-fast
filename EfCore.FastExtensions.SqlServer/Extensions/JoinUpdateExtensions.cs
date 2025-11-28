using EfCore.FastExtensions.SqlServer.Accessors;
using EfCore.FastExtensions.SqlServer.Builders;
using Microsoft.EntityFrameworkCore;
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
        var sql = GetUpdateJoinSqlScript(query, updateExpression, db);

        // OPTIONAL: include parameters later if needed
        // Here it's pure SQL execution
        return await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }


    private static string GetUpdateJoinSqlScript<TEntity>(
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
        var whereLambda = WhereClauseBuilder.ExtractWhereExpression(query.Expression);
        var whereExpressions = whereLambda != null
            ? new[] { whereLambda.Body }                 // Expression, not lambda wrapper
            : Array.Empty<Expression>();

        // 3. Combine all expressions that might reference navigations
        var allExpressions = valueExpressions
            .Select(v => v.Body)                         // Lambda → Body
            .Concat(whereExpressions)
            .ToList();
        var joinRoot = JoinTreeBuilder.BuildJoinTree<TEntity>(db, allExpressions);

        // 3. Build FROM / JOIN SQL from joinRoot (next step for you)
        var fromClause = FromClauseBuilder.BuildFromClause(joinRoot);

        // 4. Build SET clause using ops + aliases from joinRoot
        var setClause = SetClauseBuilder.BuildSetClauseFromOperations<TEntity>(ops, joinRoot, db);
        var whereClause = WhereClauseBuilder.BuildWhereClause(query, joinRoot, db);


        // 5. Compose final UPDATE statement
        var rootTableAlias = joinRoot.TableAlias;
        var rootTableName = joinRoot.TableName;

        var sql = $@"
UPDATE {rootTableAlias}
SET {setClause}
FROM {fromClause}
WHERE {whereClause};
";

        return sql.Trim();
    }

}
