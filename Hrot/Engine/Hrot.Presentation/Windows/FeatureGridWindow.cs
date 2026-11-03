using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Orchestration;
using Hrot.Common.Infrastructure;
using ImGuiNET;

namespace Hrot.Presentation.Windows;

/// <summary>One node of the grid, as the master's roster holds it: its name, roles, the feature ids it ADVERTISES (<c>hrot.feature.&lt;id&gt;</c> tokens) and the ids ON on it (its heartbeat's <c>FeaturesOn</c>).</summary>
public sealed record FeatureGridNode(string Name, string Roles, IReadOnlySet<string> Available, IReadOnlySet<string> On);

public enum FeatureCellState { NotAvailable, On, Off }

/// <summary>One cell: the state and where it comes from — <c>cluster override</c> (an entry of the run settings names this node, or all) else <c>default</c> (the role's / the scenario's / the node's own — the master cannot tell those apart).</summary>
public sealed record FeatureGridCell(FeatureCellState State, string Source);

public sealed record FeatureGridRow(string FeatureId, IReadOnlyList<FeatureGridCell> Cells);

/// <summary>
/// ⭐ CE-4247 (rules runnable ⑨c; DESIGN_Node_Features F-11 / S2) — the operator's feature grid, PURE: nodes × features from the master's roster, and the toggle as a new REPLACEMENT list of the cluster's
/// <see cref="FeatureOverrideSpec"/>s (the run settings replace the whole list, §8 deviation 2).
/// </summary>
public static class FeatureGridModel
{
    public const string OverrideSource = "cluster override", DefaultSource = "default";
    private const string On = "{\"Enabled\":true}", Off = "{\"Enabled\":false}";

    public static IReadOnlyList<FeatureGridRow> Build(IReadOnlyList<FeatureGridNode> nodes, IReadOnlyList<FeatureOverrideSpec> overrides)
    {
        var ids = nodes.SelectMany(n => n.Available).Distinct().OrderBy(i => i, StringComparer.Ordinal).ToList();
        return ids.Select(id => new FeatureGridRow(id, nodes.Select(n =>
            !n.Available.Contains(id) ? new FeatureGridCell(FeatureCellState.NotAvailable, "")
            : new FeatureGridCell(n.On.Contains(id) ? FeatureCellState.On : FeatureCellState.Off, OverrideOf(overrides, id, n.Name) != null ? OverrideSource : DefaultSource)).ToList())).ToList();
    }

    private static FeatureOverrideSpec? OverrideOf(IReadOnlyList<FeatureOverrideSpec> overrides, string id, string node)
        => overrides.LastOrDefault(o => o.Feature == id && (o.Nodes.Count == 0 || o.Nodes.Contains(node, StringComparer.OrdinalIgnoreCase)));

    /// <summary>The override list after switching <paramref name="feature"/> on <paramref name="node"/> to <paramref name="turnOn"/>: an all-nodes entry of the feature is split per the OTHER nodes (their meaning kept), the node is removed from the feature's other named entries, and one entry for the node is added.</summary>
    public static FeatureOverrideSpec[] Toggled(IReadOnlyList<FeatureOverrideSpec> overrides, IReadOnlyList<FeatureGridNode> nodes, string feature, string node, bool turnOn)
    {
        var result = new List<FeatureOverrideSpec>();
        foreach (var o in overrides)
        {
            if (o.Feature != feature) { result.Add(o); continue; }
            if (o.Nodes.Count == 0) { foreach (var n in nodes.Where(n => !string.Equals(n.Name, node, StringComparison.OrdinalIgnoreCase))) result.Add(o with { Nodes = new[] { n.Name } }); continue; }
            var rest = o.Nodes.Where(n => !string.Equals(n, node, StringComparison.OrdinalIgnoreCase)).ToList();
            if (rest.Count > 0) result.Add(o with { Nodes = rest });
        }
        result.Add(new FeatureOverrideSpec(feature, new[] { node }, turnOn ? On : Off));
        return result.ToArray();
    }
}

/// <summary>What the <see cref="FeatureGridWindow"/> shows — the rails and an agent read it.</summary>
public sealed record FeatureGridProbe(IReadOnlyList<string> Nodes, IReadOnlyList<string> Rows, string? Message);

/// <summary>The panel snapshot of the feature grid.</summary>
public sealed record FeatureGridViewModel(string PanelId, string PanelKind, FeatureGridProbe Shown) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// ⭐ CE-4247 (⑨c) — <b>Window ▸ Features</b>: every node of the cluster × every feature any node advertises — <i>not available</i> / on / off with where the value comes from; a checkbox writes the cluster override through the
/// run settings (<see cref="SetRunSettingsIntent.FeatureOverrides"/>, the path <c>POST /run-settings</c> uses). A server-UI operator tool; the nodes apply a Live feature at once.
/// </summary>
public sealed class FeatureGridWindow : ManagedWindow
{
    public const string Kind = "feature-grid";

