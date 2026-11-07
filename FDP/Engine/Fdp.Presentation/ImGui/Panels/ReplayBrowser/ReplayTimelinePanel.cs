using System;
using NodeEditor.Core.Widgets;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Icons;
using Fdp.Toolkit.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser.Federation;
using ImGuiNET;

namespace Fdp.Presentation.Panels.ReplayBrowser;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — the whole of what <see cref="ReplayTimelinePanel"/> shows, this frame.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example. ⭐ Mirrors <see cref="ReplayTimelinePanel.DrawRow2_TimeInfo"/>/
/// <see cref="ReplayTimelinePanel.DrawRow3_Slider"/>'s own math by hand — kept in sync since it reads private state.
/// </summary>
public sealed record ReplayTimelinePanelViewModel(
    string PanelId,
    string PanelKind,
    bool HasRecording,
    int CurrentFrame,
    int TotalFrames,
    long WallClockTicks,
    double RelativeWallSeconds,
    double SimTimeSeconds,
    bool IsPlaying,
    float PlaybackRate,
    string CurrentFdpPath,
    string? LoadGroupRejectionReason) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// Transport control panel for the replay browser.
/// Renders timeline controls, frame metadata, file loading, and JSON export options.
/// </summary>
public sealed class ReplayTimelinePanel
{
    private FederatedReplayManager? _manager;
    private readonly Func<int> _getSelectedNodeId;
    private readonly IRecordingExportService _exportService;
    private readonly IFileDialogService _fileDialogService;
    private readonly PlaybackHistoryTracker _playbackHistory;
    private readonly InspectorState _inspectorState;

    /// <summary>Returns the active context for the currently selected node, or null if not available.</summary>
    private ReplayBrowserContext? ActiveContext =>
        _manager != null && _manager.Contexts.TryGetValue(_getSelectedNodeId(), out var ctx) ? ctx : null;

    private JsonExportOptions _options = new();
    private bool _isExporting;
    public bool IsPlaying { get; set; }
    public float PlaybackRate { get; set; } = 1.0f;
    private static readonly float[] TimeRates = { 0.1f, 0.5f, 1.0f, 1.5f, 2.0f, 5.0f, 10.0f };
    private float _stepHoldTime;
    private int _stepHoldDirection;
    private float _autoStepAccumulator;

    /// <summary>
    /// Fired when the user selects an entity via the timeline panel
    /// (e.g. via a filter drop-down or entity link in metadata).
    /// </summary>
    public Action<Entity>? OnEntitySelected { get; set; }

    /// <summary>
    /// Called when the user confirms file selection. Receives the selected paths.
    /// Returns a rejection reason string, or null on success.
    /// </summary>
    public Func<string[], string?>? OnLoadGroup { get; set; }

    /// <summary>
    /// After a rejected LoadGroup call, holds the rejection reason shown in a modal.
    /// Cleared when the modal is dismissed. Exposed for testing.
    /// </summary>
    internal string? LoadGroupRejectionReason { get; private set; }

    /// <summary>Returns true when the active view is Merged View.</summary>
    public Func<bool>? IsMergedViewQuery { get; set; }

    /// <summary>⭐ <c>CE-3330</c> T13 — whether the viewer's axis is Sim (the host's; null = no axis toggle shown).</summary>
    public Func<bool>? AxisIsSim { get; set; }

    /// <summary>⭐ <c>CE-3330</c> T13 — flips the viewer's axis S ⇄ W.</summary>
    public Action? ToggleAxis { get; set; }

    /// <summary>⭐ <c>CE-3330</c> T13 — whether the loaded group has a sim axis at all (a v6 recording has not: the toggle is disabled).</summary>
    public Func<bool>? HasSimAxis { get; set; }

    public ReplayTimelinePanel(
        FederatedReplayManager? manager,
        Func<int> getSelectedNodeId,
        IRecordingExportService exportService,
        IFileDialogService fileDialogService,
        PlaybackHistoryTracker playbackHistory,
        InspectorState inspectorState)
    {
        _manager = manager;
        _getSelectedNodeId = getSelectedNodeId;
        _exportService = exportService;
        _fileDialogService = fileDialogService;
        _playbackHistory = playbackHistory;
        _inspectorState = inspectorState;
    }

