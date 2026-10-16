using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Maneuvers;
using Fdp.Toolkit.Squad.Primitives;
using Hrot.AI.Behaviors.Logging;
using Hrot.AI.Behaviors.Orders;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-2176</c> IF-2 (FM-1…FM-8, R-2018; 📄 docs/designs/indirect-fire/DESIGN_Indirect_Fire.md §3.2, §3.4, §4) — the pieces a fire
    /// mission is made of: which of a gun's weapons fires indirect (its own or the tube it mans), its rounds left, where each gun of a
    /// battery aims (the sheaf), and the danger-close rule. Pure reads; the battery play and the one-gun strike both use them.
    /// </summary>
    public static class IndirectFireNodes
    {
        /// <summary>Friendly troops this close to the point refuse the mission unless it says danger close (m, FM-5).</summary>
        public const float DangerCloseMetres = 600f;

        /// <summary>⭐ <c>CE-1109</c> (FM-4) — the time-on-target margin past the slowest gun's lay + flight: the OnTarget settle and one
        /// think (s). ⛔ SUPERSEDED: a fixed <c>LaySeconds = 10</c> for every gun — an M109 (10 °/s) turning 180° needs 18 s.</summary>
        public const float LayMarginSeconds = 2f;

        /// <summary>Rounds per gun when the order names none.</summary>
        public const int DefaultRounds = 3;

        /// <summary>An open sheaf's default spacing per gun is twice the burst's injury radius, at least this (m).</summary>
        public const float MinGunSpacingMetres = 20f;

        /// <summary>
        /// The mount <paramref name="gun"/> fires indirect with: its own first mount that declares an indirect arc, else the first weapon it
        /// controls that does (the tube a gunner mans, 32 + k). −1 = none. <paramref name="weapon"/> is the entity that fires it,
        /// <paramref name="dto"/> its mount.
        /// </summary>
        public static int MountOf(EntityRepository world, Entity gun, out Entity weapon, out Fdp.Toolkit.Tkb.Domain.WeaponMountDto? dto)
        {
            weapon = gun; dto = null;
            for (int i = 0; i < 8; i++)
            {
                var m = CombatTkb.MountOf(world, gun, i);
                if (m == null) break;
                if (m.Arc == Fdp.Toolkit.Tkb.Domain.IndirectArc.Unset) continue;
                dto = m;
                weapon = WeaponMountQuery.MountEntity(world, gun, i);
                return i;
            }
            Span<Entity> controlled = stackalloc Entity[WeaponSystems.MaxControlled];
            int n = WeaponSystems.Controlled(world, gun, controlled);
            for (int k = 0; k < n; k++)
            {
                var m = CombatTkb.MountOf(world, controlled[k], 0);
                if (m == null || m.Arc == Fdp.Toolkit.Tkb.Domain.IndirectArc.Unset) continue;
                dto = m; weapon = controlled[k];
                return WeaponSystems.ControlledMountBase + k;
            }
            weapon = Entity.Null;
            return -1;
        }

        /// <summary>The entity that fires <paramref name="gun"/>'s mount <paramref name="mount"/>: a controlled weapon (32 + k) is itself,
        /// an own mount its mount entity.</summary>
        public static Entity WeaponAt(EntityRepository world, Entity gun, int mount)
            => WeaponSystems.IsControlledIndex(mount) ? WeaponSystems.ControlledAt(world, gun, mount) : WeaponMountQuery.MountEntity(world, gun, mount);

        /// <summary>The rounds <paramref name="weapon"/> holds (its <see cref="WeaponState.Ammo"/>), or −1.</summary>
        public static int AmmoOf(EntityRepository world, Entity weapon)
            => !weapon.IsNull && world.HasComponent<WeaponState>(weapon) ? world.GetComponentRO<WeaponState>(weapon).Ammo : -1;

        /// <summary>The fire solution for <paramref name="dto"/> fired by <paramref name="weapon"/> from where it stands at
        /// <paramref name="point"/> (the mount's own charges, the round it loads).</summary>
        public static Solution SolutionFor(EntityRepository world, Entity weapon, Fdp.Toolkit.Tkb.Domain.WeaponMountDto dto, Vector3 point)
        {
            if (!world.HasComponent<SimTransform>(weapon)) return new Solution { Refusal = FireRefusal.TooFar };
            var db = world.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>() ? world.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>() : null;
            long ammo = AmmoChoice.LoadedAmmo(world, weapon);
            if (ammo == 0) ammo = unchecked((long)dto.AmmoGuid);
            return FireSolution.For(Fdp.Toolkit.World.WorldQuery.Of(world), world.GetComponentRO<SimTransform>(weapon).Position, point,
                                    dto, FireSolution.ChargesOf(db, dto, ammo));
        }

        /// <summary>
        /// ⭐ <c>CE-1109</c> (FM-4) — the seconds <paramref name="weapon"/>'s turret needs to be laid on <paramref name="aim"/> by
        /// <paramref name="solution"/>'s arc, from where it points NOW: <see cref="TurretSlew.SecondsTo"/> on the azimuth / elevation the
        /// muscle will solve (<see cref="BallisticAim.Solve"/>, as <c>WeaponTaskDispatcherSystem</c> does). 0 for a mount with no turret
        /// part (it fires without laying, as <c>FireAtPointExecutor.Lay</c> does). A turret part whose pose this node does not hold reads
        /// the worst case — turned all the way round. A turret that can never get there (a rate of 0) costs 0: it will not fire anyway.
        /// </summary>
        public static float LaySecondsFor(EntityRepository world, Entity weapon, Fdp.Toolkit.Tkb.Domain.WeaponMountDto dto, Vector3 aim, in Solution solution)
        {
            if (dto.TurretIndex < 0 || !TurretParts.HostsTurrets(world) || !world.HasComponent<SimTransform>(weapon)) return 0f;
            var part = TurretParts.Find(world, weapon, dto.TurretIndex);
            var turret = CombatTkb.TurretOf(world, weapon, dto.TurretIndex);
            if (part.IsNull || turret == null) return 0f;
            long ammo = AmmoChoice.LoadedAmmo(world, weapon);
            if (ammo == 0) ammo = unchecked((long)dto.AmmoGuid);
            var (azimuth, elevation) = BallisticAim.Solve(world, weapon, turret, dto, aim,
                                                          solution.High ? ArcPreference.High : ArcPreference.Low, ammo);
            var pose = world.HasComponent<TurretPose>(part)
                ? world.GetComponentRO<TurretPose>(part)
                : new TurretPose { Azimuth = TurretSlew.Wrap(azimuth + MathF.PI), Elevation = elevation };
            float seconds = TurretSlew.SecondsTo(pose, azimuth, elevation, turret);
            return float.IsFinite(seconds) ? seconds : 0f;
        }

        /// <summary>FM-5 — a friend of <paramref name="unit"/>'s force within <see cref="DangerCloseMetres"/> of <paramref name="point"/>.</summary>
        public static bool DangerClose(EntityRepository world, Entity unit, Vector3 point)
            => HiddenTargetThrow.FriendlyInBurst(world, unit, point, DangerCloseMetres);

        /// <summary>
        /// FM-3 — gun <paramref name="i"/>'s aim point: Converged ⇒ the point; Parallel ⇒ its own lateral place in the battery (across the
        /// line of fire); Open ⇒ evenly over <paramref name="width"/> in the guns' lateral order. <paramref name="lateral"/> is each gun's
        /// lateral coordinate, <paramref name="rank"/> its place in that order (0…<paramref name="guns"/>−1).
        /// </summary>
        public static Vector3 AimPoint(FireMissionSheaf sheaf, Vector3 point, Vector2 across, float lateral, int rank, int guns, float width)
        {
            var side = new Vector3(across.X, across.Y, 0f);
            return sheaf switch
            {
                FireMissionSheaf.Parallel => point + side * lateral,
                FireMissionSheaf.Open when guns > 1 => point + side * (-0.5f * width + width * (rank + 0.5f) / guns),
                _ => point,
            };
        }

        /// <summary>The point of a Strike: its Target where it stands now, else its Location on the ground.</summary>
        public static Vector3 PointOf(in StrikeOrder order, EntityRepository world)
        {
            var target = order.Target.Resolve(world);
            if (!target.IsNull && world.IsAlive(target) && world.HasComponent<SimTransform>(target))
                return world.GetComponentRO<SimTransform>(target).Position;
            var at = OrderGeo.ToLocal(world, order.Location, 0f);
            return new Vector3(at.X, at.Y, Fdp.Toolkit.World.WorldQuery.Of(world)?.GroundHeightAt(at.X, at.Y) ?? 0f);
        }
    }

    /// <summary>⭐ <c>CE-2176</c> IF-2 — a battery's fire mission as resolved from a <see cref="StrikeOrder"/>, and its plan (per gun).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct BatteryFireParams
    {
        public Vector3 Point;
        public int Rounds;
        public byte Sheaf;
        public byte Control;
        public byte DangerClose;
        /// <summary>1 once the plan below is made (the first tick).</summary>
        public byte Planned;
        /// <summary>⭐ IF-3 (FM-6) — 1 = an ADJUSTING round: one gun (the one nearest the battery's centre), one round, on the point.</summary>
        public byte Adjust;
        public float SheafWidthM;
        public float ImpactInS;
        /// <summary>Time on target: when every first round lands (sim s); 0 = when ready.</summary>
        public double ImpactAt;
        public fixed float AimX[16];
        public fixed float AimY[16];
        public fixed float AimZ[16];
        public fixed float Tof[16];
        /// <summary>Gun i's rounds when the mission started.</summary>
        public fixed int StartAmmo[16];
        public fixed byte Mount[16];
        /// <summary>1 = gun i reaches its aim point.</summary>
        public fixed byte Able[16];
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2176</c> IF-2 (FM-1) — <c>ArtilleryUnitStrike</c>: the VIRTUAL BATTERY COMMANDER. A Strike to an artillery battery runs
    /// <see cref="BatteryFireMissionPlay"/> over its roster of guns — each gun that reaches gets its rounds, its aim point and its fire time.
    /// </summary>
    public static class ArtilleryUnitStrikeBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct ArtilleryUnitStrikeBlackboard
        {
            public BatteryFireParams Mission;
            public SquadPlayState State;
        }

        [BehaviorResolver(BehaviorNames.ArtilleryUnitStrike)]
        public static void Resolve(in StrikeOrder order, ref ArtilleryUnitStrikeBlackboard block, EntityRepository world, Entity self)
        {
            bool adjust = order.Method == FireMissionMethod.Adjust;
            block.Mission = new BatteryFireParams
            {
                Point = IndirectFireNodes.PointOf(in order, world),
                Rounds = adjust ? 1 : order.Rounds > 0 ? order.Rounds : IndirectFireNodes.DefaultRounds,
                Adjust = adjust ? (byte)1 : (byte)0,
                Sheaf = adjust ? (byte)FireMissionSheaf.Converged : (byte)order.Sheaf,
                Control = adjust ? (byte)FireMissionControl.WhenReady : (byte)order.Control,
                DangerClose = order.DangerClose ? (byte)1 : (byte)0,
                SheafWidthM = order.SheafWidthM,
                ImpactInS = order.ImpactInS,
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.ArtilleryUnitStrike, Params = typeof(StrikeOrder), Categories = BehaviorCategory.Commander)]
        [BTreeDefinition(BehaviorNames.ArtilleryUnitStrike, Curated = true, Group = "Library/Orders", ParamsType = typeof(ArtilleryUnitStrikeBlackboard))]
        public static BTreeBuilder<ArtilleryUnitStrikeBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<ArtilleryUnitStrikeBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Mission, bb => bb.State, Run);

        public static NodeStatus Run(ref BatteryFireParams p, ref SquadPlayState ws, Entity self, EntityRepository world)
            => SquadManeuverRunner.Tick<BatteryFireMissionPlay, BatteryFireParams>(ref p, ref ws, self, world);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2176</c> IF-2 — the fire mission as a play (FM-1…FM-8): Start plans it (who reaches, the sheaf, the fire times, danger
    /// close); every issue gives each gun the rounds it has LEFT (a resumed mission never fires twice); it ends when every able gun's
    /// rounds are gone (rounds complete).
    /// </summary>
    public unsafe struct BatteryFireMissionPlay : ISquadPlay<BatteryFireParams>
    {
        private static readonly SquadHsmShell s_shell = new(BatteryFireMissionManeuver.BuildTransitionTable(), BatteryFireMissionManeuver.PhaseAborted);

        /// <summary>A mission still running this long after its last planned impact has failed (s).</summary>
        public const double OverrunSeconds = 120.0;

        public SquadHsmShell Shell => s_shell;
        public ushort ManeuverKind => BatteryFireMissionManeuver.ManeuverKind;
        public ushort StartPhase => BatteryFireMissionManeuver.PhaseFiring;
        public ushort AbortPhase => BatteryFireMissionManeuver.PhaseAborted;
        public byte ElementCount => 1;

        public PlayStart Start(ref BatteryFireParams p, ref SquadPlayState ws, in SquadPlayContext ctx, Span<MemberPartitionInput> partition)
        {
            for (int i = 0; i < ctx.Roster.Count; i++) partition[i] = new MemberPartitionInput(1f, 0f);
            if (p.DangerClose == 0 && IndirectFireNodes.DangerClose(ctx.World, ctx.Commander, p.Point))
            {
                BehaviorLog.Warn(ctx.Commander, ctx.World, $"fire mission refused: danger close (friends within {IndirectFireNodes.DangerCloseMetres:0} m)");
                return PlayStart.Fail;
            }
            return Plan(ref p, in ctx) > 0 ? PlayStart.Go : PlayStart.Fail;
        }

        /// <summary>The plan: each gun's mount, its aim point (the sheaf), its solution and time of flight, its rounds at the start; then the
        /// impact time. Returns how many guns reach.</summary>
        public static int Plan(ref BatteryFireParams p, in SquadPlayContext ctx)
        {
            var world = ctx.World;
            int n = Math.Min(ctx.Roster.Count, 16);
            var dtos = new Fdp.Toolkit.Tkb.Domain.WeaponMountDto?[n];
            var pos = new Vector3[n];
            var weapons = new Entity[n];
            int reach = 0;
            for (int i = 0; i < n; i++)
            {
                p.Able[i] = 0;
                if (!ctx.Live(i, out var gun)) continue;
                int mount = IndirectFireNodes.MountOf(world, gun, out var weapon, out var dto);
                if (mount < 0 || dto == null) { BehaviorLog.Warn(ctx.Commander, world, $"fire mission: gun {i} has no indirect weapon"); continue; }
                var s = IndirectFireNodes.SolutionFor(world, weapon, dto, p.Point);
                if (!s.Ok) { BehaviorLog.Warn(ctx.Commander, world, $"fire mission: gun {i} cannot fire ({s.Refusal})"); continue; }
                p.Able[i] = 1; p.Mount[i] = (byte)mount;
                p.StartAmmo[i] = IndirectFireNodes.AmmoOf(world, weapon);
                dtos[i] = dto; weapons[i] = weapon; pos[i] = world.GetComponentRO<SimTransform>(weapon).Position;
                reach++;
            }
            if (reach == 0) { BehaviorLog.Warn(ctx.Commander, world, "fire mission refused: no gun reaches the point"); p.Planned = 1; return 0; }

            // ⭐ IF-3 (FM-6) — an adjusting round: only the able gun nearest the able guns' centre fires
            if (p.Adjust != 0 && reach > 1)
            {
                var mid = Vector3.Zero;
                for (int i = 0; i < n; i++) if (p.Able[i] != 0) mid += pos[i];
                mid /= reach;
                int pick = -1; float best = float.MaxValue;
                for (int i = 0; i < n; i++)
                    if (p.Able[i] != 0 && Vector3.DistanceSquared(pos[i], mid) < best) { best = Vector3.DistanceSquared(pos[i], mid); pick = i; }
                for (int i = 0; i < n; i++) if (i != pick) p.Able[i] = 0;
                reach = 1;
            }

            // the sheaf (FM-3): across the line of fire from the able guns' centre
            var centre = Vector3.Zero;
            for (int i = 0; i < n; i++) if (p.Able[i] != 0) centre += pos[i];
            centre /= reach;
            var line = new Vector2(p.Point.X - centre.X, p.Point.Y - centre.Y);
            line = line.LengthSquared() > 1e-6f ? Vector2.Normalize(line) : Vector2.UnitX;
            var across = new Vector2(-line.Y, line.X);
            var lateral = new float[n];
            for (int i = 0; i < n; i++) lateral[i] = Vector2.Dot(new Vector2(pos[i].X - centre.X, pos[i].Y - centre.Y), across);
            float width = p.SheafWidthM;
            if (width <= 0f)
            {
                float burst = 0f;
                for (int i = 0; i < n; i++)
                    if (p.Able[i] != 0 && CombatTkb.WarheadOf(world, dtos[i]) is { } wh) burst = MathF.Max(burst, wh.BlastInjuryRadiusM);
                width = reach * MathF.Max(2f * burst, IndirectFireNodes.MinGunSpacingMetres);
            }
            float lead = 0f;   // ⭐ CE-1109 — the slowest gun's lay + flight
            for (int i = 0; i < n; i++)
            {
                if (p.Able[i] == 0) continue;
                int rank = 0;
                for (int j = 0; j < n; j++) if (p.Able[j] != 0 && (lateral[j] < lateral[i] || (lateral[j] == lateral[i] && j < i))) rank++;
                var aim = IndirectFireNodes.AimPoint((FireMissionSheaf)p.Sheaf, p.Point, across, lateral[i], rank, reach, width);
                aim.Z = Fdp.Toolkit.World.WorldQuery.Of(world)?.GroundHeightAt(aim.X, aim.Y) ?? aim.Z;
                var s = IndirectFireNodes.SolutionFor(world, weapons[i], dtos[i]!, aim);
                if (!s.Ok) { aim = p.Point; s = IndirectFireNodes.SolutionFor(world, weapons[i], dtos[i]!, aim); }   // the sheaf's edge out of reach: the point
                p.AimX[i] = aim.X; p.AimY[i] = aim.Y; p.AimZ[i] = aim.Z; p.Tof[i] = s.TimeOfFlight;
                lead = MathF.Max(lead, IndirectFireNodes.LaySecondsFor(world, weapons[i], dtos[i]!, aim, in s) + s.TimeOfFlight);
            }
            // time on target (FM-4): every first round lands at ImpactAt
            p.ImpactAt = (FireMissionControl)p.Control == FireMissionControl.TimeOnTarget
                ? ctx.Now + MathF.Max(lead + IndirectFireNodes.LayMarginSeconds, p.ImpactInS)
                : 0d;
            p.Planned = 1;
            return reach;
        }

        /// <summary>Gun <paramref name="i"/>'s rounds fired so far (its rounds at the start minus now; a gun out of ammunition counts all).</summary>
        public static int Fired(ref BatteryFireParams p, EntityRepository world, Entity gun, int i)
        {
            int now = IndirectFireNodes.AmmoOf(world, IndirectFireNodes.WeaponAt(world, gun, p.Mount[i]));
            return now < 0 ? p.Rounds : Math.Max(p.StartAmmo[i] - now, 0);
        }

        /// <summary>Gun <paramref name="i"/> has nothing left to fire: its rounds are gone, or its ammunition.</summary>
        public static bool Done(ref BatteryFireParams p, EntityRepository world, Entity gun, int i)
            => p.Able[i] == 0 || Fired(ref p, world, gun, i) >= p.Rounds
               || IndirectFireNodes.AmmoOf(world, IndirectFireNodes.WeaponAt(world, gun, p.Mount[i])) <= 0;

        public void Update(ref BatteryFireParams p, ref SquadPlayState ws, in SquadPlayContext ctx) { }

        public NodeStatus Outcome(ref BatteryFireParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            bool any = false, all = true;
            for (int i = 0; i < Math.Min(ctx.Roster.Count, 16); i++)
            {
                if (p.Able[i] == 0) continue;
                if (!ctx.Live(i, out var gun)) continue;
                any = true;
                if (!Done(ref p, ctx.World, gun, i)) all = false;
            }
            if (!any) return NodeStatus.Failure;
            if (all)
            {
                BehaviorLog.Trace(ctx.Commander, ctx.World, "fire mission: rounds complete");
                return NodeStatus.Success;
            }
            double deadline = Math.Max(p.ImpactAt, ws.StartedAt) + OverrunSeconds;
            return ctx.Now > deadline ? NodeStatus.Failure : NodeStatus.Running;
        }

        public bool PhaseComplete(ref BatteryFireParams p, ref SquadPlayState ws, in SquadPlayContext ctx, out PhaseEventKind evt)
        {
            evt = default;
            return false;
        }

        public void BeginIssue(ref BatteryFireParams p, ref SquadPlayState ws, in SquadPlayContext ctx) { }

        public MemberOrder OrderFor(ref BatteryFireParams p, ref SquadPlayState ws, in SquadPlayContext ctx, int member, byte element, Vector3 at)
        {
            ctx.Live(member, out var gun);
            if (member >= 16 || Done(ref p, ctx.World, gun, member))
                return new MemberOrder { Kind = MemberOrderKind.Hold, RoleId = BatteryFireMissionManeuver.RoleStandby };
            int left = p.Rounds - Fired(ref p, ctx.World, gun, member);
            return new MemberOrder
            {
                Kind = MemberOrderKind.FireAtPoint,
                Point = new Vector3(p.AimX[member], p.AimY[member], p.AimZ[member]),
                Rounds = left,
                Mount = p.Mount[member],
                FireAt = p.ImpactAt > 0d ? p.ImpactAt - p.Tof[member] : 0d,
                RoleId = BatteryFireMissionManeuver.RoleFiring,
            };
        }

        public void Release(ref SquadPlayState ws, EntityRepository world) { }
    }

    /// <summary>
    /// ⭐ <c>CE-2176</c> IF-2 (FM-1) — <c>SelfPropelledArtilleryStrike</c>: Strike to ONE self-propelled gun — its rounds on the point from
    /// its indirect mount (the library <c>FireAtPoint</c> node), danger close refused (FM-5). A battery's sheaf and time on target are the
    /// battery's (<see cref="ArtilleryUnitStrikeBehavior"/>).
    /// </summary>
    public static class SelfPropelledArtilleryStrikeBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct SelfPropelledArtilleryStrikeBlackboard
        {
            public FireAtPointNodeParams Fire;
        }

        [BehaviorResolver(BehaviorNames.SelfPropelledArtilleryStrike)]
        public static void Resolve(in StrikeOrder order, ref SelfPropelledArtilleryStrikeBlackboard block, EntityRepository world, Entity self)
        {
            var point = IndirectFireNodes.PointOf(in order, world);
            int mount = IndirectFireNodes.MountOf(world, self, out _, out _);
            bool refused = !order.DangerClose && IndirectFireNodes.DangerClose(world, self, point);
            if (refused) BehaviorLog.Warn(self, world, $"fire mission refused: danger close (friends within {IndirectFireNodes.DangerCloseMetres:0} m)");
            block.Fire = new FireAtPointNodeParams
            {
                Point = point,
                Rounds = refused || mount < 0 ? -1 : order.Method == FireMissionMethod.Adjust ? 1 : order.Rounds > 0 ? order.Rounds : IndirectFireNodes.DefaultRounds,
                Mount = (byte)Math.Max(mount, 0),
                FireAt = order.ImpactInS > 0f ? Fdp.Toolkit.Combat.HitModel.Now(world) + order.ImpactInS : 0d,
            };
        }

        [Behavior(BehaviorNames.SelfPropelledArtilleryStrike, Params = typeof(StrikeOrder), Categories = BehaviorCategory.MilitaryApc)]
        [BTreeDefinition(BehaviorNames.SelfPropelledArtilleryStrike, Curated = true, Group = "Library/Orders", ParamsType = typeof(SelfPropelledArtilleryStrikeBlackboard))]
        public static BTreeBuilder<SelfPropelledArtilleryStrikeBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<SelfPropelledArtilleryStrikeBlackboard, BTreeContext>()
                .Action(bb => bb.Fire, Fire);

        /// <summary>A refused mission (danger close, no indirect weapon) fails at once; otherwise the library node.</summary>
        [Fbt.Kernel.SharedAiAction]
        public static NodeStatus Fire(ref FireAtPointNodeParams p, Entity self, EntityRepository world)
            => p.Rounds < 0 ? NodeStatus.Failure : FireAtPointNodes.FireAtPoint(ref p, self, world);
    }

    /// <summary>⭐ <c>CE-2176</c> IF-3 — an observer's fire mission: the target, the battery, the point last called, the bracket.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ObserveFireParams
    {
        /// <summary>What the observer looks at (where the target really is).</summary>
        public Vector3 Target;
        /// <summary>The point last called to the battery.</summary>
        public Vector3 Call;
        public Entity Battery;
        public int Rounds;
        public float SheafWidthM;
        public byte Sheaf;
        public byte Control;
        public byte DangerClose;
        /// <summary>0 = not called yet · 1 = adjusting · 2 = fire for effect called.</summary>
        public byte Phase;
        public byte Locating;
        /// <summary>The side of the target the last round burst on, along the observer–target line: +1 over, −1 short, 0 none yet.</summary>
        public sbyte LastSide;
        /// <summary>1 once a round has burst on each side (the bracket is split from then on).</summary>
        public byte Splitting;
        public byte Pad;
        /// <summary>The range correction in force (m); 0 = no bracket yet.</summary>
        public float Step;
        /// <summary>Calls sent (the first one included).</summary>
        public int Calls;
        public double CalledAt;
        /// <summary>The last burst looked at (<see cref="RecentBurst.Serial"/>).</summary>
        public long SeenSerial;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2176</c> IF-3 (IF-E, FM-6, FM-7; 📄 docs/designs/indirect-fire/DESIGN_Indirect_Fire.md §3.5, §3.8) — <c>PersonObserveFire</c>:
    /// the FORWARD OBSERVER. He calls an adjusting round on the target (with the error of how he located it), watches where each of the
    /// battery's rounds bursts — only a burst his eye can see counts — and corrects: the lateral error at once, the range by the bracket
    /// (800 → 400 → 200 → 100 → 50 m). Within 50 m, or with the bracket split to 50, he calls fire for effect. Every call is a Strike to
    /// the battery, so the observer, not the battery, owns the adjust: kill him and no correction comes.
    /// </summary>
    public static class PersonObserveFireBehavior
    {
        /// <summary>A burst this close to the target, in range and laterally, is good enough to fire for effect (m, FM-6).</summary>
        public const float EffectWithinMetres = 50f;

        /// <summary>No seen burst this long after a call ⇒ "observer lost" (s).</summary>
        public const double BurstWaitSeconds = 180.0;

        /// <summary>Farther than this the observer cannot judge a burst (m).</summary>
        public const float MaxObservationMetres = 6000f;

        /// <summary>The error of a judged burst grows with distance (m per m: 1 % — binoculars and a reticle).</summary>
        public const float ObservationErrorPerMetre = 0.01f;

        /// <summary>The first call's error by how the target was located (m, IF-F).</summary>
        public static float LocationErrorMetres(FireMissionLocating how) => how == FireMissionLocating.Laser ? 10f : 150f;

        /// <summary>The bracket's first range step: the smallest of 800 / 400 / 200 / 100 / 50 that covers the error (at most 800).</summary>
        public static float FirstStep(float rangeError)
        {
            float e = MathF.Abs(rangeError);
            foreach (float step in s_ladder) if (step >= e) return step;
            return s_ladder[^1];
        }

        private static readonly float[] s_ladder = { 50f, 100f, 200f, 400f, 800f };

        [StructLayout(LayoutKind.Sequential)]
        public struct PersonObserveFireBlackboard
        {
            public ObserveFireParams Observe;
        }

        [BehaviorResolver(BehaviorNames.PersonObserveFire)]
        public static void Resolve(in ObserveFireOrder order, ref PersonObserveFireBlackboard block, EntityRepository world, Entity self)
        {
            var strike = new StrikeOrder { Location = order.Location, Target = order.Target };
            block.Observe = new ObserveFireParams
            {
                Target = IndirectFireNodes.PointOf(in strike, world),
                Battery = order.Battery.Resolve(world),
                Rounds = order.Rounds,
                Sheaf = (byte)order.Sheaf,
                SheafWidthM = order.SheafWidthM,
                Control = (byte)order.Control,
                DangerClose = order.DangerClose ? (byte)1 : (byte)0,
                Locating = (byte)order.Locating,
            };
        }

        [Behavior(BehaviorNames.PersonObserveFire, Params = typeof(ObserveFireOrder), Categories = BehaviorCategory.Infantry)]
        [BTreeDefinition(BehaviorNames.PersonObserveFire, Curated = true, Group = "Library/Orders", ParamsType = typeof(PersonObserveFireBlackboard))]
        public static BTreeBuilder<PersonObserveFireBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<PersonObserveFireBlackboard, BTreeContext>()
                .Action(bb => bb.Observe, Observe);

        /// <summary>One look: call, wait for the burst, correct — see the type.</summary>
        [Fbt.Kernel.SharedAiAction]
        public static NodeStatus Observe(ref ObserveFireParams p, Entity self, EntityRepository world)
        {
            if (p.Battery.IsNull || !world.IsAlive(p.Battery) || !world.HasComponent<SimTransform>(self))
            {
                BehaviorLog.Warn(self, world, "observe fire: no battery to call");
                return NodeStatus.Failure;
            }
            double now = Fdp.Toolkit.Combat.HitModel.Now(world);
            var eye = world.GetComponentRO<SimTransform>(self).Position;
            if (p.Phase == 0)
            {
                if (!Sees(world, eye, p.Target))
                {
                    BehaviorLog.Warn(self, world, "observe fire: I cannot see the target");
                    return NodeStatus.Failure;
                }
                var err = Fdp.Toolkit.Perception.Sensors.AcousticPerception.Error(self.PackedValue, p.Battery.PackedValue, 0u,
                                                                                  LocationErrorMetres((FireMissionLocating)p.Locating));
                p.Call = Ground(world, p.Target + new Vector3(err.X, err.Y, 0f));
                p.Phase = 1;
                Call(ref p, world, self, now, FireMissionMethod.Adjust);
                BehaviorLog.Trace(self, world, "observe fire: adjust fire, over");
                return NodeStatus.Running;
            }

            Span<RecentBurst> bursts = stackalloc RecentBurst[RecentDetonations.Capacity];
            int n = RecentDetonations.Since(world, p.CalledAt, bursts);
            for (int i = 0; i < n; i++)
            {
                ref readonly var b = ref bursts[i];
                if (b.Serial <= p.SeenSerial) continue;
                p.SeenSerial = b.Serial;
                if (!FromBattery(world, p.Battery, b.Shooter) || !Sees(world, eye, b.At)) continue;
                if (p.Phase == 2)
                {
                    BehaviorLog.Trace(self, world, "observe fire: end of mission");
                    return NodeStatus.Success;
                }
                var judged = b.At + Judged(self, b.Serial, Vector2.Distance(new Vector2(eye.X, eye.Y), new Vector2(b.At.X, b.At.Y)));
                bool effect = Correct(ref p, eye, judged);
                p.Call = Ground(world, p.Call);
                if (effect)
                {
                    p.Phase = 2;
                    Call(ref p, world, self, now, FireMissionMethod.FireForEffect);
                    BehaviorLog.Trace(self, world, "observe fire: fire for effect, over");
                }
                else
                {
                    Call(ref p, world, self, now, FireMissionMethod.Adjust);
                    BehaviorLog.Trace(self, world, $"observe fire: correction {p.Step:0} m, over");
                }
                return NodeStatus.Running;
            }
            if (now - p.CalledAt > BurstWaitSeconds)
            {
                if (p.Phase == 2) { BehaviorLog.Trace(self, world, "observe fire: end of mission (not observed)"); return NodeStatus.Success; }
                BehaviorLog.Warn(self, world, "observe fire: lost — no round seen");
                return NodeStatus.Failure;
            }
            return NodeStatus.Running;
        }

        /// <summary>
        /// FM-6 — the next call from a judged burst: the lateral error is taken out at once; the range moves by the bracket step toward the
        /// target (doubling back and halving once a round has burst on each side). True = fire for effect (the burst within
        /// <see cref="EffectWithinMetres"/>, or the bracket split to 50 m — the call moved by that last 50 m).
        /// </summary>
        public static bool Correct(ref ObserveFireParams p, Vector3 observer, Vector3 burst)
        {
            var line = new Vector2(p.Target.X - observer.X, p.Target.Y - observer.Y);
            line = line.LengthSquared() > 1e-6f ? Vector2.Normalize(line) : Vector2.UnitX;
            var right = new Vector2(line.Y, -line.X);
            var off = new Vector2(burst.X - p.Target.X, burst.Y - p.Target.Y);
            float range = Vector2.Dot(off, line), lateral = Vector2.Dot(off, right);
            if (MathF.Abs(range) <= EffectWithinMetres && MathF.Abs(lateral) <= EffectWithinMetres) return true;

            var move = -right * lateral;
            sbyte side = range > EffectWithinMetres ? (sbyte)1 : range < -EffectWithinMetres ? (sbyte)-1 : (sbyte)0;
            bool effect = false;
            if (side != 0)
            {
                if (p.Step <= 0f) p.Step = FirstStep(range);
                else if (p.Splitting != 0 || (p.LastSide != 0 && side != p.LastSide)) { p.Splitting = 1; p.Step = MathF.Max(p.Step * 0.5f, s_ladder[0]); }
                move += line * (-side * p.Step);
                p.LastSide = side;
                effect = p.Splitting != 0 && p.Step <= s_ladder[0];
            }
            p.Call += new Vector3(move.X, move.Y, 0f);
            return effect;
        }

        /// <summary>The observer's eye reaches the burst (its flash and smoke, 2 m up) — no terrain resident ⇒ sight is unknown ⇒ seen.</summary>
        public static bool Sees(EntityRepository world, Vector3 eye, Vector3 at)
        {
            if (Vector2.Distance(new Vector2(eye.X, eye.Y), new Vector2(at.X, at.Y)) > MaxObservationMetres) return false;
            var terrain = Fdp.Toolkit.World.WorldQuery.Of(world);
            return terrain == null || !terrain.SightBlocked(eye + new Vector3(0f, 0f, 1.7f), at + new Vector3(0f, 0f, 2f));
        }

        /// <summary>A round of <paramref name="battery"/>: fired by it, by one of its guns, or by a tube one of its guns mans.</summary>
        public static bool FromBattery(EntityRepository world, Entity battery, Entity shooter)
        {
            if (shooter.IsNull) return false;
            if (shooter.Equals(battery)) return true;
            var controller = WeaponSystems.ControllerOf(world, shooter);
            if (!world.HasComponent<UnitRoster>(battery)) return false;
            ref readonly var roster = ref world.GetComponentRO<UnitRoster>(battery);
            for (int i = 0; i < roster.Count; i++)
            {
                var gun = roster.SubordinateEntities[i];
                if (gun.Equals(shooter) || (!controller.IsNull && gun.Equals(controller))) return true;
            }
            return false;
        }

        // The judged offset of burst `serial` at `distance`: deterministic, inside 1 % of the distance.
        private static Vector3 Judged(Entity observer, long serial, float distance)
        {
            var e = Fdp.Toolkit.Perception.Sensors.AcousticPerception.Error(observer.PackedValue, unchecked((ulong)serial), 1u, distance * ObservationErrorPerMetre);
            return new Vector3(e.X, e.Y, 0f);
        }

        private static Vector3 Ground(EntityRepository world, Vector3 at)
            => new(at.X, at.Y, Fdp.Toolkit.World.WorldQuery.Of(world)?.GroundHeightAt(at.X, at.Y) ?? at.Z);

        // A Strike to the battery: an ORDER it resolves itself (a battery, or one self-propelled gun), from a superior's rank.
        private static void Call(ref ObserveFireParams p, EntityRepository world, Entity self, double now, FireMissionMethod method)
        {
            var order = new StrikeOrder
            {
                Location = OrderGeo.ToGeo(world, p.Call),
                Method = method,
                Rounds = method == FireMissionMethod.Adjust ? 1 : p.Rounds,
                Sheaf = (FireMissionSheaf)p.Sheaf,
                SheafWidthM = p.SheafWidthM,
                Control = (FireMissionControl)p.Control,
                Observer = Fdp.Toolkit.Replication.EntityRef.Of(world, self),
                DangerClose = p.DangerClose != 0,
            };
            world.Bus.PublishManaged(new Fdp.Toolkit.Behavior.Events.AssignTacticalIntentEvent
            {
                Entity = p.Battery,
                IntentId = Hrot.Map.Definitions.Behavior.Orders.OrderAttribute.IntentFor(nameof(OrderKind.Strike)),
                JsonParams = System.Text.Json.JsonSerializer.Serialize(order, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed),
                Origin = BehaviorOrigin.Superior,
            });
            p.Calls++;
            p.CalledAt = now;
        }
    }
}
