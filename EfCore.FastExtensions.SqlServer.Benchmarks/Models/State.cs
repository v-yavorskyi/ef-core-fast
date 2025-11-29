namespace EfCore.FastExtensions.SqlServer.Benchmarks.Models;

public class State
{
    public int Id { get; set; }
    public string StateCode { get; set; } = string.Empty;
    public string StateName { get; set; } = string.Empty;
    public Country? Country { get; set; }
    public int CountryId { get; set; }
    public ICollection<User> Users { get; set; } = new List<User>();
}
