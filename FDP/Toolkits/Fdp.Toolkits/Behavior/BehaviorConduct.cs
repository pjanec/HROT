using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-4621</c> M-3 (CD-1, R-4263; 📄 <c>docs/designs/vehicle-tactics/DESIGN_Motorcycle.md</c> §8a) — what a REACTION does, as a
    /// CATEGORY its author declares once (<c>[Behavior(Conduct = …)]</c>), so an order can forbid it without naming a behaviour.
    /// ⭐ Firing is not a conduct (CD-6): a reaction that only shoots declares none and is never refused.
    /// </summary>
    [Flags]
    public enum Conduct : byte
    {
        None        = 0,
        /// <summary>It stops the unit (a contact drill, a fight from cover).</summary>
        Halts       = 1,
        /// <summary>It puts men out of their vehicle.</summary>
        Dismounts   = 2,
        /// <summary>It abandons the unit's route or held position.</summary>
        LeavesRoute = 4,
        /// <summary>It sends the unit to cover.</summary>
        SeeksCover  = 8,
        All         = Halts | Dismounts | LeavesRoute | SeeksCover,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4621</c> M-3 (CD-2…CD-5, R-4263) — the ONE table the gate asks: what each reaction DOES and what each order behaviour
    /// FORBIDS, both keyed by the behaviour's registry hash (<see cref="BehaviorHash.FromName"/> — the same in every process and node).
    /// <para>⭐ The Hrot layer fills it ONCE at load (its order kinds live there — <c>OrderConduct</c>, beside <c>OrderKind</c>); an FDP-only
    /// host that installs nothing forbids nothing. ⭐ Copy-on-write and immutable after a merge ⇒ the gate reads it lock-free from the parallel
    /// brain phase (CLAUDE.md: no shared mutable statics in a step — a write happens only at load).</para>
    /// </summary>
    public static class BehaviorConduct
    {
        private sealed class Table
        {
            public readonly Dictionary<int, Conduct> Does;
            public readonly Dictionary<int, Conduct> Forbids;
            public Table(Dictionary<int, Conduct> does, Dictionary<int, Conduct> forbids) { Does = does; Forbids = forbids; }
        }

        private static volatile Table s_table = new(new Dictionary<int, Conduct>(), new Dictionary<int, Conduct>());
        private static readonly object Gate = new();

        /// <summary>Merge declarations into the table (a later entry for the same hash replaces the earlier). Load time only.</summary>
        public static void Install(IEnumerable<KeyValuePair<int, Conduct>> does, IEnumerable<KeyValuePair<int, Conduct>> forbids)
        {
            lock (Gate)
            {
                var d = new Dictionary<int, Conduct>(s_table.Does);
                var f = new Dictionary<int, Conduct>(s_table.Forbids);
                foreach (var kv in does) { if (kv.Value == Conduct.None) d.Remove(kv.Key); else d[kv.Key] = kv.Value; }
                foreach (var kv in forbids) { if (kv.Value == Conduct.None) f.Remove(kv.Key); else f[kv.Key] = kv.Value; }
                s_table = new Table(d, f);
            }
        }

        /// <summary>What the behaviour <paramref name="behaviorHash"/> does when it runs (CD-1); <see cref="Conduct.None"/> when undeclared.</summary>
        public static Conduct Of(int behaviorHash)
            => s_table.Does.TryGetValue(behaviorHash, out var c) ? c : Conduct.None;

        /// <summary>What a unit running the order behaviour <paramref name="orderHash"/> may not do (CD-2).</summary>
        public static Conduct ForbiddenBy(int orderHash)
            => orderHash != BehaviorIds.None && s_table.Forbids.TryGetValue(orderHash, out var c) ? c : Conduct.None;

        /// <summary>
        /// ⭐ The behaviour hash of the unit's current ORDER: the running task when a superior or an operator started it; the task a reaction
        /// paused when that was such an order; else none. ⚠ The same rule as Hrot's <c>CurrentOrders.Of</c> (which also needs the request id).
        /// </summary>
        public static int OrderHashOf(EntityRepository repo, Entity unit, in BehaviorState running)
        {
            if (running.ActiveBehaviorHash != BehaviorIds.None && IsOrderOrigin(running.Origin)) return running.ActiveBehaviorHash;
            if (running.Origin == BehaviorOrigin.Reaction && repo.TryGetTable(typeof(PausedTask), out _)
                && BehaviorIngressSystem.PausedTaskOf(repo, unit) is { } paused && IsOrderOrigin(paused.Origin))
                return paused.Hash;
            return BehaviorIds.None;
        }

        private static bool IsOrderOrigin(BehaviorOrigin origin) => origin == BehaviorOrigin.Superior || origin == BehaviorOrigin.Operator;

        /// <summary>
        /// ⭐⭐ CD-3 — does the unit's current order forbid what the reaction <paramref name="reactionHash"/> does? ⭐ CD-5: the forbidding yields
        /// when the unit cannot comply (<see cref="CannotComply"/>). Allocation-free.
        /// </summary>
        public static bool Forbids(EntityRepository repo, Entity unit, in BehaviorState running, int reactionHash)
        {
            if (reactionHash == BehaviorIds.None) return false;
            var table = s_table;
            if (table.Forbids.Count == 0 || !table.Does.TryGetValue(reactionHash, out var does)) return false;
            int order = OrderHashOf(repo, unit, running);
            if (order == BehaviorIds.None || !table.Forbids.TryGetValue(order, out var forbidden)) return false;
            if ((does & forbidden) == 0) return false;
            return !CannotComply(repo, unit);
        }

        /// <summary>
        /// ⭐ CD-5 — the unit cannot carry its order on: it cannot move itself (a mobility kill, and it rides in nothing), it rides a carrier that
        /// is dead or cannot move, or — a unit with a roster — a live member rides such a carrier (a disabled bike's men must get off).
        /// </summary>
        public static bool CannotComply(EntityRepository repo, Entity unit)
        {
            var carrier = CarrierOf(repo, unit);
            if (!carrier.IsNull) return !Mobile(repo, carrier);
            if (repo.HasComponent<ActorCapabilityState>(unit)
                && (repo.GetComponentRO<ActorCapabilityState>(unit).Capabilities & ActorCapabilities.CanMove) == 0)
                return true;
            if (!repo.HasComponent<UnitRoster>(unit)) return false;
            var roster = repo.GetComponentRO<UnitRoster>(unit);
            for (int i = 0; i < roster.Count; i++)
            {
                var member = roster.SubordinateEntities[i];
                if (!repo.IsAlive(member) || !Fdp.Toolkit.Combat.CombatLife.IsAlive(repo, member)) continue;
                var ride = CarrierOf(repo, member);
                if (!ride.IsNull && !Mobile(repo, ride)) return true;
            }
            return false;
        }

        private static Entity CarrierOf(EntityRepository repo, Entity e)
            => repo.IsAlive(e) && repo.IsComponentTypeRegistered<IsEmbarkedTag>() && repo.HasComponent<IsEmbarkedTag>(e)
                ? repo.GetComponentRO<IsEmbarkedTag>(e).VehicleEntity
                : Entity.Null;

        private static bool Mobile(EntityRepository repo, Entity vehicle)
            => repo.IsAlive(vehicle) && Fdp.Toolkit.Combat.CombatLife.IsAlive(repo, vehicle)
               && (!repo.HasComponent<ActorCapabilityState>(vehicle)
                   || (repo.GetComponentRO<ActorCapabilityState>(vehicle).Capabilities & ActorCapabilities.CanMove) != 0);
    }
}
