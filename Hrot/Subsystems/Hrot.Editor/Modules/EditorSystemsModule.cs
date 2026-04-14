using Fdp.Core;
using Hrot.Editor.Systems;
using Hrot.Map.Common.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Editor.Modules;

/// <summary>
/// <see cref="IEcsModule"/> that registers the three editor-only ECS systems:
/// <see cref="EditorCargoSystem"/>, <see cref="EditorPerceptionSetupSystem"/>, and
/// <see cref="EditorZoneAuthoringSystem"/>.
///
/// <para>Uses the direct-execution pattern: each system is initialised via
/// <see cref="ComponentSystem.Create"/> at construction time and driven via
/// <see cref="ComponentSystem.Run"/> on every <see cref="Tick"/> call.</para>
///
/// <para><b>Usage:</b> register with the kernel <em>before</em> calling
/// <c>kernel.Initialize()</c>:</para>
/// <code>
/// kernel.RegisterModule(new EditorSystemsModule(world));
/// kernel.Initialize();
/// </code>
/// </summary>
public sealed class EditorSystemsModule : IEcsModule
{
    private readonly EditorCargoSystem           _cargo;
    private readonly EditorPerceptionSetupSystem _perception;
    private readonly EditorZoneAuthoringSystem   _zone;

    /// <inheritdoc/>
    public string Name => "EditorSystems";

    /// <inheritdoc/>
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    /// <summary>
    /// Creates and initialises the three editor-only ECS systems against
    /// <paramref name="world"/>.
    /// </summary>
    /// <param name="world">The shared <see cref="EntityRepository"/> used by all systems.</param>
    /// <param name="zoneService">
    /// Optional <see cref="ZoneManagerService"/> used by <see cref="EditorZoneAuthoringSystem"/>
    /// to mirror authored zone data into the save pipeline.
    /// </param>
    public EditorSystemsModule(EntityRepository world, ZoneManagerService? zoneService = null)
    {
        _cargo      = new EditorCargoSystem();
        _perception = new EditorPerceptionSetupSystem();
        _zone       = new EditorZoneAuthoringSystem(zoneService);

        _cargo.Create(world);
        _perception.Create(world);
        _zone.Create(world);
    }

    /// <inheritdoc/>
    public void Tick(ISimulationView view, float deltaTime)
    {
        _cargo.Run();
        _perception.Run();
        _zone.Run();
    }
}
