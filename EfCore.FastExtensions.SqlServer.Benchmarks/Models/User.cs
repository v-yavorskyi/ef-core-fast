namespace EfCore.FastExtensions.SqlServer.Benchmarks.Models;

public class User
{
    public int Id { get; set; }
    public int StateId { get; set; }
    public string? Region { get; set; }
    public State? State { get; set; }
}
