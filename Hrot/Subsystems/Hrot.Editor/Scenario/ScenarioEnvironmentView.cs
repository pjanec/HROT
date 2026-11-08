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

/// <summary>
/// ⭐⭐ CE-1161 (docs/designs/weather/DESIGN_Weather_And_Time.md §7; backend → ui handoff U2/U4) — THE SCENARIO VIEW: shown in Details when
/// the empty map is clicked. The scenario START TIME (UTC, with the display offset and the local time it gives) is edited through
/// <see cref="ScenarioClockEdit"/>; the global WEATHER is the World Environment's <c>WeatherConditions</c>, drawn by the borrowed entity
/// inspector exactly as the Components view draws a selected entity (E-3: no second weather editor — fixed buffers already edit).
/// <para>🔒 User (W-8): <i>"scenario start time … saved as is, not from current wall clock utc time"</i> — nothing here reads the wall
/// clock.</para>
/// </summary>
public sealed class ScenarioEnvironmentView : IDetailsViewInstance
{
    private readonly Func<EntityRepository?> _world;
    private readonly Func<IInspectableSession?> _session;
    private readonly Action<IInspectableSession, Entity> _drawComponents;
    private readonly ParametersDrawer? _parameters;
    private string _startText = string.Empty;
    private long _shownTicks = long.MinValue;
    private string? _error;

    public ScenarioEnvironmentView(Func<EntityRepository?> world, Func<IInspectableSession?> session,
        Action<IInspectableSession, Entity> drawComponents, ParametersDrawer? parameters = null)
    {
        _parameters = parameters;
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _drawComponents = drawComponents ?? throw new ArgumentNullException(nameof(drawComponents));
    }

    public void Draw(DetailsContext context, string idScope)
    {
        DrawEnvironment(context, idScope);
        DrawParameters();
    }

    /// <summary>
    /// ⭐ CE-4253 — the scenario's PARAMETERS: a collapsible header, CLOSED by default (🔒 user: <i>"a collapsible header, by default collapsed, called parameters … the same editor that now opens in a
    /// separate panel"</i>), holding the one parameter editor. Opened for a frame when something asked (<see cref="ParametersDrawer.RequestOpen"/>).
    /// </summary>
    internal void DrawParameters()
    {
        if (_parameters is null) return;
        ImGui.Separator();
        if (_parameters.ConsumeOpenRequest()) ImGui.SetNextItemOpen(true, ImGuiCond.Always);
        ParametersHeaderOpen = SectionHeader.Draw("hdr.parameters", "Parameters", "section/params");
        if (ParametersHeaderOpen) _parameters.Draw();
    }

    /// <summary>For rails: whether the Parameters header was open on the last draw.</summary>
    internal bool ParametersHeaderOpen { get; private set; }

    private static readonly string[] ClockLabels = { "Scenario start (UTC)", "Local offset (min)" };

    private void DrawEnvironment(DetailsContext context, string idScope)
    {
        var world = _world();
        if (!ScenarioClockEdit.TryRead(world, out var state))
        {
            ImGui.TextDisabled("No World Environment in this scenario yet.");
            return;
        }
        if (state.Clock.StartUtcTicks != _shownTicks)   // the clock moved (a load, another node): show it again
        {
            _shownTicks = state.Clock.StartUtcTicks;
            _startText = state.StartUtc.ToString(ScenarioClockEdit.Format, System.Globalization.CultureInfo.InvariantCulture);
            _error = null;
        }

        int offset = state.Clock.DisplayUtcOffsetMinutes;
        if (FormGrid.Begin("scn.clock", ClockLabels))
        {
            FormGrid.Row("Scenario start (UTC)");
            ImGui.SetNextItemWidth(-48f * ImGui.GetFontSize() / 13f);
            if (ImGui.InputText($"##start{idScope}", ref _startText, 32, ImGuiInputTextFlags.EnterReturnsTrue))
                Apply(world!);
            ImGui.SameLine();
            if (ImGui.Button($"Set##start{idScope}")) Apply(world!);
            if (_error != null) { ImGui.TableNextRow(); ImGui.TableSetColumnIndex(1); NodeEditor.Core.Widgets.RowMarks.ErrorLine(_error); }

            FormGrid.Row("Local offset (min)");
            if (ImGui.InputInt($"##off{idScope}", ref offset, 15, 60))
                ScenarioClockEdit.SetOffsetMinutes(world!, offset);
            FormGrid.End();
        }
        ImGui.TextDisabled($"Local start: {state.StartLocal.ToString(ScenarioClockEdit.Format, System.Globalization.CultureInfo.InvariantCulture)}"
                           + (state.Direct ? string.Empty : "  (sent to the owner)"));

        ImGui.Separator();
        ImGui.TextUnformatted("Global weather");
        if (_session() is { } session && session.IsAlive(state.Environment))
            _drawComponents(session, state.Environment);
        else
            ImGui.TextDisabled("Open a scenario to edit the weather.");
    }

    /// <summary>Applies the typed start time (exposed for rails: the ImGui half is a thin shell over it).</summary>
    internal bool Apply(EntityRepository world)
    {
        if (!ScenarioClockEdit.TryParse(_startText, out var utc)) { _error = $"Use {ScenarioClockEdit.Format} (UTC)."; return false; }
        _error = null;
        return ScenarioClockEdit.SetStartUtc(world, utc);
    }

    /// <summary>For rails: what the start field holds.</summary>
    internal string StartText { get => _startText; set => _startText = value; }

    public void Dispose() { }
}

/// <summary>⭐ CE-1161 — the Scenario view's descriptor (rank 10: above the Zones view).</summary>
public static class ScenarioEnvironmentViewDescriptor
{
    public const string ViewId = "details.scenario";
    public const int Rank = 10;

    public static DetailsViewDescriptor For(Func<EntityRepository?> world, Func<IInspectableSession?> session,
        Action<IInspectableSession, Entity> drawComponents, ParametersDrawer? parameters = null)
    {
        var instance = new ScenarioEnvironmentView(world, session, drawComponents, parameters);
        return new DetailsViewDescriptor(
            Id:        ViewId,
            Title:     "Scenario",
            Rank:      Rank,
            AppliesTo: DetailsViewPredicates.MapBackgroundWithNothingSelected,
            Create:    () => instance);
    }
}

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
