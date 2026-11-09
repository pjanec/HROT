using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Toolkit.Orchestration;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.RunMode;
using ImGuiNET;
using NodeEditor.Core.Widgets;

namespace Hrot.UI.Common.Panels;

/// <summary>
/// Main-toolbar time-control section rendering transport buttons (play/pause, step, stop)
/// at 64 px via <see cref="TransportIcons"/>, plus a formatted time readout and a
/// time-rate multiplier popup selector.
/// </summary>
/// <remarks>
/// The section reads the <see cref="ITimeTransportFacade"/> every frame (immediate mode).
/// Action logic is split from ImGui draw calls so it can be unit-tested headlessly:
/// use <see cref="PlayPauseFace"/>, <see cref="OnPlayPause"/>, <see cref="OnStep"/>,
/// <see cref="OnStop"/>, and <see cref="OnSelectRate"/> without an ImGui context.
/// </remarks>
public sealed class MainToolbarTimeControlSection
{
    private readonly ITimeTransportFacade _facade;
    private readonly Func<DateTime> _now;
    private readonly RunModeChoice? _choice;   // ⭐ CE-4097 — the host's run choice (null: the pre-4097 indicator, which only SHOWS the cluster's mode)

    // ⭐ F15 — ■ ignores a press for StopGuard after a review returned to live (a double press across ■ → ⏏ must not stop the exercise)
    private TransportMode _lastMode;
    private bool _modeSeen;
    private DateTime? _returnedToLiveAt;
    private readonly Func<IReadOnlyList<string>>? _runSetupLines;
    private readonly RunToController? _runTo;   // ⭐ W-4 (LC-10) — Run to …, on a host that has one (the editor)

    /// <summary>F15 — how long ■ ignores presses after a review returned to live.</summary>
    public static readonly TimeSpan StopGuard = TimeSpan.FromSeconds(3);

    /// <param name="facade">THE time facade.</param>
    /// <param name="now">The clock of the F15 guard (tests inject one); default <see cref="DateTime.UtcNow"/>.</param>
    /// <param name="choice">⭐ CE-4097 (AQ108 §2.8) — the host's <see cref="RunModeChoice"/>: the indicator draws its <see cref="RunModeChoice.Indicator"/>, its menu is the
    /// choice's moves (each with its confirmation, drawn as a modal), ▶ ■ keep calling the facade. ⛔ A host that HAS one must pass it.</param>
    /// <param name="runTo">⭐ W-4 (LC-10) — the host's <see cref="RunToController"/>: the mode menu offers <i>Run to …</i> in Edit and Preview. ⛔ A host that HAS one must pass it.</param>
    public MainToolbarTimeControlSection(ITimeTransportFacade facade, Func<DateTime>? now = null, RunModeChoice? choice = null, Func<IReadOnlyList<string>>? runSetupLines = null,
        RunToController? runTo = null)
    {
        _runTo = runTo;
        _runSetupLines = runSetupLines;
        _facade = facade;
        _now = now ?? (() => DateTime.UtcNow);
        _choice = choice;
    }

    // ── Headless-testable seams ────────────────────────────────────────

    /// <summary>
    /// Returns the icon shape that reflects the current play/pause state:
    /// <see cref="TransportIcons.BtnShape.Play"/> when paused,
    /// <see cref="TransportIcons.BtnShape.Pause"/> when running.
    /// </summary>
    public static TransportIcons.BtnShape PlayPauseFace(bool isPaused)
        => isPaused ? TransportIcons.BtnShape.Play : TransportIcons.BtnShape.Pause;

    /// <summary>
    /// Handles a Play/Pause click. Only invokes <see cref="ITimeTransportFacade.TogglePlayPause"/>
    /// when <see cref="ITimeTransportFacade.IsPlayPauseEnabled"/> is <c>true</c>.
    /// </summary>
    public void OnPlayPause() => _facade.UserPlayPause(_choice);   // ⭐ CE-4097 part two (D22): from Edit the choice's ▶, else the facade's toggle

    /// <summary>⭐ ▶ / ⏸ can be pressed (the facade says so; from Edit the armed mode can also run on this host).</summary>
    public bool IsPlayEnabled => _facade.IsUserPlayEnabled(_choice);

    /// <summary>⭐ Step can be pressed (same rule as <see cref="IsPlayEnabled"/>).</summary>
    public bool IsStepEnabled => _facade.IsUserStepEnabled(_choice);

    /// <summary>
    /// Handles a Step click. Only invokes <see cref="ITimeTransportFacade.Step"/>
    /// when <see cref="ITimeTransportFacade.IsStepEnabled"/> is <c>true</c>.
    /// </summary>
    public void OnStep() => _facade.UserStep(_choice);

    /// <summary>
    /// Handles a Stop click. Only invokes <see cref="ITimeTransportFacade.Stop"/>
    /// when <see cref="ITimeTransportFacade.IsStopEnabled"/> is <c>true</c>.
    /// </summary>
    public void OnStop()
    {
        Observe();
        if (StopGuarded) return;   // ⭐ F15 — a second press right after the return to live must not stop the exercise
        if (IsStopEnabled)
            _facade.Stop();
    }

    /// <summary>⭐ CE-3413 — ■ can be pressed: the facade says so, and the Live briefing (paused at 0:00, never ran) has nothing to stop.</summary>
    public bool IsStopEnabled => _facade.IsStopEnabled && _choice?.StopDisabledReason is null;

    /// <summary>⭐ CE-3413 — ⏪ Review is pressable: not during the briefing ("nothing to review yet").</summary>
    public bool IsReviewEnabled => _choice?.ReviewDisabledReason is null;

    /// <summary>
    /// Selects a time-rate multiplier via <see cref="ITimeTransportFacade.SetTimeScale"/>.
    /// </summary>
    public void OnSelectRate(float rate)
    {
        _facade.SetTimeScale(rate);
    }


    // ── ⭐ CE-3330 E3 — the mode indicator, its menu, ■ → ⏏, the axis icon (DAR §9a) ──────────────────────────────────

    /// <summary>The mode indicator of <see cref="ModeGlyph"/>: its FontAwesome 6 glyph (the spec's ✎ ◐ ● ⟲ ▶ — no baked font carries them, CE-1063), the colour.</summary>
    public sealed record ModeIndicator(string Glyph, Vector4 Color, string Name);

