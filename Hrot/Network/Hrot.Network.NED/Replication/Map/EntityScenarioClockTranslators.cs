using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Weather;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ CE-3216 S1-0 (R-3012; 📄 docs/designs/weather/DESIGN_Weather_And_Time.md W-4, W-8) — publishes the World Environment's
    /// <see cref="ScenarioClock"/> from the node owning its <c>dtScenarioClock</c> (the creating Brain, which also applies a runtime edit
    /// of the start time), on change. Same shape as <see cref="EntityWeaponSystemStateEgressTranslator"/>. ⭐ The entity has no
    /// <c>SimTransform</c> and nothing here asks for one.
    /// </summary>
    public sealed class EntityScenarioClockEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityScenarioClock";
        private readonly DdsWriter<EntityScenarioClock>? _writer;
        private readonly Dictionary<long, EntityScenarioClock> _last = new();
        private int _worldEpoch;

        private static readonly int[] s_targetIds = { GlobalComponentIds.ScenarioClock };

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => (long)EDescriptorType.dtScenarioClock;
        /// <summary>⭐ The pairing the attribute path needs: the <c>"ScenarioStartUtcTicks"</c> attribute writes <see cref="ScenarioClock"/>,
        /// and this descriptor republishes it (<c>TheDescriptorMapIsWiredTests.EveryComponentTheVocabularyWritesIsCoveredByATranslator</c>).</summary>
        public IReadOnlyList<int> TargetComponentIds => s_targetIds;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityScenarioClockEgressTranslator(DdsParticipant? participant)
            => _writer = participant is not null ? new DdsWriter<EntityScenarioClock>(participant, DdsTopicName) : null;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (WorldEpoch.Moved(view, ref _worldEpoch)) _last.Clear();
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<ScenarioClock>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var env in view.Query().With<ScenarioClock>().With<NetworkIdentity>().Build())
            {
                if (!view.HasAuthority(env, packedKey)) continue;
                long netId = view.GetComponentRO<NetworkIdentity>(env).Value;
                var sample = ToSample(netId, view.GetComponentRO<ScenarioClock>(env));
                if (_last.TryGetValue(netId, out var prev) && prev.Equals(sample)) continue;
                _last[netId] = sample;
                _writer?.Write(sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityScenarioClock ToSample(long netId, in ScenarioClock c)
            => new() { EntityId = (int)netId, StartUtcTicks = c.StartUtcTicks, DisplayUtcOffsetMinutes = c.DisplayUtcOffsetMinutes };

        public void Dispose(long networkEntityId) => _last.Remove(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// ⭐ CE-3216 S1-0 — applies the published scenario clock on every node that does not own it. A sample for an entity this node does
    /// not know yet is HELD (the newest per entity) until it does — it is sent once per change (TransientLocal). The recorded owner
    /// never takes its own sample back (S8 / F-5). Same shape as <see cref="EntityWeaponSystemStateIngressTranslator"/>.
    /// </summary>
    public sealed class EntityScenarioClockIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityScenarioClock";
        private const long OrdinalValue = (long)EDescriptorType.dtScenarioClock;
        private readonly DdsReader<EntityScenarioClock>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly Dictionary<long, EntityScenarioClock> _pending = new();
        private readonly List<long> _done = new();

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        /// <summary>Samples waiting for an entity this node does not know yet.</summary>
        public int PendingCount => _pending.Count;
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityScenarioClockIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityScenarioClock>(participant, DdsTopicName) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

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
        internal void Receive(in EntityScenarioClock d) { ReceivedSampleCount++; _pending[d.EntityId] = d; }

        /// <summary>Applies every held sample whose entity is known.</summary>
        internal void Flush(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_pending.Count == 0) return;
            foreach (var (id, d) in _pending)
                if (TryApply(in d, cmd, view)) _done.Add(id);
            foreach (var id in _done) _pending.Remove(id);
            _done.Clear();
        }

        private bool TryApply(in EntityScenarioClock d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.EntityId, out var env) || !view.IsAlive(env)) return false;
            if (view is EntityRepository repo
                && AuthorityExtensions.IsRecordedOwner(repo, env, OwnershipExtensions.PackKey(OrdinalValue, 0))) return true;   // ours: drop
            cmd.AddComponent(env, new ScenarioClock { StartUtcTicks = d.StartUtcTicks, DisplayUtcOffsetMinutes = d.DisplayUtcOffsetMinutes });
            return true;
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) => _pending.Remove(networkEntityId);
    }
}
