using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using Hrot.Map.Common.Dds;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.ScenarioRules;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ rules V-3 (📄 docs/DESIGN_Scenario_Rules.md §00.10) — publishes a trigger's <see cref="RuleLeaves"/> from the node owning its <c>dtRuleLeaves</c> (Brain group), on change. ⭐ No throttle
    /// here: the SAMPLER is the throttle (≤ 2 Hz of sim time, only while the node feature <c>ui.rules-live-values</c> is on), so a change is one sample on the wire; while the feature is off
    /// nothing is written, nothing changes, nothing is sent (the last sample stays on the replicas — a viewer shows it greyed). Same shape as <see cref="EntityExerciseResultEgressTranslator"/>.
    /// </summary>
    public sealed class EntityRuleLeavesEgressTranslator : IDescriptorTranslator
    {
        private readonly IDdsWriter<EntityRuleLeaves>? _writer;
        private readonly Dictionary<int, EntityRuleLeaves> _last = new();
        private int _worldEpoch;
        private static readonly int[] s_targetIds = { GlobalComponentIds.RuleLeaves };

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityRuleLeaves>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtRuleLeaves;
        public IReadOnlyList<int> TargetComponentIds => s_targetIds;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityRuleLeavesEgressTranslator(DdsParticipant? participant)
            : this(participant is not null ? new DdsWriterAdapter<EntityRuleLeaves>(participant) : null) { }

        /// <summary>The test seam: a capturing writer instead of a live participant.</summary>
        internal EntityRuleLeavesEgressTranslator(IDdsWriter<EntityRuleLeaves>? writer) => _writer = writer;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (WorldEpoch.Moved(view, ref _worldEpoch))
            {
                foreach (var id in _last.Keys) _writer?.DisposeInstance(new EntityRuleLeaves { EntityId = id });   // CE-4007: a wiped world leaves no dead run
                _last.Clear();
            }
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<RuleLeaves>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var env in view.Query().With<RuleLeaves>().DefaultRoutePublishable().Build())
            {
                if (!view.HasAuthority(env, packedKey)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(env).Value;
                var sample = ToSample(netId, view.GetComponentRO<RuleLeaves>(env));
                if (_last.TryGetValue(netId, out var prev) && Same(in prev, in sample)) continue;
                _last[netId] = sample;
                _writer?.Write(sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityRuleLeaves ToSample(int netId, in RuleLeaves c)
        {
            var list = c.Count == 0 ? null : new List<DdsRuleLeaf>(c.Count);
            for (int i = 0; i < c.Count && i < RuleLeafPlan.MaxLeaves; i++)
                list!.Add(new DdsRuleLeaf { Label = c.Leaves[i].Label.ToString(), Value = c.Leaves[i].Value, Measured = c.Leaves[i].Measured });
            return new EntityRuleLeaves { EntityId = netId, Leaves = list! };
        }

        internal static bool Same(in EntityRuleLeaves a, in EntityRuleLeaves b)
        {
            int na = a.Leaves?.Count ?? 0, nb = b.Leaves?.Count ?? 0;
            if (na != nb) return false;
            for (int i = 0; i < na; i++)
            {
                var x = a.Leaves![i]; var y = b.Leaves![i];
                if (x.Label != y.Label || x.Value != y.Value || x.Measured != y.Measured) return false;
            }
            return true;
        }

        public void Dispose(int networkEntityId) => _last.Remove(networkEntityId);

        /// <summary>⭐ CE-4079 ② — the grantee confirmed (F7): let go of the instance WITHOUT disposing it, and forget the cache so a reclaim republishes.</summary>
        public void ReleaseInstance(int networkEntityId, long instanceId)
        {
            if (_last.Remove(networkEntityId)) _writer?.UnregisterInstance(new EntityRuleLeaves { EntityId = networkEntityId });
        }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// ⭐ rules V-3 — applies a published <see cref="RuleLeaves"/> on every node that does not own it. A sample for an entity this node does not know yet is HELD (the newest per entity)
    /// until it does. The recorded owner never takes its own sample back (S8 / F-5). Same shape as <see cref="EntityExerciseResultIngressTranslator"/>.
    /// </summary>
    public sealed class EntityRuleLeavesIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtRuleLeaves;
        private readonly DdsReader<EntityRuleLeaves>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly Dictionary<int, EntityRuleLeaves> _pending = new();
        private readonly List<int> _done = new();

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityRuleLeaves>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public int PendingCount => _pending.Count;
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityRuleLeavesIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityRuleLeaves>(participant) : null;
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
        internal void Receive(in EntityRuleLeaves d) { ReceivedSampleCount++; _pending[d.EntityId] = d; }

        internal void Flush(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_pending.Count == 0) return;
            foreach (var (id, d) in _pending)
                if (TryApply(in d, cmd, view)) _done.Add(id);
            foreach (var id in _done) _pending.Remove(id);
            _done.Clear();
        }

        private bool TryApply(in EntityRuleLeaves d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.EntityId, out var env) || !view.IsAlive(env)) return false;
            if (view is EntityRepository repo)
            {
                if (!repo.IsComponentTypeRegistered<RuleLeaves>()) return true;   // not composed here: drop
                if (AuthorityExtensions.IsRecordedOwner(repo, env, OwnershipExtensions.PackKey(OrdinalValue, 0))) return true;   // ours: drop
            }
            cmd.AddComponent(env, FromSample(in d));
            return true;
        }

        /// <summary>The leaves a sample carries (the test seam).</summary>
        internal static RuleLeaves FromSample(in EntityRuleLeaves d)
        {
            var c = default(RuleLeaves);
            int n = Math.Min(d.Leaves?.Count ?? 0, RuleLeafPlan.MaxLeaves);
            c.Count = (byte)n;
            for (int i = 0; i < n; i++)
            {
                var l = d.Leaves![i];
                c.Leaves[i] = new RuleLeaf { Label = new FixedString32(l.Label ?? string.Empty), Value = l.Value, Measured = l.Measured };
            }
            return c;
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) => _pending.Remove(networkEntityId);
    }
}
