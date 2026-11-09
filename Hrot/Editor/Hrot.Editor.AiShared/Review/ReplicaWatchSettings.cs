using System;
using System.Collections.Generic;
using System.Globalization;
using Fdp.Core;
using Fdp.Presentation.Fonts;
using Fdp.Toolkit.ReplayBrowser.Federation;
using Fdp.Toolkit.Replication.Attributes;
using ImGuiNET;
using NodeEditor.Core.Widgets;

namespace Hrot.Editor.AiShared.Review;

/// <summary>
/// ⭐ Review §6.13 (RW-5; mockup <c>docs/UX/replica-watch-visual.svg</c> ③) — the lane's SETTINGS popup, opened from the lane's gear (<see cref="ReplicaWatchLane.OpenSettings"/>): <i>Watched on &lt;unit&gt;</i>, one row per watched item
/// (teal eye · label · the tolerance field in the attribute's unit, <i>exact</i> when 0 · ✕), the lane's threshold (200 ms, "(default)"), the note, and <i>+ Watch another attribute…</i> (a picker over the vocabulary attributes the unit has,
/// plus a component field by path — expert). It edits the host's ONE <see cref="ReplicaWatchList"/> — a change moves its <see cref="ReplicaWatchList.Version"/> and the lane rescans. ⭐ ONE class for both hosts.
/// </summary>
public sealed class ReplicaWatchSettings
{
    public const string PopupId = "Replica watch settings";

    private readonly ReplicaWatchLane _lane;
    private readonly Func<(EntityRepository Repo, Entity Unit)?> _unit;
    private readonly Func<string?> _unitName;
    private string _path = "";
    private readonly List<int> _subs = new();

    /// <param name="lane">The lane (its watch list and its gear request).</param>
    /// <param name="unit">The selected unit's entity in a world that holds it (the attribute picker lists what it has), or null.</param>
    /// <param name="unitName">The unit's name for the heading.</param>
    public ReplicaWatchSettings(ReplicaWatchLane lane, Func<(EntityRepository Repo, Entity Unit)?> unit, Func<string?> unitName)
    { _lane = lane; _unit = unit; _unitName = unitName; }

    /// <summary>Once a frame: opens the popup when the gear asked, draws it while open.</summary>
    public void Draw()
    {
        if (_lane.TakeSettingsRequest()) ImGui.OpenPopup(PopupId);
        if (!ImGui.BeginPopup(PopupId)) return;
        long unit = _lane.Unit;
        var watch = _lane.Watch;
        ImGui.TextColored(IconPalette.Muted, "Watched on " + (_unitName() ?? ("unit " + unit)));
        ImGui.SameLine(ImGui.GetContentRegionAvail().X > 380 ? 300f : 0f);
        ImGui.TextDisabled("differs when beyond");
        var items = watch.ItemsOf(unit);
        string? remove = null; int removeSub = 0;
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            ImGui.PushID(i);
            ImGui.TextColored(IconPalette.Teal, IconsFontAwesome6.Eye);
            ImGui.SameLine(); ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(it.Colour), it.Expert ? IconsFontAwesome6.Code : IconsFontAwesome6.Crosshairs);
            ImGui.SameLine(); ImGui.TextUnformatted(it.Label);
            ImGui.SameLine(300f);
            ImGui.SetNextItemWidth(110f);
            double tol = it.Tolerance;
            string unitText = UnitOf(it);
            if (ImGui.InputDouble("##tol", ref tol, 0, 0, tol == 0 ? "exact" : "%.3g" + (unitText.Length > 0 ? " " + unitText : ""), ImGuiInputTextFlags.EnterReturnsTrue))
                watch.SetTolerance(unit, it.Path, it.SubIndex, tol);
            ImGui.SameLine();
            if (ImGui.SmallButton(IconsFontAwesome6.Xmark)) { remove = it.Path; removeSub = it.SubIndex; }
            ImGui.PopID();
        }
        if (remove != null) watch.Remove(unit, remove, removeSub);
        ImGui.Separator();
        ImGui.TextUnformatted("Show a disagreement only when it lasts longer than");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80f);
        double ms = Math.Round(watch.ThresholdSeconds * 1000);
        if (ImGui.InputDouble("##thr", ref ms, 0, 0, "%.0f ms", ImGuiInputTextFlags.EnterReturnsTrue)) watch.ThresholdSeconds = ms / 1000d;
        if (Math.Abs(watch.ThresholdSeconds - ReplicaWatchList.DefaultThresholdSeconds) < 1e-9) { ImGui.SameLine(); ImGui.TextDisabled("(default)"); }
        ImGui.TextColored(IconPalette.Muted, "≈ 2 × the time-sync tolerance: normal replication catch-up stays hidden");
        ImGui.Separator();
        ImGui.TextColored(IconPalette.Green, IconsFontAwesome6.Plus);
        ImGui.SameLine();
        if (ImGui.BeginMenu("Watch another attribute…"))
        {
            if (_unit() is { } u)
                foreach (var def in AttributeVocabulary.All)
                {
                    _subs.Clear();
                    AttributeReaders.SubIndicesOf(u.Repo, u.Unit, def.AttributeId, _subs);
                    foreach (int sub in _subs)
                    {
                        string shown = def.JsonPath.EndsWith(".*", StringComparison.Ordinal) ? def.JsonPath.Substring(0, def.JsonPath.Length - 1) + sub.ToString(CultureInfo.InvariantCulture) : def.JsonPath;
                        if (ImGui.MenuItem(shown, null, watch.IsWatched(unit, def.JsonPath, sub))) watch.Toggle(unit, def.JsonPath, sub);
                    }
                }
            ImGui.EndMenu();
        }
        ImGui.TextDisabled("or a component field by path (expert):");
        ImGui.SetNextItemWidth(260f);
        ImGui.InputText("##path", ref _path, 128);
        ImGui.SameLine();
        if (ImGui.SmallButton("Add") && _path.Trim().IndexOf('.') > 0) { watch.Add(unit, _path.Trim(), 0, expert: true); _path = ""; }
        ImGui.EndPopup();
    }

    private static string UnitOf(ReplicaWatchItem it)
        => !it.Expert && AttributeReaders.TryResolve(it.Path, out var id, out _) && AttributeReaders.DefinitionOf(id)?.Unit is { } u ? u : "";
}
