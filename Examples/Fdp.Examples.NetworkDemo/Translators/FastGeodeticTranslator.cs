using System;
using System.Numerics;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.Examples.NetworkDemo.Components;
using Fdp.Examples.NetworkDemo.Descriptors;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Extensions;
using FDP.Toolkit.Replication.Components;
using Fdp.Modules.Geographic;
using Fdp.ModuleHost_Core.Abstractions;
using Fdp.Network.Cyclone.Translators;

namespace Fdp.Examples.NetworkDemo.Translators
{
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

        protected override void Decode(in GeoStateDescriptor data, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!EntityMap.TryGetEntity(data.EntityId, out Entity entity))
                return;

            var cartesian = _geoTransform.ToCartesian(data.Lat, data.Lon, data.Alt);
            
            Quaternion rot = Quaternion.Identity;
            // Try to preserve existing rotation
            if (view.HasComponent<SimTransform>(entity))
            {
                 // We can only read component safely if we are in a system
                 // Component might be missing if just spawned.
                 try {
                     rot = view.GetComponentRO<SimTransform>(entity).Rotation;
                 } catch {}
            }
            
            cmd.SetComponent(entity, new SimTransform { 
                Position = new Vector3((float)cartesian.X, (float)cartesian.Y, (float)cartesian.Z),
                Rotation = rot
            });
        }

        public override void ScanAndPublish(ISimulationView view)
        {
            var query = view.Query()
                .With<SimTransform>()
                .With<NetworkIdentity>()
                .WithLifecycle(Fdp.Kernel.EntityLifecycle.All)
                .Build();
            long packedKey = Fdp.ModuleHost_Core.Network.OwnershipExtensions.PackKey(DescriptorOrdinal, 0);

            foreach (var entity in query)
            {
                if (!view.HasAuthority(entity, packedKey))
                    continue;

                ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);
                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
                
                // Assuming ToGeodetic accepts Vector3 or 3 doubles
                var (lat, lon, alt) = _geoTransform.ToGeodetic(new Vector3(tf.Position.X, tf.Position.Y, tf.Position.Z));

                Publish(new GeoStateDescriptor
                {
                    EntityId = (long)netId.Value,
                    Lat = lat,
                    Lon = lon,
                    Alt = (float)alt,
                    Heading = 0.0f
                });
            }
        }

        public override void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is GeoStateDescriptor descriptor)
            {
                var flatPos = _geoTransform.ToCartesian(descriptor.Lat, descriptor.Lon, descriptor.Alt);
                
                Quaternion rot = Quaternion.Identity;
                if (repo.HasComponent<SimTransform>(entity))
                {
                    rot = repo.GetComponent<SimTransform>(entity).Rotation;
                }
                
                repo.SetComponent(entity, new SimTransform { 
                    Position = new Vector3((float)flatPos.X, (float)flatPos.Y, (float)flatPos.Z),
                    Rotation = rot
                });
            }
        }
    }
}
