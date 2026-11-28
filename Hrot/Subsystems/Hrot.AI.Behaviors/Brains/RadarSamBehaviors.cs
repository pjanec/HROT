using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Radar;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-4617</c> (B-3, RS-5) — the parameters of <see cref="RadarSamNodes.RadarSamEngage"/>; a zero means the default.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RadarSamParams
    {
        /// <summary>Relocate once no aircraft has been tracked for this long (s; default 20).</summary>
        public SimDuration QuietSeconds;
        /// <summary>After the salvo's last launch the outcome is known within this (s; default 18 — the missile's flight).</summary>
        public SimDuration OutcomeSeconds;
        /// <summary>How far it relocates (m; default 350, inside the design's 200–500).</summary>
        public float RelocateM;
        /// <summary>The move gives up after this (s; default 90).</summary>
        public SimDuration RelocateSeconds;
        /// <summary>Its speed while relocating (m/s; default 8).</summary>
        public float RelocateSpeed;
    }

    /// <summary>⭐ <c>CE-4617</c> — the working state of <see cref="RadarSamNodes.RadarSamEngage"/> (scratch, lost on every exit).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RadarSamState
    {
        public byte Stage, Moving;
        public int Salvo;
        public Entity Target;
        public uint AirTick;
        public long AirSlot;
        public Vector3 AirAt;
        public float RangeM;
        public SimInstant StartedAt, LastTrackAt, LastAirAt, LaunchedAt, RelocateAt, RetryFireAt;
        public EngageState Fire;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4617</c> (B-3, RS-4 / RS-5 / RV-10 / RV-11; 📄 docs/designs/air-attack/DESIGN_Radar_SAM.md) — the RADAR SAM's engagement: cued
    /// by an air contact (its eyes, its ears, a report — the SOP row's fresh air sense), it switches its radar ON (Search; Track while it
    /// holds an aircraft), and fires a SALVO at the aircraft its fire control sees (<see cref="FireControlSight"/>: the radar is enough for a
    /// radar-guided round) — 2 missiles at a manned aircraft, 1 at a drone, none at a target its seeker cannot lock (an FPV,
    /// <c>AmmoChoice.InDomain</c>). Once the salvo's outcome is known (the target down, or the missiles' flight over) or nothing has been
    /// tracked for <c>QuietSeconds</c>, whichever first, it switches the radar OFF and RELOCATES (shoot and scoot) — Success on arrival.
    /// The radar is written ONLY through <see cref="RadarEmissions.Set"/> on its own entity (RS-10); the deactivator switches it off.
    /// </summary>
    public static class RadarSamNodes
    {
        public static readonly RadarSamParams Defaults = new()
            { QuietSeconds = 20f, OutcomeSeconds = 18f, RelocateM = 350f, RelocateSeconds = 90f, RelocateSpeed = 8f };

        private const byte StageEngage = 0, StageRelocate = 1;

        public static RadarSamParams Effective(in RadarSamParams a)
        {
            var e = a;
            if (e.QuietSeconds <= 0f) e.QuietSeconds = Defaults.QuietSeconds;
            if (e.OutcomeSeconds <= 0f) e.OutcomeSeconds = Defaults.OutcomeSeconds;
            if (e.RelocateM <= 0f) e.RelocateM = Defaults.RelocateM;
            if (e.RelocateSeconds <= 0f) e.RelocateSeconds = Defaults.RelocateSeconds;
            if (e.RelocateSpeed <= 0f) e.RelocateSpeed = Defaults.RelocateSpeed;
            return e;
        }

        /// <summary>The SOP row's gate: <paramref name="self"/> carries a radar AND a radar-guided launcher with a round left.</summary>
        [SharedAiCondition]
        public static bool CanEngageByRadar(Entity self, EntityRepository world) => IsRadarSam(world, self);

        /// <inheritdoc cref="CanEngageByRadar"/>
        public static bool IsRadarSam(EntityRepository world, Entity unit)
        {
            if (!world.IsAlive(unit) || !Injury.IsInTheFight(world, unit) || RadarEmissions.RangeOf(world, unit) <= 0f) return false;
            for (int i = 0; i < AirDefenceWeapons.MaxMounts; i++)
            {
                var m = CombatTkb.MountOf(world, unit, i);
                if (m == null) break;
                if (FireControlSight.RadarGuided(world, m) && AirDefenceWeapons.HasRounds(world, unit, i)) return true;
            }
            return false;
        }

        /// <summary>
        /// ⭐ RS-4 — how many missiles a salvo at <paramref name="air"/> takes: 0 when no radar-guided round of <paramref name="unit"/> may be
        /// fired at it (its seeker cannot lock it — an FPV — or it is no air target), 1 at a drone (SISO air category 50, unmanned), else 2.
        /// </summary>
        public static int SalvoFor(EntityRepository world, Entity unit, Entity air)
        {
            if (air.IsNull || !world.IsAlive(air)) return 0;
            bool usable = false;
            for (int i = 0; i < AirDefenceWeapons.MaxMounts && !usable; i++)
            {
                var m = CombatTkb.MountOf(world, unit, i);
                if (m == null) break;
                usable = FireControlSight.RadarGuided(world, m) && AmmoChoice.InDomain(world, air, unchecked((long)m.AmmoGuid));
            }
            if (!usable) return 0;
            return Unmanned(world, air) ? 1 : 2;
        }

        /// <summary>An aircraft whose DIS type is SISO's unmanned air category (1.2.*.50).</summary>
        public static bool Unmanned(EntityRepository world, Entity air)
            => CombatTkb.TryGetTemplate(world, air, out var t) && Fdp.Toolkit.Motion.DroneBatteries.IsUnmannedAir(t.DisType);   // ⭐ SD-S2b — the one unmanned rule

        /// <summary>Running while it engages and while it relocates; Success once relocated.</summary>
        [SharedAiAction]
        public static NodeStatus RadarSamEngage(ref RadarSamParams authored, ref RadarSamState ws, Entity self, EntityRepository world)
        {
            var p = Effective(in authored);
            double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d;
            // ⭐ out of the fight (destroyed): the radar goes off and the engagement ends (sam-site: a destroyed SAM kept its radar on)
            if (!Injury.IsInTheFight(world, self)) { Release(ref ws, self, world, now); return NodeStatus.Failure; }
            if (ws.StartedAt == 0d)
            {
                ws.StartedAt = (SimInstant)now;
                ws.LastTrackAt = (SimInstant)now;
                ws.LastAirAt = (SimInstant)now;
                ws.RangeM = RadarEmissions.RangeOf(world, self);
            }
            if (ws.Stage == StageRelocate) return Relocate(in p, ref ws, self, world, now);

            var air = AirSense.Newest(world, self, now, ref ws.AirTick, ref ws.LastAirAt, out ws.AirAt, out ws.AirSlot);
            bool tracked = !air.IsNull && FireControlSight.SeesForAnyMount(world, self, air);
            if (tracked) ws.LastTrackAt = (SimInstant)now;

            // ⭐ RV-10 — relocate after the salvo's OUTCOME or QuietSeconds with nothing tracked, whichever comes first
            bool outcome = ws.LaunchedAt != 0d
                           && (!world.IsAlive(ws.Target) || !Injury.IsInTheFight(world, ws.Target) || now - ws.LaunchedAt >= p.OutcomeSeconds);
            if (outcome || now - ws.LastTrackAt >= p.QuietSeconds) { BeginRelocate(in p, ref ws, self, world, now); return NodeStatus.Running; }

            RadarEmissions.Set(world, self, tracked ? RadarMode.Track : RadarMode.Search, tracked ? air : Entity.Null,
                               RadarEmitterClass.AirDefence, ws.RangeM, now);
            if (ws.LaunchedAt == 0d) Salvo(ref ws, self, world, air, tracked, now);
            return NodeStatus.Running;
        }

        /// <summary>One salvo per engagement: asked of the weapon brain at the tracked aircraft; done when its rounds have left.</summary>
        private static void Salvo(ref RadarSamState ws, Entity self, EntityRepository world, Entity air, bool tracked, double now)
        {
            if (!world.HasComponent<WeaponChannel>(self)) return;
            ref readonly var channel = ref world.GetComponentRO<WeaponChannel>(self);
            bool mine = !ws.Fire.Threat.IsNull && channel.ActiveAction == CombatConstants.ActionIdAimAndFire;
            if (mine && channel.Status == NodeStatus.Success)
            {
                ws.LaunchedAt = (SimInstant)now;   // the salvo has left: its outcome is awaited
                PostureNodes.StopFiring(world, self, ref ws.Fire);
                return;
            }
            if (mine && channel.Status == NodeStatus.Failure)
            {
                PostureNodes.StopFiring(world, self, ref ws.Fire);
                ws.RetryFireAt = (SimInstant)(now + AirSense.RetryFireSeconds);
                return;
            }
            if (mine || !tracked || now < ws.RetryFireAt) return;
            int salvo = SalvoFor(world, self, air);
            if (salvo <= 0) return;   // nothing it may fire at this one (an FPV): it keeps searching
            ws.Target = air;
            ws.Salvo = salvo;
            PostureNodes.Fire(world, self, ref ws.Fire, 0f, salvo, air);
        }

        /// <summary>Radar OFF, the weapon stopped, and away: <c>RelocateM</c> straight from the last aircraft (from its own heading when none).</summary>
        private static void BeginRelocate(in RadarSamParams p, ref RadarSamState ws, Entity self, EntityRepository world, double now)
        {
            PostureNodes.StopFiring(world, self, ref ws.Fire);
            RadarEmissions.Set(world, self, RadarMode.Off, Entity.Null, RadarEmitterClass.AirDefence, ws.RangeM, now);
            ws.Stage = StageRelocate;
            ws.RelocateAt = (SimInstant)now;
            if (!world.HasComponent<SimTransform>(self)) return;
            var tf = world.GetComponentRO<SimTransform>(self);
            var at = tf.Position;
            var away = new Vector2(at.X - ws.AirAt.X, at.Y - ws.AirAt.Y);
            if (ws.AirAt == default || away.LengthSquared() < 1f)
            {
                var f = Vector3.Transform(Vector3.UnitX, tf.Rotation);
                away = new Vector2(f.X, f.Y);
            }
            away = away.LengthSquared() > 1e-6f ? Vector2.Normalize(away) : Vector2.UnitX;
            var to = at + new Vector3(away * p.RelocateM, 0f);
            ws.Moving = (byte)(LocomotionMoveTo.Issue(world, self, to, p.RelocateSpeed, 15f, onSurface: true, source: MoveSource.Flee) ? 1 : 0);
        }

        private static NodeStatus Relocate(in RadarSamParams p, ref RadarSamState ws, Entity self, EntityRepository world, double now)
        {
            RadarEmissions.Set(world, self, RadarMode.Off, Entity.Null, RadarEmitterClass.AirDefence, ws.RangeM, now);   // ⭐ Off while moving
            if (ws.Moving == 1 && LocomotionMoveTo.Status(world, self) == NodeStatus.Running && now - ws.RelocateAt < p.RelocateSeconds)
                return NodeStatus.Running;
            Release(ref ws, self, world, now);
            return NodeStatus.Success;
        }

        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.RadarSamNodes.RadarSamEngage")]
        public static void Deactivate_RadarSamEngage(ref RadarSamParams p, ref RadarSamState ws, Entity self, EntityRepository world)
            => Release(ref ws, self, world, world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d);

        private static void Release(ref RadarSamState ws, Entity self, EntityRepository world, double now)
        {
            PostureNodes.StopFiring(world, self, ref ws.Fire);
            if (ws.Moving == 1) LocomotionMoveTo.Stop(world, self);
            RadarEmissions.Set(world, self, RadarMode.Off, Entity.Null, RadarEmitterClass.AirDefence, ws.RangeM, now);
            ws = default;
        }
    }

    /// <summary>⭐ <c>CE-4617</c> — the library behaviour <c>RadarSamEngage</c> (a reaction: <see cref="BasicVehicleSop"/>'s radar-SAM row).</summary>
    public static class RadarSamEngageBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RadarSamBlackboard
        {
            public RadarSamParams Engage;
            public RadarSamState EngageState;
        }

        [BehaviorResolver(BehaviorNames.RadarSamEngage)]
        public static void Resolve(in NoParams authored, ref RadarSamBlackboard block, EntityRepository world, Entity self)
            => block = default;

        [Behavior(BehaviorNames.RadarSamEngage, Params = typeof(NoParams), Categories = BehaviorCategory.AllMilitary, Description = "A radar SAM: radar on, a salvo at the aircraft it tracks, then radar off and relocate.")]
        [BTreeDefinition(BehaviorNames.RadarSamEngage, Curated = true, Group = "Library/Combat", ParamsType = typeof(RadarSamBlackboard))]
        public static BTreeBuilder<RadarSamBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<RadarSamBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Engage, bb => bb.EngageState, RadarSamNodes.RadarSamEngage);
    }
}
