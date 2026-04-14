using Fdp.Presentation.Abstractions;
using Hrot.ExCon.Logic;

namespace Hrot.ExCon.Adapters;

/// <summary>
/// Implements <see cref="IContextMenuBuilder"/> by collecting <see cref="ContextMenuItem"/>
/// DTOs and the corresponding action callbacks for later serialisation and dispatch.
///
/// <para>
/// Each <see cref="AddItem"/> call assigns a monotonically increasing integer ID to the
/// item and stores the <c>Action</c> callback in an internal dictionary keyed by that ID.
/// When the IG echoes back a <c>ContextActionInvoked</c> message the host can look up and
/// invoke the matching callback via <see cref="GetCallbackRegistry"/>.
/// </para>
///
/// <para>Not thread-safe; instances should be created once per right-click event,
/// used immediately, and then discarded.</para>
/// </summary>
public sealed class JsonContextMenuBuilder : IContextMenuBuilder
{
    private int _nextId;
    private readonly List<ContextMenuItem>    _items     = new();
    private readonly Dictionary<int, Action>  _callbacks = new();

    // ── IContextMenuBuilder ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void AddItem(string label, Action callback, bool enabled = true)
    {
        int id = _nextId++;
        _callbacks[id] = callback;
        _items.Add(new ContextMenuItem { Id = id, Label = label, Enabled = enabled });
    }

    /// <inheritdoc/>
    /// <remarks>Returns <c>this</c> — sub-menus are flattened into the main list.</remarks>
    public IContextMenuBuilder BeginSubmenu(string label) => this;

    /// <inheritdoc/>
    public void EndSubmenu() { }

    /// <inheritdoc/>
    public void AddSeparator()
        => _items.Add(new ContextMenuItem { IsSeparator = true });

    // ── Result accessors ───────────────────────────────────────────────────────

    /// <summary>Returns the ordered list of menu items ready for JSON serialisation.</summary>
    public IReadOnlyList<ContextMenuItem> Build() => _items;

    /// <summary>
    /// Returns the mapping from item IDs to their corresponding callbacks.
    /// Store the returned dictionary between the <see cref="Build"/> call and any
    /// incoming <c>ContextActionInvoked</c> response.
    /// </summary>
    public IReadOnlyDictionary<int, Action> GetCallbackRegistry() => _callbacks;
}
