using System;
using Bagira.IG.Systems;
using ModuleHost.Core.Abstractions;

namespace Bagira.IG.Modules;

/// <summary>
/// Thin module wrapper that registers <see cref="StyleResolutionSystem"/> in the
/// <see cref="ModuleHostKernel"/> scheduler.
///
/// Follows the same system-based pattern as <see cref="SpawningModule"/>: the system
/// instance is constructed here and passed to the registry so that tests can also
/// construct it directly without a kernel.
/// </summary>
public class StyleResolutionModule : IModule
{
    public string          Name   => "StyleResolution";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    private readonly StyleResolutionSystem _system;

    /// <param name="config">
    /// Operator configuration forwarded to <see cref="StyleResolutionSystem"/>
    /// as the Layer-3 highest-priority override source.
    /// </param>
    public StyleResolutionModule(MapUserConfig config)
        => _system = new StyleResolutionSystem(
            config ?? throw new ArgumentNullException(nameof(config)));

    /// <inheritdoc/>
    public void RegisterSystems(ISystemRegistry registry)
        => registry.RegisterSystem(_system);

    /// <inheritdoc/>
    public void Tick(ISimulationView view, float dt) { }
}
