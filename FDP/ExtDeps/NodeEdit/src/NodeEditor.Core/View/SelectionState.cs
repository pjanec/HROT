using System.Collections.Generic;
using System.Linq;
using NodeEditor.Primitives;

namespace NodeEditor.Core.View;

/// <summary>
/// Mutable selection of editor elements. Equality is by identity, not order.
/// Methods follow the click-modifier semantics in the canvas spec:
/// no modifier → replace; Shift → add; Ctrl → toggle.
/// </summary>
public sealed class SelectionState
{
    private readonly HashSet<SelectionEntry> _items = new();

    public IReadOnlyCollection<SelectionEntry> Items => _items;
    public int Count => _items.Count;
    public bool IsEmpty => _items.Count == 0;

    public bool Contains(SelectionEntry e) => _items.Contains(e);

    public IEnumerable<NodeId> Nodes =>
        _items.Where(e => e.Kind == SelectionEntryKind.Node).Select(e => e.Node);
    public IEnumerable<LinkId> Links =>
        _items.Where(e => e.Kind == SelectionEntryKind.Link).Select(e => e.Link);
    public IEnumerable<CommentId> Comments =>
        _items.Where(e => e.Kind == SelectionEntryKind.Comment).Select(e => e.Comment);
    public IEnumerable<RerouteRef> Reroutes =>
        _items.Where(e => e.Kind == SelectionEntryKind.Reroute).Select(e => e.Reroute);

    public IEnumerable<AttachmentId> Attachments =>
        _items.Where(e => e.Kind == SelectionEntryKind.Attachment).Select(e => e.Attachment);

    /// <summary>Replace the selection with exactly one entry.</summary>
    public void ReplaceWith(SelectionEntry entry)
    {
        _items.Clear();
        _items.Add(entry);
    }

    /// <summary>Replace the selection with a set of entries.</summary>
    public void ReplaceWith(IEnumerable<SelectionEntry> entries)
    {
        _items.Clear();
        foreach (var e in entries) _items.Add(e);
    }

    /// <summary>Add an entry (Shift+click semantics).</summary>
    public void Add(SelectionEntry entry) => _items.Add(entry);

    /// <summary>Add many entries.</summary>
    public void AddRange(IEnumerable<SelectionEntry> entries)
    {
        foreach (var e in entries) _items.Add(e);
    }

    /// <summary>Toggle an entry (Ctrl+click semantics).</summary>
    public void Toggle(SelectionEntry entry)
    {
        if (!_items.Add(entry)) _items.Remove(entry);
    }

    /// <summary>Remove an entry if present.</summary>
    public void Remove(SelectionEntry entry) => _items.Remove(entry);

    /// <summary>Clear the selection.</summary>
    public void Clear() => _items.Clear();
}