    /// <summary>DAR §9a — the indicator per mode: Edit grey pen, Preview violet eye (CE-3394), Live green circle, Review amber clock-back, Replay blue play; Idle none (<see langword="null"/>).</summary>
    public static ModeIndicator? ModeGlyph(TransportMode mode) => mode switch
    {
        TransportMode.Edit    => EditIndicator,
        TransportMode.Preview => PreviewIndicator,
        TransportMode.Live    => LiveIndicator,
        TransportMode.Review  => ReviewIndicator,
        TransportMode.Replay  => ReplayIndicator,
        _ => null,
    };

    // ⭐ U-2 — folded into the ONE icon map (NodeEditor.Core `Icons`, keys "mode/…"): the glyph and the colour come from there, only the word is this section's. Built once.
    private static ModeIndicator Indicator(string key, string name) { var g = NodeEditor.Core.Widgets.Icons.Get(key); return new ModeIndicator(g.Glyph, g.Color, name); }
    private static readonly ModeIndicator EditIndicator    = Indicator("mode/edit", "Edit");
    private static readonly ModeIndicator PreviewIndicator = Indicator("mode/preview", "Preview");
    private static readonly ModeIndicator LiveIndicator    = Indicator("mode/live", "Live");
    private static readonly ModeIndicator ReviewIndicator  = Indicator("mode/review", "DAR");
    private static readonly ModeIndicator ReplayIndicator  = Indicator("mode/replay", "AAR");

    /// <summary>
    /// ⭐ CE-3409 (AQ108 §0 D27) — the colour of ▶ / ⏸: <b>the glyph wears the indicator's colour, never the button's background</b>. In Edit / Idle ▶ is the Preview, violet (the pen is the INDICATOR's — ▶ previews); in a run the run's own:
    /// Preview violet, Live red when recorded / green when not (<paramref name="indicatorColour"/>, the shown indicator's), DAR amber, AAR blue. ⛔ Replaces L-11's rule (the ARMED mode's colour).
    /// </summary>
    public static Vector4? PlayColour(TransportMode mode, Vector4? indicatorColour) => mode switch
    {
        TransportMode.Edit or TransportMode.Idle => ModeGlyph(TransportMode.Preview)?.Color,
        _ => indicatorColour ?? ModeGlyph(mode)?.Color,
    };

    // ── ⭐ CE-3393 (📄 docs/UX/DESIGN_Scenario_Presets.md §5c, D8) — the narrow ▾ at ▶'s right: THE preset picker; it ARMS (✓), never the mode ──────────

    /// <summary>The ▾ is drawn: from Edit or the Live briefing, with a run choice, when the loaded scenario has a preset besides Default.</summary>
    /// <remarks>⭐ CE-3405 — Edit (▶ previews with it) and the Live briefing (a preset reloads Live with it); never while a run is on, in a DAR or an AAR.</remarks>
    public bool ShowsPresetArrow => _choice is { } c && (_facade.Mode == TransportMode.Edit || c.LiveBriefing) && c.LoadedPresets.Count > 1;

    private void DrawPresetArrow(float iconSize)
    {
        if (!ShowsPresetArrow) return;
        ImGui.SameLine(0f, 1f);
        if (ImGui.ArrowButton("##mt_presets", ImGuiDir.Down)) ImGui.OpenPopup("##mt_presets_menu");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Run with a preset");
        if (!ImGui.BeginPopup("##mt_presets_menu")) return;
        var choice = _choice!;
        var presets = choice.LoadedPresets;
        var armed = choice.ArmedPreset;
        for (int i = 0; i < presets.Count; i++)
        {
            var p = presets[i];
            if (ImGui.MenuItem(PresetText.Label(p), null, ReferenceEquals(p, armed) || armed is not null && string.Equals(p.Name, armed.Name, StringComparison.OrdinalIgnoreCase)))
                choice.ArmPreset(p.Name);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(PresetText.Summary(p));
        }
        ImGui.Separator();
        if (ImGui.MenuItem("Edit presets…", null, false, choice.EditPresets is not null)) choice.EditPresets!.Invoke();
        ImGui.EndPopup();
    }

    /// <summary>The mode indicator's FontAwesome 6 Solid glyph (merged into the UI font): Edit Pen, Preview Eye (CE-3394, presets D9), Live Circle,
    /// Review ClockRotateLeft, Replay Play; Idle none (<see langword="null"/>).</summary>
    public static string? ModeIcon(TransportMode mode) => ModeGlyph(mode)?.Glyph;   // ONE table: the indicator's

    /// <summary>One entry of the mode menu: a label, what it does, whether it can be chosen, and (a sub-menu) its children.</summary>
    public sealed record ModeMenuItem(string Label, Action? Invoke, bool Enabled = true, IReadOnlyList<ModeMenuItem>? Children = null, bool Selected = false,
        string? Glyph = null, Vector4? GlyphColor = null, bool IsSeparator = false)
    {
        /// <summary>A separator line between groups of the menu.</summary>
        public static readonly ModeMenuItem Separator = new("", null, IsSeparator: true);
    }

    /// <summary>The mode menu: the header line (mode + context) and the items.</summary>
    public sealed record ModeMenuModel(string Header, IReadOnlyList<ModeMenuItem> Items);

    private static readonly double[] ReviewWindows = Hrot.Common.Actions.RunModeActions.DarWindows;   // ⭐ A6 — one table of DAR windows

    private static string WindowLabel(double seconds) => seconds < 60 ? $"{seconds:0} s" : $"{seconds / 60:0} min";

    /// <summary>m:ss of a span in seconds ("5:27").</summary>
    public static string FormatSpan(double seconds)
    {
        var t = (long)Math.Round(Math.Max(0, seconds));
        return $"{t / 60}:{t % 60:00}";
    }

    /// <summary>How far the playhead is behind the live point, seconds — <see langword="null"/> when the live point is not known.</summary>
    private double? SecondsBehindLive()
    {
        if (_facade.Session?.LivePointSimSeconds is { } lp) return Math.Max(0, lp - _facade.TotalTime);   // TotalTime is the run's sim clock
        if (_facade.Axis == ReplayAxis.Sim && _facade.LivePoint is { } l) return Math.Max(0, l - _facade.TotalTime);
        return null;
    }

    /// <summary>The seek value of a checkpoint on the active axis — Sim: its sim time; Wall: seconds after the session's zero.</summary>
    private double CheckpointAt(CheckpointMark cp)
        => _facade.Axis == ReplayAxis.Sim
            ? cp.TotalTime
            : (cp.WallTicks - (_facade.Session?.ZeroWallTicks ?? 0)) / (double)TimeSpan.TicksPerSecond;

