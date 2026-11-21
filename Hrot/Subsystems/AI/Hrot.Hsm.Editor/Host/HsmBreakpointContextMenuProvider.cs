using System;
using System.Collections.Generic;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Hsm.Editor.Debug;
using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using FdpBuilder = Fdp.Presentation.Abstractions.IContextMenuBuilder;

namespace Hrot.Hsm.Editor.Host;

/// <summary>
/// Provides right-click context menu items for HSM breakpoint gutter elements.
/// Matched by RendererId to <see cref="Renderers.HsmBreakpointGutterRenderer.Id"/>.
/// </summary>
internal sealed class HsmBreakpointContextMenuProvider : ICustomElementContextMenuProvider
{
    private readonly IDataBreakpointManager _manager;

    public HsmBreakpointContextMenuProvider(IDataBreakpointManager manager)
        => _manager = manager;

    /// <summary>⭐ CE-1323 — the document's asset (set when the canvas is built): the dot's state is looked up in it. 🔴 It used a stub
    /// state with FlatIndex 0, so "Break on Enter" from the dot watched the ROOT state of every HSM.</summary>
    public HsmAsset? Asset { get; set; }

    public string RendererId => "hsm.breakpoint_gutter";

    public IReadOnlyList<ContextMenuItem> GetItemsFor(string elementKey, CustomElementHit hit)
    {
        // elementKey is the state StableId string encoded by the gutter renderer.
        if (Asset is null || !Guid.TryParse(elementKey, out var stableId) || Asset.FindStateByStableId(stableId) is not { } state)
            return Array.Empty<ContextMenuItem>();
        var collector = new HsmContextMenuItemCollector();
        HsmBreakpointMenuPopulator.PopulateStateMenu(Asset, state, collector, _manager);
        return collector.Items;
    }
}

/// <summary>⭐ CE-1323 (DBG-14) — a transition LINE's right-click: break when it fires.</summary>
internal sealed class HsmLinkContextMenuProvider : ILinkContextMenuProvider
{
    private readonly HsmAsset _asset;
    private readonly IDataBreakpointManager _manager;

    public HsmLinkContextMenuProvider(HsmAsset asset, IDataBreakpointManager manager) { _asset = asset; _manager = manager; }

    public IReadOnlyList<ContextMenuItem> GetItemsFor(LinkId link)
    {
        if (_asset.FindTransitionByVisualId(link.Value) is not { } t) return Array.Empty<ContextMenuItem>();
        var collector = new HsmContextMenuItemCollector();
        HsmBreakpointMenuPopulator.PopulateTransitionMenu(_asset, t, collector, _manager);
        return collector.Items;
    }
}

/// <summary>
/// Implements <see cref="FdpBuilder"/> by collecting items as
/// <see cref="ContextMenuItem"/> records for the NodeEditor context-menu system.
/// </summary>
internal sealed class HsmContextMenuItemCollector : FdpBuilder
{
    private readonly List<ContextMenuItem> _items = new();
    private readonly HsmContextMenuItemCollector? _parent;
    private readonly string _label = "";
    public IReadOnlyList<ContextMenuItem> Items => _items;

    public HsmContextMenuItemCollector() { }
    private HsmContextMenuItemCollector(HsmContextMenuItemCollector parent, string label) { _parent = parent; _label = label; }

    public void AddItem(string label, Action callback, bool enabled = true)
        => _items.Add(new ContextMenuItem(label, callback, enabled));

    /// <summary>⭐ CE-1323 — a real submenu (it flattened everything into one level).</summary>
    public FdpBuilder BeginSubmenu(string label) => new HsmContextMenuItemCollector(this, label);

    public void EndSubmenu()
    {
        if (_parent != null && _items.Count > 0) _parent._items.Add(new ContextMenuItem(_label, () => { }, true, _items.ToArray()));
    }

    public void AddSeparator() { }
}
