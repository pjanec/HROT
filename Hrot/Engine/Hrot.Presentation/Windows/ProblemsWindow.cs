using System.Diagnostics;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using ImGuiNET;

namespace Hrot.Presentation.Windows;

/// <summary>
/// ⭐ CE-4318 (L-6, docs/UX/DESIGN_Scenario_Lifecycle_Review.md LC-13, §2.7) — the ONE <b>Problems</b> window: what is wrong with the scenario
/// (a rule whose subject is gone, a dangling link, a group mixing sides, an objective on a side with no units — backend's <see cref="ScenarioProblems"/>),
/// one row each; a click selects the culprit. ⛔ It never blocks Preview / Live — it only tells.
/// <para>⭐ Read on a timer (<see cref="RefreshSeconds"/>), never per frame (§2.7); the row texts are rebuilt only when the list CHANGED, so a steady
/// frame draws kept strings. <see cref="DrawToolbarCount"/> is the toolbar's count (nothing when there are none), one click opening this window.</para>
/// </summary>
public sealed class ProblemsWindow : ManagedWindow
{
    public const string Kind = "problems";
    /// <summary>How often the checks run (wall seconds).</summary>
    public const double RefreshSeconds = 1.0;

    private readonly Func<EntityRepository?> _world;
    private readonly Action<int> _selectNetworkId;
    private readonly List<ScenarioProblem> _scan = new();
    private readonly List<ScenarioProblem> _shown = new();
    private readonly List<string> _texts = new();
    private long _lastScan = long.MinValue;
    private string _countLabel = "";
    private int _countLabelFor = -1;
    private Action<string>? _focus;

    /// <param name="selectNetworkId">Selects an entity by its network id (the host's one selection request).</param>
    public ProblemsWindow(Func<EntityRepository?> world, Action<int> selectNetworkId, string id, string perspective, Vector4 titleBarColor)
        : base(id, "Problems", perspective, WindowScope.PerspectiveBound)
    {
        _world = world; _selectNetworkId = selectNetworkId;
        TitleBarColor = titleBarColor; IsOpen = false;
    }

    /// <summary>How many problems the last check found.</summary>
    public int Count => _shown.Count;
    /// <summary>The rows as drawn (rails).</summary>
    public IReadOnlyList<string> Rows => _texts;
    /// <summary>The problems as found (rails).</summary>
    public IReadOnlyList<ScenarioProblem> Problems => _shown;

    /// <inheritdoc/>
    public override void OnRegistered(WindowManager manager) => _focus = manager.FocusWindow;

    /// <summary>Runs the checks when <see cref="RefreshSeconds"/> passed (or <paramref name="force"/>); the rows change only when the problems did.</summary>
    public void Refresh(bool force = false)
    {
        long now = Stopwatch.GetTimestamp();
        if (!force && _lastScan != long.MinValue && (now - _lastScan) < RefreshSeconds * Stopwatch.Frequency) return;
        _lastScan = now;
        var world = _world();
        if (world == null) { if (_shown.Count > 0) { _shown.Clear(); _texts.Clear(); } return; }
        ScenarioProblems.Collect(world, _scan);
        if (Same(_scan, _shown)) return;
        _shown.Clear(); _shown.AddRange(_scan);
        _texts.Clear();
        foreach (var p in _shown) _texts.Add(ScenarioProblems.Text(world, p));
    }

    private static bool Same(List<ScenarioProblem> a, List<ScenarioProblem> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (!a[i].Equals(b[i])) return false;
        return true;
    }

    /// <summary>Selects the culprit of row <paramref name="index"/> (the row's click).</summary>
    public bool Select(int index)
    {
        if ((uint)index >= (uint)_shown.Count || _world() is not { } w) return false;
        var e = _shown[index].Culprit;
        if (!w.IsAlive(e) || !w.IsComponentTypeRegistered<NetworkIdentity>() || !w.HasComponent<NetworkIdentity>(e)) return false;
        _selectNetworkId(w.GetComponentRO<NetworkIdentity>(e).Value);
        return true;
    }

    /// <summary>The toolbar's count: ⚠ N, amber, nothing when there are none; a click opens this window.</summary>
    public void DrawToolbarCount()
    {
        Refresh();
        if (Count == 0) return;
        if (_countLabelFor != Count) { _countLabelFor = Count; _countLabel = $"{Fdp.Presentation.Fonts.IconsFontAwesome6.TriangleExclamation} {Count}##problems-count"; }   // 0 B while the count stands
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.96f, 0.70f, 0.18f, 1f));
        bool open = ImGui.Button(_countLabel);
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Count == 1 ? "1 problem in the scenario - open Problems" : $"{Count} problems in the scenario - open Problems");
        if (open) { IsOpen = true; _focus?.Invoke(Id); }
    }

    protected override void DrawClientArea() => DrawInto();

    /// <summary>The window's content, into the current ImGui window (a frame rail draws it without the window manager).</summary>
    internal void DrawInto()
    {
        Refresh();
        if (_texts.Count == 0) { ImGui.TextDisabled("No problems found."); return; }
        ImGui.TextDisabled("Click a row to select what it is about. Problems never stop Preview or Live.");
        for (int i = 0; i < _texts.Count; i++)
        {
            ImGui.PushID(i);
            ImGui.TextColored(new Vector4(0.96f, 0.70f, 0.18f, 1f), Fdp.Presentation.Fonts.IconsFontAwesome6.TriangleExclamation);
            ImGui.SameLine();
            if (ImGui.Selectable(_texts[i])) Select(i);
            ImGui.PopID();
        }
    }
}
