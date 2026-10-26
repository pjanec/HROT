using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Squad.Command;
using Fdp.Toolkit.Utility;

namespace Fdp.Toolkit.ReplayBrowser.Timeline
{
    /// <summary>
    /// ⭐ <c>CE-1302</c> (docs/DESIGN_Director_Timeline_Editor.md §9 TL-5) — reads ONE played frame and adds what happened in it. Called by
    /// <see cref="TimelineIndexBuilder"/> right after <c>PlaybackController.StepForward</c>, so the bus holds exactly that frame's events and
    /// the repository that frame's state. An extractor keeps its own frame-to-frame memory (the diffs).
    /// </summary>
    public interface ITimelineExtractor
    {
        void Frame(EntityRepository repo, FdpEventBus bus, int frame, TimelineEventSink sink);
    }

    /// <summary>⭐ <c>CE-1302</c> — the growing per-lane lists a build writes into; <see cref="Snapshot"/> copies them into an immutable index.</summary>
    public sealed class TimelineEventSink
    {
        private readonly List<TimelineEvent>[] _lanes = new List<TimelineEvent>[TimelineLanes.Count];

        public TimelineEventSink()
        {
            for (int i = 0; i < _lanes.Length; i++) _lanes[i] = new List<TimelineEvent>();
        }

        public void Add(in TimelineEvent e) => _lanes[(int)e.Lane].Add(e);

        public int Count(TimelineLane lane) => _lanes[(int)lane].Count;

        public TimelineIndex Snapshot(long[] frameTicks, int scannedFrames, int totalFrames)
        {
            var lanes = TimelineIndex.NewLanes();
            for (int i = 0; i < lanes.Length; i++) lanes[i] = _lanes[i].ToArray();
            return new TimelineIndex(lanes, frameTicks, scannedFrames, totalFrames);
        }

        /// <summary>The id an event names: the entity's NETWORK id, else <c>-(index + 1)</c> (a dead or local entity), 0 for none.</summary>
        public static long IdOf(EntityRepository repo, Entity e)
        {
            if (e.Equals(default(Entity)) || e.IsNull) return 0;
            if (repo.IsComponentTypeRegistered<NetworkIdentity>())
            {
                long net = NetworkIdResolver.RuntimeNetworkIdOf(repo, e);
                if (net > 0) return net;
            }
            return -(e.Index + 1L);
        }
    }

    /// <summary>
    /// ⭐ <c>CE-1302</c> Life lanes — a network id appearing (Spawned) or leaving (Destroyed), by a set diff of the networked entities each
    /// frame. X / Y are where it appeared or was last seen. ⚠ Only NETWORKED entities: rounds and other local helpers have no id and would
    /// drown the lane.
    /// </summary>
    public sealed class LifeExtractor : ITimelineExtractor
    {
        private Dictionary<long, Vector2> _was = new();
        private Dictionary<long, Vector2> _now = new();

        public void Frame(EntityRepository repo, FdpEventBus bus, int frame, TimelineEventSink sink)
        {
            _now.Clear();
            if (repo.IsComponentTypeRegistered<NetworkIdentity>())
            {
                bool hasPose = repo.IsComponentTypeRegistered<SimTransform>();
                foreach (var e in repo.Query().With<NetworkIdentity>().Build())
                {
                    long id = repo.GetComponentRO<NetworkIdentity>(e).Value;
                    if (id <= 0) continue;
                    var p = hasPose && repo.HasComponent<SimTransform>(e) ? repo.GetComponentRO<SimTransform>(e).Position : Vector3.Zero;
                    _now[id] = new Vector2(p.X, p.Y);
                    if (!_was.ContainsKey(id)) sink.Add(new TimelineEvent(frame, TimelineLane.Spawned, id, 0, p.X, p.Y, 0));
                }
            }
            foreach (var (id, at) in _was)
                if (!_now.ContainsKey(id)) sink.Add(new TimelineEvent(frame, TimelineLane.Destroyed, id, 0, at.X, at.Y, 0));
            (_was, _now) = (_now, _was);
        }
    }

