using System;
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

    /// <summary>Binds a new manager after a successful group load.</summary>
    public void SetManager(FederatedReplayManager manager) { _manager = manager; }

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
        DrawRow1_Transport();
        DrawRow2_TimeInfo();
        DrawRow3_Slider();
        DrawRow4_Meta();
        DrawRow5_FileLoader();
        DrawRow6_ExportExpander();
    }

    // ── Row implementations ───────────────────────────────────────────────

    private void DrawRow1_Transport()
    {
        float iconSize = Gui.GetFrameHeight() * 1.8f;
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
        if (Gui.Button("Load .fdp..."))
        {
            // File dialog is async; fire-and-forget
            _ = LoadFdpAsync();
        }
        Gui.SameLine();
        string displayPath = ActiveContext?.CurrentFdpPath ?? "(none)";
        Gui.TextUnformatted(displayPath);
    }

    private void DrawRow6_ExportExpander()
    {
        if (!Gui.TreeNode("JSON Export Options"))
            return;

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

        Gui.TreePop();
    }

    // ── Export expander sub-sections ─────────────────────────────────────

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
        Gui.InputInt("Start Frame", ref _options.StartFrame);
        Gui.InputInt("End Frame", ref _options.EndFrame);
        if (GetDisabledFrameInputs(_options.WindowMode)) Gui.EndDisabled();
    }

    private void DrawTimeInputs()
    {
        if (GetDisabledTimeInputs(_options.WindowMode)) Gui.BeginDisabled();
        Gui.InputFloat("Start Time (s)", ref _options.StartTimeSec);
        Gui.InputFloat("End Time (s)", ref _options.EndTimeSec);
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
        Gui.Checkbox("Filter by Entity Index", ref _options.FilterByEntityIndex);
        if (!_options.FilterByEntityIndex) Gui.BeginDisabled();
        Gui.InputInt("Entity Index", ref _options.TargetEntityIndex);
        if (!_options.FilterByEntityIndex) Gui.EndDisabled();
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
        Gui.Checkbox("Include Entities", ref _options.IncludeEntities);
        Gui.SameLine();
        Gui.Checkbox("Include Events", ref _options.IncludeEvents);
        Gui.Checkbox("Minified Output", ref _options.Minified);

        bool epsilonDisabled = _options.FormatMode == ExportFormatMode.AbsoluteState;
        if (epsilonDisabled) Gui.BeginDisabled();
        double eps = _options.EpsilonTolerance;
        if (Gui.InputDouble("Epsilon", ref eps))
            _options.EpsilonTolerance = eps;
        if (epsilonDisabled) Gui.EndDisabled();
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
