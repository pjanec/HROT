namespace NodeEditor.UI.Picker;

/// <summary>Pure builder that groups filtered picker entries into a folder/leaf tree
/// from each entry's Category path ("A/B/C"). Used by TreeLayout; unit-testable.</summary>
internal static class PickerTreeBuilder
{
    public sealed class Node
    {
        public string Name = "";                 // segment label (folder) ; leaf uses entry name
        public string FullPath = "";             // full category path of a folder ("A/B")
        public bool IsLeaf;
        public int FilteredIndex = -1;           // leaf: index into state.Filtered ; folder: -1
        public List<Node> Folders = new();       // child folders (sorted, OrdinalIgnoreCase)
        public List<Node> Leaves  = new();        // leaf children at this depth (in input order)
    }

    /// <summary>Build the root node. <paramref name="items"/> is the filtered list in display order;
    /// each item supplies its filtered index, Category (nullable), and Name.
    /// Folders are created only for categories that actually contain leaves (so empty/filtered-out
    /// folders are absent). Uncategorized entries become leaves directly under the root.</summary>
    public static Node Build(IReadOnlyList<(int FilteredIndex, string? Category, string Name)> items,
                             bool foldSingleChildFolders = false)
    {
        var root = new Node { Name = "" };

        // Build a temporary tree of folder nodes keyed by full path (case-insensitive).
        var folderMap = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);

        foreach (var (filteredIndex, category, name) in items)
        {
            if (string.IsNullOrEmpty(category))
            {
                // Uncategorized → leaf directly under root.
                root.Leaves.Add(new Node
                {
                    Name = name,
                    IsLeaf = true,
                    FilteredIndex = filteredIndex,
                });
                continue;
            }

            // Split category into segments.
            string[] segments = category.Split('/');
            string currentPath = "";

            // Ensure all ancestor folders exist.
            Node? parentFolder = root;
            for (int s = 0; s < segments.Length; s++)
            {
                string previousPath = currentPath;
                currentPath = s == 0 ? segments[s] : currentPath + "/" + segments[s];

                if (!folderMap.TryGetValue(currentPath, out var folderNode))
                {
                    folderNode = new Node
                    {
                        Name = segments[s],
                        FullPath = currentPath,
                        IsLeaf = false,
                    };
                    folderMap[currentPath] = folderNode;

                    // Add to the correct parent.
                    if (s == 0)
                    {
                        root.Folders.Add(folderNode);
                    }
                    else
                    {
                        var parentFolderNode = folderMap[previousPath];
                        parentFolderNode.Folders.Add(folderNode);
                    }
                }

                parentFolder = folderNode;
            }

            // The leaf goes into the last folder's Leaves list.
            // currentPath is the full category path.
            var leafParent = folderMap[currentPath];
            leafParent.Leaves.Add(new Node
            {
                Name = name,
                IsLeaf = true,
                FilteredIndex = filteredIndex,
            });
        }

        // Sort folders at every level (OrdinalIgnoreCase).
        SortFoldersRecursive(root);

        if (foldSingleChildFolders)
            FoldRecursive(root);

        return root;
    }

    /// <summary>Separator shown between the segments of a folded folder row ("Platform › Land").</summary>
    public const string FoldSeparator = " › ";

    /// <summary>CE-1017: a folder whose only content is one sub-folder merges with it into ONE row
    /// (e.g. "Platform › Land › Russia" when nothing else sits on that chain), so a sparse catalog is
    /// not a ladder of one-item folders. The merged row keeps the DEEPEST folder's
    /// <see cref="Node.FullPath"/>, so reveal and open-state keys still name a real category.</summary>
    private static void FoldRecursive(Node node)
    {
        for (int i = 0; i < node.Folders.Count; i++)
        {
            var folder = node.Folders[i];
            while (folder.Leaves.Count == 0 && folder.Folders.Count == 1)
            {
                var only = folder.Folders[0];
                only.Name = folder.Name + FoldSeparator + only.Name;
                folder = only;
            }
            node.Folders[i] = folder;
            FoldRecursive(folder);
        }
    }

    private static void SortFoldersRecursive(Node node)
    {
        node.Folders.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        foreach (var folder in node.Folders)
            SortFoldersRecursive(folder);
    }
}
