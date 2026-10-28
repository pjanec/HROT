using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Perception.Look;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ <c>CE-3252</c> (LD-1 / LD-2, LD-E; docs/designs/look-direction/DESIGN_Look_Direction_And_FOV.md §3.2a) — publishes a person's
    /// <see cref="LookState"/> from the node owning its <c>dtLookState</c> (the Muscle, which turns the head), so vision on a Perception
    /// node, the map's cone and HTTP read the same eyes everywhere. On change only: a head turn under <see cref="YawEpsilon"/> or a target
    /// moved under <see cref="TargetEpsilon"/> is not news. The <c>dtHitMarks</c> shape.
    /// </summary>
    public sealed class EntityLookStateEgressTranslator : IDescriptorTranslator
    {
        /// <summary>The head turn below which a change is not sent (1°).</summary>
        internal const float YawEpsilon = MathF.PI / 180f;
        /// <summary>The target move below which a change is not sent (m).</summary>
        internal const float TargetEpsilon = 0.25f;

        private readonly DdsWriter<EntityLookState>? _writer;
        private readonly Dictionary<long, EntityLookState> _last = new();
        private int _worldEpoch;

        private static readonly int[] s_targetIds = { GlobalComponentIds.LookState };

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityLookState>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtLookState;
        public IReadOnlyList<int> TargetComponentIds => s_targetIds;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityLookStateEgressTranslator(DdsParticipant? participant)
            => _writer = participant is not null ? new DdsWriter<EntityLookState>(participant) : null;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (WorldEpoch.Moved(view, ref _worldEpoch))
            {
                foreach (var id in _last.Keys) _writer?.DisposeInstance(new EntityLookState { EntityId = id });
                _last.Clear();
            }
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<LookState>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var unit in view.Query().With<LookState>().DefaultRoutePublishable().Build())
            {
                if (!view.HasAuthority(unit, packedKey)) continue;
                long netId = view.GetComponentRO<NetworkIdentity>(unit).Value;
                var sample = ToSample(netId, view.GetComponentRO<LookState>(unit));
                if (_last.TryGetValue(netId, out var prev) && Same(prev, sample)) continue;
                _last[netId] = sample;
                _writer?.Write(sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityLookState ToSample(long netId, in LookState l) => new()
        {
            EntityId = netId, TargetX = l.Target.X, TargetY = l.Target.Y, TargetZ = l.Target.Z, HasTarget = l.HasTarget,
            Source = (byte)l.Source, HeadYaw = l.HeadYaw,
        };

        internal static bool Same(in EntityLookState a, in EntityLookState b)
            => a.HasTarget == b.HasTarget && a.Source == b.Source && MathF.Abs(a.HeadYaw - b.HeadYaw) < YawEpsilon
               && Vector3.DistanceSquared(new Vector3(a.TargetX, a.TargetY, a.TargetZ), new Vector3(b.TargetX, b.TargetY, b.TargetZ))
                  < TargetEpsilon * TargetEpsilon;

        public void Dispose(long networkEntityId) => _last.Remove(networkEntityId);

        /// <summary>⭐ CE-4079 ② — the grantee confirmed (F7): let go of the instance WITHOUT disposing it, and forget the cache so a reclaim republishes.</summary>
        public void ReleaseInstance(long networkEntityId, long instanceId)
        {
            if (_last.Remove(networkEntityId)) _writer?.UnregisterInstance(new EntityLookState { EntityId = networkEntityId });
        }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>⭐ <c>CE-3252</c> — applies a person's published <see cref="LookState"/> on this node (vision, the map's cone, HTTP read
    /// it). An unknown unit is dropped; the recorded owner never takes its own sample back.</summary>
    public sealed class EntityLookStateIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtLookState;
        private readonly DdsReader<EntityLookState>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityLookState>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityLookStateIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityLookState>(participant) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        /// <inheritdoc />
        public int DiscardIngress() => _reader.DiscardAll();   // ⭐ CE-3329 W5

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

        /// <summary>Applies one sample (the test seam).</summary>
        internal void Apply(in EntityLookState d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.EntityId, out var unit) || !view.IsAlive(unit)) { DroppedSampleCount++; return; }
            if (view is EntityRepository repo
                && AuthorityExtensions.IsRecordedOwner(repo, unit, OwnershipExtensions.PackKey(OrdinalValue, 0))) return;
            cmd.AddComponent(unit, new LookState
            {
                Target = new Vector3(d.TargetX, d.TargetY, d.TargetZ), HasTarget = d.HasTarget, Source = (LookSource)d.Source,
                HeadYaw = d.HeadYaw,
            });
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
