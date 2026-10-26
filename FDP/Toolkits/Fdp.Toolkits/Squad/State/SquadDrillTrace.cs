using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Squad
{
    /// <summary>
    /// ⭐ <c>CE-2157</c> (O-a) — what a commander's squad drill is doing, kept for the debug route and the demo checkers: the drill and
    /// its phase, the last <see cref="HistoryCapacity"/> phase changes (and the event that caused each), the order each member got,
    /// the two fire-and-movement gates and the casualty-care state. Written by the squad manoeuvre runner; read by
    /// <c>GET /entities/{id}/squad</c> ("maneuver"). 📄 <c>docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md</c> §7.8.1.
    /// <para>Brain-local and transient (like <c>SubordinateOrders</c>): never on the wire, never saved, registered on first use.
    /// Immutable — every change sets a new instance (changes come per phase / per issue, not per frame).</para>
    /// </summary>
    [ComponentId(GlobalComponentIds.SquadDrillTrace)]
    [DataPolicy(DataPolicy.Transient)]
    public sealed class SquadDrillTrace
    {
        /// <summary>How many phase changes are kept (oldest dropped).</summary>
        public const int HistoryCapacity = 16;

        /// <summary><see cref="Step.Event"/>: the drill started (its first phase).</summary>
        public const byte EventStart = 200;
        /// <summary><see cref="Step.Event"/>: the drill took the squad back after another drill used it.</summary>
        public const byte EventRetake = 201;
        /// <summary><see cref="Step.Event"/>: the drill ended (success, failure, abort or cleared).</summary>
        public const byte EventEnd = 202;
        /// <summary><see cref="Step.Event"/>: a phase moved on because it ran out of time (not because its gate held).</summary>
        public const byte EventTimeout = 203;

        /// <summary>One member's current order (index = roster slot).</summary>
        public readonly struct Order
        {
            public readonly Entity Member;
            /// <summary>The runner's <c>MemberOrderKind</c> value (Hold, AdvanceTo, Disembark, …); 255 = no order.</summary>
            public readonly byte Kind;
            public readonly Vector3 Point;
            /// <summary>The point came from a cover query.</summary>
            public readonly bool Cover;
            public readonly double IssuedAt;
            /// <summary>The entity the order is about (the contact to suppress, the man to carry); Null when none.</summary>
            public readonly Entity Target;
            public Order(Entity member, byte kind, Vector3 point, bool cover, double issuedAt, Entity target = default)
            {
                Member = member; Kind = kind; Point = point; Cover = cover; IssuedAt = issuedAt; Target = target;
            }
        }

        /// <summary>One phase change.</summary>
        public readonly struct Step
        {
            public readonly double At;
            public readonly ushort Phase;
            /// <summary>A <c>PhaseEventKind</c> value, or <see cref="EventStart"/> / <see cref="EventRetake"/> / <see cref="EventEnd"/> /
            /// <see cref="EventTimeout"/>.</summary>
            public readonly byte Event;
            public Step(double at, ushort phase, byte evt) { At = at; Phase = phase; Event = evt; }
        }

        /// <summary>The casualty-care play's state (§7.5).</summary>
        public readonly struct CareState
        {
            public readonly Entity Casualty;
            public readonly Entity Medic;
            public readonly Vector3 CoverPoint;
            public readonly bool HasCoverPoint;
            /// <summary>The care play's stage (its phase id).</summary>
            public readonly byte Stage;
            public CareState(Entity casualty, Entity medic, Vector3 coverPoint, bool hasCoverPoint, byte stage)
            {
                Casualty = casualty; Medic = medic; CoverPoint = coverPoint; HasCoverPoint = hasCoverPoint; Stage = stage;
            }
        }

        public readonly bool Active;
        public readonly ushort Kind;
        public readonly ushort Phase;
        public readonly double PhaseSince;
        public readonly Order[] Orders;
        /// <summary>Oldest first, at most <see cref="HistoryCapacity"/>.</summary>
        public readonly Step[] History;
        public readonly bool HasGates;
        public readonly bool Covering;
        public readonly bool Set;
        public readonly bool HasCare;
        public readonly CareState Care;

        public static readonly SquadDrillTrace Empty = new(false, 0, 0, 0d, Array.Empty<Order>(), Array.Empty<Step>(), false, false, false, false, default);

        private SquadDrillTrace(bool active, ushort kind, ushort phase, double phaseSince, Order[] orders, Step[] history,
                                bool hasGates, bool covering, bool set, bool hasCare, CareState care)
        {
            Active = active; Kind = kind; Phase = phase; PhaseSince = phaseSince; Orders = orders; History = history;
            HasGates = hasGates; Covering = covering; Set = set; HasCare = hasCare; Care = care;
        }

        private SquadDrillTrace With(bool? active = null, ushort? kind = null, ushort? phase = null, double? phaseSince = null,
                                     Order[]? orders = null, Step[]? history = null, bool? hasGates = null, bool? covering = null,
                                     bool? set = null, bool? hasCare = null, CareState? care = null)
            => new(active ?? Active, kind ?? Kind, phase ?? Phase, phaseSince ?? PhaseSince, orders ?? Orders, history ?? History,
                   hasGates ?? HasGates, covering ?? Covering, set ?? Set, hasCare ?? HasCare, care ?? Care);

        private Step[] Append(Step step)
        {
            int keep = Math.Min(History.Length, HistoryCapacity - 1);
            var h = new Step[keep + 1];
            Array.Copy(History, History.Length - keep, h, 0, keep);
            h[keep] = step;
            return h;
        }

        // ── the writes (the runner and the plays) ─────────────────────────────────────────────────────────────────────────

        /// <summary>The trace on <paramref name="commander"/>, or <see cref="Empty"/>.</summary>
        public static SquadDrillTrace Of(EntityRepository repo, Entity commander)
            => repo.TryGetTable(typeof(SquadDrillTrace), out _) && repo.IsAlive(commander) && repo.HasManagedComponent<SquadDrillTrace>(commander)
                ? ((ISimulationView)repo).GetManagedComponentRO<SquadDrillTrace>(commander) ?? Empty : Empty;

        private static void Put(EntityRepository repo, Entity commander, SquadDrillTrace trace)
        {
            if (!repo.TryGetTable(typeof(SquadDrillTrace), out _)) repo.RegisterManagedComponent<SquadDrillTrace>();
            repo.SetManagedComponent(commander, trace);
            // ⭐ CE-4047 (ui, cross-lane) — the recorded headline follows every change, so the Brain tab's Squad section replays
            if (!repo.IsComponentTypeRegistered<SquadDrillState>()) repo.RegisterComponent<SquadDrillState>();
            var state = new SquadDrillState { Active = (byte)(trace.Active ? 1 : 0), Kind = trace.Kind, Phase = trace.Phase, PhaseSince = trace.PhaseSince };
            if (repo.HasComponent<SquadDrillState>(commander)) repo.SetComponent(commander, state);
            else repo.AddComponent(commander, state);
        }

        /// <summary>A drill started on the squad: a fresh trace (the old one's history is kept, so a drill that follows another shows both).</summary>
        public static void Started(EntityRepository repo, Entity commander, ushort kind, ushort phase, double now)
        {
            var t = Of(repo, commander);
            Put(repo, commander, t.With(active: true, kind: kind, phase: phase, phaseSince: now, orders: Array.Empty<Order>(),
                                        history: t.Append(new Step(now, phase, EventStart)), hasGates: false, covering: false, set: false,
                                        hasCare: false, care: default));
            // ⭐ CE-1250 (Brain View BV-14) — every play also writes the commander's brain journal; this trace stays (the squad route reads it)
            Fdp.Toolkit.Behavior.Diagnostics.BrainJournalOf.Write(repo, commander, Fdp.Toolkit.Behavior.Diagnostics.BrainJournalKind.PlayStarted,
                a: kind, b: phase, cause: EventStart);
        }

        /// <summary>The phase changed to <paramref name="phase"/> on <paramref name="evt"/>.</summary>
        public static void PhaseChanged(EntityRepository repo, Entity commander, ushort kind, ushort phase, byte evt, double now)
        {
            var t = Of(repo, commander);
            Put(repo, commander, t.With(active: true, kind: kind, phase: phase, phaseSince: now, history: t.Append(new Step(now, phase, evt))));
            Fdp.Toolkit.Behavior.Diagnostics.BrainJournalOf.Write(repo, commander, Fdp.Toolkit.Behavior.Diagnostics.BrainJournalKind.PlayPhase,
                a: kind, b: phase, cause: evt);   // ⭐ CE-1250 — the cause: the phase event, or Retake / Timeout
        }

        /// <summary>The drill ended.</summary>
        public static void Ended(EntityRepository repo, Entity commander, double now)
        {
            var t = Of(repo, commander);
            if (!t.Active) return;
            Put(repo, commander, t.With(active: false, history: t.Append(new Step(now, t.Phase, EventEnd))));
            Fdp.Toolkit.Behavior.Diagnostics.BrainJournalOf.Write(repo, commander, Fdp.Toolkit.Behavior.Diagnostics.BrainJournalKind.PlayEnded,
                a: t.Kind, b: t.Phase, cause: EventEnd);   // ⭐ CE-1250
        }

        /// <summary>The orders of one issue, one per roster slot (a slot with no order: <see cref="Order.Kind"/> 255).</summary>
        public static void Issued(EntityRepository repo, Entity commander, Order[] orders)
            => Put(repo, commander, Of(repo, commander).With(orders: orders));

        /// <summary>The fire-and-movement gates as the commander reads them — written only when they change.</summary>
        public static void Gates(EntityRepository repo, Entity commander, bool covering, bool set)
        {
            var t = Of(repo, commander);
            if (t.HasGates && t.Covering == covering && t.Set == set) return;
            Put(repo, commander, t.With(hasGates: true, covering: covering, set: set));
        }

        /// <summary>The casualty-care state — written only when it changes.</summary>
        public static void CareChanged(EntityRepository repo, Entity commander, in CareState care)
        {
            var t = Of(repo, commander);
            if (t.HasCare && t.Care.Casualty == care.Casualty && t.Care.Medic == care.Medic && t.Care.Stage == care.Stage
                && t.Care.HasCoverPoint == care.HasCoverPoint && t.Care.CoverPoint == care.CoverPoint) return;
            Put(repo, commander, t.With(hasCare: true, care: care));
        }
    }
}