    /// <summary>
    /// The mode indicator's menu (DAR §9a) — <see langword="null"/> where there is nothing worth offering (Edit, Preview, Replay, Idle:
    /// the deferred items — ⚑ mark, ⑂ go live from here, review from a checkpoint — are not built). Every action goes through the ONE facade.
    /// </summary>
    public ModeMenuModel? ModeMenu()
    {
        var model = WithRunTo(ModeMenuBase());
        // ⭐ CE-4317 (R-4168, L-10) — Run setup ▸ Preview · Live: the preset each mode starts with (Edit menu and the Live briefing's)
        if (model is not null && RunSetupItem() is { } runSetup)
        {
            var withSetup = new List<ModeMenuItem>(model.Items);
            bool grouped = false;
            foreach (var i in model.Items) grouped |= i.IsSeparator;
            if (!grouped) withSetup.Add(ModeMenuItem.Separator);
            withSetup.Add(runSetup);
            model = new(model.Header, withSetup);
        }
        // ⭐ CE-3406 (story ②) — the Edit menu ends with Close (back to the Start screen, through the one door: unsaved edits ask first)
        if (model is not null && _choice is { CanCloseScenario: true } closer && _facade.Mode == TransportMode.Edit)
        {
            var closing = new List<ModeMenuItem>(model.Items) { ModeMenuItem.Separator, new("Close", closer.CloseScenario, Glyph: Fdp.Presentation.Fonts.IconsFontAwesome6.RightFromBracket) };
            model = new(model.Header, closing);
        }
        // ⭐ AQ108 D24 — the running scenario's variant and changed params, as plain (disabled) lines under the moves
        if (model is not null && _facade.Mode is not (TransportMode.Edit or TransportMode.Idle) && _runSetupLines?.Invoke() is { Count: > 0 } setup)
        {
            var items = new List<ModeMenuItem>(model.Items);
            foreach (var line in setup) items.Add(new(line, null, Enabled: false));
            model = new(model.Header, items);
        }
        return model;
    }

    /// <summary>The label of the mode menu's run-setup entry (rails).</summary>
    public const string RunSetupLabel = "Run setup";

    /// <summary>
    /// ⭐ CE-4317 (R-4168; presets D8 / D11) — <i>Run setup ▸ Preview ▸ presets · Live ▸ presets · Edit presets…</i>: ONE list per mode (the same <see cref="PresetText"/> wording as the ▾ at ▶), ✓ on the armed one. Choosing ARMS it for that mode
    /// (<see cref="RunModeChoice.ArmPresetFor"/>) — ⛔ never the mode, and ⛔ no second run-setup editor: params and variants are authored in the preset editor (<i>Edit presets…</i>). Offered in Edit and in the Live briefing (Live only there);
    /// <see langword="null"/> elsewhere. A Preview of the armed preset runs IN PLACE on the edit world — unsaved edits included (R-4168).
    /// </summary>
    private ModeMenuItem? RunSetupItem()
    {
        if (_choice is not { } c || c.LoadedPresets.Count <= 1) return null;   // like the ▾: only a scenario with a preset besides Default
        bool edit = _facade.Mode == TransportMode.Edit;
        if (!edit && !c.LiveBriefing) return null;
        if (c.LoadedScenario is not { } scenario) return null;
        var groups = new List<ModeMenuItem>();
        if (edit) groups.Add(PresetGroup(c, scenario, RunArmed.Preview, "Preview", ModeGlyph(TransportMode.Preview)));
        groups.Add(PresetGroup(c, scenario, RunArmed.Live, "Live", ModeGlyph(TransportMode.Live), c.LiveColour));
        var presets = new List<ModeMenuItem>();
        if (groups.Count == 1) presets.AddRange(groups[0].Children!); else presets.AddRange(groups);
        presets.Add(ModeMenuItem.Separator);
        presets.Add(new("Edit presets…", c.EditPresets, c.EditPresets is not null));
        return new(RunSetupLabel, null, Children: presets, Glyph: Fdp.Presentation.Fonts.IconsFontAwesome6.Sliders, GlyphColor: NodeEditor.Core.Widgets.IconPalette.Amber);
    }

    private static ModeMenuItem PresetGroup(RunModeChoice c, string scenario, RunArmed mode, string label, ModeIndicator? icon, Vector4? colour = null)
    {
        var armed = c.ArmedPresetOf(scenario, mode);
        var rows = new List<ModeMenuItem>();
        foreach (var p in c.LoadedPresets)
        {
            var name = p.Name;
            rows.Add(new(PresetText.Label(p), () => c.ArmPresetFor(mode, name), Selected: string.Equals(name, armed.Name, StringComparison.OrdinalIgnoreCase)));
        }
        return new(label, null, Children: rows, Glyph: icon?.Glyph, GlyphColor: colour ?? icon?.Color);
    }

    // ── ⭐ W-4 — Run to … (LC-10, 📄 DESIGN_Scenario_Lifecycle_Review.md §2.8; mockup run-to-visual.svg) ──────────────────────────────

    /// <summary>The run choices the Run to popup offers, read when it opens (never per frame).</summary>
    private readonly List<RunToChoices.Choice> _runToPhases = new(), _runToRules = new();
    private bool _runToOpenRequested;
    private Vector2 _runToAnchor;   // the bottom-left of the mode indicator, where the popup opens
    private string _runToText = "";

    /// <summary>The phases the Run to popup offers (rails).</summary>
    public IReadOnlyList<RunToChoices.Choice> RunToPhases => _runToPhases;
    /// <summary>The rules the Run to popup offers under <i>When a rule fires</i> (rails).</summary>
    public IReadOnlyList<RunToChoices.Choice> RunToRules => _runToRules;
    /// <summary>The Run to popup opens on the next drawn frame (rails).</summary>
    public bool RunToOpenRequested => _runToOpenRequested;

    /// <summary>Run to … is offered now: a host with a controller, in a Preview, or in Edit when the host can start one from there.</summary>
    public bool OffersRunTo => _runTo is { } r && (_facade.Mode == TransportMode.Preview || (_facade.Mode == TransportMode.Edit && r.CanStartFromEdit));

    /// <summary>⭐ The item's click: reads the phases and rules of the world once, then the popup opens.</summary>
    public void OpenRunTo()
    {
        if (_runTo?.World is { } world) RunToChoices.Collect(world, _runToPhases, _runToRules);
        else { _runToPhases.Clear(); _runToRules.Clear(); }
        _runToOpenRequested = true;
    }

