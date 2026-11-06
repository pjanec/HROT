using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using CarKinem.Tkb;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Diagnostics;
using Fdp.ModuleHost.Scheduling;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Translators;
using Fdp.Toolkit.Combat.Translators;
using Fdp.Toolkit.Diagnostics;
using Fdp.Toolkit.Navigation.EngineBacked;
using Fdp.Toolkit.Perception.Translators;
using Fdp.Toolkit.Spatial;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.NetworkSpawning.Systems;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Time;
using Fdp.Core.Orchestration;
using Fdp.Core.Diagnostics;
using Fdp.Core.Serialization.Migrations;
using Hrot.Common.Diagnostics;
using Hrot.Common.EntityCreation;
using Hrot.Common.Infrastructure;
using Hrot.Core.Diagnostics;
using Hrot.Core.Network;
using Hrot.Network.Infrastructure;
using Hrot.SimHost.Modules;
using Hrot.SimHost.Serializers;
using Hrot.SimHost.Systems;

namespace Hrot.SimHost;

/// <summary>
/// Concrete <see cref="SharedApplicationBootstrapper"/> for the SimHost node.
/// Implements all abstract hooks to produce a node with roles:
/// <see cref="NodeRole.MuscleGround"/> | <see cref="NodeRole.Perception"/>.
/// </summary>
public sealed class SimHostNodeBootstrapper : SharedApplicationBootstrapper
{
    private readonly INetworkFactory? _networkFactory;
    private readonly NodeRole _role;
    private readonly string _localTempRoot;
    private readonly IDiagnosticEventHistoryService? _eventHistoryService;
    private readonly HrotNodeConfig _hrotConfig;
    private readonly float _simulationRateHz;

    private NodeBootstrapper? _nodeBootstrapper;
    private ITkbDatabase? _tkbDb;
    private EngineBackedNavigationModule? _navModule;

    /// <summary>
    /// ⭐⭐ W6 — THE node's navmesh (docs/DESIGN_Terrain_World.md §4.1): the <c>INavmeshProvider</c> singleton, the
    /// path solver's navmesh, and what a terrain commit publishes its bake into — one instance, so the background
    /// solver sees a terrain load. Straight lines until a terrain is baked.
    /// </summary>
    public Fdp.Toolkit.Navigation.SwitchableNavmeshProvider Navmesh { get; } = new();

    /// <summary>
    /// ⭐ W6 — bakes the navmesh from each loaded terrain (DotRecast by default — this host composes the solver, so
    /// it must bake; silent-default rule). Null ⇒ no bake: the solver keeps straight lines (a test that wants no bake).
    /// </summary>
    public Fdp.Toolkit.Navigation.INavmeshFactory? NavmeshFactory { get; set; } =
        new Fdp.Toolkit.Navigation.Recast.RecastNavmeshFactory();

    /// <summary>
    /// Core simulation systems pack. Valid after <see cref="SharedApplicationBootstrapper.BootstrapNode"/> returns.
    /// </summary>
    public SimHostCoreLogicPack? CoreLogicPack { get; private set; }

    /// <summary>
    /// Slave orchestration translator. Valid after <see cref="SharedApplicationBootstrapper.BootstrapNode"/> returns.
    /// </summary>
    public ISlaveOrchestrationTranslator? SlaveTranslator { get; private set; }


    /// <summary>
    /// Physics toolkit module. Valid after <see cref="SharedApplicationBootstrapper.BootstrapNode"/> returns.
    /// </summary>
    public PhysicsToolkitModule? PhysicsModule { get; private set; }

    /// <summary>
    /// Behavior registry. Valid after <see cref="SharedApplicationBootstrapper.BootstrapNode"/> returns.
    /// </summary>
    public BehaviorRegistry? BehaviorRegistry { get; private set; }

    /// <summary>Migration services bundle. Valid after BootstrapNode() returns.</summary>
    public MigrationServices? MigrationServices { get; private set; }

