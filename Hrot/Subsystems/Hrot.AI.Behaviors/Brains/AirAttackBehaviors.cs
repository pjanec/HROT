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
    /// <see cref="AirAttackProfiles.Pick(EntityRepository, Entity, out byte, out byte)"/> from the aircraft and its weapons (Air Operations §7, §9d).</summary>
    public enum AirAttackProfile : byte
    {
        Auto = 0,
        /// <summary>A plane flies level over the point and its weapon brain releases a bomb on the cue (§8).</summary>
        LevelBomb = 1,
        /// <summary>⭐ Slice 6 — a plane flies a low pass at the point and ripples its rocket pod inside the fire window, then flies through.</summary>
        RocketRun = 2,
        /// <summary>⭐ Slice 6 — a helicopter runs in low at the point: its rockets inside the fire window, then its gun on the move, and it
        /// BREAKS OFF short of the point (it never overflies).</summary>
        RunningFire = 3,
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
        /// <summary>⭐ Slice 6 — rockets per pass; 0 ⇒ <see cref="AirAttackNodes.DefaultRipple"/>.</summary>
        public int Ripple;
        /// <summary>⭐ Slice 6 — the rockets fire from this range in (m); 0 ⇒ min(<see cref="AirAttackNodes.DefaultRocketFireM"/>, the pod's range).</summary>
        public float FireRangeM;
        /// <summary>⭐ Slice 6 — a helicopter breaks off this far from the point (m); 0 ⇒ <see cref="AirAttackNodes.DefaultBreakM"/>.</summary>
        public float BreakRangeM;
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
        /// <summary>⭐ Slice 6 — the gun mount of a running fire (<see cref="AirAttackProfiles.None"/> = none); <see cref="Mount"/> is the rockets'.</summary>
        public byte GunMount;
        /// <summary>⭐ Slice 6 — which weapon the pass is on: 0 the rockets, 1 the gun, 2 done.</summary>
        public byte Weapon;
        /// <summary>⭐ Slice 6 — 1 once a round left this pass.</summary>
        public byte Fired;
    }

    /// <summary>⭐ <c>CE-2173</c> slice 5 (B4) / slice 6 — which profile an aircraft flies against a point (Air Operations §7's table):
    /// a plane with a bomb ⇒ LevelBomb; a plane with a rocket pod ⇒ RocketRun; a helicopter with a rocket pod or a gun ⇒ RunningFire.
    /// Null = no profile built for this aircraft (the attack fails, loudly) — ⚠ a plane with only a gun (GunRun) is not built yet.</summary>
    public static class AirAttackProfiles
    {
        public const int MaxMounts = 16;
        /// <summary>No mount.</summary>
        public const byte None = 0xFF;

        public static AirAttackProfile? Pick(EntityRepository world, Entity aircraft, out byte mount)
            => Pick(world, aircraft, out mount, out _);

        /// <summary>The profile, its main mount (the bomb, the rockets — <see cref="None"/> when a running fire has only a gun) and a
        /// running fire's gun (<see cref="None"/> otherwise).</summary>
        public static AirAttackProfile? Pick(EntityRepository world, Entity aircraft, out byte mount, out byte gun)
        {
            Scan(world, aircraft, out var bomb, out var rockets, out var firstGun);
            var locomotion = Locomotions.Of(world, aircraft);
            AirAttackProfile? picked =
                locomotion == Fdp.Toolkit.Tkb.Domain.LocomotionClass.FixedWing
                    ? bomb != None ? AirAttackProfile.LevelBomb : rockets != None ? AirAttackProfile.RocketRun : null
                : locomotion == Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft && (rockets != None || firstGun != None)
                    ? AirAttackProfile.RunningFire
                    : null;
            mount = gun = None;
            if (picked is { } p) MountsFor(bomb, rockets, firstGun, p, out mount, out gun);
            return picked;
        }

        /// <summary>The mounts a GIVEN profile flies with on this aircraft; false when it has none for it (a bomb run with no bomb).</summary>
        public static bool MountsFor(EntityRepository world, Entity aircraft, AirAttackProfile profile, out byte mount, out byte gun)
        {
            Scan(world, aircraft, out var bomb, out var rockets, out var firstGun);
            return MountsFor(bomb, rockets, firstGun, profile, out mount, out gun);
        }

        private static bool MountsFor(byte bomb, byte rockets, byte firstGun, AirAttackProfile profile, out byte mount, out byte gun)
        {
            mount = gun = None;
            switch (profile)
            {
                case AirAttackProfile.LevelBomb: mount = bomb; return bomb != None;
                case AirAttackProfile.RocketRun: mount = rockets; return rockets != None;
                case AirAttackProfile.RunningFire: mount = rockets; gun = firstGun; return rockets != None || firstGun != None;
                default: return false;
            }
        }

        /// <summary>The first bomb, rocket pod and gun (machine gun or autocannon) in the aircraft's weapon suite.</summary>
        private static void Scan(EntityRepository world, Entity aircraft, out byte bomb, out byte rockets, out byte gun)
        {
            bomb = rockets = gun = None;
            for (int i = 0; i < MaxMounts; i++)
            {
                var m = CombatTkb.MountOf(world, aircraft, i);
                if (m == null) break;
                switch (m.Category)
                {
                    case Fdp.Toolkit.Tkb.Domain.WeaponCategory.Bomb: if (bomb == None) bomb = (byte)i; break;
                    case Fdp.Toolkit.Tkb.Domain.WeaponCategory.Rocket: if (rockets == None) rockets = (byte)i; break;
                    case Fdp.Toolkit.Tkb.Domain.WeaponCategory.MachineGun or Fdp.Toolkit.Tkb.Domain.WeaponCategory.Autocannon:
                        if (gun == None) gun = (byte)i; break;
                }
            }
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2173</c> slice 5 (B4, B6, A2, A6) / slice 6 (📄 docs/designs/air-attack/DESIGN_Air_Operations.md §7, §8, §9c, §9d) — ONE
    /// air attack, the profile a parameter. Every profile: ① (a bearing given) fly to the run-in start,
    /// <see cref="AirAttackParams.RunInDistanceM"/> before the point on that bearing; ② the run-in — steer at a point BEYOND the target on
    /// the line through it (so the arrival circle never starts before the weapon), at the pass height above the ground there.
    /// <b>LevelBomb</b>: the weapon brain holds the bomb (<c>FireAtPoint</c>, one round) for the release cue; once released, fly on and out.
    /// <b>RocketRun</b> (a plane): the pod ripples inside the fire window (<see cref="AirAttackParams.FireRangeM"/> down to the break
    /// range), then the plane flies through and out. <b>RunningFire</b> (a helicopter): the rockets in the same window, then the gun from
    /// 0.9 × its range, and at the break range it turns away to the right and flies out — it never overflies the point. A pass that ends
    /// with nothing fired comes round for another (3 at most).
    /// </summary>
    public static class AirAttackNodes
    {
        public const float DefaultRunInM = 4000f;
        public const float DefaultAttackHeightAgl = 300f;
        /// <summary>⭐ Slice 6 — a plane's rocket run is flown this low (m above the point).</summary>
        public const float DefaultRocketRunHeightAgl = 150f;
        /// <summary>⭐ Slice 6 — a helicopter's running fire is flown this low (m above the point).</summary>
        public const float DefaultRunningFireHeightAgl = 60f;
        /// <summary>How far past the point the run-in steers (m) — beyond the plane's arrival circle, so it flies straight over the point.</summary>
        public const float RunOutM = 4000f;
        /// <summary>The run-in steering is refreshed this often (s) while farther than <see cref="SteerFreezeM"/> from the point.</summary>
        public const double SteerPeriod = 0.5;
        public const float SteerFreezeM = 2500f;
        public const int MaxPasses = 3;
        /// <summary>⭐ Slice 6 — rockets per pass.</summary>
        public const int DefaultRipple = 8;
        /// <summary>⭐ Slice 6 — the rockets fire from this range in (m), when the pod reaches it.</summary>
        public const float DefaultRocketFireM = 2500f;
        /// <summary>⭐ Slice 6 — a helicopter breaks off this far from the point (m).</summary>
        public const float DefaultBreakM = 400f;
        /// <summary>⭐ Slice 6 — a plane's rockets stop this far from the point (m; it pulls out over the point).</summary>
        public const float DefaultPlaneBreakM = 800f;
        /// <summary>⭐ Slice 6 — a helicopter's break-away point: this far to the right of where it broke off (m), and a quarter of it back.</summary>
        public const float BreakAwayM = 1500f;
        /// <summary>⭐ Slice 6 — seconds between rockets in a ripple (⚠ the TKB has no per-mount rate of fire; the primary's only).</summary>
        public const float RocketIntervalS = 0.25f;
        /// <summary>⭐ Slice 6 — a running fire's gun burst (rounds) and the seconds between them.</summary>
        public const int GunBurst = 30;
        public const float GunIntervalS = 0.1f;

        private const byte StageStart = 0, StageToRunIn = 1, StageRunIn = 2, StageOut = 3;
        private const byte WeaponRockets = 0, WeaponGun = 1, WeaponDone = 2;

        public static NodeStatus Run(ref AirAttackParams p, ref AirAttackState ws, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<SimTransform>(self)) return NodeStatus.Failure;
            var at = world.GetComponentRO<SimTransform>(self).Position;
            var target = p.Point;
            double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d;

            if (ws.Stage == StageStart)
            {
                AirAttackProfile? picked;
                byte mount, gun;
                if (p.Profile == AirAttackProfile.Auto) picked = AirAttackProfiles.Pick(world, self, out mount, out gun);
                else picked = AirAttackProfiles.MountsFor(world, self, p.Profile, out mount, out gun) ? p.Profile : null;
                if (picked == null)
                {
                    BehaviorLog.Error(self, world, p.Profile == AirAttackProfile.Auto
                        ? "AirAttack: no attack profile for this aircraft and its weapons (a plane needs a bomb or a rocket pod; a helicopter rockets or a gun)."
                        : $"AirAttack: this aircraft has no weapon for a {p.Profile}.");
                    return NodeStatus.Failure;
                }
                ws.Profile = picked.Value;
                ws.Mount = mount;
                ws.GunMount = gun;
                ws.Weapon = WeaponRockets;
                ws.Fired = 0;
                ws.Dir = p.BearingGiven != 0
                    ? new Vector2(MathF.Sin(p.RunInBearingDeg * MathF.PI / 180f), MathF.Cos(p.RunInBearingDeg * MathF.PI / 180f))
                    : Flat(target - at);
                if (p.BearingGiven != 0)
                {
                    var start = target - new Vector3(ws.Dir * RunIn(in p, world, self), 0f);
                    if (!Fly(world, self, start, in p, ws.Profile)) return NodeStatus.Failure;
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
                    if (!Fly(world, self, target + new Vector3(flat * RunOutM, 0f), in p, ws.Profile)) return NodeStatus.Failure;
                    ws.NextSteer = now + SteerPeriod;
                }
                if (ws.Profile == AirAttackProfile.LevelBomb)
                {
                    var fire = new FireAtPointNodeParams { Point = target, Rounds = 1, Mount = ws.Mount, Started = ws.Fire.Started };
                    var released = FireAtPointNodes.FireAtPoint(ref fire, self, world);
                    ws.Fire = fire;
                    if (released == NodeStatus.Failure) return NodeStatus.Failure;   // no bomb left
                    if (released == NodeStatus.Success)
                    {
                        ws.Stage = StageOut;
                        if (!Fly(world, self, target + new Vector3(ws.Dir * RunOutM, 0f), in p, ws.Profile)) return NodeStatus.Failure;
                        return NodeStatus.Running;
                    }
                }
                else
                {
                    float breakM = BreakRange(in p, ws.Profile);
                    if (distance <= breakM || along <= 0f)
                    {
                        StopFire(world, self, ref ws);
                        if (ws.Profile == AirAttackProfile.RunningFire)
                        {
                            // break off short of the point: turn away to the right of the run-in, a little back, and fly out — never
                            //   a heading that still closes on the point (a forward-right break-away passes it nearer than the break)
                            var right = new Vector2(ws.Dir.Y, -ws.Dir.X);
                            if (!Fly(world, self, at + new Vector3(right * BreakAwayM - ws.Dir * (BreakAwayM * 0.25f), 0f), in p, ws.Profile))
                                return NodeStatus.Failure;
                            ws.Stage = StageOut;
                            return NodeStatus.Running;
                        }
                        if (ws.Fired != 0)
                        {
                            ws.Stage = StageOut;   // the plane flies through: the run-in's steer point is already beyond the target
                            if (!Fly(world, self, target + new Vector3(ws.Dir * RunOutM, 0f), in p, ws.Profile)) return NodeStatus.Failure;
                            return NodeStatus.Running;
                        }
                    }
                    else ForwardFire(ref p, ref ws, self, world, target, distance);
                }
                if (along < -RunOutM * 0.25f)   // overflown without a weapon gone: come round for another pass
                {
                    if (++ws.Passes >= MaxPasses) return NodeStatus.Failure;
                    ws.Dir = -ws.Dir;
                    ws.Weapon = WeaponRockets;
                    var start = target - new Vector3(ws.Dir * RunIn(in p, world, self), 0f);
                    if (!Fly(world, self, start, in p, ws.Profile)) return NodeStatus.Failure;
                    ws.Stage = StageToRunIn;
                }
                return NodeStatus.Running;
            }

            var outbound = LocomotionMoveTo.Status(world, self);   // out: done when the egress point is reached
            if (outbound != NodeStatus.Success || ws.Profile != AirAttackProfile.RunningFire || ws.Fired != 0) return outbound;
            // a running fire that broke off with nothing fired: come in again from where it is now
            if (++ws.Passes >= MaxPasses) return NodeStatus.Failure;
            ws.Dir = Flat(target - at);
            ws.Weapon = WeaponRockets;
            ws.Stage = StageRunIn;
            ws.NextSteer = 0;
            return NodeStatus.Running;
        }

        /// <summary>⭐ Slice 6 — inside the fire window: the rockets (a ripple), then — a running fire — the gun, each until its rounds are
        /// gone or it fails (empty, refused); then the next. The window's far edge is each weapon's own range.</summary>
        private static void ForwardFire(ref AirAttackParams p, ref AirAttackState ws, Entity self, EntityRepository world, Vector3 target, float distance)
        {
            if (ws.Weapon == WeaponRockets && ws.Mount == AirAttackProfiles.None) ws.Weapon = WeaponGun;
            if (ws.Weapon == WeaponGun && ws.GunMount == AirAttackProfiles.None) ws.Weapon = WeaponDone;
            if (ws.Weapon == WeaponDone) return;
            bool rockets = ws.Weapon == WeaponRockets;
            byte mount = rockets ? ws.Mount : ws.GunMount;
            float range = CombatTkb.MountOf(world, self, mount)?.Range ?? 0f;
            float window = rockets
                ? MathF.Min(p.FireRangeM > 0f ? p.FireRangeM : DefaultRocketFireM, range > 0f ? range : float.MaxValue)
                : 0.9f * (range > 0f ? range : 1000f);
            if (distance > window && ws.Fire.Started == 0) return;
            var fire = new FireAtPointNodeParams
            {
                Point = target, Mount = mount, Started = ws.Fire.Started,
                Rounds = rockets ? (p.Ripple > 0 ? p.Ripple : DefaultRipple) : GunBurst,
                CooldownSeconds = rockets ? RocketIntervalS : GunIntervalS,
            };
            var status = FireAtPointNodes.FireAtPoint(ref fire, self, world);
            ws.Fire = fire;
            if (status == NodeStatus.Running) return;
            if (status == NodeStatus.Success) ws.Fired = 1;
            ws.Fire = default;
            ws.Weapon = rockets ? WeaponGun : WeaponDone;
        }

        /// <summary>Hands the weapon channel back when a fire action of this attack is still on it (the break: no round after it).</summary>
        private static void StopFire(EntityRepository world, Entity self, ref AirAttackState ws)
        {
            if (ws.Fire.Started != 0 && world.HasComponent<Fdp.Toolkit.Behavior.Components.WeaponChannel>(self))
            {
                ref var channel = ref world.GetComponentRW<Fdp.Toolkit.Behavior.Components.WeaponChannel>(self);
                if (channel.ActionInstanceId == ws.Fire.Started && channel.ActiveAction == CombatConstants.ActionIdFireAtPoint)
                {
                    if (channel.Status == NodeStatus.Success) ws.Fired = 1;
                    channel.ActiveAction = 0;
                    unchecked { channel.ActionInstanceId++; if (channel.ActionInstanceId == 0) channel.ActionInstanceId = 1; }
                }
            }
            ws.Fire = default;
            ws.Weapon = WeaponDone;
        }

        private static float BreakRange(in AirAttackParams p, AirAttackProfile profile)
            => p.BreakRangeM > 0f ? p.BreakRangeM : profile == AirAttackProfile.RunningFire ? DefaultBreakM : DefaultPlaneBreakM;

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
        private static bool Fly(EntityRepository world, Entity self, Vector3 to, in AirAttackParams p, AirAttackProfile profile)
        {
            float height = p.AttackHeightAgl > 0f ? p.AttackHeightAgl : profile switch
            {
                AirAttackProfile.RocketRun => DefaultRocketRunHeightAgl,
                AirAttackProfile.RunningFire => DefaultRunningFireHeightAgl,
                _ => DefaultAttackHeightAgl,
            };
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
