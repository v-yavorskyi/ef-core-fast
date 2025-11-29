namespace EfCore.FastExtensions.SqlServer.Benchmarks.Models;

public class Country
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ICollection<State> States { get; set; } = new List<State>();
}
