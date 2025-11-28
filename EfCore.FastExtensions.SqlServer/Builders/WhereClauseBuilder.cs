using EfCore.FastExtensions.SqlServer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace EfCore.FastExtensions.SqlServer.Builders;

internal static class WhereClauseBuilder
{
    /// <summary>
    /// Extracts all WHERE lambda expressions from a query (supports chaining, Include, Select, etc.)
    /// </summary>
    public static IReadOnlyList<LambdaExpression> ExtractWhereExpressions(Expression expr)
    {
        var predicates = new List<LambdaExpression>();
        CollectWhereExpressions(expr, predicates);
        predicates.Reverse();
        return predicates;
    }

    private static void CollectWhereExpressions(Expression expr, List<LambdaExpression> predicates)
    {
        if (expr is not MethodCallExpression mce)
        {
            return;
        }

        if (mce.Method.Name == nameof(Queryable.Where) && mce.Arguments.Count == 2)
        {
            var unary = (UnaryExpression)mce.Arguments[1];
            predicates.Add((LambdaExpression)unary.Operand);
        }

        CollectWhereExpressions(mce.Arguments[0], predicates);
    }

    /// <summary>
    /// Builds WHERE clause using the same SQL translation logic as SET clause.
    /// If the query has no .Where(), returns empty string.
    /// </summary>
    public static string BuildWhereClause<TEntity>(
        IQueryable<TEntity> query,
        JoinNode root,
        DbContext db,
        SqlParameterAccumulator parameters)
        where TEntity : class
    {
        var whereExprs = ExtractWhereExpressions(query.Expression);

        if (!whereExprs.Any())
            return ""; // No WHERE clause in original query

        // WHERE body (e.g. p => p.Id == 10)
        var mergedBody = MergePredicates(whereExprs);
        var sqlBody = TranslateExpression(mergedBody, root, db, parameters);

        return sqlBody;
    }

    /// <summary>
    /// Reuses the same expression translator used by SET.
    /// This keeps aliasing and navigation resolution EXACTLY identical.
    /// </summary>
    private static string TranslateExpression(Expression expr, JoinNode root, DbContext db, SqlParameterAccumulator parameters)
    {
        // We simply delegate to your existing TranslateExpression implementation.
        // If your code is in SetClauseBuilder, call that method.
        return ExpressionSqlTranslator.Translate(expr, root, db, parameters);
    }

    private static Expression MergePredicates(IReadOnlyList<LambdaExpression> predicates)
    {
        if (predicates.Count == 1)
        {
            return predicates[0].Body;
        }

        var primaryParameter = predicates[0].Parameters[0];
        Expression combined = predicates[0].Body;

        foreach (var predicate in predicates.Skip(1))
        {
            var rewritten = ParameterReplacer.Replace(predicate.Body, predicate.Parameters[0], primaryParameter);
            combined = Expression.AndAlso(combined, rewritten);
        }

        return combined;
    }
}

internal static class ExpressionSqlTranslator
{
    public static string Translate(
        Expression expr,
        JoinNode root,
        DbContext db,
        SqlParameterAccumulator parameters)
    {
        switch (expr)
        {
            case MemberExpression me:
                return TranslateMemberAccess(me, root, parameters);

            case BinaryExpression be:
                var left = Translate(be.Left, root, db, parameters);
                var op = GetSqlOperator(be.NodeType);
                var right = Translate(be.Right, root, db, parameters);
                return $"{left} {op} {right}";

            case ConstantExpression ce:
                return FormatConstant(ce.Value, parameters);

            case UnaryExpression ue:
                return TranslateUnary(ue, root, db, parameters);

            case MethodCallExpression mc:
                return TranslateMethodCall(mc, root, db, parameters);

            case ConditionalExpression ce:
                return $"CASE WHEN {Translate(ce.Test, root, db, parameters)} " +
                       $"THEN {Translate(ce.IfTrue, root, db, parameters)} " +
                       $"ELSE {Translate(ce.IfFalse, root, db, parameters)} END";

            case ParameterExpression:
                return root.TableAlias;

            default:
                throw new NotSupportedException(
                    $"Expression type '{expr.NodeType}' is not supported: {expr}");
        }
    }

    // -------------------------------------------------------
    // 1. Member Access (properties)
    // -------------------------------------------------------
    private static string TranslateMemberAccess(MemberExpression me, JoinNode root, SqlParameterAccumulator parameters)
    {
        var (rootParam, chain, last) = CollectMemberChain(me);

        if (rootParam == null)
        {
            // Captured variables (e.g. outside locals) become constants
            object? value = GetValueFromExpression(me);
            return FormatConstant(value, parameters);
        }

        // Navigate through join tree
        var node = root;
        foreach (var member in chain)
        {
            var nav = node.EntityType.FindNavigation(member.Name)
                  ?? (INavigationBase?)node.EntityType.FindSkipNavigation(member.Name);

            if (nav == null)
            {
                throw new InvalidOperationException(
                    $"'{member.Name}' is not a navigation on '{node.EntityType.Name}'.");
            }

            node = node.Children.First(c => c.IncomingNavigation == nav);
        }

        // Final member = scalar property
        var property = node.EntityType.FindProperty(last.Name)
                   ?? throw new InvalidOperationException(
                       $"Property '{last.Name}' not mapped on entity '{node.EntityType.Name}'");

        var column = property.GetColumnName();
        return $"{node.TableAlias}.{column}";
    }

