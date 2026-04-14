using Hrot.IG.Systems;
using Fdp.ModuleHost_Core.Abstractions;

namespace Hrot.IG.Modules;

/// <summary>
/// Module wrapper that registers <see cref="MapLayerAssignmentSystem"/> with the
/// <see cref="ModuleHostKernel"/> scheduler.
///
/// <para>The system periodically re-evaluates every entity's <c>MapDisplayComponent</c>
/// bitmask using the <see cref="MapLayerRegistry.All"/> predicate registry, enabling
/// the rendering hot-path to perform O(1) bitwise layer filtering.</para>
/// </summary>
public class MapLayerModule : IEcsModule
{
    /// <inheritdoc/>
    public string Name => "MapLayerAssignment";

    /// <inheritdoc/>
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    private readonly MapLayerAssignmentSystem _system;

    /// <summary>
    /// Constructs the module with the default <see cref="MapLayerRegistry.All"/> layer set.
    /// </summary>
    public MapLayerModule() => _system = new MapLayerAssignmentSystem();

    /// <inheritdoc/>
    public void RegisterSystems(ISystemRegistry registry)
        => registry.RegisterSystem(_system);

    /// <inheritdoc/>
    public void Tick(ISimulationView view, float dt) { }
}
