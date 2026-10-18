using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
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
            int n = squad.Contacts.Count;
            var pool = squad.Contacts.Contacts;
            var sum = Vector3.Zero;
            for (int i = 0; i < n; i++)
            {
                var c = pool[i];
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
            for (int i = 0; i < n; i++)
            {
                var c = pool[i];
                radius = MathF.Max(radius, Vector3.Distance(centre, new Vector3(c.PositionX, c.PositionY, c.PositionZ)));
            }
            var size = SizeOf(n);
            if (outbox.SpotSent != 0 && outbox.SpotSize == size && Vector3.Distance(outbox.SpotAt, centre) <= SpotMoveMetres) return;
            outbox.SpotSent = 1;
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
                repo.Bus.PublishManaged(new SquadReportNotification { FromUnit = unit, ToUnit = superior, Report = q.Report, SentAt = now });
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

        public StaffInboxSystem(bool gateOnAuthority = false) => _gateOnAuthority = gateOnAuthority;

        /// <summary>Reports folded / dropped as stale since this system was built (a test / diagnostics probe).</summary>
        public int FoldedCount { get; private set; }
        public int DroppedCount { get; private set; }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.IsComponentTypeRegistered<CommandPicture>() || !repo.Bus.IsRegisteredManaged<SquadReportNotification>()) return;
            float now = CommandPictureOps.Now(repo);
            foreach (var n in repo.Bus.ReadManaged<SquadReportNotification>())
            {
                if (n.ToUnit.IsNull || !repo.IsAlive(n.ToUnit) || n.FromUnit.IsNull) continue;
                if (_gateOnAuthority && repo.HasComponent<BehaviorState>(n.ToUnit) && !repo.HasAuthority<BehaviorState>(n.ToUnit)) continue;
                if (Fold(repo, n.FromUnit, n.ToUnit, n.Report, now)) FoldedCount++; else DroppedCount++;
            }
        }

        /// <summary>What a report does to the addressee's picture, NOW (the inbox and the HTTP fake share it). <c>false</c> = dropped.</summary>
        public static bool Fold(EntityRepository repo, Entity fromUnit, Entity toUnit, CommandReport r, float now)
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
                    case ReportKind.Progress: sub.Progress = (ProgressKind)r.Subtype; break;
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
