using EfCore.FastExtensions.SqlServer.Accessors;
using EfCore.FastExtensions.SqlServer.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Collections;
using System.Linq;
using System.Linq.Expressions;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace EfCore.FastExtensions.SqlServer.Extensions;

public static class IncludeTempTableExtensions
{
    /// <summary>
    /// Allows to include custom data collection as TEMPORARY table
    /// </summary>
    /// <typeparam name="TEntity">Entity framework entity</typeparam>
    /// <typeparam name="TDto">Your custom type (For example Dto class)</typeparam>
    /// <typeparam name="TKey"></typeparam>
    /// <param name="query">EF query</param>
    /// <param name="entityKeySelector"></param>
    /// <param name="tempDtos"></param>
    /// <param name="dtoKeySelector"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public static ITempTableQueryable<TEntity, TDto, TKey> IncludeTempTable<TEntity, TDto, TKey>(
        this IQueryable<TEntity> query,
        Expression<Func<TEntity, TKey>> entityKeySelector,
        IEnumerable<TDto> tempDtos,
        Func<TDto, TKey> dtoKeySelector
    ) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(entityKeySelector);
        ArgumentNullException.ThrowIfNull(tempDtos);
        ArgumentNullException.ThrowIfNull(dtoKeySelector);

        if (query is IQueryable<TEntity> extended)
        {
            return new TempTableQuery<TEntity, TDto, TKey>(extended, entityKeySelector, tempDtos, dtoKeySelector);
        }

        return new TempTableQuery<TEntity, TDto, TKey>(new WrappingExtendedQueryable<TEntity>(query), entityKeySelector, tempDtos, dtoKeySelector);
    }

    public static Task<List<TResult>> ExecuteSelectAsync<TEntity, TDto, TKey, TResult>(
        this ITempTableQueryable<TEntity, TDto, TKey> query,
        Expression<Func<TEntity, TResult>> selector,
        Func<IQueryable<TDto>, IQueryable<TDto>> tempTableSelector,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        return ExecuteSelectAsync(query, selector, tempTableSelector, static (projection, _) => projection, cancellationToken);
    }

    public static Task<List<TResult>> ExecuteSelectAsync<TEntity, TDto, TKey, TResult>(
        this ITempTableQueryable<TEntity, TDto, TKey> query,
        Expression<Func<TEntity, TResult>> selector,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        return ExecuteSelectAsync(query, selector, static temp => temp, static (projection, _) => projection, cancellationToken);
    }

    public static Task<List<TResult>> ExecuteSelectAsync<TEntity, TDto, TKey, TResult>(
        this ITempTableQueryable<TEntity, TDto, TKey> query,
        Func<IQueryable<TDto>, IQueryable<TDto>> tempTableSelector,
        Func<TEntity, TDto, TResult> resultSelector,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        return ExecuteSelectAsync(query, static entity => entity, tempTableSelector, resultSelector, cancellationToken);
    }

    public static Task<List<TResult>> ExecuteSelectAsync<TEntity, TDto, TKey, TResult>(
        this ITempTableQueryable<TEntity, TDto, TKey> query,
        Func<TEntity, TDto, TResult> resultSelector,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        return ExecuteSelectAsync(query, static entity => entity, static temp => temp, resultSelector, cancellationToken);
    }

    public static Task<List<TResult>> ExecuteSelectAsync<TEntity, TDto, TKey, TProjection, TResult>(
        this ITempTableQueryable<TEntity, TDto, TKey> query,

        // user LINQ projection
        Expression<Func<TEntity, TProjection>> selector,

        // temp table alias access object
        Func<IQueryable<TDto>, IQueryable<TDto>> tempTableSelector,

        // final result projection that has access to both entity projection and temp dto
        Func<TProjection, TDto, TResult> resultSelector,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(tempTableSelector);
        ArgumentNullException.ThrowIfNull(resultSelector);

        var db = DbContextAccessor.GetDbContextFromQuery(query.InnerQuery);

        var filteredTempDtos = tempTableSelector(query.TempDtos.AsQueryable()).ToList();

        if (filteredTempDtos.Count == 0)
        {
            return Task.FromResult(new List<TResult>());
        }

        return ExecuteSelectInternalAsync(selector, resultSelector, query, db, filteredTempDtos, cancellationToken);
    }

    private static async Task<List<TResult>> ExecuteSelectInternalAsync<TEntity, TProjection, TResult, TDto, TKey>(
        Expression<Func<TEntity, TProjection>> selector,
        Func<TProjection, TDto, TResult> resultSelector,
        ITempTableQueryable<TEntity, TDto, TKey> tempQuery,
        DbContext db,
        IReadOnlyCollection<TDto> filteredTempDtos,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var keyColumnName = "Key";
        var keyClrType = Nullable.GetUnderlyingType(typeof(TKey)) ?? typeof(TKey);
        var keySqlType = GetSqlTypeName(typeof(TKey));
        var tempTableName = $"#Temp_{Guid.NewGuid():N}";

        var connection = (SqlConnection)db.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;
        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var tempTableCreated = false;

        try
        {
            var createTableSql = $"CREATE TABLE {tempTableName} ([{keyColumnName}] {keySqlType} NOT NULL);";

            await using (var createCommand = new SqlCommand(createTableSql, connection))
            {
                await createCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            tempTableCreated = true;

            var bulkTable = new DataTable();
            bulkTable.Columns.Add(keyColumnName, keyClrType);
            foreach (var dto in filteredTempDtos)
            {
                bulkTable.Rows.Add(tempQuery.DtoKeySelector(dto));
            }

            using (var bulkCopy = new SqlBulkCopy(connection)
            {
                DestinationTableName = tempTableName
            })
            {
                bulkCopy.ColumnMappings.Add(keyColumnName, keyColumnName);
                await bulkCopy.WriteToServerAsync(bulkTable, cancellationToken);
            }

            var entityType = db.Model.FindEntityType(typeof(TEntity))
                             ?? throw new InvalidOperationException($"Entity type {typeof(TEntity).Name} is not part of the DbContext model.");

            var storeObject = StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema());
            var entityKeyProperty = GetKeyProperty<TEntity>(tempQuery.EntityKeySelector.Body, entityType);
            var entityKeyColumn = entityKeyProperty.GetColumnName(storeObject)
                                  ?? throw new InvalidOperationException("Unable to resolve entity key column name.");

            var fullTableName = string.IsNullOrWhiteSpace(entityType.GetSchema())
                ? $"[{entityType.GetTableName()}]"
                : $"[{entityType.GetSchema()}].[{entityType.GetTableName()}]";

            var joinSql = $"SELECT DISTINCT e.[{entityKeyColumn}] FROM {fullTableName} AS e INNER JOIN {tempTableName} AS t ON e.[{entityKeyColumn}] = t.[{keyColumnName}]";
            var matchedKeys = new List<TKey>();

            await using (var joinCommand = new SqlCommand(joinSql, connection))
            await using (var reader = await joinCommand.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    matchedKeys.Add((TKey)reader.GetValue(0));
                }
            }

            if (matchedKeys.Count == 0)
            {
                return new List<TResult>();
            }

            var predicate = BuildContainsPredicate(tempQuery.EntityKeySelector, matchedKeys);
            var selectedEntities = await tempQuery.InnerQuery.Where(predicate).ToListAsync(cancellationToken);

            var selectorFunc = selector.Compile();
            var dtoLookup = filteredTempDtos.ToLookup(tempQuery.DtoKeySelector);
            var entityKeyFunc = tempQuery.EntityKeySelector.Compile();

            var averageMatchesPerKey = filteredTempDtos.Count / Math.Max(1, matchedKeys.Count);
            var results = new List<TResult>(selectedEntities.Count * Math.Max(1, averageMatchesPerKey));

            foreach (var entity in selectedEntities)
            {
                var key = entityKeyFunc(entity);
                var projection = selectorFunc(entity);
                foreach (var dto in dtoLookup[key])
                {
                    results.Add(resultSelector(projection, dto));
                }
            }

            return results;
        }
        finally
        {
            if (tempTableCreated)
            {
                await DropTempTableAsync(tempTableName, connection, cancellationToken);
            }

            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task DropTempTableAsync(string tempTableName, SqlConnection connection, CancellationToken cancellationToken)
    {
        var dropSql = $"DROP TABLE {tempTableName};";
        await using var dropCommand = new SqlCommand(dropSql, connection);
        await dropCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IProperty GetKeyProperty<TEntity>(Expression expression, IEntityType entityType)
    {
        if (expression is MemberExpression memberExpr)
        {
            var property = entityType.FindProperty(memberExpr.Member.Name);
            if (property == null)
            {
                throw new InvalidOperationException($"Property {memberExpr.Member.Name} is not mapped on entity {entityType.DisplayName()}.");
            }

            return property;
        }

        throw new NotSupportedException("Only simple member access is supported for key selector.");
    }

    private static Expression<Func<TEntity, bool>> BuildContainsPredicate<TEntity, TKey>(
        Expression<Func<TEntity, TKey>> keySelector,
        IReadOnlyCollection<TKey> keys)
    {
        var keyList = keys.ToList();
        var containsMethod = typeof(List<TKey>).GetMethod(nameof(List<TKey>.Contains), new[] { typeof(TKey) })!;

        var parameter = keySelector.Parameters[0];
        var body = Expression.Call(Expression.Constant(keyList), containsMethod, keySelector.Body);
        return Expression.Lambda<Func<TEntity, bool>>(body, parameter);
    }

    private static string GetSqlTypeName(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type == typeof(int)) return "INT";
        if (type == typeof(long)) return "BIGINT";
        if (type == typeof(short)) return "SMALLINT";
        if (type == typeof(byte)) return "TINYINT";
        if (type == typeof(Guid)) return "UNIQUEIDENTIFIER";
        if (type == typeof(DateTime)) return "DATETIME2";
        if (type == typeof(bool)) return "BIT";
        if (type == typeof(decimal)) return "DECIMAL(18,4)";
        if (type == typeof(double)) return "FLOAT";
        if (type == typeof(float)) return "REAL";
        if (type == typeof(string)) return "NVARCHAR(MAX)";

        throw new NotSupportedException($"Type {type.Name} is not supported for temporary table key column.");
    }
}