    private static (ParameterExpression? rootParam, List<MemberInfo> chain, MemberInfo last)
        CollectMemberChain(MemberExpression leaf)
    {
        var list = new List<MemberInfo>();
        Expression? expr = leaf;

        while (expr is MemberExpression me)
        {
            list.Add(me.Member);
            expr = me.Expression;
        }

        list.Reverse();

        if (expr is not ParameterExpression param)
            return (null, new List<MemberInfo>(), leaf.Member);

        return (param, list.Take(list.Count - 1).ToList(), list.Last());
    }

    private static object? GetValueFromExpression(MemberExpression me)
    {
        var objectMember = Expression.Convert(me, typeof(object));
        var getterLambda = Expression.Lambda<Func<object>>(objectMember);
        return getterLambda.Compile().Invoke();
    }


    // -------------------------------------------------------
    // 2. Operators (==, !=, +, -, etc.)
    // -------------------------------------------------------
    public static string GetSqlOperator(ExpressionType type) =>
        type switch
        {
            ExpressionType.Add => "+",
            ExpressionType.Subtract => "-",
            ExpressionType.Multiply => "*",
            ExpressionType.Divide => "/",
            ExpressionType.Equal => "=",
            ExpressionType.NotEqual => "<>",
            ExpressionType.GreaterThan => ">",
            ExpressionType.GreaterThanOrEqual => ">=",
            ExpressionType.LessThan => "<",
            ExpressionType.LessThanOrEqual => "<=",
            ExpressionType.AndAlso => "AND",
            ExpressionType.OrElse => "OR",
            _ => throw new NotSupportedException($"Operator '{type}' is not supported.")
        };


    // -------------------------------------------------------
    // 3. SQL Constants
    // -------------------------------------------------------
    public static string FormatConstant(object? value, SqlParameterAccumulator parameters) =>
        parameters.Add(value);


    // -------------------------------------------------------
    // 4. Unary Expressions (!, negation, casts)
    // -------------------------------------------------------
    private static string TranslateUnary(UnaryExpression ue, JoinNode root, DbContext db, SqlParameterAccumulator parameters)
    {
        return ue.NodeType switch
        {
            ExpressionType.Convert or ExpressionType.ConvertChecked =>
                Translate(ue.Operand, root, db, parameters),

            ExpressionType.Negate or ExpressionType.NegateChecked =>
                "-" + Translate(ue.Operand, root, db, parameters),

            ExpressionType.Not when ue.Operand.Type == typeof(bool) =>
                $"(CASE WHEN {Translate(ue.Operand, root, db, parameters)} = 1 THEN 0 ELSE 1 END)",

            _ => throw new NotSupportedException($"Unary operator '{ue.NodeType}' not supported.")
        };
    }


    // -------------------------------------------------------
    // 5. Method Calls (Contains, StartsWith, ToUpper, etc.)
    // -------------------------------------------------------
    private static string TranslateMethodCall(MethodCallExpression mc, JoinNode root, DbContext db, SqlParameterAccumulator parameters)
    {
        // string.Concat(...)
        if (mc.Method.DeclaringType == typeof(string) &&
            mc.Method.Name == nameof(string.Concat))
        {
            var args = mc.Arguments.Select(a => Translate(a, root, db, parameters));
            return string.Join(" + ", args); // SQL Server concatenation
        }

        // string methods
        if (mc.Method.DeclaringType == typeof(string))
        {
            var instance = mc.Object != null ? Translate(mc.Object, root, db, parameters) : null;

            return mc.Method.Name switch
            {
                nameof(string.ToUpper) => $"UPPER({instance})",
                nameof(string.ToLower) => $"LOWER({instance})",
                nameof(string.Trim) => $"LTRIM(RTRIM({instance}))",
                nameof(string.TrimStart) => $"LTRIM({instance})",
                nameof(string.TrimEnd) => $"RTRIM({instance})",

                nameof(string.StartsWith) =>
                    $"{instance} LIKE {Translate(mc.Arguments[0], root, db, parameters)} + '%' ",

                nameof(string.EndsWith) =>
                    $"{instance} LIKE '%' + {Translate(mc.Arguments[0], root, db, parameters)} ",

                nameof(string.Contains) =>
                    $"{instance} LIKE '%' + {Translate(mc.Arguments[0], root, db, parameters)} + '%'",

                nameof(string.Substring) when mc.Arguments.Count == 2 =>
                    $"SUBSTRING({instance}, ({Translate(mc.Arguments[0], root, db, parameters)}) + 1, {Translate(mc.Arguments[1], root, db, parameters)})",

                nameof(string.Substring) when mc.Arguments.Count == 1 =>
                    $"SUBSTRING({instance}, ({Translate(mc.Arguments[0], root, db, parameters)}) + 1, LEN({instance}) - ({Translate(mc.Arguments[0], root, db, parameters)}))",

                _ => throw new NotSupportedException($"String method '{mc.Method.Name}' is not supported.")
            };
        }

        throw new NotSupportedException(
            $"Method call '{mc.Method.DeclaringType}.{mc.Method.Name}' not supported in SQL translation.");
    }
}

internal sealed class ParameterReplacer : ExpressionVisitor
{
    private readonly ParameterExpression _source;
    private readonly ParameterExpression _target;

    private ParameterReplacer(ParameterExpression source, ParameterExpression target)
    {
        _source = source;
        _target = target;
    }

    public static Expression Replace(Expression expression, ParameterExpression source, ParameterExpression target)
    {
        return new ParameterReplacer(source, target).Visit(expression)!;
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        if (node == _source)
        {
            return _target;
        }

        return base.VisitParameter(node);
    }
}