using EfCore.FastExtensions.SqlServer.Models;
using System.Text;
using System.Threading.Tasks;

namespace EfCore.FastExtensions.SqlServer.Builders;

internal static class FromClauseBuilder
{
    public static string BuildFromClause(JoinNode root)
    {
        var sb = new StringBuilder();

        // 1. Root table
        sb.AppendLine($"{root.TableName} AS {root.TableAlias}");

        // 2. All other nodes (children, grandchildren…)
        foreach (var node in root.Children.SelectMany(c => c.PreOrder()))
        {
            if (node.Parent == null)
                continue; // skip root

            AppendJoinClause(sb, node);
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendJoinClause(StringBuilder sb, JoinNode node)
    {
        var joinType = node.JoinType == SqlJoinType.Left ? "LEFT JOIN" : "INNER JOIN";

        sb.AppendLine($"{joinType} {node.TableName} AS {node.TableAlias}");

        var joinKeys = node.GetJoinKeys().ToList();
        if (joinKeys.Count == 0)
        {
            throw new InvalidOperationException(
                $"Cannot build JOIN ON clause: node {node.TableAlias} has no JoinKeys (no FK metadata).");
        }

        sb.Append("    ON ");

        for (int i = 0; i < joinKeys.Count; i++)
        {
            var key = joinKeys[i];

            if (i > 0)
                sb.Append(" AND ");

            sb.Append($"{key.LeftAlias}.{key.LeftColumn} = {key.RightAlias}.{key.RightColumn}");
        }

        sb.AppendLine();
    }
}
