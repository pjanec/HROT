using System;
using System.Collections.Generic;
using CarKinem.Core;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.NetworkSpawning.Systems;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Hrot.Common.Systems;
using Hrot.Map.Common;
using Hrot.Map.Common.Translators;
using Hrot.Network.Systems;
using Hrot.Network.Translators;
using Hrot.Common;
using Hrot.Common.Abstractions;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Scheduling;
using Fdp.Network.Cyclone.Modules;
using Fdp.Network.Cyclone.Systems;
using Fdp.Toolkit.Navigation;
using FdpIDescriptorTranslator = Fdp.Interfaces.IDescriptorTranslator;
using DescriptorOwnershipMap    = Fdp.Toolkit.Replication.Services.DescriptorOwnershipMap;
using EDescriptorType           = Hrot.NED.Descriptors.EDescriptorType;

namespace Hrot.Network.Replication;

/// <summary>
/// Composite <see cref="IEcsModule"/> that bundles NED translator packs with their
/// tightly-coupled ECS systems (ghost lifecycle, dead-reckoning, cleanup) behind a
/// single module boundary.
///
/// <para><b>Role mapping:</b></para>
/// <list type="table">
///   <item>
///     <term><see cref="NodeRole.MuscleGround"/></term>
///     <description>Shared + kinematic packs; GhostCreationSystem; SmartEgressSystem; cleanup.</description>
///   </item>
///   <item>
///     <term><see cref="NodeRole.ImageGenerator"/></term>
///     <description>Shared pack + EntityStatesIngressPack; GhostCreationSystem; DeadReckoningSyncSystem (driveFromNetwork=true).</description>
///   </item>
///   <item>
///     <term><see cref="NodeRole.Brain"/></term>
///     <description>Shared + cognitive packs; GhostCreationSystem; SmartEgressSystem; cleanup.</description>
///   </item>
/// </list>
/// </summary>
public sealed class NedReplicationModule : INedReplicationModule
{
    public string Name => "NedReplication";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    // ── Role state ────────────────────────────────────────────────────────────
    private readonly NodeRole _role;
    private readonly bool _roleHasMuscle;
    private readonly bool _roleHasIG;
    private readonly bool _roleHasBrain;
    private readonly bool _driveFromNetwork;

    // ── DDS params (held for RegisterSystems) ─────────────────────────────────
    private readonly DdsParticipant?     _participant;
    private readonly NetworkEntityMap    _entityMap;
    private readonly IGeographicTransform _geoTransform;
    private readonly FdpEventBus         _eventBus;
    private readonly int                 _localNodeId;

    // ── Ghost lifecycle deps (IG role — GhostPromotionSystem) ─────────────────
    private readonly ITkbDatabase?         _tkbDb;
    private readonly EntityLifecycleModule? _lifecycleModule;

    // ── Translator lists ───────────────────────────────────────────────────────
    private readonly IEnumerable<INetworkTranslator> _sharedTranslators;
    private readonly IEnumerable<FdpIDescriptorTranslator>? _kinematicTranslators;
    private readonly IEnumerable<FdpIDescriptorTranslator>? _cognitiveTranslators;

    // ── Descriptor → ECS component mapping (Single Source of Truth) ───────────
    // Populated from FdpIDescriptorTranslator.TargetComponentIds during construction
    // so that OwnershipIngressSystem and DeferredTakeoverSystem can call
    // SetAuthority(entity, exactComponentId, bool) without try/catch.
    private readonly DescriptorOwnershipMap _descriptorOwnershipMap = new();

    // ── Pre-genesis routing translators (Brain egress / Muscle ingress) ────────
    private readonly DeferredTakeOwnershipEgressTranslator?  _dtoEgress;
    private readonly DeferredTakeOwnershipIngressTranslator? _dtoIngress;

    private CycloneNetworkCleanupSystem? _cleanupSystem;

    /// <summary>
    /// Ghost-creation system creates replica entities from incoming DDS samples.
    /// Exposed so Phase 4 can wire it into <c>ReplayLoadClusterOpHandler</c>.
    /// </summary>
    public GhostCreationSystem GhostCreationSystem { get; }

