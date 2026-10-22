using System;
using System.Collections.Generic;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.WindowManager;
using Hrot.ScenarioEditor.Map;

namespace Hrot.UI.Common.Menus;

/// <summary>
/// ⭐ CE-1134 / CE-1135 (docs/designs/gizmos-1/canvas-context-menu-design.md §9) — joins a host's MAP (<see cref="MapInteraction"/>, built
/// by <see cref="MapInteractionPack.Build"/>) to its ENTITY INSPECTOR window (registered by <c>DiagnosticsWindowsBundle</c>), so the
/// inspector's right-click menu is the map's entity menu, and the inspector finds the map's request bus (⭐ BP-515 DS-7: "Properties…"
/// is a request the inspector ANSWERS — the window list that let the map call the inspector is gone; CE-1150 retires the rest).
/// <para>⭐ The key is the INSPECTOR STATE both are already given by every host — the pack's <c>MapInteractionContext.Inspector</c>
/// and the bundle's <c>DiagnosticsHostServices.InspectorState</c> return the same object (measured on all four hosts, 2026-10-16).
/// ⛔ Not a constructor argument: three of the four hosts do not keep their window manager, and the pack and the bundle are built
/// at different points of a host's start-up, so neither can hand the other a reference.</para>
/// <para>Weak: a host that rebuilds its map on a scenario reload leaves the old one to the collector; the newest live link wins.</para>
/// </summary>
public static class InspectorMapLink
{
    private static readonly object Gate = new();
    private static readonly List<WeakReference<MapInteraction>> Maps = new();

    /// <summary>The pack registers each map it builds.</summary>
    public static void RegisterMap(MapInteraction map)
    {
        lock (Gate) { Prune(); Maps.Add(new WeakReference<MapInteraction>(map)); }
    }

    /// <summary>The newest live map whose inspector is <paramref name="state"/>, or null.</summary>
    public static MapInteraction? MapFor(object? state)
    {
        if (state is null) return null;
        lock (Gate)
        {
            for (int i = Maps.Count - 1; i >= 0; i--)
                if (Maps[i].TryGetTarget(out var map) && map.Inspector?.Invoke() is { } s && ReferenceEquals(s, state)) return map;
        }
        return null;
    }

    /// <summary>Forget everything (tests).</summary>
    public static void Reset()
    {
        lock (Gate) Maps.Clear();
    }

    private static void Prune()
    {
        Maps.RemoveAll(w => !w.TryGetTarget(out _));
    }
}