    /// <summary>
    /// Loaded road network. Valid after <see cref="SharedApplicationBootstrapper.BootstrapNode"/> returns.
    /// </summary>
    public CarKinem.Road.RoadNetworkBlob? RoadNetwork { get; private set; }

    /// <summary>
    /// The node's trajectory-pool owner — the first resource this host takes from a provider rather
    /// than from whichever module happened to default it (<c>B4b</c>).
    /// </summary>
    /// <remarks>
    /// <para><b>What changed and why it matters.</b> The pool used to be created inside
    /// <c>GroundKinematicsModule</c> (as a <c>??=</c> default) and then threaded by hand from
    /// <c>CoreLogicPack.TrajectoryPool</c> into <c>EngineBackedNavigationModule</c>. That worked, but
    /// only because the single production caller remembered to do it: the solver writes resolved
    /// routes into the pool by handle and the kinematics systems read them back by that handle, so a
    /// consumer handed a different pool produces <b>routes that resolve and vehicles that never
    /// follow them</b> — silently (<c>CE-180</c>).</para>
    ///
    /// <para>Now the node allocates it <b>once</b>, up front, and hands the same instance to every
    /// consumer. Sharing is by construction instead of by care, and the pool finally has an owner with
    /// a lifetime: this bootstrapper disposes the provider, which nothing did before.</para>
    /// </remarks>
    public Hrot.Common.Infrastructure.TrajectoryPoolProvider TrajectoryPool { get; } = new();

    /// <summary>
    /// CE-199 — the DECLARATION half of the resource seam: the keys this role will allocate.
    /// </summary>
    /// <remarks>
    /// <para>⭐ Read when the boot step is BUILT, before <c>BuildContext</c>, so it may depend on
    /// nothing but the role. That is why it is a static role-to-keys map and not
    /// <c>plan.RequiredResources(...)</c>: this node's <c>NodeCompositionPlan</c> cannot exist until
    /// <c>PopulateSystems</c>, which needs the context and the loaded road network.</para>
    ///
    /// <para>⛔ It is not a second source of truth. <c>PopulateSystems</c> cross-checks it against the
    /// capabilities' own declared <c>Needs</c> and throws if they disagree — see
    /// the base bootstrapper's `capabilities` step (CE-4013 D10).</para>
    ///
    /// <para>📐 <c>MuscleGround</c> is the role whose kinematics borrow the trajectory pool the
    /// navigation solver writes; a node without it allocates no pool.</para>
    /// </remarks>
    protected override System.Collections.Generic.IReadOnlyList<string> DeclaredResourceKeys(NodeRole role)
        => MuscleRoleInputs.DeclaredResourceKeys(role);   // ⭐ CE-4270 B7b — the ONE Muscle-role implementation (CGF's fused boot calls it too)

    /// <summary>
    /// CE-199 — the INSTANCE half: the providers that satisfy <see cref="DeclaredResourceKeys"/>.
    /// </summary>
    /// <remarks>
    /// Runs inside the <c>node-resources</c> step, so <see cref="TrajectoryPool"/>'s
    /// <c>Allocate</c> may legally publish into the plan's values. The base frees them
    /// (<c>DisposeResources</c>); this class no longer carries its own copy of that loop.
    /// </remarks>
    protected override System.Collections.Generic.IReadOnlyList<Hrot.Common.Infrastructure.INodeResourceProvider>
        ResolveResources(HrotNodeContext context, NodeRole role)
        => MuscleRoleInputs.ResolveResources(role, TrajectoryPool);