    /// <summary>
    /// Groups <see cref="GhostCreationSystem"/> under a lifecycle gate for replay control.
    /// Phase 4 <c>ReplayLoadClusterStateHandler</c> sets <see cref="NetworkLifecycleSystemGroup.Enabled"/>
    /// to <c>false</c> during replay playback to prevent ghost promotions.
    /// </summary>
    public NetworkLifecycleSystemGroup NetworkLifecycleGroup { get; }

    /// <summary>Exposes the <see cref="CycloneNetworkCleanupSystem"/> for composition-root afterSeek wiring.</summary>
    public CycloneNetworkCleanupSystem? CleanupSystem => _cleanupSystem;

    /// <inheritdoc/>
    public Action? AfterSeekCallback =>
        _cleanupSystem != null ? (Action)(() => {
            //_cleanupSystem.ResetTracking();  
        }) : null;

    /// <summary>
    /// Whether dead-reckoning is configured to run on all remote entities (<c>true</c>)
    /// or only on entities still in <c>EntityLifecycle.Ghost</c> state (<c>false</c>).
    /// <para>
    /// <c>true</c> for pure <see cref="NodeRole.ImageGenerator"/>; <c>false</c> for
    /// combined roles that also own entities locally (e.g. <see cref="NodeRole.AllInOne"/>).
    /// </para>
    /// </summary>
    public bool DriveFromNetwork => _driveFromNetwork;

