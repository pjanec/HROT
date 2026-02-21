using System;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.Examples.NetworkDemo.Components;
using Fdp.Examples.NetworkDemo.Descriptors;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Extensions;
using FDP.Toolkit.Replication.Components;
using Fdp.Modules.Geographic;
using ModuleHost.Core.Abstractions;
using ModuleHost.Network.Cyclone.Translators;

namespace Fdp.Examples.NetworkDemo.Translators
{
    /// <summary>
    /// High-performance geodetic translator with direct coordinate conversion.
    /// Eliminates intermediate components and smoothing systems.
    /// </summary>
    public class FastGeodeticTranslator : CycloneTranslator<GeoStateDescriptor, GeoStateDescriptor>
    {
        private readonly IGeographicTransform _geoTransform;

        public FastGeodeticTranslator(
            DdsParticipant participant, 
            IGeographicTransform geoTransform,
            NetworkEntityMap entityMap) 
            : base(participant, "Tank_GeoState", ordinal: 5, entityMap)
        {
            _geoTransform = geoTransform ?? throw new ArgumentNullException(nameof(geoTransform));
        }

        // INGRESS: DDS (Lat/Lon) -> Math -> ECS (X/Y)
        protected override void Decode(in GeoStateDescriptor data, IEntityCommandBuffer cmd, ISimulationView view)
        {
            // 1. Resolve entity (no intermediate component)
            if (!EntityMap.TryGetEntity(data.EntityId, out Entity entity))
                return;

            // 2. Convert coordinates (math while data is hot in cache)
            var cartesian = _geoTransform.ToCartesian(data.Lat, data.Lon, data.Alt);

            // 3. Write directly to internal simulation component
            // Note: We use SetComponent assuming the entity is spawned with DemoPosition. 
            // If it handles creation, it might need AddComponent, but standard replication usually updates existing.
            // Using SetComponent for speed as per instructions.
            cmd.SetComponent(entity, new DemoPosition { Value = cartesian });
        }

        // EGRESS: ECS (X/Y) -> Math -> DDS (Lat/Lon)
        public override void ScanAndPublish(ISimulationView view)
        {
            var query = view.Query()
                .With<DemoPosition>()
                .With<NetworkIdentity>()
                .WithLifecycle(Fdp.Kernel.EntityLifecycle.All)
                .Build();

            foreach (var entity in query)
            {
                // 1. Check authority
                if (!view.HasAuthority(entity, DescriptorOrdinal))
                    continue;

                // 2. Read position
                ref readonly var pos = ref view.GetComponentRO<DemoPosition>(entity);
                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);

                // 3. Convert coordinates (math while data is hot in cache)
                var (lat, lon, alt) = _geoTransform.ToGeodetic(pos.Value);

                // 4. Write to DDS (stack-allocated struct)
                Publish(new GeoStateDescriptor
                {
                    EntityId = (long)netId.Value, // Descriptor uses long
                    Lat = lat,
                    Lon = lon,
                    Alt = (float)alt,
                    Heading = 0.0f // Calculate from velocity if needed
                });
            }
        }

        public override void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is GeoStateDescriptor descriptor)
            {
                var flatPos = _geoTransform.ToCartesian(descriptor.Lat, descriptor.Lon, descriptor.Alt);
                repo.SetComponent(entity, new DemoPosition { Value = flatPos });
            }
        }
    }
}
