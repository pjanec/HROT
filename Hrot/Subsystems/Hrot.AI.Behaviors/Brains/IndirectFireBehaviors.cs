using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
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

        /// <summary>The lay time the battery adds before the first round of a time-on-target mission (s, FM-4).</summary>
        public const float LaySeconds = 10f;

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
            if (order.Method == FireMissionMethod.Adjust)
                BehaviorLog.Warn(self, world, "fire mission: Adjust is not built yet (IF-3) — firing for effect");
            block.Mission = new BatteryFireParams
            {
                Point = IndirectFireNodes.PointOf(in order, world),
                Rounds = order.Rounds > 0 ? order.Rounds : IndirectFireNodes.DefaultRounds,
                Sheaf = (byte)order.Sheaf,
                Control = (byte)order.Control,
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
            float maxTof = 0f;
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
                maxTof = MathF.Max(maxTof, s.TimeOfFlight);
            }
            // time on target (FM-4): every first round lands at ImpactAt
            p.ImpactAt = (FireMissionControl)p.Control == FireMissionControl.TimeOnTarget
                ? ctx.Now + MathF.Max(maxTof + IndirectFireNodes.LaySeconds, p.ImpactInS)
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
                Rounds = refused || mount < 0 ? -1 : order.Rounds > 0 ? order.Rounds : IndirectFireNodes.DefaultRounds,
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
}
