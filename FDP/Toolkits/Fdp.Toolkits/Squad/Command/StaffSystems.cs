using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Events;

namespace Fdp.Toolkit.Squad.Command
{
    /// <summary>
    /// ⭐⭐ <c>CE-2293</c> CC-C (CC-5, CC-8) — the SENDING half of a commander's staff: composes the standing reports nobody has to ask for
    /// and flushes the outbox when due. Runs on the brain node that owns the commander, before the brain tick.
    /// <list type="bullet">
    /// <item><b>PROGRESS</b> when the commander's TASK ends (<see cref="BehaviorFinishedEvent"/>, not a reaction or the SOP): Set on success,
    /// TaskFailed otherwise — so <c>SquadMarch</c>, unchanged, reports SET (CC-5).</item>
    /// <item><b>SPOT</b> from the squad's merged pool (<see cref="SquadCognitiveState"/>): the pool's contacts as ONE element (CC-7: one
    /// SPOT, not one per contact), re-sent only when it is new, changes size class or moves &gt; <see cref="SpotMoveMetres"/>; a SPOT with
    /// Count 0 when the pool empties (lost).</item>
    /// <item><b>SITREP</b> at the superior's requested rate (<see cref="ReportingRequirements.SitrepEverySeconds"/>): strength as a colour
    /// from <see cref="CombatEffective"/>, the play's phase.</item>
    /// <item><b>Flush</b>: a queued report whose time has come goes to <see cref="UnitSubordinate.Commander"/> — unless the outbox is muted
    /// (CC-15, lost comms) or Silent (CC-8: only a CONTACT goes).</item>
    /// </list>
    /// 📄 docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md §3, §5, §8 CC-C.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class StaffComposerSystem : IEcsModuleSystem
    {
        /// <summary>How long a composed report waits before it goes (R-2031: ~3 s, a leader's report).</summary>
        public const float ReportDelaySeconds = 3f;
        /// <summary>CB-37 — an element that moved this far is news.</summary>
        public const float SpotMoveMetres = 25f;
        /// <summary>⭐ CE-2294 (CB-33, CB-34) — a still-held element is SPOTted again this often, so the superior's picture keeps it
        /// fresh while the squad still sees it (and a stale one means it is really gone) — the engaged heartbeat.</summary>
        public const float SpotRefreshSeconds = 15f;

        private readonly bool _gateOnAuthority;
        private EntityQuery? _commanders;

        /// <param name="gateOnAuthority">the pack's execution gate (as <c>SquadCoordinationSystem</c>): only commanders whose brain this
        /// node owns.</param>
        public StaffComposerSystem(bool gateOnAuthority = false) => _gateOnAuthority = gateOnAuthority;

        /// <summary>Reports sent since this system was built (a test / diagnostics probe).</summary>
        public int SentCount { get; private set; }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.IsComponentTypeRegistered<ReportOutbox>() || !repo.IsComponentTypeRegistered<UnitSubordinate>()
                || !repo.IsComponentTypeRegistered<UnitRoster>() || !repo.Bus.IsRegisteredManaged<SquadReportNotification>()) return;
            float now = CommandPictureOps.Now(repo);

            // PROGRESS on the end of a commander's TASK (CC-5)
            if (repo.Bus.IsRegistered<BehaviorFinishedEvent>())
                foreach (var fin in repo.Bus.Read<BehaviorFinishedEvent>())
                {
                    if (fin.Origin == BehaviorOrigin.Reaction || fin.Origin == BehaviorOrigin.Sop) continue;
                    if (!Sends(repo, fin.Entity)) continue;
                    var progress = fin.Result == Fbt.NodeStatus.Success ? ProgressKind.Set : ProgressKind.TaskFailed;
                    ReportQueue.Enqueue(repo, fin.Entity, new CommandReport
                    {
                        Kind = ReportKind.Progress, Subtype = (byte)progress, At = PositionOf(repo, fin.Entity), ObservedAt = now,
                    }, now + ReportDelaySeconds);
                }

