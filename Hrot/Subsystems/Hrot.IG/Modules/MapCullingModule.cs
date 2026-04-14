using System;
using Hrot.IG.Systems;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.IG.Modules;

/// <summary>
/// Thin module wrapper that registers <see cref="MapCullingSystem"/> in the
/// <see cref="ModuleHostKernel"/> scheduler.
///
/// Follows the same system-based pattern as <see cref="SpawningModule"/>.
/// </summary>
public class MapCullingModule : IEcsModule
{
    public string          Name   => "MapCulling";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    private readonly MapCullingSystem _system;

    /// <param name="viewport">
    /// Application-owned viewport object updated each frame before the kernel
    /// ticks; provides world-space bounds and zoom level to the culling system.
    /// </param>
    public MapCullingModule(MapCameraViewport viewport)
        => _system = new MapCullingSystem(
            viewport ?? throw new ArgumentNullException(nameof(viewport)));

    /// <inheritdoc/>
    public void RegisterSystems(ISystemRegistry registry)
        => registry.RegisterSystem(_system);

    /// <inheritdoc/>
    public void Tick(ISimulationView view, float dt) { }
}
