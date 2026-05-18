namespace Fdp.Presentation.WindowManager;

/// <summary>
/// A single node in the global menu trie. Represents one path segment.
/// Leaf nodes have an action (<see cref="OnClick"/> or <see cref="GetCheckedState"/>/<see cref="OnCheckedChanged"/>)
/// or are separators (<see cref="IsSeparator"/>). Intermediate nodes have only <see cref="Children"/>.
/// </summary>
public class MenuItemNode
{
    /// <summary>The display name for this menu segment.</summary>
    public string Name { get; set; } = "";

    /// <summary>Invoked when a plain menu item is selected. Null for non-leaf nodes.</summary>
    public Action? OnClick { get; set; }

    /// <summary>Returns the current checked state for a checkable menu item.</summary>
    public Func<bool>? GetCheckedState { get; set; }

    /// <summary>Invoked with the new checked value when a checkable item is selected.</summary>
    public Action<bool>? OnCheckedChanged { get; set; }

    /// <summary>When <c>true</c>, this node renders as a visual separator line.</summary>
    public bool IsSeparator { get; set; }

    /// <summary>Child nodes keyed by path segment name.</summary>
    public Dictionary<string, MenuItemNode> Children { get; } = new();
}

/// <summary>
/// Trie-based registry for global application menu items.
/// Paths use <c>'/'</c> as separator (e.g. <c>"Tools/Radar/Show"</c>).
/// </summary>
public class GlobalMenuRegistry
{
    /// <summary>The trie root. Its <see cref="MenuItemNode.Children"/> are the top-level menu entries.</summary>
    public MenuItemNode Root { get; } = new() { Name = "<root>" };

    /// <summary>
    /// Registers a plain menu item at the given path.
    /// Intermediate nodes are created as needed. Last-write-wins on re-registration.
    /// </summary>
    /// <param name="path">Slash-separated path (e.g. <c>"File/Open"</c>). Must not be empty.</param>
    /// <param name="onClick">Action to invoke when the item is selected.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null or empty.</exception>
    public void RegisterItem(string path, Action onClick)
    {
        var node = TraversePath(path);
        node.OnClick = onClick;
        node.GetCheckedState = null;
        node.OnCheckedChanged = null;
        node.IsSeparator = false;
    }

    /// <summary>
    /// Registers a checkable menu item at the given path.
    /// Last-write-wins on re-registration.
    /// </summary>
    /// <param name="path">Slash-separated path. Must not be empty.</param>
    /// <param name="getChecked">Returns the current checked state.</param>
    /// <param name="onChanged">Invoked with the new checked value when selected.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null or empty.</exception>
    public void RegisterCheckableItem(string path, Func<bool> getChecked, Action<bool> onChanged)
    {
        var node = TraversePath(path);
        node.OnClick = null;
        node.GetCheckedState = getChecked;
        node.OnCheckedChanged = onChanged;
        node.IsSeparator = false;
    }

    /// <summary>
    /// Registers a separator at the given path.
    /// Last-write-wins on re-registration.
    /// </summary>
    /// <param name="path">Slash-separated path. Must not be empty.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null or empty.</exception>
    public void RegisterSeparator(string path)
    {
        var node = TraversePath(path);
        node.OnClick = null;
        node.GetCheckedState = null;
        node.OnCheckedChanged = null;
        node.IsSeparator = true;
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Traverses (and creates) trie nodes for <paramref name="path"/>.
    /// Returns the leaf node. Sets <see cref="MenuItemNode.Name"/> on each created node.
    /// </summary>
    private MenuItemNode TraversePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("Menu item path must not be empty.", nameof(path));

        var segments = path.Split('/');
        var current = Root;

        foreach (var segment in segments)
        {
            // Skip empty segments (e.g. trailing slash produces an empty last segment).
            if (segment.Length == 0) continue;

            if (!current.Children.TryGetValue(segment, out var child))
            {
                child = new MenuItemNode { Name = segment };
                current.Children[segment] = child;
            }

            current = child;
        }

        return current;
    }
}
