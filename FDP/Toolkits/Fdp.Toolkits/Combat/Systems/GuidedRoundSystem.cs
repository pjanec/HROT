using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
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
    public sealed class GuidedRoundSystem : IEcsModuleSystem
    {
        /// <summary>The steering limit (D-D: ≤ 30°/s).</summary>
        public const float TurnRateDegPerSec = 30f;

        /// <summary>A command round aims this far (seconds of flight) ahead of its own point on the sight line.</summary>
        public const float CommandLookaheadSeconds = 0.5f;

        /// <summary>A top-attack round climbs at this angle.</summary>
        public const float ClimbAngleDeg = 30f;

        private EntityQuery? _rounds;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (deltaTime <= 0f || view is not EntityRepository repo || !repo.IsComponentTypeRegistered<GuidedRound>()) return;
            var world = Fdp.Toolkit.World.WorldQuery.Of(repo);
            _rounds = repo.Query().With<GuidedRound>().With<SimTransform>().With<SimVelocity>().Build();
            foreach (var e in _rounds)
            {
                ref var g = ref repo.GetComponentRW<GuidedRound>(e);
                if (g.Guidance == GuidanceKind.Unguided || g.Phase == GuidedPhase.Lost) continue;
                if (repo.HasComponent<BallisticProjectile>(e) && repo.GetComponentRO<BallisticProjectile>(e).StoppedTick != 0) continue;
                var pos = repo.GetComponentRO<SimTransform>(e).Position;
                ref var vel = ref repo.GetComponentRW<SimVelocity>(e).Linear;
                if (!Steer(repo, world, ref g, pos, out var desired)) continue;
                vel = Turn(vel, desired, TurnRateDegPerSec * (MathF.PI / 180f) * deltaTime) * g.SpeedMps;
            }
        }

        /// <summary>The direction the round wants to fly, or false when it flies on as it is (lost, no target).</summary>
        private static bool Steer(EntityRepository repo, Fdp.Toolkit.World.IWorldQuery? world, ref GuidedRound g, Vector3 pos, out Vector3 desired)
        {
            desired = default;
            bool targetKnown = repo.IsAlive(g.Target) && repo.HasComponent<SimTransform>(g.Target);
            var targetPoint = targetKnown ? AimPoint.Middle(repo, g.Target, out _, out _) : g.LastSeen;

            if (g.Guidance == GuidanceKind.Command)
            {
                if (!targetKnown || !repo.IsAlive(g.Gunner) || !repo.HasComponent<SimTransform>(g.Gunner)) { g.Phase = GuidedPhase.Lost; return false; }
                var eye = repo.GetComponentRO<SimTransform>(g.Gunner).Position;
                eye.Z += Fdp.Toolkit.Perception.LineOfSight.TerrainWorldLosStrategy.EyeHeightFor(repo, g.Gunner, HitModel.LogicalStance(repo, g.Gunner));
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

            // fire-and-forget: the round's own seeker
            if (targetKnown && (world == null || !world.SightBlocked(pos, targetPoint))) g.LastSeen = targetPoint;
            var goal = g.LastSeen;
            var toGoal = goal - pos;
            if (toGoal.LengthSquared() < 1e-6f) return false;
            if (g.TopAttack == 0 || g.Phase == GuidedPhase.Dive) { desired = Vector3.Normalize(toGoal); return true; }

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
                    float c = MathF.Cos(ClimbAngleDeg * (MathF.PI / 180f)), s = MathF.Sin(ClimbAngleDeg * (MathF.PI / 180f));
                    desired = new Vector3(h.X * c, h.Y * c, s);
                    return true;
                }
            }
            // cruise: level at the cruise height until the dive must begin — the turn onto the dive angle is an arc of radius
            // speed / turn rate (R), so it starts R·sin(dive) + the rest of the height at the dive angle before the target
            float r = g.SpeedMps / (TurnRateDegPerSec * (MathF.PI / 180f));
            float lead = r * MathF.Sin(dive) + MathF.Max(0f, height - r * (1f - MathF.Cos(dive))) / MathF.Tan(dive);
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
