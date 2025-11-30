using EfCore.FastExtensions.SqlServer.Models;
using System.Linq.Expressions;

namespace EfCore.FastExtensions.SqlServer.Extensions;
public static class IncludeTempTableExtensions
{
    /// <summary>
    /// Allows to include custom data collection as TEMPORARY table
    /// </summary>
    /// <typeparam name="TEntity">Entity framework entity</typeparam>
    /// <typeparam name="TDto">Your custom type (For example Dto class)</typeparam>
    /// <typeparam name="TKey"></typeparam>
    /// <param name="query">EF query</param>
    /// <param name="entityKeySelector"></param>
    /// <param name="tempDtos"></param>
    /// <param name="dtoKeySelector"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public static IQueryableExtended<TEntity> IncludeTempTable<TEntity, TDto, TKey>(
        this IQueryable<TEntity> query,
        Expression<Func<TEntity, TKey>> entityKeySelector,
        IEnumerable<TDto> tempDtos,
        Func<TDto, TKey> dtoKeySelector
    ) where TEntity : class
    {
        throw new NotImplementedException();
    }

    public static Task<List<TResult>> ExecuteSelectAsync<TEntity, TResult, TDto>(
        this IQueryableExtended<TEntity> query,

        // user LINQ projection
        Expression<Func<TEntity, TResult>> selector,

        // temp table alias access object
        Func<IQueryable<TDto>, IQueryable<TDto>> tempTableSelector
    ) where TEntity : class
    {
        throw new NotImplementedException(); 
    }
}
