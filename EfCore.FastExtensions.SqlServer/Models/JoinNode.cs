using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System;
using System.Collections.Generic;
using System.Text;

namespace EfCore.FastExtensions.SqlServer.Models;

internal enum SqlJoinType
{
    Inner,
    Left
}
internal sealed class JoinNode
{
    private readonly List<JoinNode> _children = new();

    public JoinNode(
        IEntityType entityType,
        string tableAlias,
        string tableName,
        JoinNode? parent = null,
        INavigationBase? incomingNavigation = null,
        IForeignKey? incomingFk = null,
        SqlJoinType joinType = SqlJoinType.Inner)
    {
        EntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
        TableAlias = tableAlias ?? throw new ArgumentNullException(nameof(tableAlias));
        TableName = tableName ?? throw new ArgumentNullException(nameof(tableName));

        Parent = parent;
        IncomingNavigation = incomingNavigation;
        IncomingFk = incomingFk;
        JoinType = joinType;
    }

    public IEntityType EntityType { get; }
    public string TableAlias { get; }
    public string TableName { get; }

    /// <summary>Parent node (null for root).</summary>
    public JoinNode? Parent { get; }

    /// <summary>Children join nodes.</summary>
    public IReadOnlyList<JoinNode> Children => _children;

    /// <summary>
    /// Navigation from Parent -> this node (null for root).
    /// </summary>
    public INavigationBase? IncomingNavigation { get; }

    /// <summary>
    /// Foreign key that defines Parent–Child join (null for root).
    /// Useful for ON clause.
    /// </summary>
    public IForeignKey? IncomingFk { get; }

    public SqlJoinType JoinType { get; }

    /// <summary>Debug path (root.State.Country etc.).</summary>
    public string DebugPath =>
        Parent == null ? TableAlias : $"{Parent.DebugPath}.{IncomingNavigation?.Name ?? TableAlias}";

    internal void AddChild(JoinNode child)
    {
        _children.Add(child);
    }

    /// <summary>Flattens the tree (root first).</summary>
    public IEnumerable<JoinNode> PreOrder()
    {
        yield return this;

        foreach (var c in _children.SelectMany(x => x.PreOrder()))
            yield return c;
    }

    /// <summary>
    /// Get dependent/principal property pairs to build JOIN ON clause.
    /// </summary>
    public IEnumerable<JoinKey> GetJoinKeys()
    {
        if (IncomingFk == null)
            yield break;

        var dependentProps = IncomingFk.Properties;
        var principalProps = IncomingFk.PrincipalKey.Properties;

        for (int i = 0; i < dependentProps.Count; i++)
        {
            var dep = dependentProps[i];
            var prin = principalProps[i];

            // If this FK is from this entity to parent or vice versa,
            // we use aliases accordingly
            var parentAlias = Parent?.TableAlias ?? Parent?.EntityType.GetTableName() ?? "tParent";
            var thisAlias = TableAlias;

            bool childIsDependent = IncomingFk.DeclaringEntityType == EntityType;

            yield return childIsDependent
                ? new JoinKey(
                    LeftAlias: thisAlias,
                    LeftColumn: dep.GetColumnName(),
                    RightAlias: parentAlias!,
                    RightColumn: prin.GetColumnName())
                : new JoinKey(
                    LeftAlias: parentAlias!,
                    LeftColumn: dep.GetColumnName(),
                    RightAlias: thisAlias,
                    RightColumn: prin.GetColumnName());
        }
    }
}

public readonly record struct JoinKey(
    string LeftAlias,
    string LeftColumn,
    string RightAlias,
    string RightColumn);