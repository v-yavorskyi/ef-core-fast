using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using EfCore.FastExtensions.SqlServer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EfCore.FastExtensions.SqlServer.Builders;

internal static class SetClauseBuilder
{
    public static string BuildSetClauseFromOperations<TEntity>(
        List<SetOperation> operations,
        JoinNode root,
        DbContext db,
        SqlParameterAccumulator parameters)
    {
        if (operations == null) throw new ArgumentNullException(nameof(operations));
        if (root == null) throw new ArgumentNullException(nameof(root));
        if (db == null) throw new ArgumentNullException(nameof(db));
        if (parameters == null) throw new ArgumentNullException(nameof(parameters));

        var sets = operations
            .Select(op => BuildSetClause(op, root, db, parameters))
            .ToList();

        return string.Join(", ", sets);
    }

    private static string BuildSetClause(
        SetOperation op,
        JoinNode root,
        DbContext db,
        SqlParameterAccumulator parameters)
    {
        // Left: p => p.Region
        var leftSql = TranslateMemberAccessLeft(op.Property, root);

        // Right: p => p.State.StateName + p.State.Country.Name
        var rightSql = TranslateExpression(op.Value.Body, root, db, parameters);

        return $"{leftSql} = {rightSql}";
    }

    #region LEFT side (target column)

    /// <summary>
    /// Converts p => p.Region -> t.Region
    /// </summary>
    private static string TranslateMemberAccessLeft(
        LambdaExpression lambda,
        JoinNode root)
    {
        if (lambda.Body is not MemberExpression me)
            throw new NotSupportedException(
                "Left side of SetProperty must be a simple member access, e.g. p => p.Region.");

        var propertyName = me.Member.Name;

        var property = root.EntityType.FindProperty(propertyName)
                       ?? throw new InvalidOperationException(
                           $"Property '{propertyName}' is not mapped on entity '{root.EntityType.Name}'.");

        var columnName = property.GetColumnName();

        return $"{root.TableAlias}.{columnName}";
    }

    #endregion

    #region RIGHT side (full expression)

    private static string TranslateExpression(
        Expression expr,
        JoinNode root,
        DbContext db,
        SqlParameterAccumulator parameters)
    {
        switch (expr)
        {
            case MemberExpression me:
                return TranslateMemberAccessRight(me, root);

            case BinaryExpression be:
                return $"{TranslateExpression(be.Left, root, db, parameters)} {GetSqlOperator(be.NodeType)} {TranslateExpression(be.Right, root, db, parameters)}";

            case ConstantExpression ce:
                return FormatConstant(ce.Value, parameters);

            case UnaryExpression ue:
                return TranslateUnary(ue, root, db, parameters);

            case MethodCallExpression mc:
                return TranslateMethodCall(mc, root, db, parameters);

            case ConditionalExpression ce:
                return
                    $"CASE WHEN {TranslateExpression(ce.Test, root, db, parameters)} " +
                    $"THEN {TranslateExpression(ce.IfTrue, root, db, parameters)} " +
                    $"ELSE {TranslateExpression(ce.IfFalse, root, db, parameters)} END";

            case ParameterExpression:
                // Root parameter "p" – usually not used bare on RHS
                return root.TableAlias;

            default:
                throw new NotSupportedException(
                    $"Expression node type '{expr.NodeType}' is not supported in SET clause: {expr}.");
        }
    }

    /// <summary>
    /// p.State.StateName → t_State.StateName
    /// p.State.Country.Name → t_State_Country.Name
    /// </summary>
    private static string TranslateMemberAccessRight(
        MemberExpression me,
        JoinNode root)
    {
        var (rootParam, membersExceptLast, lastMember) = CollectMemberChain(me);

        if (rootParam == null)
            throw new InvalidOperationException(
                "Member access in SetProperty value must ultimately start from the lambda parameter (e.g. p => p.State.Name).");

        var node = root;
        // walk navigations
        foreach (var member in membersExceptLast)
        {
            var nav = node.EntityType.FindNavigation(member.Name)
                      ?? (INavigationBase?)node.EntityType.FindSkipNavigation(member.Name);

            if (nav == null)
            {
                // if this is not navigation, we stop — means last scalar on root
                throw new InvalidOperationException(
                    $"Member '{member.Name}' is not a navigation on entity '{node.EntityType.Name}'.");
            }

            node = node.Children
                .FirstOrDefault(c => c.IncomingNavigation == nav)
                ?? throw new InvalidOperationException(
                    $"JoinNode tree does not contain navigation '{nav.Name}' from '{node.EntityType.Name}'. " +
                    "Ensure JoinTreeBuilder picked this navigation from expressions.");
        }

        // last member should be scalar property on current node's entity
        var property = node.EntityType.FindProperty(lastMember.Name)
                       ?? throw new InvalidOperationException(
                           $"Property '{lastMember.Name}' is not mapped on entity '{node.EntityType.Name}'.");

        var columnName = property.GetColumnName();

        return $"{node.TableAlias}.{columnName}";
    }

