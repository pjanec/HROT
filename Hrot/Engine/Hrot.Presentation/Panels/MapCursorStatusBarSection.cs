using System;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Vis2D;
using Hrot.ScenarioEditor.Map;
using ImGuiNET;

namespace Hrot.UI.Common.Panels;

/// <summary>
/// ⭐ <c>CE-1289</c> (folds in <c>CE-3151</c>; docs/DESIGN_Map_3D_Mode.md §6ac) — the status-bar section that shows the
/// point under the map cursor: the local flat coordinates and, beside them, latitude / longitude / altitude. The same section on the
/// 2-D and the 3-D map, on every host that has a map.
///
/// <para>🔒 User, 2026-10-23: <i>"in status bar we should see the coordinates of point under cursor (2d map as well 3d map) - both flat
/// coordinates, lat/long coordinates with altitude"</i>; and backend's <c>CE-3151</c>, 2026-10-09: <i>"I would like to see map cursor
/// coordinates (the local flat ones) in the status bar when cursor moves over map area."</i></para>
///
/// <para>It reads the <see cref="MapCursorReadout"/> the shared map attach puts on the canvas
/// (<c>MapInteractionPack.AttachMapLayers</c>), resolved per frame, so a host that replaces its canvas keeps the section.
/// Registered with <see cref="Register"/> — one call per host, in <c>RegisterWindows</c>.</para>
/// </summary>
public sealed class MapCursorStatusBarSection
{
    /// <summary>The status-bar section id (a host's duplicate registration replaces, never doubles).</summary>
    public const string SectionId = "map_cursor";

    /// <summary>Left of the message-log icon (90) and the time controls (100), right of "System OK" (0).</summary>
    public const int SortOrder = 50;

    private readonly Func<MapCanvas?> _canvas;

    public MapCursorStatusBarSection(Func<MapCanvas?> canvas) => _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));

    /// <summary>Registers the section on <paramref name="bar"/> for <paramref name="perspective"/> (the perspective that shows the map).</summary>
    public static MapCursorStatusBarSection Register(StatusBarManager bar, Func<MapCanvas?> canvas, string perspective)
    {
        var section = new MapCursorStatusBarSection(canvas);
        bar.RegisterSection(SectionId, SortOrder, section.Render, perspective);
        return section;
    }

    /// <summary>What the section would show now: the readout's text, or the placeholder before the cursor has been over the map.</summary>
    public string TextNow()
    {
        var text = _canvas()?.Get<MapCursorReadout>()?.Text;
        return string.IsNullOrEmpty(text) ? "Cursor: -" : text!;
    }

    /// <summary>Called each frame by the <see cref="StatusBarManager"/>, inside an active ImGui frame and the status-bar window.</summary>
    public void Render() => ImGui.TextUnformatted(TextNow());
}
