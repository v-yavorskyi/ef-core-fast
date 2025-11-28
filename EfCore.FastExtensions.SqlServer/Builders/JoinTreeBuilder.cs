using EfCore.FastExtensions.SqlServer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Linq.Expressions;
using System.Reflection;

namespace EfCore.FastExtensions.SqlServer.Builders;


internal static class JoinTreeBuilder
{
    public static JoinNode BuildJoinTree<TEntity>(
        DbContext db,
        IEnumerable<Expression> expressions,
        string rootAlias = "t")
        where TEntity : class
    {
        if (db == null) throw new ArgumentNullException(nameof(db));
        if (expressions == null) throw new ArgumentNullException(nameof(expressions));

        var entityType = db.Model.FindEntityType(typeof(TEntity))
                         ?? throw new InvalidOperationException(
                             $"Entity type {typeof(TEntity).Name} is not part of the DbContext model.");

        var tableName = entityType.GetTableName()
                        ?? throw new InvalidOperationException(
                            $"Entity type {entityType.DisplayName()} is not mapped to a table.");

        var root = new JoinNode(
            entityType: entityType,
            tableAlias: rootAlias,
            tableName: tableName,
            parent: null,
            incomingNavigation: null,
            incomingFk: null,
            joinType: SqlJoinType.Inner);

        foreach (var lambda in expressions)
        {
            VisitExpression(lambda, root);
        }

        return root;
    }

    #region Expression walking

    private static void VisitExpression(Expression expr, JoinNode root)
    {
        switch (expr)
        {
            case MemberExpression me:
                HandleMemberChain(me, root);
                VisitExpression(me.Expression!, root);
                break;

            case BinaryExpression be:
                VisitExpression(be.Left, root);
                VisitExpression(be.Right, root);
                break;

            case MethodCallExpression mc:
                foreach (var arg in mc.Arguments)
                    VisitExpression(arg, root);
                break;

            case UnaryExpression ue:
                VisitExpression(ue.Operand, root);
                break;

            case ConditionalExpression ce:
                VisitExpression(ce.Test, root);
                VisitExpression(ce.IfTrue, root);
                VisitExpression(ce.IfFalse, root);
                break;

                // constants, parameters, etc. — nothing to do
        }
    }

    /// <summary>
    /// Handles a member chain like p.State.Country.Name:
    /// we are interested in navigations: State, Country.
    /// </summary>
    private static void HandleMemberChain(MemberExpression leaf, JoinNode root)
    {
        // Collect chain upwards: Name <- Country <- State <- p
        var members = new Stack<MemberInfo>();
        Expression? current = leaf;

        while (current is MemberExpression me)
        {
            members.Push(me.Member);
            current = me.Expression;
        }

        // We only care if chain starts at root parameter (p)
        if (current is not ParameterExpression)
            return;

        var entityType = root.EntityType;
        var currentNode = root;

        while (members.Count > 0)
        {
            var member = members.Pop();

            // Try to interpret the member as a navigation from current entity
            var navigation =
                (INavigationBase?)entityType.FindNavigation(member.Name) ??
                entityType.FindSkipNavigation(member.Name);

            if (navigation == null)
            {
                // scalar property -> we stop, no more navigations in this chain
                return;
            }

            currentNode = GetOrCreateChildForNavigation(currentNode, navigation);
            entityType = navigation.TargetEntityType;
        }
    }

    private static JoinNode GetOrCreateChildForNavigation(
    JoinNode parent,
    INavigationBase navigation)
    {
        var existing = parent.Children
            .FirstOrDefault(c => c.IncomingNavigation == navigation);

        if (existing != null)
            return existing;

        var targetEntityType = navigation.TargetEntityType;

        var tableName = targetEntityType.GetTableName()
                        ?? throw new InvalidOperationException(
                            $"Entity '{targetEntityType.DisplayName()}' is not mapped to a table.");

        // Determine FK (only supported for INavigation)
        IForeignKey? fk = null;

        if (navigation is INavigation nav)
        {
            fk = nav.ForeignKey;   // ✔️ valid
        }
        else if (navigation is ISkipNavigation skip)
        {
            // ❗ For many-to-many you need join table
            // but your Update-with-join should *not* support M:N for now

            throw new NotSupportedException(
                $"Many-to-many navigation '{navigation.Name}' is not supported in UPDATE JOIN. " +
                "Avoid using skip navigations in SetProperty expressions.");
        }

        // Generate alias
        var alias = $"{parent.TableAlias}_{navigation.Name}";

        var node = new JoinNode(
            entityType: targetEntityType,
            tableAlias: alias,
            tableName: tableName,
            parent: parent,
            incomingNavigation: navigation,
            incomingFk: fk,
            joinType: SqlJoinType.Inner);

        parent.AddChild(node);

        return node;
    }
    #endregion
}