using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ <c>CE-3219</c> (civilians B-1, CV-6; docs/DESIGN_Civilians_And_Crowd_Control.md §7.3) — publishes a person's stored
    /// <see cref="EffectState"/> from the node owning its <c>dtEffectState</c> (the Health owner, Brain group), on change. The <c>dtWeaponAmmo</c> shape.
    /// </summary>
    public sealed class EntityEffectStateEgressTranslator : IDescriptorTranslator
    {
        // ⭐⭐ CE-4007 (Cluster Load Phase §8.3) — what this writer published: the send-on-change cache AND the instances it deletes on a
        //   destruction, a wipe or (as an unregister) a hand-over.
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityEffectState> _instances;

        private static readonly int[] s_targetIds = { GlobalComponentIds.EffectState };

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityEffectState>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtEffectState;
        public IReadOnlyList<int> TargetComponentIds => s_targetIds;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityEffectStateEgressTranslator(DdsParticipant? participant)
            => _instances = new(participant is not null ? new DdsWriter<EntityEffectState>(participant) : null, static key => new EntityEffectState { EntityId = (int)key });

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);   // a wiped world: every instance this writer published is deleted (CE-4007, §8.3)
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<EffectState>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var unit in view.Query().With<EffectState>().DefaultRoutePublishable().Build())
            {
                if (!view.HasAuthority(unit, packedKey)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(unit).Value;
                var sample = ToSample(netId, view.GetComponentRO<EffectState>(unit));
                if (_instances.Unchanged(netId, sample)) continue;
                _instances.Write(netId, sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityEffectState ToSample(int netId, in EffectState s) => new()
        {
            EntityId = netId, StunnedUntil = s.StunnedUntil, IrritatedUntil = s.IrritatedUntil, KnockedDownUntil = s.KnockedDownUntil, Cowering = s.Cowering,
        };

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);   // ⭐ CE-4007 — the delete goes on the wire

        /// <summary>⭐ CE-4079 ② — the grantee confirmed (F7): let go of the instance WITHOUT disposing it, and forget the cache so a reclaim republishes.</summary>
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>⭐ <c>CE-3219</c> — applies a person's published <see cref="EffectState"/> on this node (bodies, maps, brains read it). An
    /// unknown unit is dropped; the recorded owner never takes its own sample back.</summary>
    public sealed class EntityEffectStateIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtEffectState;
        private readonly DdsReader<EntityEffectState>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityEffectState>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityEffectStateIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityEffectState>(participant) : null;
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
        internal void Apply(in EntityEffectState d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.EntityId, out var unit) || !view.IsAlive(unit)) { DroppedSampleCount++; return; }
            if (view is EntityRepository repo
                && AuthorityExtensions.IsRecordedOwner(repo, unit, OwnershipExtensions.PackKey(OrdinalValue, 0))) return;
            cmd.AddComponent(unit, new EffectState
            {
                StunnedUntil = (SimInstant)d.StunnedUntil, IrritatedUntil = (SimInstant)d.IrritatedUntil, KnockedDownUntil = (SimInstant)d.KnockedDownUntil, Cowering = d.Cowering,
            });
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) { }
    }
}
