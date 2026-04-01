using FDP.Toolkit.ImGui.WindowManager;
using Hrot.SimHost;
using Hrot.SimHost.UI;
using Fdp.Kernel;
using ModuleHost.Core;

namespace Hrot.ClusterRunner.Windows;

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
