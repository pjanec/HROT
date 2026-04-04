using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using Hrot.SimHost.Systems;
using Fdp.Interfaces;
using FDP.Toolkit.NetworkSpawning.Systems;
using ModuleHost.Core.Abstractions;

namespace Hrot.SimHost.Modules
{
    // ─── Module ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Hosts a <see cref="FDP.Toolkit.NetworkSpawning.Systems.NetworkSpawningSystem"/>
    /// together with the DDS-backed request/delete systems that were moved out of the
    /// constructor (see <c>SimHostNetworkAdapters.cs</c> for the DDS adapter classes).
    /// Exposes optional egress/ingress translators for DDS topic publication and ingestion.
    /// All DDS adapters and request-handling systems are created by the application
    /// bootstrap layer (<c>SimHostApp</c> / <c>SimHostInstance</c>) and injected here.
    /// </summary>
    public class SimHostModule : IEcsModule
    {
        public string         Name   => "SimHost";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly NetworkSpawningSystem             _spawnSystem;
        private readonly CreateEntityRequestSystem?        _requestSystem;
        private readonly DeleteEntityRequestSystem?        _deleteSystem;
        private readonly NedRequestFinalizationSystem?     _finalizationSystem;
        private readonly GeoSpatialEgressTranslator?       _geoEgressTranslator;
        private readonly MapVisualOverlayEgressTranslator? _mapOverlayEgressTranslator;
        private readonly MapRouteEgressTranslator?         _mapRouteEgressTranslator;
        private readonly EntityMissionIngressTranslator?   _missionIngressTranslator;
        private readonly EntityMissionEgressTranslator?    _missionEgressTranslator;

        public SimHostModule(
            NetworkSpawningSystem             spawnSystem,
            CreateEntityRequestSystem?        requestSystem              = null,
            DeleteEntityRequestSystem?        deleteSystem               = null,
            NedRequestFinalizationSystem?     finalizationSystem         = null,
            GeoSpatialEgressTranslator?       geoEgressTranslator        = null,
            MapVisualOverlayEgressTranslator? mapOverlayEgressTranslator = null,
            MapRouteEgressTranslator?         mapRouteEgressTranslator   = null,
            EntityMissionIngressTranslator?   missionIngressTranslator   = null,
            EntityMissionEgressTranslator?    missionEgressTranslator    = null)
        {
            _spawnSystem                = spawnSystem;
            _requestSystem              = requestSystem;
            _deleteSystem               = deleteSystem;
            _finalizationSystem         = finalizationSystem;
            _geoEgressTranslator        = geoEgressTranslator;
            _mapOverlayEgressTranslator = mapOverlayEgressTranslator;
            _mapRouteEgressTranslator   = mapRouteEgressTranslator;
            _missionIngressTranslator   = missionIngressTranslator;
            _missionEgressTranslator    = missionEgressTranslator;
        }

        /// <summary>
        /// Gets the GeoSpatial egress translator for registration with the network module.
        /// Returns null if no geographic transform was provided.
        /// </summary>
        public GeoSpatialEgressTranslator? GeoEgressTranslator => _geoEgressTranslator;

        /// <summary>
        /// Gets the MapVisualOverlay egress translator for registration with the network module.
        /// Returns null if no geographic transform was provided.
        /// </summary>
        public MapVisualOverlayEgressTranslator? MapOverlayEgressTranslator => _mapOverlayEgressTranslator;

        /// <summary>
        /// Gets the MapRoute egress translator for registration with the network module.
        /// Returns null if no geographic transform was provided.
        /// </summary>
        public MapRouteEgressTranslator? MapRouteEgressTranslator => _mapRouteEgressTranslator;

        /// <summary>
        /// Gets the EntityMission ingress translator (DDS → ECS).
        /// Null when not provided at construction.
        /// </summary>
        public EntityMissionIngressTranslator? MissionIngressTranslator => _missionIngressTranslator;

        /// <summary>
        /// Gets the EntityMission egress translator (ECS → DDS).
        /// Null when not provided at construction.
        /// </summary>
        public EntityMissionEgressTranslator? MissionEgressTranslator => _missionEgressTranslator;

        public void RegisterSystems(ISystemRegistry registry)
        {
            if (_requestSystem     != null) registry.RegisterSystem(_requestSystem);
            registry.RegisterSystem(_spawnSystem);
            if (_deleteSystem      != null) registry.RegisterSystem(_deleteSystem);
            if (_finalizationSystem != null) registry.RegisterSystem(_finalizationSystem);
        }

        public void Tick(ISimulationView view, float dt) { }
    }
}
