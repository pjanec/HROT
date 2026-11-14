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
using Fdp.Toolkit.Terrain;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-7012</c> (City Battle V-2) — the bulldozer's clearing task: which obstacle, and its working state.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ClearObstacleParams
    {
        public Entity Obstacle;
        /// <summary>Drive speed to the blocker (m/s).</summary>
        public float Speed;
        /// <summary>Seconds of blade contact that empty ANY obstacle (the push is <c>Health.Max / PushSeconds</c> per second).</summary>
        public float PushSeconds;
        /// <summary>1 while a MoveTo to the blocker runs.</summary>
        public byte Moving;
        /// <summary>Total hit points pushed off (a test / diagnostics probe).</summary>
        public float Pushed;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-7012</c> (City Battle §11 V-2; 📄 docs/DESIGN_Peek_And_Fire.md §9.9, the blade's seam) — <c>ClearObstacle</c>: the bulldozer drives to the
    /// blocker's NEAREST FACE, and while its blade is within <see cref="ContactMetres"/> of it pushes <c>Health.Max / PushSeconds × dt</c> each tick through
    /// <see cref="ObstacleDamage.Damage"/> — the one <see cref="Fdp.Toolkit.Combat.Events.DamageAssessedEvent"/> path, so the owner, the removal and the
    /// rebake are CE-6003's, unchanged. Success when the obstacle is gone (or empty); failure when it is not a destructible obstacle.
    /// <para>⭐ Parallel (R-4212): the dozer changes only its OWN locomotion channel; the obstacle is changed by the damage EVENT, which in the phase rides the
    /// batch's buffer (<c>FdpEventBus.Publish</c> → <c>BatchContext.Commands</c>) and lands next tick. Its reads — <c>Health</c>, <c>SimTransform</c>,
    /// <c>StaticObstacle</c>, <c>ObstacleShape</c> — are declared (<c>BrainReads</c>); nobody writes the last two in the phase.</para>
    /// </summary>
    public static class ClearObstacleBehavior
    {
        /// <summary>Lane default (R-7004): any obstacle is cleared in 10 s of contact — the handoff's frame.</summary>
        public const float DefaultPushSeconds = 10f;
        public const float DefaultSpeed = 4f;
        /// <summary>Hull centre to the blocker's face (m) at which the blade is on it: half a dozer (≈ 3.5 m) + the blade + slack.</summary>
        public const float ContactMetres = 6f;
        /// <summary>The drive stops this far short of the face (m) — the hull centre, the blade then on it.</summary>
        public const float StandOffMetres = 4f;
        public const float ArrivalRadius = 1.5f;

        [StructLayout(LayoutKind.Sequential)]
        public struct ClearObstacleBlackboard
        {
            public ClearObstacleParams Clear;
        }

        [BehaviorResolver(BehaviorNames.ClearObstacle)]
        public static void Resolve(in ClearObstacleOrder order, ref ClearObstacleBlackboard block, EntityRepository world, Entity self)
        {
            block.Clear = new ClearObstacleParams
            {
                Obstacle = order.Obstacle.Ref.Resolve(world),
                Speed = order.Speed > 0f ? order.Speed : DefaultSpeed,
                PushSeconds = order.PushSeconds > 0f ? order.PushSeconds : DefaultPushSeconds,
            };
        }

        [Behavior(BehaviorNames.ClearObstacle, Params = typeof(ClearObstacleOrder), Categories = BehaviorCategory.MilitaryApc,
                  Description = "Drives to an obstacle and pushes it away with the blade (a bulldozer).")]
        [BTreeDefinition(BehaviorNames.ClearObstacle, Curated = true, Group = "Library/Engineer", ParamsType = typeof(ClearObstacleBlackboard))]
        public static BTreeBuilder<ClearObstacleBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<ClearObstacleBlackboard, BTreeContext>()
                .Action(bb => bb.Clear, Clear);

        /// <summary>One tick: gone ⇒ done; blade on it ⇒ push; else drive (again) to its nearest face.</summary>
        [SharedAiAction]
        public static NodeStatus Clear(ref ClearObstacleParams p, Entity self, EntityRepository world)
        {
            var o = p.Obstacle;
            if (o.IsNull || !world.IsAlive(o) || (world.HasComponent<Health>(o) && world.GetComponentRO<Health>(o).Current <= 0f))
            {
                StopMove(ref p, self, world);
                return NodeStatus.Success;
            }
            if (!ObstacleDamage.IsDestructible(world, o) || !world.HasComponent<SimTransform>(self) || !world.HasComponent<SimTransform>(o))
            {
                StopMove(ref p, self, world);
                return NodeStatus.Failure;
            }
            Vector3 at = world.GetComponentRO<SimTransform>(self).Position;
            var face = Face(world, o, at);
            var flat = new Vector2(face.X - at.X, face.Y - at.Y);
            float dist = flat.Length();
            if (dist <= ContactMetres)
            {
                StopMove(ref p, self, world);
                ref readonly var hp = ref world.GetComponentRO<Health>(o);
                float dt = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().DeltaTime : 0f;
                float push = hp.Max / MathF.Max(p.PushSeconds, 0.1f) * dt;
                if (ObstacleDamage.Damage(world, o, push, self)) p.Pushed += push;
                return NodeStatus.Running;
            }
            if (p.Moving == 1 && LocomotionMoveTo.Status(world, self) == NodeStatus.Running) return NodeStatus.Running;
            // the drive ended short (or never started): (re)issue it to the point StandOffMetres in front of the face
            Vector3 to = dist > 1e-3f ? new Vector3(face.X - flat.X / dist * StandOffMetres, face.Y - flat.Y / dist * StandOffMetres, at.Z) : at;
            p.Moving = (byte)(LocomotionMoveTo.Issue(world, self, to, p.Speed, ArrivalRadius, MoveGait.Tactical, onSurface: true,
                                                     source: LocomotionMoveTo.OrderSource(world, self)) ? 1 : 0);
            return NodeStatus.Running;
        }

        /// <summary>The obstacle's point nearest to <paramref name="q"/> (its box, <see cref="ObstacleDamage.NearestPoint"/>; its origin with no shape).</summary>
        internal static Vector3 Face(EntityRepository world, Entity o, Vector3 q)
        {
            ref readonly var tf = ref world.GetComponentRO<SimTransform>(o);
            if (!world.IsComponentTypeRegistered<ObstacleShape>() || !world.HasComponent<ObstacleShape>(o)) return tf.Position;
            return ObstacleDamage.NearestPoint(tf.Position, TerrainObstacles.Yaw(tf.Rotation), in world.GetComponentRO<ObstacleShape>(o), q);
        }

        private static void StopMove(ref ClearObstacleParams p, Entity self, EntityRepository world)
        {
            if (p.Moving == 1) LocomotionMoveTo.Stop(world, self);
            p.Moving = 0;
        }
    }

    /// <summary>⭐ <c>CE-7012</c> — the authored order: the obstacle to clear; a zero field keeps its default.</summary>
    [Fdp.Core.ParamsDto]
    [StructLayout(LayoutKind.Sequential)]
    public struct ClearObstacleOrder
    {
        public const string BehaviorId = BehaviorNames.ClearObstacle;

        /// <summary>The obstacle (a roadblock, a car, sandbags…) to push away.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("Obstacle")]
        public Fdp.Toolkit.Replication.EntityLink Obstacle;

        /// <summary>Drive speed (m/s); 0 = 4.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("Speed")]
        public float Speed;

        /// <summary>Seconds of contact that clear any obstacle; 0 = 10.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("PushSeconds")]
        public float PushSeconds;
    }
}
