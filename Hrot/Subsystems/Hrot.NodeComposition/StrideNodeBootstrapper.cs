using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Scheduling;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.NetworkSpawning.Systems;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Vis2D.Components;
using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.Core.Network;
using Hrot.IG.Components;
using Hrot.Network.Infrastructure;
using Hrot.IG.Systems;
using Hrot.Map.Common;
using Hrot.SimHost;
using Hrot.SimHost.Modules;
using Hrot.SimHost.Serializers;
using Hrot.SimHost.Systems;

namespace Hrot.NodeComposition;

/// <summary>
/// Concrete <see cref="SharedApplicationBootstrapper"/> for the Stride-hosted simulation node.
///
/// <para>
/// ST-014: this type used to live in <c>Hrot.StrideMock</c> and outlived it. The mock was temporary
/// scaffolding standing in for the real Stride app; this is the node composition root that app
/// actually consumes (<c>StrideHrotGame</c> holds it and takes it via <c>AttachBootstrapper</c>).
/// It could not move down into <c>Hrot.Common</c> with <see cref="SharedApplicationBootstrapper"/>,
/// because it composes <c>Hrot.SimHost</c> and <c>Hrot.IG</c> systems and both of those reference
/// <c>Hrot.Common</c> - that edge would be a cycle. See <c>DESIGN_Stride_Port.md</c>.
/// </para>
///
/// <para>
/// Implements all abstract hooks to produce a headless-compatible simulation node
/// with the following roles:
/// <see cref="NodeRole.MuscleGround"/> |
/// <see cref="NodeRole.Perception"/> |
/// <see cref="NodeRole.NavigationSolver"/> |
/// <see cref="NodeRole.ImageGenerator"/>.
/// </para>
///
/// <para>
/// Domain modules (kinematics, perception, combat, navigation) are injected via the
/// constructor so Stage 2 can swap in Stride-native implementations without touching
/// the orchestration code.
/// </para>
///
/// <para>
/// IMPORTANT: this class must not reference Raylib, ImGui, or
/// <c>IMapCameraProvider</c>. It is engine-agnostic by design.
/// </para>
/// </summary>
public sealed class StrideNodeBootstrapper : SharedApplicationBootstrapper, IDisposable
{
    /// <summary>Combined node role for all Stride-hosted node responsibilities.</summary>
    public static readonly NodeRole Role =
        NodeRole.MuscleGround | NodeRole.Perception |
        NodeRole.NavigationSolver | NodeRole.ImageGenerator;

    private readonly IEcsModule? _kinematicsModule;
    private readonly IEcsModule? _perceptionModule;
    private readonly IEcsModule? _combatModule;
    private readonly IEcsModule? _navigationModule;

    // Saved by the overriding BootstrapNode so the abstract hooks can access them.
    private HrotNodeConfig?   _savedConfig;
    private NodeRole          _savedRole;
    private NodeBootstrapper? _nodeBootstrapper;

    /// <summary>
    /// The fully wired node context. Valid after <see cref="BootstrapNode"/> returns.
    /// </summary>
    public HrotNodeContext Context { get; private set; } = default!;

    /// <summary>
    /// Sim-phase togglable group exposed for subsystem rendering and test inspection.
    /// Valid after <see cref="BootstrapNode"/> returns.
    /// </summary>
    public TogglableSimulationGroup SimGroup { get; private set; } = default!;

    /// <summary>
    /// Post-sim togglable group. Valid after <see cref="BootstrapNode"/> returns.
    /// </summary>
    public TogglablePostSimulationGroup PostSimGroup { get; private set; } = default!;

    /// <summary>
    /// Optional callback invoked during Phase 6d (after network translators, before Initialize).
    /// Used to register diagnostic capture systems and other application-level systems.
    /// </summary>
    public Action<HrotNodeContext>? ApplicationSystemsRegistrar { get; set; }

    /// <summary>
    /// Producer buffer: local ECS systems write gizmos here each frame, then
    /// the batch is published to DDS (when a participant is present).
    /// </summary>
    public DebugPrimitiveBuffer ProducerBuffer { get; } = new DebugPrimitiveBuffer();

    /// <summary>
    /// Consumer buffer: populated from DDS by the debug-primitives ingress translator.
    /// The Raylib renderer reads from this buffer.
    /// </summary>
    public DebugPrimitiveBuffer ConsumerBuffer { get; } = new DebugPrimitiveBuffer();

