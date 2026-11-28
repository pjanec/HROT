using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Motion;
using Hrot.AI.Behaviors.Logging;
using Hrot.AI.Behaviors.Orders;
using Hrot.Map.Definitions.Behavior;
using Fdp.Toolkit.Behavior.Diagnostics;

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
        /// <summary>⭐ Slice 6e (B5, §9i) — the FPV drone: (a point) search the area, pick the best target, then pursue it at the cruise
        /// height and dive on it from above; the drone IS its warhead — the Muscle's impact ends the flight.</summary>
        Dive = 6,
        /// <summary>⭐ <c>CE-7027</c> (City Battle §11 R-1b) — the BOMBER quadcopter: a rotorcraft whose only weapon is a drop rack flies to
        /// hover over the point, drops one round when it stands over it (the hover release cue, <c>BombBallistics.ReleaseNow</c>), and flies
        /// back to where it started — it lives to drop again.</summary>
        HoverDrop = 7,
        /// <summary>⭐⭐ CE-10003 (air combat AC-13, R-2015) — an Attack on an AIRCRAFT by a fighter with an air-to-air weapon: the dogfight
        /// (<see cref="AirCombatNodes"/>, mode 8).</summary>
        AirCombat = 8,
        /// <summary>⭐⭐ CE-3490 (DESIGN_Breath_Attack BA-1, BA-2) — a rotorcraft with a FLAMETHROWER (a dragon's breath) STRAFES: in at 60 m, down to 12 m and
        /// 10 m/s 80 m out, breathing while the aim is in the jet's reach, OVER the aim, out 150 m and up, and back on a line 120° round (never the same line).</summary>
        BreathPass = 9,
        /// <summary>⭐⭐ CE-3490 (BA-4) — the same flyer against a BURNABLE BUILDING: hovers 25 m off its nearest burnable wall, 8 m up, breathes on it 3 s,
        /// then flies home (HoverDrop's shape).</summary>
        BreathHover = 10,
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
        /// <summary>⭐ Slice 6e (§9i D-4) — a drone's search circle round the struck point (m); 0 ⇒ <see cref="AirAttackNodes.DefaultSearchRadiusM"/>.</summary>
        public float SearchRadiusM;
        /// <summary>⭐ Slice 6e (§9i D-2) — the dive's angle below the horizontal (degrees); 0 ⇒ <see cref="AirAttackNodes.DefaultDiveAngleDeg"/>.</summary>
        public float DiveAngleDeg;
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
        /// <summary>⭐ <c>CE-2191</c> (§7e) — the sim time of the last missile warning this attack answered (its <c>MissileWarning.At</c>).</summary>
        public SimInstant WarnedAt;
        /// <summary>⭐ <c>CE-2191</c> — the evasion holds until this sim time (the missile's time to impact, and a margin).</summary>
        public SimInstant EvadeUntil;
        /// <summary>⭐ <c>CE-2191</c> — decoys released in this evasion.</summary>
        public byte Decoys;
        /// <summary>⭐ <c>CE-2191</c> — the stage the evasion returns to: the orbit, or (any other) a fresh run-in.</summary>
        public byte Resume;
        /// <summary>⭐ Slice 6e (§9i D-6) — the sim time the attack began (the FPV's track sightings are timed from it; ⛔ the battery no longer — SD-S2b's <c>DroneBattery</c>).</summary>
        public SimInstant StartedAt;
        /// <summary>⭐ Slice 6e — 1 once the battery ran out: the dive is straight down.</summary>
        public byte BatteryOut;
        /// <summary>⭐ <c>CE-7027</c> — where a hover drop started: the bomber flies back here once it dropped.</summary>
        public Vector3 Home;
        /// <summary>⭐ <c>CE-4621</c> MC-4a (R-2122) — the FPV's OWN perceived track: the last <see cref="AirAttackNodes.TrackSamples"/> camera
        /// sightings of its target (xyz, w = seconds since <see cref="StartedAt"/>), newest first, one every <see cref="AirAttackNodes.DiveSteerPeriod"/>.</summary>
        public Vector4 Seen0, Seen1, Seen2, Seen3;
        /// <summary>⭐ MC-4a — sightings held (0…4) and when the next is due (seconds since <see cref="StartedAt"/>).</summary>
        public byte SeenCount;
        public float NextSeen;
        /// <summary>⭐ MC-4a — 1 once the dive COMMITTED (time-to-go ≤ <see cref="AirAttackNodes.CommitSeconds"/>): no more re-aims.</summary>
        public byte Committed;
        /// <summary>⭐ CE-3490 (BA-4) — a breath hover's station (25 m off the wall, 8 m up) and the point on the wall it breathes at.</summary>
        public Vector3 HoverAt, WallAim;
        /// <summary>⭐ CE-3490 (BA-4) — when the breath on the wall began (sim s; 0 = not yet).</summary>
        public SimInstant BreathSince;
        /// <summary>⭐ CE-3490 (BA-3) — 1 once the pass's line was set from a GROUP it remembers (the run-in re-picks it once, the first time it sees one).</summary>
        public byte GroupLine;
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
            Scan(world, aircraft, excluded, out var bomb, out var rockets, out var firstGun, out var flame);
            bool heavy = !entity && Heavy(kind);
            if (heavy) firstGun = None;   // W1: a heavy point never takes the gun
            var locomotion = Locomotions.Of(world, aircraft);
            AirAttackProfile? picked = null;
            if (DetonatesOnImpact(world, aircraft))
            {
                // ⭐ slice 6e (§9i D-1) — a flyer that IS its warhead dives on its target; its mount 0 is the warhead
                mount = 0;
                gun = None;
                return AirAttackProfile.Dive;
            }
            // ⭐⭐ CE-3490 (BA-1, BA-8) — a rotorcraft whose flame can light this target breathes on it: a building hovers, the rest strafes. The flame mount
            //   is already in `excluded` when it cannot (BA-5, AirAttackProfiles.Unlightable) ⇒ a mixed loadout flies its other weapons' profile below
            if (locomotion == Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft && flame != None && (entity || kind == StrikeTargetKind.Building || !heavy))
                picked = !entity && kind == StrikeTargetKind.Building ? AirAttackProfile.BreathHover : AirAttackProfile.BreathPass;
            else if (locomotion == Fdp.Toolkit.Tkb.Domain.LocomotionClass.FixedWing)
                picked = entity
                    ? firstGun != None ? AirAttackProfile.GunRun : rockets != None ? AirAttackProfile.RocketRun
                      : bomb != None ? AirAttackProfile.LevelBomb : null
                    : bomb != None ? AirAttackProfile.LevelBomb : rockets != None ? AirAttackProfile.RocketRun
                      : firstGun != None ? AirAttackProfile.GunRun : null;
            else if (locomotion == Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft && (rockets != None || firstGun != None))
                picked = AirAttackProfile.RunningFire;
            else if (locomotion == Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft && !heavy && DoorGuns(world, aircraft, stackalloc byte[MaxDoorGuns]) > 0)
                picked = AirAttackProfile.DoorGunOrbit;   // ⭐ slice 6d — its only guns are in its doors
            else if (locomotion == Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft && bomb != None)
                picked = AirAttackProfile.HoverDrop;      // ⭐ CE-7027 — a rotorcraft with only a drop rack (the bomber quadcopter) hovers and drops
            mount = gun = None;
            if (picked is { } p) MountsFor(bomb, rockets, firstGun, flame, p, out mount, out gun);
            return picked;
        }

        /// <summary>⭐ Slice 6e (§9i D-1, R-3007) — a flyer that bursts when it meets something: its type's <c>Gen.Flight DetonatesOnImpact</c>.</summary>
        public static bool DetonatesOnImpact(EntityRepository world, Entity aircraft)
            => world.IsComponentTypeRegistered<FlightParams>() && world.HasComponent<FlightParams>(aircraft)
               && world.GetComponentRO<FlightParams>(aircraft).DetonatesOnImpact != 0;

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
            if (profile == AirAttackProfile.Dive)
            {
                mount = 0;
                gun = None;
                return DetonatesOnImpact(world, aircraft);
            }
            Scan(world, aircraft, excluded, out var bomb, out var rockets, out var firstGun, out var flame);
            return MountsFor(bomb, rockets, firstGun, flame, profile, out mount, out gun);
        }

        private static bool MountsFor(byte bomb, byte rockets, byte firstGun, byte flame, AirAttackProfile profile, out byte mount, out byte gun)
        {
            mount = gun = None;
            switch (profile)
            {
                case AirAttackProfile.LevelBomb: mount = bomb; return bomb != None;
                case AirAttackProfile.HoverDrop: mount = bomb; return bomb != None;
                case AirAttackProfile.RocketRun: mount = rockets; return rockets != None;
                case AirAttackProfile.RunningFire: mount = rockets; gun = firstGun; return rockets != None || firstGun != None;
                case AirAttackProfile.GunRun: gun = firstGun; return firstGun != None;
                case AirAttackProfile.BreathPass or AirAttackProfile.BreathHover: mount = flame; return flame != None;   // ⭐ CE-3490
                default: return false;
            }
        }

        /// <summary>The first bomb, rocket pod, gun (machine gun or autocannon) and ⭐ CE-3490 flamethrower in the aircraft's weapon suite not in
        /// <paramref name="excluded"/>.</summary>
        private static void Scan(EntityRepository world, Entity aircraft, ushort excluded, out byte bomb, out byte rockets, out byte gun, out byte flame)
        {
            bomb = rockets = gun = flame = None;
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
                    case Fdp.Toolkit.Tkb.Domain.WeaponCategory.Flamethrower: if (flame == None) flame = (byte)i; break;
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

        /// <summary>
        /// ⭐⭐ CE-3490 (DESIGN_Breath_Attack BA-5, BA-6) — the FLAMETHROWER mounts that may not breathe on this target: the ROE forbids incendiaries
        /// (<see cref="ForceLevels.IncendiaryPermits"/>), or the flame cannot light it — an entity <see cref="Ignition.Of"/> calls none (an armoured hull, an
        /// aircraft); a building strike on a building that does not burn (<see cref="Fdp.Toolkit.Terrain.BuildingFires.BurnableAt"/>); any other heavy point.
        /// Passed over like a dry mount ⇒ the other weapons' profile, or none (the attack fails, logged). 0 B per call.
        /// </summary>
        public static ushort Unlightable(EntityRepository world, Entity aircraft, Entity target, Vector3 point, StrikeTargetKind kind)
        {
            ushort mask = 0;
            for (int i = 0; i < MaxMounts; i++)
            {
                var m = CombatTkb.MountOf(world, aircraft, i);
                if (m == null) break;
                if (m.Category != Fdp.Toolkit.Tkb.Domain.WeaponCategory.Flamethrower) continue;
                bool barred = !ForceLevels.IncendiaryPermits(world, aircraft, 0, m);
                if (!target.IsNull) barred |= Ignition.Of(world, target) == Ignition.Kind.None;
                else if (kind == StrikeTargetKind.Building) barred |= BurnableBuilding(world, point) < 0;
                else barred |= Heavy(kind);
                if (barred) mask |= (ushort)(1 << i);
            }
            return mask;
        }

        /// <summary>⭐ CE-3490 (BA-4) — the burnable building at <paramref name="point"/> (index; −1 when none, or no terrain).</summary>
        public static int BurnableBuilding(EntityRepository world, Vector3 point)
            => world.HasSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>() && world.GetSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>() is { } t
               ? Fdp.Toolkit.Terrain.BuildingFires.BurnableAt(t, new Vector2(point.X, point.Y), BuildingSearchM) : -1;

        /// <summary>⭐ CE-3490 — a building strike's point may lie this far outside the footprint (m).</summary>
        public const float BuildingSearchM = 15f;

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

        /// <summary>⭐ <c>CE-2191</c> (§7e) — decoys an evasion releases (one per dispenser interval).</summary>
        public const int DecoySalvo = 4;
        /// <summary>⭐ <c>CE-2191</c> — the evasion holds at least this long (s), and this margin past the missile's time to impact.</summary>
        public const float MinEvadeS = 4f, EvadeMarginS = 2f;
        /// <summary>⭐ <c>CE-2191</c> — a helicopter breaks DOWN to this height above the target's ground (m); a plane keeps its pass height.</summary>
        public const float EvadeHeightAglRotor = 25f;
        /// <summary>⭐ CE-10005 (R-10023) — a plane's missile break flies no lower than this above the ground (m).</summary>
        public const float EvadeHeightAglPlane = 150f;
        /// <summary>⭐ <c>CE-2191</c> — how much of the break turns AWAY from the missile (the rest puts it on the beam).</summary>
        public const float EvadeDragFraction = 0.3f;

        /// <summary>⭐ Slice 6e (§9i D-2) — a drone's cruise height above its target (m) and its dive angle (degrees) — FPV §2.</summary>
        public const float DefaultDiveHeightAgl = 60f, DefaultDiveAngleDeg = 60f;
        /// <summary>⭐ Slice 6e (§9i D-4) — a drone's search circle round a struck point (m).</summary>
        public const float DefaultSearchRadiusM = 150f;
        /// <summary>⭐ Slice 6e — the dive is re-aimed this often (s): every re-aim is a new intent on the wire.</summary>
        public const double DiveSteerPeriod = 0.2;
        /// <summary>⭐ <c>CE-4621</c> MC-4a (R-2122; Motorcycle MV-1) — the operator's delay (s): every aim is made from what the camera showed this
        /// long ago (a whole number of <see cref="DiveSteerPeriod"/>s).</summary>
        public const float OperatorDelaySeconds = 0.4f;
        /// <summary>⭐ MC-4a — the commit point: at this time-to-go (s) the drone stops re-aiming and flies its last line into the ground.
        /// ⇒ a target that breaks at lateral acceleration a after the last aim it could see misses by up to ½·a·(commit + delay)².</summary>
        public const float CommitSeconds = 0.7f;
        /// <summary>⭐ MC-4a — the sightings the track keeps (delay / period + 2: two seen at least the delay ago, for a velocity).</summary>
        public const int TrackSamples = 4;
        /// <summary>⭐ Slice 6e — within this horizontal distance of the target the dive line reaches its middle (m).</summary>
        public const float DiveHitRadiusM = 1f;
        /// <summary>⭐ CE-7014 — <c>AirAttackState.BatteryOut</c> when the link was JAMMED (1 = the battery ran out): it comes down where it is.</summary>
        public const byte BatteryOutJammed = 2;

        private const byte StageStart = 0, StageToRunIn = 1, StageRunIn = 2, StageOut = 3, StageOrbit = 4, StageEvade = 5, StageRadarEvade = 6;

        /// <summary>⭐ <c>CE-4617</c> (B-4, RS-6 / RV-9; DESIGN_Radar_SAM.md) — tracked by a radar, an attacking aircraft drops to this height above
        /// the target's ground (m): below the radar's clutter floor (<c>RadarPerception.ClutterFloorM</c>, 15 m).</summary>
        public const float RadarEvadeAglM = 10f;
        /// <summary>⭐ <c>CE-4617</c> — the radar escape clears this long after the last TRACKED warning (s).</summary>
        public const float RadarClearS = 8f;
        private const byte StageSearch = 6, StagePursue = 7, StageDive = 8, StageHover = 9;
        /// <summary>⭐ <c>CE-7027</c> — a bomber quadcopter drops from this high over the point (m).</summary>
        public const float DefaultDropHeightAgl = 30f;
        /// <summary>⭐ <c>CE-7027</c> — the hover's arrival radius (m): tighter than the hover release's own tolerance.</summary>
        public const float HoverArrivalM = 1f;
        private const byte WeaponRockets = 0, WeaponGun = 1, WeaponDone = 2;

        // ⭐⭐ CE-3490 — the breath attack (docs/designs/air-attack/DESIGN_Breath_Attack.md §2, §4: starter numbers, the dr-dragon-* scenarios tune them)
        /// <summary>BA-2 — the run-in starts this far before the aim (m), flown this high above it (m).</summary>
        public const float BreathRunInM = 250f, BreathCruiseAgl = 60f;
        /// <summary>BA-2 — this far before the aim (m, plus the distance it needs to slow down) it drops to <see cref="BreathAgl"/> and slows to <see cref="BreathSpeedMps"/>.</summary>
        public const float BreathLowM = 80f, BreathAgl = 12f, BreathSpeedMps = 10f;
        /// <summary>BA-2 — past the aim it climbs out to this far beyond it (m), then turns this far round (degrees) for the next pass.</summary>
        public const float BreathClimbOutM = 150f, BreathTurnDeg = 120f;
        /// <summary>BA-2 — passes that breathed nothing before the attack gives up (a held breath counts: BA-6).</summary>
        public const int BreathMaxDryPasses = 4;
        /// <summary>BA-3 — the hostile men remembered this near the aim (m) set the pass's line (their long axis); a point strike is done when none is left.</summary>
        public const float BreathGroupRadiusM = 30f;
        /// <summary>BA-4 — the hover's stand-off from the wall (m) and height above the ground (m), and how long it breathes on the wall (s; F-3's 2 s + 1).</summary>
        public const float BreathHoverWallM = 25f, BreathHoverAgl = 8f;
        public const double BreathOnWallSeconds = 3.0;
        /// <summary>BA-4 — the hover's last leg starts this far further out (m), so the flyer arrives FACING the wall (its neck turns ±70°).</summary>
        public const float BreathHoverLeadM = 40f;
        /// <summary>A breath's fuel shots (one every <see cref="BreathIntervalS"/>; the pass stops it) — the dragon fires 10 a second.</summary>
        public const int BreathRounds = 100;
        public const float BreathIntervalS = 0.1f;
        /// <summary>A hover held this long without a breath (s; a neutral at the wall, the arc) gives up.</summary>
        public const double BreathHoverGiveUpSeconds = 30.0;
        private const byte StageBreathToStart = 10, StageBreathIn = 11, StageBreathLow = 12, StageBreathOut = 13,
                           StageHoverLead = 14, StageHoverStation = 15, StageHoverBreathe = 16, StageBreathHome = 17;

        public static bool IsBreath(AirAttackProfile profile) => profile is AirAttackProfile.BreathPass or AirAttackProfile.BreathHover;

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
                if (!Injury.IsInTheFight(world, p.Target) && ws.Profile != AirAttackProfile.AirCombat)
                {
                    StopFire(world, self, ref ws);
                    // ⭐ CE-3490 — a strafe flies its climb-out to the end first (📐 dr-dragon-attack: the target died with the dragon low over it, and the
                    //   attack's end froze it there, mid-climb): the Out stage ends the attack on arrival
                    if (ws.Profile == AirAttackProfile.BreathPass && ws.Stage is StageBreathIn or StageBreathLow) ClimbOut(in p, ref ws, self, world);
                    if (ws.Profile == AirAttackProfile.BreathPass && ws.Stage == StageBreathOut)
                        return LocomotionMoveTo.Status(world, self) == NodeStatus.Running ? NodeStatus.Running : NodeStatus.Success;
                    return NodeStatus.Success;
                }
                if (Fdp.Toolkit.Replication.ReplicatedMotion.Of(world, p.Target, out var now3, out _)) p.Point = now3;
            }
            var target = p.Point;
            double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d;

            // ⭐⭐ CE-10003 (AC-13, R-2015) — an Attack on an AIRCRAFT by a fighter: the air-combat profile of this ONE attack
            if (entity && (ws.Profile == AirAttackProfile.AirCombat || (ws.Stage == StageStart && AirCombatNodes.Applies(world, self, p.Target))))
            {
                ws.Profile = AirAttackProfile.AirCombat;
                ws.Stage = StageRunIn;
                return AirCombatNodes.Fight(ref p, ref ws, self, world, now);
            }

            if (ws.Stage == StageStart)
            {
                if (!Choose(ref p, ref ws, self, world)) return NodeStatus.Failure;
                if (ws.Profile == AirAttackProfile.Dive)
                {
                    // ⭐ slice 6e (§9i) — an entity is pursued at once; a point is searched first
                    ws.Stage = entity ? StagePursue : StageSearch;
                    ws.StartedAt = (SimInstant)now;
                    ws.NextSteer = 0;
                }
                else if (ws.Profile == AirAttackProfile.HoverDrop)
                {
                    // ⭐ CE-7027 — no run-in: straight to the hover over the point; it comes back here afterwards
                    ws.Home = at;
                    ws.Stage = StageHover;
                    if (!HoverOver(world, self, target, in p)) return NodeStatus.Failure;
                    ws.NextSteer = now + SteerPeriod;
                }
                else if (IsBreath(ws.Profile))
                {
                    // ⭐ CE-3490 — the breath's own run-in (a strafe) or its hover; it comes back here after a hover
                    ws.Home = at;
                    if (!StartBreath(ref p, ref ws, self, world, at, target)) return NodeStatus.Failure;
                }
                else if (ws.Profile == AirAttackProfile.DoorGunOrbit)
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

            if (ws.Profile == AirAttackProfile.Dive) return Dive(ref p, ref ws, self, world, at, now);
            if (ws.Profile == AirAttackProfile.HoverDrop) return HoverDrop(ref p, ref ws, self, world, at, target, now);

            // ⭐ CE-2191 (§7e) — a missile warning interrupts any stage: decoys, a break onto the missile's beam, then the attack comes round
            //   ⭐⭐ CE-10002 (AC-9, RV-14) — through THE ONE missile defence (MissileDefence.Run, a MoveTo flight): started by the launch,
            //   held while the motion node's live threat says a round is inbound (else the launch snapshot), state in MissileDefenceState
            if (Defend(ref p, ref ws, self, world, at, now)) return NodeStatus.Running;
            if (ws.Stage == StageEvade) return ComeRound(ref p, ref ws, self, world, at, target, countPass: true);
            // ⭐ CE-4617 (B-4, RS-6) — a GROUND radar TRACKING it (its own RadarWarning, derived on this node): off the radar's bearing and down
            //   below its clutter floor; the attack comes round 8 s after the track ends, and that pass is NOT counted (a radar abort).
            //   ⭐ RV-3 — a fighter's radar (an aircraft emitter) is exempt (MissileDefence.RadarTracked)
            if (RadarTracked(world, self))
            {
                if (ws.Stage != StageRadarEvade) BeginRadarEvade(ref p, ref ws, self, world, at);
                ws.EvadeUntil = (SimInstant)(now + RadarClearS);
            }
            if (ws.Stage == StageRadarEvade) return RadarEvade(ref p, ref ws, self, world, at, target, now);
            if (IsBreath(ws.Profile)) return Breath(ref p, ref ws, self, world, at, target, now, entity);   // ⭐ CE-3490

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
        /// ⭐ Slice 6e (§9i) — the drone's attack: <b>Search</b> (a point: circle it, pick the best remembered target in the area —
        /// <see cref="DroneTargets.Pick"/>), <b>Pursue</b> (the point above the target at the cruise height), <b>Dive</b> (once the
        /// horizontal distance is within height / tan(angle): at the target's middle, full speed, re-aimed every
        /// <see cref="DiveSteerPeriod"/>). The battery empty (<c>DroneBattery</c>, SD-S2b) ⇒ straight down. The Muscle's impact ends the flight.
        /// </summary>
        private static NodeStatus Dive(ref AirAttackParams p, ref AirAttackState ws, Entity self, EntityRepository world, Vector3 at, double now)
        {
            var flight = world.HasComponent<FlightParams>(self) ? world.GetComponentRO<FlightParams>(self) : default;
            float speed = p.Speed > 0f ? p.Speed : flight.MaxSpeedMps;
            float height = p.AttackHeightAgl > 0f ? p.AttackHeightAgl : DefaultDiveHeightAgl;
            // ⭐ devices SD-S2b (§3.13c D-108) — the drone's ONE DroneBattery (drained airborne, frozen in pause), ⛔ no longer a clock from the attack's start
            if (ws.BatteryOut == 0 && Fdp.Toolkit.Motion.DroneBatteries.Empty(world, self))
            {
                ws.BatteryOut = 1;   // D-6 — whatever is below
                ws.Stage = StageDive;
                ws.NextSteer = 0;
            }
            // ⭐⭐ CE-7014 (City Battle R-2) — in an enemy JAMMER's bubble the control link is lost: the FPV comes down where it is (the battery's
            //   path, 2 = jammed) — it never reaches its target. The bubbles are the serial pre-pass's index (DroneJamming), read here.
            if (ws.BatteryOut == 0 && Fdp.Toolkit.Motion.DroneJamming.Jammed(world, self, at))
            {
                ws.BatteryOut = BatteryOutJammed;
                ws.Stage = StageDive;
                ws.NextSteer = 0;
            }
            if (ws.BatteryOut != 0)
            {
                if (now < ws.NextSteer) return NodeStatus.Running;
                ws.NextSteer = now + DiveSteerPeriod;
                float ground = Fdp.Toolkit.World.WorldQuery.Of(world)?.GroundHeightAt(at.X, at.Y) ?? 0f;
                return LocomotionMoveTo.Issue(world, self, new Vector3(at.X, at.Y, ground), speed, 0f, source: MoveSource.Attack) ? NodeStatus.Running : NodeStatus.Failure;
            }

            if (ws.Stage == StageSearch)
            {
                if (now < ws.NextSteer) return NodeStatus.Running;
                ws.NextSteer = now + SteerPeriod;
                float radius = p.SearchRadiusM > 0f ? p.SearchRadiusM : DefaultSearchRadiusM;
                var picked = DroneTargets.Pick(world, self, p.Point, radius);
                if (!picked.IsNull && Fdp.Toolkit.Replication.ReplicatedMotion.Of(world, picked, out var where, out _))
                {
                    p.Target = picked;
                    p.Point = where;
                    ws.Stage = StagePursue;
                    ws.NextSteer = 0;
                    return NodeStatus.Running;
                }
                // circle the point (D-4): straight at it from afar, then the point 30° ahead on the circle, as the door-gun orbit
                var off = new Vector2(at.X - p.Point.X, at.Y - p.Point.Y);
                Vector3 to;
                if (off.Length() > 1.2f * radius) to = p.Point;
                else
                {
                    float bearing = MathF.Atan2(off.Y, off.X) + OrbitLeadRad;
                    to = p.Point + new Vector3(MathF.Cos(bearing) * radius, MathF.Sin(bearing) * radius, 0f);
                }
                return LocomotionMoveTo.Issue(world, self, new Vector3(to.X, to.Y, p.Point.Z + height), speed, AirOrderNodes.DefaultArrivalRadius, source: MoveSource.Attack)
                    ? NodeStatus.Running : NodeStatus.Failure;
            }

            // ⭐ CE-4621 MC-4a — once committed nothing is re-aimed: the drone flies its last line into the ground (the Muscle's impact ends it)
            if (ws.Committed != 0) return NodeStatus.Running;
            // the target (Run keeps p.Point on it and ends the attack when it is dead), LED: where it will be when the drone gets there.
            //   📐 live ua-drone-shelter (2026-10-16): aimed at where it IS, the drone sank to the hull's height and trailed a fleeing M1
            //   ~10 m behind for a minute (braking on the approach, it held the tank's own speed)
            //   ⭐⭐ CE-4621 MC-4a (R-2122) — LED FROM ITS OWN PERCEIVED TRACK, as an operator flies it: the camera's sightings, the newest one
            //   at least OperatorDelaySeconds old, the velocity from two of them (never the target's true velocity)
            float t = (float)(now - (double)ws.StartedAt);
            var aim = p.Point;
            if (!p.Target.IsNull && world.HasComponent<SimTransform>(p.Target))
            {
                float middleZ = Fdp.Toolkit.Combat.AimPoint.Middle(world, p.Target, out _, out _).Z - world.GetComponentRO<SimTransform>(p.Target).Position.Z;
                if (t >= ws.NextSeen) { See(ref ws, p.Point, t); ws.NextSeen = t + (float)DiveSteerPeriod - 1e-4f; }
                var v = Vector3.Zero;
                if (Perceived(in ws, t, out var seenNow, out v)) aim = seenNow;   // where its sightings say it is now…
                aim.Z += middleZ;
                // …and led to the impact — twice: the time to go measured to the LED point, not to where it is now (📐 measured, MC-4a's
                //   rail: one pass over-led a crossing 20 m/s bike by ~2 m — the drone reached the aim height 0.1 s before it)
                var flatV = new Vector3(v.X, v.Y, 0f);
                var led = aim + flatV * (Vector3.Distance(at, aim) / MathF.Max(speed, 1f));
                aim += flatV * (Vector3.Distance(at, led) / MathF.Max(speed, 1f));
            }
            float flat = new Vector2(aim.X - at.X, aim.Y - at.Y).Length();
            float angle = (p.DiveAngleDeg > 0f ? p.DiveAngleDeg : DefaultDiveAngleDeg) * MathF.PI / 180f;
            if (ws.Stage == StagePursue && flat <= MathF.Max(at.Z - aim.Z, 0f) / MathF.Tan(angle))
            {
                ws.Stage = StageDive;
                ws.NextSteer = 0;
            }
            // ⭐ CE-4621 MC-4a — the COMMIT: at the time-to-go of CommitSeconds the last aim is the line from the drone through the led
            //   target, carried on into the ground, and nothing is re-aimed after it — checked every tick, not only at a re-aim
            if (ws.Stage == StageDive && !p.Target.IsNull && Vector3.Distance(at, aim) <= CommitSeconds * MathF.Max(speed, 1f))
            {
                ws.Committed = 1;
                var line = aim - at;
                float ground = Fdp.Toolkit.World.WorldQuery.Of(world)?.GroundHeightAt(aim.X, aim.Y) ?? 0f;
                float drop = at.Z - aim.Z;
                float k = drop > 0.1f ? MathF.Max(1f, (at.Z - (ground - CommitPastGroundM)) / drop) : 1f;
                return LocomotionMoveTo.Issue(world, self, at + line * k, speed, 0f, source: MoveSource.Attack) ? NodeStatus.Running : NodeStatus.Failure;
            }
            if (now < ws.NextSteer) return NodeStatus.Running;
            if (ws.Stage == StagePursue)
            {
                ws.NextSteer = now + SteerPeriod;
                return LocomotionMoveTo.Issue(world, self, new Vector3(aim.X, aim.Y, p.Point.Z + height), speed, AirOrderNodes.DefaultArrivalRadius, source: MoveSource.Attack)
                    ? NodeStatus.Running : NodeStatus.Failure;
            }
            ws.NextSteer = now + DiveSteerPeriod;
            // D-3 — down the DIVE LINE: the point above the (led) target at flat · tan(angle), never above the cruise height — so the drone
            //   stays over the target until it is above it and comes down on its roof, never skimming the ground behind it
            //   ⭐ CE-4621 MC-4a — the point is a LOOK-AHEAD down that line (one commit's flight nearer the target), not the point straight
            //   over the target at the drone's own height: the drone then flies the line itself, so the committed line CONTINUES its path
            //   (📐 the old point made it fly level and curl down, and the turn at the commit cost ~2 m against a 20 m/s bike)
            var back = new Vector2(at.X - aim.X, at.Y - aim.Y);
            var u = back.LengthSquared() > 1e-4f ? Vector2.Normalize(back) : Vector2.Zero;
            float f = MathF.Max(0f, flat - DiveHitRadiusM - CommitSeconds * MathF.Max(speed, 1f) * MathF.Cos(angle));
            float over = MathF.Min(height, f * MathF.Tan(angle));
            var onLine = aim + new Vector3(u * f, 0f) + new Vector3(0f, 0f, over);
            return LocomotionMoveTo.Issue(world, self, onLine, speed, 0f, source: MoveSource.Attack) ? NodeStatus.Running : NodeStatus.Failure;
        }

        /// <summary>⭐ MC-4a — the committed line is carried this far under the ground (m), so the drone never brakes or hovers short of it.</summary>
        public const float CommitPastGroundM = 5f;

        /// <summary>⭐ <c>CE-4621</c> MC-4a — one camera sighting of the target (its position, no velocity) onto the track, newest first.</summary>
        public static void See(ref AirAttackState ws, Vector3 seen, float t)
        {
            ws.Seen3 = ws.Seen2; ws.Seen2 = ws.Seen1; ws.Seen1 = ws.Seen0;
            ws.Seen0 = new Vector4(seen, t);
            if (ws.SeenCount < TrackSamples) ws.SeenCount++;
        }

        /// <summary>⭐ <c>CE-4621</c> MC-4a — what the operator acts on at <paramref name="t"/>: the newest sighting at least
        /// <see cref="OperatorDelaySeconds"/> old and the velocity from it and the one before (zero with one only); false with none.</summary>
        public static bool Perceived(in AirAttackState ws, float t, out Vector3 at, out Vector3 velocity)
        {
            at = default; velocity = default;
            float cut = t - OperatorDelaySeconds + 1e-3f;
            int n = ws.SeenCount;
            for (int i = 0; i < n; i++)
            {
                var s = Sample(in ws, i);
                if (s.W > cut) continue;
                at = new Vector3(s.X, s.Y, s.Z);
                if (i + 1 < n)
                {
                    var o = Sample(in ws, i + 1);
                    float dt = s.W - o.W;
                    if (dt > 1e-3f) velocity = (at - new Vector3(o.X, o.Y, o.Z)) / dt;
                }
                at += velocity * (t - s.W);   // where that sighting says it is NOW
                return true;
            }
            return false;
        }

        private static Vector4 Sample(in AirAttackState ws, int i) => i switch { 0 => ws.Seen0, 1 => ws.Seen1, 2 => ws.Seen2, _ => ws.Seen3 };

        /// <summary>⭐ <c>CE-2191</c> — the decoy that fools the warned seeker: a flare an infra-red one, chaff a radar one; none for a
        /// command-guided or fire-and-forget missile (the break alone).</summary>
        public static DecoyKind? DecoyFor(Fdp.Toolkit.Tkb.Domain.GuidanceKind seeker) => seeker switch
        {
            Fdp.Toolkit.Tkb.Domain.GuidanceKind.InfraRed => DecoyKind.Flare,
            Fdp.Toolkit.Tkb.Domain.GuidanceKind.Radar => DecoyKind.Chaff,
            _ => null,
        };

        /// <summary>
        /// ⭐ <c>CE-4617</c> (B-2, RV-7; docs/designs/air-attack/DESIGN_Radar_SAM.md) — chaff is held until the missile's END-GAME (its time to go,
        /// estimated from the warning, inside <see cref="Fdp.Toolkit.Combat.Systems.CountermeasureSystem.ChaffEndGameS"/>): a radar seeker
        /// ignores a cloud before that (<c>CountermeasureSystem.InChaffEndGame</c>), so a cloud dropped at the launch is wasted. Flares are unchanged.
        /// </summary>
        public static bool ChaffTooEarly(DecoyKind kind, in MissileWarning w, double now)
            => kind == DecoyKind.Chaff && w.TimeToImpactS - (now - (double)w.At) > Fdp.Toolkit.Combat.Systems.CountermeasureSystem.ChaffEndGameS;

        /// <summary>
        /// ⭐⭐ CE-10002 (AC-9) — the attack's missile defence, through <see cref="MissileDefence.Run"/> (a MoveTo flight: the beam break
        /// <see cref="BreakAwayM"/> out; a helicopter drops to <see cref="EvadeHeightAglRotor"/>, a plane keeps its height). True while it runs;
        /// on its first frame the weapon stops and the stage to come back to is kept. A second launch re-breaks.
        /// </summary>
        private static bool Defend(ref AirAttackParams p, ref AirAttackState ws, Entity self, EntityRepository world, Vector3 at, double now)
        {
            var before = MissileDefence.Load(world, self);
            var ds = before;
            bool rotor = Locomotions.Of(world, self) == Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft;
            // ⭐ CE-10005 (R-10023) — a PLANE breaks at no less than EvadeHeightAglPlane above the ground under it: 📐 measured on ac-attack-evades,
            //   the A-10's beam break "at its height" from a 16 m gun-run pass flew into the hills and it crashed
            float planeZ = MathF.Max(at.Z, (Fdp.Toolkit.World.WorldQuery.Of(world)?.GroundHeightAt(at.X, at.Y) ?? 0f) + EvadeHeightAglPlane);
            var flight = new MissileDefence.Flight { Fighter = false, MoveZ = rotor ? p.Point.Z + EvadeHeightAglRotor : planeZ, Speed = p.Speed, FallbackDir = ws.Dir };
            bool defending = MissileDefence.Run(world, self, ref ds, now, in flight, out _, out bool started);
            if (!Same(in before, in ds)) MissileDefence.Store(world, self, in ds);
            if (started)
            {
                StopFire(world, self, ref ws);
                if (ws.Stage != StageEvade) ws.Resume = ws.Stage == StageOrbit ? StageOrbit : StageRunIn;
                ws.Stage = StageEvade;
            }
            return defending && ws.Stage == StageEvade;
        }

        /// <summary>Byte equality of two defence states (no boxing: the state is written only when it changed).</summary>
        internal static bool Same(in MissileDefenceState a, in MissileDefenceState b)
            => System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<MissileDefenceState>(in a))
                .SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<MissileDefenceState>(in b)));

        /// <summary>
        /// ⭐ <c>CE-4617</c> (B-4, RS-6 / RV-9) — is <paramref name="self"/> TRACKED by a radar now (its <see cref="Fdp.Toolkit.Perception.Radar.RadarWarning"/>,
        /// written on this node by <c>RadarWarningReceiverSystem</c> from the emitters' replicated state)? A searching radar does not count.
        /// </summary>
        public static bool RadarTracked(EntityRepository world, Entity self) => MissileDefence.RadarTracked(world, self);

        /// <summary>⭐ <c>CE-4617</c> — the radar escape: the weapon stops; the aircraft turns AWAY from the radar's bearing (half beam, half away —
        /// off the line it is tracked along) and drops to <see cref="RadarEvadeAglM"/>, below the clutter floor.</summary>
        private static void BeginRadarEvade(ref AirAttackParams p, ref AirAttackState ws, Entity self, EntityRepository world, Vector3 at)
        {
            StopFire(world, self, ref ws);
            if (ws.Stage != StageRadarEvade) ws.Resume = ws.Stage == StageOrbit ? StageOrbit : StageRunIn;
            ws.Stage = StageRadarEvade;
            RadarEscape(ref p, ref ws, self, world, at);
        }

        private static void RadarEscape(ref AirAttackParams p, ref AirAttackState ws, Entity self, EntityRepository world, Vector3 at)
            => MissileDefence.RadarEscape(world, self, at, p.Point.Z + RadarEvadeAglM, p.Speed, ws.Dir);

        /// <summary>⭐ <c>CE-4617</c> — while tracked it keeps escaping (a fresh leg when one ends); <see cref="RadarClearS"/> after the last track
        /// the attack comes round — ⭐ a radar abort is NOT a counted pass (RV-9: three aborts would drive it off without firing).</summary>
        private static NodeStatus RadarEvade(ref AirAttackParams p, ref AirAttackState ws, Entity self, EntityRepository world,
                                             Vector3 at, Vector3 target, double now)
        {
            if (now < ws.EvadeUntil)
            {
                if (RadarTracked(world, self) && LocomotionMoveTo.Status(world, self) != NodeStatus.Running) RadarEscape(ref p, ref ws, self, world, at);
                return NodeStatus.Running;
            }
            return ComeRound(ref p, ref ws, self, world, at, target, countPass: false);
        }

        /// <summary>⭐ <c>CE-2191</c> (shared by <c>CE-4617</c>) — the attack comes round after an escape: the orbit carries on; any other profile
        /// flies a fresh run-in from where it is now — the aborted pass counted (a missile) or not (a radar track).</summary>
        private static NodeStatus ComeRound(ref AirAttackParams p, ref AirAttackState ws, Entity self, EntityRepository world,
                                            Vector3 at, Vector3 target, bool countPass)
        {
            ws.Stage = ws.Resume;
            ws.NextSteer = 0;
            if (ws.Resume == StageOrbit) return NodeStatus.Running;
            if (countPass && ++ws.Passes >= MaxPasses)
            {
                BehaviorLog.Error(self, world, $"AirAttack: driven off — {MaxPasses} passes, the last broken off by a missile.");
                return NodeStatus.Failure;
            }
            if (!Choose(ref p, ref ws, self, world)) return NodeStatus.Failure;
            if (IsBreath(ws.Profile)) return StartBreath(ref p, ref ws, self, world, at, target) ? NodeStatus.Running : NodeStatus.Failure;   // ⭐ CE-3490 BA-7
            ws.Dir = Flat(target - at);
            ws.Weapon = WeaponRockets;
            ws.Fired = 0;
            var start = target - new Vector3(ws.Dir * RunIn(in p, world, self), 0f);
            if (!Fly(world, self, start, in p, ws.Profile)) return NodeStatus.Failure;
            ws.Stage = StageToRunIn;
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
            // ⭐ CE-3490 (BA-5, BA-6) — a flame that cannot light this target, or that the ROE forbids, is passed over like a dry mount
            ushort unlightable = AirAttackProfiles.Unlightable(world, self, p.Target, p.Point, p.Kind);
            ushort excluded = (ushort)(ws.Spent | endangering | unlightable);
            AirAttackProfile? picked;
            byte mount, gun;
            if (p.Profile == AirAttackProfile.Auto)
                picked = AirAttackProfiles.Pick(world, self, out mount, out gun, !p.Target.IsNull, p.Kind, excluded);
            else picked = AirAttackProfiles.MountsFor(world, self, p.Profile, excluded, out mount, out gun) ? p.Profile : null;
            if (picked == null)
            {
                BehaviorLog.Error(self, world,
                    unlightable != 0 ? "AirAttack: the flame cannot light this target (or the ROE forbids incendiaries), and no other weapon is left for it."
                    : endangering != 0 ? "AirAttack: every weapon left would hurt friends or neutrals near the point (order it danger close to fire)."
                    : ws.Spent != 0 ? "AirAttack: no weapon left for this target."
                    : p.Profile == AirAttackProfile.Auto
                        ? "AirAttack: no attack profile for this aircraft and its weapons (a plane needs a bomb, a rocket pod or a gun; a helicopter or drone rockets, a gun or a drop rack)."
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

        /// <summary>A straight round's speed from <paramref name="mount"/> — the order FireProcessingSystem flies it at (`:156-158`): the
        /// missile's own, else (an aircraft's mount 1+) the mount's muzzle velocity, else the FIRING entity's <c>WeaponState</c> muzzle
        /// velocity (a door gun 32 + k is its weapon system, on its own mount 0), else the mount's, else the engine's fallback.
        /// 📐 Measured live (ua-air-attack ⑤): the M134 flies at its WeaponState's 500 m/s while the lead assumed the 800 m/s fallback ⇒
        /// every round passed ~3 m over the truck.</summary>
        /// <summary>⭐ CE-10003 — <see cref="RoundSpeed"/> for the air-combat gun.</summary>
        internal static float RoundSpeedOf(EntityRepository world, Entity self, byte mount) => RoundSpeed(world, self, mount);

        /// <summary>⭐ CE-10003 — <see cref="Retarget"/> for the air-combat gun.</summary>
        internal static void RetargetFire(EntityRepository world, Entity self, uint started, Vector3 aim) => Retarget(world, self, started, aim);

        private static float RoundSpeed(EntityRepository world, Entity self, byte mount)
        {
            if (mount == AirAttackProfiles.None) return Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.MuzzleVelocity;
            bool controlled = WeaponSystems.IsControlledIndex(mount);
            var firing = controlled ? WeaponSystems.ControlledAt(world, self, mount) : self;
            int index = controlled ? 0 : mount;
            var m = firing.IsNull ? null : CombatTkb.MountOf(world, firing, index);
            if (m != null && CombatTkb.MissileOf(world, unchecked((long)m.AmmoGuid)) is { SpeedMps: > 0f } missile) return missile.SpeedMps;
            if (index > 0 && m is { MuzzleVelocity: > 0f }) return m.MuzzleVelocity;
            if (!firing.IsNull && world.HasComponent<Fdp.Toolkit.Combat.Components.WeaponState>(firing) && world.GetComponentRO<Fdp.Toolkit.Combat.Components.WeaponState>(firing).MuzzleVelocity > 0f)
                return world.GetComponentRO<Fdp.Toolkit.Combat.Components.WeaponState>(firing).MuzzleVelocity;
            return m is { MuzzleVelocity: > 0f } ? m.MuzzleVelocity : Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.MuzzleVelocity;
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

        /// <summary>
        /// ⭐⭐ CE-3490 (BA-2…BA-4) — starts (or restarts, after an evasion) a breath attack from where the flyer is: a STRAFE flies to its run-in start on
        /// the pass line (BA-3; straight on when already on it); a HOVER finds the building's nearest burnable wall and flies to its station 25 m off it, by
        /// a lead point further out so it arrives facing the wall. False (logged) when there is nothing to fly to.
        /// </summary>
        private static bool StartBreath(ref AirAttackParams p, ref AirAttackState ws, Entity self, EntityRepository world, Vector3 at, Vector3 target)
        {
            ws.NextSteer = 0;
            ws.Fire = default;
            ws.Fired = 0;
            if (ws.Profile == AirAttackProfile.BreathHover)
            {
                int building = AirAttackProfiles.BurnableBuilding(world, target);
                var terrain = world.HasSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>() ? world.GetSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>() : null;
                if (building < 0 || terrain == null
                    || !Fdp.Toolkit.Terrain.BuildingFires.NearestBurnableWall(terrain, building, new Vector2(at.X, at.Y), out var wall, out var outward, out float midZ))
                {
                    BehaviorLog.Error(self, world, "AirAttack: no burnable wall at the struck building.");
                    return false;
                }
                var station = wall + outward * BreathHoverWallM;
                ws.HoverAt = new Vector3(station, Ground(world, station) + BreathHoverAgl);
                ws.WallAim = new Vector3(wall, midZ);
                ws.BreathSince = default;
                ws.Dir = -outward;
                ws.Stage = StageHoverLead;
                var lead = station + outward * BreathHoverLeadM;
                return LocomotionMoveTo.Issue(world, self, new Vector3(lead, ws.HoverAt.Z), p.Speed, AirOrderNodes.DefaultArrivalRadius, source: MoveSource.Attack);
            }
            ws.Dir = PassLine(world, self, at, target, out bool group);
            if (group) ws.GroupLine = 1;
            var off = new Vector2(at.X - target.X, at.Y - target.Y);
            float along = -Vector2.Dot(off, ws.Dir);                                    // > 0 ⇒ it is before the aim on the line
            float aside = MathF.Abs(off.X * ws.Dir.Y - off.Y * ws.Dir.X);
            if (along >= 0.5f * BreathRunInM && aside <= BreathGroupRadiusM)
            {
                ws.Stage = StageBreathIn;                                               // already on the line, far enough out: straight in
                return true;
            }
            ws.Stage = StageBreathToStart;
            var start = target - new Vector3(ws.Dir * BreathRunInM, 0f);
            return LocomotionMoveTo.Issue(world, self, new Vector3(start.X, start.Y, p.Point.Z + BreathCruiseAgl), p.Speed, AirOrderNodes.DefaultArrivalRadius, source: MoveSource.Attack);
        }

        /// <summary>
        /// ⭐⭐ CE-3490 (DESIGN_Breath_Attack §0) — one tick of a breath attack. STRAFE: to the run-in start → in at 60 m → (80 m out, plus its braking
        /// distance) down to 12 m at 10 m/s, breathing while the aim is in the jet's reach from the mouth → over the aim, out 150 m and up → the next pass
        /// 120° round. Ends: the entity out of the fight (<see cref="Run"/>), a point with no hostile left near it after a pass that breathed, four passes
        /// that breathed nothing, or no fuel left. HOVER: the lead point, the station, 3 s on the wall, home.
        /// </summary>
        private static NodeStatus Breath(ref AirAttackParams p, ref AirAttackState ws, Entity self, EntityRepository world, Vector3 at, Vector3 target, double now, bool entity)
        {
            if (ws.Profile == AirAttackProfile.BreathHover) return BreathHover(ref p, ref ws, self, world, at, now);
            var aim = target + new Vector3(0f, 0f, Fdp.Toolkit.Combat.FlameJets.TargetMiddleM);   // a man's middle (the jet's aim at a unit)
            float along = Vector2.Dot(new Vector2(target.X - at.X, target.Y - at.Y), ws.Dir);    // > 0 ⇒ the aim is still ahead
            var beyond = target + new Vector3(ws.Dir * BreathClimbOutM, 0f);
            switch (ws.Stage)
            {
                case StageBreathToStart:
                {
                    if (RepickLine(ref p, ref ws, self, world, at, target, now, out var repicked)) return repicked;
                    var s = LocomotionMoveTo.Status(world, self);
                    if (s == NodeStatus.Failure) return NodeStatus.Failure;
                    if (s == NodeStatus.Running) return NodeStatus.Running;
                    ws.Stage = StageBreathIn;
                    ws.NextSteer = 0;
                    return NodeStatus.Running;
                }
                case StageBreathIn:
                    if (RepickLine(ref p, ref ws, self, world, at, target, now, out var again)) return again;
                    if (along <= BreathLowM + LetDownDistance(world, self, at.Z - (p.Point.Z + BreathAgl)))
                    {
                        ws.Stage = StageBreathLow;
                        ws.NextSteer = 0;
                        goto case StageBreathLow;
                    }
                    if (now >= ws.NextSteer)
                    {
                        if (!LocomotionMoveTo.Issue(world, self, new Vector3(beyond.X, beyond.Y, p.Point.Z + BreathCruiseAgl), p.Speed, AirOrderNodes.DefaultArrivalRadius, source: MoveSource.Attack))
                            return NodeStatus.Failure;
                        ws.NextSteer = now + SteerPeriod;
                    }
                    return NodeStatus.Running;
                case StageBreathLow:
                    if (along <= 0f)
                    {
                        StopFire(world, self, ref ws);   // over the aim: the breath ends (a burst that left a round counts the pass as lit)
                        ws.Stage = StageBreathOut;
                        return LocomotionMoveTo.Issue(world, self, new Vector3(beyond.X, beyond.Y, p.Point.Z + BreathCruiseAgl), p.Speed, AirOrderNodes.DefaultArrivalRadius, source: MoveSource.Attack)
                            ? NodeStatus.Running : NodeStatus.Failure;
                    }
                    if (now >= ws.NextSteer)
                    {
                        if (!LocomotionMoveTo.Issue(world, self, new Vector3(beyond.X, beyond.Y, p.Point.Z + BreathAgl), BreathSpeedMps, AirOrderNodes.DefaultArrivalRadius, source: MoveSource.Attack))
                            return NodeStatus.Failure;
                        ws.NextSteer = now + SteerPeriod;
                    }
                    return Breathe(ref p, ref ws, self, world, at, target, aim);
                default:   // StageBreathOut — the pass is done when it is out and up
                {
                    var s = LocomotionMoveTo.Status(world, self);
                    if (s == NodeStatus.Failure) return NodeStatus.Failure;
                    if (s == NodeStatus.Running) return NodeStatus.Running;
                    if (ws.Fired == 0 && ++ws.Passes >= BreathMaxDryPasses)
                    {
                        BehaviorLog.Error(self, world, $"AirAttack: {BreathMaxDryPasses} breath passes lit nothing (held, or out of reach).");
                        return NodeStatus.Failure;
                    }
                    if (!entity && ws.Fired != 0 && HostilesNear(world, self, target, BreathGroupRadiusM, out _, out _) == 0) return NodeStatus.Success;
                    // the next pass: a line 120° round — never the same line twice (BA-2)
                    float turn = BreathTurnDeg * (MathF.PI / 180f);
                    ws.Dir = new Vector2(ws.Dir.X * MathF.Cos(turn) - ws.Dir.Y * MathF.Sin(turn), ws.Dir.X * MathF.Sin(turn) + ws.Dir.Y * MathF.Cos(turn));
                    ws.Fired = 0;
                    ws.Stage = StageBreathToStart;
                    var start = target - new Vector3(ws.Dir * BreathRunInM, 0f);
                    return LocomotionMoveTo.Issue(world, self, new Vector3(start.X, start.Y, p.Point.Z + BreathCruiseAgl), p.Speed, AirOrderNodes.DefaultArrivalRadius, source: MoveSource.Attack)
                        ? NodeStatus.Running : NodeStatus.Failure;
                }
            }
        }

        /// <summary>⭐ CE-3490 (BA-3) — the first pass's line was set before the flyer had seen anyone (📐 dr-dragon-attack: its memory was empty on the
        /// attack's first tick, so the pass crossed the row and burnt one man): on the run-in, the first time it remembers a GROUP near the aim, the run
        /// restarts on the group's line. Once per attack; checked every <see cref="SteerPeriod"/>, beyond the let-down.</summary>
        private static bool RepickLine(ref AirAttackParams p, ref AirAttackState ws, Entity self, EntityRepository world, Vector3 at, Vector3 target, double now, out NodeStatus status)
        {
            status = NodeStatus.Running;
            if (ws.GroupLine != 0 || now < ws.NextSteer && ws.Stage == StageBreathIn) return false;
            if (HostilesNear(world, self, target, BreathGroupRadiusM, out _, out _) < 2) return false;
            var line = PassLine(world, self, at, target, out bool group);
            if (!group) return false;
            ws.GroupLine = 1;
            if (Vector2.Dot(line, ws.Dir) > 0.94f) return false;   // within 20°: keep the run it is on
            status = StartBreath(ref p, ref ws, self, world, at, target) ? NodeStatus.Running : NodeStatus.Failure;
            return true;
        }

        /// <summary>⭐ CE-3490 (BA-2) — the breath on the low leg: a held <c>FireAtPoint</c> at the aim while it is inside the jet's reach from the mouth
        /// (<see cref="Fdp.Toolkit.Combat.ShotOrigin.From"/>, the rule the jet itself leaves by); out of reach ⇒ the burst stops. The release gates
        /// (the ROE's incendiary rule, a neutral in the flame) are the executor's: a held breath is Running, never a failure (BA-6).</summary>
        private static NodeStatus Breathe(ref AirAttackParams p, ref AirAttackState ws, Entity self, EntityRepository world, Vector3 at, Vector3 target, Vector3 aim)
        {
            float reach = BreathReach(world, self, ws.Mount);
            var mouth = Fdp.Toolkit.Combat.ShotOrigin.From(world, self, ws.Mount, Fdp.Toolkit.Combat.ShotOrigin.Eye(world, self, out _), aim, out _);
            if (Vector3.Distance(mouth, aim) > reach)
            {
                if (ws.Fire.Started != 0) StopFire(world, self, ref ws);
                ws.Weapon = WeaponRockets;   // StopFire marks the weapon done; a breath may start again inside the reach
                return NodeStatus.Running;
            }
            var fire = new FireAtPointNodeParams { Point = aim, Mount = ws.Mount, Started = ws.Fire.Started, Rounds = BreathRounds, CooldownSeconds = BreathIntervalS };
            var status = FireAtPointNodes.FireAtPoint(ref fire, self, world);
            ws.Fire = fire;
            if (status == NodeStatus.Running) { Retarget(world, self, ws.Fire.Started, aim); return NodeStatus.Running; }
            ws.Fire = default;
            if (status == NodeStatus.Success) { ws.Fired = 1; return NodeStatus.Running; }
            return BreathSpent(ref p, ref ws, self, world, at, target);
        }

        /// <summary>⭐ CE-3490 — the flame ran dry (or was refused): the other weapons' profile (a mixed loadout, BA-8), else the attack fails, logged.</summary>
        private static NodeStatus BreathSpent(ref AirAttackParams p, ref AirAttackState ws, Entity self, EntityRepository world, Vector3 at, Vector3 target)
        {
            ws.Spent |= (ushort)(1 << ws.Mount);
            if (!Choose(ref p, ref ws, self, world)) return NodeStatus.Failure;
            if (IsBreath(ws.Profile)) return StartBreath(ref p, ref ws, self, world, at, target) ? NodeStatus.Running : NodeStatus.Failure;
            ws.Dir = Flat(target - at);
            ws.Stage = StageRunIn;
            ws.NextSteer = 0;
            return NodeStatus.Running;
        }

        /// <summary>⭐ CE-3490 (BA-4) — the hover on a building: the lead point, the station (arriving facing the wall), 3 s of breath on the wall, home.</summary>
        private static NodeStatus BreathHover(ref AirAttackParams p, ref AirAttackState ws, Entity self, EntityRepository world, Vector3 at, double now)
        {
            var s = LocomotionMoveTo.Status(world, self);
            switch (ws.Stage)
            {
                case StageHoverLead:
                    if (s == NodeStatus.Failure) return NodeStatus.Failure;
                    if (s == NodeStatus.Running) return NodeStatus.Running;
                    ws.Stage = StageHoverStation;
                    return LocomotionMoveTo.Issue(world, self, ws.HoverAt, BreathSpeedMps, HoverArrivalM, source: MoveSource.Attack) ? NodeStatus.Running : NodeStatus.Failure;
                case StageHoverStation:
                    if (s == NodeStatus.Failure) return NodeStatus.Failure;
                    if (s == NodeStatus.Running) return NodeStatus.Running;
                    ws.Stage = StageHoverBreathe;
                    ws.BreathSince = (SimInstant)now;
                    return NodeStatus.Running;
                case StageHoverBreathe:
                {
                    int rounds = (int)MathF.Ceiling((float)(BreathOnWallSeconds / BreathIntervalS));
                    var fire = new FireAtPointNodeParams { Point = ws.WallAim, Mount = ws.Mount, Started = ws.Fire.Started, Rounds = rounds, CooldownSeconds = BreathIntervalS };
                    var status = FireAtPointNodes.FireAtPoint(ref fire, self, world);
                    ws.Fire = fire;
                    if (status == NodeStatus.Running)
                    {
                        if (now - (double)ws.BreathSince > BreathHoverGiveUpSeconds && Fdp.Toolkit.Combat.Executors.FireAtPointExecutor.RoundsFired(ref Channel(world, self)) == 0)
                        {
                            StopFire(world, self, ref ws);
                            BehaviorLog.Error(self, world, "AirAttack: the breath on the building was held too long (a neutral in the flame, or out of the neck's arc).");
                            return NodeStatus.Failure;
                        }
                        return NodeStatus.Running;
                    }
                    ws.Fire = default;
                    if (status == NodeStatus.Failure) return NodeStatus.Failure;
                    ws.Fired = 1;
                    ws.Stage = StageBreathHome;
                    return LocomotionMoveTo.Issue(world, self, new Vector3(ws.Home.X, ws.Home.Y, MathF.Max(ws.Home.Z, ws.HoverAt.Z)), p.Speed, AirOrderNodes.DefaultArrivalRadius, source: MoveSource.Attack)
                        ? NodeStatus.Running : NodeStatus.Failure;
                }
                default:   // StageBreathHome — done on arrival
                    return s;
            }
        }

        private static ref Fdp.Toolkit.Behavior.Components.WeaponChannel Channel(EntityRepository world, Entity self)
            => ref world.GetComponentRW<Fdp.Toolkit.Behavior.Components.WeaponChannel>(self);

        /// <summary>⭐ CE-3490 (BA-3) — the pass's line (unit, flat): the long axis of the hostile men remembered within <see cref="BreathGroupRadiusM"/> of the aim
        /// (a line of men burns in one pass), pointed away from the flyer; fewer than two ⇒ straight at the aim from where it is.</summary>
        private static Vector2 PassLine(EntityRepository world, Entity self, Vector3 at, Vector3 target, out bool group)
        {
            group = false;
            var toward = Flat(target - at);
            if (HostilesNear(world, self, target, BreathGroupRadiusM, out var mean, out var cov) < 2) return toward;
            // the principal axis of the 2×2 covariance (sxx, sxy, syy)
            float sxx = cov.X, sxy = cov.Y, syy = cov.Z;
            float angle = 0.5f * MathF.Atan2(2f * sxy, sxx - syy);
            var axis = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            float spread = MathF.Sqrt(MathF.Max(0f, 0.5f * (sxx + syy) + MathF.Sqrt(0.25f * (sxx - syy) * (sxx - syy) + sxy * sxy)));
            if (spread < 1f) return toward;   // a knot of men, no line: straight in
            group = true;
            return Vector2.Dot(axis, toward) >= 0f ? axis : -axis;
        }

        /// <summary>⭐ CE-3490 — the live, identified hostile contacts in the flyer's OWN memory within <paramref name="radius"/> (flat) of <paramref name="centre"/>:
        /// their count, mean and covariance (sxx, sxy, syy). 0 B per call.</summary>
        private static unsafe int HostilesNear(EntityRepository world, Entity self, Vector3 centre, float radius, out Vector2 mean, out Vector3 cov)
        {
            mean = default;
            cov = default;
            if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Components.TargetMemory>()
                || !world.HasComponent<Fdp.Toolkit.Perception.Components.TargetMemory>(self)) return 0;
            ref readonly var memory = ref world.GetComponentRO<Fdp.Toolkit.Perception.Components.TargetMemory>(self);
            int n = 0;
            float sx = 0f, sy = 0f, sxx = 0f, sxy = 0f, syy = 0f;
            for (int i = 0; i < memory.Count; i++)
            {
                if (Fdp.Toolkit.Perception.Components.TargetMemory.IsAnonymous(in memory, i)) continue;
                var e = new Entity((ulong)memory.EntityIds[i]);
                if (!world.IsAlive(e) || !Injury.IsInTheFight(world, e) || !DroneTargets.IsHostile(world, self, e)) continue;
                float x = memory.PositionsX[i], y = memory.PositionsY[i];
                if (new Vector2(x - centre.X, y - centre.Y).Length() > radius) continue;
                n++; sx += x; sy += y; sxx += x * x; sxy += x * y; syy += y * y;
            }
            if (n == 0) return 0;
            mean = new Vector2(sx / n, sy / n);
            cov = new Vector3(sxx / n - mean.X * mean.X, sxy / n - mean.X * mean.Y, syy / n - mean.Y * mean.Y);
            return n;
        }

        /// <summary>⭐ CE-3490 — the flame's reach (m): the loaded fuel's <c>FlameRangeM</c>, else the mount's range.</summary>
        private static float BreathReach(EntityRepository world, Entity self, byte mount)
        {
            var m = CombatTkb.MountOf(world, self, mount);
            if (m == null) return 0f;
            return CombatTkb.WarheadOf(world, m) is { FlameRangeM: > 0f } w ? w.FlameRangeM : m.Range;
        }

        /// <summary>⭐ CE-3490 (BA-2) — how far before the low leg's 80 m the flyer must start down (m) to be AT 12 m and 10 m/s by then: the larger of its
        /// braking distance from its speed now (its type's acceleration) and the ground it covers descending <paramref name="drop"/> m at its climb rate at
        /// the breath speed. 📐 Rail-measured: started at 80 m flat, the stand-in was still 28 m up at its first breath.</summary>
        private static float LetDownDistance(EntityRepository world, Entity self, float drop)
        {
            var flight = world.HasComponent<FlightParams>(self) ? world.GetComponentRO<FlightParams>(self) : default;
            float braking = 0f;
            if (Fdp.Toolkit.Replication.ReplicatedMotion.Of(world, self, out _, out var v))
            {
                float speed = new Vector2(v.X, v.Y).Length();
                if (flight.AccelMps2 > 0f && speed > BreathSpeedMps) braking = (speed * speed - BreathSpeedMps * BreathSpeedMps) / (2f * flight.AccelMps2);
            }
            float climb = flight.ClimbMps > 0f ? flight.ClimbMps : DefaultLetDownMps;
            float descent = MathF.Max(0f, drop) / climb * BreathSpeedMps;
            return MathF.Max(braking, descent);
        }

        /// <summary>⭐ CE-3490 — the descent rate assumed when the type states no climb rate (m/s; the dragon's is 8).</summary>
        public const float DefaultLetDownMps = 8f;

        /// <summary>⭐ CE-3490 — a strafe whose target is out of the fight mid-pass flies on out and up, 150 m beyond the aim at 60 m, and the attack ends
        /// on arrival there (<see cref="Run"/>).</summary>
        private static void ClimbOut(in AirAttackParams p, ref AirAttackState ws, Entity self, EntityRepository world)
        {
            var beyond = p.Point + new Vector3(ws.Dir * BreathClimbOutM, 0f);
            LocomotionMoveTo.Issue(world, self, new Vector3(beyond.X, beyond.Y, p.Point.Z + BreathCruiseAgl), p.Speed, AirOrderNodes.DefaultArrivalRadius, source: MoveSource.Attack);
            ws.Stage = StageBreathOut;
        }

        private static float Ground(EntityRepository world, Vector2 at) => Fdp.Toolkit.World.WorldQuery.Of(world)?.GroundHeightAt(at.X, at.Y) ?? 0f;

        /// <summary>A MoveTo to (x, y) at the pass height above the ground at the TARGET (the run is flown level).</summary>
        /// <summary>⭐ <c>CE-7027</c> — HoverDrop: fly to hover over the point (an entity's point refreshed every <see cref="SteerPeriod"/>);
        /// standing over it, the weapon brain drops one round on the hover cue; then fly back home — Success on arrival there.</summary>
        private static NodeStatus HoverDrop(ref AirAttackParams p, ref AirAttackState ws, Entity self, EntityRepository world, Vector3 at, Vector3 target, double now)
        {
            if (ws.Stage != StageHover) return LocomotionMoveTo.Status(world, self);   // home: done on arrival
            if (!p.Target.IsNull && now >= ws.NextSteer)
            {
                if (!HoverOver(world, self, target, in p)) return NodeStatus.Failure;
                ws.NextSteer = now + SteerPeriod;
            }
            if (new Vector2(target.X - at.X, target.Y - at.Y).Length() > BombBallistics.HoverToleranceM) return NodeStatus.Running;
            var fire = new FireAtPointNodeParams { Point = target, Rounds = 1, Mount = ws.Mount, Started = ws.Fire.Started };
            var dropped = FireAtPointNodes.FireAtPoint(ref fire, self, world);
            ws.Fire = fire;
            if (dropped != NodeStatus.Success) return dropped;   // Running: waiting for the hover cue · Failure: nothing left to drop
            ws.Fired = 1;
            ws.Stage = StageOut;
            return Fly(world, self, ws.Home, in p, ws.Profile) ? NodeStatus.Running : NodeStatus.Failure;
        }

        private static bool HoverOver(EntityRepository world, Entity self, Vector3 target, in AirAttackParams p)
        {
            float height = p.AttackHeightAgl > 0f ? p.AttackHeightAgl : DefaultDropHeightAgl;
            return LocomotionMoveTo.Issue(world, self, new Vector3(target.X, target.Y, p.Point.Z + height), p.Speed, HoverArrivalM, source: MoveSource.Attack);
        }

        private static bool Fly(EntityRepository world, Entity self, Vector3 to, in AirAttackParams p, AirAttackProfile profile)
        {
            float height = p.AttackHeightAgl > 0f ? p.AttackHeightAgl : profile switch
            {
                AirAttackProfile.RocketRun => DefaultRocketRunHeightAgl,
                AirAttackProfile.RunningFire => DefaultRunningFireHeightAgl,
                AirAttackProfile.GunRun => DefaultGunRunHeightAgl,
                AirAttackProfile.DoorGunOrbit => DefaultOrbitHeightAgl,
                AirAttackProfile.HoverDrop => DefaultDropHeightAgl,
                _ => DefaultAttackHeightAgl,
            };
            return LocomotionMoveTo.Issue(world, self, new Vector3(to.X, to.Y, p.Point.Z + height), p.Speed, AirOrderNodes.DefaultArrivalRadius, source: MoveSource.Attack);
        }
    }

    /// <summary>
    /// ⭐ Slice 6e (§9i D-5; FPV Drone §2's "DroneTargetDecision") — what an FPV drone dives on: the live, identified, hostile contact in
    /// its OWN memory, inside the searched area, with the best freshness × its warhead's share of a kill (<see cref="AmmoChoice.ExpectedDamage"/>
    /// over the target's maximum health — the one damage rule, R-212 A1) × (a vehicle 1, anything else 0.3).
    /// </summary>
    public static class DroneTargets
    {
        /// <summary>The weight of a target that is not a vehicle (a person).</summary>
        public const float PersonWeight = 0.3f;

        /// <summary>The best target remembered within <paramref name="radius"/> (flat, m) of <paramref name="centre"/>; Null when none.</summary>
        public static unsafe Entity Pick(EntityRepository world, Entity drone, Vector3 centre, float radius)
        {
            if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Components.TargetMemory>()
                || !world.HasComponent<Fdp.Toolkit.Perception.Components.TargetMemory>(drone)) return Entity.Null;
            ref readonly var memory = ref world.GetComponentRO<Fdp.Toolkit.Perception.Components.TargetMemory>(drone);
            var best = Entity.Null;
            float bestScore = 0f;
            // ⭐ CE-10005 (AC-13, R-10022) — a FIGHTER (a fixed-wing with an air-to-air mount) on a CAP picks only AIRCRAFT, and one a wingman
            //   already fights only when nothing else is left (deconfliction through the arena's tick-start index of the fighters' targets)
            bool fighter = AirCombatNodes.IsFighter(world, drone);
            if (fighter) return PickBandit(world, drone, in memory, centre, radius);
            for (int i = 0; i < memory.Count; i++)
            {
                if (Fdp.Toolkit.Perception.Components.TargetMemory.IsAnonymous(in memory, i)) continue;   // heard, never identified: nothing to dive on
                var e = new Entity((ulong)memory.EntityIds[i]);
                if (!world.IsAlive(e) || !Injury.IsInTheFight(world, e) || !Hostile(world, drone, e)) continue;
                if (new Vector2(memory.PositionsX[i] - centre.X, memory.PositionsY[i] - centre.Y).Length() > radius) continue;
                float score = Score(world, drone, e, memory.Freshness[i]);
                if (score > bestScore) { bestScore = score; best = e; }
            }
            return best;
        }

        /// <summary>
        /// ⭐ CE-10005 (AC-13 / RV-11, R-10022) — a FIGHTER's pick: the hostile AIRCRAFT it remembers inside the area. Deconflicted with no
        /// cross-entity read in the phase: a bandit a wingman already fights (the arena's tick-start index) is taken only when nothing else is
        /// left, and when several are free the pilots of one force spread by RANK (the k-th pilot by entity index takes the k-th bandit by
        /// entity index) — so two pilots picking on the same tick do not pick the same bandit. Allocation-free (≤ 16 candidates).
        /// </summary>
        private static unsafe Entity PickBandit(EntityRepository world, Entity self, in Fdp.Toolkit.Perception.Components.TargetMemory memory, Vector3 centre, float radius)
        {
            var force = world.HasComponent<EntityInfo>(self) ? world.GetComponentRO<EntityInfo>(self).ForceId : ForceId.Neutral;
            Span<Entity> free = stackalloc Entity[16];
            int nFree = 0;
            var taken = Entity.Null;
            for (int i = 0; i < memory.Count; i++)
            {
                if (Fdp.Toolkit.Perception.Components.TargetMemory.IsAnonymous(in memory, i)) continue;
                var e = new Entity((ulong)memory.EntityIds[i]);
                if (!world.IsAlive(e) || !Injury.IsInTheFight(world, e) || !Hostile(world, self, e) || !Locomotions.IsAircraft(world, e)) continue;
                if (new Vector2(memory.PositionsX[i] - centre.X, memory.PositionsY[i] - centre.Y).Length() > radius) continue;
                if (Fdp.Toolkit.Motion.AirArenaSystem.TakenByAnother(world, self, force, e)) { if (taken.IsNull || e.Index < taken.Index) taken = e; continue; }
                if (nFree == free.Length) continue;
                int at = nFree++;
                while (at > 0 && free[at - 1].Index > e.Index) { free[at] = free[at - 1]; at--; }   // sorted by entity index
                free[at] = e;
            }
            if (nFree == 0) return taken;
            return free[Fdp.Toolkit.Motion.AirArenaSystem.RankOf(world, self, force) % nFree];
        }

        /// <summary>freshness × the warhead's share of a kill × the kind's weight.</summary>
        public static float Score(EntityRepository world, Entity drone, Entity target, float freshness)
        {
            var mount = CombatTkb.MountOf(world, drone, 0);
            float max = world.HasComponent<Health>(target) ? world.GetComponentRO<Health>(target).Max : 0f;
            if (max <= 0f) max = CombatTkb.PlatformOf(world, target)?.MaxHealth ?? 0f;
            float damage = mount != null ? AmmoChoice.ExpectedDamage(world, drone, target, mount, unchecked((long)mount.AmmoGuid)) : 0f;
            float share = max > 0f ? MathF.Min(1f, damage / max) : (damage > 0f ? 1f : 0f);
            float kind = Fdp.Toolkit.Spatial.Eqs.VehicleCover.IsVehicle(world, target) ? 1f : PersonWeight;
            return MathF.Max(freshness, 0f) * share * kind;
        }

        /// <summary>⭐ CE-3490 — <see cref="Hostile"/> for the breath attack's group line.</summary>
        internal static bool IsHostile(EntityRepository world, Entity a, Entity b) => Hostile(world, a, b);

        /// <summary>Another force, not a neutral one (perception remembers every other force, neutrals included).</summary>
        private static bool Hostile(EntityRepository world, Entity a, Entity b)
        {
            if (!world.IsComponentTypeRegistered<EntityInfo>() || !world.HasComponent<EntityInfo>(a) || !world.HasComponent<EntityInfo>(b)) return true;
            var mine = world.GetComponentRO<EntityInfo>(a).ForceId;
            var theirs = world.GetComponentRO<EntityInfo>(b).ForceId;
            return theirs != mine && theirs != ForceId.Neutral;
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
            var target = order.Target.Ref.Resolve(world);
            Vector3 point;
            if (!target.IsNull && world.IsAlive(target) && world.HasComponent<SimTransform>(target))
                point = world.GetComponentRO<SimTransform>(target).Position;
            else
            {
                var at = (System.Numerics.Vector3)order.Location;
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
            var target = order.Target.Ref.Resolve(world);
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
