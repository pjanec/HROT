using System;
using System.Collections.Generic;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Hsm.Editor.Debug;
using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.Interfaces;
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

    public string RendererId => "hsm.breakpoint_gutter";

    public IReadOnlyList<ContextMenuItem> GetItemsFor(string elementKey, CustomElementHit hit)
    {
        // elementKey is the state StableId string encoded by the gutter renderer.
        Guid.TryParse(elementKey, out var stableId);
        var stubState = new StateNode(elementKey) { StableId = stableId, FlatIndex = 0 };

        var collector = new HsmContextMenuItemCollector();
        HsmBreakpointMenuPopulator.PopulateStateMenu(stubState, collector, _manager);
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
    public IReadOnlyList<ContextMenuItem> Items => _items;

    public void AddItem(string label, Action callback, bool enabled = true)
        => _items.Add(new ContextMenuItem(label, callback, enabled));

    public FdpBuilder BeginSubmenu(string label) => this;
    public void EndSubmenu() { }
    public void AddSeparator() { }
}
