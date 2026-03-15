using System.Numerics;
using Bagira.BDC.SSTD;
using Bagira.DDS.DM;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Extensions;
using FDP.Toolkit.Replication.Services;
using Fdp.Modules.Geographic;
using ModuleHost.Core.Abstractions;

using EcsNavigationIntent = FDP.Toolkit.Navigation.NavigationIntent;
using EcsNavMode          = FDP.Toolkit.Navigation.NavigationMode;

namespace Bagira.SimHost.Network
{
    /// <summary>
    /// Egress translator: reads the ECS <see cref="EcsNavigationIntent"/> component
    /// for all locally-owned entities and publishes a DDS <see cref="Bagira.BDC.SSTD.NavigationIntent"/>
    /// sample for each.
    ///
    /// <para>
    /// The <see cref="EcsNavigationIntent.FinalDestination"/> (Cartesian <c>Vector2</c>) is
    /// converted to a WGS-84 <see cref="GeoPosition"/> via <see cref="IGeographicTransform"/>,
    /// mirroring the pattern from <c>GeoSpatialEgressTranslator</c>.
    /// </para>
    ///
    /// <para>
    /// Entities with <c>intent.Mode == NavigationMode.None</c> are skipped (no active command).
    /// </para>
    /// </summary>
    public sealed class NavigationIntentEgressTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<Bagira.BDC.SSTD.NavigationIntent> _writer;
        private readonly NetworkEntityMap _entityMap;
        private readonly IGeographicTransform _geoTransform;

        public string TopicName      => "NavigationIntent";
        public long   DescriptorOrdinal => 52;

        public NavigationIntentEgressTranslator(
            DdsParticipant      dds,
            NetworkEntityMap    entityMap,
            IGeographicTransform geoTransform)
        {
            _writer       = new DdsWriter<Bagira.BDC.SSTD.NavigationIntent>(dds, "NavigationIntent");
            _entityMap    = entityMap   ?? throw new System.ArgumentNullException(nameof(entityMap));
            _geoTransform = geoTransform ?? throw new System.ArgumentNullException(nameof(geoTransform));
        }

        /// <summary>No ingress for this translator.</summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <summary>
        /// Publishes <see cref="Bagira.BDC.SSTD.NavigationIntent"/> to DDS for every
        /// authority-owned entity that has an active <see cref="EcsNavigationIntent"/>.
        /// </summary>
        public void ScanAndPublish(ISimulationView view)
        {
            var query = view.Query()
                .With<EcsNavigationIntent>()
                .With<NetworkIdentity>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            foreach (var entity in query)
            {
                // Only publish navigation intent for locally-owned entities.
                if (!view.HasAuthority(entity, DescriptorOrdinal))
                    continue;

                var intent = view.GetComponentRO<EcsNavigationIntent>(entity);

                // Skip inactive intents — no command to broadcast.
                if (intent.Mode == EcsNavMode.None)
                    continue;

                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);

                // Convert Cartesian destination to geodetic (WGS-84) for the wire format.
                var (lat, lon, alt) = _geoTransform.ToGeodetic(
                    new Vector3(intent.FinalDestination.X, intent.FinalDestination.Y, 0f));

                _writer.Write(new Bagira.BDC.SSTD.NavigationIntent
                {
                    EntityId         = (int)netId.Value,
                    IntentId         = intent.IntentId,
                    Mode             = MapMode(intent.Mode),
                    FinalDestination = new GeoPosition { Latitude = lat, Longitude = lon, Altitude = alt },
                    TargetSpeed      = intent.TargetSpeed,
                    ArrivalRadius    = intent.ArrivalRadius
                });

                FdpLog<NavigationIntentEgressTranslator>.Debug(
                    "[TRACE-SH] NavigationIntent egress: EntityId={0} IntentId={1} Mode={2}",
                    netId.Value, intent.IntentId, intent.Mode);
            }
        }

        /// <summary>
        /// Ghost promotion is not needed for NavigationIntent egress.
        /// </summary>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <summary>No DDS dispose needed for navigation intent.</summary>
        public void Dispose(long networkEntityId) { }

        // ── Enum mapping ──────────────────────────────────────────────────────

        private static ENavigationMode MapMode(EcsNavMode mode) => mode switch
        {
            EcsNavMode.DirectPoint   => ENavigationMode.NAV_DIRECT_POINT,
            EcsNavMode.FollowRoute   => ENavigationMode.NAV_FOLLOW_ROUTE,
            EcsNavMode.JoinFormation => ENavigationMode.NAV_JOIN_FORMATION,
            _                        => ENavigationMode.NAV_NONE,
        };
    }
}
