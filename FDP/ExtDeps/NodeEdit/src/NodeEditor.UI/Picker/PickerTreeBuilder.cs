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
    public static Node Build(IReadOnlyList<(int FilteredIndex, string? Category, string Name)> items)
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

        return root;
    }

    private static void SortFoldersRecursive(Node node)
    {
        node.Folders.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        foreach (var folder in node.Folders)
            SortFoldersRecursive(folder);
    }
}
