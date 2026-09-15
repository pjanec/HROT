using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Resilience;
using ImGuiNET;
using ImGuiApi = ImGuiNET.ImGui;

namespace Fdp.Presentation.Panels;

/// <summary>⭐ One module row, projected for the dump.</summary>
public sealed record SystemProfilerRowViewModel(string ModuleName, int ExecutionCount, int FailureCount, bool IsHealthy);

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — the whole of what <see cref="SystemProfilerPanel"/> shows, this frame.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example.
///
/// <para>⚠⚠ <b>No production host exists.</b> 📐 Measured — <c>SystemProfilerPanel</c> has ZERO callers
/// anywhere in the tree (only listed as a "standalone panel" in <c>docs/designs/win-mgr-1/DESIGN.md</c>,
/// predating the window-manager unification). ⇒ per the queue's caller-registers rule there is no host
/// to call <c>DeclareInstrumented</c>/<c>Register</c> from — <see cref="SystemProfilerPanel.BuildViewModel"/>
/// exists so the projection is ready the moment a host is written, but this panel is NOT wired into
/// <c>PanelSnapshot</c> yet. Reported rather than silently skipped, per the sweep's own rule.</para>
/// </summary>
public sealed record SystemProfilerPanelViewModel(
    string PanelId,
    string PanelKind,
    IReadOnlyList<SystemProfilerRowViewModel> Rows) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

public static class SystemProfilerPanel
{
    /// <summary>⭐⭐⭐ BUILD — a pure projection of <paramref name="stats"/>. No ImGui. ⚠ Not wired to any
    /// host yet — see the view-model's own remarks.</summary>
    public static SystemProfilerPanelViewModel BuildViewModel(List<ModuleStats>? stats, string panelId, string panelKind)
    {
        var rows = (stats ?? new List<ModuleStats>())
            .Select(s => new SystemProfilerRowViewModel(
                s.ModuleName ?? "Unknown", s.ExecutionCount, s.FailureCount, s.CircuitState == CircuitState.Closed))
            .ToList();
        return new SystemProfilerPanelViewModel(panelId, panelKind, rows);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>DRAW — CONTENT ONLY, and RENDERED FROM THE VIEW-MODEL.</b>
    /// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> — the contract's central INVARIANT is that the
    /// draw renders <i>only</i> from the model it published. ⛔ No rail can enforce it (identical
    /// characters render either way), so it is stated here and checked in review.
    ///
    /// <para>⚠⚠ <b>No <c>Begin</c>/<c>End</c> here, DELIBERATELY.</b> 📐 <c>ManagedWindow.Render</c> calls
    /// <c>Gui.Begin</c> at :202 and <c>Gui.End</c> at :224 <b>around</b> <c>DrawClientArea()</c> (:221)
    /// ⇒ a panel that opens its own window inside that nests a SECOND ImGui window: the managed window
    /// renders empty and a stray floating one appears beside it. 📌 This is why every converted sibling
    /// is named <c>DrawContent</c> — see <c>ArchitectureDiagnosticsPanel</c>.</para>
    /// </summary>
    public static void DrawContent(SystemProfilerPanelViewModel vm)
    {
        if (!ImGuiApi.BeginTable("ProfilerTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
            return;

        ImGuiApi.TableSetupColumn("Module");
        ImGuiApi.TableSetupColumn("Frequency");
        ImGuiApi.TableSetupColumn("Failures");
        ImGuiApi.TableSetupColumn("Status");
        ImGuiApi.TableHeadersRow();

        foreach (var row in vm.Rows)
        {
            ImGuiApi.TableNextRow();

            ImGuiApi.TableSetColumnIndex(0);
            ImGuiApi.Text(row.ModuleName);

            ImGuiApi.TableSetColumnIndex(1);
            ImGuiApi.Text($"{row.ExecutionCount}");

            ImGuiApi.TableSetColumnIndex(2);
            ImGuiApi.Text($"{row.FailureCount}");

            ImGuiApi.TableSetColumnIndex(3);

            // Status Indicator
            Vector4 color = row.IsHealthy ? new Vector4(0, 1, 0, 1) : new Vector4(1, 0, 0, 1);
            ImGuiApi.TextColored(color, row.IsHealthy ? "OK" : "CRITICAL");
            ImGuiApi.SameLine();

            // Small circle indicator, vertically centred on the line
            var drawList  = ImGuiApi.GetWindowDrawList();
            var cursorPos = ImGuiApi.GetCursorScreenPos();
            const float radius = 5.0f;
            var center = new Vector2(cursorPos.X + radius, cursorPos.Y + ImGuiApi.GetTextLineHeight() * 0.5f);
            drawList.AddCircleFilled(center, radius, ImGuiApi.ColorConvertFloat4ToU32(color));
        }

        ImGuiApi.EndTable();
    }

    /// <summary>
    /// ⭐ The STANDALONE entry point — its own ImGui window around <see cref="DrawContent"/>.
    /// ⚠ Kept because it is this panel's original public surface and a host that is not a
    /// <c>ManagedWindow</c> may still want it; ⛔ it ROUTES to the one renderer rather than repeating it,
    /// so the two can never drift. 📌 <c>SystemProfilerWindow</c> does NOT use this — see its remarks.
    /// </summary>
    public static void Draw(List<ModuleStats> stats)
    {
        if (ImGuiApi.Begin("System Profiler"))
            DrawContent(BuildViewModel(stats, "system-profiler-standalone", PanelIds.SystemProfiler));
        ImGuiApi.End();
    }
}
