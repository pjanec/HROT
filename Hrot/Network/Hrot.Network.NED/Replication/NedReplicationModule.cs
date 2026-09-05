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
    private readonly IReadOnlyList<ITkbEntityTranslator>? _tkbEntityTranslators;

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
    /// <param name="tkbEntityTranslators">
    ///   Optional translator list forwarded to <see cref="GhostPromotionSystem"/> so that
    ///   component injection uses the same translator instances as
    ///   <see cref="Fdp.Toolkit.NetworkSpawning.Systems.NetworkSpawningSystem"/> and
    ///   <see cref="Fdp.Toolkit.Lifecycle.Systems.BlueprintApplicationSystem"/>.
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
        EntityLifecycleModule? lifecycleModule  = null,
        IReadOnlyList<ITkbEntityTranslator>? tkbEntityTranslators = null)
    {
        _participant     = participant;
        _role            = role;
        _entityMap       = entityMap  ?? throw new ArgumentNullException(nameof(entityMap));
        _geoTransform    = geoTransform ?? throw new ArgumentNullException(nameof(geoTransform));
        _eventBus        = eventBus   ?? throw new ArgumentNullException(nameof(eventBus));
        _localNodeId     = localNodeId;
        _tkbDb           = tkbDb;
        _lifecycleModule = lifecycleModule;
        _tkbEntityTranslators = tkbEntityTranslators;

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


        // ── Deferred Takeover — ROLE-INDEPENDENT (CE-142) ──
        // Executes a split-authority handover: claims the local ECS authority bits for any
        // descriptor grant addressed to this node, once the ghost finishes constructing.
        //
        // ⭐⭐⭐ CE-142 — this used to be gated on _roleHasMuscle. Measured: the class contains
        //   ZERO role logic; its only "Muscle" was a comment. Gating it on the role removed a
        //   capability from every other node, which R-138 forbids ("the ECS component ownership
        //   is transferrable per entity during entity lifetime … nodes should be equal").
        //   Delegation is "here is a grant, addressed to a node id" — node-agnostic in both
        //   directions. Role belongs on the POLICY (IOwnershipDistributionStrategy), never here.
        //
        // ⭐ Safe: ExecuteTakeover is doubly guarded — it self-filters on `ownerNodeId !=
        //   _localNodeId`, then checks HasComponentByTypeId per component, so a grant naming a
        //   component this node does not carry is skipped silently rather than throwing.
        //   A node nobody addresses a grant to pays one idle system.
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

            // ── DeferredTakeOwnership transport — ROLE-INDEPENDENT (CE-142) ──
            // ⭐⭐⭐ These were gated `_roleHasBrain` (egress) and `_roleHasMuscle` (ingress), i.e.
            //   "Brain publishes, Muscle receives" — a one-directional assumption baked into the
            //   TRANSPORT. Measured: neither class has role logic. The egress converts a
            //   DescriptorGrant into one DDS sample; the ingress already self-filters to entries
            //   whose NodeId equals the local node.
            //
            // 🔒 R-138: ownership is per-component, dynamic and transferable, and NodeRole is a
            //   convention, never a protocol restriction. Delegation must therefore work in EVERY
            //   direction — including a Muscle-originated entity handing its cognitive descriptors
            //   to a Brain node, which the old gating made silently impossible: the originator had
            //   no egress translator, so its grants were computed and then dropped with no error.
            //
            // ⭐ Role now selects only the POLICY (which grants are computed at all — see
            //   IOwnershipDistributionStrategy and CreateEntityRequestSystem's `_ownershipStrategy
            //   != null` lever). A node nobody addresses grants to pays two idle translators.
            _dtoEgress  = new DeferredTakeOwnershipEgressTranslator(participant, localNodeId: localNodeId);
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


        if (_participant != null)
        {
            bool pureIg = _roleHasIG && !_roleHasMuscle && !_roleHasBrain;
            if (!pureIg)
                registry.RegisterSystem(new CycloneNetworkIngressSystem(ingressTranslators.ToArray()));
            registry.RegisterSystem(new CycloneEgressSystem(egressTranslators.ToArray()));
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



        // ── Ghost promotion — ROLE-INDEPENDENT ───────────────────────────────
        // ⭐⭐⭐ CE-155. This registration used to exist TWICE, each behind a role gate: once inside
        //   the pureIgRole block and once behind `_roleHasMuscle`. Pure-Brain (CGF) matched NEITHER,
        //   so a CGF that RECEIVES a ghost it did not spawn never promoted it — the ghost kept only
        //   its replicated network components and never gained its TKB projection.
        //
        // 📐 MEASURED 2026-09-01 (MissionToMovementChainProbe): SimHost spawns a tank, CGF receives
        //   the ghost, a MoveToLocation MissionControlRequest is ACKed and lands correctly as
        //   MissionPlanQueue{Phase 0/1} + ActiveMissionPlan{BehaviorName="MoveToLocation"} on the CGF
        //   ghost — and then nothing happens, forever. The CGF ghost carried 12 components and NONE
        //   of the brain tier; MissionAdapterSystem queries `MissionPlanQueue AND BehaviorState`, so
        //   the missing BehaviorState made it a silent zero-iteration loop. The whole cognitive chain
        //   (tactical intent → behaviour → BTree → LocomotionChannel → NavigationIntent → NavState →
        //   kinematics) never started. Meanwhile the SimHost copy of the same entity had all 35
        //   components including the entire brain tier — the tiers were exactly inverted.
        //
        // 🔒 Q65-B (Architect_Question_65_Entity_Genesis_Uniformity.md §4) prescribes precisely this:
        //   "collapse the two role-gated sites into ONE registration valid for ANY role, once _tkbDb
        //   and _lifecycleModule are present."
        //
        // ⚠ Q65-B also said to sequence this AFTER Q65-A′, because "before A′, pure-Brain promotion
        //   is dead code". That caveat's PREMISE — Q65 obstacle 2b's "pure-Brain spawns rather than
        //   receives" — is superseded by R-138 (2026-09-01, later than both): every ECS node can
        //   create entities, nodes are equal, and NodeRole is a convention rather than a protocol
        //   restriction. A Brain node receiving a ghost of an entity another node originated is
        //   therefore a normal configuration, not dead code, and gating promotion on the role
        //   removes a capability — which Q65 section 0's governing ruling forbids without exception.
        //
        // ⛔ HONEST SCOPE: this turns NO test green on its own. Measured 2026-09-01, before and
        //   after: the same 9 reds in the cluster suite. Ghost promotion is only the first of three
        //   pieces — see CE-142 for the delegation transport and the tracker for the remaining
        //   POLICY half (nothing computes Brain-ward grants yet). An earlier version of this comment
        //   claimed the caveat was refuted by the movement test's behaviour; that argument was
        //   circular, because that test reaches CGF through a spawn hook the design excludes.
        //
        // ⭐ Safe by tkb-1/DESIGN.md §6.5b gate ②: every translator guards each write with
        //   IsComponentTypeRegistered<T>(), so widening the ROLE gate cannot write a component a host
        //   never registered. The per-host lever is the REGISTRATION SET, never the role.
        //
        // ⚠ The two null guards remain the real (and silent) per-host lever — a role that supplies no
        //   TKB database still skips promotion with no diagnostic. Not converted to a throw here:
        //   which hosts pass null has not been measured.
        if (_tkbDb != null && _lifecycleModule != null)
            registry.RegisterSystem(new GhostPromotionSystem(_tkbDb, _lifecycleModule, _tkbEntityTranslators));
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
        ContributeDescriptorPairings(view);

        NetworkLifecycleGroup.ExecuteGroup(view, dt);
    }

    /// <summary>⭐ Set once, on the first tick that hands this module a world.</summary>
    private bool _contributedDescriptorPairings;

    /// <summary>
    /// ⭐⭐⭐ <b><c>AX-022</c> — publishes this module's descriptor↔component pairings into the WORLD, so the
    /// attribute apply path sees everything this module knows.</b>
    ///
    /// <para>📄 <c>docs/blueprints/Architect_Question_59_…md</c> §13.6.</para>
    ///
    /// <para>🔴 <b>The gap this closes.</b> <c>AX-019</c> made the FDP attribute path resolve descriptor
    /// ordinals from a PER-WORLD <see cref="DescriptorOwnershipMap"/>, contributed by
    /// <c>CycloneEgressSystem</c> — i.e. from the <b>egress</b> translators only. ⛔ This module knows more:
    /// its shared, kinematic and cognitive packs include <b>ingress</b> translators that also declare
    /// <c>TargetComponentIds</c>. ⇒ ⚠ a component covered only by an ingress-side declaration would have been
    /// invisible to the attribute path, and a write to it would never have been marked for republication —
    /// the <c>AX-015</c> failure mode, reached by a different route.</para>
    ///
    /// <para>⭐⭐ <b>Additive, so ORDER DOES NOT MATTER.</b> <c>ContributeTranslators</c> merges, and
    /// <c>RegisterFromTranslator</c> is idempotent per (ordinal, component) pair ⇒ this module and every
    /// egress system can contribute in any order, any number of times, and the result is their union.</para>
    ///
    /// <para>⚠⚠ <b>What this deliberately does NOT do: collapse the two instances into one.</b>
    /// <see cref="_descriptorOwnershipMap"/> stays private and stays the one handed to
    /// <c>OwnershipIngressSystem</c>, <c>DeferredTakeoverSystem</c> and <c>LocalAuthorityYieldSystem</c> at
    /// CONSTRUCTION — before any world exists. ⛔ Rewiring those three to resolve from the world would change
    /// how <b>authority</b> is decided, which is far more load-bearing than the tidiness it would buy. ⭐ The
    /// real hazard was the world's map being a SUBSET; that is what is fixed. 📌 Two instances holding the
    /// same knowledge is cosmetic — one holding LESS was not.</para>
    /// </summary>
    private void ContributeDescriptorPairings(ISimulationView view)
    {
        if (_contributedDescriptorPairings) return;
        if (view is not EntityRepository repo) return;   // ⭐ the established pattern in the translators

        var provider = Fdp.Toolkit.Replication.Attributes.AttributeInterpreterProvider.GetDescriptorMap(repo);

        foreach (int componentId in _descriptorOwnershipMap.CoveredComponentIds.ToArray())
            foreach (long ordinal in _descriptorOwnershipMap.GetDescriptorsForComponentId(componentId))
                provider.RegisterFromTranslator(ordinal, new[] { componentId });

        _contributedDescriptorPairings = true;
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

}
