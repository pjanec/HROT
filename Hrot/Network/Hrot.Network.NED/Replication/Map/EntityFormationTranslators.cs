using System;
using System.Collections.Generic;
using CarKinem.Formation;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Common.Dds;
using Hrot.NED.Descriptors;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐⭐ <c>CE-9007</c> (R-9007 / R-9012; docs/designs/vehicle-tactics/DESIGN_Road_Convoy.md C-8) — publishes a leader's <see cref="FormationController"/>
    /// from the node owning its <c>dtFormationController</c> (the Brain group: a road convoy's lead sets it to a <see cref="FormationType.Column"/>), keyed by
    /// network id, ON CHANGE only (a column forms and ends; it never changes per frame). The kinematic owner's <c>ConvoyLeadSystem</c> and the traffic node's
    /// gap squares read it. Every vehicle carries a default controller from its type on every node, so a removal is not sent.
    /// </summary>
    public sealed class EntityFormationControllerEgressTranslator : IDescriptorTranslator
    {
        private readonly Fdp.Toolkit.Replication.Utilities.EgressVisitGate _gate = new(typeof(FormationController));
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityFormationController> _instances;
        private static readonly int[] s_targetIds = { GlobalComponentIds.FormationController };

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityFormationController>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtFormationController;
        public IReadOnlyList<int> TargetComponentIds => s_targetIds;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityFormationControllerEgressTranslator(DdsParticipant? participant)
            : this(participant is not null ? new DdsWriterAdapter<EntityFormationController>(participant) : null) { }

        /// <summary>The test seam: a capturing writer instead of a live participant.</summary>
        internal EntityFormationControllerEgressTranslator(IDdsWriter<EntityFormationController>? writer)
            => _instances = new(writer is null ? null : s => writer.Write(s), writer is null ? null : s => writer.DisposeInstance(s),
                writer is null ? null : s => writer.UnregisterInstance(s), static key => new EntityFormationController { EntityId = (int)key });

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<FormationController>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            _gate.Begin(view);
            foreach (var e in view.Query().With<FormationController>().DefaultRoutePublishable().Build())
            {
                if (!_gate.Visit(e)) continue;
                if (!view.HasAuthority(e, packedKey)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(e).Value;
                var sample = ToSample(netId, view.GetComponentRO<FormationController>(e));
                if (_instances.Unchanged(netId, sample)) continue;   // on change only
                _instances.Write(netId, sample);
                SentSampleCount++;
            }
        }

        internal static EntityFormationController ToSample(int netId, in FormationController c)
            => new()
            {
                EntityId = netId, TemplateId = c.TemplateId, Type = (byte)c.Type, Spacing = c.Params.Spacing, WedgeAngleRad = c.Params.WedgeAngleRad,
                MaxCatchUpFactor = c.Params.MaxCatchUpFactor, BreakDistance = c.Params.BreakDistance, ArrivalThreshold = c.Params.ArrivalThreshold,
                SpeedFilterTau = c.Params.SpeedFilterTau,
            };

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-9007</c> — publishes a follower's <see cref="FormationFollower"/> slot when it names an EXPLICIT <see cref="FormationFollower.Leader"/> (a road
    /// column's rank — only the convoy sets one), from the node owning its <c>dtFormationFollower</c> (the Brain group), on change; and ONE sample with
    /// <c>LeaderId = 0</c> when the slot is gone (the vehicle left the column), so every node drops it too (the <c>EntityDescent</c> pattern). ⛔ A
    /// commander-led slot is NOT sent: <c>UnitHierarchySystem</c> derives it on every node from the replicated assign events — one producer each.
    /// </summary>
    public sealed class EntityFormationFollowerEgressTranslator : IDescriptorTranslator
    {
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityFormationFollower> _instances;
        private readonly HashSet<int> _seen = new();
        private readonly List<int> _gone = new();
        private static readonly int[] s_targetIds = { GlobalComponentIds.FormationFollower };

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityFormationFollower>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtFormationFollower;
        public IReadOnlyList<int> TargetComponentIds => s_targetIds;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityFormationFollowerEgressTranslator(DdsParticipant? participant)
            : this(participant is not null ? new DdsWriterAdapter<EntityFormationFollower>(participant) : null) { }

        /// <summary>The test seam: a capturing writer instead of a live participant.</summary>
        internal EntityFormationFollowerEgressTranslator(IDdsWriter<EntityFormationFollower>? writer)
            => _instances = new(writer is null ? null : s => writer.Write(s), writer is null ? null : s => writer.DisposeInstance(s),
                writer is null ? null : s => writer.UnregisterInstance(s), static key => new EntityFormationFollower { EntityId = (int)key });

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<FormationFollower>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            _seen.Clear();
            foreach (var e in view.Query().With<FormationFollower>().DefaultRoutePublishable().Build())
            {
                if (!view.HasAuthority(e, packedKey)) continue;
                ref readonly var f = ref view.GetComponentRO<FormationFollower>(e);
                if (f.Leader.IsNull) continue;                                                     // a commander-led slot: derived on every node
                if (!view.IsAlive(f.Leader) || !view.HasComponent<NetworkIdentity>(f.Leader)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(e).Value;
                _seen.Add(netId);
                var sample = ToSample(netId, view.GetComponentRO<NetworkIdentity>(f.Leader).Value, in f);
                if (_instances.Unchanged(netId, sample)) continue;
                Send(netId, sample);
            }
            // a slot that ended here: one "left" sample (LeaderId 0), then nothing until it joins again
            _gone.Clear();
            foreach (long key in _instances.Keys)
                if (_instances.TryGetLast(key, out var prev) && !_seen.Contains((int)key) && prev.LeaderId != 0) _gone.Add((int)key);
            foreach (var netId in _gone) Send(netId, new EntityFormationFollower { EntityId = netId });
        }

        private void Send(int netId, in EntityFormationFollower sample)
        {
            _instances.Write(netId, sample);
            SentSampleCount++;
        }

        internal static EntityFormationFollower ToSample(int netId, int leaderNetId, in FormationFollower f)
            => new() { EntityId = netId, LeaderId = leaderNetId, SlotIndex = f.SlotIndex, State = (byte)f.State, IsInFormation = f.IsInFormation };

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// ⭐ <c>CE-9007</c> — applies a published <see cref="FormationController"/> to this node's copy of the leader. The recorded owner never takes its own
    /// sample back; an unknown entity is dropped (Reliable + TransientLocal re-sends on the next change).
    /// </summary>
    public sealed class EntityFormationControllerIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtFormationController;
        private readonly DdsReader<EntityFormationController>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityFormationController>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityFormationControllerIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityFormationController>(participant) : null;
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

        internal void Apply(in EntityFormationController s, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(s.EntityId, out var e) || !view.IsAlive(e)) { DroppedSampleCount++; return; }
            if (view is EntityRepository repo)
            {
                if (!repo.IsComponentTypeRegistered<FormationController>()) { DroppedSampleCount++; return; }
                if (AuthorityExtensions.IsRecordedOwner(repo, e, OwnershipExtensions.PackKey(OrdinalValue, 0))) return;
            }
            cmd.AddComponent(e, new FormationController
            {
                TemplateId = s.TemplateId, Type = (FormationType)s.Type,
                Params = new FormationParams
                {
                    Spacing = s.Spacing, WedgeAngleRad = (Angle)s.WedgeAngleRad, MaxCatchUpFactor = s.MaxCatchUpFactor, BreakDistance = s.BreakDistance,
                    ArrivalThreshold = s.ArrivalThreshold, SpeedFilterTau = s.SpeedFilterTau,
                },
            });
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) { }
    }

    /// <summary>
    /// ⭐ <c>CE-9007</c> — applies a published explicit-leader <see cref="FormationFollower"/> slot, its leader resolved to THIS node's entity; <c>LeaderId 0</c>
    /// removes the slot — but only an explicit-leader one (a commander-led slot here is this node's own, from <c>UnitHierarchySystem</c>). An unknown entity
    /// or leader is dropped; the recorded owner never takes its own sample.
    /// </summary>
    public sealed class EntityFormationFollowerIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtFormationFollower;
        private readonly DdsReader<EntityFormationFollower>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityFormationFollower>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityFormationFollowerIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityFormationFollower>(participant) : null;
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

        internal void Apply(in EntityFormationFollower s, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(s.EntityId, out var e) || !view.IsAlive(e)) { DroppedSampleCount++; return; }
            if (view is EntityRepository repo)
            {
                if (!repo.IsComponentTypeRegistered<FormationFollower>()) { DroppedSampleCount++; return; }
                if (AuthorityExtensions.IsRecordedOwner(repo, e, OwnershipExtensions.PackKey(OrdinalValue, 0))) return;
            }
            if (s.LeaderId == 0)
            {
                if (view.HasComponent<FormationFollower>(e) && !view.GetComponentRO<FormationFollower>(e).Leader.IsNull) cmd.RemoveComponent<FormationFollower>(e);
                return;
            }
            if (!_entityMap.TryGetEntity(s.LeaderId, out var leader) || !view.IsAlive(leader)) { DroppedSampleCount++; return; }
            cmd.AddComponent(e, new FormationFollower
            {
                SlotIndex = s.SlotIndex, State = (FormationMemberState)s.State, IsInFormation = s.IsInFormation, Leader = leader,
            });
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) { }
    }
}
