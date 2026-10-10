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
        private const string DdsTopicName = "EntityObstacleShape";
        private const long   OrdinalValue = (long)EDescriptorType.dtObstacleShape;

        private readonly DdsWriter<EntityObstacleShape> _writer;
        private readonly Dictionary<long, ObstacleShape> _lastPublished = new();
        private int _worldEpoch;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityObstacleShapeEgressTranslator(DdsParticipant participant)
        {
            _writer = new DdsWriter<EntityObstacleShape>(participant, DdsTopicName);
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (WorldEpoch.Moved(view, ref _worldEpoch)) _lastPublished.Clear();

            var query = view.Query()
                .With<ObstacleShape>()
                .With<NetworkIdentity>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var entity in query)
            {
                if (!view.HasAuthority(entity, packedKey)) continue;

                long netId = view.GetComponentRO<NetworkIdentity>(entity).Value;
                var shape = view.GetComponentRO<ObstacleShape>(entity);
                if (_lastPublished.TryGetValue(netId, out var prev)
                    && prev.Length == shape.Length && prev.Width == shape.Width && prev.Height == shape.Height) continue;
                _lastPublished[netId] = shape;

                _writer.Write(new EntityObstacleShape
                {
                    EntityId = (int)netId, Length = shape.Length, Width = shape.Width, Height = shape.Height,
                });
                SentSampleCount++;
            }
        }

        public void Dispose(long networkEntityId) => _lastPublished.Remove(networkEntityId);

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}