    /// <inheritdoc/>
    /// <remarks>
    /// <para>Calls <see cref="EngineBackedNavigationModule.RegisterProviders"/> here (post-Initialize)
    /// because RegisterProviders requires <c>_navmesh</c>/<c>_registry</c> which are
    /// created by <c>RegisterSystems</c> during <c>Kernel.Initialize()</c> (Phase 7).</para>
    ///
    /// <para>⛔⛔ <b>CE-197 — GUARDED on the capability actually being composed.</b> This used to call
    /// <c>_navModule!.RegisterProviders</c> unconditionally, which was safe only because
    /// <c>PopulateSystems</c> composed <c>NavigationSolver</c> from a hard-coded constant. 📐 The moment
    /// the set is resolved from the declared role, a node without that flag never registers the module,
    /// <c>RegisterSystems</c> never runs, and this throws
    /// <i>"Call RegisterSystems before RegisterProviders."</i> — measured: it reddened five SimHost boot
    /// tests.</para>
    ///
    /// <para>⚠ The unconditional call was an assumption about composition made by a step that does not
    /// control it. Asking the resolved set is what makes a narrower role a supported configuration rather
    /// than a crash.</para>
    /// </remarks>
    protected override void PostInitialize(HrotNodeContext context)
        => MuscleRoleInputs.RegisterNavigationProviders(Capabilities, _navModule, context.World);   // ⭐ CE-4270 B7b — shared with CGF's fused boot

    /// <param name="networkFactory">Optional network factory for DDS setup.</param>
    /// <param name="role">Node role controlling which simulation modules are activated.</param>
    /// <param name="localTempRoot">Root directory for checkpoints and temporary files.</param>
    /// <param name="eventHistoryService">Optional diagnostic event history service.</param>
    /// <param name="hrotConfig">Hrot node configuration.</param>
    /// <param name="simulationRateHz">Simulation rate in Hz for GlobalTime singleton.</param>
    public SimHostNodeBootstrapper(
        INetworkFactory? networkFactory,
        NodeRole role,
        string localTempRoot,
        IDiagnosticEventHistoryService? eventHistoryService,
        HrotNodeConfig hrotConfig,
        float simulationRateHz = 20.0f)
    {
        _networkFactory = networkFactory;
        _role = role;
        _localTempRoot = localTempRoot;
        _eventHistoryService = eventHistoryService;
        _hrotConfig = hrotConfig;
        _simulationRateHz = simulationRateHz;
    }

    /// <inheritdoc/>
    protected override HrotNodeContext BuildContext(HrotNodeConfig config, NodeRole role, INetworkFactory? networkFactory)
    {
        // ⭐⭐⭐ CE-140 step 3, host (b) — the translator list is no longer BUILT HERE.
        //    EntityCreationPack owns it (RegisterSpawningPipeline below): it composes
        //    TkbTranslatorSet.Base() + ExtraTranslators, hands that ONE instance to the ELM and to
        //    NetworkSpawningSystem, and GhostPromotionSystem reads it back off the ELM.
        //
        // ⛔ `.WithTranslators(...)` is DELIBERATELY GONE. It fed NedReplicationModule's
        //    `tkbEntityTranslators`, whose ONLY consumer is GhostPromotionSystem — and that system now
        //    falls back to EntityLifecycleModule.Translators when no explicit list is given. Keeping the
        //    call would hand promotion a SECOND, equal-but-distinct list instance, which is exactly the
        //    thing tkb-1/DESIGN.md §6.3 asks not to happen ("identical for all three systems within the
        //    same node"). ⭐ Dropping it makes that true by CONSTRUCTION rather than by two copies
        //    agreeing.
        //
        // 📐 Measured before removing: `_tkbEntityTranslators` has exactly one read site,
        //    NedReplicationModule.cs:413's GhostPromotionSystem construction.
        var ctx = Hrot.Network.Infrastructure.HrotNodeBuilderReplicationExtensions.BuildReplicatedNode(config, role, networkFactory, GetBehaviorRegistry());   // ⭐ CE-4013 D17

        _tkbDb = ctx.TkbDb;
        return ctx;
    }

    /// <inheritdoc/>
    protected override BehaviorRegistry? GetBehaviorRegistry()
    {
        BehaviorRegistry ??= new BehaviorRegistry();
        return BehaviorRegistry;
    }

