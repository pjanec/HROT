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
        /// <summary>⭐ Slice 6c — a plane's very low level pass (it stands for the shallow dive the model cannot fly): its fixed gun inside
        /// the window, then through and out (§9e).</summary>
        GunRun = 4,
        /// <summary>⭐ Slice 6d — a helicopter whose guns are its DOOR guns (the doors-off UH-60) circles the target with the door that bears
        /// turned to it, its door gunners firing; a gunner shot ⇒ his gun falls silent and the orbit turns the other door in (§9f).</summary>
        DoorGunOrbit = 5,
    }

    /// <summary>⭐ <c>CE-2173</c> slice 5 — what an air attack is told: the point and the numbers of the pass (0 = automatic).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AirAttackParams
    {
        /// <summary>The aim point (world metres; Z = the ground there). ⭐ Slice 6c — an entity attack keeps it on the target each frame.</summary>
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
        /// <summary>⭐ Slice 6c (B12–B14) — the ENTITY attacked (Null = a point): direct fire first, led, passes until it is destroyed.</summary>
        public Entity Target;
        /// <summary>⭐ Slice 6c (W1) — what a struck point is; a heavy kind takes a heavy weapon, never a gun.</summary>
        public StrikeTargetKind Kind;
        /// <summary>⭐ Slice 6c (W3) — 1 ⇒ fire although friends or neutrals stand inside the weapon's burst.</summary>
        public byte DangerClose;
        /// <summary>⭐ Slice 6d — a door-gun orbit's radius (m); 0 ⇒ <see cref="AirAttackNodes.DefaultOrbitRadiusM"/>, at least two turn
        /// radii, at most 0.8 × the door gun's range.</summary>
        public float OrbitRadiusM;
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
        /// <summary>⭐ Slice 6c (B14) — mounts that ran dry (bit i = mount i): the next pass picks among the others.</summary>
        public ushort Spent;
        /// <summary>⭐ Slice 6d — the bearing turned round the target on the orbit so far (rad), and the last bearing it was at.</summary>
        public float Swept;
        public float LastBearing;
        /// <summary>⭐ Slice 6d — the orbit's turn: +1 counter-clockwise (the target on the LEFT door), −1 clockwise (the right).</summary>
        public sbyte Turn;
        /// <summary>⭐ Slice 6d — door guns that ran dry (bit k = mount 32 + k).</summary>
        public byte DoorDry;
    }

    /// <summary>⭐ <c>CE-2173</c> slice 5 (B4) / slice 6 / slice 6c (B12, W1) — which profile an aircraft flies (Air Operations §7, §9e):
    /// against an ENTITY a plane prefers its gun (GunRun), then rockets (RocketRun), the bomb (LevelBomb) last; against a POINT the bomb
    /// first, then rockets, then the gun — and a heavy point (a building, a bunker, a bridge, a runway) never takes the gun; a helicopter
    /// flies RunningFire with its rockets and gun. Mounts in <c>excluded</c> (dry, or W3: endangering others) are passed over.
    /// Null = no profile (the attack fails, loudly).</summary>
    public static class AirAttackProfiles
    {
        public const int MaxMounts = 16;
        /// <summary>No mount.</summary>
        public const byte None = 0xFF;

        public static AirAttackProfile? Pick(EntityRepository world, Entity aircraft, out byte mount)
            => Pick(world, aircraft, out mount, out _);

        /// <summary>The profile against a point of no stated kind (a strike), its main mount (the bomb, the rockets — <see cref="None"/>
        /// when the profile fires only a gun) and its gun (<see cref="None"/> otherwise).</summary>
        public static AirAttackProfile? Pick(EntityRepository world, Entity aircraft, out byte mount, out byte gun)
            => Pick(world, aircraft, out mount, out gun, entity: false, StrikeTargetKind.Unspecified, excluded: 0);

        /// <summary>⭐ Slice 6c — the profile against an entity (<paramref name="entity"/>) or a point of <paramref name="kind"/>, passing
        /// over the mounts in <paramref name="excluded"/>.</summary>
        public static AirAttackProfile? Pick(EntityRepository world, Entity aircraft, out byte mount, out byte gun,
                                             bool entity, StrikeTargetKind kind, ushort excluded)
        {
            Scan(world, aircraft, excluded, out var bomb, out var rockets, out var firstGun);
            bool heavy = !entity && Heavy(kind);
            if (heavy) firstGun = None;   // W1: a heavy point never takes the gun
            var locomotion = Locomotions.Of(world, aircraft);
            AirAttackProfile? picked = null;
            if (locomotion == Fdp.Toolkit.Tkb.Domain.LocomotionClass.FixedWing)
                picked = entity
                    ? firstGun != None ? AirAttackProfile.GunRun : rockets != None ? AirAttackProfile.RocketRun
                      : bomb != None ? AirAttackProfile.LevelBomb : null
                    : bomb != None ? AirAttackProfile.LevelBomb : rockets != None ? AirAttackProfile.RocketRun
                      : firstGun != None ? AirAttackProfile.GunRun : null;
            else if (locomotion == Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft && (rockets != None || firstGun != None))
                picked = AirAttackProfile.RunningFire;
            else if (locomotion == Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft && !heavy && DoorGuns(world, aircraft, stackalloc byte[MaxDoorGuns]) > 0)
                picked = AirAttackProfile.DoorGunOrbit;   // ⭐ slice 6d — its only guns are in its doors
            mount = gun = None;
            if (picked is { } p) MountsFor(bomb, rockets, firstGun, p, out mount, out gun);
            return picked;
        }

        /// <summary>⭐ Slice 6c (W1) — a point that needs a heavy weapon.</summary>
        public static bool Heavy(StrikeTargetKind kind)
            => kind is StrikeTargetKind.Building or StrikeTargetKind.Fortification or StrikeTargetKind.Bridge or StrikeTargetKind.Runway;

        /// <summary>The mounts a GIVEN profile flies with on this aircraft; false when it has none for it (a bomb run with no bomb).</summary>
        public static bool MountsFor(EntityRepository world, Entity aircraft, AirAttackProfile profile, out byte mount, out byte gun)
            => MountsFor(world, aircraft, profile, 0, out mount, out gun);

        /// <summary>⭐ Slice 6c — the same, passing over the mounts in <paramref name="excluded"/>.</summary>
        public static bool MountsFor(EntityRepository world, Entity aircraft, AirAttackProfile profile, ushort excluded, out byte mount, out byte gun)
        {
            if (profile == AirAttackProfile.DoorGunOrbit)
            {
                mount = gun = None;
                return DoorGuns(world, aircraft, stackalloc byte[MaxDoorGuns]) > 0;
            }
            Scan(world, aircraft, excluded, out var bomb, out var rockets, out var firstGun);
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
                case AirAttackProfile.GunRun: gun = firstGun; return firstGun != None;
                default: return false;
            }
        }

        /// <summary>The first bomb, rocket pod and gun (machine gun or autocannon) in the aircraft's weapon suite not in <paramref name="excluded"/>.</summary>
        private static void Scan(EntityRepository world, Entity aircraft, ushort excluded, out byte bomb, out byte rockets, out byte gun)
        {
            bomb = rockets = gun = None;
            for (int i = 0; i < MaxMounts; i++)
            {
                var m = CombatTkb.MountOf(world, aircraft, i);
                if (m == null) break;
                if ((excluded & (1 << i)) != 0) continue;
                switch (m.Category)
                {
                    case Fdp.Toolkit.Tkb.Domain.WeaponCategory.Bomb: if (bomb == None) bomb = (byte)i; break;
                    case Fdp.Toolkit.Tkb.Domain.WeaponCategory.Rocket: if (rockets == None) rockets = (byte)i; break;
                    case Fdp.Toolkit.Tkb.Domain.WeaponCategory.MachineGun or Fdp.Toolkit.Tkb.Domain.WeaponCategory.Autocannon:
                        if (gun == None) gun = (byte)i; break;
                }
            }
        }

        public const int MaxDoorGuns = 4;

        /// <summary>⭐ Slice 6d (§9f) — the aircraft's DOOR guns: the weapon systems it controls (mount <c>32 + k</c>,
        /// <see cref="WeaponSystems.Controlled"/>) whose own mount is a machine gun or an autocannon, written to <paramref name="mounts"/>;
        /// returns the count. A gun whose gunner is down is still listed (<see cref="WeaponSystems.CanFire(EntityRepository, Entity)"/> says whether it may fire).</summary>
        public static int DoorGuns(EntityRepository world, Entity aircraft, Span<byte> mounts)
        {
            Span<Entity> weapons = stackalloc Entity[WeaponSystems.MaxControlled];
            int n = WeaponSystems.Controlled(world, aircraft, weapons), count = 0;
            for (int k = 0; k < n && count < mounts.Length; k++)
                if (CombatTkb.MountOf(world, weapons[k], 0)?.Category is Fdp.Toolkit.Tkb.Domain.WeaponCategory.MachineGun
                                                                    or Fdp.Toolkit.Tkb.Domain.WeaponCategory.Autocannon)
                    mounts[count++] = (byte)(WeaponSystems.ControlledMountBase + k);
            return count;
        }

        /// <summary>⭐ Slice 6c (W3, R-2020) — the mounts whose round would hurt a friend or a neutral standing near
        /// <paramref name="aim"/>: inside the largest blast injury radius of the mount's rounds (its ammo and its loadout).</summary>
        public static ushort Endangering(EntityRepository world, Entity aircraft, Vector3 aim)
        {
            ushort mask = 0;
            for (int i = 0; i < MaxMounts; i++)
            {
                var m = CombatTkb.MountOf(world, aircraft, i);
                if (m == null) break;
                float r = InjuryRadius(world, unchecked((long)m.AmmoGuid));
                foreach (var load in m.Loadout) r = MathF.Max(r, InjuryRadius(world, unchecked((long)load.AmmoGuid)));
                if (HiddenTargetThrow.FriendlyInBurst(world, aircraft, aim, r, neutralsToo: true)) mask |= (ushort)(1 << i);
            }
            return mask;
        }

        private static float InjuryRadius(EntityRepository world, long ammo)
            => ammo != 0 && CombatTkb.WarheadOf(world, ammo) is { } w ? w.BlastInjuryRadiusM : 0f;
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
        /// <summary>⭐ Slice 6c — a gun run's pass height above the target (m): the GAU-8's 5° nose cone holds the target to 571 m at 50 m,
        /// to 1 714 m at 150 m — beyond its 1 200 m range (Air Operations §9e).</summary>
        public const float DefaultGunRunHeightAgl = 50f;
        /// <summary>⭐ Slice 6c — a gun run stops firing this far from the target (m) and flies through.</summary>
        public const float DefaultGunRunBreakM = 600f;

        /// <summary>⭐ Slice 6d — a door-gun orbit's radius round the target (m) and its height above it (m): 📐 at 400 m / 100 m the guns
        /// look down 14°, inside the M134 pintle's −60°, and the 404 (70 m/s) goes round in ~36 s (Air Operations §9f).</summary>
        public const float DefaultOrbitRadiusM = 400f;
        public const float DefaultOrbitHeightAgl = 100f;
        /// <summary>⭐ Slice 6d — the orbit is steered at the point this far round the circle ahead of the helicopter (rad, 30°).</summary>
        public const float OrbitLeadRad = MathF.PI / 6f;

        private const byte StageStart = 0, StageToRunIn = 1, StageRunIn = 2, StageOut = 3, StageOrbit = 4;
        private const byte WeaponRockets = 0, WeaponGun = 1, WeaponDone = 2;

        /// <summary>⭐ Slice 6c (B12–B14) — the air attack on an ENTITY: fails (logged) when the order named none; else <see cref="Run"/>.</summary>
        public static NodeStatus Attack(ref AirAttackParams p, ref AirAttackState ws, Entity self, EntityRepository world)
        {
            if (p.Target.IsNull)
            {
                BehaviorLog.Error(self, world, "AirAttack: the attack names no target entity.");
                return NodeStatus.Failure;
            }
            return Run(ref p, ref ws, self, world);
        }

        public static NodeStatus Run(ref AirAttackParams p, ref AirAttackState ws, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<SimTransform>(self)) return NodeStatus.Failure;
            var at = world.GetComponentRO<SimTransform>(self).Position;
            bool entity = !p.Target.IsNull;
            if (entity)
            {
                // ⭐ B14 — the attack on an entity ends the moment it is destroyed; until then the point follows it (B13)
                if (!CombatLife.IsAlive(world, p.Target)) { StopFire(world, self, ref ws); return NodeStatus.Success; }
                if (Fdp.Toolkit.Replication.ReplicatedMotion.Of(world, p.Target, out var now3, out _)) p.Point = now3;
            }
            var target = p.Point;
            double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d;

            if (ws.Stage == StageStart)
            {
                if (!Choose(ref p, ref ws, self, world)) return NodeStatus.Failure;
                if (ws.Profile == AirAttackProfile.DoorGunOrbit)
                {
                    // ⭐ slice 6d — no run-in: it flies onto the circle from wherever it is
                    ws.Stage = StageOrbit;
                    ws.Turn = 0;
                    ws.Swept = 0f;
                    ws.LastBearing = MathF.Atan2(at.Y - target.Y, at.X - target.X);
                    ws.NextSteer = 0;
                }
                else
                {
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
            }

            if (ws.Stage == StageOrbit) return Orbit(ref p, ref ws, self, world, at, target, now, entity);

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
                // ⭐ slice 6c — a moving ENTITY keeps the run-in on it inside the freeze too, while it is still ahead and beyond the break
                //   (a fixed gun's 5° cone would lose a technical that drifts sideways once the line is frozen)
                bool track = entity && distance > BreakRange(in p, ws.Profile) && Vector2.Dot(flat, ws.Dir) > 0.5f;
                if ((distance > SteerFreezeM || track) && now >= ws.NextSteer)
                {
                    ws.Dir = flat;
                    if (!Fly(world, self, target + new Vector3(flat * RunOutM, 0f), in p, ws.Profile)) return NodeStatus.Failure;
                    ws.NextSteer = now + SteerPeriod;
                }
                var aim = AimPoint(in p, in ws, self, world);   // ⭐ B13 — the led point (the point itself when it does not move)
                if (ws.Profile == AirAttackProfile.LevelBomb)
                {
                    var fire = new FireAtPointNodeParams { Point = aim, Rounds = 1, Mount = ws.Mount, Started = ws.Fire.Started };
                    var released = FireAtPointNodes.FireAtPoint(ref fire, self, world);
                    ws.Fire = fire;
                    if (released == NodeStatus.Running) Retarget(world, self, ws.Fire.Started, aim);
                    if (released == NodeStatus.Failure)   // no bomb left: an entity attack goes round for its other weapons
                    {
                        if (!entity) return NodeStatus.Failure;
                        ws.Spent |= (ushort)(1 << ws.Mount);
                        ws.Fire = default;
                        ws.Stage = StageOut;
                        if (!Fly(world, self, target + new Vector3(ws.Dir * RunOutM, 0f), in p, ws.Profile)) return NodeStatus.Failure;
                        return NodeStatus.Running;
                    }
                    if (released == NodeStatus.Success)
                    {
                        ws.Fired = 1;
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
                    else ForwardFire(ref p, ref ws, self, world, aim, distance);
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
            if (outbound == NodeStatus.Success && entity)
            {
                // ⭐ B14 — the target still stands: come round (3 passes), with the weapons that are left and do not endanger others
                if (++ws.Passes >= MaxPasses)
                {
                    BehaviorLog.Error(self, world, $"AirAttack: the target survived {MaxPasses} passes.");
                    return NodeStatus.Failure;
                }
                if (!Choose(ref p, ref ws, self, world)) return NodeStatus.Failure;
                ws.Dir = Flat(target - at);
                ws.Stage = StageRunIn;
                ws.NextSteer = 0;
                return NodeStatus.Running;
            }
            if (outbound != NodeStatus.Success || ws.Profile != AirAttackProfile.RunningFire || ws.Fired != 0) return outbound;
            // a running fire that broke off with nothing fired: come in again from where it is now
            if (++ws.Passes >= MaxPasses) return NodeStatus.Failure;
            ws.Dir = Flat(target - at);
            ws.Weapon = WeaponRockets;
            ws.Stage = StageRunIn;
            ws.NextSteer = 0;
            return NodeStatus.Running;
        }

        /// <summary>
        /// ⭐ Slice 6d (§9f) — the door-gun orbit: steered round the target at <see cref="OrbitRadius"/> with the door that bears turned to it
        /// (counter-clockwise ⇒ the LEFT door), every frame the first door gun that may fire (<see cref="WeaponSystems.CanFire(EntityRepository, Entity)"/> — a gunner
        /// down silences his gun, WS-3), is not dry, and has the led point in its station arc and in range fires a burst through
        /// <c>FireAtPoint(32 + k)</c>; no able gun on the turning side but one on the other ⇒ the orbit reverses. Ends: an entity dead
        /// (handled in <see cref="Run"/>) · a point after one full turn with rounds out ⇒ Success · every door gun silent or dry, or
        /// <see cref="MaxPasses"/> turns with the target alive ⇒ Failure, logged.
        /// </summary>
        private static NodeStatus Orbit(ref AirAttackParams p, ref AirAttackState ws, Entity self, EntityRepository world,
                                        Vector3 at, Vector3 target, double now, bool entity)
        {
            float radius = OrbitRadius(in p, world, self);
            float bearing = MathF.Atan2(at.Y - target.Y, at.X - target.X);
            // the turn counts from the moment it is ON the circle (within 1.1 R), then on — 📐 a 1.5 R gate counted the approach's own
            //   swing, and a point strike ended 23° short of a full turn on the circle (rail-measured)
            if (ws.Swept > 0f || new Vector2(at.X - target.X, at.Y - target.Y).Length() < 1.1f * radius)
                ws.Swept += MathF.Abs(MathF.IEEERemainder(bearing - ws.LastBearing, 2f * MathF.PI));
            ws.LastBearing = bearing;

            Span<byte> guns = stackalloc byte[AirAttackProfiles.MaxDoorGuns];
            int n = AirAttackProfiles.DoorGuns(world, self, guns);
            // the turn: the door whose gun may fire, turned to the target — a gunner down on this side ⇒ the other side (the reversal
            //   stops the silenced gun's burst; 404 has one gun a side, so no other stop is needed — mutant-measured, §9f)
            bool left = false, right = false;
            for (int i = 0; i < n; i++)
            {
                if (!Able(world, self, guns[i], ws.DoorDry)) continue;
                int side = Side(world, self, guns[i]);
                left |= side > 0;
                right |= side < 0;
            }
            if (!left && !right)
            {
                StopFire(world, self, ref ws);
                BehaviorLog.Error(self, world, "AirAttack: every door gun is silent or empty.");
                return NodeStatus.Failure;
            }
            if (ws.Turn == 0 || (ws.Turn > 0 ? !left : !right))
            {
                if (ws.Turn != 0) StopFire(world, self, ref ws);
                ws.Turn = left ? (sbyte)1 : (sbyte)-1;
                ws.NextSteer = 0;
            }

            if (now >= ws.NextSteer)
            {
                float ahead = bearing + ws.Turn * OrbitLeadRad;
                var to = new Vector3(target.X + radius * MathF.Cos(ahead), target.Y + radius * MathF.Sin(ahead), target.Z);
                if (!Fly(world, self, to, in p, ws.Profile)) return NodeStatus.Failure;
                ws.NextSteer = now + SteerPeriod;
            }

            // the fire: the running burst follows the led point; else the first gun that bears starts one
            if (ws.Fire.Started == 0)
            {
                ws.GunMount = AirAttackProfiles.None;
                for (int i = 0; i < n && ws.GunMount == AirAttackProfiles.None; i++)
                {
                    if (!Able(world, self, guns[i], ws.DoorDry)) continue;
                    var weapon = WeaponSystems.ControlledAt(world, self, guns[i]);
                    ws.GunMount = guns[i];
                    var lead = AimPoint(in p, in ws, self, world);
                    float range = MountDto(world, self, guns[i])?.Range ?? 0f;
                    if ((range > 0f && Vector3.Distance(at, lead) > 0.9f * range) || !WeaponSystems.InStationArc(world, weapon, lead))
                        ws.GunMount = AirAttackProfiles.None;
                }
            }
            if (ws.GunMount != AirAttackProfiles.None)
            {
                var aim = AimPoint(in p, in ws, self, world);
                var fire = new FireAtPointNodeParams
                {
                    Point = aim, Mount = ws.GunMount, Started = ws.Fire.Started, Rounds = GunBurst, CooldownSeconds = GunIntervalS,
                };
                var status = FireAtPointNodes.FireAtPoint(ref fire, self, world);
                ws.Fire = fire;
                if (status == NodeStatus.Running) Retarget(world, self, ws.Fire.Started, aim);
                else
                {
                    if (status == NodeStatus.Success) ws.Fired = 1;
                    else ws.DoorDry |= (byte)(1 << (ws.GunMount - WeaponSystems.ControlledMountBase));   // dry (or refused)
                    ws.Fire = default;
                    ws.GunMount = AirAttackProfiles.None;
                }
            }

            const float FullTurn = 2f * MathF.PI;
            if (!entity && ws.Fired != 0 && ws.Swept >= FullTurn) { StopFire(world, self, ref ws); return NodeStatus.Success; }
            if (ws.Swept >= FullTurn * MaxPasses)
            {
                StopFire(world, self, ref ws);
                BehaviorLog.Error(self, world, $"AirAttack: the target survived {MaxPasses} turns of the door guns.");
                return NodeStatus.Failure;
            }
            return NodeStatus.Running;
        }

        /// <summary>⭐ Slice 6d — may door gun <paramref name="mount"/> (32 + k) fire now: listed, not dry, <see cref="WeaponSystems.CanFire(EntityRepository, Entity)"/>.</summary>
        private static bool Able(EntityRepository world, Entity self, byte mount, byte dry)
        {
            if (!WeaponSystems.IsControlledIndex(mount) || (dry & (1 << (mount - WeaponSystems.ControlledMountBase))) != 0) return false;
            var weapon = WeaponSystems.ControlledAt(world, self, mount);
            return !weapon.IsNull && WeaponSystems.CanFire(world, weapon);
        }

        /// <summary>⭐ Slice 6d — which door <paramref name="mount"/> covers: +1 the left (its station arc holds a point abeam to the left),
        /// −1 the right, 0 neither.</summary>
        private static int Side(EntityRepository world, Entity self, byte mount)
        {
            var weapon = WeaponSystems.ControlledAt(world, self, mount);
            if (weapon.IsNull || !world.HasComponent<SimTransform>(self)) return 0;
            ref readonly var tr = ref world.GetComponentRO<SimTransform>(self);
            if (WeaponSystems.InStationArc(world, weapon, tr.Position + Vector3.Transform(new Vector3(0f, 100f, -25f), tr.Rotation))) return 1;
            if (WeaponSystems.InStationArc(world, weapon, tr.Position + Vector3.Transform(new Vector3(0f, -100f, -25f), tr.Rotation))) return -1;
            return 0;
        }

        /// <summary>⭐ Slice 6d — the orbit's radius: the order's, else <see cref="DefaultOrbitRadiusM"/> held inside 0.8 × the first door
        /// gun's range and outside two turn radii.</summary>
        private static float OrbitRadius(in AirAttackParams p, EntityRepository world, Entity self)
        {
            if (p.OrbitRadiusM > 0f) return p.OrbitRadiusM;
            float r = DefaultOrbitRadiusM;
            Span<byte> guns = stackalloc byte[AirAttackProfiles.MaxDoorGuns];
            if (AirAttackProfiles.DoorGuns(world, self, guns) > 0 && MountDto(world, self, guns[0]) is { Range: > 0f } gun) r = MathF.Min(r, 0.8f * gun.Range);
            float turn = world.HasComponent<FlightParams>(self) ? world.GetComponentRO<FlightParams>(self).TurnRadius : 0f;
            return MathF.Max(r, 2f * turn);
        }

        /// <summary>The TKB mount behind <paramref name="mount"/>: the aircraft's own, or — a controlled index (32 + k) — the weapon
        /// system's own mount 0.</summary>
        private static Fdp.Toolkit.Tkb.Domain.WeaponMountDto? MountDto(EntityRepository world, Entity self, byte mount)
            => mount == AirAttackProfiles.None ? null
             : WeaponSystems.IsControlledIndex(mount) ? CombatTkb.MountOf(world, WeaponSystems.ControlledAt(world, self, mount), 0)
             : CombatTkb.MountOf(world, self, mount);

        /// <summary>⭐ Slice 6c — picks the pass's profile and mounts: the order's profile or the table's (B12, W1), passing over the mounts
        /// that ran dry (<see cref="AirAttackState.Spent"/>) and — unless danger close — those whose round would hurt a friend or a neutral
        /// near the point (W3). False (logged) when nothing is left.</summary>
        private static bool Choose(ref AirAttackParams p, ref AirAttackState ws, Entity self, EntityRepository world)
        {
            ushort endangering = p.DangerClose != 0 ? (ushort)0 : AirAttackProfiles.Endangering(world, self, p.Point);
            ushort excluded = (ushort)(ws.Spent | endangering);
            AirAttackProfile? picked;
            byte mount, gun;
            if (p.Profile == AirAttackProfile.Auto)
                picked = AirAttackProfiles.Pick(world, self, out mount, out gun, !p.Target.IsNull, p.Kind, excluded);
            else picked = AirAttackProfiles.MountsFor(world, self, p.Profile, excluded, out mount, out gun) ? p.Profile : null;
            if (picked == null)
            {
                BehaviorLog.Error(self, world,
                    endangering != 0 ? "AirAttack: every weapon left would hurt friends or neutrals near the point (order it danger close to fire)."
                    : ws.Spent != 0 ? "AirAttack: no weapon left for this target."
                    : p.Profile == AirAttackProfile.Auto
                        ? "AirAttack: no attack profile for this aircraft and its weapons (a plane needs a bomb, a rocket pod or a gun; a helicopter rockets or a gun)."
                        : $"AirAttack: this aircraft has no weapon for a {p.Profile}.");
                return false;
            }
            ws.Profile = picked.Value;
            ws.Mount = mount;
            ws.GunMount = gun;
            ws.Weapon = WeaponRockets;
            ws.Fired = 0;
            ws.Fire = default;
            return true;
        }

        /// <summary>⭐ Slice 6c (B13) — where this pass's weapon aims: a point that does not move is itself; a moving target is LED — a
        /// gun's or a rocket's round by <see cref="TargetLead.Point"/> (it keeps the aircraft's velocity, A4), a bomb by the target's
        /// velocity over the bomb's fall time. Motion from <see cref="Fdp.Toolkit.Replication.ReplicatedMotion"/> (the brain node).</summary>
        private static Vector3 AimPoint(in AirAttackParams p, in AirAttackState ws, Entity self, EntityRepository world)
        {
            if (p.Target.IsNull || !Fdp.Toolkit.Replication.ReplicatedMotion.Of(world, p.Target, out var tp, out var tv)) return p.Point;
            if (!Fdp.Toolkit.Replication.ReplicatedMotion.Of(world, self, out var sp, out var sv)) return tp;
            if (ws.Profile == AirAttackProfile.LevelBomb)
            {
                // no drag: the fall from the release height, starting at the bomb's launch velocity (BombBallistics.Launch)
                float vz = BombBallistics.Launch(sv).Z, h = MathF.Max(0f, sp.Z - tp.Z), g = CombatConstants.Gravity;
                float fall = (vz + MathF.Sqrt(vz * vz + 2f * g * h)) / g;
                return tp + new Vector3(tv.X, tv.Y, 0f) * fall;
            }
            byte mount = ws.Weapon == WeaponRockets && ws.Mount != AirAttackProfiles.None ? ws.Mount : ws.GunMount;
            // ⭐ a gun or a rocket aims at the target's MIDDLE (the height FireProcessingSystem aims at, AimPoint.Middle) — 📐 measured live
            //   (ua-air-attack ②): aimed at the ground point, a 3° gun pass burst every 30 mm round on the ground ~7 m short of the truck
            if (world.HasComponent<SimTransform>(p.Target))
                tp.Z += Fdp.Toolkit.Combat.AimPoint.Middle(world, p.Target, out _, out _).Z - world.GetComponentRO<SimTransform>(p.Target).Position.Z;
            return TargetLead.Point(sp, sv, RoundSpeed(world, self, mount), tp, tv);
        }

        /// <summary>A straight round's speed from <paramref name="mount"/>: the missile's own, else the mount's muzzle velocity, else the
        /// engine's fallback (the order <c>AimAndFireExecutor.GuidedFlightSeconds</c> reads them in).</summary>
        private static float RoundSpeed(EntityRepository world, Entity self, byte mount)
        {
            var m = MountDto(world, self, mount);   // ⭐ slice 6d — a door gun's own mount
            if (m == null) return Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.MuzzleVelocity;
            return CombatTkb.MissileOf(world, unchecked((long)m.AmmoGuid)) is { SpeedMps: > 0f } missile ? missile.SpeedMps
                 : m.MuzzleVelocity > 0f ? m.MuzzleVelocity
                 : Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.MuzzleVelocity;
        }

        /// <summary>⭐ Slice 6c — moves the RUNNING fire action's aim point (the executor reads its params every tick), so a ripple or a
        /// burst keeps tracking without a new action per frame.</summary>
        private static unsafe void Retarget(EntityRepository world, Entity self, uint started, Vector3 aim)
        {
            if (started == 0 || !world.HasComponent<Fdp.Toolkit.Behavior.Components.WeaponChannel>(self)) return;
            ref var channel = ref world.GetComponentRW<Fdp.Toolkit.Behavior.Components.WeaponChannel>(self);
            if (channel.ActionInstanceId != started || channel.ActiveAction != CombatConstants.ActionIdFireAtPoint) return;
            System.Runtime.CompilerServices.Unsafe.As<byte, Fdp.Toolkit.Combat.Executors.FireAtPointParams>(ref channel.Params[0]).Point = aim;
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
            if (status == NodeStatus.Running) { Retarget(world, self, ws.Fire.Started, target); return; }
            if (status == NodeStatus.Success) ws.Fired = 1;
            else ws.Spent |= (ushort)(1 << mount);   // ⭐ slice 6c — dry (or refused): the next pass picks among the others
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
                    // ⭐ slice 6c — a burst cut short by the break still fired this pass (B14: the plane flies through and comes round)
                    if (channel.Status == NodeStatus.Success || Fdp.Toolkit.Combat.Executors.FireAtPointExecutor.RoundsFired(ref channel) > 0) ws.Fired = 1;
                    channel.ActiveAction = 0;
                    unchecked { channel.ActionInstanceId++; if (channel.ActionInstanceId == 0) channel.ActionInstanceId = 1; }
                }
            }
            ws.Fire = default;
            ws.Weapon = WeaponDone;
        }

        private static float BreakRange(in AirAttackParams p, AirAttackProfile profile)
            => p.BreakRangeM > 0f ? p.BreakRangeM : profile switch
            {
                AirAttackProfile.RunningFire => DefaultBreakM,
                AirAttackProfile.GunRun => DefaultGunRunBreakM,
                _ => DefaultPlaneBreakM,
            };

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
                AirAttackProfile.GunRun => DefaultGunRunHeightAgl,
                AirAttackProfile.DoorGunOrbit => DefaultOrbitHeightAgl,
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
            block.Attack = new AirAttackParams { Point = point, Kind = order.TargetKind, DangerClose = order.DangerClose ? (byte)1 : (byte)0 };
            block.State = default;
        }

        [Behavior(BehaviorNames.AirPlatformStrike, Params = typeof(StrikeOrder), Categories = BehaviorCategory.MilitaryApc)]
        [BTreeDefinition(BehaviorNames.AirPlatformStrike, Curated = true, Group = "Library/Orders", ParamsType = typeof(AirPlatformStrikeBlackboard))]
        public static BTreeBuilder<AirPlatformStrikeBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<AirPlatformStrikeBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Attack, bb => bb.State, AirAttackNodes.Run);
    }
    /// <summary>
    /// ⭐ <c>CE-2173</c> slice 6c (B12–B14, R-278, R-2020) — ATTACK for every aircraft (<c>AirPlatform</c>): the one
    /// <see cref="AirAttackNodes.Run"/> on the order's ENTITY — direct fire first (a plane's gun, then rockets, the bomb last; a
    /// helicopter's running fire), led at it as it moves, pass after pass until it is destroyed (Air Operations §9e).
    /// </summary>
    public static class AirPlatformAttackBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct AirPlatformAttackBlackboard
        {
            public AirAttackParams Attack;
            public AirAttackState State;
        }

        [BehaviorResolver(BehaviorNames.AirPlatformAttack)]
        public static void Resolve(in AttackOrder order, ref AirPlatformAttackBlackboard block, EntityRepository world, Entity self)
        {
            var target = order.Target.Resolve(world);
            bool known = !target.IsNull && world.IsAlive(target) && world.HasComponent<SimTransform>(target);
            block.Attack = new AirAttackParams
            {
                Target = known ? target : Entity.Null,
                Point = known ? world.GetComponentRO<SimTransform>(target).Position : default,
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.AirPlatformAttack, Params = typeof(AttackOrder), Categories = BehaviorCategory.MilitaryApc)]
        [BTreeDefinition(BehaviorNames.AirPlatformAttack, Curated = true, Group = "Library/Orders", ParamsType = typeof(AirPlatformAttackBlackboard))]
        public static BTreeBuilder<AirPlatformAttackBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<AirPlatformAttackBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Attack, bb => bb.State, AirAttackNodes.Attack);
    }
}