    /// <summary>Why the typed time cannot run (null = it can): not a time, or not ahead of the clock (in Preview) / of the start (from Edit).</summary>
    public string? RunToTimeProblem(string text, out double seconds)
    {
        if (!RunToChoices.TryParseTime(text, out seconds)) return "type a time: m:ss, h:mm:ss, or minutes";
        double now = _facade.Mode == TransportMode.Preview ? _facade.TotalTime : 0;
        return seconds > now ? null : $"{RunToController.Clock(seconds)} is not ahead of the clock ({RunToController.Clock(now)})";
    }

    /// <summary>Runs to the typed time (the popup's Run); false with the reason in the controller's status.</summary>
    public bool RunToTime(string text)
        => _runTo is { } r && RunToTimeProblem(text, out double t) is null && r.Request(t, null);

    /// <summary>Runs until <paramref name="choice"/> holds (a phase begins, a rule fires).</summary>
    public bool RunToChoice(RunToChoices.Choice choice) => _runTo is { } r && r.Request(null, choice.Until);

    private ModeMenuModel? WithRunTo(ModeMenuModel? model)
    {
        // ⭐ A6 — in an AAR the playhead's move (Go live from here), after the arming moves
        if (_facade.Mode == TransportMode.Replay && model is not null)
        {
            var rows = PlayheadRows();
            if (rows.Count > 0)
            {
                var withRows = new List<ModeMenuItem>(model.Items) { ModeMenuItem.Separator };
                foreach (var r in rows) withRows.Add(new(r.Shown, r.Run, Enabled: r.Why is null));
                model = new(model.Header, withRows);
            }
        }
        if (_runTo is not { } run) return model;
        bool active = run.IsRunning || run.IsStarting;
        if (!active && !OffersRunTo) return model;
        model ??= new(_facade.Mode == TransportMode.Preview ? $"Preview · {TransportIcons.FormatTime(_facade.TotalTime)}" : "Edit", Array.Empty<ModeMenuItem>());   // a host with no run choice: Run to alone
        var items = new List<ModeMenuItem>(model.Items);
        if (items.Count > 0) items.Add(ModeMenuItem.Separator);
        var hourglass = Fdp.Presentation.Fonts.IconsFontAwesome6.HourglassHalf;
        if (active)
        {
            items.Add(new(run.Status, null, Enabled: false));
            items.Add(new("Stop Run to", () => run.Cancel(), Glyph: Fdp.Presentation.Fonts.IconsFontAwesome6.Xmark, GlyphColor: RunToStopColor));
        }
        else
        {
            if (run.Status.Length > 0) items.Add(new(run.Status, null, Enabled: false));   // why the last one ended, until the next
            foreach (var r in PlayheadRows())   // ⭐ A6 — declared and worded once (RunModeActions.RunTo; its modes: Edit, Preview)
                if (r.Id == Hrot.Common.Constants.GlobalActionIds.RunTo)
                    items.Add(new(r.Label, r.Run, Glyph: hourglass, GlyphColor: ModeGlyph(TransportMode.Preview)?.Color));
        }
        return new(model.Header, items);
    }

    private static readonly Vector4 RunToStopColor = new(0.90f, 0.33f, 0.33f, 1f);   // the house red (C.RED)
    private static readonly Vector4 PhaseColor = new(0.95f, 0.75f, 0.20f, 1f);       // amber: a phase (the Rules window's flag)
    private static readonly Vector4 RuleColor = new(0.95f, 0.61f, 0.22f, 1f);        // orange: a rule (a trigger's bolt)

