using Hrot.Editor.Systems;
using Hrot.Map.Common.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Editor.Modules;

/// <summary>
/// <see cref="IEcsModule"/> that registers the editor-only ECS systems:
/// <see cref="EditorPerceptionSetupSystem"/> and <see cref="EditorZoneAuthoringSystem"/>.
/// <para>⛔ <c>EditorCargoSystem</c> is RETIRED (<c>CE-3110</c> I-2a): the ORBAT's embark/disembark commands are applied by
/// <c>EmbarkationSystem</c>, which the editor gets from <c>CgfLogicPack</c> like CGF — one applier, one set of rules
/// (📄 docs/DESIGN_Entity_Interactions.md §7.1a).</para>
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
[TickMode(TickPolicy.EveryFrame)]
public sealed class EditorSystemsModule : IEcsModule
{
    private readonly EditorPerceptionSetupSystem _perception;
    private readonly EditorZoneAuthoringSystem   _zone;

    /// <inheritdoc/>
    public string Name => "EditorSystems";

    /// <inheritdoc/>
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    /// <summary>
    /// Creates the editor-only ECS systems.
    /// <para>⛔ The former <c>zoneService</c> parameter is gone (F1): the zone authoring system no longer
    /// mirrors authored zones into a save-pipeline DTO, because a zone is an entity and the ordinary save
    /// gate already carries it. 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §5.1, §6.</para>
    /// </summary>
    /// <param name="creation">⭐ <c>CE-3141</c> — the node's creation pack, read late (the obstacle tool requests typed obstacles
    /// through it). A host that has it MUST pass it; null drops placements loudly.</param>
    public EditorSystemsModule(System.Func<Hrot.Common.EntityCreation.EntityCreation?>? creation = null)
    {
        _perception = new EditorPerceptionSetupSystem();
        _zone       = new EditorZoneAuthoringSystem(creation);
    }

    /// <inheritdoc/>
    public void RegisterSystems(ISystemRegistry registry)
    {
        // ⭐ CE-3293 S2 — still ticked by this module, in this order, through the scheduler's one gate.
        _perceptionRun = registry.RegisterManualSystem(_perception);
        _zoneRun       = registry.RegisterManualSystem(_zone);
    }

    private IEcsModuleSystem? _perceptionRun;
    private IEcsModuleSystem? _zoneRun;

    /// <inheritdoc/>
    public void Tick(ISimulationView view, float deltaTime)
    {
        (_perceptionRun ?? _perception).Execute(view, deltaTime);
        (_zoneRun ?? _zone).Execute(view, deltaTime);
    }
}
