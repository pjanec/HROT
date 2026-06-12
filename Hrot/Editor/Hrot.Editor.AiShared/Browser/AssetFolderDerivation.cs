using NodeEditor.UI.Picker;

namespace Hrot.Editor.AiShared.Browser;

/// <summary>
/// Derives logical subfolder knowledge from the asset catalog, independent of
/// the real filesystem. Used to seed <see cref="FolderPickerState"/> for the
/// New-Asset / Save-As name+folder modal.
/// </summary>
public static class AssetFolderDerivation
{
    /// <summary>
    /// Returns the distinct logical subfolder relative paths that already exist
    /// for <paramref name="kind"/>, derived from the catalog assets'
    /// <see cref="AssetRelPath.RelPath"/> directory parts (NOT the filesystem).
    /// </summary>
    /// <param name="assets">
    /// The asset catalog snapshot (typically <see cref="Catalog.IAssetCatalog.All"/>).
    /// </param>
    /// <param name="kind">The asset kind to filter by.</param>
    /// <param name="baseFolderResolver">
    /// Returns the base folder for a given <see cref="AssetKind"/> (used for
    /// relative-path computation). Typically
    /// <see cref="AssetBrowserPanel.BaseFolderFor"/>.
    /// </param>
    /// <returns>
    /// A sorted, case-insensitive-distinct list of subfolder relative paths
    /// (using <c>/</c> separators). Always includes <c>""</c> (root).
    /// When no assets of <paramref name="kind"/> exist, returns <c>[""]</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The subfolder extraction mirrors <see cref="AssetPickerSource.ToEntry"/>:
    /// take the directory part of <see cref="AssetRelPath.RelPath"/> (everything
    /// before the last <c>/</c>, or <c>""</c> when there is no separator).
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> KnownSubfolders(
        IReadOnlyList<IEditableAsset> assets,
        AssetKind kind,
        Func<AssetKind, string?> baseFolderResolver)
    {
        if (assets == null)
            throw new ArgumentNullException(nameof(assets));
        if (baseFolderResolver == null)
            throw new ArgumentNullException(nameof(baseFolderResolver));

        var baseFolder = baseFolderResolver(kind);
        var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "" };

        foreach (var asset in assets)
        {
            if (asset.Kind != kind)
                continue;

            var rel = AssetRelPath.RelPath(asset, baseFolder);

            // Extract the subfolder (directory part) from the relative path.
            // Mirrors AssetPickerSource.ToEntry (lines 138–142).
            int lastSlash = rel.LastIndexOf('/');
            string dir = lastSlash >= 0 ? rel.Substring(0, lastSlash) : "";

            distinct.Add(dir);
        }

        var result = distinct.ToList();
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result.AsReadOnly();
    }

    /// <summary>
    /// Builds a <see cref="CategoryNode"/> tree from a list of relative folder
    /// paths (using <c>/</c> separators). The root node has an empty
    /// <see cref="CategoryNode.Name"/> and its <see cref="CategoryNode.Children"/>
    /// mirror the <c>/</c>-split folder hierarchy.
    /// </summary>
    /// <param name="relPaths">
    /// Relative folder paths. <see langword="null"/> entries are skipped.
    /// The empty string <c>""</c> represents the root and is ignored as a path entry.
    /// </param>
    /// <returns>
    /// A root <see cref="CategoryNode"/> with <see cref="CategoryNode.Name"/> set to
    /// <c>""</c>. Children are sorted deterministically by
    /// <see cref="StringComparer.Ordinal"/> on their <see cref="CategoryNode.Name"/>.
    /// Returns a root with no children when <paramref name="relPaths"/> is empty.
    /// </returns>
    public static CategoryNode ToCategoryNode(IReadOnlyList<string> relPaths)
    {
        if (relPaths == null)
            throw new ArgumentNullException(nameof(relPaths));

        // Build trie: parent fullPath → set of child fullPaths.
        var childrenOf = new Dictionary<string, HashSet<string>>();
        childrenOf[""] = new HashSet<string>();

        foreach (var path in relPaths)
        {
            if (path == null) continue;
            if (path == "") continue; // root, already in the trie

            var segments = path.Split('/');
            var accumulated = "";

            for (int i = 0; i < segments.Length; i++)
            {
                var parent = accumulated;
                accumulated = i == 0 ? segments[i] : accumulated + "/" + segments[i];

                if (!childrenOf.ContainsKey(accumulated))
                {
                    childrenOf[accumulated] = new HashSet<string>();
                }

                // Link parent → child.
                if (childrenOf.ContainsKey(parent))
                    childrenOf[parent].Add(accumulated);
            }
        }

        return FreezeNode("", childrenOf);
    }

    /// <summary>
    /// Recursively builds a <see cref="CategoryNode"/> subtree from the trie.
    /// </summary>
    private static CategoryNode FreezeNode(
        string fullPath,
        Dictionary<string, HashSet<string>> childrenOf)
    {
        var name = string.IsNullOrEmpty(fullPath)
            ? ""
            : fullPath.Contains('/')
                ? fullPath.Substring(fullPath.LastIndexOf('/') + 1)
                : fullPath;

        var frozenChildren = new List<CategoryNode>();
        if (childrenOf.TryGetValue(fullPath, out var childPaths))
        {
            foreach (var childPath in childPaths)
            {
                frozenChildren.Add(FreezeNode(childPath, childrenOf));
            }
        }

        // Sort children by Name (ordinal, deterministic).
        frozenChildren.Sort((a, b) => StringComparer.Ordinal.Compare(a.Name, b.Name));

        return new CategoryNode(name, frozenChildren.AsReadOnly());
    }
}
