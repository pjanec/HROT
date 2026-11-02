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
using Fdp.Toolkit.ScenarioRules;
using Hrot.Map.Common.Dds;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ rules S2-e (📄 docs/DESIGN_Scenario_Rules.md §00.9, SR-122) — publishes the exercise's <see cref="ExerciseResult"/> (phase, outcome per side) from the node
    /// owning its <c>dtExerciseResult</c> (Brain group), on change. Same shape as <see cref="EntityScenarioClockEgressTranslator"/>; the sample is small and
    /// changes twice per exercise, so every change goes at once.
    /// </summary>
    public sealed class EntityExerciseResultEgressTranslator : IDescriptorTranslator
    {
        private readonly IDdsWriter<EntityExerciseResult>? _writer;
        private readonly Dictionary<int, EntityExerciseResult> _last = new();
        private int _worldEpoch;
        private static readonly int[] s_targetIds = { GlobalComponentIds.ExerciseResult };

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityExerciseResult>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtExerciseResult;
        public IReadOnlyList<int> TargetComponentIds => s_targetIds;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityExerciseResultEgressTranslator(DdsParticipant? participant)
            : this(participant is not null ? new DdsWriterAdapter<EntityExerciseResult>(participant) : null) { }

        /// <summary>The test seam: a capturing writer instead of a live participant.</summary>
        internal EntityExerciseResultEgressTranslator(IDdsWriter<EntityExerciseResult>? writer) => _writer = writer;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (WorldEpoch.Moved(view, ref _worldEpoch))
            {
                foreach (var id in _last.Keys) _writer?.DisposeInstance(new EntityExerciseResult { EntityId = id });   // CE-4007: a wiped world leaves no dead run
                _last.Clear();
            }
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<ExerciseResult>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var env in view.Query().With<ExerciseResult>().DefaultRoutePublishable().Build())
            {
                if (!view.HasAuthority(env, packedKey)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(env).Value;
                var sample = ToSample(netId, view.GetComponentRO<ExerciseResult>(env));
                if (_last.TryGetValue(netId, out var prev) && Same(in prev, in sample)) continue;
                _last[netId] = sample;
                _writer?.Write(sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityExerciseResult ToSample(int netId, in ExerciseResult x) => new()
        {
            EntityId = netId, Phase = (byte)x.Phase, EndedAt = x.EndedAt, Reason = x.Reason.ToString(),
            FriendOutcome = (byte)x.Friend.Outcome, FriendPoints = x.Friend.Points, FriendScore = x.Friend.Score,
            HostileOutcome = (byte)x.Hostile.Outcome, HostilePoints = x.Hostile.Points, HostileScore = x.Hostile.Score,
            NeutralOutcome = (byte)x.Neutral.Outcome, NeutralPoints = x.Neutral.Points, NeutralScore = x.Neutral.Score,
        };

        internal static bool Same(in EntityExerciseResult a, in EntityExerciseResult b)
            => a.Phase == b.Phase && a.EndedAt == b.EndedAt && a.Reason == b.Reason
               && a.FriendOutcome == b.FriendOutcome && a.FriendPoints == b.FriendPoints && a.FriendScore == b.FriendScore
               && a.HostileOutcome == b.HostileOutcome && a.HostilePoints == b.HostilePoints && a.HostileScore == b.HostileScore
               && a.NeutralOutcome == b.NeutralOutcome && a.NeutralPoints == b.NeutralPoints && a.NeutralScore == b.NeutralScore;

        public void Dispose(int networkEntityId) => _last.Remove(networkEntityId);

        /// <summary>⭐ CE-4079 ② — the grantee confirmed (F7): let go of the instance WITHOUT disposing it, and forget the cache so a reclaim republishes.</summary>
        public void ReleaseInstance(int networkEntityId, long instanceId)
        {
            if (_last.Remove(networkEntityId)) _writer?.UnregisterInstance(new EntityExerciseResult { EntityId = networkEntityId });
        }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// ⭐ rules S2-e — applies the published exercise result on every node that does not own it, and raises the local <see cref="ExerciseEndedEvent"/> ONCE when the
    /// phase becomes Ended (the owner raises its own — <c>EndExercise</c>). A sample for an entity this node does not know yet is HELD (the newest per entity)
    /// until it does. The recorded owner never takes its own sample back (S8 / F-5). Same shape as <see cref="EntityObjectiveStatusIngressTranslator"/>.
    /// </summary>
    public sealed class EntityExerciseResultIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtExerciseResult;
        private readonly DdsReader<EntityExerciseResult>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly Dictionary<int, EntityExerciseResult> _pending = new();
        private readonly List<int> _done = new();
        private readonly HashSet<int> _endedRaised = new();

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityExerciseResult>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public int PendingCount => _pending.Count;
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityExerciseResultIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityExerciseResult>(participant) : null;
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
        internal void Receive(in EntityExerciseResult d) { ReceivedSampleCount++; _pending[d.EntityId] = d; }

        internal void Flush(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_pending.Count == 0) return;
            foreach (var (id, d) in _pending)
                if (TryApply(in d, cmd, view)) _done.Add(id);
            foreach (var id in _done) _pending.Remove(id);
            _done.Clear();
        }

        private bool TryApply(in EntityExerciseResult d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.EntityId, out var env) || !view.IsAlive(env)) return false;
            if (view is EntityRepository repo)
            {
                if (!repo.IsComponentTypeRegistered<ExerciseResult>()) return true;   // not composed here: drop
                if (AuthorityExtensions.IsRecordedOwner(repo, env, OwnershipExtensions.PackKey(OrdinalValue, 0))) return true;   // ours: drop
                if ((ExercisePhase)d.Phase == ExercisePhase.Ended)
                {
                    if (_endedRaised.Add(d.EntityId))   // ⭐ ONCE: the phase became Ended
                    {
                        if (!repo.Bus.IsRegistered<ExerciseEndedEvent>()) repo.Bus.Register<ExerciseEndedEvent>();
                        repo.Bus.Publish(new ExerciseEndedEvent { EndedAt = d.EndedAt });
                    }
                }
                else _endedRaised.Remove(d.EntityId);   // a new run on the same entity
            }
            cmd.AddComponent(env, FromSample(in d));
            return true;
        }

        /// <summary>The result a sample carries (the test seam).</summary>
        internal static ExerciseResult FromSample(in EntityExerciseResult d) => new()
        {
            Phase = (ExercisePhase)d.Phase, EndedAt = d.EndedAt, Reason = new FixedString64(d.Reason ?? string.Empty),
            Friend = new SideResult { Outcome = (SideOutcome)d.FriendOutcome, Points = d.FriendPoints, Score = d.FriendScore },
            Hostile = new SideResult { Outcome = (SideOutcome)d.HostileOutcome, Points = d.HostilePoints, Score = d.HostileScore },
            Neutral = new SideResult { Outcome = (SideOutcome)d.NeutralOutcome, Points = d.NeutralPoints, Score = d.NeutralScore },
        };

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) { _pending.Remove(networkEntityId); _endedRaised.Remove(networkEntityId); }
    }
}
