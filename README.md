# 🚀 EF.Core Extensions

Write efficient `UPDATE` and `DELETE` queries using LINQ `Include()` chains that get translated into optimized SQL JOINs at execution time.
Use this extension only when you have to update/delete an entity based on other entities.

Extends Entity Framework Core with static IQueryable-level join-based execution for relational SQL databases.

---

## 🧠 Features

- ✅ `ExecuteUpdateJoinAsync(...)` — run update queries using translated SQL JOIN
- ✅ `ExecuteDeleteJoinAsync(...)` — delete entities using join-based filters
- ✅ `ExecuteBulkInsertAsync(...)` — efficiently insert large batches without manual SQL
- ✅ No raw SQL required
- ✅ Fully composable `IQueryable` experience
- ✅ Async execution support
- ⚡ Generates optimized JOIN queries for faster execution paths

---

## 📦 Installation

```bash
dotnet add package EfCore.JoinExtensions
```

## Usage Examples
```
var deleted = await db.Users
    .Include(u => u.State)
    .ThenInclude(s => s.Country)
    .Where(u => u.State!.StateCode == "AB")
    .ExecuteDeleteJoinAsync(CancellationToken.None);

var affected = await db.Users
    .Include(x => x.State)
    .ThenInclude(x => x.Country)
    .Where(u => u.Id == 1 && u.State.Country.Id > 0)
    .ExecuteUpdateJoinAsync(
        x => x.SetProperty(p => p.Region, p => (p.State!.StateName.ToUpper()))
    );

var inserted = await db.Users
    .AsQueryable()
    .ExecuteBulkInsertAsync(new List<User>
    {
        new() { Name = "Alice", Region = "West" },
        new() { Name = "Bob", Region = "East" }
    }, batchSize: 5000);
```

## Benchmark: Native EF Core vs Fast Join Extensions

Performance comparison between native CRUD operations and optimized `JOIN` extensions using navigation properties.

| Method | Mean | Error | StdDev | Median | Allocated |
|---|---:|---:|---:|---:|---:|
| **Native EF Core Update** | 9.794 ms | 0.6103 ms | 1.800 ms | 9.753 ms | 475.92 KB |
| **ExecuteUpdateJoinAsync (JOIN + SET + WHERE)** | 7.001 ms ✅ | 0.3860 ms | 1.120 ms | 6.552 ms | 149.49 KB ✅ |
| **Native EF Core RemoveRange** | 20.866 ms ⚠ | 2.1838 ms | 6.370 ms | 18.531 ms | 3.31 MB ⚠ |
| **ExecuteDeleteJoinAsync** | 11.131 ms ✅ | 0.4575 ms | 1.349 ms | 10.975 ms | 138.04 KB ✅ |

---

## 🤝 Contributing
- Fork the repository
- Create a branch (feature/your-feature)
- Commit your code
- Push and open a Pull Request

- 
