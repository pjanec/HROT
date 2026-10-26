using System.Collections.Generic;
using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Events;

namespace Fdp.Toolkit.Behavior.Components
{
    /// <summary>
    /// ⭐⭐ <c>CE-2141</c> (AQ90 A5) — the orders a commander's behaviour RUN gave its members. Like the run's sensors
    /// (<see cref="BehaviorOwnedParts"/>, CE-485 D4) they belong to the run: when it ends — finished, cleared, replaced, re-assigned,
    /// a dropped paused task, but NOT paused — each member's order is cleared (a Clear intent that NAMES the ordered behaviour), so
    /// the member's own SOP picks next. 📄 <c>docs/DESIGN_Orders_Params_Entity_Classes.md</c> §7.
    /// <para>Brain-local and transient (like <see cref="PausedTask"/>): never on the wire, never saved. Immutable — every change
    /// sets a new instance.</para>
    /// </summary>
    [ComponentId(GlobalComponentIds.SubordinateOrders)]
    // ⭐ CE-3299 D1 — RECORDED (was Transient/NoReplay): sim state a restored run diverges without (restore design §6b, M4).
    [DataPolicy(DataPolicy.NoPreview | DataPolicy.NoScenario)]
    // ⭐ CE-4046 — an immutable record: Record / Release build a new value and Set it, so the recorder sees every change
    public sealed record SubordinateOrders
    {
        /// <summary>One member's latest order from this commander.</summary>
        // ⭐ CE-3299 D1 — not `readonly`: the recorder writes public settable members (CE-3305), so readonly fields were unrecordable.
        //   The members are still only set through the constructor.
        public struct Entry
        {
            public Entity Member;
            /// <summary>The behaviour ordered ("" = unknown to the commander, e.g. an order NAME a unit member resolves itself:
            /// its release is an unnamed clear).</summary>
            public string Behavior;
            public BehaviorOrigin Origin;
            /// <summary>The commander's run that gave it (<see cref="BehaviorState.InstanceId"/>, or the SOP's run).</summary>
            public uint Run;
            /// <summary>⭐ <c>CE-2304</c> — the order's params as sent ("" = not kept): an unchanged re-send is recognised by them.</summary>
            public string Json;

            public Entry(Entity member, string behavior, BehaviorOrigin origin, uint run, string json = "")
            {
                Member = member; Behavior = behavior ?? string.Empty; Origin = origin; Run = run; Json = json ?? string.Empty;
            }
        }

        public IReadOnlyList<Entry> Orders { get; init; }   // ⭐ CE-4046 — read-only (same name and wire shape as the array it was)

        public SubordinateOrders(Entry[] orders) => Orders = orders;

        /// <summary>⭐ <c>CE-3299</c> D1 — for the recorder, which builds a component before it writes its members.</summary>
        public SubordinateOrders() => Orders = Array.Empty<Entry>();

        /// <summary>The commander's run that is ticking now gave <paramref name="member"/> <paramref name="behavior"/> with
        /// <paramref name="json"/> (replaces that member's previous record — a member runs one task).</summary>
        public static void Record(EntityRepository repo, Entity commander, Entity member, string behavior, BehaviorOrigin origin, string json = "")
        {
            uint run = BehaviorOwnedParts.OwnerOf(repo, commander);
            if (run == 0) return;
            if (!repo.TryGetTable(typeof(SubordinateOrders), out _)) repo.RegisterManagedComponent<SubordinateOrders>();
            var before = Of(repo, commander)?.Orders ?? Array.Empty<Entry>();
            var after = new Entry[before.Count + 1];
            int n = 0;
            foreach (var e in before) if (!e.Member.Equals(member)) after[n++] = e;
            after[n++] = new Entry(member, behavior, origin, run, json);
            Array.Resize(ref after, n);
            repo.SetManagedComponent(commander, new SubordinateOrders(after));
        }

        /// <summary>The run <paramref name="endingRun"/> of <paramref name="commander"/> ended: clear every order it gave (a Clear
        /// intent naming the ordered behaviour, at the order's origin). Returns how many.</summary>
        public static int Release(EntityRepository repo, Entity commander, uint endingRun)
        {
            if (endingRun == 0) return 0;
            var held = Of(repo, commander);
            if (held == null) return 0;
            int released = 0;
            var keep = new Entry[held.Orders.Count];
            int n = 0;
            foreach (var e in held.Orders)
            {
                if (e.Run != endingRun) { keep[n++] = e; continue; }
                if (!repo.IsAlive(e.Member)) continue;
                repo.Bus.PublishManaged(new AssignTacticalIntentEvent
                {
                    Entity = e.Member, Kind = TacticalIntentKind.Clear, IntentId = e.Behavior, Origin = e.Origin,
                });
                EngagementAreas.WithdrawSuperior(repo, e.Member);   // ⭐ CE-2302 (DA-32) — the sector the run gave him ends with it
                released++;
            }
            if (released == 0 && n == held.Orders.Count) return 0;
            Array.Resize(ref keep, n);
            repo.SetManagedComponent(commander, n == 0 ? null! : new SubordinateOrders(keep));
            return released;
        }

        /// <summary>What <paramref name="commander"/> holds, or null.</summary>
        public static SubordinateOrders? Of(EntityRepository repo, Entity commander)
            => repo.TryGetTable(typeof(SubordinateOrders), out _) && repo.IsAlive(commander) && repo.HasManagedComponent<SubordinateOrders>(commander)
                ? ((ISimulationView)repo).GetManagedComponentRO<SubordinateOrders>(commander)
                : null;
    }
}
