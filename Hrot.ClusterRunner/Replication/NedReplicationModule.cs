using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using Hrot.IG.Systems;
using Hrot.Map.Common;
using Hrot.Map.Common.Translators;
using Hrot.SimHost;
using Hrot.SimHost.Network;
using ModuleHost.Core.Abstractions;
using ModuleHost.Network.Cyclone.Modules;
using ModuleHost.Network.Cyclone.Systems;

namespace Hrot.ClusterRunner.Replication;

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
///   <item>
///     <term><see cref="NodeRole.AllInOne"/></term>
///     <description>All packs; GhostCreationSystem; SmartEgressSystem; DeadReckoningSyncSystem (driveFromNetwork=false).</description>
///   </item>
/// </list>
///
/// <para>
/// TODO: move to shared if NedReplicationModule is extracted from Hrot.ClusterRunner.
/// DeadReckoningSyncSystem is currently in Hrot.IG/Systems/ — accessible here because
/// Hrot.ClusterRunner references Hrot.IG. If NedReplicationModule is later moved to a
/// shared project, DeadReckoningSyncSystem would need to move with it.
/// </para>
/// </summary>
public sealed class NedReplicationModule : IEcsModule
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

    // ── Translator lists ───────────────────────────────────────────────────────
    private readonly IEnumerable<IDescriptorTranslator> _sharedTranslators;
    private readonly IEnumerable<IDescriptorTranslator>? _kinematicTranslators;
    private readonly IEnumerable<IDescriptorTranslator>? _cognitiveTranslators;

    /// <summary>
    /// Ghost-creation system creates replica entities from incoming DDS samples.
    /// Exposed so Phase 4 can wire it into <c>ReplayLoadClusterOpHandler</c>.
    /// </summary>
    public GhostCreationSystem GhostCreationSystem { get; }

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
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="role"/> is not one of the supported replication roles
    /// (MuscleGround, ImageGenerator, Brain, AllInOne).
    /// </exception>
    public NedReplicationModule(
        DdsParticipant?      participant,
        NodeRole             role,
        NetworkEntityMap     entityMap,
        IGeographicTransform geoTransform,
        FdpEventBus          eventBus,
        int                  localNodeId,
        int                  domainId)
    {
        _participant  = participant;
        _role         = role;
        _entityMap    = entityMap  ?? throw new ArgumentNullException(nameof(entityMap));
        _geoTransform = geoTransform ?? throw new ArgumentNullException(nameof(geoTransform));
        _eventBus     = eventBus   ?? throw new ArgumentNullException(nameof(eventBus));
        _localNodeId  = localNodeId;

        // Validate role
        _roleHasMuscle = role == NodeRole.MuscleGround || role == NodeRole.AllInOne;
        _roleHasIG     = role == NodeRole.ImageGenerator || role == NodeRole.AllInOne;
        _roleHasBrain  = role == NodeRole.Brain || role == NodeRole.AllInOne;

        if (!_roleHasMuscle && !_roleHasIG && !_roleHasBrain)
            throw new ArgumentException(
                $"NedReplicationModule requires a role with MuscleGround, ImageGenerator, Brain, or AllInOne. Got: {role}",
                nameof(role));

        // driveFromNetwork = true when ONLY IG (no local physics)
        // driveFromNetwork = false when Muscle or AllInOne is present (local entities must not be overridden)
        _driveFromNetwork = !_roleHasMuscle && !_roleHasBrain;

        // Ghost creation — shared by all ingress translators + replay handler
        GhostCreationSystem = new GhostCreationSystem(entityMap);

        // Build translator sets — deferred until RegisterSystems to allow null-participant
        // headless contexts to construct the module without creating DDS writers/readers.
        if (participant != null)
        {
            _sharedTranslators = SharedTranslatorPack.Create(
                participant, entityMap, localNodeId, eventBus, GhostCreationSystem);

            if (_roleHasMuscle)
                _kinematicTranslators = KinematicTranslatorPack.Create(
                    participant, entityMap, geoTransform);

            if (_roleHasBrain)
                _cognitiveTranslators = CognitiveTranslatorPack.Create(
                    participant, entityMap, geoTransform,
                    doctrineRegistry: null,   // moved to subsystem responsibility in Phase 4
                    GhostCreationSystem);
        }
        else
        {
            // Headless / test mode — no DDS translators
            _sharedTranslators    = System.Array.Empty<IDescriptorTranslator>();
            _kinematicTranslators = null;
            _cognitiveTranslators = null;
        }
    }

    public void RegisterSystems(ISystemRegistry registry)
    {
        // ── Ghost lifecycle systems (all roles) ─────────────────────────────
        registry.RegisterSystem(GhostCreationSystem);

        // ── Translator routing systems ───────────────────────────────────────
        var allTranslators = new List<IDescriptorTranslator>(_sharedTranslators);
        if (_roleHasMuscle && _kinematicTranslators != null)
            allTranslators.AddRange(_kinematicTranslators);
        if (_roleHasBrain && _cognitiveTranslators != null)
            allTranslators.AddRange(_cognitiveTranslators);

        // Register ingress + egress systems only when a live DDS participant is available.
        if (_participant != null)
        {
            registry.RegisterSystem(new CycloneNetworkIngressSystem(allTranslators.ToArray()));
            registry.RegisterSystem(new CycloneEgressSystem(allTranslators.ToArray()));
        }

        // ── ImageGenerator: inline EntityStatesIngressPack ───────────────────
        if (_roleHasIG)
        {
            if (_participant != null)
            {
                var igPack = new EntityStatesIngressPack(
                    PackRole.Ingress, _participant, _entityMap, _eventBus,
                    GhostCreationSystem, _geoTransform);
                igPack.RegisterSystems(registry);
            }

            // DR sync — ghost entities are smoothed; combined-role skips local entities
            registry.RegisterSystem(new DeadReckoningSyncSystem(_driveFromNetwork));
        }

        // ── Role-specific systems ────────────────────────────────────────────
        if (_roleHasMuscle || _roleHasBrain)
            registry.RegisterSystem(new SmartEgressSystem());

        // ── Cleanup systems (all roles) ──────────────────────────────────────
        registry.RegisterSystem(new CycloneNetworkCleanupSystem(allTranslators));
        registry.RegisterSystem(new DisposalMonitoringSystem(_entityMap));
    }

    public void Tick(ISimulationView view, float dt) { }
}