    /// <inheritdoc/>
    protected override void RegisterDomainComponents(EntityRepository world)
    {
        SimHostComponentRegistry.RegisterAll(world);
        // TKB-015 — the world's ITkbDatabase is the context's (_tkbDb); HrotNodeBuilder publishes it (CE-4013 D4).
    }

    /// <inheritdoc/>
    protected override ScenarioSerializer BuildSerializer(BehaviorRegistry? registry)
    {
        // ⭐ A live-cluster node loads LENIENTLY: a component the registry cannot resolve is
        //   skipped with a warning, never a hard failure. Measured 2026-09-28 — P4 retired
        //   BrainBlackboard without a migrator and every shipped scenario became unloadable,
        //   the 2PC PrepareLive faulting so an exercise could not start at all. The editor and
        //   CI keep the strict default so a typo still fails loudly. See UnknownComponentPolicy.
        return HrotScenarioSerializerFactory.Build(
            registry ?? new BehaviorRegistry(),
            unknownComponentPolicy: Fdp.Toolkit.Scenario.UnknownComponentPolicy.WarnAndSkip);
    }

    /// <inheritdoc/>
    /// <remarks>⭐ CE-4013 — the plan is built here, in the base's <c>capabilities</c> step (after <c>node-resources</c>, before
    /// <c>system-groups</c>): the first point where the context and the allocated pool exist, as before. The base walks the result.</remarks>
    protected override IReadOnlyList<Hrot.Common.Infrastructure.INodeCapability> ResolveCapabilities(HrotNodeContext context, NodeRole role)
    {
        // ⭐ CE-3127 (R-230) — no boot-time road file: the road graph is part of the TERRAIN (terrain.json `roadNetworks`),
        //   published by TerrainResidency.Commit as ZoneEnvironmentData + the RoadNetworkHolder, which every consumer below
        //   already prefers. The packs get an empty blob as their "no terrain loaded" value, as they did whenever the old
        //   NodeConfiguration.RoadNetworkBlobPath was empty — which no config ever set.
        var roadNetwork = default(CarKinem.Road.RoadNetworkBlob);
        RoadNetwork = roadNetwork;

        // B4b: the pool comes from the node's provider, not from whichever module defaults one.
        // Every consumer below receives THIS instance, so the solver's routes and the kinematics
        // systems' lookups address the same memory by construction (CE-180).
        // ⭐ CE-4270 B7b — the pack, the navigation module and the solver factory are built by the ONE Muscle-role step (MuscleRoleInputs), which
        //   CGF's bootstrapper also calls when it is booted as the fused server.
        var muscle = MuscleRoleInputs.Build(context.EntityMap, roadNetwork, TrajectoryPool, Navmesh, () => _nodeBootstrapper);
        CoreLogicPack = muscle.CoreLogicPack;

        // ⭐ S2b — the owner-side edit-request handlers moved to the base bootstrapper's input group, so
        //   every host applies another node's edit (F-10). ⛔ This host used to call ConfigureForNode a
        //   SECOND time here just to build them — a second factory with its own cluster cache.

        // B4b step 2: the node's units come from a declared plan rather than a hand-written list.
        // ⚠ Resolved once; the base walks the SAME instances at every composition step (CE-4013).
        // ⭐ W6 — the SOLVER shares the navmesh, the pool and (once orchestration built it) the road holder (inside MuscleRoleInputs.Build).
        _navModule = muscle.NavigationModule;

        // ⭐⭐⭐ CE-4270 B10 (§4.1ag, R-3077): the ONE role table, the same one CGF, the fused server and the editor resolve. SimHost's row: the Muscle, no brain
        //   (so the report inbox comes as a capability, CE-2301), SUMO cars mirrored (AQ93 §12 N6, R-4056: rounds, sensors and avoidance see them; idle without a net).
        var plan = RoleCapabilityTable.Plan(new RoleTableInputs
        {
            Muscle         = muscle,
            TrajectoryPool = TrajectoryPool,
            Traffic        = Hrot.Traffic.TrafficPack.Mirroring(),
        });

        // ⭐⭐⭐ B4b step 3 — THE NODE COMPOSES BY ITS DECLARED ROLE, not by a hard-coded constant.
        //
        // ⚠⚠ This replaces `const NodeRole composed = MuscleGround | Perception | NavigationSolver`, and
        //    the measurement the old comment asked for is what made the swap safe — and it did NOT say
        //    what was assumed: 📐 SimHostSubsystem.cs:101 declared `MuscleGround | Perception`,
        //    WITHOUT NavigationSolver, while this host unconditionally composed all three. Resolving by
        //    the declared role as it stood would have SILENTLY DROPPED EngineBackedNavigationModule and
        //    EqsModule — a regression dressed as a refactor.
        //
        // ⇒ ⭐ The fix is to make the DECLARATION true rather than keep overriding it: this host runs the
        //    navigation solver, so its role now says so (SimHostSubsystem.cs:101, SimHostApp.cs:154).
        //    The composed set is then identical to the retired constant BY MEASUREMENT, and the role
        //    flags stop being decorative.
        //
        // ⚠ NodeRole.NavigationSolver is tested in exactly ONE place repo-wide —
        //   NedSimHostPathfindingTranslators.cs:35 — so the blast radius of the honest declaration is
        //   fully enumerated. 📄 See §4.1v for what that pack does and why it stays inert.
        NodeRole composed = _role;

        var capabilities = plan.Resolve(composed);

        // ⭐ CE-199 — every selected Need must have a provider (RequiredResources throws, naming it); the declared-vs-needed cross-check
        //   is the base's now (CE-4013 D10).
        plan.RequiredResources(composed);

        return capabilities;
    }