    /// <summary>
    /// Constructs the module for the given role.
    /// </summary>
    /// <param name="participant">Live DDS participant (may be <c>null</c> in headless tests).</param>
    /// <param name="role">Node role; must include at least one of MuscleGround, ImageGenerator, Brain, or AllInOne.</param>
    /// <param name="entityMap">Shared network entity map.</param>
    /// <param name="geoTransform">Geodetic coordinate transform.</param>
    /// <param name="eventBus">Application event bus.</param>
    /// <param name="localNodeId">Local DDS node identifier.</param>
    /// <param name="domainId">DDS domain ID (unused in this version; reserved for future use).</param>
    /// <param name="behaviorRegistry">
    ///   Optional behavior registry forwarded to <see cref="CognitiveTranslatorPack"/> for
    ///   <c>EntityMissionEgressTranslator</c> and <c>EntityMissionIngressTranslator</c>.
    /// </param>
    /// <param name="tkbDb">
    ///   Optional TKB database needed by <see cref="GhostPromotionSystem"/> (ImageGenerator role).
    ///   When <c>null</c>, ghost promotion is disabled and entities remain in Ghost state.
    /// </param>
    /// <param name="lifecycleModule">
    ///   Optional <see cref="EntityLifecycleModule"/> that <see cref="GhostPromotionSystem"/>
    ///   uses to look up TKB templates for ghost-to-Constructing lifecycle transitions.
    ///   Required when <paramref name="tkbDb"/> is provided.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="role"/> is not one of the supported replication roles
    /// (MuscleGround, ImageGenerator, Brain, AllInOne).
    /// </exception>
    public NedReplicationModule(
        DdsParticipant?       participant,
        NodeRole              role,
        NetworkEntityMap      entityMap,
        IGeographicTransform  geoTransform,
        FdpEventBus           eventBus,
        int                   localNodeId,
        int                   domainId,
        BehaviorRegistry?     behaviorRegistry  = null,
        ITkbDatabase?         tkbDb             = null,
        EntityLifecycleModule? lifecycleModule  = null)
    {
        _participant     = participant;
        _role            = role;
        _entityMap       = entityMap  ?? throw new ArgumentNullException(nameof(entityMap));
        _geoTransform    = geoTransform ?? throw new ArgumentNullException(nameof(geoTransform));
        _eventBus        = eventBus   ?? throw new ArgumentNullException(nameof(eventBus));
        _localNodeId     = localNodeId;
        _tkbDb           = tkbDb;
        _lifecycleModule = lifecycleModule;

        // Validate role
        _roleHasMuscle = role.HasFlag(NodeRole.MuscleGround);
        _roleHasIG     = role.HasFlag(NodeRole.ImageGenerator);
        _roleHasBrain  = role.HasFlag(NodeRole.Brain);

        if (!_roleHasMuscle && !_roleHasIG && !_roleHasBrain)
            throw new ArgumentException(
                $"NedReplicationModule requires a role with MuscleGround, ImageGenerator, or Brain. Got: {role}",
                nameof(role));

        // driveFromNetwork = true when ONLY IG (no local physics)
        // driveFromNetwork = false when Muscle or AllInOne is present (local entities must not be overridden)
        _driveFromNetwork = !_roleHasMuscle && !_roleHasBrain;

        // Ghost creation — shared by all ingress translators + replay handler
        GhostCreationSystem = new GhostCreationSystem(entityMap);

        var lifecycleInnerSystems = new List<IEcsModuleSystem> { GhostCreationSystem };


        // ── Ghost Destruction (Pure-Brain Only) ──
        // Pure Brains receive remote entities as ghosts and must purge them instantly on DISPOSE.
        // If this ran on Muscle/AllInOne, it would consume DestroyEntityCommand instantly and
        // bypass the NetworkSpawningSystem's TearDown phase, preventing EntityMaster DISPOSE 
        // packets from being sent to the IG (leaving zombie entities).
        if (_roleHasBrain && !_roleHasMuscle && !_roleHasIG)
            lifecycleInnerSystems.Add(new GhostDestructionSystem(_entityMap));

        // ── Deferred Takeover (Muscle / AllInOne Only) ──
        // Executes the split-authority handover. When a Brain creates an entity and delegates 
        // physics to the Muscle, this system claims the local ECS authority bits (e.g., SimTransform) 
        // once the ghost finishes constructing.
        if (_roleHasMuscle)
            lifecycleInnerSystems.Add(new DeferredTakeoverSystem(_entityMap, _localNodeId, _descriptorOwnershipMap, _tkbDb));

        NetworkLifecycleGroup = new NetworkLifecycleSystemGroup(lifecycleInnerSystems.ToArray());

        // Build translator sets — deferred until RegisterSystems to allow null-participant
        // headless contexts to construct the module without creating DDS writers/readers.
        if (participant != null)
        {
            _sharedTranslators = SharedTranslatorPack.Create(
                participant, entityMap, localNodeId, eventBus, GhostCreationSystem, geoTransform);

            if (_roleHasMuscle)
                _kinematicTranslators = KinematicTranslatorPack.Create(
                    participant, entityMap, geoTransform, localNodeId: localNodeId);

            if (_roleHasBrain)
                _cognitiveTranslators = CognitiveTranslatorPack.Create(
                    participant, entityMap, geoTransform,
                    behaviorRegistry,
                    GhostCreationSystem,
                    localNodeId: localNodeId);

            // DeferredTakeOwnership: Brain publishes, Muscle receives.
            if (_roleHasBrain)
                _dtoEgress  = new DeferredTakeOwnershipEgressTranslator(participant, localNodeId: localNodeId);
            if (_roleHasMuscle)
                _dtoIngress = new DeferredTakeOwnershipIngressTranslator(
                    participant, entityMap, GhostCreationSystem, localNodeId);
        }
        else
        {
            // Headless / test mode — no DDS translators
            _sharedTranslators    = System.Array.Empty<INetworkTranslator>();
            _kinematicTranslators = null;
            _cognitiveTranslators = null;
        }

        // Populate DescriptorOwnershipMap from every translator's TargetComponentIds.
        // This is the Single Source of Truth for descriptor → ECS component ID mapping.
        PopulateDescriptorOwnershipMap();
    }

