using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace EfCore.FastExtensions.SqlServer.Models;

public interface ITempTableQueryable<TEntity, TDto, TKey> : IQueryable<TEntity>
{
    IQueryable<TEntity> InnerQuery { get; }

    Expression<Func<TEntity, TKey>> EntityKeySelector { get; }

    IEnumerable<TDto> TempDtos { get; }

    Func<TDto, TKey> DtoKeySelector { get; }
}
