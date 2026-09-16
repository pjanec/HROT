using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.ModuleHost;
using Fdp.Presentation.Panels;
using Fdp.Presentation.WindowManager;

namespace Hrot.Presentation.Windows;

/// <summary>
/// Shared module/system execution-stats profiler managed window.
///
/// <para>⭐⭐⭐ <c>BP-327</c> — <see cref="SystemProfilerPanel"/> was a static panel with zero production
/// callers (<c>ModuleHostKernel.GetExecutionStats()</c> is reachable from every host, but nothing hosted
/// the panel as a <see cref="ManagedWindow"/>). ⭐ Mirrors <c>ArchitectureDiagnosticsWindow</c> — the HOST
/// supplies the address (its own <see cref="ManagedWindow.Id"/>) and the kind
/// (<see cref="PanelIds.SystemProfiler"/>), since <c>SystemProfilerPanel</c> itself has no window
/// identity of its own.</para>
///
/// <para>⚠ <see cref="SystemProfilerPanel"/> is fully static and takes its stats as a plain
/// <c>List&lt;ModuleStats&gt;?</c> rather than wrapping a service — so this window holds the LAZY
/// accessor directly (a <see cref="Func{TResult}"/> over the kernel's stats), the same laziness
/// <c>ArchitectureDiagnosticsService</c> gives its kernel getter, for the same reason: the kernel does
/// not exist yet at <c>RegisterWindows</c> time.</para>
/// </summary>
public sealed class SystemProfilerWindow : ManagedWindow
{
    /// <summary>⭐⭐ THE KIND — hosted by more than one subsystem, so it MUST cite the shared constant.</summary>
    internal const string Kind = PanelIds.SystemProfiler;

    private readonly Func<List<ModuleStats>?> _statsProvider;

    public SystemProfilerWindow(
        string id,
        string title,
        string owningPerspective,
        Func<List<ModuleStats>?> statsProvider,
        Vector4? titleBarColor = null)
        : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
    {
        _statsProvider = statsProvider ?? throw new ArgumentNullException(nameof(statsProvider));
        IsOpen = false;
        TitleBarColor = titleBarColor;

        // ⭐⭐⭐ DECLARED AT CONSTRUCTION, ALWAYS, ungated on CaptureEnabled.
        PanelSnapshot.DeclareInstrumented(Id);
    }

    /// <summary>⭐⭐⭐ BUILD · CAPTURE. No ImGui here.</summary>
    private SystemProfilerPanelViewModel BuildAndPublish()
    {
        var vm = SystemProfilerPanel.BuildViewModel(_statsProvider(), Id, Kind);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Test hook — the BUILD + CAPTURE portion, callable with no live ImGui context.</summary>
    internal SystemProfilerPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    /// <summary>
    /// ⭐⭐⭐ <b>BUILD ONCE, then RENDER FROM THE MODEL — the contract's central invariant.</b>
    /// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c>.
    ///
    /// <para>⛔⛔ <b>Two things this must NOT do, both of which the first cut did:</b>
    /// <list type="number">
    ///   <item>⛔ <b>Sample twice.</b> Calling <c>_statsProvider()</c> for the dump and again for the
    ///   draw lets the published model and the pixels disagree within one frame — ⚠ the snapshot would
    ///   then be evidence about a frame that was never shown.</item>
    ///   <item>⛔⛔ <b>Call <c>SystemProfilerPanel.Draw</c>.</b> 📐 That overload opens its OWN
    ///   <c>ImGuiApi.Begin("System Profiler")</c>, and <c>ManagedWindow.Render</c> has already called
    ///   <c>Gui.Begin</c> (:202) before <c>DrawClientArea</c> (:221) ⇒ a nested second window: this one
    ///   renders EMPTY and a stray floating panel appears beside it. ⭐ <c>DrawContent</c> exists for
    ///   exactly this, mirroring <c>ArchitectureDiagnosticsPanel.DrawContent</c>.</item>
    /// </list></para>
    /// </summary>
    protected override void DrawClientArea() => SystemProfilerPanel.DrawContent(BuildAndPublish());
}
