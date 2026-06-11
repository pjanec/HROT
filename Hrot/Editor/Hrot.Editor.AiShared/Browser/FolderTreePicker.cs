namespace Hrot.Editor.AiShared.Browser;

/// <summary>
/// A pure, ImGui-free tree builder that constructs a folder hierarchy from a list of
/// relative paths (using <c>/</c> separators). Each path is a leaf (e.g.
/// <c>"combat/patrol/Guard.bp.json"</c> or a bare <c>"Scout"</c>).
/// </summary>
/// <remarks>
/// This is the <b>read mode</b> tree-builder used by the Asset Browser panel (§10.1).
/// The pick mode (select/add folder, bounded to a root) is implemented separately
/// (MTB-P6-T4).
/// </remarks>
public sealed class FolderTreeNode
{
    /// <summary>The segment name (folder name or leaf name).</summary>
    public string Name { get; }

    /// <summary>The accumulated relative path to this node (using <c>/</c> separators).</summary>
    public string FullPath { get; }

    /// <summary>
    /// <see langword="true"/> for terminal asset paths (leaves);
    /// <see langword="false"/> for intermediate folder segments and the root node.
    /// </summary>
    public bool IsLeaf { get; }

    /// <summary>
    /// Child nodes, sorted deterministically: folders first, then leaves, each group
    /// alphabetically by <see cref="Name"/> (ordinal comparison). Never <see langword="null"/>.
    /// </summary>
    public IReadOnlyList<FolderTreeNode> Children { get; }

    internal FolderTreeNode(string name, string fullPath, bool isLeaf,
        IReadOnlyList<FolderTreeNode> children)
    {
        Name = name;
        FullPath = fullPath;
        IsLeaf = isLeaf;
        Children = children;
    }
}

/// <summary>
/// Static entry point for building a folder tree from a flat list of relative asset paths.
/// </summary>
public static class FolderTreePicker
{
    /// <summary>
    /// Builds a folder hierarchy from a collection of relative paths.
    /// </summary>
    /// <param name="relativePaths">
    /// Relative asset paths using <c>/</c> separators (e.g. <c>"combat/patrol/Guard.bp.json"</c>).
    /// <see langword="null"/> and empty entries are silently skipped.
    /// </param>
    /// <returns>
    /// A root <see cref="FolderTreeNode"/> whose <see cref="FolderTreeNode.Children"/>
    /// contain the top-level folder and leaf nodes. The root itself has an empty
    /// <see cref="FolderTreeNode.Name"/> and <see cref="FolderTreeNode.FullPath"/> and
    /// is never a leaf. Returns a root with no children when input is empty or contains
    /// only skipped entries.
    /// </returns>
    /// <remarks>
    /// <para><b>Sort rule (stable/deterministic):</b> children at every level are ordered
    /// folders-first, leaves-second. Within each group, entries are sorted alphabetically
    /// by name using <see cref="StringComparer.Ordinal"/>. The same set of input paths
    /// always produces the same tree regardless of input order.</para>
    /// </remarks>
    public static FolderTreeNode Build(IEnumerable<string>? relativePaths)
    {
        // Mutable trie: fullPath (using '/') → set of child fullPaths.
        var childrenOf = new Dictionary<string, HashSet<string>>();
        var isLeaf = new Dictionary<string, bool>();
        var nameOf = new Dictionary<string, string>();

        // Seed root.
        childrenOf[""] = new HashSet<string>();
        isLeaf[""] = false;
        nameOf[""] = "";

        if (relativePaths == null)
            return Freeze("", childrenOf, isLeaf, nameOf);

        foreach (var rawPath in relativePaths)
        {
            if (string.IsNullOrEmpty(rawPath))
                continue;

            var segments = rawPath.Split('/');
            var accumulated = "";

            for (int i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                var prev = accumulated;
                accumulated = i == 0 ? segment : accumulated + "/" + segment;
                var isLastSegment = (i == segments.Length - 1);

                // Ensure parent exists (only relevant for i>0, since root always exists).
                var parentKey = i == 0 ? "" : prev;
                if (!childrenOf.ContainsKey(parentKey))
                {
                    // Create intermediate parent — needed when a deeper path introduces
                    // a mid-level folder that has no explicit entry yet.
                    // (Root always exists; for i>0, prev was created in a previous iteration
                    // of THIS path's loop, so this should be unreachable. But guard anyway.)
                    childrenOf[parentKey] = new HashSet<string>();
                    isLeaf[parentKey] = false;
                    nameOf[parentKey] = parentKey.Contains('/')
                        ? parentKey.Substring(parentKey.LastIndexOf('/') + 1)
                        : parentKey;
                }

                if (!childrenOf.ContainsKey(accumulated))
                {
                    // New node.
                    childrenOf[accumulated] = new HashSet<string>();
                    isLeaf[accumulated] = isLastSegment;
                    nameOf[accumulated] = segment;

                    // Link parent → child.
                    childrenOf[parentKey].Add(accumulated);
                }
                else if (isLastSegment)
                {
                    // Existing node (folder from another path) is also a leaf.
                    isLeaf[accumulated] = true;
                }
            }
        }

        return Freeze("", childrenOf, isLeaf, nameOf);
    }

    private static FolderTreeNode Freeze(
        string fullPath,
        Dictionary<string, HashSet<string>> childrenOf,
        Dictionary<string, bool> isLeaf,
        Dictionary<string, string> nameOf)
    {
        var childPaths = childrenOf.GetValueOrDefault(fullPath);
        var frozenChildren = new List<FolderTreeNode>();

        if (childPaths != null)
        {
            foreach (var childPath in childPaths)
            {
                frozenChildren.Add(Freeze(childPath, childrenOf, isLeaf, nameOf));
            }
        }

        // Sort: folders first (IsLeaf=false → group 0), leaves second (IsLeaf=true → group 1),
        // then alphabetical by Name (ordinal).
        frozenChildren.Sort((a, b) =>
        {
            var groupCmp = (a.IsLeaf ? 1 : 0).CompareTo(b.IsLeaf ? 1 : 0);
            if (groupCmp != 0)
                return groupCmp;
            return StringComparer.Ordinal.Compare(a.Name, b.Name);
        });

        return new FolderTreeNode(
            nameOf.GetValueOrDefault(fullPath, ""),
            fullPath,
            isLeaf.GetValueOrDefault(fullPath, false),
            frozenChildren.AsReadOnly());
    }
}
