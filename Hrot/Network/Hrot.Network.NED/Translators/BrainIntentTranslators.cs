using System;
using System.Collections.Generic;
using System.Text.Json;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Core.Serialization;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Common.Dds;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;

namespace Hrot.Network.Translators
{
    /// <summary>
    /// ⭐⭐ <b><c>CE-3048</c> — the Brain owner publishes what its unit runs.</b> 📄 <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §7.7.
    /// <para>For every unit whose <see cref="EDescriptorType.dtBrainIntent"/> this node owns: when the slots' SIGNATURE moved
    /// (task, SOP, ROE — unmanaged reads only, every frame), it reads the intent with <see cref="BrainIntentReader"/> in the
    /// <see cref="BrainIntentScope.Running"/> scope and writes it when the JSON differs from the last one sent. ⭐ On the
    /// <c>EntityMission</c> precedent (CE-483 W2): every writer of the slots is covered without remembering a MarkDirty.</para>
    /// </summary>
    public sealed class BrainIntentEgressTranslator : IDescriptorTranslator
    {
        internal const string DdsTopicName = "EntityBrainIntent";

        private readonly IDdsWriter<EntityBrainIntent> _writer;
        private readonly BehaviorRegistry _registry;
        private readonly Dictionary<long, Signature> _lastSignature = new();
        private readonly Dictionary<long, string> _lastSent = new();
        private int _worldEpoch;   // ⭐ CE-2101 — per-id bookkeeping belongs to ONE world (WorldEpoch)

        public string TopicName         => DdsTopicName;
        public long   DescriptorOrdinal => (long)EDescriptorType.dtBrainIntent;
        public long   ReceivedSampleCount { get; private set; }
        public long   SentSampleCount     { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        /// <summary>Production constructor — creates a live DDS writer.</summary>
        public BrainIntentEgressTranslator(DdsParticipant participant, BehaviorRegistry registry)
            : this(new DdsWriterAdapter<EntityBrainIntent>(participant, DdsTopicName), registry)
        {
        }

        /// <summary>Testable constructor — accepts an injected writer.</summary>
        internal BrainIntentEgressTranslator(IDdsWriter<EntityBrainIntent> writer, BehaviorRegistry registry)
        {
            _writer   = writer   ?? throw new ArgumentNullException(nameof(writer));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view)
        {
            if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(view, ref _worldEpoch)) { _lastSignature.Clear(); _lastSent.Clear(); }   // CE-2101 — the next world resends
            if (view is not EntityRepository repo) return;
            long key = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            var query = view.Query().With<NetworkIdentity>().With<BehaviorState>().WithLifecycle(EntityLifecycle.All).Build();
            foreach (var entity in query)
            {
                if (!view.HasAuthority(entity, key)) continue;
                long netId = view.GetComponentRO<NetworkIdentity>(entity).Value;

                var signature = Signature.Of(repo, entity);
                if (_lastSignature.TryGetValue(netId, out var last) && last.Equals(signature)) continue;
                _lastSignature[netId] = signature;

                var intent = BrainIntentReader.Read(repo, entity, _registry, BrainIntentScope.Running);
                string json = JsonSerializer.Serialize(intent, FdpJsonOptionsRegistry.DefaultRelaxed);
                if (_lastSent.TryGetValue(netId, out var sent) && sent == json) continue;

                _writer.Write(new EntityBrainIntent { EntityId = netId, IntentJson = json });
                _lastSent[netId] = json;
                SentSampleCount++;
            }
        }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <summary>The unit is gone: forget it and end its durable instance, so a late joiner is not handed a dead unit.</summary>
        public void Dispose(long networkEntityId)
        {
            _lastSignature.Remove(networkEntityId);
            if (_lastSent.Remove(networkEntityId))
                _writer.DisposeInstance(new EntityBrainIntent { EntityId = networkEntityId, IntentJson = string.Empty });
        }

