# 🚀 EF.Core Extensions

Write efficient `UPDATE` and `DELETE` queries using LINQ `Include()` chains that get translated into optimized SQL JOINs at execution time.

Extends Entity Framework Core with static IQueryable-level join-based execution for relational SQL databases.

---

## 🧠 Features

- ✅ `ExecuteUpdateJoinAsync(...)` — run update queries using translated SQL JOIN
- ✅ `ExecuteDeleteJoinAsync(...)` — delete entities using join-based filters
- ✅ No raw SQL required
- ✅ Fully composable `IQueryable` experience
- ✅ Async execution support
- ⚡ Generates optimized JOIN queries for faster execution paths

---

## 📦 Installation

```bash
dotnet add package EfCore.JoinExtensions

## Usage Examples
```
var deleted = await db.Users
    .Include(u => u.State)
    .ThenInclude(s => s.Country)
    .Where(u => u.State!.StateCode == "AB")
    .ExecuteDeleteJoinAsync(CancellationToken.None);
```

## 🤝 Contributing
- Fork the repository
- Create a branch (feature/your-feature)
- Commit your code
- Push and open a Pull Request
