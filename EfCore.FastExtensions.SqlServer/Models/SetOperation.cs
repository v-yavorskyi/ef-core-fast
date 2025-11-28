using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace EfCore.FastExtensions.SqlServer.Models;

public class SetOperation
{
    public LambdaExpression Property { get; set; } = default!;
    public LambdaExpression Value { get; set; } = default!;
}
