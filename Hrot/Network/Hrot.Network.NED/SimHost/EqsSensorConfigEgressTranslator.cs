using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Utilities;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Spatial.Eqs.Topics;
using Hrot.NED.Descriptors;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>
    /// Brain-side egress translator: publishes <see cref="EqsSensorConfigTopic"/> to the Muscle
    /// node whenever an <c>EqsSensor</c> component is added or mutated on an authority-owned entity.
    /// Uses <c>SmartEgressUtil</c> for dirty-tracking so the topic is only sent on actual changes.
    /// </summary>
    public sealed class EqsSensorConfigEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EqsSensorConfig";
        private readonly DdsWriter<EqsSensorConfigTopic>? _writer;
        // ⭐ CE-490 — the brain READS its own topic too: TransientLocal hands a node every live instance, including the
        //   ones a previous owner wrote. Only keys and last configs are kept — ⛔ no carrier is ever built on a brain.
        private readonly DdsReader<EqsSensorConfigTopic>? _reader;
        private readonly Dictionary<(long ParentNetworkId, int LocalChildIndex), EqsSensorConfigTopic> _onWire = new();
        // Keys this node has already suspended and not seen re-activated since — one write per orphan, not one per scan.
        private readonly HashSet<(long ParentNetworkId, int LocalChildIndex)> _suspendedByUs = new();
        private readonly List<(long ParentNetworkId, int LocalChildIndex)> _orphans = new();
        private readonly NetworkEntityMap _entityMap;

        // ⭐ CE-3002 / R-179 — the brain names each child sensor's solver. 📄 DESIGN_Ownership_Groups_And_Grants.md §5.8.
        private readonly Hrot.Network.Routing.IClusterStateCache? _clusterCache;
        private readonly int _localNodeId;
        private readonly Dictionary<(long ParentNetworkId, int LocalChildIndex), int> _solverOf = new();
        private readonly Dictionary<(long ParentNetworkId, int LocalChildIndex), int> _recordedSolver = new();
        private OwnershipApplier? _applier;

        // What was last written per sensor entity. ⭐ A reliable topic that publishes ONCE never
        // carries a later parameter change (epoch bump, new area) to the Muscle — the old
        // SmartEgressUtil gate did exactly that, and the distributed rails worked around it by
        // removing and re-adding the sensor. Publishing on any change is what keeps the split right.
        private readonly Dictionary<Entity, EqsSensorConfigTopic> _published = new();
        // The parent each child sensor was published under — an ended sensor is SUSPENDED while that parent lives and
        // disposed only once it is gone (CE-486).
        private readonly Dictionary<Entity, Entity> _parentOf = new();
        private readonly HashSet<Entity> _seen = new();
        // The wire keys a LIVE local sensor holds this scan — every local sensor, with authority or not. CE-486: an ended
        // sensor whose key a live one now holds writes nothing (the same-scan write-then-end race, design §2 ①, cannot
        // happen). CE-490: an instance a local sensor holds is never an orphan.
        private readonly HashSet<(long ParentNetworkId, int LocalChildIndex)> _liveKeys = new();
        private readonly List<Entity> _gone = new();

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => (long)EDescriptorType.dtEqsSensorConfig;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        /// <param name="clusterCache">⭐ R-179: where the solver of each child sensor is picked (least-loaded Perception
        /// node). <see langword="null"/> names no solver — every Perception node solves and the record gates the publish.</param>
        /// <param name="localNodeId">This node, for the ownership record the pick writes.</param>
        public EqsSensorConfigEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap,
                                               Hrot.Network.Routing.IClusterStateCache? clusterCache = null, int localNodeId = 0)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            if (entityMap   == null) throw new ArgumentNullException(nameof(entityMap));
            _entityMap    = entityMap;
            _clusterCache = clusterCache;
            _localNodeId  = localNodeId;
            _writer = new DdsWriter<EqsSensorConfigTopic>(participant, DdsTopicName);
            _reader = new DdsReader<EqsSensorConfigTopic>(participant, DdsTopicName);
        }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view)
        {
            if (_writer is null) return;

            // Query all entities with EqsSensor regardless of NetworkIdentity:
            // child-entity sensors identify themselves via PartMetadata.
            var query = view.Query()
                .With<EqsSensor>()
                .Build();

            ReadWire();

            _seen.Clear();
            _liveKeys.Clear();
            foreach (var entity in query)
            {
                // The wire key — the ONE rule (EqsSensorKey). A child whose parent is gone or local-only, and a
                // local-only sensor, publish nothing.
                var kind = EqsSensorKey.Resolve(view, entity, out long parentNetworkId, out int localChildIndex, out var parent);
                if (kind is EqsSensorKeyKind.None or EqsSensorKeyKind.LocalOnly) continue;
                _liveKeys.Add((parentNetworkId, localChildIndex));

                // ⭐ CE-507 (S6): the key is (descriptor, part instance) — the raw ordinal matched no record entry, so the
                //   gate always fell to the parent's PRIMARY owner, whatever the brain group's grant said.
                if (!view.HasAuthority(entity, OwnershipExtensions.PackKey(DescriptorOrdinal, localChildIndex))) continue;

                ref readonly var sensor = ref view.GetComponentRO<EqsSensor>(entity);
                if (kind == EqsSensorKeyKind.Child) _parentOf[entity] = parent;

                _seen.Add(entity);

                // Hold while a context slot names an entity that has no network id yet: sending 0
                // would tell the Muscle "no entity", and nothing would re-send once the id existed.
                if (!TrySlotNetId(sensor.ContextSlot0, out long slot0)
                    || !TrySlotNetId(sensor.ContextSlot1, out long slot1)
                    || !TrySlotNetId(sensor.ContextSlot2, out long slot2))
                    continue;

                var topic = new EqsSensorConfigTopic
                {
                    ParentNetworkId       = parentNetworkId,
                    LocalChildIndex       = localChildIndex,
                    BlueprintId           = sensor.BlueprintId,
                    Epoch                 = sensor.Epoch,
                    SearchRadius          = sensor.SearchRadius,
                    FactionFilter         = sensor.FactionFilter,
                    ThreatThreshold       = sensor.ThreatThreshold,
                    PublishPolicy         = sensor.PublishPolicy,
                    Priority              = sensor.Priority,
                    ScoreDeltaThreshold   = sensor.ScoreDeltaThreshold,
                    ContextSlot0NetworkId = slot0,
                    ContextSlot1NetworkId = slot1,
                    ContextSlot2NetworkId = slot2,
                    Suspended             = sensor.Suspended,
                    // ⭐ R-179: a child sensor names its solver; a legacy instance-0 sensor keeps its Perception group.
                    SolverNodeId          = localChildIndex == 0 ? 0 : SolverFor((parentNetworkId, localChildIndex)),
                };
                if (topic.SolverNodeId != 0 && !parent.IsNull && view is EntityRepository repo)
                    RecordSolver(repo, parent, (parentNetworkId, localChildIndex), topic.SolverNodeId);

                if (_published.TryGetValue(entity, out var last) && SameConfig(in last, in topic))
                    continue;

                _writer.Write(topic);
                _published[entity] = topic;
                // What this node put on the wire is part of the wire's state — the sweep below ends it when no local
                // sensor holds the key any more, without waiting for our own sample to echo back through the reader.
                _onWire[(parentNetworkId, localChildIndex)] = topic;
                if (!topic.Suspended) _suspendedByUs.Remove((parentNetworkId, localChildIndex));
                SentSampleCount++;
                SmartEgressUtil.MarkPublished(view, entity, DescriptorOrdinal);
            }

            // ⭐⭐ CE-486 — a child sensor (LocalChildIndex != 0) that is destroyed or loses its EqsSensor has ENDED.
            //    📄 DESIGN_Behaviour_Fault_And_Teardown.md §1 D5 ③. Its descriptor instance is NEVER disposed while the
            //    parent lives (BDC/NED descriptor rules — a dispose means entity deletion or ownership return): the end is
            //    a Suspended write, so the Muscle's carrier stops solving and the part id can be reused by the next
            //    lifetime (D5 ①). ⭐ That write is NOT made here: an ended sensor is just "an instance no local sensor
            //    holds, under an entity this node owns" — exactly what SweepOrphans ends (CE-490). ONE rule, one path
            //    (measured: a separate end-write here was fully shadowed by the sweep). It also covers the §2 ① race —
            //    a live sensor holding the same key this scan keeps it out of the sweep. Only when the PARENT is gone is
            //    the instance disposed. Legacy single sensors are disposed by the removal pass below and by Dispose().
            _gone.Clear();
            foreach (var kv in _published)
            {
                if (_seen.Contains(kv.Key)) continue;
                if (view.IsAlive(kv.Key) && view.HasComponent<EqsSensor>(kv.Key)) continue; // e.g. authority moved
                _gone.Add(kv.Key);
            }
            foreach (var entity in _gone)
            {
                var last = _published[entity];
                _published.Remove(entity);
                _parentOf.Remove(entity, out var parent);
                if (last.LocalChildIndex == 0) continue;

                var key = (last.ParentNetworkId, last.LocalChildIndex);
                if (_liveKeys.Contains(key)) continue;                         // a live sensor holds this part id now
                if (!parent.IsNull && view.IsAlive(parent)) continue;          // ended, not deleted: the sweep suspends it

                _writer.DisposeInstance(new EqsSensorConfigTopic               // the parent is gone: entity deletion
                {
                    ParentNetworkId = last.ParentNetworkId,
                    LocalChildIndex = last.LocalChildIndex,
                });
                _onWire.Remove(key);
                _suspendedByUs.Remove(key);
            }

            SweepOrphans(view);

            // Removal detection: find entities with NetworkIdentity that no longer carry
            // EqsSensor. These are legacy single-sensor entities (LocalChildIndex == 0).
            var removalQuery = view.Query()
                .With<NetworkIdentity>()
                .Without<EqsSensor>()
                .Build();

            foreach (var entity in removalQuery)
            {
                if (!view.HasAuthority(entity, OwnershipExtensions.PackKey(DescriptorOrdinal, 0))) continue;
                if (!view.HasManagedComponent<EgressPublicationState>(entity)) continue;

                var state = view.GetManagedComponentRO<EgressPublicationState>(entity);
                if (!state.LastPublishedTickMap.ContainsKey(DescriptorOrdinal)) continue;

                // Entity lost EqsSensor after a prior publish -- send dispose.
                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
                _writer.DisposeInstance(new EqsSensorConfigTopic { ParentNetworkId = netId.Value, LocalChildIndex = 0 });
                state.LastPublishedTickMap.Remove(DescriptorOrdinal);
            }
        }

        // CE-490: the latest sample of every instance on the wire. A dispose (the parent's death) forgets the key.
        private void ReadWire()
        {
            if (_reader is null) return;
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (sample.IsValid)
                {
                    var data = sample.Data;
                    var key  = (data.ParentNetworkId, data.LocalChildIndex);
                    _onWire[key] = data;
                    if (!data.Suspended) _suspendedByUs.Remove(key);   // re-activated by someone: a new orphan later is new
                    ReceivedSampleCount++;
                }
                else
                {
                    var keyData = DdsTypeSupport.FromNative<EqsSensorConfigTopic>(sample.NativePtr);
                    var key     = (keyData.ParentNetworkId, keyData.LocalChildIndex);
                    _onWire.Remove(key);
                    _suspendedByUs.Remove(key);
                }
            }
        }

        /// <summary>
        /// ⭐⭐ <c>CE-486</c> + <c>CE-490</c> — <b>the ONE place a child sensor is ended on the wire:</b> every instance under
        /// an entity this node holds authority over that no local sensor holds is written back <c>Suspended</c> — this
        /// node's own ended sensors and the ones it inherited alike. ⭐ <b>A new authority ends the sensors it inherited.</b> 📄 <c>DESIGN_Behaviour_Fault_And_Teardown.md</c>
        /// §1 D5 ④. After an authority move the old owner stops ticking the entity but never ends its behaviour
        /// (<c>BrainTickSystem</c> ticks owned entities only), so the sensors it created are never ended either — without
        /// this the Muscle would solve them forever. ⇒ every child-sensor instance on the wire whose parent THIS node holds
        /// authority over, and that no local sensor holds, gets its last config written back <c>Suspended</c>. Once per
        /// orphan; a later active write by anyone re-arms it.
        /// </summary>
        private void SweepOrphans(ISimulationView view)
        {
            _orphans.Clear();
            foreach (var (key, last) in _onWire)
            {
                if (key.LocalChildIndex == 0 || last.Suspended) continue;     // legacy, or already ended
                if (_liveKeys.Contains(key) || _suspendedByUs.Contains(key)) continue;
                if (!_entityMap.TryGetEntity(key.ParentNetworkId, out var parent) || !view.IsAlive(parent)) continue;
                if (!view.HasAuthority(parent, OwnershipExtensions.PackKey(DescriptorOrdinal, key.LocalChildIndex))) continue;    // another node's sensor
                _orphans.Add(key);
            }

            foreach (var key in _orphans)
            {
                var suspended = _onWire[key];
                suspended.Suspended = true;
                _writer!.Write(suspended);
                _suspendedByUs.Add(key);
                SentSampleCount++;
            }
        }

        /// <summary>
        /// ⭐ <c>R-179</c> — the solver of one child sensor: picked ONCE (the least-loaded Perception node) and kept; a
        /// sensor this node inherited keeps the solver already on the wire; re-picked only when that node has left the
        /// cluster cache (a departure removes it, S7). <c>0</c> while no Perception node is known — the next scan retries.
        /// </summary>
        private int SolverFor((long ParentNetworkId, int LocalChildIndex) key)
        {
            if (_clusterCache == null) return 0;
            var known = _clusterCache.AllNodeIds();
            if (_solverOf.TryGetValue(key, out int solver) && Contains(known, solver)) return solver;
            solver = _onWire.TryGetValue(key, out var wire) && wire.SolverNodeId != 0 && Contains(known, wire.SolverNodeId)
                ? wire.SolverNodeId
                : _clusterCache.GetLeastLoadedNode(NodeRole.Perception) ?? 0;
            if (solver != 0) _solverOf[key] = solver;
            return solver;
        }

        private static bool Contains(IReadOnlyList<int> nodes, int node)
        {
            for (int i = 0; i < nodes.Count; i++) if (nodes[i] == node) return true;
            return false;
        }

        /// <summary>⭐ R-179: the config is the per-sensor grant — this node records the solver as the owner of result
        /// part <c>n</c>, as every Perception node does on reading the config.</summary>
        private void RecordSolver(EntityRepository repo, Entity parent, (long ParentNetworkId, int LocalChildIndex) key, int solver)
        {
            if (_recordedSolver.TryGetValue(key, out int recorded) && recorded == solver) return;
            _applier ??= new OwnershipApplier(_localNodeId,
                Fdp.Toolkit.Replication.Attributes.AttributeInterpreterProvider.GetDescriptorMap(repo));
            _applier.Apply(repo, parent, OwnershipExtensions.PackKey((long)EDescriptorType.dtEqsResult, key.LocalChildIndex), solver);
            _recordedSolver[key] = solver;
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId)
        {
            _writer?.DisposeInstance(new EqsSensorConfigTopic { ParentNetworkId = networkEntityId, LocalChildIndex = 0 });
        }

        // A null slot is 0 on the wire. A non-null slot must have a network id; false = not yet.
        private bool TrySlotNetId(Entity slotEntity, out long netId)
        {
            netId = 0L;
            if (slotEntity.IsNull) return true;
            return _entityMap.TryGetNetworkId(slotEntity, out netId);
        }

        private static bool SameConfig(in EqsSensorConfigTopic a, in EqsSensorConfigTopic b)
            => a.ParentNetworkId       == b.ParentNetworkId
            && a.LocalChildIndex       == b.LocalChildIndex
            && a.BlueprintId           == b.BlueprintId
            && a.Epoch                 == b.Epoch
            && a.SearchRadius.Equals(b.SearchRadius)
            && a.FactionFilter         == b.FactionFilter
            && a.ThreatThreshold.Equals(b.ThreatThreshold)
            && a.PublishPolicy         == b.PublishPolicy
            && a.Priority              == b.Priority
            && a.ScoreDeltaThreshold.Equals(b.ScoreDeltaThreshold)
            && a.ContextSlot0NetworkId == b.ContextSlot0NetworkId
            && a.ContextSlot1NetworkId == b.ContextSlot1NetworkId
            && a.ContextSlot2NetworkId == b.ContextSlot2NetworkId
            && a.Suspended             == b.Suspended
            && a.SolverNodeId          == b.SolverNodeId;   // R-179: a re-pick is a change the solvers must hear
    }
}

