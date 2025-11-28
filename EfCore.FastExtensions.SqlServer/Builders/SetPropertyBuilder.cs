using System.Linq.Expressions;
using System.Text;

namespace EfCore.FastExtensions.SqlServer.Builders;

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

public class SetOperation
{
    public LambdaExpression Property { get; set; } = default!;
    public LambdaExpression Value { get; set; } = default!;
}