    public void RegisterSystems(ISystemRegistry registry)
    {
        // ── Ghost lifecycle systems (all roles) ─────────────────────────────
        registry.RegisterSystem(GhostCreationSystem);

        // ── Translator routing systems ───────────────────────────────────────
        var allTranslators = new List<INetworkTranslator>(_sharedTranslators);
        if (_roleHasMuscle && _kinematicTranslators != null)
            allTranslators.AddRange(_kinematicTranslators);
        if (_roleHasBrain && _cognitiveTranslators != null)
            allTranslators.AddRange(_cognitiveTranslators);

        // DeferredTakeOwnership translators are inserted FIRST on Muscle (ingress before EntityMaster)
        // and added at the end on Brain (egress after cognitive pack).
        var ingressTranslators = new List<INetworkTranslator>(allTranslators.Count + 1);
        if (_dtoIngress != null) ingressTranslators.Add(_dtoIngress);
        foreach (var t in allTranslators)
        {
            if ((t.Direction & TranslatorDirection.Ingress) != 0)
                ingressTranslators.Add(t);
        }

        var egressTranslators = new List<INetworkTranslator>(allTranslators.Count + 1);
        foreach (var t in allTranslators)
        {
            if ((t.Direction & TranslatorDirection.Egress) != 0)
                egressTranslators.Add(t);
        }
        if (_dtoEgress != null) egressTranslators.Add(_dtoEgress);

        // Register ingress + egress systems only when a live DDS participant is available.
        // For pure ImageGenerator (no Muscle, no Brain): skip the shared CycloneNetworkIngressSystem
        // because EntityStatesIngressPack (registered below) already provides its own
        // CycloneNetworkIngressSystem with EntityMasterIngressTranslator.
        // Having two CycloneNetworkIngressSystem instances polling the same EntityMaster DDS topic
        // would cause double ghost-creation and corrupt the NetworkEntityMap.
        if (_participant != null)
        {
            bool pureIg = _roleHasIG && !_roleHasMuscle && !_roleHasBrain;
            if (!pureIg)
            {
                registry.RegisterSystem(new CycloneNetworkIngressSystem(ingressTranslators.ToArray()));
                registry.RegisterSystem(new CycloneEgressSystem(egressTranslators.ToArray()));
            }
        }

        // ── ImageGenerator: inline EntityStatesIngressPack + ghost lifecycle ──
        // EntityStatesIngressPack is scoped to PURE ImageGenerator only.
        // For AllInOne the Muscle path already owns entity lifecycle locally; injecting
        // a second EntityMasterIngressTranslator would cause self-ghosting (SimHost
        // receiving its own EntityMaster publications and attempting to create duplicates).
        bool pureIgRole = _roleHasIG && !_roleHasMuscle && !_roleHasBrain;
        if (pureIgRole)
        {
            if (_participant != null)
            {
                var igPack = new EntityStatesIngressPack(
                    PackRole.Ingress, _participant, _entityMap, _localNodeId, _eventBus,
                    GhostCreationSystem, _geoTransform);
                igPack.RegisterSystems(registry);
            }

            // IG ghost lifecycle: ownership tracking + promotion + sub-entity cleanup.
            // These replace the legacy ReplicationLogicModule for pure IG nodes.
            registry.RegisterSystem(new OwnershipIngressSystem(_entityMap, _localNodeId, _descriptorOwnershipMap));
            if (_tkbDb != null && _lifecycleModule != null)
                registry.RegisterSystem(new GhostPromotionSystem(_tkbDb, _lifecycleModule));
            registry.RegisterSystem(new SubEntityCleanupSystem());

            // DR sync -- smooth ALL remote entities (IG can create owned entities as well!)
            registry.RegisterSystem(new DeadReckoningSyncSystem(driveFromNetwork: false));
        }
        else if (_roleHasIG)
        {
            // AllInOne/combined role: DR sync with driveFromNetwork=false so locally-owned
            // entities are not overridden by dead-reckoning.
            registry.RegisterSystem(new DeadReckoningSyncSystem(driveFromNetwork: false));
        }

        // ── Role-specific systems ────────────────────────────────────────────
        if (_roleHasMuscle || _roleHasBrain)
            registry.RegisterSystem(new SmartEgressSystem());

        // ── Ghost destruction (pure-Brain only) ─────────────────────────────────
        // Brain role receives entities from remote Muscle nodes as ghosts; when the remote
        // owner destroys an entity, EntityMasterIngressTranslator publishes DestroyEntityCommand
        // on the local event bus — GhostDestructionSystem consumes it and purges the ghost.
        //
        // For AllInOne, entities are locally-owned and must go through NetworkSpawningSystem's
        // TearDown lifecycle when destroyed. If GhostDestructionSystem ran here too, it would
        // consume the DestroyEntityCommand first and bypass TearDown, skipping EntityMaster
        // DISPOSE publication to DDS (and thus the IG ghost would never be removed).
        bool pureBrainRole = _roleHasBrain && !_roleHasMuscle && !_roleHasIG;

        // ── OwnershipIngressSystem (pure-Brain only) ─────────────────────────
        // When running split-authority (Brain + Muscle), the Muscle's DeferredTakeoverSystem
        // publishes OwnershipUpdate bus events that OwnershipUpdateTranslator writes to DDS.
        // On the Brain, OwnershipUpdateTranslator.PollIngress re-publishes them onto the local
        // bus, and this system consumes them to drop the Brain's own authority bits.
        if (pureBrainRole)
        {
            registry.RegisterSystem(new OwnershipIngressSystem(_entityMap, _localNodeId, _descriptorOwnershipMap));
            registry.RegisterSystem(new LocalAuthorityYieldSystem(_entityMap, _localNodeId, _descriptorOwnershipMap));
        }



		// ── DeferredTakeover (Muscle and AllInOne only) ──────────────────────
		// Runs BeforeSync: entity must be Constructing + have PendingAuthorityGrants.
		// Ghost promotion for Muscle: promotes ghosts received from remote Brain (CGF) nodes.
		// Pure-IG ghost promotion is registered above (pureIgRole block). Muscle needs a
		// separate registration so that CGF-spawned entities (WorldPos delegated to Muscle)
		// transition from Ghost → Constructing before DeferredTakeoverSystem claims authority.
		if( _roleHasMuscle && _tkbDb != null && _lifecycleModule != null)
            registry.RegisterSystem(new GhostPromotionSystem(_tkbDb, _lifecycleModule));

        // ── Cleanup systems (all roles) ──────────────────────────────────────
        var allCleanupTranslators = new List<FdpIDescriptorTranslator>(allTranslators.OfType<FdpIDescriptorTranslator>());
        if (_dtoIngress != null) allCleanupTranslators.Add(_dtoIngress);
        if (_dtoEgress  != null) allCleanupTranslators.Add(_dtoEgress);
        _cleanupSystem = new CycloneNetworkCleanupSystem(allCleanupTranslators);
        registry.RegisterSystem(_cleanupSystem);
        registry.RegisterSystem(new DisposalMonitoringSystem(_entityMap));
    }