            _commanders ??= repo.Query().With<UnitRoster>().With<UnitSubordinate>().WithOwnedWhen<BehaviorState>(_gateOnAuthority).Build();
            foreach (var unit in _commanders)
            {
                if (!Sends(repo, unit)) continue;
                if (!repo.HasComponent<ReportOutbox>(unit)) repo.AddComponent(unit, new ReportOutbox());
                ComposeSpot(repo, unit, now);
                ComposeSitrep(repo, unit, now);
                ComposeCasevac(repo, unit, now);
                Flush(repo, unit, now);
            }
        }

        /// <summary>A commander that has a live superior to report to.</summary>
        private static bool Sends(EntityRepository repo, Entity unit)
        {
            if (!repo.IsAlive(unit) || !repo.HasComponent<UnitSubordinate>(unit) || !repo.HasComponent<UnitRoster>(unit)) return false;
            var superior = repo.GetComponentRO<UnitSubordinate>(unit).Commander;
            return !superior.IsNull && repo.IsAlive(superior);
        }

        private static Vector3 PositionOf(EntityRepository repo, Entity e)
            => repo.HasComponent<SimTransform>(e) ? repo.GetComponentRO<SimTransform>(e).Position : Vector3.Zero;

        /// <summary>SALUTE's S from a head count.</summary>
        public static ElementSize SizeOf(int count)
            => count <= 0 ? ElementSize.SizeUnknown : count == 1 ? ElementSize.Individual : count <= 4 ? ElementSize.Team
             : count <= 12 ? ElementSize.SquadSize : ElementSize.PlatoonSize;

        private static void ComposeSpot(EntityRepository repo, Entity unit, float now)
        {
            if (!repo.HasComponent<SquadCognitiveState>(unit)) return;
            ref readonly var squad = ref repo.GetComponentRO<SquadCognitiveState>(unit);
            var pool = squad.Contacts.Contacts;
            // ⭐ CE-2294 (R-214, CE-3080) — only contacts still IN THE FIGHT are an element: a killed or incapacitated man stays in memory
            //   by design (every threat reader filters him, ThreatDanger), and a neutral is no enemy (CV-8). 📌 Measured live in
            //   cc-platoon-bound: three dead enemies kept a count-3 SPOT going forever, so the platoon never stopped holding.
            Span<int> live = stackalloc int[16];   // the pool holds at most 16 (SquadContactPool)
            int n = 0;
            for (int i = 0; i < squad.Contacts.Count && n < live.Length; i++)
            {
                long id = pool[i].EntityId;
                if (id > 0)
                {
                    var e = new Entity((ulong)id);
                    if (ThreatDanger.IsOutOfTheFight(repo, e) || ThreatDanger.IsNeutral(repo, e)) continue;
                }
                live[n++] = i;
            }
            var sum = Vector3.Zero;
            for (int k = 0; k < n; k++)
            {
                var c = pool[live[k]];
                sum += new Vector3(c.PositionX, c.PositionY, c.PositionZ);
            }
            ref var outbox = ref repo.GetComponentRW<ReportOutbox>(unit);
            if (n == 0)
            {
                if (outbox.SpotSent == 0) return;
                outbox.SpotSent = 0;   // the element is lost: say so (Count 0)
                ReportQueue.Enqueue(repo, unit, new CommandReport { Kind = ReportKind.Spot, At = outbox.SpotAt, ObservedAt = now }, now + ReportDelaySeconds);
                return;
            }
            var centre = sum / n;
            float radius = 0f;
            for (int k = 0; k < n; k++)
            {
                var c = pool[live[k]];
                radius = MathF.Max(radius, Vector3.Distance(centre, new Vector3(c.PositionX, c.PositionY, c.PositionZ)));
            }
            var size = SizeOf(n);
            if (outbox.SpotSent != 0 && outbox.SpotSize == size && Vector3.Distance(outbox.SpotAt, centre) <= SpotMoveMetres
                && now - outbox.SpotSentAt < SpotRefreshSeconds) return;
            outbox.SpotSent = 1;
            outbox.SpotSentAt = now;
            outbox.SpotAt = centre;
            outbox.SpotSize = size;
            ReportQueue.Enqueue(repo, unit, new CommandReport
            {
                Kind = ReportKind.Spot, At = centre, Radius = radius, Count = (byte)Math.Min(n, 255), Size = size, ObservedAt = now,
            }, now + ReportDelaySeconds);
        }

        private static void ComposeSitrep(EntityRepository repo, Entity unit, float now)
        {
            float every = repo.IsComponentTypeRegistered<ReportingRequirements>() && repo.HasComponent<ReportingRequirements>(unit)
                ? repo.GetComponentRO<ReportingRequirements>(unit).SitrepEverySeconds : 0f;
            if (every <= 0f) return;
            ref var outbox = ref repo.GetComponentRW<ReportOutbox>(unit);
            if (outbox.LastSitrepAt > 0f && now - outbox.LastSitrepAt < every) return;
            outbox.LastSitrepAt = MathF.Max(now, 1e-3f);
            ushort phase = repo.HasComponent<SquadCognitiveState>(unit) ? repo.GetComponentRO<SquadCognitiveState>(unit).PhaseId : (ushort)0;
            ReportQueue.Enqueue(repo, unit, new CommandReport
            {
                Kind = ReportKind.Sitrep, At = PositionOf(repo, unit), Strength = StrengthOf(repo, unit), Phase = phase, ObservedAt = now,
            }, now + ReportDelaySeconds);
        }

        /// <summary>⭐ <c>CE-2305</c> RQ-1, RQ-5 — the 9-line as a standing procedure: a member down and ALIVE (incapacitated) is asked for
        /// once — REQUEST Casevac at where he lies, the man as its subject. A man back in the fight frees his slot's bit; a dead man is not
        /// asked for (and keeps it). The squad's own buddy aid goes on regardless; the answer takes him from there.</summary>
        private static void ComposeCasevac(EntityRepository repo, Entity unit, float now)
        {
            ref readonly var roster = ref repo.GetComponentRO<UnitRoster>(unit);
            int n = Math.Min(roster.Count, 16);
            ushort asked = repo.GetComponentRO<ReportOutbox>(unit).CasevacAsked;
            for (int i = 0; i < n; i++)
            {
                var man = roster.SubordinateEntities[i];
                ushort bit = (ushort)(1 << i);
                if (man.IsNull || !repo.IsAlive(man)) continue;
                if (Fdp.Toolkit.Combat.Injury.IsInTheFight(repo, man)) { asked &= (ushort)~bit; continue; }
                if ((asked & bit) != 0 || !Fdp.Toolkit.Combat.Injury.IsIncapacitated(repo, man)) continue;
                asked |= bit;
                ReportQueue.Enqueue(repo, unit, new CommandReport
                {
                    Kind = ReportKind.Request, Subtype = (byte)RequestKind.Casevac, At = PositionOf(repo, man), Count = 1, ObservedAt = now,
                }, now + ReportDelaySeconds, man);
            }
            repo.GetComponentRW<ReportOutbox>(unit).CasevacAsked = asked;
        }

        /// <summary>A unit's strength as a colour: GREEN ≥ 90 % of its members fighting, AMBER ≥ 70 %, RED while still combat
        /// effective, BLACK once it is not (CB-37).</summary>
        public static StatusColour StrengthOf(ISimulationView view, Entity unit)
        {
            if (!view.HasComponent<UnitRoster>(unit)) return StatusColour.ColourUnknown;
            int count = view.GetComponentRO<UnitRoster>(unit).Count;
            if (count == 0) return StatusColour.Black;
            if (!CombatEffective.IsInTheFight(view, unit)) return StatusColour.Black;
            float f = CombatEffective.FightingMembers(view, unit) / (float)count;
            return f >= 0.9f ? StatusColour.Green : f >= 0.7f ? StatusColour.Amber : StatusColour.Red;
        }

        private void Flush(EntityRepository repo, Entity unit, float now)
        {
            ref var outbox = ref repo.GetComponentRW<ReportOutbox>(unit);
            if (outbox.Queued.Count == 0) return;
            var superior = repo.GetComponentRO<UnitSubordinate>(unit).Commander;
            bool muted = outbox.MutedUntil > now;
            int keep = 0;
            for (int i = 0; i < outbox.Queued.Count; i++)
            {
                var q = outbox.Queued.Items[i];
                bool goes = q.DueAt <= now && !muted
                         && (outbox.Posture != ReportingPosture.Silent || q.Report.Kind == ReportKind.Contact);
                if (!goes) { outbox.Queued.Items[keep++] = q; continue; }
                repo.Bus.PublishManaged(new SquadReportNotification { FromUnit = unit, ToUnit = superior, Subject = q.Subject, Report = q.Report, SentAt = now });
                SentCount++;
            }
            outbox.Queued.Count = keep;
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2293</c> CC-C (CC-6, CB-44) — the RECEIVING half: folds each <see cref="SquadReportNotification"/> (this node's own, or
    /// one the BR-3 ingress raised from another node — CE-3250) addressed to a
    /// commander whose brain is here into its <see cref="CommandPicture"/> and raises <see cref="SensorChange.ReportReceived"/> on it (the
    /// SOP's edge). Behaviours read the picture, never the reports. Newest per sender wins (CB-32 — a late older <c>Seq</c> is dropped,
    /// a <c>Synthetic</c> fake always taken, CC-14).
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class StaffInboxSystem : IEcsModuleSystem
    {
        /// <summary>A sender's new SPOT replaces its element within this many metres; farther, it is another element.</summary>
        public const float SameElementMetres = 50f;

        private readonly bool _gateOnAuthority;
        private readonly bool _requireOwnedBrain;

        public StaffInboxSystem(bool gateOnAuthority = false) => _gateOnAuthority = gateOnAuthority;

        private StaffInboxSystem(bool gateOnAuthority, bool requireOwnedBrain)
        {
            _gateOnAuthority = gateOnAuthority;
            _requireOwnedBrain = requireOwnedBrain;
        }

        /// <summary>
        /// ⭐ <c>CE-2301</c> ① (R-2058) — the inbox of a node with no brain (SimHost, IG): it LOGS every report and folds only for a
        /// commander whose <see cref="BehaviorState"/> is here AND owned here — so on such a node it never folds, and a replicated
        /// <see cref="CommandPicture"/> keeps its one writer (the fold would otherwise add a local picture, <c>EnsureRow</c>).
        /// 📄 DESIGN_Command_Chain_Example.md §8b.
        /// </summary>
        public static StaffInboxSystem ForNodeWithoutBrain() => new(gateOnAuthority: true, requireOwnedBrain: true);

        /// <summary>Reports folded / dropped as stale since this system was built (a test / diagnostics probe).</summary>
        public int FoldedCount { get; private set; }
        public int DroppedCount { get; private set; }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.Bus.IsRegisteredManaged<SquadReportNotification>()) return;
            // ⭐ CE-2301 ① (R-2058) — the log is every node's; only the fold needs the picture (§8b)
            bool canFold = repo.IsComponentTypeRegistered<CommandPicture>();
            bool brainHere = repo.IsComponentTypeRegistered<BehaviorState>();
            float now = CommandPictureOps.Now(repo);
            SquadReportLog? log = null;
            foreach (var n in repo.Bus.ReadManaged<SquadReportNotification>())
            {
                // ⭐ CE-2295 (PC-11) — every report this node sees goes into its log, with what the inbox did with it
                log ??= SquadReportLog.For(repo);
                var entry = new SquadReportLogEntry
                {
                    SeenAt = now, FromUnit = n.FromUnit, ToUnit = n.ToUnit, Report = n.Report, OriginNodeId = n.OriginNodeId,
                    Verdict = "not-owned-here",
                };
                log.Add(entry);
                if (!canFold || n.ToUnit.IsNull || !repo.IsAlive(n.ToUnit) || n.FromUnit.IsNull) continue;
                if (_requireOwnedBrain && !(brainHere && repo.HasComponent<BehaviorState>(n.ToUnit) && repo.HasAuthority<BehaviorState>(n.ToUnit))) continue;
                if (_gateOnAuthority && brainHere && repo.HasComponent<BehaviorState>(n.ToUnit) && !repo.HasAuthority<BehaviorState>(n.ToUnit)) continue;
                if (Fold(repo, n.FromUnit, n.ToUnit, n.Report, now, n.Subject)) { FoldedCount++; entry.Verdict = "folded"; }
                else { DroppedCount++; entry.Verdict = "stale"; }
            }
        }

        /// <summary>What a report does to the addressee's picture, NOW (the inbox and the HTTP fake share it). <c>false</c> = dropped.
        /// ⭐ <c>CE-2305</c> RQ-2 — a REQUEST keeps <paramref name="subject"/> (the notification's) as its network id.</summary>
        public static bool Fold(EntityRepository repo, Entity fromUnit, Entity toUnit, CommandReport r, float now, Entity subject = default)
        {
            long from = CommandPictureOps.NetIdOf(repo, fromUnit);
            int row = CommandPictureOps.EnsureRow(repo, toUnit, from);
            if (!repo.HasComponent<CommandPicture>(toUnit)) return false;
            ref var picture = ref repo.GetComponentRW<CommandPicture>(toUnit);
            if (row >= 0)
            {
                ref var sub = ref picture.Subordinates.Items[row];
                if (r.Synthetic == 0 && r.Seq != 0 && r.Seq <= sub.LastSeq) return false;   // CB-32: a late older report
                if (r.Synthetic == 0) sub.LastSeq = Math.Max(sub.LastSeq, r.Seq);
                sub.LastReportAt = MathF.Max(now, 1e-3f);
                switch (r.Kind)
                {
                    case ReportKind.Progress:
                        if (r.ObservedAt >= sub.OrderedAt) sub.Progress = (ProgressKind)r.Subtype;   // CE-2294: older = the last order's
                        break;
                    case ReportKind.Sitrep:   sub.Strength = r.Strength; sub.Ammo = r.Ammo; sub.Phase = r.Phase; sub.LastSitrepAt = sub.LastReportAt; break;
                    case ReportKind.Lace:     sub.Ammo = r.Ammo; break;
                }
            }
            switch (r.Kind)
            {
                case ReportKind.Spot:
                case ReportKind.Contact:
                    FoldElement(ref picture, from, r, now);
                    break;
                case ReportKind.Request:
                    if (picture.Requests.Count == CommandPicture.MaxRequests) RemoveAt(ref picture.Requests, 0);
                    picture.Requests.Items[picture.Requests.Count++] = new PendingRequest
                    {
                        FromNetId = from, Kind = (RequestKind)r.Subtype, At = r.At, ReceivedAt = now, Seq = r.Seq,
                        SubjectNetId = subject.IsNull ? 0 : CommandPictureOps.NetIdOf(repo, subject),
                    };
                    break;
            }
            if (repo.Bus.IsRegistered<SensorChangedEvent>())
                repo.Bus.Publish(new SensorChangedEvent { Unit = toUnit, What = SensorChange.ReportReceived });
            return true;
        }

        private static void FoldElement(ref CommandPicture picture, long from, in CommandReport r, float now)
        {
            int same = -1;
            float best = SameElementMetres;
            for (int i = 0; i < picture.Elements.Count; i++)
            {
                ref readonly var e = ref picture.Elements.Items[i];
                if (e.FromNetId != from) continue;
                float d = Vector3.Distance(e.At, r.At);
                if (d <= best) { best = d; same = i; }
            }
            if (r.Count == 0)                               // lost: the sender's element there goes
            {
                if (same >= 0) RemoveAt(ref picture.Elements, same);
                return;
            }
            var element = new ReportedElement
            {
                At = r.At, Radius = r.Radius, Count = r.Count, Size = r.Size, Activity = r.Activity, Equipment = r.Equipment,
                FromNetId = from, ObservedAt = r.ObservedAt, ReceivedAt = now, Seq = r.Seq, Synthetic = r.Synthetic,
            };
            if (same >= 0) { picture.Elements.Items[same] = element; return; }
            if (picture.Elements.Count == CommandPicture.MaxElements)   // full: the oldest observation goes
            {
                int oldest = 0;
                for (int i = 1; i < picture.Elements.Count; i++)
                    if (picture.Elements.Items[i].ObservedAt < picture.Elements.Items[oldest].ObservedAt) oldest = i;
                RemoveAt(ref picture.Elements, oldest);
            }
            picture.Elements.Items[picture.Elements.Count++] = element;
        }

        private static void RemoveAt(ref ReportedElementList list, int index)
        {
            for (int i = index + 1; i < list.Count; i++) list.Items[i - 1] = list.Items[i];
            list.Count--;
        }

        private static void RemoveAt(ref PendingRequestList list, int index)
        {
            for (int i = index + 1; i < list.Count; i++) list.Items[i - 1] = list.Items[i];
            list.Count--;
        }
    }
}