    /// <inheritdoc/>
    /// <remarks>⭐ CE-4013 — the capabilities' systems are added by the base, right after this hook.</remarks>
    protected override void PopulateSystems(
        HrotNodeContext context,
        List<IEcsModuleSystem> input,
        List<IEcsModuleSystem> sim,
        List<IEcsModuleSystem> postSim)
    {
        // Seed GlobalTime singleton (⭐ CE-4270 B7b — the ONE Muscle-role seed, shared with CGF's fused boot)
        MuscleRoleInputs.SeedGlobalTime(context.World, _simulationRateHz);
    }

    /// <inheritdoc/>
    /// <summary>⭐ <c>CE-3354</c> ③ — where this host's 3-D map step polls the map's build from (lazy: the map is attached after the bootstrap; a null result = no map ⇒ the step resolves). The map-in-load setting makes THIS node's load wait for it.</summary>
    public System.Func<Fdp.Toolkit.Orchestration.IMapBuildStatus?>? MapBuildStatus { get; set; }

    /// <summary>⭐ Node Features F-17 — this host supplies a 3-D map status, so it declares `map3d.in-load` (default false here).</summary>
    protected override System.Collections.Generic.IEnumerable<Hrot.Common.Infrastructure.FeatureDeclaration> HostFeatures
        => new[] { Hrot.Common.Infrastructure.Map3dInLoadFeature.Declaration(onByDefault: false) }
           .Concat(Hrot.Common.Infrastructure.HudFeatures.Declarations(playerDefault: false));   // ⭐ CE-4148 — the gameplay HUD aids (hud.*), default ON for the Player only

