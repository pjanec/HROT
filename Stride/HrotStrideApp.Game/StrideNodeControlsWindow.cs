#nullable enable
using Fdp.Core;
using Fdp.ModuleHost;
using Fdp.Presentation.WindowManager;
using Hrot.SimHost.UI;

namespace HrotStrideApp;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-214</c> — the mode-2 node's CLUSTER TIME CONTROLS.</b>
/// </summary>
/// <remarks>
/// <para>🔒 <b>User ruling, <c>2026-09-09</c>:</b> <i>"simhost is also time slave. there should be
/// toolbar butons to control the cluster wide time. these are what is required, local time control
/// seems useless as there should be no local time used anywhere (if not a pure view of the cluster
/// time), we work in cluster and use cluster synced time."</i></para>
///
/// <para><b>⭐⭐ This hosts SimHost's OWN panel, unchanged.</b> <c>SimHostSimulationControlsPanel</c> is
/// already <c>public</c> and already host-agnostic: it takes an <c>ITimeTransportFacade</c> and its own
/// remarks say <i>"SimHost is a SLAVE node, so it must never pause itself — a pause is cluster-wide,
/// issued as an intent."</i> ⇒ ⛔ there is nothing Stride-specific to write, and nothing to duplicate.
/// 🔒 <c>R-S18</c>: <i>"the more unified, the better."</i></para>
///
/// <para><b>📌 Why this window has to exist at all.</b> <c>SimHostVisualization.SetPanelsWindowManaged()</c>
/// makes <c>DrawUI</c> skip <c>SimHostMainUI</c> — correct for the inspector and event browser, which
/// the diagnostics bundle registers — but on mode 2 <b>nothing else hosted the controls</b>, because
/// <c>SimHostControlsWindow</c> is <c>internal</c> to <c>Hrot.SimHost</c>. ⇒ measured: the Stride node's
/// <c>/panels</c> was missing <c>controls</c> entirely.</para>
///
/// <para>⛔ <b>Deliberately NOT the spawning half of <c>SimHostMainUI</c>.</b> 🔒 The user's own reading:
/// <i>"the spawning panel is something old from early simhost time … its only value is that it exposes
/// the features like road following and formations the cgf and editor might have no idea that they
/// exist"</i>, and it should become <i>"the unified and shared one … [that] sends entity creation
/// request to default entity creation request handler"</i>. ⭐ That is a real change to a shared,
/// working spawn path — <c>SimHostScenarioManager</c> creates entities DIRECTLY today — so it is filed
/// rather than bolted on here. ⚠ The controls half is what the ruling calls <i>"required"</i>.</para>
/// </remarks>
internal sealed class StrideNodeControlsWindow : ManagedWindow
{
    private readonly SimHostSimulationControlsPanel _panel;
    private readonly System.Func<EntityRepository?>  _repo;
    private readonly System.Func<ModuleHostKernel?>  _kernel;

    internal StrideNodeControlsWindow(
        SimHostSimulationControlsPanel  panel,
        System.Func<EntityRepository?>  repo,
        System.Func<ModuleHostKernel?>  kernel)
        : base("stride_controls", "Stride Node Controls", "SimHost", WindowScope.PerspectiveBound)
    {
        _panel  = panel;
        _repo   = repo;
        _kernel = kernel;
        IsOpen  = true;
        // ⭐ CE-083 — this host's ONE colour, the same constant its bundle windows use.
        TitleBarColor = StrideWindowColor.TitleBar;
    }

    protected override void DrawClientArea()
    {
        var repo   = _repo();
        var kernel = _kernel();
        if (repo == null || kernel == null) return;
        _panel.Render(repo, kernel);
    }
}
