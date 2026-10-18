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
        }

        /// <summary>A CONCRETE order: <paramref name="member"/> runs <paramref name="behaviour"/> with <paramref name="json"/>. Its release
        /// names the behaviour.</summary>
        public static void Assign(EntityRepository world, Entity commander, Entity member, string behaviour, string json, bool newOrder = false)
        {
            SubordinateOrders.Record(world, commander, member, behaviour, BehaviorOrigin.Superior);
            if (newOrder) CommandPictureOps.OrderGiven(world, commander, member);
            world.Bus.PublishManaged(new AssignTacticalIntentEvent
            {
                Entity = member, IntentId = behaviour, JsonParams = json, Origin = BehaviorOrigin.Superior,
            });
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
