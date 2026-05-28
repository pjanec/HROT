using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Fdp.Core.Serialization.Migrations.Internal;

/// <summary>
/// Walks a <see cref="DiffNode"/> tree produced by <see cref="DomDiffer"/>
/// and flattens the result into the flat JSONPath-based journal operation
/// list expected by <see cref="Fdp.Core.Serialization.Migrations.UnknownsJournal"/>.
/// </summary>
internal static class DiffToJournalConverter
{
    /// <summary>
    /// Converts a DiffNode tree into a list of journal operations.
    /// </summary>
    /// <param name="diffRoot">
    /// The root of the diff tree. Pass <c>null</c> (identical DOMs) to
    /// receive an empty list.
    /// </param>
    /// <param name="preMigrationDom">
    /// The DOM as it existed before migration. Used to extract original
    /// values for <c>Set</c> operations and to resolve array-index segments.
    /// </param>
    public static IReadOnlyList<JournalOperation> Convert(
        DiffNode? diffRoot,
        JsonObject preMigrationDom)
    {
        if (diffRoot is null || !diffRoot.IsModified)
            return Array.Empty<JournalOperation>();

        var ops = new List<JournalOperation>();
        var pathStack = new List<object>();
        Walk(diffRoot, preMigrationDom, pathStack, ops);
        return ops.AsReadOnly();
    }

    private static void Walk(
        DiffNode node,
        JsonObject rootDom,
        List<object> pathStack,
        List<JournalOperation> ops)
    {
        if (!node.IsModified)
            return;

        if (node is DiffObject obj)
        {
            foreach (var child in obj.Children)
            {
                if (!child.IsModified) continue;

                // $meta is pipeline-managed; never capture it as an unknown.
                if (pathStack.Count == 0 && child.Name == "$meta") continue;

                object segment = DetermineSegment(child.Name, rootDom, pathStack);
                pathStack.Add(segment);
                Walk(child, rootDom, pathStack, ops);
                pathStack.RemoveAt(pathStack.Count - 1);
            }
        }
        else if (node is DiffValue)
        {
            var path = JsonPathParser.Build(pathStack);
            var parsedPath = JsonPathParser.Parse(path);
            var preValue = parsedPath.Read(rootDom);

            if (preValue is not null)
                ops.Add(new JournalOperation(JournalOpKind.Set, path, preValue.DeepClone()));
            else
                ops.Add(new JournalOperation(JournalOpKind.Remove, path, null));
        }
    }

    /// <summary>
    /// Determines the path segment (string key or int index) for a child node.
    /// If the parent node in <paramref name="rootDom"/> is a <see cref="JsonArray"/>
    /// and <paramref name="name"/> is a valid non-negative integer, the segment
    /// is returned as an <see cref="int"/>; otherwise it is returned as a
    /// <see cref="string"/>.
    /// </summary>
    private static object DetermineSegment(
        string name,
        JsonObject rootDom,
        List<object> pathStack)
    {
        if (pathStack.Count > 0
            && int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int idx)
            && idx >= 0)
        {
            var parentPath = JsonPathParser.Build(pathStack);
            var parentNode = JsonPathParser.Parse(parentPath).Read(rootDom);
            if (parentNode is JsonArray)
                return idx;
        }

        return name;
    }
}
