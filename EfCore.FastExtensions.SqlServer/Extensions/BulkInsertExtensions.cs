using EfCore.FastExtensions.SqlServer.Accessors;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Data;
using System.Text;

namespace EfCore.FastExtensions.SqlServer.Extensions;

public static class BulkInsertExtensions
{

    public static async Task<int> ExecuteBulkInsertAsync<TEntity>(
        this IQueryable<TEntity> query,
        List<TEntity> entities,
        int batchSize = 5000,
        CancellationToken cancellationToken = default)
    where TEntity : class
    {
        if (entities.Count == 0)
            return 0;

        var db = DbContextAccessor.GetDbContextFromQuery(query);
        var entityType = db.Model.FindEntityType(typeof(TEntity))
                         ?? throw new InvalidOperationException(
                             $"Entity type {typeof(TEntity).Name} is not part of the DbContext model.");

        var tableName = entityType.GetTableName()
                        ?? throw new InvalidOperationException(
                            $"Entity type {entityType.DisplayName()} is not mapped to a table.");

        var schema = entityType.GetSchema();

        var insertableProperties = entityType.GetProperties()
            .Where(p => p.ValueGenerated != ValueGenerated.OnAdd &&
                        p.ValueGenerated != ValueGenerated.OnAddOrUpdate)
            .ToList();

        if (insertableProperties.Count == 0)
            throw new InvalidOperationException("No insertable properties were found for the entity type.");

        if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer"
            && db.Database.GetDbConnection() is SqlConnection sqlConnection)
        {
            return await ExecuteSqlBulkCopyAsync(sqlConnection, schema, tableName, insertableProperties, entities, cancellationToken);
        }

        var sqlBatches = GetInsertSqlScripts(query, entities, batchSize, db, entityType, schema, tableName, insertableProperties);

        var affected = 0;
        foreach (var sqlResult in sqlBatches)
        {
            affected += await db.Database.ExecuteSqlRawAsync(sqlResult.Sql, sqlResult.Parameters, cancellationToken);
        }

        return affected;
    }

    private static IReadOnlyList<(string Sql, object[] Parameters)> GetInsertSqlScripts<TEntity>(
        this IQueryable<TEntity> query,
        IEnumerable<TEntity> entities,
        int batchSize,
        DbContext db,
        IEntityType entityType,
        string? schema,
        string tableName,
        IReadOnlyList<IProperty> properties)
    where TEntity : class
    {
        var fullTableName = string.IsNullOrWhiteSpace(schema)
            ? tableName
            : $"{schema}.{tableName}";

        var storeObject = StoreObjectIdentifier.Table(tableName, schema);
        var columnList = string.Join(", ", properties.Select(p => p.GetColumnName(storeObject)
            ?? throw new InvalidOperationException($"Column mapping not found for property {p.Name}.")));

        var batches = new List<(string Sql, object[] Parameters)>();
        var commandBatch = new List<string>(batchSize);
        var parameters = new Builders.SqlParameterAccumulator();

        var providerParameterLimit = GetProviderParameterLimit(db.Database.ProviderName);
        var effectiveBatchSize = Math.Max(1, Math.Min(batchSize, providerParameterLimit / properties.Count));

        foreach (var entity in entities)
        {
            var placeholders = new List<string>(properties.Count);

            foreach (var property in properties)
            {
                var value = GetPropertyValue(property, entity);
                placeholders.Add(parameters.Add(value));
            }

            commandBatch.Add($"({string.Join(", ", placeholders)})");

            if (commandBatch.Count == effectiveBatchSize)
            {
                batches.Add(BuildInsertCommand(fullTableName, columnList, commandBatch, parameters));
                commandBatch = new List<string>(effectiveBatchSize);
                parameters = new Builders.SqlParameterAccumulator();
            }
        }

        if (commandBatch.Count > 0)
        {
            batches.Add(BuildInsertCommand(fullTableName, columnList, commandBatch, parameters));
        }

        return batches;
    }

    private static object? GetPropertyValue<TEntity>(IProperty property, TEntity entity)
        where TEntity : class
    {
        if (property.PropertyInfo != null)
        {
            return property.PropertyInfo.GetValue(entity);
        }

        var getter = property.GetGetter();
        return getter?.GetClrValue(entity);
    }

    private static async Task<int> ExecuteSqlBulkCopyAsync<TEntity>(
        SqlConnection connection,
        string? schema,
        string tableName,
        IReadOnlyList<IProperty> properties,
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var storeObject = StoreObjectIdentifier.Table(tableName, schema);
        var fullTableName = string.IsNullOrWhiteSpace(schema)
            ? tableName
            : $"{schema}.{tableName}";

        var wasClosed = connection.State == ConnectionState.Closed;

        using var dataTable = new DataTable();

        foreach (var property in properties)
        {
            var targetType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            dataTable.Columns.Add(property.Name, targetType);
        }

        foreach (var entity in entities)
        {
            var values = properties
                .Select(property => GetPropertyValue(property, entity) ?? DBNull.Value)
                .ToArray();
            dataTable.Rows.Add(values);
        }

        if (dataTable.Rows.Count == 0)
        {
            return 0;
        }

        if (wasClosed)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            using var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints, null)
            {
                DestinationTableName = fullTableName,
                EnableStreaming = true
            };

            foreach (var property in properties)
            {
                var columnName = property.GetColumnName(storeObject)
                                 ?? throw new InvalidOperationException(
                                     $"Column mapping not found for property {property.Name}.");
                bulkCopy.ColumnMappings.Add(property.Name, columnName);
            }

            await bulkCopy.WriteToServerAsync(dataTable, cancellationToken).ConfigureAwait(false);
            return dataTable.Rows.Count;
        }
        finally
        {
            if (wasClosed)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    private static (string Sql, object[] Parameters) BuildInsertCommand(
        string tableName,
        string columns,
        IReadOnlyCollection<string> values,
        Builders.SqlParameterAccumulator parameters)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"INSERT INTO {tableName} ({columns})");
        sb.AppendLine("VALUES");
        sb.AppendLine(string.Join(",\n", values));
        sb.AppendLine(";");

        return (sb.ToString().Trim(), parameters.ToArray());
    }

    private static int GetProviderParameterLimit(string? providerName)
    {
        return providerName switch
        {
            "Microsoft.EntityFrameworkCore.SqlServer" => 2100,
            "Microsoft.EntityFrameworkCore.Sqlite" => 999,
            _ => 2000
        };
    }
}