    // ── DescriptorOwnershipMap population ────────────────────────────────────

    private void PopulateDescriptorOwnershipMap()
    {
        foreach (var t in _sharedTranslators.OfType<FdpIDescriptorTranslator>())
            _descriptorOwnershipMap.RegisterFromTranslator(t.DescriptorOrdinal, t.TargetComponentIds);
        if (_kinematicTranslators != null)
            foreach (var t in _kinematicTranslators)
                _descriptorOwnershipMap.RegisterFromTranslator(t.DescriptorOrdinal, t.TargetComponentIds);
        if (_cognitiveTranslators != null)
            foreach (var t in _cognitiveTranslators)
                _descriptorOwnershipMap.RegisterFromTranslator(t.DescriptorOrdinal, t.TargetComponentIds);

        // Explicit mapping: WorldPos descriptor represents the entire physical/kinematic authority block.
        // GeoSpatialIngressTranslator writes NetworkTransform (ordinal 10), but the authoritative
        // components on the Muscle side are SimTransform + SimVelocity (fed by SimTransformBridgeSystem)
        // plus the CarKinem physics state that CarKinematicsSystem requires write access to.
        // DeferredTakeoverSystem uses these mappings to SetAuthority(entity, componentId, true)
        // when the Muscle receives a split-authority WorldPos delegation.
        // All five IDs are passed in a single call to avoid overwriting the prior entry.
        _descriptorOwnershipMap.RegisterMapping(
            (long)EDescriptorType.dtWorldPos,
            ComponentType<SimTransform>.ID,
            ComponentType<SimVelocity>.ID,
            ComponentType<VehicleState>.ID,
            ComponentType<VehicleParams>.ID,
            ComponentType<NavState>.ID);

        // Explicit mapping: NavigationStatus descriptor -> NavigationStatus ECS component.
        // NavigationStatusEgressTranslator (Muscle-only) provides TargetComponentIds for Muscle,
        // but the Brain's NavigationStatusIngressTranslator has empty TargetComponentIds.
        // This mapping ensures OwnershipIngressSystem on Brain clears NavigationStatus authority
        // when SimHost claims dtNavigationStatus via DeferredTakeover.
        _descriptorOwnershipMap.RegisterMapping(
            (long)EDescriptorType.dtNavigationStatus,
            NavigationContractsComponentIds.NavigationStatus);
    }