    protected override ClusterSlave BuildOrchestration(
        HrotNodeContext context,
        TogglableSimulationGroup simGroup,
        TogglablePostSimulationGroup postSimGroup,
        ScenarioSerializer serializer)
    {
        // Create services needed by diagnostics handler
        var archService = new ArchitectureDiagnosticsService(context.Kernel);
        var entityService = new EntityStateExtractionService(context.World, context.EntityMap);
        var logService = new LogArchiveExtractionService(
            string.IsNullOrWhiteSpace(_hrotConfig.LogDirectory)
                ? System.IO.Path.Combine(System.AppContext.BaseDirectory, "logs")
                : _hrotConfig.LogDirectory,
            _hrotConfig.SubsystemName,
            context.NodeId);
        var diagHandler = new DiagnosticsDumpClusterOpHandler(
            _eventHistoryService!, archService, entityService, logService, _hrotConfig);

        _nodeBootstrapper = new NodeBootstrapper(_networkFactory);
        // ⭐ W6 — the terrain this node loads bakes into the shared navmesh (silent-default rule: we hold it, so pass it).
        if (NavmeshFactory != null) _nodeBootstrapper.AttachNavmesh(NavmeshFactory, Navmesh);
        MigrationServices = _nodeBootstrapper.RegisterMigrationServices(
            _role,
            writerIdentifier: _role.HasFlag(NodeRole.Brain) ? Hrot.Common.Scenario.HrotSubsystemTypes.Cgf : Hrot.Common.Scenario.HrotSubsystemTypes.SimHost);
        // ⭐ CE-4270 B11 — the ONE orchestration step (HostOrchestration). ⚠ No input or post-simulation group (as before, §4.1ag B11 drift table).
        var slave = Hrot.EcsNode.Hosting.HostOrchestration.Build(_nodeBootstrapper, _role, context,
            new Hrot.EcsNode.Hosting.OrchestrationSpec
            {
                SubsystemName   = "SimHost",
                LocalTempRoot   = _localTempRoot,
                Serializer      = serializer,
                SimGroup        = simGroup,
                DiagnosticsDump = diagHandler,
                MapBuildStatus  = MapBuildStatus,   // ⭐ CE-3354 ③ — this host HAS a map when it shows one (SimHostApp wires it)
            });   // ⭐ CE-3354 ③ — this host HAS a map when it shows one (SimHostApp wires it), so it passes it

        SlaveTranslator = _nodeBootstrapper.SlaveTranslator;
        return slave;
    }

    /// <inheritdoc/>
    protected override void RegisterSpawningPipeline(HrotNodeContext context)
    {
        // The physics toolkit's raycast batch first: the spawn pipeline and the muscle capabilities raycast through it.
        PhysicsModule = new PhysicsToolkitModule();
        PhysicsModule.Initialize(context.World);

        // ⭐ CE-4270 B4 (§4.1ag) — the ONE shared spawning step: the pack (AI diagnostics + animation TKB translators; not the broadcast
        //   arbiter — the Brain is), then the capabilities, then the genesis tail — the order every host takes after D5 (R-3077).
        EntityCreation = Hrot.EcsNode.Hosting.HostSpawning.Register(context, ConfiguredNetworkFactory,
            new Hrot.EcsNode.Hosting.SpawningSpec
            {
                IsBroadcastArbiter = false,
                ExtraTranslators   = new ITkbEntityTranslator[]
                {
                    new Hrot.SimHost.Diagnostics.AiDiagnosticsTkbTranslator(),
                    new Hrot.MuscleCharacter.Animation.Translators.AnimationTkbTranslator(null),
                },
            },
            () => RegisterCapabilities(context));
    }

    /// <inheritdoc/>
    protected override void RegisterNetworkTranslators(
        HrotNodeContext context,
        INetworkFactory? configuredFactory)
    {
        // ⭐ CE-4270 B7b — the ONE SimHost translator set (MuscleRoleInputs), shared with CGF's fused boot.
        // ⭐ CE-2121 slice ② — the body's half of the stance wire: the Brain's request in, the body's report out (gated on the
        //   MuscleGround group's descriptor, not on the entity's owner). 📄 docs/DESIGN_Decision_Layer.md §3.3g.
        MuscleRoleInputs.RegisterTranslators(context, configuredFactory, CoreLogicPack!.TrajectoryPool, NodeRole.MuscleGround);
    }
}