    /// <summary>Binds a new manager after a successful group load; <see langword="null"/> when the replay is closed (RB-1, <c>POST /replay/stop</c>).</summary>
    public void SetManager(FederatedReplayManager? manager) { _manager = manager; }

    // ── Public BUILD entry point (U-obs-5) ───────────────────────────────
    /// <summary>⭐⭐⭐ BUILD — a pure projection of frame/timing state. No ImGui. ⭐ Reuses the SAME
    /// null/negative-frame guards <see cref="DrawRow2_TimeInfo"/> and <see cref="DrawRow3_Slider"/>
    /// apply, by hand.</summary>
    public ReplayTimelinePanelViewModel BuildViewModel(string panelId, string panelKind)
    {
        bool hasRecording = ActiveContext?.Playback != null;
        int currentFrame = (ActiveContext?.CurrentFrame ?? -1) < 0 ? 0 : ActiveContext!.CurrentFrame;
        int totalFrames = ActiveContext?.Playback?.TotalFrames ?? 1;

        long wallClockTicks = 0;
        double relativeWallSec = 0.0;
        double simTimeSec = 0.0;
        if (hasRecording && (ActiveContext?.CurrentFrame ?? -1) >= 0)
        {
            var meta = ActiveContext!.Playback!.GetFrameMetadata(ActiveContext.CurrentFrame);
            long firstFrameWallTicks = ActiveContext.Playback.GetFrameMetadata(0).WallClockTicks;
            wallClockTicks = meta.WallClockTicks;
            relativeWallSec = (meta.WallClockTicks - firstFrameWallTicks) / (double)TimeSpan.TicksPerSecond;
            if (ActiveContext?.SandboxRepo?.HasSingletonUnmanaged<GlobalTime>() ?? false)
                simTimeSec = ActiveContext.SandboxRepo.GetSingletonUnmanaged<GlobalTime>().TotalTime;
        }

        return new ReplayTimelinePanelViewModel(
            panelId, panelKind, hasRecording, currentFrame, totalFrames, wallClockTicks,
            relativeWallSec, simTimeSec, IsPlaying, PlaybackRate,
            ActiveContext?.CurrentFdpPath ?? "(none)", LoadGroupRejectionReason);
    }

    // ── Public draw entry point ───────────────────────────────────────────

    public void DrawContent()
    {
        if (LoadGroupRejectionReason != null)
        {
            Gui.OpenPopup("LoadGroupError");
        }
        if (Gui.BeginPopupModal("LoadGroupError", ImGuiWindowFlags.AlwaysAutoResize))
        {
            Gui.TextWrapped(LoadGroupRejectionReason ?? string.Empty);
            if (Gui.Button("OK"))
            {
                LoadGroupRejectionReason = null;
                Gui.CloseCurrentPopup();
            }
            Gui.EndPopup();
        }
        // ⭐ CE-4261 — the transport bar is NOT drawn here any more: the browser registers it as an entry of the MAIN TOOLBAR (DrawTransport, below), the one drawer
        DrawRow2_TimeInfo();
        DrawRow3_Slider();
        DrawRow4_Meta();
        DrawRow5_FileLoader();
        // ⭐ CE-4261 — the JSON Export options moved to File ▸ JSON Export… (JsonExportWindow draws DrawExportBody); the Load .fdp… button to File ▸ Load .fdp…
    }

    /// <summary>
    /// ⭐ CE-4261 — the replay TRANSPORT bar (history back / forward · rewind · play-pause · rate · axis · step back / forward, with the hold-to-scrub), drawn by the main toolbar's replay entry
    /// (<c>ReplayBrowserSubsystem</c> registers it) — the ONE drawer: the timeline panel no longer draws a copy. <paramref name="iconSize"/> ≤ 0 ⇒ the frame height (the editor's time control's size).
    /// </summary>
    public void DrawTransport(float iconSize = 0f)
    {
        DrawRow1_Transport(iconSize);
        DrawSeekBar();   // ⭐ Replay Browser UX ① (R-4153) — the seek bar fills the main bar after the transport
    }

    /// <summary>⭐ Replay Browser UX ① — the recording's sim seconds at a wall tick (the host's time map, <c>ReplayTimeMap.SimAt</c>); used while
    /// <see cref="AxisIsSim"/> says the sim axis is active and <see cref="HasSimAxis"/> allows it. Null ⇒ the wall axis only.</summary>
    public Func<long, double>? SimAtWall { get; set; }

