using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Components;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Look;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>Where a man's look comes from, in the order <see cref="LookAt.Want"/> tries them.</summary>
    public enum LookMode : byte
    {
        /// <summary>The top threat, else a fresh sound, else his sector, else the node's bearing (a hold, a fight).</summary>
        ThreatThenSector = 0,
        /// <summary>His sector, else the node's bearing — a threat or a sound elsewhere does not take his eyes (a security man).</summary>
        SectorOnly = 1,
    }

    /// <summary>What the look was pointed at last (the node's working state, for the rails and the slot route).</summary>
    public enum LookChoice : byte { NoLook = 0, Threat = 1, Sector = 2, Bearing = 3, Released = 4, Heard = 5 }

    [StructLayout(LayoutKind.Sequential)]
    public struct LookParams
    {
        public LookMode Mode;
        /// <summary>1 = <see cref="Bearing"/> is set (compass degrees, 0 = north, clockwise) — the fallback after the sector.</summary>
        public byte BearingSet;
        public float Bearing;
        /// <summary>Scan ±this many degrees around the chosen bearing (0 = a fixed look).</summary>
        public float HalfWidth;
        /// <summary>How long each of the scan's fixation points is held (s); 0 = 2.</summary>
        public float StepSeconds;
        /// <summary>How far out the look point is put along a bearing (m); 0 = 100.</summary>
        public float Metres;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LookNodeState
    {
        public LookChoice Source;
        public Entity Threat;
        public float Bearing;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2297</c> LD-3 (R-2048, R-2049) — who points a man's eyes: the top threat, else his sector (<see cref="EngagementArea"/>'s
    /// <c>WatchBearing</c>, else the bisector of his arc), else an order's own bearing — scanned in fixation steps when a width is given —
    /// written through backend's <see cref="LookRequest"/> (the one write; the muscle turns the head, and a lasting look the body).
    /// Pure of the tree: the node below calls it. 📄 docs/designs/look-direction/DESIGN_Look_Direction_And_FOV.md §3.3a.
    /// </summary>
    public static class LookAt
    {
        public const float DefaultMetres = 100f;
        public const float DefaultStepSeconds = 2f;
        /// <summary>The scan's fixation points across ±HalfWidth (ping-pong: 0,1,2,3,4,3,2,1,…).</summary>
        public const int ScanPoints = 5;
        /// <summary>LD-B — the widest scan (a sentry's ±60°).</summary>
        public const float MaxScanHalfWidth = 60f;
        /// <summary>The scan around a bare <c>WatchBearing</c>.</summary>
        public const float WatchHalfWidth = 30f;
        /// <summary>How long a heard sound keeps his eyes (ticks — the <c>KnownCombatants</c> clock; ≈ 5 s at 60 Hz).</summary>
        public const uint HeardWindowTicks = 300;
        /// <summary>How long a hit or a near miss puts the sound of the shot above a threat he sees (s).</summary>
        public const double UnderFireSeconds = 2.0;

        /// <summary>Choose and write the look; returns what it chose. Releases only a look this run wrote.</summary>
        public static LookChoice Want(EntityRepository world, Entity self, in LookParams p, ref LookNodeState ws, double now)
        {
            if (!LookRequest.CanCarry(world, self)) return LookChoice.NoLook;
            uint run = world.HasComponent<BehaviorState>(self) ? world.GetComponentRO<BehaviorState>(self).InstanceId : 0u;

            // ⭐ shot at: a round that hit or just missed him turns him to where it was HEARD from, over a threat he already sees
            //   (📌 ld-sentry-surprise: with A in sight in front, B's shots from behind never turned him)
            if (p.Mode == LookMode.ThreatThenSector && UnderFire(world, self) && FreshHeard(world, self, out var shotFrom))
            {
                LookRequest.AtPoint(world, self, shotFrom, behaviorInstance: run);
                ws.Threat = Entity.Null;
                return ws.Source = LookChoice.Heard;
            }
            if (p.Mode == LookMode.ThreatThenSector && EqsTacticsNodes.TopThreat(world, self, ws.Threat, out var threat) && world.IsAlive(threat))
            {
                // ⭐ AQ102 C (CE-2356; Peek & Fire D14) — one in sight now is followed; one out of sight is looked for where he is REMEMBERED,
                //   at the eyes of the stance he was seen in (📌 user, 2026-10-23: a defender's head tracked a player under the wall)
                if (InSightNow(world, self, threat)) LookRequest.AtEntity(world, self, threat, behaviorInstance: run);
                else if (Remembered(world, self, threat, out var where)) LookRequest.AtPoint(world, self, where, behaviorInstance: run);
                else LookRequest.AtEntity(world, self, threat, behaviorInstance: run);
                ws.Threat = threat;
                return ws.Source = LookChoice.Threat;
            }
            ws.Threat = Entity.Null;
            if (p.Mode == LookMode.ThreatThenSector && FreshHeard(world, self, out var heard))
            {
                LookRequest.AtPoint(world, self, heard, behaviorInstance: run);   // ⭐ a shot heard: he turns to the sound (LD-5 ④)
                return ws.Source = LookChoice.Heard;
            }

            LookChoice source;
            float half = p.HalfWidth;
            if (SectorBearing(world, self, out float bearing, out float sectorHalf)) { source = LookChoice.Sector; if (half <= 0f) half = sectorHalf; }
            else if (p.BearingSet != 0) { bearing = p.Bearing; source = LookChoice.Bearing; }
            else
            {
                if (ws.Source != LookChoice.NoLook && ws.Source != LookChoice.Released && OwnsLook(world, self, run))
                    LookRequest.Release(world, self, run);
                return ws.Source = LookChoice.Released;
            }
            float step = p.StepSeconds > 0f ? p.StepSeconds : DefaultStepSeconds;
            ws.Bearing = half > 0f ? ScanBearing(bearing, half, step, now) : bearing;
            var origin = world.HasComponent<SimTransform>(self) ? world.GetComponentRO<SimTransform>(self).Position : Vector3.Zero;
            LookRequest.AtPoint(world, self, PointAt(origin, ws.Bearing, p.Metres > 0f ? p.Metres : DefaultMetres), behaviorInstance: run);
            return ws.Source = source;
        }

        /// <summary>His sector's bearing and the half-width he scans: <c>WatchBearing</c> (±<see cref="WatchHalfWidth"/>) when set, else the
        /// bisector of his arc (clockwise from left to right; half the arc, at most <see cref="MaxScanHalfWidth"/>).</summary>
        public static bool SectorBearing(EntityRepository world, Entity self, out float bearing, out float halfWidth)
        {
            bearing = 0f;
            halfWidth = 0f;
            if (!world.IsComponentTypeRegistered<EngagementArea>() || !world.HasComponent<EngagementArea>(self)) return false;
            ref readonly var a = ref world.GetComponentRO<EngagementArea>(self);
            if (a.WatchSet != 0)
            {
                bearing = Norm(a.WatchBearing);
                halfWidth = WatchHalfWidth;
                return true;
            }
            if (a.ArcSet == 0) return false;
            float span = MathF.Abs(a.RightBearing - a.LeftBearing) >= 360f ? 360f : Norm(a.RightBearing - a.LeftBearing);
            bearing = Norm(a.LeftBearing + span * 0.5f);
            halfWidth = MathF.Min(span * 0.5f, MaxScanHalfWidth);
            return true;
        }

        /// <summary>The scan's bearing at <paramref name="now"/>: <see cref="ScanPoints"/> fixations across ±half, each held
        /// <paramref name="step"/> seconds, ping-pong.</summary>
        public static float ScanBearing(float centre, float half, float step, double now)
        {
            int cycle = 2 * (ScanPoints - 1);
            int k = (int)(Math.Max(0.0, now) / step) % cycle;
            int index = k < ScanPoints ? k : cycle - k;
            return Norm(centre - half + 2f * half * index / (ScanPoints - 1));
        }

        /// <summary>The point <paramref name="metres"/> out from <paramref name="origin"/> along a compass bearing (0 = north / +Y).</summary>
        public static Vector3 PointAt(Vector3 origin, float bearing, float metres)
        {
            float r = bearing * (MathF.PI / 180f);
            return new Vector3(origin.X + MathF.Sin(r) * metres, origin.Y + MathF.Cos(r) * metres, origin.Z);
        }

        /// <summary>⭐ <c>CE-2356</c> — how recently a threat must have been stamped for the eyes to follow him (perception ticks, ≈ 0.5 s).</summary>
        public const uint InSightTicks = 30;

        /// <summary>⭐ <c>CE-2356</c> (AQ102 C) — <paramref name="threat"/> is in his eyes now: a memory slot he has SEEN (the Visual bit), stamped
        /// within <see cref="InSightTicks"/>. ⚠ A sound of him also stamps the slot, so a seen man who is heard nearby counts as in sight.</summary>
        public static unsafe bool InSightNow(EntityRepository world, Entity self, Entity threat)
        {
            if (!world.HasComponent<TargetMemory>(self)) return false;
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(self);
            uint now = Fdp.Toolkit.Perception.PerceptionClock.Now(world);
            long id = (long)threat.PackedValue;
            for (int i = 0; i < mem.Count; i++)
            {
                if (mem.EntityIds[i] != id || TargetMemory.IsAnonymous(in mem, i)) continue;
                if ((mem.Modalities[i] & (byte)Fdp.Toolkit.Perception.Components.SensorModality.Visual) == 0) return false;
                return mem.LastSeenTick[i] > now || now - mem.LastSeenTick[i] <= InSightTicks;
            }
            return false;
        }

        /// <summary>⭐ <c>CE-2356</c> (AQ102 C) — where <paramref name="threat"/> is remembered, raised to the eyes of the stance he was last
        /// seen in (standing 1.6 m · crouched 1.0 · prone 0.3 — the body points of Building Interiors §3f).</summary>
        public static unsafe bool Remembered(EntityRepository world, Entity self, Entity threat, out Vector3 at)
        {
            at = default;
            if (!world.HasComponent<TargetMemory>(self)) return false;
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(self);
            long id = (long)threat.PackedValue;
            for (int i = 0; i < mem.Count; i++)
            {
                if (mem.EntityIds[i] != id || TargetMemory.IsAnonymous(in mem, i)) continue;
                float eyes = mem.SeenStance[i] switch
                {
                    (byte)Fdp.Toolkit.Tkb.Domain.StanceId.Crouched + 1 => 1.0f,
                    (byte)Fdp.Toolkit.Tkb.Domain.StanceId.Prone + 1 => 0.3f,
                    _ => 1.6f,
                };
                at = new Vector3(mem.PositionsX[i], mem.PositionsY[i], mem.PositionsZ[i] + eyes);
                return true;
            }
            return false;
        }

        /// <summary>Hit or near-missed within <see cref="UnderFireSeconds"/> (<c>RecentSensesOf</c>, the self-defence clock).</summary>
        public static bool UnderFire(EntityRepository world, Entity self)
            => RecentSensesOf.Within(world, self, Fdp.Toolkit.Perception.Events.SensorChange.Hit, UnderFireSeconds)
            || RecentSensesOf.Within(world, self, Fdp.Toolkit.Perception.Events.SensorChange.NearMiss, UnderFireSeconds);

        /// <summary>The freshest HEARD contact (an anonymous memory slot — <c>TopThreat</c> ranks entities only) stamped within
        /// <see cref="HeardWindowTicks"/> of now (<c>PerceptionClock.Now</c>, the clock perception stamps it with — CE-3292).</summary>
        public static unsafe bool FreshHeard(EntityRepository world, Entity self, out Vector3 at)
        {
            at = default;
            if (!world.HasComponent<TargetMemory>(self)) return false;
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(self);
            uint now = Fdp.Toolkit.Perception.PerceptionClock.Now(world);   // ⭐ CE-3292 — LastSeenTick's clock (sim steps)
            for (int i = 0; i < mem.Count; i++)
            {
                if (!TargetMemory.IsAnonymous(in mem, i) || now - mem.LastSeenTick[i] > HeardWindowTicks) continue;
                at = new Vector3(mem.PositionsX[i], mem.PositionsY[i], mem.PositionsZ[i]);
                return true;
            }
            return false;
        }

        /// <summary>The compass bearing from <paramref name="from"/> to <paramref name="to"/> (0 = north / +Y, clockwise).</summary>
        public static float BearingTo(Vector3 from, Vector3 to) => Norm(MathF.Atan2(to.X - from.X, to.Y - from.Y) * (180f / MathF.PI));

        /// <summary>The compass bearing a body faces (its forward is +X, <c>LookOf.YawOf</c>).</summary>
        public static float FacingBearing(Quaternion rotation)
            => Norm(90f - Fdp.Toolkit.Perception.Look.LookOf.YawOf(rotation) * (180f / MathF.PI));

        private static bool OwnsLook(EntityRepository world, Entity self, uint run)
            => world.GetComponentRO<LookAtChannel>(self).BehaviorInstanceId == run;

        private static float Norm(float deg)
        {
            deg %= 360f;
            return deg < 0f ? deg + 360f : deg;
        }
    }

    /// <summary>⭐ <c>CE-2297</c> LD-3 — the one look node: a play puts it beside its work (always Running).</summary>
    public static class LookNodes
    {
        [SharedAiAction]
        public static NodeStatus Look(ref LookParams p, ref LookNodeState ws, Entity self, EntityRepository world)
        {
            LookAt.Want(world, self, in p, ref ws, world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0.0);
            return NodeStatus.Running;
        }
    }
}
