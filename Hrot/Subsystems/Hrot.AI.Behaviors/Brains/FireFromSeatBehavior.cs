using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-3454</c> (📄 docs/designs/vehicle-tactics/DESIGN_Motorcycle.md PF-3: "the passenger's own weapon brain decides to fire") —
    /// <c>FireFromSeat</c>, the reaction of a man in an open FIRING seat (<see cref="Embarkation.IsFiringSeat"/>: the pillion, a mounted
    /// archer, a pickup-bed rifleman): he fires at the enemy he sees INSIDE his seat's arc, from the seat — no cover search, no move (the
    /// vehicle's driver decides where they go). The weapon path still bounds every shot: the arc, the ROE, the stopped-only rider seat
    /// (<c>AimAndFireExecutor.SeatHold</c> / <c>HoldOf</c>). Ends (Success) after <see cref="QuietSeconds"/> with no enemy seen in the arc,
    /// so the SOP's idle resumes. 📐 Before: the SOP's "Aboard → the vehicle decides" row stood every passenger aside — the war-horse rider
    /// held two targets in memory for 45 s and never fired (<c>ua-animal-horse-rider</c>); the swarm-raid pillions never fired.
    /// </summary>
    public static class FireFromSeatBehavior
    {
        /// <summary>Seconds with no enemy seen in the arc before the reaction ends.</summary>
        public const float QuietSeconds = 3f;
        /// <summary>Seconds between shots (the AimAndFire executor's cooldown) — the posture's own number.</summary>
        public const float CooldownSeconds = 1f;

        [StructLayout(LayoutKind.Sequential)]
        public struct FireFromSeatState
        {
            public EngageState Fire;
            /// <summary>When an enemy was last seen in the arc (sim s).</summary>
            public SimInstant LastSeen;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FireFromSeatBlackboard
        {
            public EngageParams Engage;
            public FireFromSeatState State;
        }

        /// <summary>No params (CE-2132): a payload with fields is refused.</summary>
        [BehaviorResolver(BehaviorNames.FireFromSeat)]
        public static unsafe void Resolve(string json, byte* memory, int capacity, EntityRepository world, Entity self)
            => BehaviorParams.RequireNoStoredFields(json, BehaviorNames.FireFromSeat, typeof(NoParams).FullName!);

        [Behavior(BehaviorNames.FireFromSeat, Params = typeof(NoParams), Categories = BehaviorCategory.AllMilitary,
            Description = "From a firing seat, fires at an enemy it sees inside the seat's arc.")]
        [BTreeDefinition(BehaviorNames.FireFromSeat, Curated = true, Group = "Library/Combat", ParamsType = typeof(FireFromSeatBlackboard))]
        public static BTreeBuilder<FireFromSeatBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<FireFromSeatBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Engage, bb => bb.State, Run);

        /// <summary>One tick: the target in the arc (kept while it qualifies), fired at; Success once quiet. Failure off a firing seat.</summary>
        [SharedAiAction]
        public static NodeStatus Run(ref EngageParams p, ref FireFromSeatState ws, Entity self, EntityRepository world)
        {
            if (!OnFiringSeat(self, world)) { PostureNodes.StopFiring(world, self, ref ws.Fire); return NodeStatus.Failure; }
            double now = world.SimulationTime > 0 ? world.SimulationTime : 1e-6;
            if (ws.LastSeen <= 0) ws.LastSeen = (SimInstant)now;
            var target = TargetInArc(self, world, ws.Fire.Threat);
            if (target.IsNull)
            {
                PostureNodes.StopFiring(world, self, ref ws.Fire);
                return now - ws.LastSeen >= QuietSeconds ? NodeStatus.Success : NodeStatus.Running;
            }
            ws.LastSeen = (SimInstant)now;
            return PostureNodes.Fire(world, self, ref ws.Fire, CooldownSeconds, 0, target) ? NodeStatus.Running : NodeStatus.Failure;
        }

        /// <summary>Leaving the reaction (an order, the SOP's gate): the weapon it aimed stops.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.FireFromSeatBehavior.Run")]
        public static void Deactivate_Run(ref EngageParams p, ref FireFromSeatState ws, Entity self, EntityRepository world)
        {
            PostureNodes.StopFiring(world, self, ref ws.Fire);
            ws = default;
        }

        /// <summary>He sits aboard in a FIRING seat (PF-1) — not a closed hull's passenger, not a weapon's gunner. ⭐ Read from HIS OWN state: boarding
        /// strips <c>CanShoot</c> everywhere but a firing seat or a weapon's gunner seat (<see cref="Embarkation.StrippedOn"/>), so a man aboard who
        /// can still shoot sits in one of those two; the weapon's gunner keeps his own rows (CE-4035 G-11). 📐 Reading the host's stations here
        /// threw under <c>HROT_READ_CHECK=1</c> on the spawn frame (the host is not yet in the tick-start view) — every scenario with a rider.</summary>
        [SharedAiCondition]
        public static bool OnFiringSeat(Entity self, EntityRepository world)
        {
            if (!world.IsComponentTypeRegistered<IsEmbarkedTag>() || !world.HasComponent<IsEmbarkedTag>(self)) return false;
            if (!world.HasComponent<ActorCapabilityState>(self)
                || (world.GetComponentRO<ActorCapabilityState>(self).Capabilities & ActorCapabilities.CanShoot) == 0) return false;
            return !WeaponSystems.IsWeaponSystem(world, world.GetComponentRO<IsEmbarkedTag>(self).VehicleEntity);
        }

        /// <summary>The SOP row's condition: on a firing seat AND an enemy seen (within 1 s) inside the seat's arc.</summary>
        [SharedAiCondition]
        public static bool EnemyInArc(Entity self, EntityRepository world)
            => OnFiringSeat(self, world) && !TargetInArc(self, world, Entity.Null).IsNull;

        /// <summary>
        /// The enemy to fire at: <paramref name="current"/> while it still qualifies (no flip-flop), else the first qualifying memory slot
        /// (slots are score-ordered). Qualifies: identified, SEEN within <see cref="CoverFightConditions.InSightTicks"/>, alive, in the fight,
        /// hostile, his sector allows it, and — where his memory places it — inside his seat's arc. ⭐ Reads only his own memory and the
        /// seat's arc (his host's stations and transform, last tick). No allocation.
        /// </summary>
        public static unsafe Entity TargetInArc(Entity self, EntityRepository world, Entity current)
        {
            if (!world.IsComponentTypeRegistered<TargetMemory>() || !world.HasComponent<TargetMemory>(self)) return Entity.Null;
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(self);
            uint now = PerceptionClock.Now(world);
            Entity first = Entity.Null;
            for (int i = 0; i < mem.Count && i < PerceptionConstants.MaxTrackedTargets; i++)
            {
                if (mem.EntityIds[i] <= 0 || TargetMemory.IsAnonymous(in mem, i)) continue;
                if ((mem.Modalities[i] & (byte)SensorModality.Visual) == 0) continue;
                if (mem.LastSeenTick[i] <= now && now - mem.LastSeenTick[i] > CoverFightConditions.InSightTicks) continue;
                var e = new Entity((ulong)mem.EntityIds[i]);
                if (!world.IsAlive(e) || !Injury.IsInTheFight(world, e) || !KillZone.Hostile(world, self, e)) continue;
                if (!EngagementAreas.Allows(world, self, e)) continue;
                if (!WeaponSystems.InStationArc(world, self, new Vector3(mem.PositionsX[i], mem.PositionsY[i], mem.PositionsZ[i]))) continue;
                if (e.Equals(current)) return e;
                if (first.IsNull) first = e;
            }
            return first;
        }
    }
}