    /// <summary>The seek bar's least width; below it the bar is not drawn (a narrow window keeps the transport).</summary>
    public const float SeekBarMinWidth = 80f;

    private int _seekLabelFrame = -1, _seekEndFrames = -1, _seekHoverFrame = -1;
    private bool _seekLabelsSim;
    private string _seekNow = "", _seekEnd = "", _seekHover = "";

    /// <summary>The seconds of <paramref name="frame"/> on the active axis.</summary>
    public double SecondsOf(int frame)
    {
        var pb = ActiveContext?.Playback;
        if (pb == null || pb.TotalFrames <= 0) return 0d;
        frame = Math.Clamp(frame, 0, pb.TotalFrames - 1);
        long wall = pb.GetFrameMetadata(frame).WallClockTicks, first = pb.GetFrameMetadata(0).WallClockTicks;
        if (SimAxisActive) return SimAtWall!(wall) - SimAtWall!(first);
        return (wall - first) / (double)TimeSpan.TicksPerSecond;
    }

    /// <summary>⭐ A probe for the frame rails (R-4138: layout is measured): the seek bar's screen rect and the playhead's fraction as last drawn; null when not drawn.</summary>
    public (System.Numerics.Vector2 Min, System.Numerics.Vector2 Max, float Fraction, string Now, string End)? LastSeekBar { get; private set; }

    private bool SimAxisActive => SimAtWall != null && (AxisIsSim?.Invoke() ?? false) && (HasSimAxis?.Invoke() ?? true);

