using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using Hrot.Map.Common.Dds;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Animals;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐⭐ <c>CE-4623</c> AB-3 (docs/designs/animals/DESIGN_Animals.md §6.1) — publishes an animal's <see cref="AnimalState"/> (mode, fear band,
    /// species, flags) from the node owning its <c>dtAnimalState</c> (the Brain group — where its fear system and its HSM run), keyed by network
    /// id, ON CHANGE only (the band changes a few times a fright, never per frame). Every other node's presentation reads it (pose, gait, the
    /// alarm look). The cache clears when the world moves (<c>WorldEpoch</c>, CE-4007).
    /// </summary>
    public sealed class EntityAnimalStateEgressTranslator : IDescriptorTranslator
    {
        private readonly Fdp.Toolkit.Replication.Utilities.EgressVisitGate _gate = new(typeof(AnimalState));
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityAnimalState> _instances;
        private static readonly int[] s_targetIds = { GlobalComponentIds.AnimalState };

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityAnimalState>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtAnimalState;
        public IReadOnlyList<int> TargetComponentIds => s_targetIds;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityAnimalStateEgressTranslator(DdsParticipant? participant)
            : this(participant is not null ? new DdsWriterAdapter<EntityAnimalState>(participant) : null) { }

        /// <summary>The test seam: a capturing writer instead of a live participant.</summary>
        internal EntityAnimalStateEgressTranslator(IDdsWriter<EntityAnimalState>? writer)
            => _instances = new(writer is null ? null : s => writer.Write(s), writer is null ? null : s => writer.DisposeInstance(s),
                writer is null ? null : s => writer.UnregisterInstance(s), static key => new EntityAnimalState { EntityId = (int)key });

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<AnimalState>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            _gate.Begin(view);
            foreach (var e in view.Query().With<AnimalState>().DefaultRoutePublishable().Build())
            {
                if (!_gate.Visit(e)) continue;
                if (!view.HasAuthority(e, packedKey)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(e).Value;
                var sample = ToSample(netId, view.GetComponentRO<AnimalState>(e));
                if (_instances.TryGetLast(netId, out var prev) && Same(prev, sample)) continue;   // on change only
                _instances.Write(netId, sample);
                SentSampleCount++;
            }
        }

        internal static EntityAnimalState ToSample(int netId, in AnimalState c)
            => new() { EntityId = netId, Mode = (byte)c.Mode, FearBand = c.FearBand, Species = (byte)c.Species, Flags = (byte)c.Flags };

        private static bool Same(in EntityAnimalState a, in EntityAnimalState b)
            => a.Mode == b.Mode && a.FearBand == b.FearBand && a.Species == b.Species && a.Flags == b.Flags;

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }

    /// <summary>
    /// ⭐ <c>CE-4623</c> AB-3 — publishes a bird flock's <see cref="FlockState"/> (birds left, seed, flying / landed / perched) from its Brain
    /// node, on change. The presentation draws the N birds from it on every node.
    /// </summary>
    public sealed class EntityFlockStateEgressTranslator : IDescriptorTranslator
    {
        private readonly Fdp.Toolkit.Replication.Utilities.EgressVisitGate _gate = new(typeof(FlockState));
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityFlockState> _instances;
        private static readonly int[] s_targetIds = { GlobalComponentIds.FlockState };

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityFlockState>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtFlockState;
        public IReadOnlyList<int> TargetComponentIds => s_targetIds;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityFlockStateEgressTranslator(DdsParticipant? participant)
            : this(participant is not null ? new DdsWriterAdapter<EntityFlockState>(participant) : null) { }

        internal EntityFlockStateEgressTranslator(IDdsWriter<EntityFlockState>? writer)
            => _instances = new(writer is null ? null : s => writer.Write(s), writer is null ? null : s => writer.DisposeInstance(s),
                writer is null ? null : s => writer.UnregisterInstance(s), static key => new EntityFlockState { EntityId = (int)key });

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<FlockState>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            _gate.Begin(view);
            foreach (var e in view.Query().With<FlockState>().DefaultRoutePublishable().Build())
            {
                if (!_gate.Visit(e)) continue;
                if (!view.HasAuthority(e, packedKey)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(e).Value;
                var sample = ToSample(netId, view.GetComponentRO<FlockState>(e));
                if (_instances.TryGetLast(netId, out var prev)
                    && prev.Count == sample.Count && prev.InitialCount == sample.InitialCount && prev.Seed == sample.Seed && prev.Mode == sample.Mode) continue;
                _instances.Write(netId, sample);
                SentSampleCount++;
            }
        }

        internal static EntityFlockState ToSample(int netId, in FlockState c)
            => new() { EntityId = netId, Count = c.Count, InitialCount = c.InitialCount, Seed = c.Seed, Mode = (byte)c.Mode };

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// ⭐ <c>CE-4623</c> AB-3 — applies a published <see cref="AnimalState"/> to this node's copy of the animal. The recorded owner never takes
    /// its own sample back; an unknown entity is dropped (Reliable + TransientLocal re-sends on the next change).
    /// </summary>
    public sealed class EntityAnimalStateIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtAnimalState;
        private readonly DdsReader<EntityAnimalState>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityAnimalState>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityAnimalStateIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityAnimalState>(participant) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        public int DiscardIngress() => _reader.DiscardAll();

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

        internal void Apply(in EntityAnimalState s, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(s.EntityId, out var e) || !view.IsAlive(e)) { DroppedSampleCount++; return; }
            if (view is EntityRepository repo)
            {
                if (!repo.IsComponentTypeRegistered<AnimalState>()) { DroppedSampleCount++; return; }
                if (AuthorityExtensions.IsRecordedOwner(repo, e, OwnershipExtensions.PackKey(OrdinalValue, 0))) return;
            }
            cmd.AddComponent(e, new AnimalState { Mode = (AnimalMode)s.Mode, FearBand = s.FearBand, Species = (AnimalSpecies)s.Species, Flags = (AnimalFlags)s.Flags });
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) { }
    }

    /// <summary>⭐ <c>CE-4623</c> AB-3 — applies a published <see cref="FlockState"/> to this node's copy of the flock.</summary>
    public sealed class EntityFlockStateIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtFlockState;
        private readonly DdsReader<EntityFlockState>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityFlockState>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityFlockStateIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityFlockState>(participant) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        public int DiscardIngress() => _reader.DiscardAll();

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

        internal void Apply(in EntityFlockState s, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(s.EntityId, out var e) || !view.IsAlive(e)) { DroppedSampleCount++; return; }
            if (view is EntityRepository repo)
            {
                if (!repo.IsComponentTypeRegistered<FlockState>()) { DroppedSampleCount++; return; }
                if (AuthorityExtensions.IsRecordedOwner(repo, e, OwnershipExtensions.PackKey(OrdinalValue, 0))) return;
            }
            cmd.AddComponent(e, new FlockState { Count = s.Count, InitialCount = s.InitialCount, Seed = s.Seed, Mode = (FlockMode)s.Mode });
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) { }
    }
}
