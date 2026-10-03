using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Utilities;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Spatial.Eqs.Topics;
using Hrot.NED.Descriptors;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>
    /// Muscle-side ingress translator: receives <see cref="EqsSensorConfigTopic"/> samples
    /// and applies the <c>EqsSensor</c> component to the corresponding ghost entity so the
    /// solver picks it up on the next tick.
    /// On <c>NOT_ALIVE_DISPOSED</c>, removes a LEGACY sensor (part 0) from the ghost entity; a child sensor's dispose
    /// only forgets the key — its carrier dies with its parent (<c>CE-486</c>). An ended child sensor arrives as a
    /// <c>Suspended</c> config, which the solver skips. ⭐ <c>CE-492</c>: an instance can have two writers after an authority
    /// move, so samples are taken in SOURCE-TIME order per instance, not arrival order (<see cref="Receive"/>).
    /// </summary>
    public sealed class EqsSensorConfigIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EqsSensorConfig";
        private readonly DdsReader<EqsSensorConfigTopic>? _reader;
        private readonly NetworkEntityMap _entityMap;
        // Dictionary-cached entity lookup: (ParentNetworkId, LocalChildIndex) -> child ghost entity.
        // Avoids ECS query scans inside the polling loop (O(1) steady-state lookup).
        private readonly Dictionary<(long ParentNetId, int ChildIndex), Entity> _childGhostCache = new();

        // ⭐ Samples whose parent or context-slot entities are not on this node YET. DDS Take() consumes
        // a sample, and the Brain re-sends only on change — so a sample applied too early (or dropped
        // because its parent was missing) would leave the sensor without its area FOREVER. Each poll
        // retries these; a sample leaves the set once its parent and every named slot resolve.
        private readonly Dictionary<(long ParentNetId, int ChildIndex), Pending> _pending = new();
        private readonly List<(long ParentNetId, int ChildIndex)> _resolvedKeys = new();
        // Carriers created through the command buffer and not yet visible in the world.
        private readonly HashSet<(long ParentNetId, int ChildIndex)> _awaitingPlayback = new();
        // ⭐ CE-492 — per instance, the newest source time taken; an older sample is stale (two writers after an authority move).
        private readonly SourceTimeOrder<(long ParentNetId, int ChildIndex)> _order = new();

        private struct Pending
        {
            public EqsSensorConfigTopic Data;
            public bool Applied;          // applied at least once (parent was present)
            public int  ResolvedSlotMask; // which named slots resolved when last applied
        }

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => (long)EDescriptorType.dtEqsSensorConfig;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        // ⭐ CE-3002 / R-179 — the config names its solver; only that node builds a carrier. 📄
        //   DESIGN_Ownership_Groups_And_Grants.md §5.8.
        private readonly int _localNodeId;
        private readonly Dictionary<(long ParentNetId, int ChildIndex), int> _recordedSolver = new();
        private OwnershipApplier? _applier;

        /// <summary>Sensors this node was told another node solves (diagnostics and rails).</summary>
        public long SolvedElsewhereCount { get; private set; }

        /// <param name="localNodeId">⭐ R-179: this node — a child sensor whose config names another solver gets no
        /// carrier here.</param>
        public EqsSensorConfigIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap, int localNodeId = 0)
        {
            if (entityMap == null) throw new ArgumentNullException(nameof(entityMap));
            _entityMap   = entityMap;
            _localNodeId = localNodeId;
            _reader = participant != null
                ? new DdsReader<EqsSensorConfigTopic>(participant, DdsTopicName)
                : null;
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;

            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (sample.IsValid)
                {
                    Receive(cmd, sample.Data, valid: true, disposed: false, sample.Info.SourceTimestamp);
                }
                else
                {
                    // For NOT_ALIVE samples the managed .Data property throws.
                    // Read key fields directly from the native serialised buffer.
                    var keyData = DdsTypeSupport.FromNative<EqsSensorConfigTopic>(sample.NativePtr);
                    Receive(cmd, keyData, valid: false,
                        disposed: sample.Info.InstanceState == DdsInstanceState.NotAliveDisposed, sample.Info.SourceTimestamp);
                }
            }

            ApplyPending(cmd, view);
        }

        /// <summary>
        /// One sample, in arrival order. ⭐⭐ <c>CE-492</c> — a sample OLDER (by source time) than the newest one already
        /// taken for its instance is STALE and dropped (<see cref="SourceTimeOrder{TKey}"/>). 📄
        /// <c>DESIGN_Behaviour_Fault_And_Teardown.md</c> §3a. 🔴 The case: after an authority move an instance has two
        /// writers — the old owner's last ACTIVE sample and the new owner's SUSPEND (its orphan sweep, <c>CE-490</c>). A
        /// late-joining Muscle gets both from TransientLocal in discovery order, and whichever came last won — so an ended
        /// sensor could be solved forever. ⚠ Not "the same epoch": the owner run is NOT unique across an authority move (a
        /// new owner counts from the same start), so an epoch match cannot tell a stale sample from a new lifetime.
        /// </summary>
        /// <remarks>Internal so a rail can play a chosen arrival order with chosen source times — real DDS gives a test
        /// control over neither.</remarks>
        internal void Receive(IEntityCommandBuffer cmd, in EqsSensorConfigTopic data, bool valid, bool disposed, long sourceTimestamp)
        {
            if (!valid && !disposed) return;                     // unregistered / no writers: nothing to apply
            long parentNetId    = data.ParentNetworkId;
            int  localChildIndex = data.LocalChildIndex;
            var  cacheKey       = (parentNetId, localChildIndex);
            if (!_order.Accept(cacheKey, sourceTimestamp)) { StaleSampleCount++; return; }
            if (valid) ReceivedSampleCount++;

            if (localChildIndex == 0)
            {
                // Legacy single-sensor path: sensor lives directly on the parent ghost entity.
                if (valid)
                {
                    _pending[cacheKey] = new Pending { Data = data };
                }
                else
                {
                    _pending.Remove(cacheKey);
                    if (!_entityMap.TryGetEntity(parentNetId, out var parentGhost)) return;
                    cmd.RemoveComponent<EqsSensor>(parentGhost);
                    _childGhostCache.Remove(cacheKey);
                }
                return;
            }

            // Child-entity sensor path: carrier ghost is spawned/reused from cache.
            if (valid)
            {
                _pending[cacheKey] = new Pending { Data = data };
            }
            else
            {
                // ⭐⭐ CE-486 — a child-sensor dispose is NOT "destroy the carrier". The Brain never disposes a
                //    child instance while its parent lives (it writes Suspended instead), so a dispose now only
                //    comes with the parent's death — and SubEntityCleanupSystem already destroys that parent's
                //    carriers. 🔴 Destroying here was the design's §2 ② race: a dispose and a write for the same
                //    key in one batch queued the destroy, then applied the new config to the doomed carrier.
                //    📄 DESIGN_Behaviour_Fault_And_Teardown.md §1 D5 ③.
                _pending.Remove(cacheKey);
                _awaitingPlayback.Remove(cacheKey);
                _childGhostCache.Remove(cacheKey);
            }
        }

        /// <summary>Samples dropped as older than their instance's newest (<c>CE-492</c>; diagnostics and rails).</summary>
        public long StaleSampleCount { get; private set; }

        /// <summary>Applies what <see cref="Receive"/> left pending (exposed for rails that drive <c>Receive</c>).</summary>
        internal void ApplyPendingForRail(IEntityCommandBuffer cmd, ISimulationView view) => ApplyPending(cmd, view);

        // Applies every pending sample whose parent is present; re-applies one only when a named slot
        // has newly resolved; drops it from the set once the parent and all named slots resolved.
        private void ApplyPending(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_pending.Count == 0) return;

            _resolvedKeys.Clear();
            foreach (var key in new List<(long ParentNetId, int ChildIndex)>(_pending.Keys))
            {
                var pending = _pending[key];
                if (!_entityMap.TryGetEntity(key.ParentNetId, out var parentGhost)) continue;

                var sensor = BuildSensor(pending.Data);
                int named  = NamedSlotMask(pending.Data);
                int mask   = ResolvedSlotMask(in sensor);

                if (!pending.Applied || mask != pending.ResolvedSlotMask)
                {
                    // A carrier created by an earlier poll is not in the world until that command
                    // buffer plays back — wait for it rather than create a second one.
                    if (!Apply(cmd, view, key, parentGhost, sensor, pending.Data.SolverNodeId)) continue;
                    pending.Applied          = true;
                    pending.ResolvedSlotMask = mask;
                    _pending[key]            = pending;
                }

                if (mask == named) _resolvedKeys.Add(key);
            }

            foreach (var key in _resolvedKeys) _pending.Remove(key);
        }

        // Returns false when the sample must wait (its carrier is created but not yet played back).
        private bool Apply(IEntityCommandBuffer cmd, ISimulationView view, (long ParentNetId, int ChildIndex) key,
                           Entity parentGhost, EqsSensor sensor, int solverNodeId = 0)
        {
            if (key.ChildIndex == 0)
            {
                // Legacy single-sensor path: sensor lives directly on the parent ghost entity.
                cmd.SetComponent(parentGhost, sensor);
                _childGhostCache[key] = parentGhost;
                return true;
            }

            // ⭐ R-179 — the config is the per-sensor grant: every node records the named solver as the owner of result
            //   part n (the S6 result gate reads it). Another node's sensor gets no carrier here, and a carrier left from
            //   an earlier assignment stops solving.
            if (solverNodeId != 0)
            {
                if (view is EntityRepository repo) RecordSolver(repo, parentGhost, key, solverNodeId);
                if (solverNodeId != _localNodeId)
                {
                    SolvedElsewhereCount++;
                    _awaitingPlayback.Remove(key);
                    if (TryFindCarrier(view, parentGhost, key.ChildIndex, out var stale))
                    {
                        var stopped = sensor;
                        stopped.Suspended = true;
                        cmd.SetComponent(stale, stopped);
                    }
                    return true;
                }
            }

            if (TryFindCarrier(view, parentGhost, key.ChildIndex, out var child))
            {
                // Existing carrier: update its parameters.
                _awaitingPlayback.Remove(key);
                cmd.SetComponent(child, sensor);
                return true;
            }

            if (_awaitingPlayback.Contains(key)) return false;

            // ⭐ CE-486 — an ended sensor with no carrier needs none: there is nothing to solve. (A late-joining Muscle
            //   receives every instance TransientLocal holds, suspended ones included.) The next lifetime's config for
            //   this part id creates the carrier then.
            if (sensor.Suspended) return true;

            // ⭐ No carrier yet: create one. ⛔ The handle cmd.CreateEntity() returns is a PLACEHOLDER
            // that is valid only inside this command buffer's playback — it used to be cached and reused
            // for every later update and dispose, so on the Muscle a child sensor's parameters never
            // changed after its first sample and a disposed sensor's carrier was never destroyed. The real
            // entity is found in the world on the next poll (TryFindCarrier).
            // No NetworkIdentity, TkbIdentity, or GhostStateTracker on the carrier.
            child = cmd.CreateEntity();
            cmd.AddComponent(child, new PartMetadata
            {
                ParentEntity      = parentGhost,
                InstanceId        = key.ChildIndex,
            });
            cmd.AddComponent(child, sensor);
            cmd.AddComponent(child, default(EqsCognitiveBuffer));
            _awaitingPlayback.Add(key);
            return true;
        }

        private void RecordSolver(EntityRepository repo, Entity parentGhost, (long ParentNetId, int ChildIndex) key, int solver)
        {
            if (_recordedSolver.TryGetValue(key, out int recorded) && recorded == solver) return;
            _applier ??= new OwnershipApplier(_localNodeId,
                Fdp.Toolkit.Replication.Attributes.AttributeInterpreterProvider.GetDescriptorMap(repo));
            _applier.Apply(repo, parentGhost,
                Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey((long)EDescriptorType.dtEqsResult, key.ChildIndex), solver);
            _recordedSolver[key] = solver;
        }

        // The carrier ghost for (parent, childIndex) as it exists in the WORLD — never an ECB placeholder.
        private bool TryFindCarrier(ISimulationView view, Entity parentGhost, int childIndex, out Entity carrier)
        {
            if (_childGhostCache.TryGetValue((ParentKey(parentGhost), childIndex), out carrier)
                && IsCarrierOf(view, carrier, parentGhost, childIndex))
                return true;

            foreach (var e in view.Query().With<PartMetadata>().With<EqsSensor>().Build())
            {
                if (!IsCarrierOf(view, e, parentGhost, childIndex)) continue;
                carrier = e;
                _childGhostCache[(ParentKey(parentGhost), childIndex)] = e;
                return true;
            }

            carrier = Entity.Null;
            return false;
        }

        private long ParentKey(Entity parentGhost)
            => _entityMap.TryGetNetworkId(parentGhost, out long net) ? net : 0L;

        private static bool IsCarrierOf(ISimulationView view, Entity e, Entity parentGhost, int childIndex)
        {
            if (e.IsNull || e.Index < 0 || !view.IsAlive(e) || !view.HasComponent<PartMetadata>(e)) return false;
            var meta = view.GetComponentRO<PartMetadata>(e);
            return meta.ParentEntity == parentGhost && meta.InstanceId == childIndex;
        }

        private static int NamedSlotMask(in EqsSensorConfigTopic d)
            => (d.ContextSlot0NetworkId != 0 ? 1 : 0)
             | (d.ContextSlot1NetworkId != 0 ? 2 : 0)
             | (d.ContextSlot2NetworkId != 0 ? 4 : 0);

        private static int ResolvedSlotMask(in EqsSensor s)
            => (s.ContextSlot0.IsNull ? 0 : 1)
             | (s.ContextSlot1.IsNull ? 0 : 2)
             | (s.ContextSlot2.IsNull ? 0 : 4);

        /// <summary>Samples waiting for their parent or a context-slot entity (test hook).</summary>
        internal int PendingCount => _pending.Count;

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view) { }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }

        // Resolves a wire network ID to the local Muscle-side Entity. Returns Entity.Null if
        // networkId == 0 or the entity is not yet in the ghost map.
        internal Entity ResolveSlot(long networkId)
        {
            if (networkId == 0L) return Entity.Null;
            return _entityMap.TryGetEntity(networkId, out Entity e) ? e : Entity.Null;
        }

        // Builds an EqsSensor struct from a received DDS topic sample.
        private EqsSensor BuildSensor(EqsSensorConfigTopic data) => new EqsSensor
        {
            BlueprintId         = data.BlueprintId,
            Epoch               = data.Epoch,
            SearchRadius        = data.SearchRadius,
            FactionFilter       = data.FactionFilter,
            ThreatThreshold     = data.ThreatThreshold,
            PublishPolicy       = data.PublishPolicy,
            Priority            = data.Priority,
            ScoreDeltaThreshold = data.ScoreDeltaThreshold,
            ContextSlot0        = ResolveSlot(data.ContextSlot0NetworkId),
            ContextSlot1        = ResolveSlot(data.ContextSlot1NetworkId),
            ContextSlot2        = ResolveSlot(data.ContextSlot2NetworkId),
            Suspended           = data.Suspended,
        };
    }
}

