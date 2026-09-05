using System.Numerics;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.WindowManager;
using Hrot.Editor;
using Hrot.Editor.UI;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Panels;

namespace Hrot.Editor.Windows;

// ⭐⭐⭐ CE-061 (Axis-C E5 item ③) — FOUR WRAPPERS WERE DELETED FROM THIS FILE:
//   EditorSpawnerWindow · EditorMissionWindow · EditorConfigWindow · EditorSharedOrbatWindow.
// 📐 Measured: each was a thin ManagedWindow over an ALREADY-SHARED panel and an ALREADY-SHARED
//   facade, with a body byte-identical to Hrot.ExCon/Windows/ExConWindows.cs's copy — the same
//   concept written twice, differing only in id/title/perspective/colour. ⇒ those four are now
//   ARGUMENTS of Hrot.Presentation.Windows.{Spawner,Mission,Config,SharedOrbat}PanelWindow, which
//   CGF can reach (Hrot.Editor → Hrot.CGF makes this file unreachable from that host).
// ⭐ The editor's window IDS ARE UNCHANGED (ScenarioPanelWindowIds.Editor*), so layout files,
//   PanelSnapshot instrumentation and every id-keyed rail still resolve.
// ⛔ EditorToolbarWindow / EditorOrbatWindow / EditorPreviewWindow / EditorZoneEditorWindow STAY:
//   design §4 — the first takes IEditorLogic, the third needs the editor-only planning state, the
//   fourth still reaches Hrot.Editor.Gizmos. Sharing them without those resolved would put a
//   window on CGF that cannot be serviced (ruling 49).


/// <summary>Editor subsystem title-bar colour (slate blue).</summary>
internal static class EditorWindowColor
{
    internal static readonly Vector4 TitleBar = new(0.15f, 0.22f, 0.48f, 1f);
}

/// <summary>Editor toolbar panel as a perspective-bound managed window.
/// ⭐⭐⭐ U-obs-5 — the HOST registers. ⚠ No sibling host: single host, kind stays a local literal.</summary>
internal sealed class EditorToolbarWindow : ManagedWindow
{
    internal const string Kind = "editor-toolbar";

    private readonly EditorToolbarPanel _panel;
    private readonly IEditorLogic       _logic;

    public EditorToolbarWindow(EditorToolbarPanel panel, IEditorLogic logic)
        : base("editor_toolbar", "Editor Toolbar", "Scenario", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _logic = logic;
        IsOpen        = true;
        TitleBarColor = EditorWindowColor.TitleBar;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    private EditorToolbarPanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(_logic, Id, Kind);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    internal EditorToolbarPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _panel.DrawContent(_logic);
    }
}

/// <summary>Editor ORBAT panel as a perspective-bound managed window.</summary>
internal sealed class EditorOrbatWindow : ManagedWindow
{
    private readonly EditorOrbatPanel _panel;
    private readonly IEditorLogic     _logic;

    public EditorOrbatWindow(EditorOrbatPanel panel, IEditorLogic logic)
        : base("editor_orbat", "Editor ORBAT", "Scenario", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _logic = logic;
        IsOpen        = true;
        TitleBarColor = EditorWindowColor.TitleBar;
    }

    protected override void DrawClientArea() => _panel.DrawContent(_logic);
}

/// <summary>Preview/Edit mode toggle panel as a perspective-bound managed window.
/// ⭐⭐⭐ U-obs-5 — the HOST registers; the KIND is <see cref="PanelIds.Preview"/>, shared with the
/// (not yet converted) ExCon host of the same <see cref="PreviewPanel"/>.</summary>
internal sealed class EditorPreviewWindow : ManagedWindow
{
    private readonly PreviewPanel       _panel;
    private readonly IPreviewController _ctrl;

    public EditorPreviewWindow(PreviewPanel panel, IPreviewController ctrl)
        : base("editor_preview", "Preview", "Scenario", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _ctrl  = ctrl;
        IsOpen        = true;
        TitleBarColor = EditorWindowColor.TitleBar;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    private PreviewPanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(_ctrl, Id, PanelIds.Preview);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    internal PreviewPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _panel.DrawContent(_ctrl);
    }
}

/// <summary>Zone editor (road network + LOS obstacle placement) as a perspective-bound managed window.
/// ⭐⭐⭐ U-obs-5 — the HOST registers; the KIND is <see cref="PanelIds.ZoneEditor"/>. ⚠ No ExCon host
/// exists for this one (measured) — single host, but the constant already lives in
/// <c>PanelIds</c> alongside its four siblings for consistency.</summary>
internal sealed class EditorZoneEditorWindow : ManagedWindow
{
    private readonly ZoneEditorPanel          _panel;
    private readonly IZoneAuthoringController _ctrl;

    public EditorZoneEditorWindow(ZoneEditorPanel panel, IZoneAuthoringController ctrl)
        : base("editor_zone_editor", "Zone Editor", "Scenario", WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _ctrl  = ctrl;
        IsOpen        = true;
        TitleBarColor = EditorWindowColor.TitleBar;
        PanelSnapshot.DeclareInstrumented(Id);
    }

    private ZoneEditorPanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(Id, PanelIds.ZoneEditor);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    internal ZoneEditorPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _panel.DrawContent(_ctrl);
    }
}
