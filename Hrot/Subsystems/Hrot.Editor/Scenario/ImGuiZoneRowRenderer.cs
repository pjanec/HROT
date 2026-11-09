using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Panels;
using Hrot.Editor.AiShared.Shell;
using Hrot.Presentation.Zones;
using Hrot.UI.Common.Weather;
using ImGuiNET;
using NodeEditor.Core.Widgets;

namespace Hrot.Editor.Scenario;

/// <summary>⭐ CE-1161 (CE-3217) — the ImGui rows of the Zones view (<see cref="ZonesDetailsView"/> has stayed ImGui-free).</summary>
public sealed class ImGuiZoneRowRenderer : IZoneRowRenderer
{
    public void Header(string idScope, IReadOnlyList<ZoneStatusRow> rows, bool anyStale, Action loadAllStale)
    {
        if (rows.Count == 0) { ImGui.TextDisabled("No terrain zones. Draw one with Add Entity ▸ Map Graphics ▸ Terrain Zone."); return; }
        ImGui.TextUnformatted($"Terrain zones ({rows.Count})");
        if (anyStale) { ImGui.SameLine(); if (ImGui.SmallButton($"Load all stale##zones{idScope}")) loadAllStale(); }
    }

    public void Row(string idScope, ZoneStatusRow row, Action load)
    {
        ImGui.TextUnformatted($"Zone {row.ZoneId}");
        ImGui.SameLine();
        ImGui.TextDisabled(row.State.ToString());
        if (row.State is ZoneLoadState.Loaded or ZoneLoadState.Loading) return;
        ImGui.SameLine();
        if (ImGui.SmallButton($"Load##zone{row.ZoneId}{idScope}")) load();
    }
}
