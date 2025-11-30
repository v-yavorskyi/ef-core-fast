# 🚀 EF Core Fast Extensions

Entity Framework Core helpers that keep LINQ fully composable while generating optimized SQL for relational databases. The
extensions target .NET 10/EF Core 10 and focus on SQL Server with fallbacks for other EF Core providers.

## 🧠 Features

- ✅ `ExecuteUpdateJoinAsync(...)` — translate navigation-heavy filters into JOIN-based `UPDATE` statements.
- ✅ `ExecuteDeleteJoinAsync(...)` — remove rows using the same translated JOIN filters.
- ✅ `ExecuteBulkInsertAsync(...)` — bulk copy when using SQL Server, or batched `INSERT` statements for other providers.
- ✅ `IncludeTempTable(...)` — join in-memory collections through temporary tables with `INNER` or `LEFT` semantics.
- ✅ Async APIs with pure `IQueryable` composition and no raw SQL strings required from consumers.

## 📦 Installation

```bash
dotnet add package EfCore.FastExtensions.SqlServer
```

## 🔧 Usage Examples

### Join-based delete
```csharp
var deleted = await db.Users
    .Include(u => u.State)
    .ThenInclude(s => s.Country)
    .Where(u => u.State!.StateCode == "AB")
    .ExecuteDeleteJoinAsync(CancellationToken.None);
```

### Join-based update
```csharp
var affected = await db.Users
    .Include(x => x.State)
    .ThenInclude(x => x.Country)
    .Where(u => u.Id == 1 && u.State!.Country.Id > 0)
    .ExecuteUpdateJoinAsync(
        x => x.SetProperty(p => p.Region, p => p.State!.StateName.ToUpper())
    );

var inserted = await db.Users
    .AsQueryable()
    .ExecuteBulkInsertAsync(new List<User>
    {
        new() { Name = "Alice", Region = "West" },
        new() { Name = "Bob", Region = "East" }
    }, batchSize: 5000);
```

### Bulk insert (SQL Server optimized)
```csharp
var newUsers = new List<User>
{
    new() { Name = "Ada", Email = "ada@example.com" },
    new() { Name = "Alan", Email = "alan@example.com" }
};

// Uses SqlBulkCopy on SQL Server, or batched INSERTs for other providers
var inserted = await db.Users.ExecuteBulkInsertAsync(newUsers, batchSize: 2000);
```

### Joining in-memory data via temporary tables
```csharp
using EfCore.FastExtensions.SqlServer.Enums;

var temporaryScores = new[]
{
    new { UserId = 1, Score = 82 },
    new { UserId = 2, Score = 91 }
};

var results = await db.Users
    .IncludeTempTable(user => user.Id, temporaryScores, score => score.UserId, SqlJoinType.Left)
    .ExecuteSelectAsync(user => new { user.Id, user.Email },
        (projection, score) => new
        {
            projection.Id,
            projection.Email,
            Score = score?.Score ?? 0
        });
```

## 🧪 Benchmark Snapshot

Performance comparison between EF Core CRUD operations and optimized JOIN extensions (from the SQL Server benchmarks in this
repository):

| Method | Mean | Error | StdDev | Median | Allocated |
|---|---:|---:|---:|---:|---:|
| **Native EF Core Update** | 9.794 ms | 0.6103 ms | 1.800 ms | 9.753 ms | 475.92 KB |
| **ExecuteUpdateJoinAsync (JOIN + SET + WHERE)** | 7.001 ms ✅ | 0.3860 ms | 1.120 ms | 6.552 ms | 149.49 KB ✅ |
| **Native EF Core RemoveRange** | 20.866 ms ⚠ | 2.1838 ms | 6.370 ms | 18.531 ms | 3.31 MB ⚠ |
| **ExecuteDeleteJoinAsync** | 11.131 ms ✅ | 0.4575 ms | 1.349 ms | 10.975 ms | 138.04 KB ✅ |

## 🤝 Contributing

- Fork the repository.
- Create a branch (e.g., `feature/your-feature`).
- Commit your code.
- Push and open a Pull Request.