    /// <summary>Map camera for 2D viewport navigation.</summary>
    public MapCamera Camera { get; } = new MapCamera();

    // ITimeControlGateway? TimeControl is inherited from SharedApplicationBootstrapper.

    /// <param name="kinematicsModule">Optional kinematics module (e.g. GroundKinematicsModule).</param>
    /// <param name="perceptionModule">Optional perception module (e.g. CognitiveSpatialModule).</param>
    /// <param name="combatModule">Optional combat module (e.g. CombatModule).</param>
    /// <param name="navigationModule">Optional navigation solver module.</param>
    public StrideNodeBootstrapper(
        IEcsModule? kinematicsModule = null,
        IEcsModule? perceptionModule = null,
        IEcsModule? combatModule     = null,
        IEcsModule? navigationModule = null)
    {
        _kinematicsModule = kinematicsModule;
        _perceptionModule = perceptionModule;
        _combatModule     = combatModule;
        _navigationModule = navigationModule;
    }

    /// <summary>
    /// Runs the 7-phase bootstrap pipeline and stores the resulting context.
    ///
    /// <para>
    /// Hides (does not override) the base-class method so config and role can
    /// be captured before the abstract hooks are invoked, and so that the
    /// <c>SlaveTranslator</c> side-effect of <see cref="BuildOrchestration"/>
    /// can be patched into the context before it is exposed publicly.
    /// </para>
    /// </summary>
    public new HrotNodeContext BootstrapNode(
        HrotNodeConfig config,
        NodeRole role,
        INetworkFactory networkFactory)
    {
        _savedConfig      = config;
        _savedRole        = role;
        _nodeBootstrapper = new NodeBootstrapper(networkFactory);

        var ctx = base.BootstrapNode(config, role, networkFactory);

        // Patch SlaveTranslator — it is a side-effect of NodeBootstrapper.BuildOrchestration
        // and is NOT set by the base class (only ClusterSlave is patched in by the base).
        if (_nodeBootstrapper.SlaveTranslator != null)
            ctx = ctx with { SlaveTranslator = _nodeBootstrapper.SlaveTranslator };

        Context = ctx;
        return Context;
    }

    /// <summary>
    /// Advances the node by one frame.  Call once per application frame from
    /// the main thread.
    /// </summary>
    public void Tick(float dt)
    {
        ProducerBuffer.EndFrame(dt);
        ConsumerBuffer.Clear();

        Context.SlaveTranslator?.Tick();
        Context.ClusterSlave.Tick();

        // Forward dt directly — SlaveSyncController needs network sync events to advance
        // deterministically, which are absent in headless/offline mode.
        //
        // ⚠ ST-014: the ORIGINAL justification for the legacy Update(float) overload was that "this
        // bootstrapper is explicitly a mock/test-only harness, not a live DDS-connected node". That
        // premise is now FALSE — the mock is retired and this is the real Stride app's composition
        // root, which can be DDS-connected. The call is left exactly as it was: this batch only moved
        // the type, and re-deciding the tick path is a behaviour change that needs its own review.
        // Filed rather than silently patched or silently kept — see the batch report.
#pragma warning disable CS0618
        Context.Kernel.Update(dt);
#pragma warning restore CS0618
        Context.EventBus.SwapBuffers();

        // _gizmoIngress?.PollAndApply();  // fills ConsumerBuffer from DDS — wire in SM-006
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Context?.Participant?.Dispose();
    }

    /// <inheritdoc/>
    protected override void RegisterApplicationSystems(HrotNodeContext context)
        => ApplicationSystemsRegistrar?.Invoke(context);

    // ── Abstract hook implementations ─────────────────────────────────────────

    /// <inheritdoc/>
    protected override HrotNodeContext BuildContext(HrotNodeConfig config, NodeRole role, INetworkFactory? networkFactory)
    {
        return new HrotNodeBuilder(config)
            .WithRole(config.SubsystemName, role)
            .WithNetworkFactory(networkFactory)
            .WithReplication(role)
            .WithBehaviorRegistry(GetBehaviorRegistry())
            .Build();
    }

