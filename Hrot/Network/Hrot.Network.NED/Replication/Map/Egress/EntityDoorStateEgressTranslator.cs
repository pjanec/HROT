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
    /// ⭐ Buildings Stage 5b — publishes a terrain door's <see cref="DoorState"/> (with its <see cref="TerrainObjectKey"/>) from
    /// the node that owns the door's <c>dtDoorState</c> descriptor, on change. 📄 docs/DESIGN_Building_Interiors.md §3j.
    /// Same shape as <see cref="EntityDamageEgressTranslator"/>: send-on-change keyed by network id, the cache cleared when the
    /// world moves (<c>WorldEpoch</c>, CE-3076) so a reused id is never suppressed against the last world's value.
    /// </summary>
    public sealed class EntityDoorStateEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityDoorState";
        private const long   OrdinalValue = (long)EDescriptorType.dtDoorState;

        private readonly DdsWriter<EntityDoorState> _writer;
        private readonly Dictionary<long, TerrainDoorState> _lastPublished = new();
        private int _worldEpoch;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityDoorStateEgressTranslator(DdsParticipant participant)
        {
            _writer = new DdsWriter<EntityDoorState>(participant, DdsTopicName);
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (WorldEpoch.Moved(view, ref _worldEpoch)) _lastPublished.Clear();

            var query = view.Query()
                .With<DoorState>()
                .WithManaged<TerrainObjectKey>()
                .DefaultRoutePublishable()
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var entity in query)
            {
                if (!view.HasAuthority(entity, packedKey)) continue;
                // ⭐ CE-2335 — a GHOST (a replica whose master has not arrived: no NetworkAuthority yet) is never the owner, though
                //   HasAuthority reads "no record" as owned (right for a node with no network). The same exclusion as
                //   DoorCommandSystem.OwnsDoor — the test this egress was meant to share. 📌 Before, a replica node published the door
                //   as if it owned it: several writers on one door, a stale value able to overwrite a newer one on another replica.
                if (!view.HasComponent<NetworkAuthority>(entity) && view.HasComponent<GhostStateTracker>(entity)) continue;

                long netId = view.GetComponentRO<NetworkIdentity>(entity).Value;
                var state = view.GetComponentRO<DoorState>(entity).State;
                if (_lastPublished.TryGetValue(netId, out var prev) && prev == state) continue;
                _lastPublished[netId] = state;

                _writer.Write(new EntityDoorState
                {
                    EntityId = netId,
                    State    = (byte)state,
                    Key      = view.GetManagedComponentRO<TerrainObjectKey>(entity).Key,
                });
                SentSampleCount++;
            }
        }

        public void Dispose(long networkEntityId) => _lastPublished.Remove(networkEntityId);

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}
