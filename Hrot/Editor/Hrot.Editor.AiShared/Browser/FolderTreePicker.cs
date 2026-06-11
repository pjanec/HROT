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

/// <summary>
/// Pure in-memory folder picker state for the pick mode (§18.1).
/// Tracks selected folder, allows adding new folders, and enforces
/// root-bounding (no <c>..</c> escape, no absolute paths).
/// Logic is fully separated from ImGui draw calls.
/// </summary>
public sealed class FolderPickerState
{
    private readonly HashSet<string> _folderPaths;
    private string _selectedRelPath;

    /// <summary>
    /// Creates a picker state from a set of known relative folder paths
    /// (using <c>/</c> separators, e.g. <c>"combat"</c>, <c>"combat/patrol"</c>).
    /// The root (<c>""</c>) is always included implicitly.
    /// </summary>
    public FolderPickerState(IEnumerable<string>? knownFolderPaths)
    {
        _folderPaths = new HashSet<string>(StringComparer.Ordinal);
        _selectedRelPath = "";

        if (knownFolderPaths != null)
        {
            foreach (var p in knownFolderPaths)
            {
                var sanitized = SanitizeRelPath(p);
                if (sanitized != null)
                    _folderPaths.Add(sanitized);
            }
        }
    }

    /// <summary>
    /// The currently selected folder path relative to the root
    /// (using <c>/</c> separators, <c>""</c> = root). Never <see langword="null"/>.
    /// </summary>
    public string SelectedRelPath
    {
        get => _selectedRelPath;
        set
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            // Only accept known folder paths or the root.
            if (value != "" && !_folderPaths.Contains(value))
                throw new ArgumentException(
                    $"Folder '{value}' is not in the known folder set.", nameof(value));
            _selectedRelPath = value;
        }
    }

    /// <summary>
    /// All known folder paths (including the root <c>""</c>).
    /// </summary>
    public IReadOnlyCollection<string> FolderPaths
    {
        get
        {
            var all = new List<string> { "" };
            all.AddRange(_folderPaths);
            all.Sort(StringComparer.Ordinal);
            return all.AsReadOnly();
        }
    }

    /// <summary>
    /// Adds a new folder under <paramref name="parentRelPath"/> and returns its
    /// relative path. The folder name is sanitized and the path is validated
    /// against root-escape rules.
    /// </summary>
    /// <param name="parentRelPath">
    /// The parent folder's relative path (<c>""</c> for root, or e.g. <c>"combat"</c>).
    /// </param>
    /// <param name="name">The new folder segment name (e.g. <c>"patrol"</c>).</param>
    /// <returns>The new folder's relative path (e.g. <c>"combat/patrol"</c>).</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the name is invalid (empty, contains <c>..</c>, or produces an
    /// escaped path) or the parent is not a known folder.
    /// </exception>
    public string AddFolder(string parentRelPath, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Folder name must not be empty.", nameof(name));

        // Validate that the parent is a known folder (or root).
        if (parentRelPath != "" && !_folderPaths.Contains(parentRelPath))
            throw new ArgumentException(
                $"Parent folder '{parentRelPath}' is not a known folder.", nameof(parentRelPath));

        // Sanitize the folder name: reject escape attempts.
        var sanitizedName = SanitizeFolderName(name);
        if (sanitizedName == null)
            throw new ArgumentException(
                $"Folder name '{name}' is not valid (must not contain '..', " +
                "must not start with '/', and must not be an absolute path).", nameof(name));

        // Build the full relative path.
        var newRelPath = string.IsNullOrEmpty(parentRelPath)
            ? sanitizedName
            : parentRelPath + "/" + sanitizedName;

        // Final root-bounding check: the result must not escape.
        if (!IsBounded(newRelPath))
            throw new ArgumentException(
                $"Resulting path '{newRelPath}' escapes the root.", nameof(name));

        _folderPaths.Add(newRelPath);
        _selectedRelPath = newRelPath;
        return newRelPath;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="relPath"/> is a known folder.
    /// </summary>
    public bool ContainsFolder(string relPath)
        => relPath == "" || _folderPaths.Contains(relPath);

    // ── Sanitization helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Validates a folder name segment. Returns the sanitized name, or
    /// <see langword="null"/> when the name is not safe.
    /// </summary>
    internal static string? SanitizeFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var trimmed = name.Trim();

        // Reject: .. (parent traversal)
        if (trimmed == ".." || trimmed.Contains(".."))
            return null;

        // Reject: absolute paths (starts with /, \, or drive letter)
        if (trimmed.StartsWith('/') || trimmed.StartsWith('\\'))
            return null;

        if (trimmed.Length >= 2 && trimmed[1] == ':')
            return null; // drive-letter pattern (e.g. "C:")

        // Reject: any path separator in the segment name
        if (trimmed.Contains('/') || trimmed.Contains('\\'))
            return null;

        return trimmed;
    }

    /// <summary>
    /// Validates a relative path against root-escape rules.
    /// Returns <see langword="null"/> when unsafe.
    /// </summary>
    internal static string? SanitizeRelPath(string relPath)
    {
        if (string.IsNullOrWhiteSpace(relPath))
            return "";

        var trimmed = relPath.Trim();

        // Reject: absolute paths (explicit check for both Windows and Unix separators
        // at position 0, plus drive-letter patterns; Path.IsPathRooted varies by platform).
        if (IsAbsolutePath(trimmed))
            return null;

        // Reject: .. traversal anywhere in the path
        var segments = trimmed.Split('/');
        foreach (var seg in segments)
        {
            if (seg == ".." || seg.Contains(".."))
                return null;
            if (seg.Contains('\\'))
                return null;
        }

        // Normalize: remove leading/trailing slashes, collapse double slashes.
        var normalized = trimmed.Trim('/');
        while (normalized.Contains("//"))
            normalized = normalized.Replace("//", "/");

        return normalized.Length == 0 ? "" : normalized;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the path looks like an absolute path
    /// (starts with <c>/</c>, <c>\</c>, or has a drive letter like <c>C:</c>).
    /// </summary>
    private static bool IsAbsolutePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        // Drive-letter pattern (e.g. "C:" or "C:\")
        if (path.Length >= 2 && path[1] == ':')
            return true;

        // Starts with separator (both / and \)
        if (path[0] == '/' || path[0] == '\\')
            return true;

        // Also check via Path.IsPathRooted as a fallback for platform-specific rules
        if (Path.IsPathRooted(path))
            return true;

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the path stays within the root
    /// (no <c>..</c>, no absolute components, no drive letters).
    /// </summary>
    internal static bool IsBounded(string relPath)
    {
        if (string.IsNullOrEmpty(relPath))
            return true;

        if (Path.IsPathRooted(relPath))
            return false;

        var segments = relPath.Split('/');
        foreach (var seg in segments)
        {
            if (seg == ".." || seg.Contains(".."))
                return false;
            if (seg.Contains('\\'))
                return false;
            if (seg.Length >= 2 && seg[1] == ':')
                return false;
        }

        return true;
    }
}