    private readonly Func<IReadOnlyList<FeatureGridNode>> _nodes;
    private readonly Func<IReadOnlyList<FeatureOverrideSpec>> _overrides;
    private readonly Action<FeatureOverrideSpec[]> _send;
    private FeatureGridProbe _probe = new(Array.Empty<string>(), Array.Empty<string>(), null);
    private string _filter = "";

    /// <param name="nodes">The roster as the master holds it (empty where there is none).</param>
    /// <param name="overrides">The cluster's override list as the nodes last heard it.</param>
    /// <param name="send">Replaces the cluster's override list (a <c>SetRunSettings</c> intent).</param>
    public FeatureGridWindow(string id, string perspective, System.Numerics.Vector4 titleBarColor, Func<IReadOnlyList<FeatureGridNode>> nodes, Func<IReadOnlyList<FeatureOverrideSpec>> overrides, Action<FeatureOverrideSpec[]> send)
        : base(id, "Features", perspective, WindowScope.PerspectiveBound)
    {
        _nodes = nodes; _overrides = overrides; _send = send;
        TitleBarColor = titleBarColor;
        IsOpen = false;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    public FeatureGridProbe Probe => _probe;
    public string? Message { get; private set; }

    /// <summary>Switches <paramref name="feature"/> on <paramref name="node"/>: sends the new override list. False when the node does not advertise it.</summary>
    public bool Set(string feature, string node, bool turnOn)
    {
        var nodes = _nodes();
        if (nodes.FirstOrDefault(n => string.Equals(n.Name, node, StringComparison.OrdinalIgnoreCase)) is not { } n || !n.Available.Contains(feature)) { Message = $"{node} does not advertise {feature}"; return false; }
        _send(FeatureGridModel.Toggled(_overrides(), nodes, feature, n.Name, turnOn));
        Message = $"{feature} on {n.Name}: {(turnOn ? "on" : "off")} (cluster override sent)";
        return true;
    }

    internal FeatureGridViewModel SimulateDrawClientArea()
    {
        var nodes = _nodes();
        var rows = FeatureGridModel.Build(nodes, _overrides());
        _probe = new FeatureGridProbe(nodes.Select(n => n.Name).ToList(),
            rows.Select(r => r.FeatureId + ": " + string.Join(" | ", r.Cells.Select(c => c.State == FeatureCellState.NotAvailable ? "-" : (c.State == FeatureCellState.On ? "on" : "off") + (c.Source == FeatureGridModel.OverrideSource ? "*" : "")))).ToList(), Message);
        var vm = new FeatureGridViewModel(Id, Kind, _probe);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    protected override void BeforeBegin()
    {
        ImGui.SetNextWindowSize(new Vector2(720f, 460f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(1f);
    }

    protected override void DrawClientArea()
    {
        SimulateDrawClientArea();
        var nodes = _nodes();
        var overrides = _overrides();
        var rows = FeatureGridModel.Build(nodes, overrides);
        ImGui.TextDisabled("Every node x every feature it advertises. * = a cluster override sets it; a tick writes one (run settings).");
        ImGui.SetNextItemWidth(220f); ImGui.InputTextWithHint("##filter", "filter features...", ref _filter, 64);
        ImGui.SameLine();
        if (overrides.Count == 0) ImGui.BeginDisabled();
        if (ImGui.Button("Clear all overrides")) { _send(Array.Empty<FeatureOverrideSpec>()); Message = "cluster overrides cleared"; }
        if (overrides.Count == 0) ImGui.EndDisabled();
        if (nodes.Count == 0) { ImGui.TextDisabled("no node on the roster (this host holds no cluster master)"); return; }
        if (ImGui.BeginTable("##grid", nodes.Count + 1, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.ScrollX | ImGuiTableFlags.SizingFixedFit, new Vector2(0, ImGui.GetContentRegionAvail().Y - 40f)))
        {
            ImGui.TableSetupScrollFreeze(1, 1);
            ImGui.TableSetupColumn("feature");
            foreach (var n in nodes) ImGui.TableSetupColumn($"{n.Name}##{n.Name}");
            ImGui.TableHeadersRow();
            foreach (var row in rows.Where(r => _filter.Length == 0 || r.FeatureId.Contains(_filter, StringComparison.OrdinalIgnoreCase)))
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted(row.FeatureId);
                for (int i = 0; i < nodes.Count; i++)
                {
                    ImGui.TableSetColumnIndex(i + 1);
                    var cell = row.Cells[i];
                    if (cell.State == FeatureCellState.NotAvailable) { ImGui.TextDisabled("-"); continue; }
                    bool on = cell.State == FeatureCellState.On;
                    if (ImGui.Checkbox($"##{row.FeatureId}@{nodes[i].Name}", ref on)) Set(row.FeatureId, nodes[i].Name, on);
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{row.FeatureId} on {nodes[i].Name} ({nodes[i].Roles}): {(on ? "on" : "off")} - {cell.Source}");
                    if (cell.Source == FeatureGridModel.OverrideSource) { ImGui.SameLine(); ImGui.TextColored(new Vector4(1f, 0.82f, 0.48f, 1f), "*"); }
                }
            }
            ImGui.EndTable();
        }
        if (Message != null) ImGui.TextWrapped(Message);
    }
}
