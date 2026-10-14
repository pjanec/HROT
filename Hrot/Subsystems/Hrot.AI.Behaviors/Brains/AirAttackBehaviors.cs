using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Motion;
using Hrot.AI.Behaviors.Logging;
using Hrot.AI.Behaviors.Orders;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-2173</c> slice 5 (B4, B5) — the attack pattern of <see cref="AirAttackNodes.Run"/>; <see cref="Auto"/> = picked by
    /// <see cref="AirAttackProfiles.Pick"/> from the aircraft and its weapons. Only <see cref="LevelBomb"/> is built (Air Operations §7).</summary>
    public enum AirAttackProfile : byte
    {
        Auto = 0,
        /// <summary>A plane flies level over the point and its weapon brain releases a bomb on the cue (§8).</summary>
        LevelBomb = 1,
    }

    /// <summary>⭐ <c>CE-2173</c> slice 5 — what an air attack is told: the point and the numbers of the pass (0 = automatic).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AirAttackParams
    {
        /// <summary>The aim point (world metres; Z = the ground there).</summary>
        public Vector3 Point;
        public AirAttackProfile Profile;
        /// <summary>1 ⇒ <see cref="RunInBearingDeg"/> is the run-in's compass bearing; 0 ⇒ straight at the point from where the aircraft is.</summary>
        public byte BearingGiven;
        /// <summary>The run-in's compass bearing (degrees, 0 = north, clockwise).</summary>
        public float RunInBearingDeg;
        /// <summary>How far before the point the run-in starts (m); 0 ⇒ max(<see cref="AirAttackNodes.DefaultRunInM"/>, 2.5 turn radii).</summary>
        public float RunInDistanceM;
        /// <summary>The height of the pass above the point (m); 0 ⇒ <see cref="AirAttackNodes.DefaultAttackHeightAgl"/>.</summary>
        public float AttackHeightAgl;
        /// <summary>The pass speed (m/s); 0 ⇒ the aircraft's own.</summary>
        public float Speed;
    }

    /// <summary>⭐ <c>CE-2173</c> slice 5 — the attack's working state (the stage of the pass, the weapon's release node).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AirAttackState
    {
        public byte Stage;
        public byte Passes;
        public byte Mount;
        public AirAttackProfile Profile;
        /// <summary>The run-in direction (unit, horizontal).</summary>
        public Vector2 Dir;
        public double NextSteer;
        public FireAtPointNodeParams Fire;
    }

    /// <summary>⭐ <c>CE-2173</c> slice 5 (B4) — which profile an aircraft flies against a point: a plane with a bomb ⇒ LevelBomb. Null = no
    /// profile built for this aircraft (the attack fails, loudly).</summary>
    public static class AirAttackProfiles
    {
        public const int MaxMounts = 16;

        public static AirAttackProfile? Pick(EntityRepository world, Entity aircraft, out byte mount)
        {
            mount = 0;
            if (Locomotions.Of(world, aircraft) != Fdp.Toolkit.Tkb.Domain.LocomotionClass.FixedWing) return null;
            for (int i = 0; i < MaxMounts; i++)
            {
                var m = CombatTkb.MountOf(world, aircraft, i);
                if (m == null) break;
                if (m.Category == Fdp.Toolkit.Tkb.Domain.WeaponCategory.Bomb) { mount = (byte)i; return AirAttackProfile.LevelBomb; }
            }
            return null;
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2173</c> slice 5 (B4, B6, A2, A6; 📄 docs/designs/air-attack/DESIGN_Air_Operations.md §7, §8, §9c) — ONE air attack, the
    /// profile a parameter. <b>LevelBomb</b>: ① (a bearing given) fly to the run-in start, <see cref="AirAttackParams.RunInDistanceM"/>
    /// before the point on that bearing; ② the run-in — steer at a point BEYOND the target on the line through it (so the plane's
    /// arrival circle never starts before the release), at the pass height above the ground there, while the weapon brain holds the
    /// bomb (<c>FireAtPoint</c>, one round) for the release cue; ③ once released, fly on and out. A pass that overflies without a release
    /// turns back for another (3 at most).
    /// </summary>
    public static class AirAttackNodes
    {
        public const float DefaultRunInM = 4000f;
        public const float DefaultAttackHeightAgl = 300f;
        /// <summary>How far past the point the run-in steers (m) — beyond the plane's arrival circle, so it flies straight over the point.</summary>
        public const float RunOutM = 4000f;
        /// <summary>The run-in steering is refreshed this often (s) while farther than <see cref="SteerFreezeM"/> from the point.</summary>
        public const double SteerPeriod = 0.5;
        public const float SteerFreezeM = 2500f;
        public const int MaxPasses = 3;

        private const byte StageStart = 0, StageToRunIn = 1, StageRunIn = 2, StageOut = 3;

        public static NodeStatus Run(ref AirAttackParams p, ref AirAttackState ws, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<SimTransform>(self)) return NodeStatus.Failure;
            var at = world.GetComponentRO<SimTransform>(self).Position;
            var target = p.Point;
            double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d;

            if (ws.Stage == StageStart)
            {
                var picked = p.Profile != AirAttackProfile.Auto ? p.Profile : AirAttackProfiles.Pick(world, self, out ws.Mount);
                if (picked != AirAttackProfile.LevelBomb)
                {
                    BehaviorLog.Error(self, world, "AirAttack: no attack profile for this aircraft and its weapons (only LevelBomb — a plane with a bomb — is built).");
                    return NodeStatus.Failure;
                }
                if (p.Profile != AirAttackProfile.Auto) AirAttackProfiles.Pick(world, self, out ws.Mount);
                ws.Profile = picked.Value;
                ws.Dir = p.BearingGiven != 0
                    ? new Vector2(MathF.Sin(p.RunInBearingDeg * MathF.PI / 180f), MathF.Cos(p.RunInBearingDeg * MathF.PI / 180f))
                    : Flat(target - at);
                if (p.BearingGiven != 0)
                {
                    var start = target - new Vector3(ws.Dir * RunIn(in p, world, self), 0f);
                    if (!Fly(world, self, start, in p)) return NodeStatus.Failure;
                    ws.Stage = StageToRunIn;
                    return NodeStatus.Running;
                }
                ws.Stage = StageRunIn;
                ws.NextSteer = 0;
            }

            if (ws.Stage == StageToRunIn)
            {
                var s = LocomotionMoveTo.Status(world, self);
                if (s == NodeStatus.Failure) return NodeStatus.Failure;
                if (s == NodeStatus.Running) return NodeStatus.Running;
                ws.Stage = StageRunIn;
                ws.NextSteer = 0;
            }

            if (ws.Stage == StageRunIn)
            {
                var flat = Flat(target - at);
                float distance = new Vector2(target.X - at.X, target.Y - at.Y).Length();
                float along = Vector2.Dot(new Vector2(target.X - at.X, target.Y - at.Y), ws.Dir);   // > 0 ⇒ the point is still ahead
                if (distance > SteerFreezeM && now >= ws.NextSteer)
                {
                    ws.Dir = flat;
                    if (!Fly(world, self, target + new Vector3(flat * RunOutM, 0f), in p)) return NodeStatus.Failure;
                    ws.NextSteer = now + SteerPeriod;
                }
                var fire = new FireAtPointNodeParams { Point = target, Rounds = 1, Mount = ws.Mount, Started = ws.Fire.Started };
                var released = FireAtPointNodes.FireAtPoint(ref fire, self, world);
                ws.Fire = fire;
                if (released == NodeStatus.Failure) return NodeStatus.Failure;   // no bomb left
                if (released == NodeStatus.Success)
                {
                    ws.Stage = StageOut;
                    if (!Fly(world, self, target + new Vector3(ws.Dir * RunOutM, 0f), in p)) return NodeStatus.Failure;
                    return NodeStatus.Running;
                }
                if (along < -RunOutM * 0.25f)   // overflown without the cue: come round for another pass
                {
                    if (++ws.Passes >= MaxPasses) return NodeStatus.Failure;
                    ws.Dir = -ws.Dir;
                    var start = target - new Vector3(ws.Dir * RunIn(in p, world, self), 0f);
                    if (!Fly(world, self, start, in p)) return NodeStatus.Failure;
                    ws.Stage = StageToRunIn;
                }
                return NodeStatus.Running;
            }

            return LocomotionMoveTo.Status(world, self);   // out: done when the egress point is reached
        }

        private static Vector2 Flat(Vector3 v)
        {
            var f = new Vector2(v.X, v.Y);
            return f.LengthSquared() > 1e-6f ? Vector2.Normalize(f) : new Vector2(0f, 1f);
        }

        private static float RunIn(in AirAttackParams p, EntityRepository world, Entity self)
        {
            if (p.RunInDistanceM > 0f) return p.RunInDistanceM;
            float r = world.HasComponent<FlightParams>(self) ? world.GetComponentRO<FlightParams>(self).TurnRadius : 0f;
            return MathF.Max(DefaultRunInM, 2.5f * r);
        }

        /// <summary>A MoveTo to (x, y) at the pass height above the ground at the TARGET (the run is flown level).</summary>
        private static bool Fly(EntityRepository world, Entity self, Vector3 to, in AirAttackParams p)
        {
            float height = p.AttackHeightAgl > 0f ? p.AttackHeightAgl : DefaultAttackHeightAgl;
            return LocomotionMoveTo.Issue(world, self, new Vector3(to.X, to.Y, p.Point.Z + height), p.Speed, AirOrderNodes.DefaultArrivalRadius);
        }
    }

    /// <summary>
    /// ⭐ <c>CE-2173</c> slice 5 (B4, R-278) — STRIKE for every aircraft (<c>AirPlatform</c>): the one <see cref="AirAttackNodes.Run"/> on the
    /// order's point (an order's <c>Target</c>, when alive, is struck where it stands now), the profile picked automatically.
    /// </summary>
    public static class AirPlatformStrikeBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct AirPlatformStrikeBlackboard
        {
            public AirAttackParams Attack;
            public AirAttackState State;
        }

        [BehaviorResolver(BehaviorNames.AirPlatformStrike)]
        public static void Resolve(in StrikeOrder order, ref AirPlatformStrikeBlackboard block, EntityRepository world, Entity self)
        {
            var target = order.Target.Resolve(world);
            Vector3 point;
            if (!target.IsNull && world.IsAlive(target) && world.HasComponent<SimTransform>(target))
                point = world.GetComponentRO<SimTransform>(target).Position;
            else
            {
                var at = OrderGeo.ToLocal(world, order.Location, 0f);
                point = new Vector3(at.X, at.Y, Fdp.Toolkit.World.WorldQuery.Of(world)?.GroundHeightAt(at.X, at.Y) ?? 0f);
            }
            block.Attack = new AirAttackParams { Point = point };
            block.State = default;
        }

        [Behavior(BehaviorNames.AirPlatformStrike, Params = typeof(StrikeOrder), Categories = BehaviorCategory.MilitaryApc)]
        [BTreeDefinition(BehaviorNames.AirPlatformStrike, Curated = true, Group = "Library/Orders", ParamsType = typeof(AirPlatformStrikeBlackboard))]
        public static BTreeBuilder<AirPlatformStrikeBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<AirPlatformStrikeBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Attack, bb => bb.State, AirAttackNodes.Run);
    }
}
