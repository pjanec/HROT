using System;
using Fdp.Core;
using Fdp.Examples.NetworkDemo.Components;
using Fdp.Examples.NetworkDemo.Descriptors;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.ModuleHost.Abstractions;
using Fdp.Network.Cyclone.Translators;
using CycloneDDS.Runtime;
using Fdp.Interfaces;

namespace Fdp.Examples.NetworkDemo.Translators
{
    public class WeaponStateTranslator : CycloneTranslator<WeaponStateTopic, WeaponStateTopic>
    {
        public override TranslatorDirection Direction => TranslatorDirection.Bidirectional;

        public WeaponStateTranslator(DdsParticipant p, NetworkEntityMap map) 
            : base(p, "SST_WeaponState", ordinal: 6, map)
        {
        }

        protected override void Decode(in WeaponStateTopic data, IEntityCommandBuffer cmd, ISimulationView view)
        {
            // 1. Resolve root tank
            if (!EntityMap.TryGetEntity(data.EntityId, out Entity root)) return;

            // 2. Find child turret
            Entity target = root;
            if (data.InstanceId > 0)
            {
                if (!view.HasManagedComponent<ChildMap>(root)) return;
                var childMap = view.GetManagedComponentRO<ChildMap>(root);
                if (!childMap.InstanceToEntity.TryGetValue((int)data.InstanceId, out target)) return;
            }

            // 3. De-aggregate into components
            cmd.SetComponent(target, new TurretState { Yaw = data.Azimuth, Pitch = data.Elevation });
            cmd.SetComponent(target, new WeaponState { Ammo = data.Ammo, Status = data.Status });
        }

        public override void ScanAndPublish(ISimulationView view)
        {
            var query = view.Query()
                .With<TurretState>()
                .With<WeaponState>()
                .With<PartMetadata>()
                .Build();

            foreach (var entity in query)
            {
                var meta = view.GetComponentRO<PartMetadata>(entity);
                
                if (!EntityMap.TryGetNetworkId(meta.ParentEntity, out long rootNetId))
                    continue;

                long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, meta.InstanceId);
                if (!view.HasAuthority(meta.ParentEntity, packedKey))
                    continue;

                var turret = view.GetComponentRO<TurretState>(entity);
                var weapon = view.GetComponentRO<WeaponState>(entity);

                Publish(new WeaponStateTopic
                {
                    EntityId = rootNetId,
                    InstanceId = meta.InstanceId,
                    Azimuth = turret.Yaw,
                    Elevation = turret.Pitch,
                    Ammo = weapon.Ammo,
                    Status = weapon.Status
                });
            }
        }

        public override void ApplyToEntity(Entity entity, object data, EntityRepository repo) 
        {
             if (data is not WeaponStateTopic topic) return;
             
             repo.SetComponent(entity, new TurretState { Yaw = topic.Azimuth, Pitch = topic.Elevation });
             repo.SetComponent(entity, new WeaponState { Ammo = topic.Ammo, Status = topic.Status });
        }
    }
}
