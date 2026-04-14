using System;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Hrot.Common;
using Hrot.Common.Systems;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Scheduling;
using Fdp.Network.Cyclone.Modules;
using Fdp.Network.Cyclone.Systems;

namespace Hrot.BDC.Replication
{
    /// <summary>
    /// BDC replication module implementing the protocol-neutral <see cref="Hrot.Common.Abstractions.IReplicationModule"/>.
    /// Provides entity state synchronisation using BDC DDS topics (BDC_EntityMaster, BDC_WorldPos).
    /// </summary>
    public sealed class BdcReplicationModule : Hrot.Common.Abstractions.IReplicationModule
    {
        public string Name => "BdcReplication";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly DdsParticipant? _participant;
        private readonly NetworkEntityMap _entityMap;
        private readonly IGeographicTransform _geoTransform;
        private readonly FdpEventBus _eventBus;
        private readonly long _localNodeId;
        private readonly bool _driveFromNetwork;

        /// <inheritdoc/>
        public GhostCreationSystem GhostCreationSystem { get; }

        /// <inheritdoc/>
        public bool DriveFromNetwork => _driveFromNetwork;

        /// <inheritdoc/>
        public Fdp.ModuleHost.Scheduling.NetworkLifecycleSystemGroup NetworkLifecycleGroup { get; }

        public BdcReplicationModule(
            DdsParticipant? participant,
            NodeRole role,
            NetworkEntityMap entityMap,
            IGeographicTransform geoTransform,
            FdpEventBus eventBus,
            long localNodeId)
        {
            _participant  = participant;
            _entityMap    = entityMap    ?? throw new ArgumentNullException(nameof(entityMap));
            _geoTransform = geoTransform ?? throw new ArgumentNullException(nameof(geoTransform));
            _eventBus     = eventBus     ?? throw new ArgumentNullException(nameof(eventBus));
            _localNodeId  = localNodeId;

            bool roleHasMuscle = role.HasFlag(NodeRole.MuscleGround);
            bool roleHasBrain  = role.HasFlag(NodeRole.Brain);
            _driveFromNetwork  = !roleHasMuscle && !roleHasBrain;

            GhostCreationSystem = new GhostCreationSystem(entityMap);
            NetworkLifecycleGroup = new Fdp.ModuleHost.Scheduling.NetworkLifecycleSystemGroup(GhostCreationSystem);
        }

        public void RegisterSystems(ISystemRegistry registry)
        {
            registry.RegisterSystem(GhostCreationSystem);

            if (_participant != null)
            {
                var masterTranslator = new BdcEntityMasterTranslator(
                    _participant, _entityMap, _localNodeId, _eventBus, GhostCreationSystem);
                var worldPosTranslator = new BdcWorldPosTranslator(
                    _participant, _entityMap, _geoTransform, _localNodeId);

                var translators = new IDescriptorTranslator[]
                {
                    masterTranslator,
                    worldPosTranslator,
                };

                registry.RegisterSystem(new CycloneNetworkIngressSystem(translators));
                registry.RegisterSystem(new CycloneEgressSystem(translators));
                registry.RegisterSystem(new CycloneNetworkCleanupSystem(translators));
            }

            registry.RegisterSystem(new SmartEgressSystem());
            registry.RegisterSystem(new DeadReckoningSyncSystem(_driveFromNetwork));
        }

        public void Tick(ISimulationView view, float dt) { }
    }
}
