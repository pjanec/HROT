using System.Numerics;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Panels;
using Fdp.Presentation.WindowManager;
using ImGuiNET;
using ImGuiApi = ImGuiNET.ImGui;

namespace Hrot.Presentation.Windows;

/// <summary>
/// Shared architecture diagnostics managed window for module/system/translator inspection.
///
/// <para>⭐⭐⭐ <b>U-obs-5 — the HOST registers, not the panel.</b> <see cref="ArchitectureDiagnosticsPanel"/>
/// is a plain <c>*Panel</c> with no window identity of its own; this window (the ONLY production host,
/// measured) supplies the address (its own <see cref="ManagedWindow.Id"/>) and the kind.</para>
/// </summary>
public sealed class ArchitectureDiagnosticsWindow : ManagedWindow
{
    /// <summary>⭐ <c>U-obs-5</c> — THE KIND. Single host: stays a local literal.</summary>
    internal const string Kind = "architecture-diagnostics";

    private readonly ArchitectureDiagnosticsPanel _panel;

    public ArchitectureDiagnosticsWindow(
        string id,
        string title,
        string owningPerspective,
        ArchitectureDiagnosticsPanel panel,
        System.Numerics.Vector4? titleBarColor = null)
        : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
    {
        _panel = panel;
        IsOpen = false;
        TitleBarColor = titleBarColor;

        // ⭐⭐⭐ U-obs-5 — DECLARED AT CONSTRUCTION, ALWAYS, ungated on CaptureEnabled.
        PanelSnapshot.DeclareInstrumented(Id);
    }

    /// <summary>⭐⭐⭐ U-obs-5: BUILD · CAPTURE. No ImGui here.</summary>
    private ArchitectureDiagnosticsPanelViewModel BuildAndPublish()
    {
        var vm = _panel.BuildViewModel(Id, Kind);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Test hook — the BUILD + CAPTURE portion, callable with no live ImGui context.</summary>
    internal ArchitectureDiagnosticsPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();
        _panel.DrawContent();
    }
}
