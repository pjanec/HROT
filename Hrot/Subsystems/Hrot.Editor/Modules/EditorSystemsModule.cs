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
/// kernel.RegisterModule(new EditorSystemsModule());
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
    /// Creates the three editor-only ECS systems.
    /// </summary>
    /// <param name="zoneService">
    /// Optional <see cref="ZoneManagerService"/> used by <see cref="EditorZoneAuthoringSystem"/>
    /// to mirror authored zone data into the save pipeline.
    /// </param>
    public EditorSystemsModule(ZoneManagerService? zoneService = null)
    {
        _cargo      = new EditorCargoSystem();
        _perception = new EditorPerceptionSetupSystem();
        _zone       = new EditorZoneAuthoringSystem(zoneService);
    }

    /// <inheritdoc/>
    public void Tick(ISimulationView view, float deltaTime)
    {
        _cargo.Execute(view, deltaTime);
        _perception.Execute(view, deltaTime);
        _zone.Execute(view, deltaTime);
    }
}
