using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Blueprints.Systems;

/// <summary>
/// Ticks all active Blueprint instances each frame.
/// Minimal stub for Phase 1 test harness; full implementation in TASK-RT-005.
/// </summary>
public sealed class BlueprintTickSystem
{
    private readonly BlueprintRegistry _registry;

    public BlueprintTickSystem(BlueprintRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>Execute Blueprint tick for all active instances. Stub is no-op.</summary>
    public void Execute(ISimulationView view) { }
}
