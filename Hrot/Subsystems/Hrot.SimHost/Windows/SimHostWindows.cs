using System.Numerics;
using FDP.Toolkit.ImGui.WindowManager;
using Hrot.SimHost;
using Hrot.SimHost.UI;
using Fdp.Kernel;
using Fdp.ModuleHost_Core;

namespace Hrot.SimHost.Windows;

/// <summary>SimHost subsystem title-bar colour (dark red).</summary>
internal static class SimHostWindowColor
{
    internal static readonly Vector4 TitleBar = new(0.50f, 0.10f, 0.10f, 1f);
}

/// <summary>
/// SimHost Controls (simulation and spawning) managed window.
/// PerspectiveBound to "SimHost" so it is only shown when the SimHost map is active
/// (unless pinned).
/// </summary>
internal sealed class SimHostControlsWindow : ManagedWindow
{
    private readonly SimHostMainUI             _ui;
    private readonly Func<EntityRepository?>   _repoGetter;
    private readonly Func<ModuleHostKernel?>   _kernelGetter;
    private readonly Func<SimHostScenarioManager?> _scenarioGetter;

    public SimHostControlsWindow(
        SimHostMainUI                  ui,
        Func<EntityRepository?>        repoGetter,
        Func<ModuleHostKernel?>        kernelGetter,
        Func<SimHostScenarioManager?>  scenarioGetter)
        : base("simhost_controls", "SimHost Controls", "SimHost", WindowScope.PerspectiveBound)
    {
        _ui             = ui;
        _repoGetter     = repoGetter;
        _kernelGetter   = kernelGetter;
        _scenarioGetter = scenarioGetter;
        IsOpen          = true;
        TitleBarColor   = new Vector4(0.50f, 0.10f, 0.10f, 1f);  // SimHost red
    }

    protected override void DrawClientArea()
    {
        var repo     = _repoGetter();
        var kernel   = _kernelGetter();
        var scenario = _scenarioGetter();
        if (repo == null || kernel == null || scenario == null) return;
        _ui.DrawContent(repo, kernel, scenario);
    }
}
