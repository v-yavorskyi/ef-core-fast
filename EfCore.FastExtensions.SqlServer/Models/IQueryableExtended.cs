using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace EfCore.FastExtensions.SqlServer.Models;

public interface IQueryableExtended<out T> : IQueryable<T>
{

}