    /// <summary>
    /// ⭐ <c>CE-1302</c> Combat lanes — from the recorded EVENTS (R0 ②: <c>ShotLog</c> / <c>DetonationLog</c> are side stores, not recorded).
    /// Shot = a fired <see cref="WeaponFireNotification"/> (a refusal is not a shot; X / Y the muzzle), Hit = <see cref="HitEvent"/>, Blast =
    /// <see cref="DetonationNotification"/> (X / Y the point, value the damage).
    /// </summary>
    public sealed class CombatExtractor : ITimelineExtractor
    {
        public void Frame(EntityRepository repo, FdpEventBus bus, int frame, TimelineEventSink sink)
        {
            foreach (ref readonly var f in bus.Read<WeaponFireNotification>())
                if (f.Fired)
                    sink.Add(new TimelineEvent(frame, TimelineLane.Shot, TimelineEventSink.IdOf(repo, f.Shooter), TimelineEventSink.IdOf(repo, f.Target),
                        f.Origin.X, f.Origin.Y, Math.Max(1, f.Quantity)));
            foreach (ref readonly var h in bus.Read<HitEvent>())
                sink.Add(new TimelineEvent(frame, TimelineLane.Hit, TimelineEventSink.IdOf(repo, h.HitEntity), 0, 0, 0, 0));
            foreach (ref readonly var d in bus.Read<DetonationNotification>())
                sink.Add(new TimelineEvent(frame, TimelineLane.Blast, TimelineEventSink.IdOf(repo, d.Shooter), TimelineEventSink.IdOf(repo, d.Target),
                    d.HitX, d.HitY, d.Damage));
        }
    }

    /// <summary>
    /// ⭐ <c>CE-1302</c> / TE-22 Behaviour lanes — frame-to-frame diffs of the recorded histories, per unit (keyed by its event id):
    /// <list type="bullet">
    /// <item>Change — <see cref="BrainJournal.Seq"/> advanced: one event per new entry still in the ring (value = its kind, X / Y its point);</item>
    /// <item>Order / Report — the history's newest entry changed (a new one, or the same order repeated; value = the traffic direction,
    ///   other = the counterpart);</item>
    /// <item>Decision — a <see cref="UtilityDecisionLog"/> slot's <c>SwitchCount</c> rose (value = the decision id, other = the winner).</item>
    /// </list>
    /// ⚠ The first frame a unit is seen is its BASELINE — what its rings already hold happened before the recording reached it.
    /// </summary>
    public sealed class BehaviourExtractor : ITimelineExtractor
    {
        private readonly Dictionary<long, uint> _journal = new();
        private readonly Dictionary<long, (int Count, float At, long Other, int Behaviour, ushort Repeats)> _orders = new();
        private readonly Dictionary<long, (int Count, float At, long From, long To)> _reports = new();
        private readonly Dictionary<long, int[]> _switches = new();

