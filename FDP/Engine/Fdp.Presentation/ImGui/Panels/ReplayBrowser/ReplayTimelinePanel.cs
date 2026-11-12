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

/// <summary>⭐ B-7 part 3 — one node's recording file, as <i>File ▸ Recording info</i> lists it.</summary>
public sealed record RecordingFileInfo(int NodeId, string Path, int Frames, double WallSeconds);

/// <summary>⭐ B-7 part 3 — the loaded group: its exercise id (null when nothing is loaded) and a file per node.</summary>
public sealed record RecordingInfo(Guid? ExerciseId, IReadOnlyList<RecordingFileInfo> Files);

/// <summary>
/// Transport control panel for the replay browser.
/// The replay transport (drawn by the main toolbar's entry: history, rewind, play, rate, axis, step, the seek bar with its frame / wall / sim tooltip) and the JSON export options
/// (File ▸ JSON Export…). ⭐ B-7 part 3: the Replay Timeline WINDOW is retired — its parts moved (the readout to the seek bar's tooltip, the meta row to the Frame Diff window, the file path to File ▸ Recording info).
/// </summary>
public sealed class ReplayTimelinePanel : IReplayTransport
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
    private readonly ReplayTransportDrawer _drawer;

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
        _drawer = new ReplayTransportDrawer(this);   // ⭐ V3 — the ONE drawer, the Replay Browser's profile (everything)
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

        FrameTimes(out long wallClockTicks, out double relativeWallSec, out double simTimeSec);

        return new ReplayTimelinePanelViewModel(
            panelId, panelKind, hasRecording, currentFrame, totalFrames, wallClockTicks,
            relativeWallSec, simTimeSec, IsPlaying, PlaybackRate,
            ActiveContext?.CurrentFdpPath ?? "(none)", LoadGroupRejectionReason);
    }

    /// <summary>
    /// ⭐ B-7 part 3 — the current frame's wall ticks, wall seconds since the first frame and sim seconds, read from the active recording: ONE implementation for the panel snapshot
    /// (<see cref="BuildViewModel"/>) and the seek bar's tooltip on the current-time label. Zeros when nothing is loaded or no frame is current.
    /// </summary>
    public void FrameTimes(out long wallClockTicks, out double relativeWallSeconds, out double simSeconds)
    {
        wallClockTicks = 0; relativeWallSeconds = 0d; simSeconds = 0d;
        if (ActiveContext?.Playback == null || (ActiveContext?.CurrentFrame ?? -1) < 0) return;
        var meta = ActiveContext!.Playback!.GetFrameMetadata(ActiveContext.CurrentFrame);
        long firstFrameWallTicks = ActiveContext.Playback.GetFrameMetadata(0).WallClockTicks;
        wallClockTicks = meta.WallClockTicks;
        relativeWallSeconds = (meta.WallClockTicks - firstFrameWallTicks) / (double)TimeSpan.TicksPerSecond;
        if (ActiveContext?.SandboxRepo?.HasSingletonUnmanaged<GlobalTime>() ?? false)
            simSeconds = ActiveContext.SandboxRepo.GetSingletonUnmanagedRO<GlobalTime>().TotalTime;
    }

    /// <summary>A recording is loaded on the selected node.</summary>
    public bool HasRecording => ActiveContext?.Playback != null;

    /// <summary>⭐ B-7 part 3 — what <i>File ▸ Recording info</i> lists: the exercise, and per node its file and length.</summary>
    public RecordingInfo DescribeRecording()
    {
        if (_manager == null || _manager.Contexts.Count == 0) return new RecordingInfo(null, Array.Empty<RecordingFileInfo>());
        var files = new List<RecordingFileInfo>();
        foreach (var (node, ctx) in _manager.Contexts.OrderBy(kv => kv.Key))
        {
            int frames = ctx.Playback?.TotalFrames ?? 0;
            double seconds = 0d;
            if (frames > 1)
                seconds = (ctx.Playback!.GetFrameMetadata(frames - 1).WallClockTicks - ctx.Playback.GetFrameMetadata(0).WallClockTicks) / (double)TimeSpan.TicksPerSecond;
            files.Add(new RecordingFileInfo(node, ctx.CurrentFdpPath ?? "(none)", frames, seconds));
        }
        return new RecordingInfo(_manager.ExerciseId.Value, files);
    }

    // ── Public draw entry point ───────────────────────────────────────────

    /// <summary>The "group rejected" modal — drawn with the transport (the toolbar entry is on screen whenever the replay perspective is), since the timeline window that used to host it is retired.</summary>
    private void DrawLoadGroupError()
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
    }

    /// <summary>
    /// ⭐ CE-4261 — the replay TRANSPORT bar (history back / forward · rewind · play-pause · rate · axis · step back / forward, with the hold-to-scrub), drawn by the main toolbar's replay entry
    /// (<c>ReplayBrowserSubsystem</c> registers it) — the ONE drawer: the timeline panel no longer draws a copy. <paramref name="iconSize"/> ≤ 0 ⇒ the frame height (the editor's time control's size).
    /// </summary>
    public void DrawTransport(float iconSize = 0f)
    {
        DrawLoadGroupError();
        _drawer.Draw(iconSize);   // ⭐ V3 — the ONE drawer over this panel's IReplayTransport; ⭐ Replay Browser UX ① (R-4153) — its seek bar fills the main bar after the transport
    }

    /// <summary>⭐ Replay Browser UX ① — the recording's sim seconds at a wall tick (the host's time map, <c>ReplayTimeMap.SimAt</c>); used while
    /// <see cref="AxisIsSim"/> says the sim axis is active and <see cref="HasSimAxis"/> allows it. Null ⇒ the wall axis only.</summary>
    public Func<long, double>? SimAtWall { get; set; }

    /// <summary>⭐ B-7 part 3 — the tooltip on the current-time label: <c>frame 2 103 · wall 40.602 s · sim 39.997 s</c> (the retired timeline window's readout).</summary>
    public static string DetailText(int frame, double wallSeconds, double simSeconds)
        => (double.IsNaN(simSeconds) ? $"frame {frame:N0} · wall {wallSeconds:0.000} s" : $"frame {frame:N0} · wall {wallSeconds:0.000} s · sim {simSeconds:0.000} s").Replace(',', ' ');   // ⭐ V3 — NaN sim = the host does not know it (the editor's AAR): omitted

    /// <summary>⭐ V3 — the ONE transport drawer this panel draws with (the Replay Browser's profile: everything). A probe for the rails.</summary>
    public ReplayTransportDrawer TransportDrawer => _drawer;

    /// <summary>The seek bar's least width; below it the bar is not drawn (a narrow window keeps the transport).</summary>
    public const float SeekBarMinWidth = ReplayTransportDrawer.SeekBarMinWidth;

    /// <summary>The tooltip on the seek bar's current-time label (frame · wall · sim), as last built — for the rails.</summary>
    public string SeekDetailText => _drawer.SeekDetailText;

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
    public (System.Numerics.Vector2 Min, System.Numerics.Vector2 Max, float Fraction, string Now, string End)? LastSeekBar => _drawer.LastSeekBar;

    private bool SimAxisActive => SimAtWall != null && (AxisIsSim?.Invoke() ?? false) && (HasSimAxis?.Invoke() ?? true);

    // ── ⭐ V3 — the Replay Browser's IReplayTransport: over its federated manager, today's behaviour unchanged ──
    int IReplayTransport.CurrentFrame => ActiveContext?.CurrentFrame ?? -1;
    int IReplayTransport.TotalFrames => ActiveContext?.Playback?.TotalFrames ?? 0;
    void IReplayTransport.Seek(int frame) => SeekToFrame(frame);
    void IReplayTransport.Step(int delta) { if (delta > 0) StepForward(); else if (delta < 0) StepBackward(); }
    bool IReplayTransport.IsPlaying => IsPlaying;
    void IReplayTransport.Play() => IsPlaying = true;
    void IReplayTransport.Pause() => IsPlaying = false;
    float IReplayTransport.Rate { get => PlaybackRate; set => PlaybackRate = value; }
    bool IReplayTransport.CanPlay => IsPlayEnabled(HasRecording);
    string? IReplayTransport.PlayDisabledReason => null;   // ⭐ G-7 S4 (M-7): ▶ plays in Merged too — ⛔ SUPERSEDED: "Continuous playback is disabled in Merged View" (the merge was a JSON round trip)
    PlaybackHistoryTracker IReplayTransport.Waypoints => _playbackHistory;
    bool IReplayTransport.HasAxisToggle => AxisIsSim != null && ToggleAxis != null;
    bool IReplayTransport.AxisIsSim => AxisIsSim?.Invoke() ?? false;
    bool IReplayTransport.HasSimAxis => HasSimAxis?.Invoke() ?? true;
    bool IReplayTransport.SimAxisActive => SimAxisActive;
    void IReplayTransport.ToggleAxis() => ToggleAxis?.Invoke();

    /// <summary>"0:39.9" / "1:02:05.3" — the seek bar's time.</summary>
    public static string SeekTime(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0d, seconds));
        int tenths = t.Milliseconds / 100;
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{tenths}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}.{tenths}";
    }

    /// <summary>The frame at <paramref name="fraction"/> (0..1) of the bar.</summary>
    public static int FrameAt(float fraction, int maxFrame) => (int)Math.Round(Math.Clamp(fraction, 0f, 1f) * Math.Max(0, maxFrame));

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

    internal static bool IsPlayEnabled(bool hasRecording) => hasRecording;

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