internal sealed class TempTableQuery<TEntity, TDto, TKey> : ITempTableQueryable<TEntity, TDto, TKey>
{
    public TempTableQuery(
        IQueryable<TEntity> innerQuery,
        Expression<Func<TEntity, TKey>> entityKeySelector,
        IEnumerable<TDto> tempDtos,
        Func<TDto, TKey> dtoKeySelector)
    {
        InnerQuery = innerQuery ?? throw new ArgumentNullException(nameof(innerQuery));
        EntityKeySelector = entityKeySelector ?? throw new ArgumentNullException(nameof(entityKeySelector));
        TempDtos = tempDtos ?? throw new ArgumentNullException(nameof(tempDtos));
        DtoKeySelector = dtoKeySelector ?? throw new ArgumentNullException(nameof(dtoKeySelector));
    }

    public IQueryable<TEntity> InnerQuery { get; }
    public Expression<Func<TEntity, TKey>> EntityKeySelector { get; }
    public IEnumerable<TDto> TempDtos { get; }
    public Func<TDto, TKey> DtoKeySelector { get; }

    public Type ElementType => InnerQuery.ElementType;
    public Expression Expression => InnerQuery.Expression;
    public IQueryProvider Provider => InnerQuery.Provider;

    public IEnumerator<TEntity> GetEnumerator() => InnerQuery.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class WrappingExtendedQueryable<TEntity> : IQueryable<TEntity>
{
    private readonly IQueryable<TEntity> _inner;

    public WrappingExtendedQueryable(IQueryable<TEntity> inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public Type ElementType => _inner.ElementType;
    public Expression Expression => _inner.Expression;
    public IQueryProvider Provider => _inner.Provider;

    public IEnumerator<TEntity> GetEnumerator() => _inner.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}