    /// <summary>"0:39.9" / "1:02:05.3" — the seek bar's time.</summary>
    public static string SeekTime(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0d, seconds));
        int tenths = t.Milliseconds / 100;
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{tenths}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}.{tenths}";
    }

    /// <summary>The frame at <paramref name="fraction"/> (0..1) of the bar.</summary>
    public static int FrameAt(float fraction, int maxFrame) => (int)Math.Round(Math.Clamp(fraction, 0f, 1f) * Math.Max(0, maxFrame));

    /// <summary>⭐ R-4153 / R-4155 — the main bar's seek bar: the current time, the bar (drag = scrub, click = jump, hover = "0:50.1 · frame 2 640"), the end time.
    /// ⛔ No ticks (the events are in the Event Timeline). Labels are cached per frame, so a steady frame allocates nothing.</summary>
    private void DrawSeekBar()
    {
        LastSeekBar = null;
        var pb = ActiveContext?.Playback;
        if (pb == null || pb.TotalFrames <= 0) return;
        int max = pb.TotalFrames - 1;
        int current = Math.Clamp(ActiveContext!.CurrentFrame, 0, max);
        bool sim = SimAxisActive;
        if (sim != _seekLabelsSim) { _seekLabelsSim = sim; _seekLabelFrame = _seekEndFrames = _seekHoverFrame = -1; }   // the axis toggled: relabel
        if (current != _seekLabelFrame) { _seekLabelFrame = current; _seekNow = SeekTime(SecondsOf(current)); }
        if (pb.TotalFrames != _seekEndFrames) { _seekEndFrames = pb.TotalFrames; _seekEnd = SeekTime(SecondsOf(max)); }

        var style = Gui.GetStyle();
        float endW = Gui.CalcTextSize(_seekEnd).X;
        Gui.SameLine(0f, style.ItemSpacing.X * 2f);
        float avail = Gui.GetContentRegionAvail().X;
        float nowW = Gui.CalcTextSize(_seekNow).X;
        float barW = avail - nowW - endW - style.ItemSpacing.X * 4f;
        if (barW < SeekBarMinWidth) return;

        Gui.AlignTextToFramePadding();
        Gui.TextUnformatted(_seekNow);
        Gui.SameLine();
        float h = Gui.GetFrameHeight();
        var p0 = Gui.GetCursorScreenPos();
        Gui.InvisibleButton("##rb_seekbar", new System.Numerics.Vector2(barW, h));
        bool hovered = Gui.IsItemHovered(), active = Gui.IsItemActive();
        float fx = Gui.GetIO().MousePos.X;
        if (active)
        {
            int target = FrameAt((fx - p0.X) / barW, max);
            if (target != current) { IsPlaying = false; SeekToFrame(target); current = target; }
        }

        var dl = Gui.GetWindowDrawList();
        float cy = p0.Y + h * 0.5f, track = 4f, knob = 6f;
        float px = p0.X + (max > 0 ? (float)current / max : 0f) * barW;
        dl.AddRectFilled(new System.Numerics.Vector2(p0.X, cy - track * 0.5f), new System.Numerics.Vector2(p0.X + barW, cy + track * 0.5f), Gui.GetColorU32(ImGuiCol.FrameBg), track * 0.5f);
        dl.AddRectFilled(new System.Numerics.Vector2(p0.X, cy - track * 0.5f), new System.Numerics.Vector2(px, cy + track * 0.5f), Gui.GetColorU32(ImGuiCol.SliderGrabActive), track * 0.5f);
        dl.AddCircleFilled(new System.Numerics.Vector2(px, cy), hovered || active ? knob + 1f : knob, Gui.GetColorU32(ImGuiCol.Text));
        LastSeekBar = (p0, new System.Numerics.Vector2(p0.X + barW, p0.Y + h), max > 0 ? (float)current / max : 0f, _seekNow, _seekEnd);

        if (hovered && !active)
        {
            int at = FrameAt((fx - p0.X) / barW, max);
            if (at != _seekHoverFrame) { _seekHoverFrame = at; _seekHover = $"{SeekTime(SecondsOf(at))} · frame {at:N0}".Replace(',', ' '); }
            Gui.SetTooltip(_seekHover);
        }
        Gui.SameLine();
        Gui.AlignTextToFramePadding();
        Gui.TextDisabled(_seekEnd);
    }

    /// <summary>⭐ CE-4261 — File ▸ Load .fdp…: the same load the button ran (the file dialog, then <see cref="OnLoadGroup"/>).</summary>
    public void RequestLoadFdp() => _ = LoadFdpAsync();

    /// <summary>⭐ CE-4261 — the JSON export options and the Save button (what the tree node held), drawn by <c>JsonExportWindow</c>; the export runs through <see cref="SaveAsync"/> — the one path.</summary>
    public void DrawExportBody()
    {
        DrawExportModeRadios();
        DrawFrameInputs();
        DrawTimeInputs();
        Gui.Separator();
        DrawFormatOptions();
        DrawFilterOptions();
        Gui.Separator();
        DrawPayloadOptions();
        Gui.Spacing();
        DrawSaveButton();
    }

    // ── Row implementations ───────────────────────────────────────────────

    private void DrawRow1_Transport(float iconSize = 0f)
    {
        if (iconSize <= 0f) iconSize = Gui.GetFrameHeight();
        float dt = Gui.GetIO().DeltaTime;
        bool hasRecording = ActiveContext?.Playback != null;

        if (TransportIconRenderer.DrawButton("##rb_hist_back", iconSize, TransportShape.HistoryBack, _playbackHistory.CanGoBack, out _, out _))
            _playbackHistory.GoBack();
        if (Gui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled | ImGuiHoveredFlags.DelayNormal))
            Gui.SetTooltip("Navigate backward in selection history");

        Gui.SameLine();
        if (TransportIconRenderer.DrawButton("##rb_hist_fwd", iconSize, TransportShape.HistoryFwd, _playbackHistory.CanGoForward, out _, out _))
            _playbackHistory.GoForward();
        if (Gui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled | ImGuiHoveredFlags.DelayNormal))
            Gui.SetTooltip("Navigate forward in selection history");

        Gui.SameLine();
        if (TransportIconRenderer.DrawButton("##rb_rewind", iconSize, TransportShape.Rewind, hasRecording, out _, out _))
        {
            IsPlaying = false;
            SeekToFirst();
        }
        if (Gui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled | ImGuiHoveredFlags.DelayNormal))
            Gui.SetTooltip("Rewind to start");

        Gui.SameLine();
        TransportShape playPauseShape = IsPlaying ? TransportShape.Pause : TransportShape.Play;
        bool isMerged = IsMergedViewQuery?.Invoke() ?? false;
        bool playEnabled = IsPlayEnabled(hasRecording, isMerged);
        if (!playEnabled) Gui.BeginDisabled();
        if (TransportIconRenderer.DrawButton("##rb_play_pause", iconSize, playPauseShape, playEnabled, out _, out _))
        {
            IsPlaying = !IsPlaying;
        }
        if (!playEnabled) Gui.EndDisabled();
        if (Gui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled | ImGuiHoveredFlags.DelayNormal))
        {
            if (isMerged)
                Gui.SetTooltip("Continuous playback is disabled in Merged View. Use Step-Forward/Backward or the timeline slider.");
            else
                Gui.SetTooltip(IsPlaying ? "Pause playback" : "Start playback");
        }

        Gui.SameLine();
        float comboOffsetY = (iconSize - Gui.GetFrameHeight()) * 0.5f;
        Gui.SetCursorPosY(Gui.GetCursorPosY() + comboOffsetY);
        Gui.SetNextItemWidth(60f);
        if (Gui.BeginCombo("##playback_rate", $"{PlaybackRate:F1}x"))
        {
            foreach (float rate in TimeRates)
            {
                bool isSelected = Math.Abs(PlaybackRate - rate) < 0.01f;
                if (Gui.Selectable($"{rate:F1}x", isSelected))
                    PlaybackRate = rate;
            }
            Gui.EndCombo();
        }
        if (Gui.IsItemHovered(ImGuiHoveredFlags.DelayNormal))
            Gui.SetTooltip("Set playback speed");
        Gui.SetCursorPosY(Gui.GetCursorPosY() - comboOffsetY);

        // ⭐ CE-3330 T13 (DAR §9c, row 34; R-3058) — the viewer's time axis, one small toggle: S = sim (pauses collapsed), W = wall
        if (AxisIsSim != null && ToggleAxis != null)
        {
            Gui.SameLine();
            Gui.SetCursorPosY(Gui.GetCursorPosY() + comboOffsetY);
            bool sim = AxisIsSim();
            bool can = HasSimAxis?.Invoke() ?? true;
            if (!can) Gui.BeginDisabled();
            if (Gui.SmallButton(sim && can ? "S##rb_axis" : "W##rb_axis")) ToggleAxis();
            if (!can) Gui.EndDisabled();
            if (Gui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled | ImGuiHoveredFlags.DelayNormal))
                Gui.SetTooltip(!can ? "Wall time - this recording has no sim time (format v6)"
                             : sim ? "Sim time - pauses skipped (click: wall time)" : "Wall time - pauses shown (click: sim time)");
            Gui.SetCursorPosY(Gui.GetCursorPosY() - comboOffsetY);
        }

        Gui.SameLine();
        TransportIconRenderer.DrawButton("##rb_step_back", iconSize, TransportShape.StepBack, hasRecording, out bool stepBackHeld, out bool stepBackActivated);
        if (Gui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled | ImGuiHoveredFlags.DelayNormal))
            Gui.SetTooltip("Step backward (hold to scrub)");
        if (stepBackActivated)
        {
            IsPlaying = false;
            StepBackward();
            _stepHoldTime = 0f;
            _stepHoldDirection = -1;
            _autoStepAccumulator = 0f;
        }

        Gui.SameLine();
        TransportIconRenderer.DrawButton("##rb_step_fwd", iconSize, TransportShape.StepFwd, hasRecording, out bool stepFwdHeld, out bool stepFwdActivated);
        if (Gui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled | ImGuiHoveredFlags.DelayNormal))
            Gui.SetTooltip("Step forward (hold to scrub)");
        if (stepFwdActivated)
        {
            IsPlaying = false;
            StepForward();
            _stepHoldTime = 0f;
            _stepHoldDirection = 1;
            _autoStepAccumulator = 0f;
        }

        bool autoBackHeld = stepBackHeld && !stepFwdHeld;
        bool autoFwdHeld = stepFwdHeld && !stepBackHeld;

        if (hasRecording && (autoBackHeld || autoFwdHeld))
        {
            _stepHoldDirection = autoBackHeld ? -1 : 1;
            _stepHoldTime += dt;

            const float holdDebounceSec = 0.30f;
            const float holdRampSec = 2.00f;

            if (_stepHoldTime > holdDebounceSec)
            {
                IsPlaying = false;

                float rampProgress = Math.Clamp((_stepHoldTime - holdDebounceSec) / holdRampSec, 0f, 1f);
                float currentRate = 1.0f + 9.0f * rampProgress;

                _autoStepAccumulator += dt * currentRate;
                float frameTime = 1.0f / 60.0f;

                int framesToStep = 0;
                while (_autoStepAccumulator >= frameTime)
                {
                    _autoStepAccumulator -= frameTime;
                    framesToStep++;
                }

                if (framesToStep > 0)
                {
                    if (_stepHoldDirection > 0)
                    {
                        for (int i = 0; i < framesToStep; i++)
                        {
                            StepForward();
                        }
                    }
                    else
                    {
                        int currentFrame = ActiveContext?.CurrentFrame ?? 0;
                        int targetFrame = Math.Max(0, currentFrame - framesToStep);
                        if (targetFrame < currentFrame)
                            SeekToFrame(targetFrame);
                    }
                }
            }
        }
        else
        {
            _stepHoldTime = 0f;
            _stepHoldDirection = 0;
            _autoStepAccumulator = 0f;
        }
    }

    private void DrawRow2_TimeInfo()
    {
        if (ActiveContext?.Playback == null || (ActiveContext?.CurrentFrame ?? -1) < 0)
        {
            Gui.TextDisabled("Frame: - | Wall Ticks: - | Wall Time: - | Sim Time: -");
            return;
        }

        int current = ActiveContext!.CurrentFrame;
        var meta = ActiveContext.Playback.GetFrameMetadata(current);
        long firstFrameWallTicks = ActiveContext.Playback.GetFrameMetadata(0).WallClockTicks;

        double relativeWallSec =
            (meta.WallClockTicks - firstFrameWallTicks) / (double)TimeSpan.TicksPerSecond;

        double simTimeSec = 0.0;
        if (ActiveContext?.SandboxRepo?.HasSingletonUnmanaged<GlobalTime>() ?? false)
            simTimeSec = ActiveContext.SandboxRepo.GetSingletonUnmanaged<GlobalTime>().TotalTime;

        Gui.TextUnformatted(
            $"Frame: {current} | Wall Ticks: {meta.WallClockTicks} | Wall Time: {relativeWallSec:F3}s | Sim Time: {simTimeSec:F3}s");
    }

    private void DrawRow3_Slider()
    {
        int totalFrames = ActiveContext?.Playback?.TotalFrames ?? 1;
        int current = (ActiveContext?.CurrentFrame ?? -1) < 0 ? 0 : ActiveContext!.CurrentFrame;
        int max = totalFrames > 0 ? totalFrames - 1 : 0;

        if (Gui.SliderInt("##timeline", ref current, 0, max))
        {
            IsPlaying = false;
            SeekToFrame(current);
        }
        Gui.SameLine();
        Gui.TextUnformatted($"Frame {current} / {max}");
    }
                                                                
    private void DrawRow4_Meta()
    {
        // Metadata display (tick, simframe, simtime, frame type, size)
        // When no recording is loaded, show dashes.
        if (ActiveContext?.Playback == null)
        {
            Gui.TextDisabled("Tick: - | SimFrame: - | SimTime: - | Type: - | Size: -");
        }
        else
        {
            Gui.TextDisabled(
                $"Tick: - | SimFrame: - | SimTime: - | FrameType: - | CompressedSize: -");
        }
    }

    private void DrawRow5_FileLoader()
    {
        // ⭐ CE-4261 — the Load button is File ▸ Load .fdp… now; the row keeps the loaded path
        string displayPath = ActiveContext?.CurrentFdpPath ?? "(none)";
        Gui.TextUnformatted(displayPath);
    }

    // ── Export expander sub-sections ─────────────────────────────────────

    private static readonly string[] ExportLabels = { "Start Frame", "End Frame", "Start Time (s)", "End Time (s)", "Filter by Entity Index", "Entity Index", "Include Entities", "Include Events", "Minified Output", "Epsilon" };

    private void DrawExportModeRadios()
    {
        Gui.TextDisabled("Export Range");
        int mode = (int)_options.WindowMode;
        if (Gui.RadioButton("Full File", ref mode, (int)ExportWindowMode.FullFile))
            _options.WindowMode = ExportWindowMode.FullFile;
        Gui.SameLine();
        if (Gui.RadioButton("By Frame", ref mode, (int)ExportWindowMode.ByFrame))
            _options.WindowMode = ExportWindowMode.ByFrame;
        Gui.SameLine();
        if (Gui.RadioButton("By Time", ref mode, (int)ExportWindowMode.ByTime))
            _options.WindowMode = ExportWindowMode.ByTime;
    }

    private void DrawFrameInputs()
    {
        if (GetDisabledFrameInputs(_options.WindowMode)) Gui.BeginDisabled();
        if (FormGrid.Begin("replay.export", ExportLabels))
        {
            FormGrid.Row("Start Frame"); Gui.InputInt("##startframe", ref _options.StartFrame);
            FormGrid.Row("End Frame");   Gui.InputInt("##endframe", ref _options.EndFrame);
            FormGrid.End();
        }
        if (GetDisabledFrameInputs(_options.WindowMode)) Gui.EndDisabled();
    }

    private void DrawTimeInputs()
    {
        if (GetDisabledTimeInputs(_options.WindowMode)) Gui.BeginDisabled();
        if (FormGrid.Begin("replay.export", ExportLabels))
        {
            FormGrid.Row("Start Time (s)"); Gui.InputFloat("##starttime", ref _options.StartTimeSec);
            FormGrid.Row("End Time (s)");   Gui.InputFloat("##endtime", ref _options.EndTimeSec);
            FormGrid.End();
        }
        if (GetDisabledTimeInputs(_options.WindowMode)) Gui.EndDisabled();
    }

    private void DrawFormatOptions()
    {
        Gui.TextDisabled("Format");
        int fmt = (int)_options.FormatMode;
        if (Gui.RadioButton("Incremental (Compact)", ref fmt, (int)ExportFormatMode.Incremental))
            _options.FormatMode = ExportFormatMode.Incremental;
        Gui.SameLine();
        if (Gui.RadioButton("Absolute State", ref fmt, (int)ExportFormatMode.AbsoluteState))
            _options.FormatMode = ExportFormatMode.AbsoluteState;
        Gui.SameLine();
        if (Gui.RadioButton("Changelog (Verbose)", ref fmt, (int)ExportFormatMode.Changelog))
            _options.FormatMode = ExportFormatMode.Changelog;
    }

    private void DrawFilterOptions()
    {
        Gui.TextDisabled("Filters");
        if (FormGrid.Begin("replay.export", ExportLabels))
        {
            FormGrid.Row("Filter by Entity Index"); Gui.Checkbox("##filterentity", ref _options.FilterByEntityIndex);
            if (!_options.FilterByEntityIndex) Gui.BeginDisabled();
            FormGrid.Row("Entity Index"); Gui.InputInt("##entityindex", ref _options.TargetEntityIndex);
            if (!_options.FilterByEntityIndex) Gui.EndDisabled();
            FormGrid.End();
        }
        Gui.Spacing();
        Gui.TextDisabled("Export Scope");
        int scope = _options.FilterBySelection ? 1 : 0;
        if (Gui.RadioButton("All Entities", ref scope, 0))
            _options.FilterBySelection = false;
        Gui.SameLine();
        if (Gui.RadioButton("Selected Entity Only", ref scope, 1))
            _options.FilterBySelection = true;
    }

    private void DrawPayloadOptions()
    {
        Gui.TextDisabled("Payload & Format");
        bool epsilonDisabled = _options.FormatMode == ExportFormatMode.AbsoluteState;
        double eps = _options.EpsilonTolerance;
        if (FormGrid.Begin("replay.export", ExportLabels))
        {
            FormGrid.Row("Include Entities"); Gui.Checkbox("##includeentities", ref _options.IncludeEntities);
            FormGrid.Row("Include Events");   Gui.Checkbox("##includeevents", ref _options.IncludeEvents);
            FormGrid.Row("Minified Output");  Gui.Checkbox("##minified", ref _options.Minified);

            if (epsilonDisabled) Gui.BeginDisabled();
            FormGrid.Row("Epsilon");
            if (Gui.InputDouble("##epsilon", ref eps))
                _options.EpsilonTolerance = eps;
            if (epsilonDisabled) Gui.EndDisabled();
            FormGrid.End();
        }
    }

    private void DrawSaveButton()
    {
        bool canSave = !_isExporting && ActiveContext?.CurrentFdpPath != null;
        if (!canSave) Gui.BeginDisabled();
        if (Gui.Button("Save to JSON..."))
        {
            _options.TargetEntities.Clear();

            if (_options.FilterBySelection)
            {
                if (_inspectorState.SelectedEntity.HasValue)
                    _options.TargetEntities.Add(_inspectorState.SelectedEntity.Value);
            }

            var snapshot = CloneOptions(_options);
            _ = SaveAsync(snapshot, ActiveContext?.CurrentFdpPath);
        }
        if (!canSave) Gui.EndDisabled();

        if (_isExporting) Gui.TextDisabled("Exporting...");
    }

    // ── Async helpers ─────────────────────────────────────────────────────

    internal async Task LoadFdpAsync()
    {
        var paths = await _fileDialogService.ShowOpenMultipleFilesDialogAsync(
            "ReplayBrowser_LoadRecording", "*.fdp");
        if (paths == null || paths.Length == 0) return;

        _playbackHistory.Clear();
        _inspectorState.SelectedEntity = null;
        IsPlaying = false;

        if (OnLoadGroup != null)
        {
            string? rejection = OnLoadGroup(paths);
            if (rejection != null)
                LoadGroupRejectionReason = rejection;
        }
        // No fallback: file loading always goes through OnLoadGroup
    }

    internal static bool IsPlayEnabled(bool hasRecording, bool isMergedView)
        => hasRecording && !isMergedView;

    private async Task SaveAsync(JsonExportOptions snapshot, string? inputPath)
    {
        _isExporting = true;
        try
        {
            string? outPath = await _fileDialogService.ShowSaveAsDialogAsync("ReplayBrowser_ExportJson", "dump.json", "*.json");
            if (string.IsNullOrEmpty(outPath) || inputPath == null) return;

            await Task.Factory.StartNew(
                () => _exportService.ExportToJson(inputPath, outPath, snapshot),
                TaskCreationOptions.LongRunning);
        }
        finally
        {
            _isExporting = false;
        }
    }

    private void SeekToFirst()
    {
        if (ActiveContext?.Playback != null)
            SeekToFrame(0);
    }

    // ── Manager-driven navigation ─────────────────────────────────────────

    private void SeekToFrame(int frame)
    {
        if (_manager == null || ActiveContext?.Playback == null) return;
        long wallTicks = ActiveContext.Playback.GetFrameMetadata(frame).WallClockTicks;
        _manager.SetBaseWallTicks(wallTicks);
    }

    private void StepForward()
    {
        var ctx = ActiveContext;
        if (ctx == null || ctx.Playback == null || _manager == null) return;
        int nextFrame = ctx.CurrentFrame + 1;
        if (nextFrame >= ctx.Playback.TotalFrames) return;
        _manager.StepForwardAll();
    }

    private void StepBackward()
    {
        var ctx = ActiveContext;
        if (ctx == null || ctx.Playback == null || _manager == null) return;
        int prevFrame = ctx.CurrentFrame - 1;
        if (prevFrame < 0) return;
        _manager.StepBackwardAll();
    }

    // ── Test seams ────────────────────────────────────────────────────────

    internal void SeekToFrameForTest(int frame) => SeekToFrame(frame);
    internal void StepForwardForTest() => StepForward();
    internal void StepBackwardForTest() => StepBackward();

    // ── Testable helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Deep-clones a <see cref="JsonExportOptions"/> instance.
    /// Modifying the original after cloning does not affect the returned snapshot.
    /// </summary>
    internal static JsonExportOptions CloneOptions(JsonExportOptions source) => new()
    {
        WindowMode         = source.WindowMode,
        FormatMode         = source.FormatMode,
        StartFrame         = source.StartFrame,
        EndFrame           = source.EndFrame,
        StartTimeSec       = source.StartTimeSec,
        EndTimeSec         = source.EndTimeSec,
        FilterBySelection  = source.FilterBySelection,
        TargetEntities     = new List<Entity>(source.TargetEntities),
        FilterByEntityIndex = source.FilterByEntityIndex,
        TargetEntityIndex  = source.TargetEntityIndex,
        IncludeEntities    = source.IncludeEntities,
        IncludeEvents      = source.IncludeEvents,
        Minified           = source.Minified,
        EpsilonTolerance   = source.EpsilonTolerance,
    };

    /// <summary>
    /// Returns true when the frame-range inputs should be disabled for
    /// <paramref name="mode"/>.  Disabled for FullFile and ByTime.
    /// </summary>
    internal static bool GetDisabledFrameInputs(ExportWindowMode mode)
        => mode != ExportWindowMode.ByFrame;

    /// <summary>
    /// Returns true when the time-range inputs should be disabled for
    /// <paramref name="mode"/>.  Disabled for FullFile and ByFrame.
    /// </summary>
    internal static bool GetDisabledTimeInputs(ExportWindowMode mode)
        => mode != ExportWindowMode.ByTime;
}