    private void DrawRunToPopup()
    {
        if (_runToOpenRequested) { _runToOpenRequested = false; ImGui.OpenPopup("##mt_runto"); ImGui.SetNextWindowPos(_runToAnchor); }   // under the mode indicator (the mockup)
        if (!ImGui.BeginPopup("##mt_runto")) return;
        // the title strip (the mockup's window title)
        {
            var dl = ImGui.GetWindowDrawList();
            var p = ImGui.GetCursorScreenPos();
            float w = Math.Max(ImGui.GetContentRegionAvail().X, 330f);
            dl.AddRectFilled(p - new Vector2(4, 4), p + new Vector2(w + 4, ImGui.GetTextLineHeight() + 4), ImGui.GetColorU32(ImGuiCol.TitleBgActive));
            ImGui.Text("Run to");
            ImGui.Dummy(new Vector2(w, 2));
        }
        ImGui.TextDisabled("Run the Preview as fast as it steps, then pause.");
        ImGui.AlignTextToFramePadding();
        ImGui.Text("Time");
        ImGui.SameLine(60);
        ImGui.SetNextItemWidth(120);
        bool enter = ImGui.InputTextWithHint("##runto_time", "m:ss", ref _runToText, 16, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        var why = RunToTimeProblem(_runToText, out _);
        ImGui.BeginDisabled(why is not null);
        bool run = ImGui.Button($"{Fdp.Presentation.Fonts.IconsFontAwesome6.Play} Run##runto_go", new Vector2(70, 0));
        ImGui.EndDisabled();
        if (why is not null && _runToText.Length > 0 && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(why);
        if ((run || enter) && why is null && RunToTime(_runToText)) ImGui.CloseCurrentPopup();
        ImGui.Separator();
        ImGui.TextDisabled("When a phase begins");
        if (_runToPhases.Count == 0) ImGui.TextDisabled("  no phases in this scenario");
        for (int i = 0; i < _runToPhases.Count; i++)
        {
            ImGui.PushID(i);
            ImGui.TextColored(PhaseColor, Fdp.Presentation.Fonts.IconsFontAwesome6.Flag);
            ImGui.SameLine();
            if (ImGui.Selectable(_runToPhases[i].Label) && RunToChoice(_runToPhases[i])) ImGui.CloseCurrentPopup();
            ImGui.PopID();
        }
        ImGui.Separator();
        ImGui.TextColored(RuleColor, Fdp.Presentation.Fonts.IconsFontAwesome6.Bolt);
        ImGui.SameLine();
        if (ImGui.BeginMenu("When a rule fires", _runToRules.Count > 0))
        {
            for (int i = 0; i < _runToRules.Count; i++)
            {
                ImGui.PushID(1000 + i);
                ImGui.TextColored(RuleColor, Fdp.Presentation.Fonts.IconsFontAwesome6.Bolt);
                ImGui.SameLine();
                if (ImGui.MenuItem(_runToRules[i].Label) && RunToChoice(_runToRules[i])) ImGui.CloseCurrentPopup();
                ImGui.PopID();
            }
            ImGui.EndMenu();
        }
        ImGui.Separator();
        ImGui.TextDisabled("It pauses there. Stop Run to (this menu) stops it sooner.");
        ImGui.EndPopup();
    }

    private ModeMenuModel? ModeMenuBase()
    {
        var legacy = LegacyModeMenu();
        if (_choice is null) return legacy;
        if (_choice.LiveEnded) return EndedMenu(_choice);   // ⭐ CE-3413 — what is next after the exercise

        // ⭐ CE-4097 — the choice's moves (AQ108 §3: only those that start from the current state; each asks its own confirmation), after the DAR items
        var moves = _choice.Moves();
        if (moves.Count == 0) return legacy;   // a DAR keeps its own menu (restart / back to live)
        var items = new List<ModeMenuItem>();
        if (legacy is not null) items.AddRange(legacy.Items);
        foreach (var move in moves)
        {
            var target = move.Target;
            var label = move.DisabledReason is { } why ? $"{move.Label} — {why}" : move.Label;
            // ⭐ R-8 (story ②): each mode's own glyph in its own colour — pen yellow · Live ● red / green · AAR ▶ blue
            var icon = target switch { RunArmed.Live => ModeGlyph(TransportMode.Live), RunArmed.Preview => ModeGlyph(TransportMode.Edit), _ => ModeGlyph(TransportMode.Replay) };
            items.Add(new(label, move.IsShown ? () => { } : () => _choice.Choose(target), move.Enabled, Selected: move.IsShown,
                Glyph: icon?.Glyph, GlyphColor: target == RunArmed.Live ? _choice.LiveColour : icon?.Color));   // ⭐ CE-4111: the shown item does nothing (no Choose)
        }
        var header = legacy?.Header ?? (_facade.Mode is TransportMode.Edit or TransportMode.Idle
            ? "Edit · ▶ previews"
            : $"{_choice.Indicator.Word} · {TransportIcons.FormatTime(_facade.TotalTime)}");
        // ⭐ CE-4111: during a run the armed mode is not lost — a load goes into it (R-1043)
        if (_facade.Mode is not (TransportMode.Edit or TransportMode.Idle)) header += $" · loads go into: {_choice.Armed}";
        return new(header, items);
    }

    /// <summary>
    /// ⭐ CE-3413 (AQ108 §0 R2″) — the indicator's menu in <b>Live · ended</b>: Run again (same scenario → the briefing) · Restart from a checkpoint ▸ · DAR — look back… · AAR — full replay of this exercise · Close the session.
    /// The live run's checkpoints ride the session layer (backend ANSWER): the operator host's adapter fills <see cref="RunModeChoice.CheckpointLabels"/>; greyed "none listed for this run" while there are none.
    /// </summary>
    private ModeMenuModel EndedMenu(RunModeChoice c)
    {
        var live = c.LiveColour;   // ⭐ R-8 — red when recorded, as the indicator
        var labels = c.CheckpointLabels?.Invoke() ?? Array.Empty<string>();
        var checkpoints = new List<ModeMenuItem>();
        foreach (var label in labels) { var l = label; checkpoints.Add(new(l, () => c.RestoreCheckpoint?.Invoke(l), c.RestoreCheckpoint is not null)); }
        var dar = ReviewChoices();
        var aarWhy = c.EndedAarDisabledReason;
        var items = new List<ModeMenuItem>
        {
            new("Run again (same scenario → the briefing)", c.RunAgain, Glyph: Fdp.Presentation.Fonts.IconsFontAwesome6.Circle, GlyphColor: live),
            new(checkpoints.Count > 0 ? "Restart from a checkpoint" : "Restart from a checkpoint — none listed for this run", null, Enabled: checkpoints.Count > 0, Children: checkpoints,
                Glyph: Fdp.Presentation.Fonts.IconsFontAwesome6.Flag, GlyphColor: NodeEditor.Core.Widgets.IconPalette.Amber),   // ⭐ R-8 — the amber checkpoint flag, as story ⑥
            new("DAR — look back…", null, Enabled: dar.Count > 0, Children: dar, Glyph: Fdp.Presentation.Fonts.IconsFontAwesome6.ClockRotateLeft, GlyphColor: ModeGlyph(TransportMode.Review)!.Color),
            new(aarWhy is null ? "AAR — full replay of this exercise" : $"AAR — full replay of this exercise — {aarWhy}", c.ReplayEndedExercise, Enabled: aarWhy is null,
                Glyph: Fdp.Presentation.Fonts.IconsFontAwesome6.Play, GlyphColor: ModeGlyph(TransportMode.Replay)!.Color),
            ModeMenuItem.Separator,
            new("Close the session", c.Stop, Glyph: Fdp.Presentation.Fonts.IconsFontAwesome6.RightFromBracket),
        };
        return new($"Live · ended · {TransportIcons.FormatTime(_facade.TotalTime)}", items);
    }

    /// <summary>
    /// ⭐ The DAR choices of a live run — ONE list for the mode menu and ⭐ CE-4321 (L-5, LC-16) the ⏪ Review button beside ▶: the last N minutes, from the start;
    /// greyed with the reason when the run is not recorded (D14). Empty outside Live.
    /// </summary>
    public IReadOnlyList<ModeMenuItem> ReviewChoices()
    {
        var items = new List<ModeMenuItem>();
        if (_facade.Mode != TransportMode.Live) return items;
        foreach (var r in PlayheadRows())
            if (r.Id is Hrot.Common.Constants.GlobalActionIds.DarLastWindow or Hrot.Common.Constants.GlobalActionIds.DarFromStart)
                items.Add(new(r.Shown, r.Run, Enabled: r.Why is null));
        return items;
    }

    /// <summary>
    /// ⭐ A6 (📄 docs/UX/DESIGN_Action_Catalogue.md §9) — the run-mode moves at the PLAYHEAD, composed from the action catalogue
    /// (<see cref="Hrot.Common.Actions.RunModeActions"/>: declared and worded once) and bound here: in Live the DAR windows, <i>DAR from the start</i>
    /// (⭐ D14 — greyed with the reason when the run is not recorded) and <i>Take checkpoint here</i>; in an AAR <i>Go live from here</i>; in Edit /
    /// Preview <i>Run to …</i> (drawn by <see cref="WithRunTo"/>). Built when the menu is built, never cached across modes.
    /// </summary>
    public List<RunModeBinding.Row> PlayheadRows()
    {
        var ctx = Hrot.Common.Actions.ActionContext.ForMoment(Hrot.Common.Actions.ActionTarget.Playhead, RunModeBinding.ModeOf(_facade.Mode), TransportIcons.FormatTime(_facade.TotalTime));
        return RunModeBinding.Rows(ctx, BindPlayhead);
    }

    private RunModeBinding.Binding? BindPlayhead(Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto row)
    {
        var mode = _facade.Mode;
        var darWhy = _choice?.DarDisabledReason;   // ⭐ D14 — a run that was not recorded has nothing to look back at (the master refuses it too)
        switch (row.Id)
        {
            case Hrot.Common.Constants.GlobalActionIds.DarLastWindow when mode == TransportMode.Live && row.Priority is int window:
                return new(() => _facade.EnterReviewAtSim(Math.Max(0, _facade.TotalTime - window)), darWhy);
            case Hrot.Common.Constants.GlobalActionIds.DarFromStart when mode == TransportMode.Live:
                return new(() => _facade.EnterReviewAtSim(0), darWhy);
            case Hrot.Common.Constants.GlobalActionIds.TakeCheckpointHere when mode == TransportMode.Live && _choice is { } c:
                return new(c.TakeCheckpoint, c.CheckpointDisabledReason);
            case Hrot.Common.Constants.GlobalActionIds.GoLiveFromHere when mode == TransportMode.Replay && _choice is { } c:
                return new(() => c.GoLiveFromHere(() => { }), c.GoLiveFromHereDisabledReason);   // the playhead IS the moment: nothing to seek
            case Hrot.Common.Constants.GlobalActionIds.RunTo when OffersRunTo:
                return new(OpenRunTo);
            default:
                return null;
        }
    }

    /// <summary>⭐ CE-4321 — the ⏪ Review button shows in a live run only.</summary>
    public bool ShowsReviewButton => _facade.Mode == TransportMode.Live;

    private IReadOnlyList<ModeMenuItem>? _reviewOpen;   // the choices while the button's popup is up (built on the click, not per frame)

    private ModeMenuModel? LegacyModeMenu()
    {
        switch (_facade.Mode)
        {
            case TransportMode.Live:
            {
                var items = new List<ModeMenuItem>();   // ⭐ A6 — the playhead's run-mode moves (the DAR choices, Take checkpoint here)
                foreach (var r in PlayheadRows()) items.Add(new(r.Shown, r.Run, Enabled: r.Why is null));
                return new($"Live · {TransportIcons.FormatTime(_facade.TotalTime)}", items);
            }
            case TransportMode.Review:
            {
                var items = new List<ModeMenuItem>();
                if (_facade.LivePoint is { } live)
                    for (int i = 0; i < 2; i++)
                    {
                        var window = ReviewWindows[i];
                        items.Add(new($"Restart from {WindowLabel(window)} before live",
                            () => _facade.Seek(Math.Max(0, live - window))));
                    }
                var marks = _facade.Session?.Checkpoints ?? Array.Empty<CheckpointMark>();
                var children = new List<ModeMenuItem>();
                foreach (var cp in marks)
                {
                    var mark = cp;
                    children.Add(new(mark.Label, () => _facade.Seek(CheckpointAt(mark))));
                }
                items.Add(new("Restart from a checkpoint", null, Enabled: children.Count > 0, Children: children));
                items.Add(new("⏏ Back to live", _facade.ExitReview));
                var behind = SecondsBehindLive();
                return new(behind is { } b ? $"DAR · {FormatSpan(b)} behind live" : "DAR", items);
            }
            default:
                return null;
        }
    }

    /// <summary>What ■ looks like: the red square, or — in a review — the green ⏏ "Back to live".</summary>
    public enum StopFaceKind { Stop, BackToLive }

    /// <summary>■ in a review is ⏏ Back to live (R-3060), else ■.</summary>
    public static StopFaceKind StopFace(TransportMode mode) => mode == TransportMode.Review ? StopFaceKind.BackToLive : StopFaceKind.Stop;

    /// <summary>⭐ CE-3413 — ■ becomes ⏏ in a DAR (back to live) and in Live · ended (close the session).</summary>
    public StopFaceKind StopFaceNow => StopFace(_facade.Mode) == StopFaceKind.BackToLive || _choice?.LiveEnded == true ? StopFaceKind.BackToLive : StopFaceKind.Stop;

    /// <summary>
    /// ⭐ The ▶ / ⏸ tooltip: what a press does, named by the mode. 🔒 User, <c>2026-10-30</c>: <i>"the Play button should have tooltip saying what it does
    /// based on armed mode (Play Live, Play AAR, Play DAR, Play Preview...)"</i>. In Edit ▶ starts the ARMED mode (R-1043 — the indicator arms it);
    /// in a run it resumes or pauses that run.
    /// </summary>
    public string PlayTooltip(bool isPaused)
    {
        var mode = _facade.Mode;
        if (mode == TransportMode.Edit || mode == TransportMode.Idle)
        {
            // ⭐ CE-3405 — ▶ in Edit is ALWAYS the Preview (the mode is switched at the indicator)
            string play = "Play Preview — a dry run of the scenario (nothing recorded)";
            // ⭐ CE-3393 (presets §5c) — the tooltip names the ARMED preset too: "▶ Preview — Night attack (variant 3 · preview only)"
            if (_choice is { ArmedPreset: { } p } && !string.Equals(p.Name, Fdp.Toolkit.Scenario.ScenarioPreset.DefaultName, StringComparison.OrdinalIgnoreCase))
                play += "\nPreset: " + p.Name + " (" + PresetText.Summary(p) + ")";
            return play;
        }
        if (_choice?.RunPlayDisabledReason is { } why) return why;   // ⭐ CE-3413 (Live · ended) · Next_2 A-9 (a host's reason: the merged Review) — a greyed ▶ says why
        string name = mode switch
        {
            TransportMode.Live    => "Live",
            TransportMode.Review  => "DAR",
            TransportMode.Replay  => "AAR",
            TransportMode.Preview => "Preview",
            _                     => "",
        };
        return isPaused ? $"Play {name} — resume" : $"Pause {name}";
    }

    /// <summary>The ■ tooltip per DAR §9a — and, for 3 s after a return to live, why a press is ignored (F15).</summary>
    public string StopTooltip()
    {
        Observe();
        if (StopGuarded) return "Just returned to live — press again in a moment to stop the exercise";
        return _facade.Mode switch
        {
            TransportMode.Review  => SecondsBehindLive() is { } b ? $"Back to live — {FormatSpan(b)} ahead" : "Back to live",
            TransportMode.Replay  => _choice is null ? "Leave the AAR" : "Back to the recording's start",
            TransportMode.Preview => "Back to edit",
            TransportMode.Live    => _choice is null ? "Stop the exercise"
                                   : _choice.LiveEnded ? "Close the session — every station returns to the Start screen"   // ⭐ CE-3413 (R2″)
                                   : _choice.StopDisabledReason ?? "End the exercise",
            _                     => "Stop",
        };
    }

    /// <summary>⭐ F15 — observes <see cref="ITimeTransportFacade.Mode"/> across calls; a Review → Live change starts the guard.</summary>
    public void Observe()
    {
        var mode = _facade.Mode;
        if (_modeSeen && _lastMode == TransportMode.Review && mode == TransportMode.Live)
            _returnedToLiveAt = _now();
        _lastMode = mode;
        _modeSeen = true;
    }

    private bool StopGuarded =>
        _facade.Mode == TransportMode.Live && _returnedToLiveAt is { } at && _now() - at < StopGuard;

    /// <summary>The axis icon's letter: S = sim (pauses skipped), W = wall (pauses shown).</summary>
    public static string AxisFace(ReplayAxis axis) => axis == ReplayAxis.Sim ? "S" : "W";

    /// <summary>The axis icon's tooltip.</summary>
    public static string AxisTooltip(ReplayAxis axis)
        => axis == ReplayAxis.Sim ? "Sim time — pauses skipped" : "Wall time — pauses shown";

    /// <summary>The axis toggles only where a session runs: Live, Review, Replay.</summary>
    public bool IsAxisEnabled => AxisEnabledIn(_facade.Mode);

    /// <summary>⭐ CE-4325 — the ONE rule (the timeline corner's S / W reads it too): the axis toggles only where a session runs.</summary>
    public static bool AxisEnabledIn(TransportMode mode) => mode is TransportMode.Live or TransportMode.Review or TransportMode.Replay;

    /// <summary>An axis-icon click: S ⇄ W for the whole session (R-3058).</summary>
    public void OnToggleAxis() => ToggleAxis(_facade);

    /// <summary>⭐ CE-4325 — the ONE toggle: S ⇄ W for the whole session. The toolbar had the button; the Event Timeline's corner has it now — both call here.</summary>
    public static void ToggleAxis(ITimeTransportFacade facade)
    {
        if (!AxisEnabledIn(facade.Mode)) return;
        facade.SetAxis(facade.Axis == ReplayAxis.Sim ? ReplayAxis.Wall : ReplayAxis.Sim);
    }

    // ── Render helpers ────────────────────────────────────────────────

    /// <summary>
    /// Draws a graphical vertical divider line matching the current toolbar height,
    /// then advances the cursor by a small gap. Replaces the old text-based " | ".
    /// </summary>
    private static void DrawVerticalSeparator(float height)
    {
        var pos = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(
            pos,
            pos + new Vector2(0f, height),
            ImGui.GetColorU32(new Vector4(0.4f, 0.4f, 0.4f, 1f)));
        ImGui.Dummy(new Vector2(5f, height));
    }

    // ── Render ─────────────────────────────────────────────────────────

    /// <summary>
    /// Renders the toolbar time-control group. Must be called inside an active
    /// ImGui frame (typically from a <c>MainToolbarManager</c> entry delegate).
    /// </summary>
    public void Render()
    {
        float iconSize = ImGui.GetFrameHeight();
        bool isPaused = _facade.IsPaused;
        float timeScale = _facade.TimeScale;
        Observe();

        // ── [Mode indicator] — opens its menu, never acts (R-3061) ─────────
        DrawModeIndicator(iconSize);
        ImGui.SameLine();
        // ⭐ R-4170 (CE-4329) — the indicator is ONE glyph button next to ▶ (AQ108 D29); the seat · hat text is the status bar's (StationSeatStatusBarSection).

        // ── [Play/Pause] ───────────────────────────────────────────────
        if (TransportIcons.DrawTransportButton("##mt_pp", iconSize,
                PlayPauseFace(isPaused), IsPlayEnabled, PlayColour(_facade.Mode, _choice?.Indicator.Color)))
        {
            OnPlayPause();
        }
        PlayRect = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip((_facade.Mode == TransportMode.Edit ? _choice?.PlayDisabledReason : _choice?.RunPlayDisabledReason) is { } playWhy
                ? playWhy                    // the gate's reason; ⭐ CE-3413 Live · ended: "the exercise has ended — restart from a checkpoint to train again"
                : PlayTooltip(isPaused));    // what a press does, by the mode
        DrawPresetArrow(iconSize);

        ImGui.SameLine();

        // ── [⏪ Review] — ⭐ CE-4321 (L-5, LC-16): the DAR choices one click away, beside ▶ (the mode menu keeps them too) ──
        if (ShowsReviewButton)
        {
            if (TransportIcons.DrawTransportButton("##mt_review", iconSize, TransportIcons.BtnShape.Rewind, enabled: IsReviewEnabled))
            {
                _reviewOpen = ReviewChoices();
                ImGui.OpenPopup("##mt_review_menu");
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(_choice?.ReviewDisabledReason ?? "Review (DAR): look back at the live run");
            if (ImGui.BeginPopup("##mt_review_menu"))
            {
                if (_reviewOpen != null)
                    foreach (var item in _reviewOpen)
                    {
                        if (!item.Enabled) ImGui.BeginDisabled();
                        if (ImGui.MenuItem(item.Label)) item.Invoke?.Invoke();
                        if (!item.Enabled) ImGui.EndDisabled();
                    }
                ImGui.EndPopup();
            }
            ImGui.SameLine();
        }

        // ── [Step] ─────────────────────────────────────────────────────
        if (TransportIcons.DrawTransportButton("##mt_step", iconSize,
                TransportIcons.BtnShape.Step, enabled: IsStepEnabled))
        {
            OnStep();
        }

        ImGui.SameLine();

        // ── [Stop] — ⏏ Back to live in a review ────────────────────────
        var stopShape = StopFaceNow == StopFaceKind.BackToLive
            ? TransportIcons.BtnShape.Eject : TransportIcons.BtnShape.Stop;
        if (TransportIcons.DrawTransportButton("##mt_stop", iconSize, stopShape, enabled: IsStopEnabled))
        {
            OnStop();
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(StopTooltip());

        ImGui.SameLine();

        // ── Time display ───────────────────────────────────────────────
        DrawVerticalSeparator(iconSize);
        ImGui.SameLine();
        ImGui.TextUnformatted(TransportIcons.FormatTime(_facade.TotalTime));

        // ── Rate selector ──────────────────────────────────────────────
        ImGui.SameLine();
        DrawVerticalSeparator(iconSize);
        ImGui.SameLine();

        if (ImGui.Button(TransportIcons.FormatRate(timeScale)))
            ImGui.OpenPopup("##mt_rate_popup");

        if (ImGui.BeginPopup("##mt_rate_popup"))
        {
            foreach (float rate in TransportIcons.TimeRates)
            {
                bool isSelected = MathF.Abs(timeScale - rate) < 0.01f;
                if (ImGui.Selectable(TransportIcons.FormatRate(rate), isSelected))
                    OnSelectRate(rate);
            }
            ImGui.EndPopup();
        }

        // ── [Axis] S / W — ⭐ CE-4325 (E7, R-4177 Q3): MOVED to the Event Timeline's title-bar corner (TimelineCornerBinding), beside the ⚑; it still sets the axis for the whole session ──

        // ── ⭐ CE-4097 — the note after ■ in Live, and the one confirmation shape (R3) ─────────────────────────────────
        if (_choice?.Toast is { } toast)
        {
            ImGui.SameLine();
            if (toast.Actions is { Count: > 0 }) { if (ImGui.SmallButton(toast.Title + "##mt_toast")) _choice.ReplayLastExercise(); }
            else ImGui.TextDisabled(toast.Title);
        }
        // ⭐ R-4001 — the unsaved round has no timeout: it is shown while it runs, and the operator may cancel it
        if (_choice?.IsCheckingUnsaved == true)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(CheckingUnsavedText);
            ImGui.SameLine();
            if (ImGui.SmallButton("Cancel##mt_check_cancel")) _choice.CancelCheck();
        }
        DrawConfirmation();
    }

    /// <summary>⭐ R-4001 — what the toolbar shows while the unsaved-changes round runs.</summary>
    public const string CheckingUnsavedText = "Checking every node for unsaved edits…";

    /// <summary>⭐ CE-4097 — draws <see cref="RunModeChoice.Pending"/> through the ONE modal helper (CE-1438): its text and buttons; closing it (Esc / ✕) is Cancel, the last button.</summary>
    private void DrawConfirmation() => ConfirmationModal.Draw(_choice?.Pending);

    /// <summary>
    /// ⭐ CE-3406 (AQ108 §0 D1′ + D29) — the mode indicator, drawn as a BUTTON through the ONE icon-button chrome (<see cref="IconButtonChrome"/>: transparent at rest, a white frame on hover) with a small ▾ (it opens the mode menu), the hand cursor and the
    /// tooltip. The glyph is the CURRENT mode (<see cref="RunModeChoice.Indicator"/>): steady, or blinking while the run plays — no dim level. Zero allocation while nothing is hovered.
    /// </summary>
    /// <summary>⭐ CE-4329 — the screen rectangles of the run-mode indicator and of ▶ as last drawn (a rail's measurement).</summary>
    internal (Vector2 Min, Vector2 Max) IndicatorRect, PlayRect;

    private void DrawModeIndicator(float size)
    {
        var mode = _facade.Mode;
        var chosen = _choice?.Indicator;
        var plain = chosen is null ? ModeGlyph(mode) : null;   // a host with no run choice: the mode's fixed glyph (a shared instance)
        if (chosen is null && plain is null) { ImGui.Dummy(new Vector2(size, size)); return; }
        var icon = chosen?.Glyph ?? plain!.Glyph;
        var color = chosen?.Color ?? plain!.Color;
        var style = ImGui.GetStyle();
        float caret = MathF.Max(5f, size * 0.22f);
        var button = new Vector2(ImGui.CalcTextSize(icon).X + style.FramePadding.X * 2f + caret + 6f, size);
        button.X = MathF.Max(button.X, size + caret + 4f);
        var pos = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.InvisibleButton("##mt_mode", button);
        IndicatorRect = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());   // ⭐ CE-4329 — a rail reads the gap to ▶ (nothing may sit between them)
        _runToAnchor = new Vector2(pos.X, pos.Y + size + 2);
        bool hovered = ImGui.IsItemHovered();
        if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        var dl = ImGui.GetWindowDrawList();
        var alpha = chosen is { } ch ? RunModeIndicator.Alpha(ch.Level, ImGui.GetTime()) : 1f;
        var ts = ImGui.CalcTextSize(icon);
        float glyphX = pos.X + (button.X - caret - 6f - ts.X) * 0.5f + 3f;
        dl.AddText(new Vector2(glyphX, pos.Y + (size - ts.Y) * 0.5f), ImGui.GetColorU32(color with { W = alpha }), icon);
        // the ▾: a small triangle (no font glyph needed), neutral grey
        float cx = pos.X + button.X - caret * 0.5f - 4f, cy = pos.Y + size * 0.55f, half = caret * 0.5f;
        dl.AddTriangleFilled(new Vector2(cx - half, cy - half * 0.5f), new Vector2(cx + half, cy - half * 0.5f), new Vector2(cx, cy + half * 0.6f), ImGui.GetColorU32(new Vector4(0.7f, 0.7f, 0.74f, 1f)));
        IconButtonChrome.DrawHoverFrame(dl, pos, button, hovered);   // ⭐ CE-4333 — the ONE icon-button chrome: transparent at rest, the white inset frame on hover (⛔ was a neutral box of its own, CE-3406)
        if (hovered)
            ImGui.SetTooltip(_choice is { } cc ? cc.IndicatorTooltip(mode == TransportMode.Review ? SecondsBehindLive() : null) : plain!.Name);

        // ⭐ CE-3413 — the menu is built when it is asked for (a click) and while it is open — never per frame (the moves, the DAR rows and the ended menu allocate)
        if (clicked && ModeMenu() is not null) ImGui.OpenPopup("##mt_mode_popup");
        if (ImGui.BeginPopup("##mt_mode_popup"))
        {
            var menu = ModeMenu();
            if (menu is not null)
            {
                ImGui.TextDisabled(menu.Header);
                ImGui.Separator();
                foreach (var item in menu.Items) DrawMenuItem(item);
            }
            ImGui.EndPopup();
        }
        DrawRunToPopup();   // ⭐ W-4 — opened by the menu's Run to … on the next frame
    }

    private static void DrawMenuItem(ModeMenuItem item)
    {
        if (item.IsSeparator) { ImGui.Separator(); return; }
        if (item.Glyph is { } glyph)   // ⭐ W-4 — a coloured glyph before the label (Colour Icons: never monochrome, R-4139)
        {
            ImGui.TextColored(item.GlyphColor ?? new Vector4(1, 1, 1, 1), glyph);
            ImGui.SameLine();
        }
        if (item.Children is { } kids)
        {
            if (ImGui.BeginMenu(item.Label, item.Enabled))
            {
                foreach (var k in kids) DrawMenuItem(k);
                ImGui.EndMenu();
            }
        }
        else if (ImGui.MenuItem(item.Label, null, item.Selected, item.Enabled))
            item.Invoke?.Invoke();
    }
}
