using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Perception.Look
{
    /// <summary>
    /// ⭐ <c>CE-3252</c> (LD-1, LD-C, R-2049) — the HEAD, on the node that owns the body, right after the gait moved it this frame
    /// (<c>GroundKinematicsModule</c>, after <c>CarKinematicsSystem</c>):
    /// <list type="bullet">
    /// <item>aiming ⇒ the eyes follow the aim (W-14 turns the body; the aim wins by construction);</item>
    /// <item>no look ⇒ the head returns to the body;</item>
    /// <item>standing with a look ⇒ the head leads while the gait turns the body (a lasting look ends with the body facing it);</item>
    /// <item>WALKING with a look off his way ⇒ a GLANCE — the head turns at most <see cref="LookOf.GlanceSeconds"/>, then rests on the
    ///   way at least <see cref="LookOf.RestSeconds"/>; never a long stare over the shoulder.</item>
    /// </list>
    /// A target ENTITY is followed: <see cref="LookState.Target"/> is refreshed from its position. Writes <see cref="LookState.HeadYaw"/>,
    /// <see cref="LookState.Source"/>. 📄 docs/designs/look-direction/DESIGN_Look_Direction_And_FOV.md §3.2a, §3.2b.
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public sealed class LookFollowSystem : IEcsModuleSystem
    {
        /// <summary>Above this speed (m/s) a person walks: a look off his way is a glance.</summary>
        public const float WalkingSpeed = 0.3f;

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.IsComponentTypeRegistered<LookState>()) return;
            double now = view.Time;
            bool kinem = repo.IsComponentTypeRegistered<global::CarKinem.Core.VehicleState>();
            foreach (var e in repo.Query().With<LookState>().With<SimTransform>().Build())
            {
                ref var look = ref repo.GetComponentRW<LookState>(e);
                var tf = repo.GetComponentRO<SimTransform>(e);
                float body = LookOf.YawOf(tf.Rotation);
                float speed = kinem && repo.HasComponent<global::CarKinem.Core.VehicleState>(e)
                    ? MathF.Abs(repo.GetComponentRO<global::CarKinem.Core.VehicleState>(e).Speed) : 0f;

                if (look.HasTarget != 0 && !look.TargetEntity.IsNull)
                {
                    if (repo.IsAlive(look.TargetEntity) && repo.HasComponent<SimTransform>(look.TargetEntity))
                        look.Target = repo.GetComponentRO<SimTransform>(look.TargetEntity).Position;
                    else look.TargetEntity = Entity.Null;   // gone: keep looking where it was last
                }

                if (Combat.WeaponPostureOf.TryAimPoint(repo, e, out var aim))
                {
                    look.Source = LookSource.LookAim;
                    look.HeadYaw = Toward(body, tf.Position, aim);
                    look.GlanceSince = 0;
                    continue;
                }
                if (look.HasTarget == 0)
                {
                    look.Source = LookSource.LookFree;
                    look.HeadYaw = 0f;
                    look.GlanceSince = 0;
                    continue;
                }
                float want = Toward(body, tf.Position, look.Target);
                if (speed <= WalkingSpeed)
                {
                    // standing: the head leads; CarKinematicsSystem turns the body after it, so the head comes back as it does
                    look.Source = LookSource.LookOrdered;
                    look.HeadYaw = want;
                    look.GlanceSince = 0;
                    continue;
                }
                // walking: glances only (LD-C)
                if (now < look.RestUntil || MathF.Abs(want) < 1e-3f)
                {
                    look.Source = LookSource.LookOrdered;
                    look.HeadYaw = 0f;
                    look.GlanceSince = 0;
                    continue;
                }
                if (look.GlanceSince <= 0) look.GlanceSince = Math.Max(now, 1e-6);   // 0 means "none", so a glance at t = 0 still ends
                if (now - look.GlanceSince > LookOf.GlanceSeconds)
                {
                    look.GlanceSince = 0;
                    look.RestUntil = now + LookOf.RestSeconds;
                    look.Source = LookSource.LookOrdered;
                    look.HeadYaw = 0f;
                    continue;
                }
                look.Source = LookSource.LookGlance;
                look.HeadYaw = want;
            }
        }

        /// <summary>The head's turn from <paramref name="bodyYaw"/> toward <paramref name="point"/>, clamped to the neck.</summary>
        public static float Toward(float bodyYaw, Vector3 from, Vector3 point)
        {
            var d = new Vector2(point.X - from.X, point.Y - from.Y);
            if (d.LengthSquared() < 1e-6f) return 0f;
            float off = LookOf.Wrap(MathF.Atan2(d.Y, d.X) - bodyYaw);
            return Math.Clamp(off, -LookOf.MaxHeadYaw, LookOf.MaxHeadYaw);
        }
    }
}
