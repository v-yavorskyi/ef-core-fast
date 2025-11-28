using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace EfCore.FastExtensions.SqlServer.Models;

public class SetPropertyBuilder<TEntity>
{
    // internal structure to collect SET operations
    internal List<SetOperation> Operations { get; } = new();

    public SetPropertyBuilder<TEntity> SetProperty<TProperty>(
        Expression<Func<TEntity, TProperty>> propertySelector,
        Expression<Func<TEntity, TProperty>> valueSelector)
    {
        Operations.Add(new SetOperation
        {
            Property = propertySelector,
            Value = valueSelector
        });

        return this;
    }
}