    /// <inheritdoc/>
    protected override void RegisterDomainComponents(EntityRepository world)
    {
        // Foundation: network-replication, geographic, definitions, lifecycle events.
        HrotSharedComponentRegistry.RegisterAll(world);

        // Muscle-tier: vehicle physics, CQRS nav state, formation components.
        // CognitiveComponentRegistry is intentionally excluded — Brain AI data stays
        // on the CGF node. TkbTemplate.ApplyTo() silently skips missing components,
        // so entity spawning works correctly here without the Brain tables.
        MuscleRoleComponentRegistry.RegisterAll(world);

        // IG presentation components — not covered by any shared registry.
        // Required by SyncFdpToStrideScript queries and EventToEffectSystem.
        PresentationComponentRegistry.RegisterAll(world);
        world.RegisterComponent<VisualEffectState>();
        world.RegisterComponent<TracerTarget>();

        // Genesis Intent DTOs: transient managed components resolved by
        // GenesisMaterializationSystem during scenario load. Must mirror the
        // set registered in SimHostComponentRegistry so cross-entity references
        // are correctly materialised on this node.
        GenesisIntentRegistry.RegisterAll(world);
    }

    /// <inheritdoc/>
    protected override ScenarioSerializer BuildSerializer(BehaviorRegistry? registry)
        => HrotScenarioSerializerFactory.Build(registry ?? new BehaviorRegistry());

    /// <inheritdoc/>
    protected override void PopulateSystems(
        HrotNodeContext context,
        List<IEcsModuleSystem> input,
        List<IEcsModuleSystem> sim,
        List<IEcsModuleSystem> postSim)
    {
        // Visual effect systems are placed in the togglable groups so they are
        // suspended during replay (SC_SM005_2).
        // EventToEffectSystem reads combat events and spawns VisualEffectState entities.
        // VisualEffectCleanupSystem removes expired effect entities.
        sim.Add(new EventToEffectSystem());
        postSim.Add(new VisualEffectCleanupSystem());
    }

    /// <inheritdoc/>
    protected override IEnumerable<IEcsModule> GetAdditionalModules()
    {
        if (_kinematicsModule != null) yield return _kinematicsModule;
        if (_perceptionModule != null) yield return _perceptionModule;
        if (_combatModule     != null) yield return _combatModule;
        if (_navigationModule != null) yield return _navigationModule;
    }

    /// <inheritdoc/>
    protected override ClusterSlave BuildOrchestration(
        HrotNodeContext context,
        TogglableSimulationGroup simGroup,
        TogglablePostSimulationGroup postSimGroup,
        ScenarioSerializer serializer)
    {
        SimGroup     = simGroup;
        PostSimGroup = postSimGroup;

        return _nodeBootstrapper!.BuildOrchestration(
            _savedRole,
            context.Kernel,
            context.World,
            context.NodeId,
            participant:         context.Participant,
            subsystemName:       _savedConfig!.SubsystemName,
            eventBus:            context.EventBus,
            scenarioSerializer:  null,    // this node does not load/save scenarios
            localTempRoot:       _savedConfig.LocalTempRoot,
            simGroup:            simGroup,
            postSimGroup:        postSimGroup,
            lifecycleGroup:      context.NedReplication?.NetworkLifecycleGroup,
            ghostCreationSystem: context.GhostCreationSystem,
            eventAccumulator:    context.EventAccumulator,
            afterSeek:           context.NedReplication?.AfterSeekCallback);
    }

    /// <inheritdoc/>
    protected override void RegisterSpawningPipeline(HrotNodeContext context)
    {
        // GenesisMaterializationSystem resolves cross-entity Intent DTOs into live
        // component data during scenario load. Runs in the Input phase.
        context.Kernel.RegisterGlobalSystem(
            new GenesisMaterializationSystem(context.EntityMap));

        // NetworkSpawningSystem handles incoming entity creation requests from the
        // network (CGF/Brain node). Only wired when an ID allocator is available;
        // headless tests with SkipAllocatorRouting=true skip this path.
        if (context.IdAllocator != null)
        {
            var elm = (EntityLifecycleModule)context.BaseModules[0];
            var spawningSystem = new NetworkSpawningSystem(
                context.TkbDb!,
                elm,
                context.EntityMap,
                context.IdAllocator,
                context.NodeId);

            context.Kernel.RegisterModule(new SimHostModule(spawningSystem));
        }
    }

    /// <inheritdoc/>
    protected override void RegisterNetworkTranslators(
        HrotNodeContext context,
        INetworkFactory? configuredFactory)
    {
        if (configuredFactory == null) return;
        // SimHost auxiliary translators: entity attribute updates, combat egress, etc.
        configuredFactory.CreateSimHostAuxiliaryTranslators().RegisterOn(context.Kernel);
    }
}
