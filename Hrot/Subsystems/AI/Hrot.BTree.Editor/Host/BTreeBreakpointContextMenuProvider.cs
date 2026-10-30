using System;
using System.Collections.Generic;
using Hrot.BTree.Editor.Debug;
using Hrot.BTree.Editor.Model;
using Hrot.Diagnostics.Breakpoints;
using NodeEditor.Core.Interfaces;
using FdpBuilder = Fdp.Presentation.Abstractions.IContextMenuBuilder;

namespace Hrot.BTree.Editor.Host;

/// <summary>
/// Provides right-click context menu items for BTree breakpoint gutter elements.
/// Matched by RendererId to <see cref="Renderers.BTreeBreakpointGutterRenderer.Id"/>.
/// </summary>
internal sealed class BTreeBreakpointContextMenuProvider : ICustomElementContextMenuProvider
{
    private readonly IDataBreakpointManager _manager;

    public BTreeBreakpointContextMenuProvider(IDataBreakpointManager manager)
        => _manager = manager;

    /// <summary>⭐ CE-1324 — the document's asset (set when the node menu is wired): the dot's node is looked up in it. 🔴 It used a stub node with blob index 0, so every dot watched node 0.</summary>
    public BehaviorTreeAsset? Asset { get; set; }

    public string RendererId => "btree.breakpoint_gutter";

    public IReadOnlyList<ContextMenuItem> GetItemsFor(string elementKey, CustomElementHit hit)
    {
        // elementKey is the node VisualId string encoded by the gutter renderer.
        if (Asset is null || !Guid.TryParse(elementKey, out var id) || Asset.FindNode(id) is not { } node)
            return Array.Empty<ContextMenuItem>();
        var collector = new ContextMenuItemCollector();
        BTreeBreakpointMenuPopulator.PopulateMenu(Asset, node, collector, _manager);
        return collector.Items;
    }
}

/// <summary>
/// Implements <see cref="FdpBuilder"/> by collecting items as
/// <see cref="ContextMenuItem"/> records for the NodeEditor context-menu system.
/// </summary>
internal sealed class ContextMenuItemCollector : FdpBuilder
{
    private readonly List<ContextMenuItem> _items = new();
    public IReadOnlyList<ContextMenuItem> Items => _items;

    public void AddItem(string label, Action callback, bool enabled = true)
        => _items.Add(new ContextMenuItem(label, callback, enabled));

    public FdpBuilder BeginSubmenu(string label) => this;
    public void EndSubmenu() { }
    public void AddSeparator() { }
}
