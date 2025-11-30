using EfCore.FastExtensions.SqlServer.Accessors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Text;

namespace EfCore.FastExtensions.SqlServer.Extensions;

public static class BulkInsertExtensions
{
    // should be re-written using  SqlBulkCopy ???
    public static async Task<int> ExecuteBulkInsertAsync<TEntity>(
        this IQueryable<TEntity> query,
        List<TEntity> entities,
        int batchSize = 500,
        CancellationToken cancellationToken = default)
    where TEntity : class
    {
        if (entities.Count == 0)
            return 0;

        var db = DbContextAccessor.GetDbContextFromQuery(query);
        var sqlBatches = GetInsertSqlScripts(query, entities, batchSize, db);

        var affected = 0;
        foreach (var sqlResult in sqlBatches)
        {
            affected += await db.Database.ExecuteSqlRawAsync(sqlResult.Sql, sqlResult.Parameters, cancellationToken);
        }

        // OPTIONAL: include parameters later if needed
        // Here it's pure SQL execution
        return affected;
    }
        
    private static IReadOnlyList<(string Sql, object[] Parameters)> GetInsertSqlScripts<TEntity>(
        this IQueryable<TEntity> query,
        IEnumerable<TEntity> entities,
        int batchSize,
        DbContext db)
    where TEntity : class
    {
        var entityType = db.Model.FindEntityType(typeof(TEntity))
                         ?? throw new InvalidOperationException(
                             $"Entity type {typeof(TEntity).Name} is not part of the DbContext model.");

        var tableName = entityType.GetTableName()
                        ?? throw new InvalidOperationException(
                            $"Entity type {entityType.DisplayName()} is not mapped to a table.");

        var schema = entityType.GetSchema();
        var fullTableName = string.IsNullOrWhiteSpace(schema)
            ? tableName
            : $"{schema}.{tableName}";

        var properties = entityType.GetProperties()
            .Where(p => p.ValueGenerated != ValueGenerated.OnAdd &&
                        p.ValueGenerated != ValueGenerated.OnAddOrUpdate)
            .ToList();

        if (properties.Count == 0)
            throw new InvalidOperationException("No insertable properties were found for the entity type.");

        var columnList = string.Join(", ", properties.Select(p => p.GetColumnName()));

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
                var value = property.PropertyInfo?.GetValue(entity);
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