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
    public sealed class EntityDoorStateEgressTranslator : IDescriptorTranslator, Fdp.Network.Cyclone.Egress.IOffFrameEgress
    {
        private const long   OrdinalValue = (long)EDescriptorType.dtDoorState;

        // ⭐⭐ CE-4007 (Cluster Load Phase §8.3) — what this writer published: the send-on-change cache AND the instances it deletes on a
        //   destruction, a wipe or (as an unregister) a hand-over.
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityDoorState> _instances;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityDoorState>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityDoorStateEgressTranslator(DdsParticipant participant)
        {
            _instances = new(new DdsWriter<EntityDoorState>(participant), static key => new EntityDoorState { EntityId = (int)key });
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            BeginFrame(view);
            ScanState(view, offFrame: false);
        }

        /// <summary>⭐ CE-3427 N-1 (docs/DESIGN_Egress_Change_Detection.md §7) — on the LIVE world, in the egress system's slot: the world-epoch and
        /// destruction bookkeeping (it reads the bus).</summary>
        public void BeginFrame(ISimulationView view)
        {
            _instances.OnFrame(view);   // a wiped world: every instance this writer published is deleted (CE-4007, §8.3)
        }

        /// <summary>⭐ CE-3427 N-1 — the state scan: reads only <paramref name="view"/>'s components and writes nothing to it, so it may run
        /// on the off-frame replica.</summary>
        public void ScanState(ISimulationView view, bool offFrame)
        {

            var query = view.Query()
                .With<DoorState>()
                .WithManaged<TerrainObjectKey>()
                .DefaultRoutePublishable()
                .WithLifecycle(EntityLifecycle.AllButTearDown)
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

                int netId = view.GetComponentRO<NetworkIdentity>(entity).Value;
                var state = view.GetComponentRO<DoorState>(entity).State;
                // (compares the state byte, not the whole sample: the Key string never changes for a door, and a field-wise compare would box)
                if (_instances.TryGetLast(netId, out var prev) && prev.State == (byte)state) continue;

                _instances.Write(netId, new EntityDoorState
                {
                    EntityId = netId,
                    State    = (byte)state,
                    Key      = view.GetManagedComponentRO<TerrainObjectKey>(entity).Key,
                });
                SentSampleCount++;
            }
        }

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);   // ⭐ CE-4007 — the delete goes on the wire

        /// <summary>⭐ CE-4079 ② — the grantee confirmed (F7): let go of the instance WITHOUT disposing it, and forget the cache so a reclaim republishes.</summary>
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}
