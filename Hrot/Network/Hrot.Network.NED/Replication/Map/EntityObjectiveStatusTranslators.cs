using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using Hrot.Map.Common.Dds;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.ScenarioRules;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ rules S1-a (📄 docs/DESIGN_Scenario_Rules.md §00.8, SR-120) — publishes an objective entity's <see cref="ObjectiveStatus"/> (with the
    /// subject and parent of its <see cref="ObjectiveLinks"/> as network ids) from the node owning its <c>dtObjectiveStatus</c> (Brain group),
    /// on change. ⭐ Throttle (SR-120): a change in State / Closed / Owner / Title / Kind / Group / Pass / StepCur / StepCount / Required / Scored / Weight / VisibleSides / FireCount / TriggerState /
    /// Subject / Parent / DependsOn / ActsOn / HoldFor / Delay / Cooldown / Deadline / ExpectedAt (V-1/V-2) goes AT ONCE; a change only in progress / value / score / points / reason / times goes at most every
    /// <see cref="ProgressRefreshSeconds"/> of sim time (<c>view.Time</c>; a clock that went back reopens the window). A held change is still compared against the
    /// last PUBLISHED sample, so the newest value goes out once the window has passed. Same shape as <see cref="EntityScenarioClockEgressTranslator"/>.
    /// </summary>
    public sealed class EntityObjectiveStatusEgressTranslator : IDescriptorTranslator
    {
        /// <summary>A progress-only change is sent at most this often (sim s): two a second (SR-120).</summary>
        public const double ProgressRefreshSeconds = 0.5;

        private readonly IDdsWriter<EntityObjectiveStatus>? _writer;
        private readonly Dictionary<int, (EntityObjectiveStatus Sample, double SentAt)> _last = new();
        private int _worldEpoch;

        private static readonly int[] s_targetIds = { GlobalComponentIds.ObjectiveStatus, GlobalComponentIds.ObjectiveLinks };

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityObjectiveStatus>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtObjectiveStatus;
        public IReadOnlyList<int> TargetComponentIds => s_targetIds;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityObjectiveStatusEgressTranslator(DdsParticipant? participant)
            : this(participant is not null ? new DdsWriterAdapter<EntityObjectiveStatus>(participant) : null) { }

        /// <summary>The test seam: a capturing writer instead of a live participant.</summary>
        internal EntityObjectiveStatusEgressTranslator(IDdsWriter<EntityObjectiveStatus>? writer) => _writer = writer;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (WorldEpoch.Moved(view, ref _worldEpoch))
            {
                // ⭐⭐ CE-4007 (Q91 §4-M) — a wiped world: DELETE every instance this node wrote, so a late joiner never receives a dead run (TransientLocal).
                foreach (var id in _last.Keys) _writer?.DisposeInstance(new EntityObjectiveStatus { EntityId = id });
                _last.Clear();
            }
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<ObjectiveStatus>()) return;
            bool links = view is not EntityRepository lr || lr.IsComponentTypeRegistered<ObjectiveLinks>();
            double now = view.Time;   // sim seconds
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var env in view.Query().With<ObjectiveStatus>().DefaultRoutePublishable().Build())
            {
                if (!view.HasAuthority(env, packedKey)) continue;
                int netId = view.GetComponentRO<NetworkIdentity>(env).Value;
                var l = links && view.HasComponent<ObjectiveLinks>(env) ? view.GetComponentRO<ObjectiveLinks>(env) : default;
                var sample = ToSample(netId, view.GetComponentRO<ObjectiveStatus>(env), in l);
                if (_last.TryGetValue(netId, out var prev))
                {
                    if (SameWire(in prev.Sample, in sample)) continue;
                    if (SameDisplayAndState(in prev.Sample, in sample) && now >= prev.SentAt && now - prev.SentAt < ProgressRefreshSeconds) continue;   // progress only, too soon
                }
                _last[netId] = (sample, now);
                _writer?.Write(sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityObjectiveStatus ToSample(int netId, in ObjectiveStatus s, in ObjectiveLinks l) => new()
        {
            EntityId = netId, Role = (byte)s.Role, State = (byte)s.State, Closed = s.Closed, Owner = (byte)s.Owner,
            ProgressCur = s.ProgressCur, ProgressNeed = s.ProgressNeed, Value = s.Value, Target = s.Target, Score = s.Score, Points = s.Points,
            ChangedAt = s.ChangedAt, Reason = s.Reason.ToString(), Subject = l.Subject.NetworkId, Parent = l.Parent.NetworkId,
            FireCount = s.FireCount, LastFired = s.LastFired, TriggerState = (byte)s.TriggerState, Title = s.Title.ToString(), Kind = s.Kind.ToString(),
            Required = s.Required, Scored = s.Scored, Weight = s.Weight, VisibleSides = s.VisibleSides,
            DependsOn = ToList(s.DependsOnCount, in s.DependsOn), ActsOn = ToList(s.ActsOnCount, in s.ActsOn),
            HoldFor = s.HoldFor, Delay = s.Delay, Cooldown = s.Cooldown, Deadline = s.Deadline, ExpectedAt = s.ExpectedAt,
            Group = s.Group.ToString(), Pass = s.Pass, StepCur = s.StepCur, StepCount = s.StepCount,
        };

        private static List<int> ToList(byte count, in NetIds8 ids)
        {
            if (count == 0) return null!;   // none: no list on the wire
            var l = new List<int>(count);
            for (int i = 0; i < count && i < 8; i++) l.Add(ids[i]);
            return l;
        }

        private static bool SameIds(List<int>? a, List<int>? b)
        {
            int na = a?.Count ?? 0, nb = b?.Count ?? 0;
            if (na != nb) return false;
            for (int i = 0; i < na; i++) if (a![i] != b![i]) return false;
            return true;
        }

        /// <summary>The fields whose change goes at once (everything but progress / value / score / points / reason / times).</summary>
        internal static bool SameDisplayAndState(in EntityObjectiveStatus a, in EntityObjectiveStatus b)
            => a.Role == b.Role && a.State == b.State && a.Closed == b.Closed && a.Owner == b.Owner
               && a.Title == b.Title && a.Kind == b.Kind && a.Group == b.Group && a.Pass == b.Pass && a.StepCur == b.StepCur && a.StepCount == b.StepCount && a.Required == b.Required && a.Scored == b.Scored
               && a.Weight == b.Weight && a.VisibleSides == b.VisibleSides && a.FireCount == b.FireCount && a.TriggerState == b.TriggerState
               && a.Subject == b.Subject && a.Parent == b.Parent
               && SameIds(a.DependsOn, b.DependsOn) && SameIds(a.ActsOn, b.ActsOn)   // ⭐ V-1/V-2: the links and the timing are display / state: at once
               && a.HoldFor == b.HoldFor && a.Delay == b.Delay && a.Cooldown == b.Cooldown && a.Deadline == b.Deadline && a.ExpectedAt == b.ExpectedAt;

        /// <summary>Every field the same.</summary>
        internal static bool SameWire(in EntityObjectiveStatus a, in EntityObjectiveStatus b)
            => SameDisplayAndState(in a, in b)
               && a.ProgressCur == b.ProgressCur && a.ProgressNeed == b.ProgressNeed && a.Value == b.Value && a.Target == b.Target
               && a.Score == b.Score && a.Points == b.Points && a.ChangedAt == b.ChangedAt && a.Reason == b.Reason && a.LastFired == b.LastFired;

        public void Dispose(int networkEntityId) => _last.Remove(networkEntityId);

        /// <summary>⭐ CE-4079 ② — the grantee confirmed (F7): let go of the instance WITHOUT disposing it, and forget the cache so a reclaim republishes.</summary>
        public void ReleaseInstance(int networkEntityId, long instanceId)
        {
            if (_last.Remove(networkEntityId)) _writer?.UnregisterInstance(new EntityObjectiveStatus { EntityId = networkEntityId });
        }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// ⭐ rules S1-a — applies a published objective status (and its links) on every node that does not own it. A sample for an entity this node
    /// does not know yet is HELD (the newest per entity) until it does — it is sent once per change (TransientLocal). The recorded owner never
    /// takes its own sample back (S8 / F-5). Same shape as <see cref="EntityScenarioClockIngressTranslator"/>.
    /// </summary>
    public sealed class EntityObjectiveStatusIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const long OrdinalValue = (long)EDescriptorType.dtObjectiveStatus;
        private readonly DdsReader<EntityObjectiveStatus>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly Dictionary<int, EntityObjectiveStatus> _pending = new();
        private readonly List<int> _done = new();

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityObjectiveStatus>();
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        /// <summary>Samples waiting for an entity this node does not know yet.</summary>
        public int PendingCount => _pending.Count;
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityObjectiveStatusIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityObjectiveStatus>(participant) : null;
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
        internal void Receive(in EntityObjectiveStatus d) { ReceivedSampleCount++; _pending[d.EntityId] = d; }

        /// <summary>Applies every held sample whose entity is known.</summary>
        internal void Flush(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_pending.Count == 0) return;
            foreach (var (id, d) in _pending)
                if (TryApply(in d, cmd, view)) _done.Add(id);
            foreach (var id in _done) _pending.Remove(id);
            _done.Clear();
        }

        private bool TryApply(in EntityObjectiveStatus d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.EntityId, out var env) || !view.IsAlive(env)) return false;
            if (view is EntityRepository repo)
            {
                if (!repo.IsComponentTypeRegistered<ObjectiveStatus>() || !repo.IsComponentTypeRegistered<ObjectiveLinks>()) return true;   // not composed here: drop
                if (AuthorityExtensions.IsRecordedOwner(repo, env, OwnershipExtensions.PackKey(OrdinalValue, 0))) return true;   // ours: drop
            }
            cmd.AddComponent(env, FromSample(in d));
            cmd.AddComponent(env, LinksFromSample(in d));
            return true;
        }

        /// <summary>The status a sample carries (the test seam).</summary>
        internal static ObjectiveStatus FromSample(in EntityObjectiveStatus d) => new()
        {
            Role = (ObjectiveRole)d.Role, State = (ObjectiveState)d.State, Closed = d.Closed, Owner = (ForceId)d.Owner,
            ProgressCur = d.ProgressCur, ProgressNeed = d.ProgressNeed, Value = d.Value, Target = d.Target, Score = d.Score, Points = d.Points,
            ChangedAt = d.ChangedAt, Reason = new FixedString64(d.Reason ?? string.Empty), FireCount = d.FireCount, LastFired = d.LastFired, TriggerState = (TriggerState)d.TriggerState,
            Title = new FixedString64(d.Title ?? string.Empty), Kind = new FixedString32(d.Kind ?? string.Empty),
            Required = d.Required, Scored = d.Scored, Weight = d.Weight, VisibleSides = d.VisibleSides,
            DependsOnCount = (byte)Math.Min(d.DependsOn?.Count ?? 0, 8), DependsOn = ToIds(d.DependsOn),
            ActsOnCount = (byte)Math.Min(d.ActsOn?.Count ?? 0, 8), ActsOn = ToIds(d.ActsOn),
            HoldFor = d.HoldFor, Delay = d.Delay, Cooldown = d.Cooldown, Deadline = d.Deadline, ExpectedAt = d.ExpectedAt,
            Group = new FixedString32(d.Group ?? string.Empty), Pass = d.Pass, StepCur = d.StepCur, StepCount = d.StepCount,
        };

        private static NetIds8 ToIds(List<int>? l)
        {
            var ids = default(NetIds8);
            if (l != null) for (int i = 0; i < l.Count && i < 8; i++) ids[i] = l[i];
            return ids;
        }

        /// <summary>The links a sample carries (the test seam): the net ids as they are — an <c>EntityRef</c> IS a network id.</summary>
        internal static ObjectiveLinks LinksFromSample(in EntityObjectiveStatus d)
            => new() { Subject = new EntityRef(d.Subject), Parent = new EntityRef(d.Parent) };

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) => _pending.Remove(networkEntityId);
    }
}
