using System;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// ⭐⭐ <c>CE-3494</c> (R-3119, <c>docs/DESIGN_Unit_Intent.md</c> §10) — THE ONE WRITER of a person's request into a unit's
    /// <see cref="UnitIntent"/>: the editor's intent section, the orders (<c>EntityOrders</c>) and the network ingress all call it, so a request is
    /// stored one way whatever sent it. Call it on the node that runs the unit's brain. A newer request REPLACES its slot (P3) and the replaced
    /// one is answered <i>refused — superseded</i> to the station that asked (P4). ⚠ Not on a hot path: a person's action.
    /// </summary>
    public static class UnitIntents
    {
        /// <summary>The answer a replaced request gets.</summary>
        public const string Superseded = "superseded by a later order before time advanced";

        /// <summary>A person's task: <paramref name="name"/> is a behaviour or an order (<c>Order.Move</c>); the slot is replaced.</summary>
        public static void RequestTask(EntityRepository repo, Entity entity, string name, string? json, BehaviorOrigin origin, Guid requestId)
            => Edit(repo, entity, i => i with
            {
                Behavior = new IntentSlot { Name = name ?? "", Params = string.IsNullOrWhiteSpace(json) ? "{}" : json!, Origin = origin, RequestId = requestId },
            }, requestId);

        /// <summary>A person's release: the running task ends when time advances; the SOP picks next.</summary>
        public static void RequestClear(EntityRepository repo, Entity entity, BehaviorOrigin origin, Guid requestId)
            => Edit(repo, entity, i => i with { Behavior = new IntentSlot { Clear = true, Origin = origin, RequestId = requestId } }, requestId);

        /// <summary>A person's SOP (assign) or its clear (<paramref name="name"/> null).</summary>
        public static void RequestSop(EntityRepository repo, Entity entity, string? name, string? json, BehaviorOrigin origin)
            => Edit(repo, entity, i => i with
            {
                Sop = name is null ? new IntentSlot { Clear = true, Origin = origin }
                                   : new IntentSlot { Name = name, Params = string.IsNullOrWhiteSpace(json) ? "{}" : json!, Origin = origin },
            }, Guid.Empty);

        /// <summary>A person's ROE change: each field it SETS over the pending one (a partial change keeps what it leaves unset — the rule
        /// <c>RoeSystem.Apply</c> applies to the running ROE).</summary>
        public static void RequestRoe(EntityRepository repo, in Events.SetRoeEvent change)
        {
            var c = change;
            Edit(repo, c.Entity, i =>
            {
                var r = i.Roe ?? new SavedRoe();
                return i with
                {
                    Roe = r with
                    {
                        Fire = c.Fire != RoeFire.FireUnset ? c.Fire : r.Fire,
                        Reactions = c.Reactions != RoeReactions.ReactionsUnset ? c.Reactions : r.Reactions,
                        ReturnFireWindowSeconds = c.ReturnFireWindowSeconds > 0f ? c.ReturnFireWindowSeconds : r.ReturnFireWindowSeconds,
                        ForceLevel = c.ForceLevel != RoeForceLevel.ForceUnset ? c.ForceLevel : r.ForceLevel,
                        Incendiary = c.Incendiary != RoeIncendiary.IncendiaryUnset ? c.Incendiary : r.Incendiary,
                        SetBy = c.Origin,
                    },
                };
            }, Guid.Empty);
        }

        /// <summary>The pending intent, or null.</summary>
        public static UnitIntent? Pending(EntityRepository repo, Entity entity)
            => repo.TryGetTable(typeof(UnitIntent), out _) && repo.IsAlive(entity) && repo.HasManagedComponent<UnitIntent>(entity)
                ? ((Fdp.ModuleHost.Abstractions.ISimulationView)repo).GetManagedComponentRO<UnitIntent>(entity) : null;

        /// <summary>
        /// Sets the unit's pending intent to <paramref name="next"/> (null, or nothing in it = none) — the one store every writer goes through,
        /// the editor's undo included. A task slot it replaces that carried a station's request is answered <see cref="Superseded"/>.
        /// </summary>
        public static void Replace(EntityRepository repo, Entity entity, UnitIntent? next)
        {
            if (!repo.IsAlive(entity)) return;
            if (!repo.TryGetTable(typeof(UnitIntent), out _)) repo.RegisterManagedComponent<UnitIntent>();
            var before = Pending(repo, entity);
            // P3/P4 — the request this one replaces is answered (a release replacing an order, an order replacing an order)
            if (before?.Behavior is { } old && !ReferenceEquals(old, next?.Behavior) && old.RequestId != Guid.Empty && old.RequestId != next?.Behavior?.RequestId)
                Fdp.Toolkit.Replication.Edits.InteractionOutcomes.Refused(repo, old.RequestId, entity, Superseded);
            if (next is null || (next.Behavior is null && next.Sop is null && next.Roe is null))
            {
                if (before != null) repo.RemoveComponent<UnitIntent>(entity);
                return;
            }
            repo.SetManagedComponent(entity, next);   // a NEW record — never mutated in place (CLAUDE.md "NEW COMPONENTS")
        }

        private static void Edit(EntityRepository repo, Entity entity, Func<UnitIntent, UnitIntent> edit, Guid requestId)
        {
            if (!repo.IsAlive(entity)) return;
            Replace(repo, entity, edit(Pending(repo, entity) ?? new UnitIntent()));
        }
    }
}
