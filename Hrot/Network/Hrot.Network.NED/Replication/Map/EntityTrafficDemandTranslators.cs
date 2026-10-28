using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Traffic;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ AQ93 §12 N4 (R-4056; 📄 docs/blueprints/Architect_Question_93_Sumo_Traffic_Node.md §12) — publishes the scenario's
    /// <see cref="TrafficDemand"/> from the node owning its <c>dtTrafficDemand</c> (the Traffic node the creating Brain granted the
    /// Traffic group to, or the Brain itself when there is none), on change. The scenario clock's shape
    /// (<see cref="EntityScenarioClockEgressTranslator"/>). ⭐ The entity has no <c>SimTransform</c> and nothing here asks for one.
    /// </summary>
    public sealed class EntityTrafficDemandEgressTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<EntityTrafficDemand>? _writer;
        private readonly Dictionary<long, EntityTrafficDemand> _last = new();
        private int _worldEpoch;

        private static readonly int[] s_targetIds = { GlobalComponentIds.TrafficDemand };

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityTrafficDemand>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtTrafficDemand;
        /// <summary>The component this descriptor republishes.</summary>
        public IReadOnlyList<int> TargetComponentIds => s_targetIds;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityTrafficDemandEgressTranslator(DdsParticipant? participant)
            => _writer = participant is not null ? new DdsWriter<EntityTrafficDemand>(participant) : null;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (WorldEpoch.Moved(view, ref _worldEpoch))
            {
                // ⭐⭐ CE-4007 (Q91 §4-M) — a wiped world: DELETE every instance this node wrote, so a late joiner never receives a dead run (the topic is TransientLocal).
                foreach (var id in _last.Keys) _writer?.DisposeInstance(new EntityTrafficDemand { EntityId = id });
                _last.Clear();
            }
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<TrafficDemand>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var env in view.Query().With<TrafficDemand>().DefaultRoutePublishable().Build())
            {
                if (!view.HasAuthority(env, packedKey)) continue;
                long netId = view.GetComponentRO<NetworkIdentity>(env).Value;
                var sample = ToSample(netId, view.GetComponentRO<TrafficDemand>(env));
                if (_last.TryGetValue(netId, out var prev) && prev.Equals(sample)) continue;
                _last[netId] = sample;
                _writer?.Write(sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityTrafficDemand ToSample(long netId, in TrafficDemand c)
            => new() { EntityId = netId, TargetCars = c.TargetCars, Seed = c.Seed, StepSeconds = c.StepSeconds,
                       TargetPedestrians = c.TargetPedestrians, ShotPanicRadius = c.ShotPanicRadius, BlastPanicRadius = c.BlastPanicRadius,
                       MaxConverted = c.MaxConverted };

        public void Dispose(long networkEntityId) => _last.Remove(networkEntityId);

        /// <summary>⭐ CE-4079 ② — the grantee confirmed (F7): let go of the instance WITHOUT disposing it, and forget the cache so a reclaim republishes.</summary>
        public void ReleaseInstance(long networkEntityId, long instanceId)
        {
            if (_last.Remove(networkEntityId)) _writer?.UnregisterInstance(new EntityTrafficDemand { EntityId = networkEntityId });
        }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// ⭐ AQ93 §12 N4 — applies the published traffic settings on every node that does not own them (the Brain that saves them included). A sample for an entity this node does
    /// not know yet is HELD (the newest per entity) until it does — it is sent once per change (TransientLocal). The recorded owner
    /// never takes its own sample back (S8 / F-5). The scenario clock's shape (<see cref="EntityScenarioClockIngressTranslator"/>).
    /// </summary>
    public sealed class EntityTrafficDemandIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtTrafficDemand;
        private readonly DdsReader<EntityTrafficDemand>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly Dictionary<long, EntityTrafficDemand> _pending = new();
        private readonly List<long> _done = new();

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityTrafficDemand>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        /// <summary>Samples waiting for an entity this node does not know yet.</summary>
        public int PendingCount => _pending.Count;
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityTrafficDemandIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityTrafficDemand>(participant) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        /// <inheritdoc />
        public int DiscardIngress() => _reader.DiscardAll();   // ⭐ CE-3329 W5

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is not null)
            {
                using var loan = _reader.Take();
                foreach (var sample in loan)
                {
                    if (!sample.IsValid || sample.Info.InstanceState != DdsInstanceState.Alive) continue;
                    ReceivedSampleCount++;
                    _pending[sample.Data.EntityId] = sample.Data;
                }
            }
            Flush(cmd, view);
        }

        /// <summary>Takes one sample (the test seam) — it is applied by the next <see cref="Flush"/>.</summary>
        internal void Receive(in EntityTrafficDemand d) { ReceivedSampleCount++; _pending[d.EntityId] = d; }

        /// <summary>Applies every held sample whose entity is known.</summary>
        internal void Flush(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_pending.Count == 0) return;
            foreach (var (id, d) in _pending)
                if (TryApply(in d, cmd, view)) _done.Add(id);
            foreach (var id in _done) _pending.Remove(id);
            _done.Clear();
        }

        private bool TryApply(in EntityTrafficDemand d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.EntityId, out var env) || !view.IsAlive(env)) return false;
            if (view is EntityRepository repo
                && AuthorityExtensions.IsRecordedOwner(repo, env, OwnershipExtensions.PackKey(OrdinalValue, 0))) return true;   // ours: drop
            cmd.AddComponent(env, new TrafficDemand { TargetCars = d.TargetCars, Seed = d.Seed, StepSeconds = d.StepSeconds,
                TargetPedestrians = d.TargetPedestrians, ShotPanicRadius = d.ShotPanicRadius, BlastPanicRadius = d.BlastPanicRadius,
                MaxConverted = d.MaxConverted });
            return true;
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) => _pending.Remove(networkEntityId);
    }
}
