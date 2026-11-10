using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ <c>CE-1153</c> (docs/DESIGN_Entity_Control.md §8h.6) — publishes a possessed unit's <see cref="OperatorUseStatus"/> (the brain's verdict
    /// on the last use press) from the node owning its <c>dtOperatorUseStatus</c> (the brain), on change. The <c>dtWeaponAmmo</c> shape.
    /// </summary>
    public sealed class EntityOperatorUseStatusEgressTranslator : IDescriptorTranslator, Fdp.Network.Cyclone.Egress.IOffFrameEgress
    {
        // ⭐⭐ CE-4007 (Cluster Load Phase §8.3) — what this writer published: the send-on-change cache AND the instances it deletes on a
        //   destruction, a wipe or (as an unregister) a hand-over.
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityOperatorUseStatus> _instances;

        private static readonly int[] s_targetIds = { GlobalComponentIds.OperatorUseStatus };

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityOperatorUseStatus>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtOperatorUseStatus;
        public IReadOnlyList<int> TargetComponentIds => s_targetIds;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityOperatorUseStatusEgressTranslator(DdsParticipant? participant)
            => _instances = new(participant is not null ? new DdsWriter<EntityOperatorUseStatus>(participant) : null, static key => new EntityOperatorUseStatus { EntityId = (int)key });

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
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<OperatorUseStatus>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var unit in view.Query().With<OperatorUseStatus>().DefaultRoutePublishable().Build())
            {
                if (!view.HasAuthority(unit, packedKey)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(unit).Value;
                var sample = ToSample(netId, view.GetComponentRO<OperatorUseStatus>(unit));
                if (_instances.Unchanged(netId, sample)) continue;
                _instances.Write(netId, sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityOperatorUseStatus ToSample(int netId, in OperatorUseStatus s) => new()
        {
            EntityId = netId, UseSeq = s.UseSeq, ActionId = s.ActionId, Outcome = (byte)s.Outcome, Reason = (byte)s.Reason,
        };

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);   // ⭐ CE-4007 — the delete goes on the wire

        /// <summary>⭐ CE-4079 ② — the grantee confirmed (F7): let go of the instance WITHOUT disposing it, and forget the cache so a reclaim republishes.</summary>
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>⭐ <c>CE-1153</c> — applies a unit's published <see cref="OperatorUseStatus"/> on this node (the station's HUD reads it). An
    /// unknown unit is dropped; the recorded owner never takes its own sample back.</summary>
    public sealed class EntityOperatorUseStatusIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtOperatorUseStatus;
        private readonly DdsReader<EntityOperatorUseStatus>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityOperatorUseStatus>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityOperatorUseStatusIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityOperatorUseStatus>(participant) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        /// <inheritdoc />
        public int DiscardIngress() => _reader.DiscardAll();   // ⭐ CE-3329 W5

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid || sample.Info.InstanceState != DdsInstanceState.Alive) continue;
                ReceivedSampleCount++;
                Apply(sample.Data, cmd, view);
            }
        }

        /// <summary>Applies one sample (the test seam).</summary>
        internal void Apply(in EntityOperatorUseStatus d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.EntityId, out var unit) || !view.IsAlive(unit)) { DroppedSampleCount++; return; }
            if (view is EntityRepository repo
                && AuthorityExtensions.IsRecordedOwner(repo, unit, OwnershipExtensions.PackKey(OrdinalValue, 0))) return;
            cmd.AddComponent(unit, new OperatorUseStatus
            {
                UseSeq = d.UseSeq, ActionId = d.ActionId, Outcome = (UseOutcome)d.Outcome, Reason = (UseReason)d.Reason,
            });
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) { }
    }
}
