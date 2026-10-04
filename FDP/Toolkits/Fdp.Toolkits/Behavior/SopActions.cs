using System.Text.Json;
using Fbt;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// ⭐⭐ <c>CE-2079</c> — the TWO things an SOP does, in ONE implementation every authoring surface calls (C#, the BTree
    /// <c>SopOrder</c> node's generated thunk, later a blueprint node). 📄 <c>docs/DESIGN_Decision_Layer.md</c> §4.6.
    /// <list type="bullet">
    /// <item><see cref="DoWhenIdle(EntityRepository, Entity, string, string)"/> — what the unit does when it has no order:
    ///   an assignment at <see cref="BehaviorOrigin.Sop"/>, the lowest rank.</item>
    /// <item><see cref="React(EntityRepository, Entity, string, ReactionUrgency, string)"/> — answer an event: an assignment at
    ///   <see cref="BehaviorOrigin.Reaction"/>, which pauses the task (R-199, the gate's rules in
    ///   <see cref="BehaviorIngressSystem.AdmitsWithReactions"/>).</item>
    /// </list>
    /// <para>⭐ Both are INSTANT — <see cref="NodeStatus.Success"/> or <see cref="NodeStatus.Failure"/>, never Running — so the
    /// SOP tree finishes every wake (§4.3). ⭐ Neither publishes what the gate would refuse: the gate is asked first, and a
    /// refusal is <see cref="NodeStatus.Failure"/> (a Selector tries its next row). ⚠ That is load-bearing — a refused
    /// Sop-origin assignment WAKES the SOP (R-195), so publishing blindly under a running order would wake it every frame.</para>
    /// <para>⭐ Params serialise with <see cref="BehaviorParams.JsonOptions"/> — the ONE options object every parse path
    /// reads with — so the two directions cannot drift.</para>
    /// </summary>
    public static class SopActions
    {
        /// <summary>The unit's idle choice: run <paramref name="behavior"/> with <paramref name="json"/> unless an order or a
        /// reaction holds the task slot. ⭐ Already running it with the same params ⇒ Success, nothing published (the SOP
        /// re-reads its tree every 0.2 s; a re-publish would restart it).</summary>
        public static NodeStatus DoWhenIdle(EntityRepository world, Entity self, string behavior, string json)
        {
            json = Normalize(json);
            if (!world.HasComponent<BehaviorState>(self)) return NodeStatus.Failure;
            ref readonly var task = ref world.GetComponentRO<BehaviorState>(self);
            if (task.ActiveBehaviorHash != BehaviorIds.None && task.Origin == BehaviorOrigin.Sop
                && RunningIs(world, self, task, behavior, json))
                return NodeStatus.Success;
            if (!BehaviorIngressSystem.AdmitsWithReactions(world, self, BehaviorOrigin.Sop, ReactionUrgency.NotAReaction, task, out _))
                return NodeStatus.Failure;
            return Publish(world, self, behavior, json, BehaviorOrigin.Sop, ReactionUrgency.NotAReaction);
        }

        /// <summary>Answer an event with <paramref name="behavior"/> at <paramref name="urgency"/>: it pauses the task unless the
        /// ROE says <see cref="RoeReactions.StayOnTask"/>, and a running reaction yields only to a more urgent one. ⭐ Already
        /// running it as a reaction ⇒ Success, nothing published (the trigger stays true for seconds after a hit).</summary>
        public static NodeStatus React(EntityRepository world, Entity self, string behavior, ReactionUrgency urgency, string json)
        {
            json = Normalize(json);
            if (!world.HasComponent<BehaviorState>(self)) return NodeStatus.Failure;
            ref readonly var task = ref world.GetComponentRO<BehaviorState>(self);
            if (task.ActiveBehaviorHash != BehaviorIds.None && task.Origin == BehaviorOrigin.Reaction
                && RunningIs(world, self, task, behavior, json: null))
                return NodeStatus.Success;
            if (!BehaviorIngressSystem.AdmitsWithReactions(world, self, BehaviorOrigin.Reaction, urgency, task, out _))
                return NodeStatus.Failure;
            return Publish(world, self, behavior, json, BehaviorOrigin.Reaction, urgency);
        }

        /// <summary><see cref="DoWhenIdle(EntityRepository, Entity, string, string)"/> with the behaviour's authored params DTO.</summary>
        public static NodeStatus DoWhenIdle<TParams>(EntityRepository world, Entity self, string behavior, in TParams parameters)
            => DoWhenIdle(world, self, behavior, ToJson(parameters));

        /// <summary><see cref="React(EntityRepository, Entity, string, ReactionUrgency, string)"/> with the behaviour's authored
        /// params DTO.</summary>
        public static NodeStatus React<TParams>(EntityRepository world, Entity self, string behavior, ReactionUrgency urgency,
            in TParams parameters)
            => React(world, self, behavior, urgency, ToJson(parameters));

        /// <summary>⭐ The params DTO as the JSON an assignment carries — the exact inverse of the parse side (same options).</summary>
        public static string ToJson<TParams>(in TParams parameters)
            => JsonSerializer.Serialize(parameters, BehaviorParams.JsonOptions);

        private static string Normalize(string? json) => string.IsNullOrWhiteSpace(json) ? "{}" : json!;

        /// <summary>Does the task slot run <paramref name="behavior"/> (and, when given, with <paramref name="json"/>)? Read from
        /// the start record of THIS run — a name, so no registry is needed.</summary>
        private static bool RunningIs(EntityRepository world, Entity self, in BehaviorState task, string behavior, string? json)
        {
            if (!world.HasManagedComponent<BehaviorStartRecord>(self)) return false;
            var record = ((ISimulationView)world).GetManagedComponentRO<BehaviorStartRecord>(self);
            if (record == null || record.InstanceId != task.InstanceId || record.BehaviorName != behavior) return false;
            return json == null || Normalize(record.JsonParams) == json;
        }

        private static NodeStatus Publish(EntityRepository world, Entity self, string behavior, string json,
            BehaviorOrigin origin, ReactionUrgency urgency)
        {
            if (!world.Bus.IsRegisteredManaged<AssignBehaviorEvent>()) return NodeStatus.Failure;
            world.Bus.PublishManaged(new AssignBehaviorEvent
            {
                Entity = self, BehaviorName = behavior, JsonParams = json, Origin = origin, Urgency = urgency,
            });
            return NodeStatus.Success;
        }
    }
}
