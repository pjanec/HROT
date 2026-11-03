using System;
using Fdp.Core;
using Fdp.Toolkit.Replication.Attributes;
using Fdp.Toolkit.Replication.Patching;

namespace Fdp.Toolkit.ScenarioRules
{
    /// <summary>
    /// ⭐⭐ CE-4216 (rules view R2, RV-5) — <b>THE one way to ask a rule entity for something at run time</b>: the DebugApi's <c>POST /objectives/{id}/control</c> and <c>/edit</c> (behaviors' V-4 / ② precedent) and the instructor's rules view
    /// both end here, so there is one <c>Seq</c> rule and one write path — never a second copy. A request is written through the EXISTING attribute path
    /// (<see cref="EntityWriteRouter"/>: applied here when this node owns the entity, a change-request to the owner elsewhere); the owner's <c>ObjectiveHost.Enter</c> honours a <c>Seq</c> it has not seen ONCE and journals who asked.
    /// <para><c>Seq</c> = the last one seen on the entity + 1 (a node that holds no <see cref="RuleControl"/> keeps its own counter: the owner acts on any Seq it has not yet seen), wrapping at <see cref="int.MaxValue"/>.</para>
    /// </summary>
    public static class RuleCommands
    {
        /// <summary>The next sequence number for <paramref name="current"/> (the entity's last seen, else this caller's own counter).</summary>
        public static int Next(int current) => current == int.MaxValue ? 1 : current + 1;

        /// <summary>
        /// Asks <paramref name="entity"/> to do <paramref name="request"/>. <paramref name="lastSeq"/> is the caller's own counter (kept across calls, used when the entity holds no <see cref="RuleControl"/>).
        /// Returns how it was routed — <see cref="EntityWriteRoute.Refused"/> = nothing was written (this node has no way to ask the owner) — and the <paramref name="seq"/> used.
        /// </summary>
        public static EntityWriteRoute Control(EntityRepository world, Entity entity, RuleRequest request, RuleOrigin origin, ref int lastSeq, out int seq)
        {
            int current = world.IsComponentTypeRegistered<RuleControl>() && world.HasComponent<RuleControl>(entity) ? world.GetComponentRO<RuleControl>(entity).Seq : lastSeq;
            seq = Next(current);
            lastSeq = seq;
            return EntityWriteRouter.For(world).Write(entity, new[]
            {
                EntityAttributeChange.Double(AttributeIds.RuleRequest, (int)request),
                EntityAttributeChange.Double(AttributeIds.RuleOrigin, (int)origin),
                EntityAttributeChange.Double(AttributeIds.RuleSeq, seq),
            });
        }

        /// <summary>A LIVE EDIT of a rule's params (§00.11a), through the same path: <see cref="RuleEdit"/> (<c>Rule.Params</c> / <c>Rule.EditSeq</c> / <c>Rule.EditOrigin</c>). <paramref name="editOrigin"/> is a <c>BehaviorOrigin</c> value.</summary>
        public static EntityWriteRoute Edit(EntityRepository world, Entity entity, string paramsJson, int editOrigin, ref int lastSeq, out int seq)
        {
            int current = world.TryGetTable(typeof(RuleEdit), out _) && world.HasManagedComponent<RuleEdit>(entity)
                ? ((Fdp.ModuleHost.Abstractions.ISimulationView)world).GetManagedComponentRO<RuleEdit>(entity).Seq : lastSeq;
            seq = Next(current);
            lastSeq = seq;
            return EntityWriteRouter.For(world).Write(entity, new[]
            {
                new EntityAttributeChange { AttributeId = AttributeIds.RuleParams, Value = AttributeValue.FromString(paramsJson) },
                EntityAttributeChange.Double(AttributeIds.RuleEditOrigin, editOrigin),
                EntityAttributeChange.Double(AttributeIds.RuleEditSeq, seq),
            });
        }

        /// <summary>
        /// ⭐ RV-5 — why <paramref name="request"/> would be REFUSED on a rule in this status, or <see langword="null"/> when it is valid: a menu shows an invalid item disabled WITH this reason (never hidden — the menu's shape does not change
        /// when the state does). Mirrors the owner's own refusals (<c>ObjectiveHost.Enter</c>, <c>TriggerBehavior.Control</c>); the owner still has the last word.
        /// </summary>
        public static string? DisabledReason(in ObjectiveStatus s, RuleRequest request)
        {
            if (request == RuleRequest.NoRequest) return "nothing to ask";
            if (s.Closed != 0) return "closed by the exercise — final";
            if (request == RuleRequest.Reset) return null;                                   // a Reset restarts any rule, a final one too
            if (s.IsFinal) return $"it is {s.State} — needs Reset";
            bool trigger = s.Role == ObjectiveRole.Trigger;
            switch (request)
            {
                case RuleRequest.Fire:
                    if (!trigger) return "only a trigger fires";
                    if (s.State != ObjectiveState.Active) return "it is switched off";
                    return s.TriggerState is TriggerState.Disarmed or TriggerState.Acting or TriggerState.Fired or TriggerState.Done ? $"it is {s.TriggerState.ToString().ToLowerInvariant()}" : null;
                case RuleRequest.Rearm:
                    if (!trigger) return "only a trigger is re-armed";
                    return s.TriggerState == TriggerState.Acting ? "it is acting" : null;
                case RuleRequest.Disarm:
                case RuleRequest.Deactivate:
                    return s.State == ObjectiveState.Active ? null : "it is already off";
                case RuleRequest.Activate:
                    return s.State == ObjectiveState.Active ? "it is already active" : null;
                default:
                    return null;
            }
        }
    }
}
