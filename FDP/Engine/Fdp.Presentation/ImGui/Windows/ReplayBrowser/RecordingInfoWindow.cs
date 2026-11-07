using System.Numerics;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Presentation.WindowManager;

namespace Fdp.Presentation.Windows.ReplayBrowser;

/// <summary>
/// ⭐ B-7 part 3 (docs/UX/DESIGN_Replay_Browser_UX.md §2a, Q-B2, R-4155) — <b>File ▸ Recording info</b>: the files of the loaded group (a file per node), its length and the
/// exercise id — what the retired <i>Replay Timeline</i> window's last row (the loaded path) held, now where a person looks for it. ⭐ It also carries the retired window's PANEL SNAPSHOT
/// (the <see cref="ReplayTimelinePanelViewModel"/>: frame, wall ticks, wall / sim time, path — built by <see cref="ReplayTimelinePanel.BuildViewModel"/>, not copied), so an agent reading
/// panels loses nothing; the snapshot is published every frame, <b>open or closed</b> (<see cref="ManagedWindow.OnRenderWhileClosed"/>: no ImGui there). Draws no transport — that is
/// the main toolbar's entry, the panel's ONE drawer.
/// </summary>
public sealed class RecordingInfoWindow : ManagedWindow
{
    public const string WindowId = "rb_recording_info";

    /// <summary>⭐ <c>U-obs-5</c> — THE KIND of the snapshot (unchanged by the move from the retired timeline window).</summary>
    internal const string Kind = "replay-timeline";

    private readonly ReplayTimelinePanel _panel;

    public RecordingInfoWindow(string owningPerspective, ReplayTimelinePanel panel, Vector4 titleBarColor)
        : base(WindowId, "Recording info", owningPerspective, WindowScope.PerspectiveBound)
    {
        _panel = panel;
        TitleBarColor = titleBarColor;
        IsOpen = false;        // opened by File ▸ Recording info
        ShowInMenu = false;

        // ⭐⭐⭐ U-obs-5 — DECLARED AT CONSTRUCTION, ALWAYS, ungated on CaptureEnabled.
        PanelSnapshot.DeclareInstrumented(Id);
    }

    /// <summary>Opens (and raises) the window — the File item calls this.</summary>
    public void Show() => IsOpen = true;

    /// <summary>⭐⭐⭐ U-obs-5: BUILD · CAPTURE. No ImGui here.</summary>
    private ReplayTimelinePanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(Id, Kind);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Test hook — the BUILD + CAPTURE portion, callable with no live ImGui context.</summary>
    internal ReplayTimelinePanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    /// <summary>Closed: still publish the snapshot (an agent reads it whether or not the dialog is up).</summary>
    protected override void OnRenderWhileClosed() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        var info = _panel.DescribeRecording();
        if (info.Files.Count == 0) { Gui.TextDisabled("No recording loaded (File ▸ Load .fdp…)."); return; }
        Gui.TextUnformatted("Exercise  " + (info.ExerciseId?.ToString() ?? "-"));
        Gui.Separator();
        foreach (var f in info.Files)
        {
            Gui.TextUnformatted($"Node {f.NodeId}");
            Gui.SameLine();
            Gui.TextDisabled($"{f.Frames:N0} frames · {ReplayTimelinePanel.SeekTime(f.WallSeconds)} long".Replace(',', ' '));
            Gui.TextUnformatted("  " + f.Path);
        }
    }
}
