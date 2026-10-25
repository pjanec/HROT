using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Squad.Command;
using Hrot.AI.Behaviors.Orders;
using Hrot.Map.Definitions.Behavior.Orders;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-2292</c> CC-B (CC-4, CC-9) — the ONE way a commander's run orders a subordinate, shared by the squad runner and the command
    /// nodes: the order is recorded on the commander (<see cref="SubordinateOrders"/>, released when the giving run ends) and published at
    /// <see cref="BehaviorOrigin.Superior"/>. ⭐ Abstract (<see cref="Order"/>: <c>Order.&lt;name&gt;</c>, the receiver's class decides how)
    /// or concrete (<see cref="Assign"/>: exactly this behaviour). 📄 docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md §7a CC-9.
    /// </summary>
    public static class CommandOrders
    {
        /// <summary>An ABSTRACT order to <paramref name="member"/>. Its release is an unnamed clear (the commander cannot know what it
        /// resolved to). <paramref name="newOrder"/> = a fresh order from a play (not the runner's re-send): the picture forgets the
        /// member's progress on the last one (<see cref="CommandPictureOps.OrderGiven"/>).</summary>
        public static void Order(EntityRepository world, Entity commander, Entity member, string orderName, string json, bool newOrder = false)
        {
            SubordinateOrders.Record(world, commander, member, string.Empty, BehaviorOrigin.Superior);
            if (newOrder) CommandPictureOps.OrderGiven(world, commander, member);
            world.Bus.PublishManaged(new AssignTacticalIntentEvent
            {
                Entity = member, IntentId = OrderAttribute.IntentFor(orderName), JsonParams = json, Origin = BehaviorOrigin.Superior,
            });
            Journal(world, commander, member, OrderAttribute.IntentFor(orderName));
        }

        /// <summary>⭐ <c>CE-1250</c> (Brain View BV-10) — the commander's brain journal records the order: A = the member's network id (0
        /// when it has none), B = the behaviour (or order) hash. Every command node gives its orders through here, so every commander writes it.</summary>
        private static void Journal(EntityRepository world, Entity commander, Entity member, string behaviour)
        {
            long id = world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>()
                ? Fdp.Toolkit.Replication.Services.NetworkIdResolver.RuntimeNetworkIdOf(world, member) : 0;
            // ⭐ CE-1319 (BV-58) — the order histories first, so the cause they copy is the change that issued it, not this order
            CommandHistoryOf.Order(world, commander, member, BehaviorHash.FromName(behaviour), CommandPictureOps.Now(world));
            Fdp.Toolkit.Behavior.Diagnostics.BrainJournalOf.Write(world, commander, Fdp.Toolkit.Behavior.Diagnostics.BrainJournalKind.OrderIssued,
                a: (int)id, b: BehaviorHash.FromName(behaviour));
        }

        /// <summary>⭐ <c>CE-2294</c> CC-D — the order the commander's TICKING run gave <paramref name="member"/> still stands: nothing
        /// else (a reaction's order, released when it ended, or an operator's) replaced it since. A run resumed after a reaction finds
        /// its orders gone and gives them again.</summary>
        public static bool Stands(EntityRepository world, Entity commander, Entity member)
        {
            uint run = BehaviorOwnedParts.OwnerOf(world, commander);
            var held = SubordinateOrders.Of(world, commander);
            if (run == 0 || held == null) return false;
            foreach (var e in held.Orders)
                if (e.Member.Equals(member)) return e.Run == run;
            return false;
        }

        /// <summary>⭐ <c>CE-2312</c> (HQ-6) — <paramref name="member"/> is BUSY with the commander's order: the commander's ticking run gave
        /// it one and it still runs a task from its superior (not finished, not cleared, not taken by a reaction). A finished asset is free
        /// again — an aircraft has no staff to report it.</summary>
        public static bool Busy(EntityRepository world, Entity commander, Entity member)
        {
            uint run = BehaviorOwnedParts.OwnerOf(world, commander);
            var held = SubordinateOrders.Of(world, commander);
            if (run == 0 || held == null || !world.IsAlive(member) || !world.HasComponent<BehaviorState>(member)) return false;
            ref readonly var bs = ref world.GetComponentRO<BehaviorState>(member);
            if (bs.Origin != BehaviorOrigin.Superior || bs.ActiveBehaviorHash == BehaviorIds.None) return false;
            foreach (var e in held.Orders)
                if (e.Member.Equals(member)) return e.Run == run;
            return false;
        }

        /// <summary>A CONCRETE order: <paramref name="member"/> runs <paramref name="behaviour"/> with <paramref name="json"/>. Its release
        /// names the behaviour.</summary>
        public static void Assign(EntityRepository world, Entity commander, Entity member, string behaviour, string json, bool newOrder = false)
        {
            SubordinateOrders.Record(world, commander, member, behaviour, BehaviorOrigin.Superior, json);
            if (newOrder) CommandPictureOps.OrderGiven(world, commander, member);
            world.Bus.PublishManaged(new AssignTacticalIntentEvent
            {
                Entity = member, IntentId = behaviour, JsonParams = json, Origin = BehaviorOrigin.Superior,
            });
            Journal(world, commander, member, behaviour);
        }

        /// <summary>⭐ <c>CE-2347</c> — the commander's ticking run's LAST order to <paramref name="member"/> was exactly this one
        /// (<paramref name="behaviour"/> with <paramref name="json"/>), whether he still runs it or has finished it. A play whose next order
        /// differs (a squad brought its wounded in, now it boards) gives it; one whose order is unchanged does not restart a finished task.</summary>
        public static bool Gave(EntityRepository world, Entity commander, Entity member, string behaviour, string json)
        {
            uint run = BehaviorOwnedParts.OwnerOf(world, commander);
            var held = SubordinateOrders.Of(world, commander);
            if (run == 0 || held == null) return false;
            foreach (var e in held.Orders)
                if (e.Member.Equals(member)) return e.Run == run && e.Behavior == behaviour && e.Json == json;
            return false;
        }

        /// <summary>⭐ <c>CE-2304</c> — the commander's ticking run already gave <paramref name="member"/> exactly this order
        /// (<paramref name="behaviour"/> with <paramref name="json"/>) and he still runs it from his superior: sending it again would only
        /// restart him — measured live, every re-send stopped a posted defender and started him again. False whenever anything differs, or
        /// he no longer runs it (finished, or a reaction / another order took him), so a needed re-send still goes.</summary>
        public static bool StillRunning(EntityRepository world, Entity commander, Entity member, string behaviour, string json)
        {
            uint run = BehaviorOwnedParts.OwnerOf(world, commander);
            var held = SubordinateOrders.Of(world, commander);
            if (run == 0 || held == null || !world.IsAlive(member) || !world.HasComponent<BehaviorState>(member)) return false;
            ref readonly var bs = ref world.GetComponentRO<BehaviorState>(member);
            if (bs.Origin != BehaviorOrigin.Superior || bs.ActiveBehaviorHash != BehaviorHash.FromName(behaviour)) return false;
            foreach (var e in held.Orders)
                if (e.Member.Equals(member)) return e.Run == run && e.Behavior == behaviour && e.Json == json;
            return false;
        }
    }

    /// <summary>Params of <see cref="CommandNodes.OrderSubordinate"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct OrderSubordinateParams
    {
        /// <summary>Which subordinate: its slot in the commander's roster.</summary>
        public int Slot;
        /// <summary>The order — Move or Hold (the ones a point and a speed describe; Attack, Strike and ObserveFire name a target and are
        /// given from C# through <see cref="CommandOrders.Order"/>).</summary>
        public OrderKind Order;
        /// <summary>Move: where (local metres; the order carries it geodetic).</summary>
        public Vector3 Point;
        /// <summary>Move: m/s (0 = the receiver's own default).</summary>
        public float Speed;
        /// <summary>Move: how near counts as there (0 = the receiver's own default).</summary>
        public float ArrivalRadius;
    }

    /// <summary>⭐ <c>CE-2294</c> — params of <see cref="CommandNodes.MoveLeg"/>: one leg of a play, one subordinate moving to one point.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MoveLegParams
    {
        /// <summary>Which subordinate: its slot in the commander's roster.</summary>
        public int Slot;
        /// <summary>Where (local metres).</summary>
        public Vector3 Point;
        /// <summary>m/s (0 = the receiver's own default).</summary>
        public float Speed;
        /// <summary>How near counts as there (0 = the receiver's own default).</summary>
        public float ArrivalRadius;
        /// <summary>The extra SITREP asked with the order (s; 0 = none) — CC-2, CC-8.</summary>
        public float SitrepEverySeconds;
    }

    /// <summary>⭐ <c>CE-2294</c> — what <see cref="CommandNodes.MoveLeg"/> keeps: how many times it gave its order.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MoveLegState
    {
        public int Orders;
    }

    /// <summary>⭐ <c>CE-2294</c> — params of <see cref="CommandNodes.WithdrawReporting"/>: the subordinates whose extras a play takes back.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct WithdrawReportingParams
    {
        public int SlotA;
        /// <summary>A second slot (-1 = none).</summary>
        public int SlotB;
    }

    /// <summary>Params of <see cref="CommandNodes.SetSubordinateReporting"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SetSubordinateReportingParams
    {
        public int Slot;
        public PirFlags Pir;
        public FfirFlags Ffir;
        public float SitrepEverySeconds;
        /// <summary>PC-3 — the area entity's network id the area PIRs refer to (0 = none).</summary>
        public long PirArea;
    }

    /// <summary>Params of <see cref="CommandNodes.SendReport"/> — a milestone or a request (CC-8: what the staff cannot infer).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SendReportParams
    {
        /// <summary>Progress or Request (the staff composes the rest).</summary>
        public ReportKind Kind;
        /// <summary>The <see cref="ProgressKind"/> or <see cref="RequestKind"/>.</summary>
        public byte Subtype;
        /// <summary>PROGRESS PhaseLine: the line's index (PC-4).</summary>
        public byte Count;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2292</c> CC-B (CB-46) — the COMMAND ACTIONS: shared C# nodes any BTree / HSM / blueprint binds, so a play orders its
    /// subordinates, asks them for reports and sends its own milestones. A subordinate is named by its roster SLOT. Each acts once and
    /// succeeds (an HSM state's entry action). 📄 Command Chain §6, §8 CC-B.
    /// </summary>
    public static class CommandNodes
    {
        /// <summary>How long a play's own report waits before it goes (R-2031: a leader's report goes ~3 s after the fact).</summary>
        public const float ReportDelaySeconds = 3f;

        /// <summary>Order the subordinate in <see cref="OrderSubordinateParams.Slot"/> to Move or Hold — ABSTRACT (CC-9): its class decides
        /// how. A fresh order: the picture forgets its progress on the last one.</summary>
        [SharedAiAction]
        public static NodeStatus OrderSubordinate(ref OrderSubordinateParams p, Entity self, EntityRepository world)
        {
            var member = CommandPictureOps.Subordinate(world, self, p.Slot);
            if (member.IsNull || !world.IsAlive(member)) return NodeStatus.Failure;
            string json;
            switch (p.Order)
            {
                case OrderKind.Move:
                    json = JsonSerializer.Serialize(new MoveOrder { Location = OrderGeo.ToGeo(world, p.Point), Speed = p.Speed, ArrivalRadius = p.ArrivalRadius },
                                                    Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);
                    break;
                case OrderKind.Hold:
                    json = "{}";
                    break;
                default:
                    return NodeStatus.Failure;
            }
            CommandOrders.Order(world, self, member, p.Order.ToString(), json, newOrder: true);
            return NodeStatus.Success;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2294</c> CC-D — one LEG of a play, as an HSM ACTIVITY (it runs every tick of its state): the subordinate in
        /// <see cref="MoveLegParams.Slot"/> moves to <see cref="MoveLegParams.Point"/> (an abstract Move, CC-9) with the extra SITREP asked
        /// beside it (CC-2). ⭐ The order is given whenever this run's order to it does not STAND (<see cref="CommandOrders.Stands"/>): on
        /// the state's first tick, and again when the run RESUMES after a reaction whose orders replaced it (an HSM resumes in its state,
        /// its activities tick on — Decision Layer §4.1 — so this is how the plan picks up where it was). The progress the picture keeps is
        /// reset each time (a fresh order), so the leg's guard waits for THIS order's Set. Running while the subordinate exists; the
        /// state's guard ends it.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus MoveLeg(ref MoveLegParams p, ref MoveLegState ws, Entity self, EntityRepository world)
        {
            var member = CommandPictureOps.Subordinate(world, self, p.Slot);
            if (member.IsNull || !world.IsAlive(member)) return NodeStatus.Failure;
            if (CommandOrders.Stands(world, self, member)) return NodeStatus.Running;
            var move = new OrderSubordinateParams { Slot = p.Slot, Order = OrderKind.Move, Point = p.Point, Speed = p.Speed, ArrivalRadius = p.ArrivalRadius };
            if (OrderSubordinate(ref move, self, world) != NodeStatus.Success) return NodeStatus.Failure;
            if (p.SitrepEverySeconds > 0f)
            {
                var extras = new SetSubordinateReportingParams { Slot = p.Slot, SitrepEverySeconds = p.SitrepEverySeconds };
                SetSubordinateReporting(ref extras, self, world);
            }
            ws.Orders++;
            return NodeStatus.Running;
        }

        /// <summary>⭐ <c>CE-2294</c> — a play takes back the extra reports it asked of its subordinates (all zero withdraws them, CC-A).</summary>
        [SharedAiAction]
        public static NodeStatus WithdrawReporting(ref WithdrawReportingParams p, Entity self, EntityRepository world)
        {
            var none = new SetSubordinateReportingParams { Slot = p.SlotA };
            SetSubordinateReporting(ref none, self, world);
            if (p.SlotB >= 0)
            {
                none = new SetSubordinateReportingParams { Slot = p.SlotB };
                SetSubordinateReporting(ref none, self, world);
            }
            return NodeStatus.Success;
        }

        /// <summary>Ask the subordinate in <see cref="SetSubordinateReportingParams.Slot"/> for EXTRA reports (CC-2, CC-8) — all zero
        /// withdraws them. Origin Superior.</summary>
        [SharedAiAction]
        public static NodeStatus SetSubordinateReporting(ref SetSubordinateReportingParams p, Entity self, EntityRepository world)
        {
            var member = CommandPictureOps.Subordinate(world, self, p.Slot);
            if (member.IsNull || !world.Bus.IsRegistered<SetReportingEvent>()) return NodeStatus.Failure;
            world.Bus.Publish(new SetReportingEvent
            {
                Entity = member, Origin = BehaviorOrigin.Superior, Pir = p.Pir, Ffir = p.Ffir,
                SitrepEverySeconds = p.SitrepEverySeconds, PirArea = p.PirArea,
            });
            return NodeStatus.Success;
        }

        /// <summary>Queue the play's own PROGRESS or REQUEST into the commander's <see cref="ReportOutbox"/>: it goes up after
        /// <see cref="ReportDelaySeconds"/> (the staff sends it, CC-C). Failure when the outbox is full or the kind is not the play's.</summary>
        [SharedAiAction]
        public static NodeStatus SendReport(ref SendReportParams p, Entity self, EntityRepository world)
        {
            if (p.Kind != ReportKind.Progress && p.Kind != ReportKind.Request) return NodeStatus.Failure;
            float now = CommandPictureOps.Now(world);
            var at = world.HasComponent<SimTransform>(self) ? world.GetComponentRO<SimTransform>(self).Position : Vector3.Zero;
            return ReportQueue.Enqueue(world, self, new CommandReport
            {
                Kind = p.Kind, Subtype = p.Subtype, Count = p.Count, At = at, ObservedAt = now,
            }, now + ReportDelaySeconds) ? NodeStatus.Success : NodeStatus.Failure;
        }
    }
}