    public void Tick(ISimulationView view, float dt)
    {
        NetworkLifecycleGroup.ExecuteGroup(view, dt);
    }

    // ── Ghost destruction system ──────────────────────────────────────────────

    /// <summary>
    /// Purges ghost entities when their remote owner publishes a DDS DISPOSE.
    /// <para>
    /// <see cref="EntityMasterIngressTranslator"/> publishes <see cref="DestroyEntityCommand"/>
    /// on the local event bus when a remote EntityMaster goes DISPOSE; this system
    /// consumes that event and removes the corresponding local ghost from the ECS world
    /// and from <see cref="NetworkEntityMap"/>.
    /// </para>
    /// </summary>
    /// <summary>
    /// Clears authority bits on the Brain for descriptors delegated to remote nodes.
    /// Runs in BeforeSync after <see cref="NetworkSpawningSystem"/> so the entity is already
    /// registered in <see cref="NetworkEntityMap"/> when this system executes.
    /// </summary>
    [UpdateInPhase(SystemPhase.BeforeSync)]
    [UpdateAfter(typeof(NetworkSpawningSystem))]
    private sealed class LocalAuthorityYieldSystem : IEcsModuleSystem
    {
        private readonly NetworkEntityMap    _entityMap;
        private readonly int                 _localNodeId;
        private readonly DescriptorOwnershipMap _descriptorMap;

        internal LocalAuthorityYieldSystem(
            NetworkEntityMap    entityMap,
            int                 localNodeId,
            DescriptorOwnershipMap descriptorMap)
        {
            _entityMap    = entityMap;
            _localNodeId  = localNodeId;
            _descriptorMap = descriptorMap;
        }

        public void Execute(ISimulationView view, float dt)
        {
            if (view is not EntityRepository repo) return;

            var commands = view.ReadManagedEvents<DeferredTakeOwnershipCommand>();
            foreach (var cmd in commands)
            {
                if (!_entityMap.TryGetEntity(cmd.NetworkId, out Entity entity)) continue;
                if (!repo.IsAlive(entity)) continue;

                foreach (var grant in cmd.Grants)
                {
                    if (grant.NodeId == _localNodeId) continue;

                    var componentIds = _descriptorMap.GetComponentIdsForDescriptor(grant.DescriptorTypeId);
                    foreach (int cid in componentIds)
                    {
                        if (repo.HasComponentByTypeId(entity, cid))
                            repo.SetAuthority(entity, cid, false);
                    }
                }
            }
        }
    }

    [UpdateInPhase(SystemPhase.PostSimulation)]
    private sealed class GhostDestructionSystem : IEcsModuleSystem
    {
        private readonly NetworkEntityMap _entityMap;

        public GhostDestructionSystem(NetworkEntityMap entityMap)
            => _entityMap = entityMap;

        public void Execute(ISimulationView view, float dt)
        {
            var world = view as EntityRepository;
            if (world == null) return;

            foreach (var cmd in view.ReadManagedEvents<DestroyEntityCommand>())
            {
                if (!cmd.IsRemote)
                    continue;

                if (_entityMap.TryGetEntity(cmd.NetworkId, out var entity))
                {
                    _entityMap.Unregister(cmd.NetworkId, view.Tick);
                    if (world.IsAlive(entity))
                        world.DestroyEntity(entity);
                }
            }
        }
    }
}
