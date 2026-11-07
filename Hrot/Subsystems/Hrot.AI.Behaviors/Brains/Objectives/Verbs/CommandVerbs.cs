using System;
using System.Text.Json.Nodes;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.ScenarioRules;
using Fdp.Toolkit.ScenarioRules.Actions;
using Hrot.Common.Events;
using Hrot.Core.Mission;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains.Objectives.Verbs
{
    /// <summary>⭐ <c>Order</c>'s params (rules S2 ⑨): who, which behaviour, and — <c>ArgsRef</c> — the index of the behaviour's own params JSON in the caller's
    /// <see cref="ActionArgs.Items"/> (-1 = none; the step's nested <c>"params"</c> is lifted there at assignment, <see cref="ActionList.ExtractParams"/>).</summary>
    public struct OrderParams : IActionArgsCheck
    {
        public EntityLink Target;
        public FixedString64 Behavior;
        public int ArgsRef;
        public OrderParams() { Target = default; Behavior = default; ArgsRef = -1; }

        /// <summary>A behaviour name this host does not declare is refused at ASSIGNMENT, not after a 10 s wait. (A name longer than the field would be cut silently.)</summary>
        public readonly string? Check(JsonObject args)
        {
            foreach (var (k, v) in args)
            {
                if (!string.Equals(k, "behavior", StringComparison.OrdinalIgnoreCase)) continue;
                if (v is not JsonValue jv || !jv.TryGetValue<string>(out var name) || string.IsNullOrWhiteSpace(name)) return "'behavior' must be a behaviour name";
                if (name.Length > 63) return $"the behaviour name is {name.Length} characters; at most 63";
                if (!ParamsDtoRegistry.Default.TryGetBehavior(name, out _)) return $"unknown behaviour '{name}'";
                return null;
            }
            return "'behavior' is required";
        }
    }

    /// <summary>⭐ <c>Order</c>'s working state.</summary>
    public struct OrderState { public byte Began; public Fdp.Core.SimInstant IssuedAt; }

    /// <summary>What <c>MissionCommand</c> asks of the target's mission plan. ⛔ <c>Replace</c> is declared (SR-97 names it) but refused at assignment: a verb carries no plan.</summary>
    public enum MissionCommandKind : byte { Jump = 0, Replace = 1, Abort = 2 }

    /// <summary>⭐ <c>MissionCommand</c>'s params (rules S2 ⑨).</summary>
    public struct MissionCommandParams : IActionArgsCheck
    {
        public EntityLink Target;
        public MissionCommandKind Command;
        public int TaskIndex;

        public readonly string? Check(JsonObject args)
        {
            foreach (var (k, v) in args)
                if (string.Equals(k, "command", StringComparison.OrdinalIgnoreCase) && v is JsonValue jv && jv.TryGetValue<string>(out var c)
                    && string.Equals(c, nameof(MissionCommandKind.Replace), StringComparison.OrdinalIgnoreCase))
                    return "Replace needs a full mission plan, which this verb cannot carry yet (use Jump or Abort)";
            return null;
        }
    }

    /// <summary>⭐ <c>MissionCommand</c>'s working state: the request in flight.</summary>
    public struct MissionCommandState { public byte Began; public Fdp.Core.SimInstant IssuedAt; public Guid RequestId; }

    public static partial class ScenarioVerbs
    {
        /// <summary>How long (sim s) <c>Order</c> / <c>MissionCommand</c> wait for the target to take the order / for the ack.</summary>
        public const double AcceptTimeoutSeconds = 10.0;

        // ═══════════════════════════════════════ Order (SR-97) ═══════════════════════════════════════

        /// <summary>
        /// Orders <c>Target</c> to run <c>Behavior</c> (with the params JSON at <c>ArgsRef</c>) as its SUPERIOR — the same <see cref="AssignTacticalIntentEvent"/> a commander
        /// publishes (<c>CommanderNodes.Action_IssueTacticalIntent</c>). Running until the target's active behaviour IS the ordered one (hash), then Success;
        /// 10 s of sim time without that ⇒ Failure "not accepted" (the gate refused it, the target is not ours, no such behaviour here …).
        /// </summary>
        [SharedAiAction]
        public static NodeStatus Order(ref OrderParams p, ref OrderState s, Entity self, EntityRepository world)
        {
            double now = ObjectiveHost.Now(world);
            var target = p.Target.Ref.Resolve(world);
            if (target.IsNull || !world.IsAlive(target)) return Fail(world, self, "target not found");
            if (!world.IsComponentTypeRegistered<BehaviorState>() || !world.HasComponent<BehaviorState>(target)) return Fail(world, self, "target has no brain");
            string name = p.Behavior.ToString();
            if (s.Began == 0)
            {
                string json = string.Empty;
                if (p.ArgsRef >= 0)
                {
                    var items = world.HasManagedComponent<ActionArgs>(self) ? ((ISimulationView)world).GetManagedComponentRO<ActionArgs>(self).Items : null;
                    if (items == null || p.ArgsRef >= items.Count) return Fail(world, self, $"order params item {p.ArgsRef} is missing");
                    json = items[p.ArgsRef];
                }
                if (!world.Bus.IsRegisteredManaged<AssignTacticalIntentEvent>()) world.Bus.RegisterManaged<AssignTacticalIntentEvent>();
                world.Bus.PublishManaged(new AssignTacticalIntentEvent
                {
                    Entity = target, IntentId = name, JsonParams = json,
                    Origin = BehaviorOrigin.Superior,   // as a commander's order (CommanderNodes.cs:80)
                });
                s.Began = 1; s.IssuedAt = (SimInstant)now;
                return NodeStatus.Running;
            }
            if (world.GetComponentRO<BehaviorState>(target).ActiveBehaviorHash == BehaviorHash.FromName(name)) return NodeStatus.Success;
            if (now - s.IssuedAt >= AcceptTimeoutSeconds - 1e-9) return Fail(world, self, "not accepted");
            return NodeStatus.Running;
        }

        // ═══════════════════════════════════════ MissionCommand ═══════════════════════════════════════

        /// <summary>
        /// Jumps the target's mission plan to task <c>TaskIndex</c> (its <c>ActiveMissionPlan.TaskOrder</c>) or aborts it, through the ONE mission-control path
        /// (<see cref="MissionControlIntent"/> → <c>MissionControlExecutionSystem</c>, as <c>ScenarioMissionService</c> publishes it). Running until the
        /// <see cref="MissionControlAckEvent"/> with this request's id: ErrorCode 0 ⇒ Success, else Failure "mission control error N"; 10 s without ack ⇒ Failure.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus MissionCommand(ref MissionCommandParams p, ref MissionCommandState s, Entity self, EntityRepository world)
        {
            double now = ObjectiveHost.Now(world);
            if (s.Began == 0)
            {
                var target = p.Target.Ref.Resolve(world);
                if (target.IsNull || !world.IsAlive(target)) return Fail(world, self, "target not found");
                if (!world.IsComponentTypeRegistered<NetworkIdentity>() || !world.HasComponent<NetworkIdentity>(target)) return Fail(world, self, "target has no network id");
                var payload = new MissionCommandPayload();
                switch (p.Command)
                {
                    case MissionCommandKind.Abort: payload.CommandType = eMissionCommandType.CMD_ABORT_ALL; break;
                    case MissionCommandKind.Jump:
                    {
                        var order = world.HasManagedComponent<ActiveMissionPlan>(target) ? ((ISimulationView)world).GetManagedComponentRO<ActiveMissionPlan>(target)?.TaskOrder : null;
                        if (order == null || (uint)p.TaskIndex >= (uint)order.Count) return Fail(world, self, $"the target's mission has no task {p.TaskIndex}");
                        payload.CommandType = eMissionCommandType.CMD_JUMP_TO_TASK; payload.TargetTaskId = order[p.TaskIndex];
                        break;
                    }
                    default: return Fail(world, self, $"mission command {p.Command} is not supported");
                }
                s.RequestId = Guid.NewGuid();
                if (!world.Bus.IsRegisteredManaged<MissionControlIntent>()) world.Bus.RegisterManaged<MissionControlIntent>();
                if (!world.Bus.IsRegistered<MissionControlAckEvent>()) world.Bus.Register<MissionControlAckEvent>();
                world.Bus.PublishManaged(new MissionControlIntent
                {
                    RequestId = s.RequestId, TargetEntityId = world.GetComponentRO<NetworkIdentity>(target).Value, BaseVersion = 0, Payload = payload,
                });
                s.Began = 1; s.IssuedAt = (SimInstant)now;
                return NodeStatus.Running;
            }
            foreach (var ack in world.ReadEvents<MissionControlAckEvent>())
                if (ack.RequestId == s.RequestId)
                    return ack.ErrorCode == 0 ? NodeStatus.Success : Fail(world, self, $"mission control error {ack.ErrorCode}");
            if (now - s.IssuedAt >= AcceptTimeoutSeconds - 1e-9) return Fail(world, self, "no mission control ack");
            return NodeStatus.Running;
        }
    }
}
