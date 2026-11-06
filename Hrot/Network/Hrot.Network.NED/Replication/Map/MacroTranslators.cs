using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Scenario;
using Fdp.ModuleHost.Abstractions;

// ⭐ MACROS S3a ③ (docs/DESIGN_Scenario_Macros.md §14 SR-170 / SR-175 / SR-178, R-134) — the wire twins of the three macro components. Each pair
//   is the <c>dtEntityGuid</c> shape (EntityStableGuidTranslators.cs): no ownership group ⇒ the entity's creator keeps the descriptor and publishes
//   it; every other node holds a sample that arrives BEFORE its entity (DDS orders nothing across topics) and applies it when the entity is here —
//   ⛔ never dropped. ⚠ Not in a CreateEntityRequest union arm: an instance is created by an ordinary request carrying the component in
//   InitialComponents. The wire structs carry wire types only (strings, int network ids, a GUID as two longs) — ⛔ no engine type in a DDS struct.

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>⭐ <c>dtMacroInstance</c> — publishes a <see cref="MacroInstance"/> from the node that owns the descriptor, on change (the record's equality).</summary>
    public sealed class EntityMacroInstanceEgressTranslator : IDescriptorTranslator
    {
        // ⭐⭐ CE-4007 (Cluster Load Phase §8.3) — what this writer published: the send-on-change cache AND the instances it deletes on a
        //   destruction, a wipe or (as an unregister) a hand-over.
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityMacroInstance> _instances;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityMacroInstance>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtMacroInstance;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityMacroInstanceEgressTranslator(DdsParticipant? participant)
            => _instances = new(participant is not null ? new DdsWriter<EntityMacroInstance>(participant) : null, static key => new EntityMacroInstance { EntityId = (int)key });

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);   // a wiped world: every instance this writer published is deleted (CE-4007, §8.3)
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var e in view.Query().WithManaged<MacroInstance>().DefaultRoutePublishable().WithLifecycle(EntityLifecycle.AllButTearDown).Build())
            {
                if (!view.HasAuthority(e, packedKey)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(e).Value;
                var mi = view.GetManagedComponentRO<MacroInstance>(e);
                // (field-wise, not a whole-sample compare: that would box the struct each frame)
                if (_instances.TryGetLast(netId, out var prev) && prev.Macro == mi.Macro && prev.ArgsJson == mi.ArgsJson) continue;   // unchanged
                _instances.Write(netId, ToSample(netId, mi));
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        public static EntityMacroInstance ToSample(int netId, MacroInstance mi)
            => new() { EntityId = netId, Macro = mi.Macro, ArgsJson = mi.ArgsJson };

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);   // ⭐ CE-4007 — the delete goes on the wire

        /// <summary>⭐ CE-4079 ② — the grantee confirmed (F7): let go of the instance WITHOUT disposing it, and forget the cache so a reclaim republishes.</summary>
        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }

    /// <summary>⭐ <c>dtMacroExpansionStatus</c> — publishes a <see cref="MacroExpansionStatus"/> from the node that owns the descriptor, on change.</summary>
    public sealed class EntityMacroExpansionStatusEgressTranslator : IDescriptorTranslator
    {
        // ⭐⭐ CE-4007 (§8.3) — what this writer published (the instances it deletes on a destruction, a wipe or a hand-over). `_last` stays as the
        //   change test: it compares the component (a FixedString128), where the wire sample holds a string that would allocate each frame.
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityMacroExpansionStatus> _instances;
        private readonly Dictionary<int, (MacroExpansionState State, FixedString128 Reason)> _last = new();

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityMacroExpansionStatus>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtMacroExpansionStatus;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityMacroExpansionStatusEgressTranslator(DdsParticipant? participant)
            => _instances = new(participant is not null ? new DdsWriter<EntityMacroExpansionStatus>(participant) : null, static key => new EntityMacroExpansionStatus { EntityId = (int)key });

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (_instances.OnFrame(view)) _last.Clear();   // a wiped world: every instance this writer published is deleted (CE-4007, §8.3)
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<MacroExpansionStatus>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var e in view.Query().With<MacroExpansionStatus>().DefaultRoutePublishable().WithLifecycle(EntityLifecycle.AllButTearDown).Build())
            {
                if (!view.HasAuthority(e, packedKey)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(e).Value;
                var st = view.GetComponentRO<MacroExpansionStatus>(e);
                if (_last.TryGetValue(netId, out var prev) && prev.State == st.State && prev.Reason.Equals(st.Reason)) continue;
                _last[netId] = (st.State, st.Reason);
                _instances.Write(netId, ToSample(netId, in st));
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        public static EntityMacroExpansionStatus ToSample(int netId, in MacroExpansionStatus s)
            => new() { EntityId = netId, State = (byte)s.State, Reason = s.Reason.ToString() };

        public void Dispose(int networkEntityId) { _last.Remove(networkEntityId); _instances.Dispose(networkEntityId); }   // ⭐ CE-4007 — the delete goes on the wire

        public void ReleaseInstance(int networkEntityId, long instanceId) { _last.Remove(networkEntityId); _instances.Release(networkEntityId); }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }

    /// <summary>⭐ <c>dtMacroOrigin</c> — publishes a <see cref="MacroOrigin"/> from the node that owns the descriptor, ONCE (it never changes after the expander stamps it).</summary>
    public sealed class EntityMacroOriginEgressTranslator : IDescriptorTranslator
    {
        // ⭐ CE-2407 (DESIGN_Egress_Change_Detection §5, E1) — skips the entities none of these inputs changed for; NetworkIdentity is implied
        private readonly Fdp.Toolkit.Replication.Utilities.EgressVisitGate _gate = new(typeof(MacroOrigin));

        // ⭐⭐ CE-4007 (Cluster Load Phase §8.3) — what this writer published: the exactly-once record AND the instances it deletes on a
        //   destruction, a wipe or (as an unregister) a hand-over.
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityMacroOrigin> _instances;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityMacroOrigin>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtMacroOrigin;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityMacroOriginEgressTranslator(DdsParticipant? participant)
            => _instances = new(participant is not null ? new DdsWriter<EntityMacroOrigin>(participant) : null, static key => new EntityMacroOrigin { EntityId = (int)key });

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            _instances.OnFrame(view);   // a wiped world: every instance this writer published is deleted (CE-4007, §8.3)
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<MacroOrigin>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            _gate.Begin(view);
            foreach (var e in view.Query().With<MacroOrigin>().DefaultRoutePublishable().WithLifecycle(EntityLifecycle.AllButTearDown).Build())
            {
                if (!_gate.Visit(e)) continue;   // ⭐ CE-2407 E1 — nothing this translator reads has changed since its last scan
                if (!view.HasAuthority(e, packedKey)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(e).Value;
                if (_instances.TryGetLast(netId, out _)) continue;   // exactly once per entity
                _instances.Write(netId, ToSample(netId, in view.GetComponentRO<MacroOrigin>(e)));
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam): the instance's network id and the macro-local GUID as two longs, as <c>EntityStableGuid</c> carries one.</summary>
        public static EntityMacroOrigin ToSample(int netId, in MacroOrigin o)
        {
            Span<byte> b = stackalloc byte[16];
            o.Local.TryWriteBytes(b);
            return new EntityMacroOrigin
            {
                EntityId = netId,
                Instance = o.Instance.NetworkId,
                LocalLo = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(b),
                LocalHi = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(b.Slice(8)),
            };
        }

        public void Dispose(int networkEntityId) => _instances.Dispose(networkEntityId);   // ⭐ CE-4007 — the delete goes on the wire

        public void ReleaseInstance(int networkEntityId, long instanceId) => _instances.Release(networkEntityId);

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }

    /// <summary>⭐ <c>dtRefKey</c> — publishes an entity's <see cref="RefKey"/> (its authored name) from the node that owns the descriptor, ONCE per non-empty value; for every keyed entity (a loaded one's key is the same everywhere).</summary>
    public sealed class EntityRefKeyEgressTranslator : IDescriptorTranslator
    {
        // ⭐⭐ CE-4007 (§8.3) — what this writer published (see above). `_sent` stays as the change test: it compares the FixedString32, where the
        //   wire sample holds a string that would allocate each frame.
        private readonly Fdp.Network.Cyclone.Translators.PublishedInstances<EntityRefKey> _instances;
        private readonly Dictionary<int, FixedString32> _sent = new();

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityRefKey>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtRefKey;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityRefKeyEgressTranslator(DdsParticipant? participant)
            => _instances = new(participant is not null ? new DdsWriter<EntityRefKey>(participant) : null, static key => new EntityRefKey { EntityId = (int)key });

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (_instances.OnFrame(view)) _sent.Clear();   // a wiped world: every instance this writer published is deleted (CE-4007, §8.3)
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<RefKey>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var e in view.Query().With<RefKey>().DefaultRoutePublishable().WithLifecycle(EntityLifecycle.AllButTearDown).Build())
            {
                if (!view.HasAuthority(e, packedKey)) continue;
                var key = view.GetComponentRO<RefKey>(e).Value;
                if (key.Length == 0) continue;                                                 // an empty key names nothing
                int netId = view.GetComponentRO<NetworkIdentity>(e).Value;
                if (_sent.TryGetValue(netId, out var prev) && prev.Equals(key)) continue;      // a key is set once; a re-keyed entity republishes
                _sent[netId] = key;
                _instances.Write(netId, ToSample(netId, in key));
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        public static EntityRefKey ToSample(int netId, in FixedString32 key) => new() { EntityId = netId, Value = key.ToString() };

        public void Dispose(int networkEntityId) { _sent.Remove(networkEntityId); _instances.Dispose(networkEntityId); }   // ⭐ CE-4007 — the delete goes on the wire

        public void ReleaseInstance(int networkEntityId, long instanceId) { _sent.Remove(networkEntityId); _instances.Release(networkEntityId); }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>⭐ <c>dtMacroInstance</c> — applies a published <see cref="MacroInstance"/> (a NEW immutable record each time); held until its entity is here; the recorded owner never takes its own sample back.</summary>
    public sealed class EntityMacroInstanceIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtMacroInstance;
        private readonly DdsReader<EntityMacroInstance>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly Dictionary<int, MacroInstance> _waiting = new();
        private readonly List<int> _applied = new();

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityMacroInstance>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        /// <summary>Samples still waiting for their entity (diagnostics and rails).</summary>
        public int WaitingCount => _waiting.Count;
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityMacroInstanceIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityMacroInstance>(participant) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        public int DiscardIngress() { _waiting.Clear(); return _reader.DiscardAll(); }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is not null)
            {
                using var loan = _reader.Take();
                foreach (var sample in loan)
                {
                    if (!sample.IsValid || sample.Info.InstanceState != DdsInstanceState.Alive) continue;
                    ReceivedSampleCount++;
                    _waiting[sample.Data.EntityId] = FromSample(sample.Data);
                }
            }
            if (_waiting.Count == 0) return;
            _applied.Clear();
            foreach (var (netId, mi) in _waiting)
                if (TryApply(netId, mi, cmd, view)) _applied.Add(netId);
            foreach (var id in _applied) _waiting.Remove(id);
        }

        /// <summary>Applies one sample (the test seam); false = its entity is not here yet.</summary>
        internal bool TryApply(int netId, MacroInstance mi, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(netId, out var e) || !view.IsAlive(e)) return false;
            if (view is EntityRepository repo && AuthorityExtensions.IsRecordedOwner(repo, e, OwnershipExtensions.PackKey(OrdinalValue, 0))) return true;
            cmd.SetManagedComponent(e, mi);
            return true;
        }

        public static MacroInstance FromSample(in EntityMacroInstance s)
            => new() { Macro = s.Macro ?? string.Empty, ArgsJson = string.IsNullOrEmpty(s.ArgsJson) ? "{}" : s.ArgsJson };

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) { _waiting.Remove(networkEntityId); }
    }

    /// <summary>⭐ <c>dtMacroExpansionStatus</c> — applies a published <see cref="MacroExpansionStatus"/> (add, or set when present); held until its entity is here.</summary>
    public sealed class EntityMacroExpansionStatusIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtMacroExpansionStatus;
        private readonly DdsReader<EntityMacroExpansionStatus>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly Dictionary<int, MacroExpansionStatus> _waiting = new();
        private readonly List<int> _applied = new();

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityMacroExpansionStatus>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public int WaitingCount => _waiting.Count;
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityMacroExpansionStatusIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityMacroExpansionStatus>(participant) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        public int DiscardIngress() { _waiting.Clear(); return _reader.DiscardAll(); }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is not null)
            {
                using var loan = _reader.Take();
                foreach (var sample in loan)
                {
                    if (!sample.IsValid || sample.Info.InstanceState != DdsInstanceState.Alive) continue;
                    ReceivedSampleCount++;
                    _waiting[sample.Data.EntityId] = FromSample(sample.Data);
                }
            }
            if (_waiting.Count == 0) return;
            _applied.Clear();
            foreach (var (netId, st) in _waiting)
                if (TryApply(netId, st, cmd, view)) _applied.Add(netId);
            foreach (var id in _applied) _waiting.Remove(id);
        }

        internal bool TryApply(int netId, MacroExpansionStatus st, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(netId, out var e) || !view.IsAlive(e)) return false;
            if (view is EntityRepository repo)
            {
                if (AuthorityExtensions.IsRecordedOwner(repo, e, OwnershipExtensions.PackKey(OrdinalValue, 0))) return true;
                if (!repo.IsComponentTypeRegistered<MacroExpansionStatus>()) return true;
            }
            if (view.HasComponent<MacroExpansionStatus>(e)) cmd.SetComponent(e, st); else cmd.AddComponent(e, st);
            return true;
        }

        public static MacroExpansionStatus FromSample(in EntityMacroExpansionStatus s)
            => new() { State = (MacroExpansionState)s.State, Reason = new FixedString128(s.Reason ?? string.Empty) };

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) { _waiting.Remove(networkEntityId); }
    }

    /// <summary>⭐ <c>dtMacroOrigin</c> — applies a published <see cref="MacroOrigin"/> once; held until its entity is here.</summary>
    public sealed class EntityMacroOriginIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtMacroOrigin;
        private readonly DdsReader<EntityMacroOrigin>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly Dictionary<int, MacroOrigin> _waiting = new();
        private readonly List<int> _applied = new();

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityMacroOrigin>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public int WaitingCount => _waiting.Count;
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityMacroOriginIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityMacroOrigin>(participant) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        public int DiscardIngress() { _waiting.Clear(); return _reader.DiscardAll(); }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is not null)
            {
                using var loan = _reader.Take();
                foreach (var sample in loan)
                {
                    if (!sample.IsValid || sample.Info.InstanceState != DdsInstanceState.Alive) continue;
                    ReceivedSampleCount++;
                    _waiting[sample.Data.EntityId] = FromSample(sample.Data);
                }
            }
            if (_waiting.Count == 0) return;
            _applied.Clear();
            foreach (var (netId, o) in _waiting)
                if (TryApply(netId, o, cmd, view)) _applied.Add(netId);
            foreach (var id in _applied) _waiting.Remove(id);
        }

        internal bool TryApply(int netId, MacroOrigin o, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(netId, out var e) || !view.IsAlive(e)) return false;
            if (view is EntityRepository repo)
            {
                if (AuthorityExtensions.IsRecordedOwner(repo, e, OwnershipExtensions.PackKey(OrdinalValue, 0))) return true;
                if (!repo.IsComponentTypeRegistered<MacroOrigin>()) return true;
            }
            if (!view.HasComponent<MacroOrigin>(e)) cmd.AddComponent(e, o);
            return true;
        }

        public static MacroOrigin FromSample(in EntityMacroOrigin s)
        {
            Span<byte> b = stackalloc byte[16];
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b, s.LocalLo);
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b.Slice(8), s.LocalHi);
            return new MacroOrigin { Instance = new EntityRef(s.Instance), Local = new Guid(b) };
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) { _waiting.Remove(networkEntityId); }
    }

    /// <summary>⭐ <c>dtRefKey</c> — applies a published <see cref="RefKey"/> to the local ghost (add, or set when present); held until its entity is here, so the live "@Key" scope finds a runtime-inserted entity on every node.</summary>
    public sealed class EntityRefKeyIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtRefKey;
        private readonly DdsReader<EntityRefKey>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly Dictionary<int, RefKey> _waiting = new();
        private readonly List<int> _applied = new();

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityRefKey>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public int WaitingCount => _waiting.Count;
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityRefKeyIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityRefKey>(participant) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        public int DiscardIngress() { _waiting.Clear(); return _reader.DiscardAll(); }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is not null)
            {
                using var loan = _reader.Take();
                foreach (var sample in loan)
                {
                    if (!sample.IsValid || sample.Info.InstanceState != DdsInstanceState.Alive) continue;
                    ReceivedSampleCount++;
                    if (string.IsNullOrEmpty(sample.Data.Value)) continue;
                    _waiting[sample.Data.EntityId] = FromSample(sample.Data);
                }
            }
            if (_waiting.Count == 0) return;
            _applied.Clear();
            foreach (var (netId, k) in _waiting)
                if (TryApply(netId, k, cmd, view)) _applied.Add(netId);
            foreach (var id in _applied) _waiting.Remove(id);
        }

        internal bool TryApply(int netId, RefKey k, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(netId, out var e) || !view.IsAlive(e)) return false;
            if (view is EntityRepository repo)
            {
                if (AuthorityExtensions.IsRecordedOwner(repo, e, OwnershipExtensions.PackKey(OrdinalValue, 0))) return true;
                if (!repo.IsComponentTypeRegistered<RefKey>()) return true;
            }
            if (view.HasComponent<RefKey>(e)) cmd.SetComponent(e, k); else cmd.AddComponent(e, k);
            return true;
        }

        /// <summary>The component form. ⚠ A key longer than 31 bytes cannot arrive from a conforming sender; <see cref="FixedString32"/> truncates rather than throws.</summary>
        public static RefKey FromSample(in EntityRefKey s) => new() { Value = new FixedString32(s.Value ?? string.Empty) };

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) { _waiting.Remove(networkEntityId); }
    }
}
