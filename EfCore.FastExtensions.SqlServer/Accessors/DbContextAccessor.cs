using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace EfCore.FastExtensions.SqlServer.Accessors;
internal static class DbContextAccessor
{
    public static DbContext GetDbContextFromQuery(IQueryable query)
    {
        if (query is IInfrastructure<IServiceProvider> infrastructure)
        {
            var serviceProvider = infrastructure.Instance;

            var currentContext = serviceProvider.GetService<ICurrentDbContext>();
            if (currentContext != null)
                return currentContext.Context;
        }
        // Otherwise use reflection (always works)
        var provider = query.Provider;

        // Look for "QueryCompiler"
        var compilerField = provider.GetType()
            .GetField("_queryCompiler", BindingFlags.NonPublic | BindingFlags.Instance);

        if (compilerField == null)
            throw new InvalidOperationException("Cannot access _queryCompiler. EF Core internals changed.");

        var queryCompiler = compilerField.GetValue(provider);

        if (queryCompiler == null)
            throw new InvalidOperationException("Cannot find _queryCompiler.");

        // Get QueryContextFactory
        var queryContextFactoryField = queryCompiler.GetType()
            .GetField("_queryContextFactory", BindingFlags.NonPublic | BindingFlags.Instance);

        if (queryContextFactoryField == null)
            throw new InvalidOperationException("Cannot access _queryContextFactory.");

        var queryContextFactory = queryContextFactoryField.GetValue(queryCompiler);

        if (queryContextFactory == null)
            throw new InvalidOperationException("Cannot find _queryContextFactory.");

        // Get Dependencies -> CurrentContext
        var dependenciesProperty = queryContextFactory.GetType()
            .GetProperty("Dependencies", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        if (dependenciesProperty == null)
            throw new InvalidOperationException("Cannot get Dependencies from QueryContextFactory.");

        var dependencies = dependenciesProperty.GetValue(queryContextFactory);

        if (dependencies == null)
            throw new InvalidOperationException("Cannot find Dependencies from QueryContextFactory.");

        var currentContextProperty = dependencies.GetType()
            .GetProperty("CurrentContext", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        if (currentContextProperty == null)
            throw new InvalidOperationException("Cannot get CurrentContext");

        var currentContext2 = (ICurrentDbContext)currentContextProperty.GetValue(dependencies)!;

        return currentContext2.Context;
    }
}
