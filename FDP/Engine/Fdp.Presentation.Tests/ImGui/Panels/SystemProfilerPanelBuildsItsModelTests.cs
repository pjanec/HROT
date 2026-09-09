using System.Collections.Generic;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Resilience;
using Fdp.Presentation.Panels;
using Xunit;

namespace Fdp.Presentation.Tests.ImGui.Panels;

/// <summary>
/// ⭐⭐ <b>U-obs-5 — <c>SystemProfilerPanel.BuildViewModel</c>, the BUILD half only.</b>
/// 📄 <c>docs/blueprints/batches/QUEUE_Panel_Observability_Sweep.md</c> group 4.
///
/// <para>⚠ <b>No <c>PanelSnapshot</c> rails here on purpose</b> — measured zero production callers of
/// <see cref="SystemProfilerPanel"/> anywhere in the tree, so there is no host to declare/register from.
/// See the view-model's own remarks.</para>
/// </summary>
public sealed class SystemProfilerPanelBuildsItsModelTests
{
    [Fact]
    public void TheDump_CarriesEachModulesHealthStatus()
    {
        var stats = new List<ModuleStats>
        {
            new() { ModuleName = "Physics", ExecutionCount = 10, FailureCount = 0, CircuitState = CircuitState.Closed },
            new() { ModuleName = "Net",     ExecutionCount = 5,  FailureCount = 2, CircuitState = CircuitState.Open },
        };

        var vm = SystemProfilerPanel.BuildViewModel(stats, "sysprof-test", "system-profiler");

        Assert.Equal(2, vm.Rows.Count);
        Assert.True(vm.Rows[0].IsHealthy);
        Assert.False(vm.Rows[1].IsHealthy);
        Assert.Equal(2, vm.Rows[1].FailureCount);
    }

    [Fact]
    public void ANullStatsList_BuildsAnEmptyDump()
    {
        var vm = SystemProfilerPanel.BuildViewModel(null, "sysprof-test", "system-profiler");
        Assert.Empty(vm.Rows);
    }
}