    private static (ParameterExpression? RootParameter, List<MemberInfo> MembersExceptLast, MemberInfo LastMember)
        CollectMemberChain(MemberExpression leaf)
    {
        var stack = new Stack<MemberInfo>();
        Expression? current = leaf;

        while (current is MemberExpression me)
        {
            stack.Push(me.Member);
            current = me.Expression;
        }

        if (current is not ParameterExpression param)
        {
            // e.g. accessing a captured variable instead of p.xxx
            return (null, new List<MemberInfo>(), leaf.Member);
        }

        var list = stack.ToList();
        var last = list.Last();
        var allButLast = list.Take(list.Count - 1).ToList();

        return (param, allButLast, last);
    }

    #endregion

    #region Helpers: operators, constants, unary, method calls

    private static string GetSqlOperator(ExpressionType type) =>
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
            _ => throw new NotSupportedException($"Binary operator '{type}' is not supported.")
        };

    private static string FormatConstant(object? value, SqlParameterAccumulator parameters) =>
        parameters.Add(value);

    private static string TranslateUnary(
        UnaryExpression ue,
        JoinNode root,
        DbContext db,
        SqlParameterAccumulator parameters)
    {
        // Most common case: (T)someExpression → we just ignore the cast
        if (ue.NodeType == ExpressionType.Convert || ue.NodeType == ExpressionType.ConvertChecked)
        {
            return TranslateExpression(ue.Operand, root, db, parameters);
        }

        if (ue.NodeType == ExpressionType.Negate || ue.NodeType == ExpressionType.NegateChecked)
        {
            return "-" + TranslateExpression(ue.Operand, root, db, parameters);
        }

        if (ue.NodeType == ExpressionType.Not && ue.Type == typeof(bool))
        {
            return $"(CASE WHEN {TranslateExpression(ue.Operand, root, db, parameters)} = 1 THEN 0 ELSE 1 END)";
        }

        throw new NotSupportedException($"Unary operator '{ue.NodeType}' is not supported.");
    }

    private static string TranslateMethodCall(
        MethodCallExpression mc,
        JoinNode root,
        DbContext db,
        SqlParameterAccumulator parameters)
    {
        // string.Concat(...)
        if (mc.Method.DeclaringType == typeof(string) && mc.Method.Name == nameof(string.Concat))
        {
            var args = mc.Arguments.Select(a => TranslateExpression(a, root, db, parameters));
            // Using + for SQL string concatenation (SQL Server style)
            return string.Join(" + ", args);
        }

        // instance string methods: p.Name.ToUpper(), p.Name.ToLower(), etc.
        if (mc.Method.DeclaringType == typeof(string))
        {
            var instanceSql = mc.Object != null ? TranslateExpression(mc.Object, root, db, parameters) : null;

            switch (mc.Method.Name)
            {
                case nameof(string.ToUpper):
                case nameof(string.ToUpperInvariant):
                    return $"UPPER({instanceSql})";

                case nameof(string.ToLower):
                case nameof(string.ToLowerInvariant):
                    return $"LOWER({instanceSql})";

                case nameof(string.Trim):
                    return $"LTRIM(RTRIM({instanceSql}))";

                case nameof(string.TrimStart):
                    return $"LTRIM({instanceSql})";

                case nameof(string.TrimEnd):
                    return $"RTRIM({instanceSql})";

                case nameof(string.Substring) when mc.Arguments.Count == 2:
                    // Substring(start, length) → SUBSTRING(col, start + 1, length)
                    var start = TranslateExpression(mc.Arguments[0], root, db, parameters);
                    var len = TranslateExpression(mc.Arguments[1], root, db, parameters);
                    return $"SUBSTRING({instanceSql}, ({start}) + 1, {len})";

                case nameof(string.Substring) when mc.Arguments.Count == 1:
                    // Substring(start) → SUBSTRING(col, start + 1, LEN(col) - start)
                    var s = TranslateExpression(mc.Arguments[0], root, db, parameters);
                    return $"SUBSTRING({instanceSql}, ({s}) + 1, LEN({instanceSql}) - ({s}))";

                case nameof(string.StartsWith):
                    return $"{instanceSql} LIKE {TranslateExpression(mc.Arguments[0], root, db, parameters)} + '%'";

                case nameof(string.EndsWith):
                    return $"{instanceSql} LIKE '%' + {TranslateExpression(mc.Arguments[0], root, db, parameters)}";

                case nameof(string.Contains):
                    return $"{instanceSql} LIKE '%' + {TranslateExpression(mc.Arguments[0], root, db, parameters)} + '%'";
            }
        }

        // Nullable.HasValue / Nullable.Value → translate as usual
        if (mc.Method.Name == "GetValueOrDefault" && mc.Object != null)
        {
            return TranslateExpression(mc.Object, root, db, parameters);
        }

        throw new NotSupportedException(
            $"Method call '{mc.Method.DeclaringType?.Name}.{mc.Method.Name}' is not supported in SET expressions.");
    }

    // Helper alias to avoid typo above
    private const string nameof_stringSubstring = "Substring";

    #endregion
}