using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Toolkit.ReplayBrowser;
using ImGuiNET;

namespace Fdp.Presentation.Panels.ReplayBrowser;

/// <summary>
/// Transport control panel for the replay browser.
/// Renders timeline controls, frame metadata, file loading, and JSON export options.
/// </summary>
public sealed class ReplayTimelinePanel
{
    private readonly ReplayBrowserContext _context;
    private readonly IRecordingExportService _exportService;
    private readonly IFileDialogService _fileDialogService;
    private readonly PlaybackHistoryTracker _playbackHistory;
    private readonly InspectorState _inspectorState;

    private JsonExportOptions _options = new();
    private bool _isExporting;

    /// <summary>
    /// Fired when the user selects an entity via the timeline panel
    /// (e.g. via a filter drop-down or entity link in metadata).
    /// </summary>
    public Action<Entity>? OnEntitySelected { get; set; }

    public ReplayTimelinePanel(
        ReplayBrowserContext context,
        IRecordingExportService exportService,
        IFileDialogService fileDialogService,
        PlaybackHistoryTracker playbackHistory,
        InspectorState inspectorState)
    {
        _context = context;
        _exportService = exportService;
        _fileDialogService = fileDialogService;
        _playbackHistory = playbackHistory;
        _inspectorState = inspectorState;
    }

    // ── Public draw entry point ───────────────────────────────────────────

    public void DrawContent()
    {
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
        // Back
        if (!_playbackHistory.CanGoBack) Gui.BeginDisabled();
        if (Gui.Button("<- Back")) _playbackHistory.GoBack();
        if (!_playbackHistory.CanGoBack) Gui.EndDisabled();

        Gui.SameLine();

        // Forward
        if (!_playbackHistory.CanGoForward) Gui.BeginDisabled();
        if (Gui.Button("Fwd ->")) _playbackHistory.GoForward();
        if (!_playbackHistory.CanGoForward) Gui.EndDisabled();

        Gui.SameLine();
        if (Gui.Button("|< Rewind")) SeekToFirst();
        Gui.SameLine();
        Gui.Button("|| Pause / Play >");
        Gui.SameLine();
        if (Gui.Button("< Step Back")) _context.StepBackward();
        Gui.SameLine();
        if (Gui.Button("Step Forward >")) _context.StepForward();
    }

    private void DrawRow2_TimeInfo()
    {
        if (_context.Playback == null || _context.CurrentFrame < 0)
        {
            Gui.TextDisabled("Frame: - | Wall Ticks: - | Wall Time: - | Sim Time: -");
            return;
        }

        int current = _context.CurrentFrame;
        var meta = _context.Playback.GetFrameMetadata(current);
        long firstFrameWallTicks = _context.Playback.GetFrameMetadata(0).WallClockTicks;

        double relativeWallSec =
            (meta.WallClockTicks - firstFrameWallTicks) / (double)TimeSpan.TicksPerSecond;

        double simTimeSec = 0.0;
        if (_context.SandboxRepo.HasSingletonUnmanaged<GlobalTime>())
            simTimeSec = _context.SandboxRepo.GetSingletonUnmanaged<GlobalTime>().TotalTime;

        Gui.TextUnformatted(
            $"Frame: {current} | Wall Ticks: {meta.WallClockTicks} | Wall Time: {relativeWallSec:F3}s | Sim Time: {simTimeSec:F3}s");
    }

    private void DrawRow3_Slider()
    {
        int totalFrames = _context.Playback?.TotalFrames ?? 1;
        int current = _context.CurrentFrame < 0 ? 0 : _context.CurrentFrame;
        int max = totalFrames > 0 ? totalFrames - 1 : 0;

        if (Gui.SliderInt("##timeline", ref current, 0, max))
        {
            _context.SeekToFrame(current);
        }
        Gui.SameLine();
        Gui.TextUnformatted($"Frame {current} / {max}");
    }
                                                                
    private void DrawRow4_Meta()
    {
        // Metadata display (tick, simframe, simtime, frame type, size)
        // When no recording is loaded, show dashes.
        if (_context.Playback == null)
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
        string displayPath = _context.CurrentFdpPath ?? "(none)";
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
        bool canSave = !_isExporting && _context.CurrentFdpPath != null;
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
            _ = SaveAsync(snapshot);
        }
        if (!canSave) Gui.EndDisabled();

        if (_isExporting) Gui.TextDisabled("Exporting...");
    }

    // ── Async helpers ─────────────────────────────────────────────────────

    private async Task LoadFdpAsync()
    {
        var path = await _fileDialogService.ShowOpenFileDialogAsync("ReplayBrowser_LoadRecording", "*.fdp");
        if (!string.IsNullOrEmpty(path))
            _context.LoadRecording(path);
    }

    private async Task SaveAsync(JsonExportOptions snapshot)
    {
        _isExporting = true;
        try
        {
            string? outPath = await _fileDialogService.ShowSaveAsDialogAsync("ReplayBrowser_ExportJson", "dump.json", "*.json");
            if (string.IsNullOrEmpty(outPath)) return;

            string inputPath = _context.CurrentFdpPath!;
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
        if (_context.Playback != null)
            _context.SeekToFrame(0);
    }

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
