using System.Numerics;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Presentation.WindowManager;

namespace Fdp.Presentation.Windows.ReplayBrowser;

/// <summary>
/// ⭐ CE-4261 — <b>File ▸ JSON Export…</b>: a window holding what used to be the Replay Timeline's <i>JSON Export Options</i> tree node — the range, format, filters and payload options and the
/// Save button (🔒 user, 2026-11-03: <i>"when such item is selected, I would like to see a panel which contains what is now under the JSON export options"</i>). ⛔ It owns no options and no export: it
/// draws <see cref="ReplayTimelinePanel.DrawExportBody"/>, whose Save runs the panel's export path (the same file the RB-4 job writes).
/// </summary>
public sealed class JsonExportWindow : ManagedWindow
{
    public const string WindowId = "rb_json_export";

    private readonly ReplayTimelinePanel _panel;

    public JsonExportWindow(string owningPerspective, ReplayTimelinePanel panel, Vector4 titleBarColor)
        : base(WindowId, "JSON Export", owningPerspective, WindowScope.PerspectiveBound)
    {
        _panel = panel;
        TitleBarColor = titleBarColor;
        IsOpen = false;        // opened by File ▸ JSON Export…
        ShowInMenu = false;
    }

    /// <summary>Opens (and raises) the window — the File item calls this.</summary>
    public void Show() => IsOpen = true;

    protected override void DrawClientArea() => _panel.DrawExportBody();
}