        /// <summary>What the published intent depends on, as plain values — cheap to compare every frame.</summary>
        internal readonly record struct Signature(
            int Hash, uint Instance, BehaviorOrigin Origin,
            int SopHash, uint SopInstance, BehaviorOrigin SopOrigin,
            RoeFire Fire, RoeReactions Reactions, BehaviorOrigin RoeSetBy, float RoeReturnFireWindow)
        {
            public static Signature Of(EntityRepository repo, Entity entity)
            {
                var state = repo.GetComponentRO<BehaviorState>(entity);
                var sop = repo.IsComponentTypeRegistered<SopState>() && repo.HasComponent<SopState>(entity)
                    ? repo.GetComponentRO<SopState>(entity) : default;
                var roe = repo.IsComponentTypeRegistered<Roe>() && repo.HasComponent<Roe>(entity)
                    ? repo.GetComponentRO<Roe>(entity) : default;
                return new Signature(state.ActiveBehaviorHash, state.InstanceId, state.Origin,
                                     sop.SopHash, sop.SopInstanceId, sop.SopOrigin, roe.Fire, roe.Reactions, roe.SetBy,
                                     roe.ReturnFireWindowSeconds);   // CE-2095: a changed window re-publishes
            }
        }
    }

    /// <summary>
    /// ⭐⭐ <b><c>CE-3048</c> — every other Brain node keeps the last published intent</b> as <see cref="ReplicatedBrainIntent"/>,
    /// for <c>BrainHandOverSystem</c> to start if this node gains the Brain. 📄 <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §7.7.
    /// <para>Skips its own samples (the recorded owner of <see cref="EDescriptorType.dtBrainIntent"/> — the F-5 rule, as
    /// <c>EntityMissionIngressTranslator</c>). A sample for a unit not yet known here is HELD and applied when the unit
    /// appears: the sample is durable but delivered once. A writer that goes away (a failover) does NOT clear the replica
    /// — that is exactly when it is needed.</para>
    /// </summary>
    public sealed class BrainIntentIngressTranslator : IDescriptorTranslator
    {
        private readonly DdsReader<EntityBrainIntent>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly Dictionary<long, string> _held = new();
        private int _worldEpoch;   // ⭐ CE-2101 — per-id bookkeeping belongs to ONE world (WorldEpoch)

        public string TopicName         => BrainIntentEgressTranslator.DdsTopicName;
        public long   DescriptorOrdinal => (long)EDescriptorType.dtBrainIntent;
        public long   ReceivedSampleCount { get; private set; }
        public long   SentSampleCount     { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        /// <summary>Production constructor. A <c>null</c> participant makes <see cref="PollIngress"/> read nothing (tests).</summary>
        public BrainIntentIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader    = participant is not null ? new DdsReader<EntityBrainIntent>(participant, BrainIntentEgressTranslator.DdsTopicName) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(view, ref _worldEpoch)) _held.Clear();   // CE-2101 — held for the last world
            if (_reader is not null)
            {
                using var loan = _reader.Take();
                foreach (var sample in loan)
                {
                    if (!sample.IsValid) continue;            // a dispose or a writer gone: keep what we hold
                    var data = sample.Data;
                    ReceivedSampleCount++;
                    _held[data.EntityId] = data.IntentJson ?? string.Empty;
                }
            }
            Apply(cmd, view);
        }

        /// <summary>Holds one received sample (tests, and the reader above).</summary>
        internal void Receive(in EntityBrainIntent sample) => _held[sample.EntityId] = sample.IntentJson ?? string.Empty;

        /// <summary>Applies every held sample whose unit is known here.</summary>
        internal void Apply(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_held.Count == 0 || view is not EntityRepository repo) return;
            long key = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            List<long>? done = null;
            foreach (var (netId, json) in _held)
            {
                if (!_entityMap.TryGetEntity(netId, out var entity) || !repo.IsAlive(entity)) continue;
                (done ??= new List<long>()).Add(netId);
                if (AuthorityExtensions.IsRecordedOwner(view, entity, key)) continue;   // our own sample came back

                InitialBrainIntent? intent = null;
                try { intent = JsonSerializer.Deserialize<InitialBrainIntent>(json, FdpJsonOptionsRegistry.DefaultRelaxed); }
                catch (JsonException e)
                {
                    Fdp.Core.Logging.FdpLog<BrainIntentIngressTranslator>.Warn(
                        "[BrainIntent] unit {0}: unreadable intent ({1}).", netId, e.Message);
                }
                if (intent == null) continue;

                if (!repo.TryGetTable(typeof(ReplicatedBrainIntent), out _)) repo.RegisterManagedComponent<ReplicatedBrainIntent>();
                cmd.SetManagedComponent(entity, new ReplicatedBrainIntent { Intent = intent });
            }
            if (done != null) foreach (long id in done) _held.Remove(id);
        }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view) { }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) => _held.Remove(networkEntityId);
    }
}
