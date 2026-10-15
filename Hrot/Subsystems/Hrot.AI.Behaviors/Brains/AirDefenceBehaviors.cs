using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-2165</c> V8 / <c>CE-2191</c> (Air Ops §7f) — the ONE "an aircraft is about" sense and the ONE aimed fire at it, shared by
    /// <see cref="ShelterFromAirNodes"/> (a vehicle that shelters and shoots back) and <see cref="AirDefenceNodes"/> (a unit that stands
    /// and engages).
    /// </summary>
    public static class AirSense
    {
        /// <summary>A failed fire (out of arc, no weapon for it) is retried after this (s) — not every frame.</summary>
        public const double RetryFireSeconds = 2.0;

        /// <summary>
        /// The newest air contact in memory (<see cref="AirContacts"/>: a flyer by its <c>Locomotion</c>, heard rotors — the sense's own rule):
        /// its position and slot out, and a newer sighting / hearing than <paramref name="airTick"/> moves <paramref name="lastAirAt"/> to
        /// now. Returns the aircraft when it is an identified one (Null for heard rotors, or nothing in the air).
        /// </summary>
        public static unsafe Entity Newest(EntityRepository world, Entity self, double now, ref uint airTick, ref double lastAirAt,
                                           out Vector3 at, out long slot)
        {
            at = default; slot = 0;
            if (!world.HasComponent<TargetMemory>(self)) return Entity.Null;
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(self);
            uint newest = 0; int found = -1;
            for (int i = 0; i < mem.Count; i++)
            {
                if (!AirContacts.IsAir(world, in mem, i) || mem.LastSeenTick[i] < newest) continue;
                // ⭐ CE-2191 — a downed aircraft (Health ≤ 0: it falls and rests as a wreck, AT3) is no longer about: nobody shelters from
                //   it or fires at it (rail-found: EngageAircraft kept the ZU-23 on a dead helicopter)
                if (!TargetMemory.IsAnonymous(in mem, i) && !CombatLife.IsAlive(world, new Entity((ulong)mem.EntityIds[i]))) continue;
                newest = mem.LastSeenTick[i];
                found = i;
            }
            if (found < 0) return Entity.Null;
            at = new Vector3(mem.PositionsX[found], mem.PositionsY[found], mem.PositionsZ[found]);
            slot = mem.EntityIds[found];
            if (newest > airTick || airTick == 0) { airTick = newest; lastAirAt = now; }
            return TargetMemory.IsAnonymous(in mem, found) ? Entity.Null : new Entity((ulong)mem.EntityIds[found]);
        }

        /// <summary>Aimed fire at <paramref name="air"/> while it is SEEN — the weapon brain picks the mount (a MANPADS at an aircraft,
        /// the ZU-23, an HMG on a pintle); a failure (out of arc) waits <see cref="RetryFireSeconds"/> before the next try; unseen ⇒ stop.</summary>
        public static void FireAt(EntityRepository world, Entity self, ref EngageState fire, ref double retryAt, Entity air, double now)
        {
            if (!world.HasComponent<WeaponChannel>(self)) return;
            bool seen = !air.IsNull && SightNow.Sees(world, self, air);
            if (!seen) { PostureNodes.StopFiring(world, self, ref fire); return; }
            ref readonly var channel = ref world.GetComponentRO<WeaponChannel>(self);
            bool mine = !fire.Threat.IsNull && channel.ActiveAction == CombatConstants.ActionIdAimAndFire;
            if (mine && channel.Status == NodeStatus.Failure)
            {
                PostureNodes.StopFiring(world, self, ref fire);
                retryAt = now + RetryFireSeconds;
                return;
            }
            if (now < retryAt) return;
            PostureNodes.Fire(world, self, ref fire, 0f, 0, air);
        }
    }

    /// <summary>⭐ <c>CE-2191</c> (Air Ops §7f) — the parameters of <see cref="AirDefenceNodes"/>; a zero means the default.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct EngageAircraftParams
    {
        /// <summary>Done once no air contact has been sensed for this long (s; default 10).</summary>
        public float ClearSeconds;
        /// <summary>A gun counts as an air-defence gun from this elevation up (degrees; default 80 — the ZU-23-2's 90°, not an M2's 70°).</summary>
        public float MinElevationDeg;
    }

    /// <summary>⭐ <c>CE-2191</c> — the working state of <see cref="AirDefenceNodes.EngageAircraft"/> (scratch, lost on every exit).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct EngageAircraftState
    {
        public uint AirTick;
        public long AirSlot;
        public Vector3 AirAt;
        public double StartedAt, LastAirAt, RetryFireAt;
        public EngageState Fire;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2191</c> (📄 docs/designs/air-attack/DESIGN_Air_Operations.md §7f) — AIR DEFENCE on the ground: a unit that carries or
    /// controls an air-defence weapon (<see cref="CanEngageAir(EntityRepository, Entity, float)"/>) and has an aircraft about STANDS and fires at the newest one it SEES
    /// (<see cref="AirSense"/> — the weapon brain picks the launcher or the gun; a MANPADS locks on its aim time while the gunner keeps it in
    /// sight). It never moves. Success once no air contact has been sensed for <c>ClearSeconds</c>.
    /// </summary>
    public static class AirDefenceNodes
    {
        public static readonly EngageAircraftParams Defaults = new() { ClearSeconds = 10f, MinElevationDeg = 80f };

        public static EngageAircraftParams Effective(in EngageAircraftParams a)
        {
            var e = a;
            if (e.ClearSeconds <= 0f) e.ClearSeconds = Defaults.ClearSeconds;
            if (e.MinElevationDeg <= 0f) e.MinElevationDeg = Defaults.MinElevationDeg;
            return e;
        }

        /// <summary>The SOP row's gate: can this unit fire at an aircraft with an air-defence weapon?</summary>
        [SharedAiCondition]
        public static bool CanEngageAir(ref EngageAircraftParams p, Entity self, EntityRepository world)
            => CanEngageAir(world, self, Effective(in p).MinElevationDeg);

        /// <summary>
        /// True when <paramref name="unit"/> carries or controls (<see cref="WeaponSystems.Controlled"/>) a weapon able to engage aircraft: a
        /// <see cref="WeaponCategory.Sam"/> launcher whose missile may be fired at the Air domain (Air or Either), or a gun whose turret
        /// elevates to <paramref name="minElevationDeg"/>. A dispenser, a rifle, an ATGM, an M2 on its 70° pintle do not count.
        /// </summary>
        public static bool CanEngageAir(EntityRepository world, Entity unit, float minElevationDeg)
        {
            if (!world.IsAlive(unit)) return false;
            for (int i = 0; i < AirAttackProfiles.MaxMounts; i++)
            {
                var m = CombatTkb.MountOf(world, unit, i);
                if (m == null) break;
                if (AirCapable(world, unit, m, minElevationDeg)) return true;
            }
            Span<Entity> controlled = stackalloc Entity[WeaponSystems.MaxControlled];
            int n = WeaponSystems.Controlled(world, unit, controlled);
            for (int k = 0; k < n; k++)
            {
                if (WeaponSystems.IsBroken(world, controlled[k])) continue;
                var m = CombatTkb.MountOf(world, controlled[k], 0);
                if (m != null && AirCapable(world, controlled[k], m, minElevationDeg)) return true;
            }
            return false;
        }

        private static bool AirCapable(EntityRepository world, Entity owner, WeaponMountDto m, float minElevationDeg)
        {
            if (m.Category == WeaponCategory.Countermeasure) return false;
            if (m.Category == WeaponCategory.Sam)
                return CombatTkb.MissileOf(world, unchecked((long)m.AmmoGuid)) is not { } missile || missile.TargetDomain != TargetDomain.Ground;
            if (m.Category is not (WeaponCategory.MachineGun or WeaponCategory.Autocannon)) return false;
            return m.TurretIndex >= 0 && CombatTkb.TurretOf(world, owner, m.TurretIndex) is { } turret && turret.ElevationMaxDeg >= minElevationDeg;
        }

        /// <summary>Running while an aircraft is about (Success when the sky has been clear for <c>ClearSeconds</c>).</summary>
        [SharedAiAction]
        public static NodeStatus EngageAircraft(ref EngageAircraftParams authored, ref EngageAircraftState ws, Entity self, EntityRepository world)
        {
            var p = Effective(in authored);
            double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d;
            if (ws.StartedAt == 0d) { ws.StartedAt = now; ws.LastAirAt = now; }
            var air = AirSense.Newest(world, self, now, ref ws.AirTick, ref ws.LastAirAt, out ws.AirAt, out ws.AirSlot);
            if (now - ws.LastAirAt >= p.ClearSeconds)
            {
                Release(ref ws, self, world);
                return NodeStatus.Success;   // the sky is clear
            }
            AirSense.FireAt(world, self, ref ws.Fire, ref ws.RetryFireAt, air, now);
            return NodeStatus.Running;
        }

        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.AirDefenceNodes.EngageAircraft")]
        public static void Deactivate_EngageAircraft(ref EngageAircraftParams p, ref EngageAircraftState ws, Entity self, EntityRepository world)
            => Release(ref ws, self, world);

        private static void Release(ref EngageAircraftState ws, Entity self, EntityRepository world)
        {
            PostureNodes.StopFiring(world, self, ref ws.Fire);
            ws = default;
        }
    }

    /// <summary>⭐ <c>CE-2191</c> — the library behaviour <c>EngageAircraft</c> (a reaction: the SOPs' air-defence row).</summary>
    public static class EngageAircraftBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct EngageAircraftBlackboard
        {
            public EngageAircraftParams Engage;
            public EngageAircraftState EngageState;
        }

        [BehaviorResolver(BehaviorNames.EngageAircraft)]
        public static void Resolve(in NoParams authored, ref EngageAircraftBlackboard block, EntityRepository world, Entity self)
            => block = default;

        [Behavior(BehaviorNames.EngageAircraft, Params = typeof(NoParams), Categories = BehaviorCategory.AllMilitary)]   // it takes nothing: the defaults
        [BTreeDefinition(BehaviorNames.EngageAircraft, Curated = true, Group = "Library/Combat", ParamsType = typeof(EngageAircraftBlackboard))]
        public static BTreeBuilder<EngageAircraftBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<EngageAircraftBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Engage, bb => bb.EngageState, AirDefenceNodes.EngageAircraft);
    }
}
