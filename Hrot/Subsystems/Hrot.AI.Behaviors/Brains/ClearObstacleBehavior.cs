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
        /// <summary>⭐ CE-4601 (B-4) — a POINT order resolved to a wall or a trunk (None: the obstacle path, or nothing to clear when <see cref="Obstacle"/> is null).</summary>
        public BladeCutTarget Target;
        /// <summary>⭐ B-4 — the point (a trunk's centre when the target is a trunk).</summary>
        public Vector3 At;
        /// <summary>⭐ B-4 — the pick radius (m) the resolver used, and the order's give-up time (s).</summary>
        public float PickRadius;
        public float TimeoutSeconds;
        /// <summary>⭐ B-4 — seconds since the point order began.</summary>
        public float Elapsed;
        /// <summary>⭐ B-4 — 1 when the order named a point (so a missing target is a failure, not success).</summary>
        public byte Pointed;
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

        /// <summary>⭐ CE-4601 (B-4, §7.6) — the pick radius round an order's point (m) and the give-up time (s), when the order leaves them 0.</summary>
        public const float DefaultPickRadius = 3f, DefaultTimeoutSeconds = 60f;

        [BehaviorResolver(BehaviorNames.ClearObstacle)]
        public static void Resolve(in ClearObstacleOrder order, ref ClearObstacleBlackboard block, EntityRepository world, Entity self)
        {
            block.Clear = new ClearObstacleParams
            {
                Obstacle = order.Obstacle.Ref.Resolve(world),
                Speed = order.Speed > 0f ? order.Speed : DefaultSpeed,
                PushSeconds = order.PushSeconds > 0f ? order.PushSeconds : DefaultPushSeconds,
                PickRadius = order.PickRadius > 0f ? order.PickRadius : DefaultPickRadius,
                TimeoutSeconds = order.TimeoutSeconds > 0f ? order.TimeoutSeconds : DefaultTimeoutSeconds,
            };
            // ⭐⭐ CE-4601 (B-4; 📄 docs/DESIGN_Vehicle_Breaching.md §7.6, R-4242) — no entity, a POINT: an obstacle within the pick radius ⇒ that obstacle (today's path);
            //   else a standing lone trunk ⇒ felled; else the nearest wall panel ⇒ a blade-wide cut. Nothing there ⇒ the order fails at its first tick.
            Vector3 point = order.At.Local;
            if (block.Clear.Obstacle.IsNull && point != Vector3.Zero)
            {
                block.Clear.Pointed = 1;
                block.Clear.At = point;
                Resolve(ref block.Clear, world);
                // the marker exists from the order on (inactive): the tick only SETS its own component — no structural change inside the parallel phase (R-4212 / D-31)
                if (block.Clear.Target != BladeCutTarget.None && world.IsComponentTypeRegistered<BladeCut>() && !world.HasComponent<BladeCut>(self))
                    world.AddComponent(self, new BladeCut());
            }
        }

        private static void Resolve(ref ClearObstacleParams p, EntityRepository world)
        {
            var at = new Vector2(p.At.X, p.At.Y);
            float pick = p.PickRadius;
            if (world.IsComponentTypeRegistered<StaticObstacle>() && world.IsComponentTypeRegistered<SimTransform>())
            {
                Entity best = Entity.Null; float bestD = float.MaxValue;
                foreach (var e in world.Query().With<StaticObstacle>().With<SimTransform>().Build())
                {
                    var face = Face(world, e, p.At);
                    float d = Vector2.Distance(new Vector2(face.X, face.Y), at);
                    if (d <= pick && d < bestD) { bestD = d; best = e; }
                }
                if (!best.IsNull) { p.Obstacle = best; return; }
            }
            var terrain = Fdp.Toolkit.Spatial.Eqs.EqsTerrainSight.World(world);
            if (terrain == null) return;
            var near = new System.Collections.Generic.List<int>();
            LoneTrees.Near(terrain, at, pick, near);
            var felled = DoorStates.Of(world, terrain).Trees;
            int trunk = -1; float trunkD = float.MaxValue;
            foreach (int i in near)
            {
                var pr = terrain.Prisms[i];
                if (felled != null && felled.Felled(pr)) continue;
                float d = Vector2.Distance((pr.Min + pr.Max) * 0.5f, at);
                if (d < trunkD) { trunkD = d; trunk = i; }
            }
            if (trunk >= 0)
            {
                var c = (terrain.Prisms[trunk].Min + terrain.Prisms[trunk].Max) * 0.5f;
                p.Target = BladeCutTarget.Tree;
                p.At = new Vector3(c, p.At.Z);
                return;
            }
            if (DozerCutSystem.NearestPanel(terrain, at, pick, near) >= 0) p.Target = BladeCutTarget.Wall;
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
            if (p.Target != BladeCutTarget.None) return Cut(ref p, self, world);   // ⭐ CE-4601 (B-4) — a wall or a trunk at a point
            if (p.Pointed == 1 && p.Obstacle.IsNull) { StopMove(ref p, self, world); return NodeStatus.Failure; }   // a point with nothing to clear
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

        /// <summary>
        /// ⭐⭐ <c>CE-4601</c> (B-4; §7.6) — a point order on a wall or a trunk: done when a Passage covers the point (or the trunk is felled) in this view; given up after the
        /// order's timeout; else drive to the stand-off before the point on the dozer's side and, with the blade there, MARK it (<see cref="BladeCut"/>, its OWN component —
        /// R-4212) for the Muscle's <see cref="DozerCutSystem"/> to cut. ⛔ The brain never writes the breach (V-13).
        /// </summary>
        private static NodeStatus Cut(ref ClearObstacleParams p, Entity self, EntityRepository world)
        {
            var terrain = Fdp.Toolkit.Spatial.Eqs.EqsTerrainSight.World(world);
            if (terrain == null || !world.HasComponent<SimTransform>(self)) { EndCut(ref p, self, world); return NodeStatus.Failure; }
            var doors = DoorStates.Of(world, terrain);
            var at2 = new Vector2(p.At.X, p.At.Y);
            bool done = p.Target == BladeCutTarget.Tree
                ? doors.Trees != null && Felled(terrain, doors.Trees, at2)
                : DozerCutSystem.PassageAt(terrain, doors, at2, p.PickRadius, t_scratch ??= new System.Collections.Generic.List<int>());
            if (done) { EndCut(ref p, self, world); return NodeStatus.Success; }
            float dt = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().DeltaTime : 0f;
            p.Elapsed += dt;
            if (p.Elapsed > p.TimeoutSeconds) { EndCut(ref p, self, world); return NodeStatus.Failure; }

            var tf = world.GetComponentRO<SimTransform>(self);
            float half = 3f;
            if (world.IsComponentTypeRegistered<global::CarKinem.Core.VehicleParams>() && world.HasComponent<global::CarKinem.Core.VehicleParams>(self))
                half = MathF.Max(1f, world.GetComponentRO<global::CarKinem.Core.VehicleParams>(self).Length * 0.5f);
            var here = new Vector2(tf.Position.X, tf.Position.Y);
            var to = at2 - here;
            float dist = to.Length();
            if (dist <= half + DozerCutSystem.ReachMetres)
            {
                StopMove(ref p, self, world);
                Mark(world, self, new BladeCut { Active = 1, Target = p.Target, At = p.At, PushSeconds = p.PushSeconds });
                return NodeStatus.Running;
            }
            if (p.Moving == 1 && LocomotionMoveTo.Status(world, self) == NodeStatus.Running) return NodeStatus.Running;
            var goal = dist > 1e-3f ? at2 - to / dist * (half + 0.5f) : here;
            p.Moving = (byte)(LocomotionMoveTo.Issue(world, self, new Vector3(goal, tf.Position.Z), p.Speed, ArrivalRadius, MoveGait.Tactical, onSurface: true,
                                                     source: LocomotionMoveTo.OrderSource(world, self)) ? 1 : 0);
            return NodeStatus.Running;
        }

        [ThreadStatic] private static System.Collections.Generic.List<int>? t_scratch;

        private static bool Felled(TerrainWorld terrain, FelledTrees felled, Vector2 at)
        {
            var near = t_scratch ??= new System.Collections.Generic.List<int>();
            near.Clear();
            LoneTrees.Near(terrain, at, 0.5f, near);
            foreach (int i in near) if (felled.Felled(terrain.Prisms[i])) return true;
            return near.Count == 0;   // the trunk is no longer in this world (a bake dropped it)
        }

        private static void Mark(EntityRepository world, Entity self, in BladeCut cut)
        {
            if (!world.IsComponentTypeRegistered<BladeCut>()) return;
            if (!world.HasComponent<BladeCut>(self)) return;   // the resolver adds it; ⛔ never a structural change from the phase
            var have = world.GetComponentRO<BladeCut>(self);
            if (have.Active != cut.Active || have.Target != cut.Target || have.At != cut.At || have.PushSeconds != cut.PushSeconds) world.SetComponent(self, cut);
        }

        private static void EndCut(ref ClearObstacleParams p, Entity self, EntityRepository world)
        {
            StopMove(ref p, self, world);
            if (world.IsComponentTypeRegistered<BladeCut>() && world.HasComponent<BladeCut>(self) && world.GetComponentRO<BladeCut>(self).Active != 0)
                world.SetComponent(self, new BladeCut());
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

        /// <summary>⭐ CE-4601 (B-4, §7.6) — clear HERE instead of an entity (zero = no point): an obstacle within <see cref="PickRadius"/> of it, else a lone trunk, else the wall.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("At")]
        public Fdp.Toolkit.Replication.PointLink At;   // ⭐ S0 — an authored place is a PointLink (a picked point, a param, a reference)

        /// <summary>⭐ B-4 — how far from <see cref="At"/> a target is looked for (m); 0 = 3.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("PickRadius")]
        public float PickRadius;

        /// <summary>⭐ B-4 — a point order gives up after this long (s); 0 = 60.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("TimeoutSeconds")]
        public float TimeoutSeconds;
    }
}
