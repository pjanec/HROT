using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Spatial.Eqs.Topics;
using Hrot.NED.Descriptors;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>
    /// Muscle-side ingress translator: receives <see cref="EqsSensorConfigTopic"/> samples
    /// and applies the <c>EqsSensor</c> component to the corresponding ghost entity so the
    /// solver picks it up on the next tick.
    /// On <c>NOT_ALIVE_DISPOSED</c>, removes <c>EqsSensor</c> from the ghost entity,
    /// signalling the solver to drop the query.
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

        public EqsSensorConfigIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            if (entityMap == null) throw new ArgumentNullException(nameof(entityMap));
            _entityMap = entityMap;
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
                long parentNetId;
                int  localChildIndex;
                if (sample.IsValid)
                {
                    parentNetId     = sample.Data.ParentNetworkId;
                    localChildIndex = sample.Data.LocalChildIndex;
                    ReceivedSampleCount++;
                }
                else
                {
                    // For NOT_ALIVE samples the managed .Data property throws.
                    // Read key fields directly from the native serialised buffer.
                    var keyData     = DdsTypeSupport.FromNative<EqsSensorConfigTopic>(sample.NativePtr);
                    parentNetId     = keyData.ParentNetworkId;
                    localChildIndex = keyData.LocalChildIndex;
                }

                if (localChildIndex == 0)
                {
                    // Legacy single-sensor path: sensor lives directly on the parent ghost entity.
                    if (sample.IsValid)
                    {
                        _pending[(parentNetId, 0)] = new Pending { Data = sample.Data };
                    }
                    else if (sample.Info.InstanceState == DdsInstanceState.NotAliveDisposed)
                    {
                        _pending.Remove((parentNetId, 0));
                        if (!_entityMap.TryGetEntity(parentNetId, out var parentGhost)) continue;
                        cmd.RemoveComponent<EqsSensor>(parentGhost);
                        _childGhostCache.Remove((parentNetId, 0));
                    }
                }
                else
                {
                    // Child-entity sensor path: carrier ghost is spawned/reused from cache.
                    var cacheKey = (parentNetId, localChildIndex);

                    if (sample.IsValid)
                    {
                        _pending[cacheKey] = new Pending { Data = sample.Data };
                    }
                    else if (sample.Info.InstanceState == DdsInstanceState.NotAliveDisposed)
                    {
                        _pending.Remove(cacheKey);
                        _awaitingPlayback.Remove(cacheKey);
                        _childGhostCache.Remove(cacheKey);
                        if (_entityMap.TryGetEntity(parentNetId, out var deadParent)
                            && TryFindCarrier(view, deadParent, localChildIndex, out var dead))
                            cmd.DestroyEntity(dead);
                    }
                }
            }

            ApplyPending(cmd, view);
        }

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
                    if (!Apply(cmd, view, key, parentGhost, sensor)) continue;
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
                           Entity parentGhost, EqsSensor sensor)
        {
            if (key.ChildIndex == 0)
            {
                // Legacy single-sensor path: sensor lives directly on the parent ghost entity.
                cmd.SetComponent(parentGhost, sensor);
                _childGhostCache[key] = parentGhost;
                return true;
            }

            if (TryFindCarrier(view, parentGhost, key.ChildIndex, out var child))
            {
                // Existing carrier: update its parameters.
                _awaitingPlayback.Remove(key);
                cmd.SetComponent(child, sensor);
                return true;
            }

            if (_awaitingPlayback.Contains(key)) return false;

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
                DescriptorOrdinal = 0,
            });
            cmd.AddComponent(child, sensor);
            cmd.AddComponent(child, default(EqsCognitiveBuffer));
            _awaitingPlayback.Add(key);
            return true;
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
        };
    }
}

