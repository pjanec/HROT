using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Toolkit.Orchestration;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.RunMode;
using ImGuiNET;

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

    /// <summary>F15 — how long ■ ignores presses after a review returned to live.</summary>
    public static readonly TimeSpan StopGuard = TimeSpan.FromSeconds(3);

    /// <param name="facade">THE time facade.</param>
    /// <param name="now">The clock of the F15 guard (tests inject one); default <see cref="DateTime.UtcNow"/>.</param>
    /// <param name="choice">⭐ CE-4097 (AQ108 §2.8) — the host's <see cref="RunModeChoice"/>: the indicator draws its <see cref="RunModeChoice.Indicator"/>, its menu is the
    /// choice's moves (each with its confirmation, drawn as a modal), ▶ ■ keep calling the facade. ⛔ A host that HAS one must pass it.</param>
    public MainToolbarTimeControlSection(ITimeTransportFacade facade, Func<DateTime>? now = null, RunModeChoice? choice = null)
    {
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
    public void OnPlayPause()
    {
        if (_facade.IsPlayPauseEnabled)
            _facade.TogglePlayPause();
    }

    /// <summary>
    /// Handles a Step click. Only invokes <see cref="ITimeTransportFacade.Step"/>
    /// when <see cref="ITimeTransportFacade.IsStepEnabled"/> is <c>true</c>.
    /// </summary>
    public void OnStep()
    {
        if (_facade.IsStepEnabled)
            _facade.Step();
    }

    /// <summary>
    /// Handles a Stop click. Only invokes <see cref="ITimeTransportFacade.Stop"/>
    /// when <see cref="ITimeTransportFacade.IsStopEnabled"/> is <c>true</c>.
    /// </summary>
    public void OnStop()
    {
        Observe();
        if (StopGuarded) return;   // ⭐ F15 — a second press right after the return to live must not stop the exercise
        if (_facade.IsStopEnabled)
            _facade.Stop();
    }

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

    /// <summary>DAR §9a — the indicator per mode: Edit grey pen, Preview violet half circle, Live green circle, Review amber clock-back, Replay blue play; Idle none (<see langword="null"/>).</summary>
    public static ModeIndicator? ModeGlyph(TransportMode mode) => mode switch
    {
        TransportMode.Edit    => new(Fdp.Presentation.Fonts.IconsFontAwesome6.Pen, new Vector4(0.60f, 0.60f, 0.60f, 1f), "Edit"),
        TransportMode.Preview => new(Fdp.Presentation.Fonts.IconsFontAwesome6.CircleHalfStroke, new Vector4(0.66f, 0.45f, 0.92f, 1f), "Preview"),
        TransportMode.Live    => new(Fdp.Presentation.Fonts.IconsFontAwesome6.Circle, new Vector4(0.25f, 0.80f, 0.30f, 1f), "Live"),
        TransportMode.Review  => new(Fdp.Presentation.Fonts.IconsFontAwesome6.ClockRotateLeft, new Vector4(1.00f, 0.70f, 0.20f, 1f), "Review"),
        TransportMode.Replay  => new(Fdp.Presentation.Fonts.IconsFontAwesome6.Play, new Vector4(0.30f, 0.60f, 1.00f, 1f), "Replay"),
        _ => null,
    };

    /// <summary>The mode indicator's FontAwesome 6 Solid glyph (merged into the UI font): Edit Pen, Preview CircleHalfStroke, Live Circle,
    /// Review ClockRotateLeft, Replay Play; Idle none (<see langword="null"/>).</summary>
    public static string? ModeIcon(TransportMode mode) => ModeGlyph(mode)?.Glyph;   // ONE table: the indicator's

    /// <summary>One entry of the mode menu: a label, what it does, whether it can be chosen, and (a sub-menu) its children.</summary>
    public sealed record ModeMenuItem(string Label, Action? Invoke, bool Enabled = true, IReadOnlyList<ModeMenuItem>? Children = null, bool Selected = false);

    /// <summary>The mode menu: the header line (mode + context) and the items.</summary>
    public sealed record ModeMenuModel(string Header, IReadOnlyList<ModeMenuItem> Items);

    private static readonly double[] ReviewWindows = { 30, 120, 600 };

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
        var legacy = LegacyModeMenu();
        if (_choice is null) return legacy;

        // ⭐ CE-4097 — the choice's moves (AQ108 §3: only those that start from the current state; each asks its own confirmation), after the DAR items
        var moves = _choice.Moves();
        if (moves.Count == 0) return legacy;   // a DAR keeps its own menu (restart / back to live)
        var items = new List<ModeMenuItem>();
        if (legacy is not null) items.AddRange(legacy.Items);
        foreach (var move in moves)
        {
            var target = move.Target;
            var label = move.DisabledReason is { } why ? $"{move.Label} — {why}" : move.Label;
            items.Add(new(label, () => _choice.Choose(target), move.Enabled, Selected: move.IsArmed));
        }
        var header = legacy?.Header ?? (_facade.Mode is TransportMode.Edit or TransportMode.Idle
            ? $"▶ will start: {_choice.Armed}"
            : $"{_choice.Indicator.Word} · {TransportIcons.FormatTime(_facade.TotalTime)}");
        return new(header, items);
    }

    private ModeMenuModel? LegacyModeMenu()
    {
        switch (_facade.Mode)
        {
            case TransportMode.Live:
            {
                var items = new List<ModeMenuItem>();
                foreach (var d in ReviewWindows)
                {
                    var window = d;
                    items.Add(new($"Review the last {WindowLabel(window)}",
                        () => _facade.EnterReviewAtSim(Math.Max(0, _facade.TotalTime - window))));
                }
                items.Add(new("Review from the start", () => _facade.EnterReviewAtSim(0)));
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
                return new(behind is { } b ? $"Review · {FormatSpan(b)} behind live" : "Review", items);
            }
            default:
                return null;
        }
    }

    /// <summary>What ■ looks like: the red square, or — in a review — the green ⏏ "Back to live".</summary>
    public enum StopFaceKind { Stop, BackToLive }

    /// <summary>■ in a review is ⏏ Back to live (R-3060), else ■.</summary>
    public static StopFaceKind StopFace(TransportMode mode) => mode == TransportMode.Review ? StopFaceKind.BackToLive : StopFaceKind.Stop;

    /// <summary>The ■ tooltip per DAR §9a — and, for 3 s after a return to live, why a press is ignored (F15).</summary>
    public string StopTooltip()
    {
        Observe();
        if (StopGuarded) return "Just returned to live — press again in a moment to stop the exercise";
        return _facade.Mode switch
        {
            TransportMode.Review  => SecondsBehindLive() is { } b ? $"Back to live — {FormatSpan(b)} ahead" : "Back to live",
            TransportMode.Replay  => _choice is null ? "Leave the replay" : "Back to the recording's start",
            TransportMode.Preview => "Back to edit",
            TransportMode.Live    => _choice is null ? "Stop the exercise" : "Stop the exercise and return to the scenario's start",
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
    public bool IsAxisEnabled => _facade.Mode is TransportMode.Live or TransportMode.Review or TransportMode.Replay;

    /// <summary>An axis-icon click: S ⇄ W for the whole session (R-3058).</summary>
    public void OnToggleAxis()
    {
        if (!IsAxisEnabled) return;
        _facade.SetAxis(_facade.Axis == ReplayAxis.Sim ? ReplayAxis.Wall : ReplayAxis.Sim);
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

        // ── [Play/Pause] ───────────────────────────────────────────────
        if (TransportIcons.DrawTransportButton("##mt_pp", iconSize,
                PlayPauseFace(isPaused), enabled: _facade.IsPlayPauseEnabled))
        {
            OnPlayPause();
        }
        if (_choice?.PlayDisabledReason is { } playWhy && _facade.Mode == TransportMode.Edit && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(playWhy);   // ⭐ CE-4097 §2.8 ⑥ — Live armed with no scenario session

        ImGui.SameLine();

        // ── [Step] ─────────────────────────────────────────────────────
        if (TransportIcons.DrawTransportButton("##mt_step", iconSize,
                TransportIcons.BtnShape.Step, enabled: _facade.IsStepEnabled))
        {
            OnStep();
        }

        ImGui.SameLine();

        // ── [Stop] — ⏏ Back to live in a review ────────────────────────
        var stopShape = StopFace(_facade.Mode) == StopFaceKind.BackToLive
            ? TransportIcons.BtnShape.Eject : TransportIcons.BtnShape.Stop;
        if (TransportIcons.DrawTransportButton("##mt_stop", iconSize, stopShape, enabled: _facade.IsStopEnabled))
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

        // ── [Axis] S / W ───────────────────────────────────────────────────
        ImGui.SameLine();
        ImGui.BeginDisabled(!IsAxisEnabled);
        if (ImGui.SmallButton(AxisFace(_facade.Axis) + "##mt_axis")) OnToggleAxis();
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(AxisTooltip(_facade.Axis));

        // ── ⭐ CE-4097 — the note after ■ in Live, and the one confirmation shape (R3) ─────────────────────────────────
        if (_choice?.Toast is { } toast)
        {
            ImGui.SameLine();
            if (toast.ReplayExercise is not null) { if (ImGui.SmallButton(toast.Text + "##mt_toast")) _choice.ReplayLastExercise(); }
            else ImGui.TextDisabled(toast.Text);
        }
        DrawConfirmation();
    }

    private const string ConfirmPopupId = "Confirm##mt_run_confirm";
    private RunModeConfirmation? _confirmOpened;

    /// <summary>⭐ CE-4097 — draws <see cref="RunModeChoice.Pending"/> as a modal: its text and buttons; closing it (Esc / ✕) is Cancel, the last button.</summary>
    private void DrawConfirmation()
    {
        var pending = _choice?.Pending;
        if (pending is null) { _confirmOpened = null; return; }
        if (!ReferenceEquals(_confirmOpened, pending)) { ImGui.OpenPopup(ConfirmPopupId); _confirmOpened = pending; }

        bool open = true;
        if (!ImGui.BeginPopupModal(ConfirmPopupId, ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (!open) pending.Resolve(pending.Buttons[^1]);   // (a modal that was closed without a button)
            return;
        }
        ImGui.TextWrapped(pending.Text);
        ImGui.Separator();
        RunModeConfirmation.Button? pressed = null;
        foreach (var button in pending.Buttons)
        {
            if (ImGui.Button(button.Label)) pressed = button;
            ImGui.SameLine();
        }
        if (!open) pressed = pending.Buttons[^1];
        if (pressed is not null) { ImGui.CloseCurrentPopup(); pending.Resolve(pressed); }
        ImGui.EndPopup();
    }

    /// <summary>A coloured disc with the mode's letter; a click opens the mode menu (where there is one).</summary>
    private void DrawModeIndicator(float size)
    {
        var mode = _facade.Mode;
        // ⭐ CE-4097 — with a run choice the indicator draws ITS state (dim = armed, bright = paused run, blinking = playing; the words Preview / Live / DAR / AAR)
        var chosen = _choice?.Indicator;
        var ind = chosen is { } c ? new ModeIndicator(c.Glyph, c.Color, c.Word) : ModeGlyph(mode);
        if (ind is null) { ImGui.Dummy(new Vector2(size, size)); return; }

        var pos = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.InvisibleButton("##mt_mode", new Vector2(size, size));
        bool hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var icon = chosen?.Glyph ?? ModeIcon(mode)!;   // FontAwesome 6 Solid, drawn in the mode's colour
        var ts = ImGui.CalcTextSize(icon);
        var alpha = chosen is { } ch ? RunModeIndicator.Alpha(ch.Level, ImGui.GetTime()) * (hovered ? 1f : 0.9f) : (hovered ? 1f : 0.85f);
        dl.AddText(pos + (new Vector2(size, size) - ts) * 0.5f, ImGui.GetColorU32(ind.Color with { W = alpha }), icon);
        if (hovered)
        {
            var behind = SecondsBehindLive();
            ImGui.SetTooltip(mode == TransportMode.Review && behind is { } b ? $"DAR — {FormatSpan(b)} behind live"
                           : chosen is { Level: IndicatorLevel.Dim } ? $"▶ will start: {ind.Name} (click to change)"
                           : ind.Name);
        }

        var menu = ModeMenu();
        if (clicked && menu is not null) ImGui.OpenPopup("##mt_mode_popup");
        if (ImGui.BeginPopup("##mt_mode_popup"))
        {
            if (menu is not null)
            {
                ImGui.TextDisabled(menu.Header);
                ImGui.Separator();
                foreach (var item in menu.Items) DrawMenuItem(item);
            }
            ImGui.EndPopup();
        }
    }

    private static void DrawMenuItem(ModeMenuItem item)
    {
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
