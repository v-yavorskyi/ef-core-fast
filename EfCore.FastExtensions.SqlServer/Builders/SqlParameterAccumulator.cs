using System;
using System.Collections.Generic;

namespace EfCore.FastExtensions.SqlServer.Builders;

/// <summary>
/// Collects parameter values while generating SQL strings and returns
/// EF Core positional placeholders (e.g. "{0}").
/// </summary>
internal sealed class SqlParameterAccumulator
{
    private readonly List<object?> _parameters = new();

    public IReadOnlyList<object?> Parameters => _parameters;

    public string Add(object? value)
    {
        var index = _parameters.Count;
        _parameters.Add(value ?? DBNull.Value);
        return $"{{{index}}}";
    }

    public object?[] ToArray() => _parameters.ToArray();
}
