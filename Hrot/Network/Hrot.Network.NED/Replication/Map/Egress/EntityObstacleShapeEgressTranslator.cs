using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Terrain;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ <c>CE-3136</c> P-7a (O3) — publishes a static obstacle's <see cref="ObstacleShape"/> from the node that owns its
    /// <c>dtObstacleShape</c> descriptor (its creator), on change. Every other node holds the obstacle's ghost until this has
    /// arrived (<c>[PerInstanceValue]</c>), so all of them bake the same box. 📄 docs/DESIGN_Peek_And_Fire.md §9.
    /// The shape of <see cref="EntityDoorStateEgressTranslator"/>: send-on-change keyed by network id, cleared when the world moves.
    /// ⚠ Lifecycle <c>All</c>: an obstacle is published while it is still <c>Constructing</c> — the other nodes need its box to bake it.
    /// </summary>
    public sealed class EntityObstacleShapeEgressTranslator : IDescriptorTranslator
    {
        private const long   OrdinalValue = (long)EDescriptorType.dtObstacleShape;

        // ⭐⭐ CE-4007 (Cluster Load Phase §8.3) — what this writer published: the send-on-change cache AND the instances it deletes on a
        //   destruction, a wipe or (as an unregister) a hand-over.
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityObstacleShape> _instances;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityObstacleShape>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityObstacleShapeEgressTranslator(DdsParticipant participant)
        {
            _instances = new(new DdsWriter<EntityObstacleShape>(participant), static key => new EntityObstacleShape { EntityId = (int)key });
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);   // a wiped world: every instance this writer published is deleted (CE-4007, §8.3)

            var query = view.Query()
                .With<ObstacleShape>()
                .DefaultRoutePublishable()
                .WithLifecycle(EntityLifecycle.AllButTearDown)
                .Build();

            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var entity in query)
            {
                if (!view.HasAuthority(entity, packedKey)) continue;

                int netId = view.GetComponentRO<NetworkIdentity>(entity).Value;
                var shape = view.GetComponentRO<ObstacleShape>(entity);
                var sample = new EntityObstacleShape
                {
                    EntityId = netId, Length = shape.Length, Width = shape.Width, Height = shape.Height,
                };
                if (_instances.Unchanged(netId, sample)) continue;
                _instances.Write(netId, sample);
                SentSampleCount++;
            }
        }

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);   // ⭐ CE-4007 — the delete goes on the wire

        /// <summary>⭐ CE-4079 ② — the grantee confirmed (F7): let go of the instance WITHOUT disposing it, and forget the cache so a reclaim republishes.</summary>
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}
