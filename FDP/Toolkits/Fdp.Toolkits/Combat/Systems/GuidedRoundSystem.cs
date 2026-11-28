using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat.Systems
{
    /// <summary>
    /// ⭐⭐ M4 (D-D; <c>docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md</c> §3) — steers every GUIDED round on the Muscle, before
    /// <see cref="BallisticsSystem"/> sweeps it: the velocity turns toward an aim point at most <see cref="TurnRateDegPerSec"/>, at the
    /// missile's speed — no aerodynamics. <b>Command</b>: onto the gunner's sight line (eye → the target's middle) while that line is
    /// clear and the gunner alive; blocked ⇒ <see cref="GuidedPhase.Lost"/>, the round flies on straight. <b>Fire-and-forget</b>: toward
    /// the target while the ROUND sees it (its last seen point otherwise); with top attack it climbs to its cruise height, cruises,
    /// and dives once the target is within altitude / tan(dive). The hit, the face (a dive arrives from above ⇒ Top) and the damage
    /// are the unchanged chain. No new wire: the round, its target and the terrain are all on this node.
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class GuidedRoundSystem : IEcsModuleSystem
    {
        /// <summary>The steering limit (D-D: ≤ 30°/s).</summary>
        public const float TurnRateDegPerSec = 30f;

        /// <summary>A command round aims this far (seconds of flight) ahead of its own point on the sight line.</summary>
        public const float CommandLookaheadSeconds = 0.5f;

        /// <summary>A top-attack round climbs at this angle.</summary>
        public const float ClimbAngleDeg = 30f;

        private const float Deg = MathF.PI / 180f;

        /// <summary>
        /// ⭐⭐ <c>CE-2201</c> (DESIGN_Missiles_And_Ammo §3, "the profile fits") — does a TOP-ATTACK flight fit a goal
        /// <paramref name="groundRangeM"/> away (horizontal), launched <paramref name="launchAboveGoalM"/> above it at
        /// <paramref name="speedMps"/>, and to what <paramref name="apexM"/> (m above the launch point, ≤ <see cref="MissileDto.CruiseAltitudeM"/>).
        /// The ONE answer the launch (<c>FireProcessingSystem</c>: Climb or direct, and the cruise height) and the danger a target reads
        /// (<c>ThreatDanger</c>: the top or the face) both use. It is this system's own flight, in closed form, at the turn limit
        /// (radius R = speed / <see cref="TurnRateDegPerSec"/>): the climb arc onto <see cref="ClimbAngleDeg"/> (or less, for a low apex),
        /// the straight climb, the pitch-over arc back to level (it peaks R·(1 − cos climb) above the apex), and the dive lead the cruise
        /// branch below triggers on. It fits when that ground ≤ the range AND the peak is high enough for the dive arc to complete
        /// (R·(1 − cos dive) — it then arrives at the dive angle, on the roof). The highest fitting apex is taken; none ⇒ false (direct).
        /// 📐 Javelin (140 m/s, cruise 150 m, dive 60°): R ≈ 267 m, a full profile needs ≈ 730 m, a capped one ≈ 600 m — below that it
        /// flies direct (measured live: a 230 m top attack overflew the tank and hit the sand 580 m beyond it).
        /// A missile with no speed or no cruise height declares no geometry: its declared <see cref="MissileDto.TopAttackMinRangeM"/> alone.
        /// </summary>
        /// <summary>⭐ <c>CE-2222</c> — a missile's own turn rate (°/s), else the engine's <see cref="TurnRateDegPerSec"/>.</summary>
        public static float TurnRateOf(float stated) => stated > 0f ? stated : TurnRateDegPerSec;

        /// <summary>⭐ <c>CE-2222</c> — a missile's own top-attack climb (°), else the engine's <see cref="ClimbAngleDeg"/>.</summary>
        public static float ClimbOf(float stated) => stated > 0f ? stated : ClimbAngleDeg;

        public static bool TopAttackFits(MissileDto missile, float speedMps, float groundRangeM, float launchAboveGoalM, out float apexM)
        {
            apexM = 0f;
            if (missile.Guidance != GuidanceKind.FireAndForget || !missile.TopAttack || groundRangeM < missile.TopAttackMinRangeM) return false;
            if (speedMps <= 0f || missile.CruiseAltitudeM <= 0f) { apexM = MathF.Max(missile.CruiseAltitudeM, 0f); return true; }
            float r = speedMps / (TurnRateOf(missile.TurnRateDegPerSec) * Deg);   // ⭐ CE-2222 — the missile's own turn and climb
            float climb = ClimbOf(missile.ClimbAngleDeg) * Deg, dive = MathF.Max(missile.DiveAngleDeg, 10f) * Deg;
            float floor = r * (1f - MathF.Cos(dive));   // the dive arc completes above this ⇒ it arrives at the dive angle
            float apex = missile.CruiseAltitudeM;
            if (Ground(apex, out _) > groundRangeM)
            {
                float lo = 0f, hi = apex;   // the ground needed grows with the apex: the highest one that fits
                for (int i = 0; i < 24; i++) { float mid = 0.5f * (lo + hi); if (Ground(mid, out _) <= groundRangeM) lo = mid; else hi = mid; }
                apex = lo;
            }
            Ground(apex, out float peak);
            if (apex <= 0f || peak + launchAboveGoalM < floor) return false;
            apexM = apex;
            return true;

            // the ground the profile needs to an apex a, and the height it peaks at
            float Ground(float a, out float peakAbove)
            {
                float arcTop = r * (1f - MathF.Cos(climb));
                float pitch = a >= arcTop ? climb : MathF.Acos(1f - a / r);   // a low apex is reached still in the first arc
                float climbGround = 2f * r * MathF.Sin(pitch) + MathF.Max(0f, a - arcTop) / MathF.Tan(climb);
                peakAbove = a + r * (1f - MathF.Cos(pitch));
                float height = peakAbove + launchAboveGoalM;
                float lead = r * MathF.Sin(dive) + MathF.Max(0f, height - r * (1f - MathF.Cos(dive))) / MathF.Tan(dive);
                return climbGround + lead;
            }
        }

        private EntityQuery? _rounds;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;   // ⭐ CE-3329 P2 — a replay: the recording owns the world (DESIGN_Replay_Time_Axis_And_Review §12l)
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<GuidedRound>()) return;   // ⭐ CE-3293 S5 (R-3036 rule 8) — no hand pause gate: [TickMode(WhenTimeAdvances)] keeps this system off a frame that does not advance
            var world = Fdp.Toolkit.World.WorldQuery.Of(repo);
            uint tick = repo.HasSingleton<GlobalTime>() ? (uint)repo.GetSingleton<GlobalTime>().FrameNumber : 0u;
            _rounds = repo.Query().With<GuidedRound>().With<SimTransform>().With<SimVelocity>().Build();
            foreach (var e in _rounds)
            {
                ref var g = ref repo.GetComponentRW<GuidedRound>(e);
                if (g.Guidance == GuidanceKind.Unguided || g.Phase == GuidedPhase.Lost) continue;
                if (repo.HasComponent<BallisticProjectile>(e) && repo.GetComponentRO<BallisticProjectile>(e).StoppedTick != 0) continue;
                var pos = repo.GetComponentRO<SimTransform>(e).Position;
                ref var vel = ref repo.GetComponentRW<SimVelocity>(e).Linear;
                bool steered = Steer(repo, world, ref g, pos, vel, out var desired);
                if (g.MaxG > 0f)
                {
                    // ⭐⭐ CE-10002 (AC-6) — boost / coast, the g by speed², the lag; too slow ⇒ DEFEATED: it expires, its shot row says so
                    if (Motor(ref g, ref vel, steered, desired, deltaTime))
                    {
                        if (ShotLog.Peek(repo)?.Of(e) is { } shot) ShotLog.End(shot, ShotOutcome.Defeated, tick, pos);
                        // ⭐ AC-9a — the aircraft it was after counts it (its motion node's report; this system runs on that node, serially)
                        if (repo.IsAlive(g.Target) && repo.IsComponentTypeRegistered<Fdp.Toolkit.Motion.FighterPilotState>()
                            && repo.HasComponent<Fdp.Toolkit.Motion.FighterPilotState>(g.Target))
                        {
                            ref var fp = ref repo.GetComponentRW<Fdp.Toolkit.Motion.FighterPilotState>(g.Target);
                            if (fp.Defeated < 255) fp.Defeated++;
                        }
                        repo.DestroyEntity(e);
                        continue;
                    }
                }
                else if (steered)
                    vel = Turn(vel, desired, TurnRateOf(g.TurnRateDegPerSec) * (MathF.PI / 180f) * deltaTime) * g.SpeedMps;
                // ⭐ CE-3198 (§3b) — the overfly fuze: fires DOWN as the round passes over the roof
                if (g.Overfly != 0 && OverflyFuze(repo, e, in g, pos, vel, deltaTime)) { repo.DestroyEntity(e); continue; }
                // ⭐ CE-2187 (§3c, AD2) — the proximity fuze: bursts where its step passes closest to its target
                if (g.ProximityM > 0f && ProximityFuze(repo, e, in g, pos, vel, deltaTime, tick)) repo.DestroyEntity(e);
            }
        }

        /// <summary>
        /// ⭐⭐ CE-10002 (AC-6, RV-1; DESIGN_Air_Combat §3, §5b) — a motor round's step: it BOOSTS to <see cref="GuidedRound.SpeedMps"/> over its
        /// burn, then COASTS losing <see cref="GuidedRound.DragPerS"/> (more while it turns: × (1 + the g it pulls ÷ its MaxG)); the g it can
        /// pull is <c>MaxG·(v / SpeedMps)²</c> (a slowing missile gets LESS agile); its steering follows the lead through a first-order
        /// lag. True when it is DEFEATED (coasting below <see cref="GuidedRound.MinSpeedMps"/>). Pure arithmetic: no allocation.
        /// </summary>
        public static bool Motor(ref GuidedRound g, ref Vector3 vel, bool steered, Vector3 desired, float dt)
        {
            if (g.SpeedNow <= 0f) g.SpeedNow = MathF.Max(vel.Length(), 1f);
            if (g.BurnLeftS > 0f) { g.SpeedNow = MathF.Min(g.SpeedMps, g.SpeedNow + g.BoostMps2 * dt); g.BurnLeftS -= dt; }
            else g.SpeedNow = MathF.Max(0f, g.SpeedNow - g.DragPerS * (1f + g.LoadG / g.MaxG) * dt);
            if (g.BurnLeftS <= 0f && g.MinSpeedMps > 0f && g.SpeedNow < g.MinSpeedMps) return true;
            float v = MathF.Max(g.SpeedNow, 1f);
            float ratio = v / MathF.Max(g.SpeedMps, 1f);
            float gAvail = g.MaxG * ratio * ratio;
            var dir = vel.LengthSquared() > 1e-8f ? Vector3.Normalize(vel) : g.SteerDir;
            g.LoadG = 0f;
            if (steered)
            {
                float k = g.SteerLagS > 0f ? MathF.Min(1f, dt / g.SteerLagS) : 1f;
                var s = g.SteerDir + (desired - g.SteerDir) * k;
                g.SteerDir = s.LengthSquared() > 1e-8f ? Vector3.Normalize(s) : desired;
                var next = Turn(dir, g.SteerDir, gAvail * 9.81f / v * dt);
                float turned = MathF.Acos(Math.Clamp(Vector3.Dot(dir, next), -1f, 1f));
                g.LoadG = turned / MathF.Max(dt, 1e-4f) * v / 9.81f;
                dir = next;
            }
            vel = dir * g.SpeedNow;
            return false;
        }

        /// <summary>Above the roof, a fuze this much higher than the overfly height still fires (the round's path is not exact).</summary>
        public const float OverflyFuzeMarginM = 2f;

        /// <summary>
        /// ⭐ <c>CE-3198</c> (DESIGN_Missiles_And_Ammo §3b) — the NLAW's overfly fuze: when the round's NEXT step (pos → pos + v·dt) enters
        /// the target's footprint (its collider circle) while the round is above the roof and no more than <see cref="GuidedRound.OverflyHeightM"/>
        /// + <see cref="OverflyFuzeMarginM"/> over it, the charge fires DOWN: a <see cref="Fdp.Toolkit.Combat.Contracts.DetonationNotification"/> on the target with a
        /// straight-down travel (⇒ the Top face, the unchanged chain), the round's own penetration, damage and ammo — as the FPV drone's
        /// impact does. True when it fired (the round is spent).
        /// </summary>
        public static bool OverflyFuze(EntityRepository repo, Entity round, in GuidedRound g, Vector3 pos, Vector3 vel, float dt)
        {
            if (!repo.IsAlive(g.Target) || !repo.HasComponent<SimTransform>(g.Target) || !repo.HasComponent<PhysicsCollider>(g.Target)) return false;
            float hull = Fdp.Toolkit.Physics.Components.PhysicsColliderReaders.HullHeight(repo, g.Target);
            if (hull <= 0f) return false;
            var at = repo.GetComponentRO<SimTransform>(g.Target).Position;
            float roof = at.Z + hull;
            if (pos.Z < roof || pos.Z > roof + g.OverflyHeightM + OverflyFuzeMarginM) return false;
            var next = pos + vel * dt;
            float radius = repo.GetComponentRO<PhysicsCollider>(g.Target).Radius;
            if (!Fdp.Toolkit.Physics.Math.Intersection2D.SegmentInCircle(new Vector2(pos.X, pos.Y), new Vector2(next.X, next.Y),
                                                                         new Vector2(at.X, at.Y), radius, out _, out _)) return false;
            var proj = repo.HasComponent<BallisticProjectile>(round) ? repo.GetComponentRO<BallisticProjectile>(round) : default;
            float speed = MathF.Max(vel.Length(), 1f);
            repo.Bus.Publish(new Fdp.Toolkit.Combat.Contracts.DetonationNotification
            {
                Shooter = proj.Shooter, Target = g.Target, HitX = pos.X, HitY = pos.Y, HitZ = roof,
                Penetration = proj.Penetration, Damage = proj.Damage, Ammo = proj.Ammo, OriginalShooter = proj.OriginalShooter,   // ⭐ CE-4155
                TravelX = 0f, TravelY = 0f, TravelZ = -speed,
            });
            return true;
        }

        /// <summary>
        /// ⭐ <c>CE-2187</c> (DESIGN_Missiles_And_Ammo §3c, AD2) — the proximity fuze: when the round's NEXT step (pos → pos + v·dt)
        /// passes within <see cref="GuidedRound.ProximityM"/> of its target's middle, it bursts at the closest point of that step — the
        /// off-target burst every fuzed round uses (<see cref="BallisticsSystem.Detonate"/> ⇒ the area effect), so what is hurt is decided by
        /// the fragments' distance, not by a rule: a round seduced by a flare bursts at the flare. True when it burst.
        /// </summary>
        public static bool ProximityFuze(EntityRepository repo, Entity round, in GuidedRound g, Vector3 pos, Vector3 vel, float dt, uint tick)
        {
            if (!repo.IsAlive(g.Target) || !repo.HasComponent<SimTransform>(g.Target)) return false;
            var target = TargetPoint(repo, g.Target);
            var step = vel * dt;
            float len2 = step.LengthSquared();
            float t = len2 > 1e-8f ? Math.Clamp(Vector3.Dot(target - pos, step) / len2, 0f, 1f) : 0f;
            var closest = pos + step * t;
            if (Vector3.DistanceSquared(closest, target) > g.ProximityM * g.ProximityM) return false;
            var proj = repo.HasComponent<BallisticProjectile>(round) ? repo.GetComponentRO<BallisticProjectile>(round) : default;
            BallisticsSystem.Detonate(repo, in proj, closest, ShotLog.Peek(repo)?.Of(round), tick);
            return true;
        }

        /// <summary>Where a round aims at <paramref name="target"/>: the middle of a body (<see cref="AimPoint.Middle"/>), the point
        /// itself for a decoy (<c>CE-2188</c>).</summary>
        private static Vector3 TargetPoint(EntityRepository repo, Entity target)
            => repo.IsComponentTypeRegistered<Decoy>() && repo.HasComponent<Decoy>(target)
                ? repo.GetComponentRO<SimTransform>(target).Position
                : AimPoint.Middle(repo, target, out _, out _);

        /// <summary>⭐ <c>CE-2187</c> (§3c) — does a seeker flying along <paramref name="vel"/> from <paramref name="pos"/> see
        /// <paramref name="point"/>: inside its cone (always, with no cone).</summary>
        /// <summary>⭐ CE-10002 (RV-4) — the notch: a target's speed along the line of sight under this (m/s), below this height (m AGL).</summary>
        public const float NotchMps = 50f, NotchAglM = 300f;

        /// <summary>⭐ CE-10002 (RV-4) — is <paramref name="target"/> in the radar seeker's notch, seen from <paramref name="seeker"/>?</summary>
        public static bool InNotch(EntityRepository repo, Fdp.Toolkit.World.IWorldQuery? world, Entity target, Vector3 seeker, Vector3 targetPoint)
        {
            float ground = world?.GroundHeightAt(targetPoint.X, targetPoint.Y) ?? 0f;
            if (targetPoint.Z - ground >= NotchAglM) return false;
            if (!Fdp.Toolkit.Replication.ReplicatedMotion.Of(repo, target, out _, out var tv)) return false;
            var los = targetPoint - seeker;
            float d = los.Length();
            return d > 1e-3f && MathF.Abs(Vector3.Dot(tv, los / d)) < NotchMps;
        }

        public static bool InCone(in GuidedRound g, Vector3 pos, Vector3 vel, Vector3 point)
        {
            if (g.SeekerCos <= 0f) return true;
            var to = point - pos;
            float d = to.Length(), v = vel.Length();
            if (d < 1e-3f || v < 1e-3f) return true;
            return Vector3.Dot(to, vel) / (d * v) >= g.SeekerCos;
        }

        /// <summary>The direction the round wants to fly, or false when it flies on as it is (lost, no target).</summary>
        private static bool Steer(EntityRepository repo, Fdp.Toolkit.World.IWorldQuery? world, ref GuidedRound g, Vector3 pos, Vector3 vel, out Vector3 desired)
        {
            desired = default;
            bool targetKnown = repo.IsAlive(g.Target) && repo.HasComponent<SimTransform>(g.Target);
            var targetPoint = targetKnown ? TargetPoint(repo, g.Target) : g.LastSeen;
            // ⭐ CE-3198 (§3b) — an overfly round's goal is above the roof, not the middle: it passes over and fires down
            if (g.Overfly != 0 && targetKnown)
                targetPoint = repo.GetComponentRO<SimTransform>(g.Target).Position
                              + new Vector3(0f, 0f, Fdp.Toolkit.Physics.Components.PhysicsColliderReaders.HullHeight(repo, g.Target) + g.OverflyHeightM);

            // SD-S4 (DESIGN_Simulated_Devices sec 3.13b D-80 / D-96, sec 3.13g D-177) - a command round launched THROUGH a fire-control device flies the DEVICE's sight line (the
            //   crosshair the trainee holds, SACLOS by hand), not the gunner's eye onto the target's middle: it needs no target, it misses when the line is off the target.
            //   Lost (NO LOCK) when the device is gone or off (no line) or the sight line to the ROUND is blocked - the sight must see the flare.
            if (g.Guidance == GuidanceKind.Command && !g.Device.IsNull)
            {
                if (!Fdp.Toolkit.Behavior.Devices.FireControls.GuideLine(repo, g.Device, out var origin, out var sight)
                    || (world != null && world.SightBlocked(origin, pos)))
                {
                    g.Phase = GuidedPhase.Lost;
                    Fdp.Toolkit.Behavior.Devices.FireControls.MarkFlight(repo, g.Device, Fdp.Toolkit.Behavior.Devices.GuidedFlightState.NoLock);
                    return false;
                }
                float ahead = MathF.Max(Vector3.Dot(pos - origin, sight) + g.SpeedMps * CommandLookaheadSeconds, 0f);
                var toLine = origin + sight * ahead - pos;
                if (toLine.LengthSquared() < 1e-6f) toLine = sight;
                desired = Vector3.Normalize(toLine);
                return true;
            }

            if (g.Guidance == GuidanceKind.Command)
            {
                if (!targetKnown || !repo.IsAlive(g.Gunner) || !repo.HasComponent<SimTransform>(g.Gunner)) { g.Phase = GuidedPhase.Lost; return false; }
                var eye = Fdp.Toolkit.Perception.LineOfSight.TerrainWorldLosStrategy.EyeOf(repo, g.Gunner, HitModel.LogicalStance(repo, g.Gunner));   // ⭐ CE-3257
                if (world != null && world.SightBlocked(eye, targetPoint)) { g.Phase = GuidedPhase.Lost; return false; }
                var line = targetPoint - eye;
                float length = line.Length();
                if (length < 1e-3f) return false;
                var dir = line / length;
                float along = Math.Clamp(Vector3.Dot(pos - eye, dir) + g.SpeedMps * CommandLookaheadSeconds, 0f, length);
                var aim = eye + dir * along - pos;
                if (aim.LengthSquared() < 1e-6f) aim = dir;
                desired = Vector3.Normalize(aim);
                return true;
            }

            // fire-and-forget (⭐ CE-2187: an IR / radar seeker too): the round's own seeker — inside its cone only
            bool sees = targetKnown && InCone(in g, pos, vel, targetPoint) && (world == null || !world.SightBlocked(pos, targetPoint));
            // ⭐⭐ CE-10002 (air combat AC-6, RV-4) — THE RADAR NOTCH: a radar seeker loses a target whose own speed along the line of sight is
            //   under NotchMps (it flies across the beam — no Doppler against the ground clutter) while it is below NotchAglM above the ground
            if (sees && g.Guidance == GuidanceKind.Radar && InNotch(repo, world, g.Target, pos, targetPoint)) sees = false;
            if (sees) g.LastSeen = targetPoint;
            var goal = g.LastSeen;
            // ⭐ CE-2187 (§3c) — a HOMING seeker (IR, radar) leads a target it sees: it steers at where the target will be when it arrives
            //   (TargetLead.Point — the one predictor, AT2's), not at where it is. ⛔ Pure pursuit at the 30°/s turn limit fell 6.8 m behind a
            //   helicopter crossing at 60 m/s — outside a 4 m proximity fuze (measured, rail CE2187_AnIrRound_…).
            if (sees && g.Guidance is GuidanceKind.InfraRed or GuidanceKind.Radar
                && Fdp.Toolkit.Replication.ReplicatedMotion.Of(repo, g.Target, out _, out var targetVelocity))
                // ⭐ R-10018 (user 2026-11-17: "Harden as you lean.") — a MOTOR round leads on its CURRENT speed (the same lead pursuit, a true
                //   input): on the burn-out speed it led a target it could not reach yet and half of the straight-flying targets were missed
                goal = TargetLead.Point(pos, Vector3.Zero, g.MaxG > 0f ? MathF.Max(g.SpeedNow, 1f) : g.SpeedMps, targetPoint, targetVelocity);
            var toGoal = goal - pos;
            if (toGoal.LengthSquared() < 1e-6f) return false;
            // ⭐ CE-2201 — a top attack's DIVE is the branch below (at least the dive angle, homing once the line is steeper). ⛔ It was
            //   returned here as pure pursuit, so the dive branch was dead and the arrival angle was luck (900 m: 40° down, the side).
            if (g.TopAttack == 0) { desired = Vector3.Normalize(toGoal); return true; }

            var flat = new Vector2(toGoal.X, toGoal.Y);
            float horizontal = flat.Length();
            var h = horizontal > 1e-3f ? flat / horizontal : Vector2.UnitX;
            float height = pos.Z - goal.Z;
            float dive = MathF.Max(g.DiveAngleDeg, 10f) * (MathF.PI / 180f);
            if (g.Phase == GuidedPhase.Dive)
            {
                // the dive: at least the dive angle, and straight at the target once the line to it is steeper
                float los = MathF.Atan2(MathF.Max(height, 0f), horizontal);
                float angle = MathF.Max(dive, los);
                desired = horizontal < 1e-3f ? -Vector3.UnitZ : new Vector3(h.X * MathF.Cos(angle), h.Y * MathF.Cos(angle), -MathF.Sin(angle));
                return true;
            }
            if (g.Phase == GuidedPhase.Climb)
            {
                if (pos.Z >= g.CruiseZ) g.Phase = GuidedPhase.Cruise;
                else
                {
                    float c = MathF.Cos(ClimbOf(g.ClimbAngleDeg) * (MathF.PI / 180f)), s = MathF.Sin(ClimbOf(g.ClimbAngleDeg) * (MathF.PI / 180f));
                    desired = new Vector3(h.X * c, h.Y * c, s);
                    return true;
                }
            }
            // cruise: level at the cruise height until the dive must begin — the turn onto the dive angle is an arc of radius
            // speed / turn rate (R), so it starts R·sin(dive) + the rest of the height at the dive angle before the target
            // ⭐ CE-2201 — from the round's CURRENT pitch γ (the cruise's altitude hold swings it after the pitch-over): the arc from γ to
            //   −dive covers R·(sin γ + sin dive) of ground and drops R·(cos γ − cos dive). ⛔ The level-entry lead (γ = 0) fell 23–73 m
            //   short entering pitched down and overflew entering pitched up (750–1000 m, measured).
            float r = g.SpeedMps / (TurnRateOf(g.TurnRateDegPerSec) * (MathF.PI / 180f));
            float speed = vel.Length();
            float pitch = speed > 1e-3f ? MathF.Asin(Math.Clamp(vel.Z / speed, -1f, 1f)) : 0f;
            float lead = r * (MathF.Sin(pitch) + MathF.Sin(dive))
                       + MathF.Max(0f, height - r * (MathF.Cos(pitch) - MathF.Cos(dive))) / MathF.Tan(dive);
            if (height > 0f && horizontal <= lead)
            {
                g.Phase = GuidedPhase.Dive;
                desired = new Vector3(h.X * MathF.Cos(dive), h.Y * MathF.Cos(dive), -MathF.Sin(dive));
                return true;
            }
            desired = Vector3.Normalize(new Vector3(h.X, h.Y, (g.CruiseZ - pos.Z) * 0.05f));
            return true;
        }

        /// <summary>The unit direction of <paramref name="velocity"/> turned toward <paramref name="desired"/> by at most
        /// <paramref name="maxRadians"/>.</summary>
        public static Vector3 Turn(Vector3 velocity, Vector3 desired, float maxRadians)
        {
            var dir = velocity.LengthSquared() > 1e-8f ? Vector3.Normalize(velocity) : desired;
            float cos = Math.Clamp(Vector3.Dot(dir, desired), -1f, 1f);
            float angle = MathF.Acos(cos);
            if (angle <= maxRadians) return desired;
            var perp = desired - dir * cos;
            if (perp.LengthSquared() < 1e-8f)   // exactly opposite: turn about any axis
                perp = MathF.Abs(dir.Z) < 0.9f ? Vector3.Cross(dir, Vector3.UnitZ) : Vector3.Cross(dir, Vector3.UnitX);
            perp = Vector3.Normalize(perp);
            return Vector3.Normalize(dir * MathF.Cos(maxRadians) + perp * MathF.Sin(maxRadians));
        }
    }
}
