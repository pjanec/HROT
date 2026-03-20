using Bagira.IG.Systems;
using ModuleHost.Core.Abstractions;

namespace Bagira.IG.Modules;

/// <summary>
/// Module wrapper that registers <see cref="HistoryRecordingSystem"/> in the
/// <see cref="ModuleHostKernel"/> scheduler.
///
/// Follows the same thin-wrapper pattern as <see cref="StyleResolutionModule"/>
/// so that tests can construct the system directly without a kernel.
/// </summary>
public class HistoryTrailModule : IEcsModule
{
    public string          Name   => "HistoryTrail";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    private readonly HistoryRecordingSystem _system = new();

    /// <inheritdoc/>
    public void RegisterSystems(ISystemRegistry registry)
        => registry.RegisterSystem(_system);

    /// <inheritdoc/>
    public void Tick(ISimulationView view, float dt) { }
}
