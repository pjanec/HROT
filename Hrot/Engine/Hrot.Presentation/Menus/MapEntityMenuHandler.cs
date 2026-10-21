using System;
using System.Collections.Generic;
using System.Text.Json;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Presentation.Abstractions;
using Hrot.Common.Events;
using Hrot.ScenarioEditor.Map;

namespace Hrot.UI.Common.Menus;

/// <summary>
/// ⭐⭐ CE-1134 (docs/designs/gizmos-1/canvas-context-menu-design.md §9) — the ENTITY INSPECTOR's right-click menu IS the map's entity
/// menu: the same JSON the map binds for that entity (<see cref="Hrot.Common.Diagnostics.Gizmos.ContextMenuProjectorGizmo.MenuJsonFor"/>),
/// and a click goes the map's way — a
/// <see cref="GlobalActionRequestedEvent"/> on the map's interaction bus, run by the map's own action registry. The inspector's own items
/// (Inspect…, Rename…) follow it, from their own handlers. 🔒 User, 2026-10-16: <i>"it should get the menu that is usually drawn on 2D
/// map and on 3D map as well, and just extend it with the items that the component inspector itself needs … especially useful if we want
/// to center the map, either 2D or 3D, on a selected entity without looking for it on the map"</i>.
/// <para>An item whose action this node does not run (no handler in the map's registry) is left out — the inspector offers only what
/// works here. ⭐ Replaces the Editor's <c>JsonEntityContextMenuHandler</c>, which read only the IG's <c>ContextMenuState</c> (written on
/// the IG alone) and so added nothing on the other hosts.</para>
/// </summary>
public sealed class MapEntityMenuHandler : IEntityContextMenuHandler
{
    private readonly Func<object?> _inspectorState;
    private readonly Func<MapInteraction?>? _map;

    /// <param name="inspectorState">The host's inspector state — the key that finds its map (<see cref="InspectorMapLink"/>).</param>
    public MapEntityMenuHandler(Func<object?> inspectorState)
        => _inspectorState = inspectorState ?? throw new ArgumentNullException(nameof(inspectorState));

    /// <summary>For a caller that holds its map (tests).</summary>
    public MapEntityMenuHandler(Func<MapInteraction?> map)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
        _inspectorState = static () => null;
    }

    /// <summary>The map this inspector belongs to, or null.</summary>
    public MapInteraction? Map => _map != null ? _map() : InspectorMapLink.MapFor(_inspectorState());

    /// <inheritdoc/>
    public void PopulateMenu(Entity entity, IContextMenuBuilder builder)
    {
        if (Map is not { } map) return;
        var world = map.World;
        string? json = MenuJsonOf(world, entity);
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return;
            AddAll(builder, doc.RootElement, entity, map);
        }
        catch (JsonException) { }   // a malformed menu adds nothing
    }

    /// <inheritdoc/>
    public void PopulateMenu(IReadOnlyCollection<Entity> entities, IContextMenuBuilder builder) { }

    /// <summary>⭐ <c>CE-1230</c> (canvas context menu §11 OM-1, R-1019) — the map's menu for the unit with network id
    /// <paramref name="networkId"/> (the ORBAT tree's row id). False when this host's map does not know it.</summary>
    public bool PopulateMenuFor(long networkId, IContextMenuBuilder builder)
    {
        if (Map is not { } map || networkId <= 0) return false;
        var world = map.World;
        if (!world.HasSingletonManaged<Fdp.Toolkit.Replication.Services.NetworkEntityMap>()
            || !world.GetSingletonManaged<Fdp.Toolkit.Replication.Services.NetworkEntityMap>()!.TryGetEntity(networkId, out var entity)) return false;
        PopulateMenu(entity, builder);
        return true;
    }

    /// <summary>The map's menu for <paramref name="entity"/> on this node — what this node's map binds for it
    /// (<see cref="Hrot.Common.Diagnostics.Gizmos.ContextMenuProjectorGizmo.MenuJsonFor"/>). Null for an entity the map gives no menu
    /// (dead, or no network id).</summary>
    public static string? MenuJsonOf(EntityRepository world, Entity entity)
    {
        if (entity.IsNull || !world.IsAlive(entity)) return null;
        if (Fdp.Toolkit.Replication.Services.NetworkIdResolver.RuntimeNetworkIdOf(world, entity) <= 0) return null;   // BP-508 — the ONE resolver
        return Hrot.Common.Diagnostics.Gizmos.ContextMenuProjectorGizmo.MenuJsonFor(world, entity);
    }

    private static void AddAll(IContextMenuBuilder builder, JsonElement items, Entity entity, MapInteraction map)
    {
        bool any = false, separatorPending = false;
        foreach (var e in items.EnumerateArray())
        {
            if (e.TryGetProperty("separator", out var sep) && sep.ValueKind == JsonValueKind.True)
            {
                separatorPending = any;   // never a leading one, never two in a row
                continue;
            }
            if (e.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
            {
                if (!AnyRuns(children, map)) continue;
                if (separatorPending) { builder.AddSeparator(); separatorPending = false; }
                var sub = builder.BeginSubmenu(Label(e));
                AddAll(sub, children, entity, map);
                sub.EndSubmenu();
                any = true;
                continue;
            }
            if (!e.TryGetProperty("id", out var idProp) || !idProp.TryGetInt32(out int actionId) || actionId == 0) continue;
            if (!map.Actions.TryGetHandler(actionId, out _)) continue;   // not run on this node
            if (separatorPending) { builder.AddSeparator(); separatorPending = false; }
            bool enabled = !e.TryGetProperty("enabled", out var en) || en.ValueKind != JsonValueKind.False;
            var bus = map.InteractionBus;
            int id = actionId;
            builder.AddItem(Label(e), () => bus.Publish(new GlobalActionRequestedEvent { ActionId = id, Target = entity }), enabled);
            any = true;
        }
    }

    private static bool AnyRuns(JsonElement items, MapInteraction map)
    {
        foreach (var e in items.EnumerateArray())
        {
            if (e.TryGetProperty("children", out var c) && c.ValueKind == JsonValueKind.Array && AnyRuns(c, map)) return true;
            if (e.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out int id) && id != 0 && map.Actions.TryGetHandler(id, out _)) return true;
        }
        return false;
    }

    private static string Label(JsonElement e) => e.TryGetProperty("label", out var l) ? l.GetString() ?? string.Empty : string.Empty;
}