        public void Frame(EntityRepository repo, FdpEventBus bus, int frame, TimelineEventSink sink)
        {
            if (repo.IsComponentTypeRegistered<BrainJournal>())
                foreach (var e in repo.Query().With<BrainJournal>().Build())
                {
                    long id = TimelineEventSink.IdOf(repo, e);
                    ref readonly var j = ref repo.GetComponentRO<BrainJournal>(e);
                    if (_journal.TryGetValue(id, out uint was) && j.Seq > was)
                    {
                        int fresh = (int)Math.Min(j.Seq - was, (uint)j.Count);
                        for (int i = j.Count - fresh; i < j.Count; i++)
                        {
                            var entry = j.At(i);
                            sink.Add(new TimelineEvent(frame, TimelineLane.Change, id, 0, entry.Point.X, entry.Point.Y, (float)entry.Kind));
                        }
                    }
                    _journal[id] = j.Seq;
                }

            // TE-22 ②: every NEW entry is an event, found by the previous newest entry's key (its time + counterpart) — so two orders in
            // one frame are two events, and a ring's eviction never loses one. The same order repeated (counted, not listed) is one more.
            if (repo.IsComponentTypeRegistered<CommandOrderHistory>())
                foreach (var e in repo.Query().With<CommandOrderHistory>().Build())
                {
                    long id = TimelineEventSink.IdOf(repo, e);
                    ref readonly var h = ref repo.GetComponentRO<CommandOrderHistory>(e);
                    if (h.Count == 0) { _orders[id] = default; continue; }
                    if (_orders.TryGetValue(id, out var was))
                    {
                        int fresh = h.Count;
                        for (int i = 0; i < h.Count; i++)
                            if (was.Count > 0 && h.Items[i].At == was.At && h.Items[i].CounterpartNetId == was.Other && h.Items[i].Behaviour == was.Behaviour) { fresh = i; break; }
                        for (int i = fresh - 1; i >= 0; i--)   // oldest new entry first
                            sink.Add(new TimelineEvent(frame, TimelineLane.Order, id, h.Items[i].CounterpartNetId, 0, 0, (float)h.Items[i].Traffic));
                        if (fresh == 0 && h.Items[0].Repeats != was.Repeats)
                            sink.Add(new TimelineEvent(frame, TimelineLane.Order, id, h.Items[0].CounterpartNetId, 0, 0, (float)h.Items[0].Traffic));
                    }
                    _orders[id] = (h.Count, h.Items[0].At, h.Items[0].CounterpartNetId, h.Items[0].Behaviour, h.Items[0].Repeats);
                }

            if (repo.IsComponentTypeRegistered<CommandReportHistory>())
                foreach (var e in repo.Query().With<CommandReportHistory>().Build())
                {
                    long id = TimelineEventSink.IdOf(repo, e);
                    ref readonly var h = ref repo.GetComponentRO<CommandReportHistory>(e);
                    if (h.Count == 0) { _reports[id] = default; continue; }
                    if (_reports.TryGetValue(id, out var was))
                    {
                        int fresh = h.Count;
                        for (int i = 0; i < h.Count; i++)
                            if (was.Count > 0 && h.Items[i].At == was.At && h.Items[i].FromNetId == was.From && h.Items[i].ToNetId == was.To) { fresh = i; break; }
                        for (int i = fresh - 1; i >= 0; i--)
                        {
                            var r = h.Items[i];
                            long other = r.Traffic == CommandTraffic.Incoming ? r.FromNetId : r.ToNetId;
                            sink.Add(new TimelineEvent(frame, TimelineLane.Report, id, other, 0, 0, (float)r.Traffic));
                        }
                    }
                    _reports[id] = (h.Count, h.Items[0].At, h.Items[0].FromNetId, h.Items[0].ToNetId);
                }

            if (repo.IsComponentTypeRegistered<UtilityDecisionLog>())
                foreach (var e in repo.Query().With<UtilityDecisionLog>().Build())
                {
                    long id = TimelineEventSink.IdOf(repo, e);
                    var slots = repo.GetComponentRO<UtilityDecisionLog>(e).SlotsRO();
                    bool seen = _switches.TryGetValue(id, out var known);
                    var was = known ?? (_switches[id] = new int[UtilityDecisionLog.MaxDecisions]);
                    for (int i = 0; i < UtilityDecisionLog.MaxDecisions; i++)
                    {
                        if (seen && slots[i].SwitchCount > was[i])
                            sink.Add(new TimelineEvent(frame, TimelineLane.Decision, id, slots[i].Winner, 0, 0, slots[i].DecisionId));
                        was[i] = slots[i].SwitchCount;
                    }
                }
        }
    }
}
