using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-4624</c> MS-B1 (Minefields MF-5 as amended by MR-5, MR-6, MR-16; 📄 docs/designs/minefields/DESIGN_Minefields.md §11) — <c>MineStrike</c>,
    /// the battle drill of a unit that set off a mine: <b>halt</b> — a squad orders every live man to hold where he stands, a vehicle stops (its crew
    /// stays aboard) — the casualty is cared for WHERE HE LIES (no one carries him back across the field), and the unit <b>holds</b> for the engineers
    /// until its side has MARKED the field, or <c>Hold</c> has passed. ⭐ It declares <c>Halts | LeavesRoute</c> (R-4263): a dismounted squad on a
    /// Dash presses on (the gate refuses the drill), a mobility-killed vehicle yields (the gate's "cannot comply"). ⭐ The report up the chain IS the
    /// knowledge path: the trigger's <see cref="MineStruck"/> reaches the side's <c>PlaceKnowledgeSystem</c> (MF-13 ①, MF-15) — nothing more to send.
    /// ⚠ Not built (R-2125): backing out along per-mover breadcrumbs (MR-5 — no per-mover record exists yet; terrain MS-T2 owns it if it is engine
    /// state) — the unit holds instead; the followers of a struck vehicle reversing in its tracks.
    /// </summary>
    public static class MineStrikeBehavior
    {
        public const float DefaultHoldSeconds = 120f;

        [StructLayout(LayoutKind.Sequential)]
        public struct MineStrikeParams
        {
            public int FieldNetId;
            public Vector3 At;
            public SimDuration Hold;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MineStrikeState
        {
            public byte Ordered;
            public SimInstant Since;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MineStrikeBlackboard
        {
            public MineStrikeParams Params;
            public MineStrikeState State;
        }

        [BehaviorResolver(BehaviorNames.MineStrike)]
        public static void Resolve(in MineStrikeParamsJsonDto authored, ref MineStrikeBlackboard block, EntityRepository world, Entity self)
        {
            block.Params = new MineStrikeParams
            {
                FieldNetId = authored.FieldNetId, At = authored.At,
                Hold = (float)authored.Hold > 0f ? authored.Hold : (SimDuration)DefaultHoldSeconds,
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.MineStrike, Conduct = Fdp.Toolkit.Behavior.Conduct.Halts | Fdp.Toolkit.Behavior.Conduct.LeavesRoute,
            Params = typeof(MineStrikeParamsJsonDto), Categories = BehaviorCategory.AllMilitary | BehaviorCategory.Commander,
            Description = "A unit that set off a mine halts, cares for the casualty where he lies and holds for the engineers.")]
        [BTreeDefinition(BehaviorNames.MineStrike, Curated = true, Group = "Library/Reactions", ParamsType = typeof(MineStrikeBlackboard))]
        public static BTreeBuilder<MineStrikeBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<MineStrikeBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.State, Tick);

        public static NodeStatus Tick(ref MineStrikeParams p, ref MineStrikeState s, Entity self, EntityRepository world)
        {
            double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : world.SimulationTime;
            if (s.Ordered == 0)
            {
                // ⭐ HALT: every live man holds where he stands; the unit (a man, a vehicle) stops; nothing is carried back across the field
                if (world.HasComponent<UnitRoster>(self))
                {
                    var roster = world.GetComponentRO<UnitRoster>(self);
                    for (int i = 0; i < roster.Count; i++)
                    {
                        var m = roster.SubordinateEntities[i];
                        if (world.IsAlive(m) && Injury.IsInTheFight(world, m)) CommandOrders.Assign(world, self, m, BehaviorNames.PersonHold, "{}", newOrder: true);
                    }
                }
                LocomotionMoveTo.Stop(world, self);
                s.Ordered = 1;
                s.Since = (SimInstant)now;
                return NodeStatus.Running;
            }
            // ⭐ until the side has MARKED the field (the engineers' work, MS-B2), or the hold is over
            var field = p.FieldNetId != 0 ? new Fdp.Toolkit.Replication.EntityRef(p.FieldNetId).Resolve(world) : Entity.Null;
            if (!field.IsNull && world.HasComponent<EntityInfo>(self)
                && PlaceKnowledgeTable.Of(world).Marked((int)world.GetComponentRO<EntityInfo>(self).ForceId, field)) return NodeStatus.Success;
            return now - (double)s.Since >= (double)p.Hold ? NodeStatus.Success : NodeStatus.Running;
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4624</c> MS-B4 (Minefields KG-7; 📄 docs/designs/minefields/DESIGN_Minefields.md §11) — <c>MinefieldAhead</c>, the squad's reaction when
    /// its route reaches a minefield the planner could not go round: every live man HOLDS where he stands (short of it — the row fires within 30 m),
    /// the squad REQUESTS ENGINEERS to the field's near side (<see cref="Fdp.Toolkit.Squad.Command.RequestKind.Engineers"/>, up the chain as every
    /// request), and it holds until the route no longer shows a minefield ahead (a lane opened and known, the field marked and routed round) — the
    /// march then resumes — or <see cref="LimitSeconds"/>. ⭐ Conduct <c>Halts</c>: a Dash presses on.
    /// </summary>
    public static class MinefieldAheadBehavior
    {
        public const float LimitSeconds = 900f;

        [StructLayout(LayoutKind.Sequential)]
        public struct MinefieldAheadState
        {
            public byte Ordered;
            public SimInstant Since;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MinefieldAheadBlackboard
        {
            public Fdp.Toolkit.Behavior.SopAreaParams Area;
            public MinefieldAheadState State;
        }

        [BehaviorResolver(BehaviorNames.MinefieldAhead)]
        public static void Resolve(in NoParams authored, ref MinefieldAheadBlackboard block, EntityRepository world, Entity self)
        {
            block.Area = new Fdp.Toolkit.Behavior.SopAreaParams { WithinMetres = 30f };
            block.State = default;
        }

        [Behavior(BehaviorNames.MinefieldAhead, Conduct = Fdp.Toolkit.Behavior.Conduct.Halts, Params = typeof(NoParams), Categories = BehaviorCategory.Commander,
            Description = "A squad whose route reaches a minefield halts short of it and calls the engineers.")]
        [BTreeDefinition(BehaviorNames.MinefieldAhead, Curated = true, Group = "Library/Reactions", ParamsType = typeof(MinefieldAheadBlackboard))]
        public static BTreeBuilder<MinefieldAheadBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<MinefieldAheadBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Area, bb => bb.State, Tick);

        public static NodeStatus Tick(ref Fdp.Toolkit.Behavior.SopAreaParams area, ref MinefieldAheadState s, Entity self, EntityRepository world)
        {
            double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : world.SimulationTime;
            if (s.Ordered == 0)
            {
                if (world.HasComponent<UnitRoster>(self))
                {
                    var roster = world.GetComponentRO<UnitRoster>(self);
                    for (int i = 0; i < roster.Count; i++)
                    {
                        var m = roster.SubordinateEntities[i];
                        if (world.IsAlive(m) && Injury.IsInTheFight(world, m)) CommandOrders.Assign(world, self, m, BehaviorNames.PersonHold, "{}", newOrder: true);
                    }
                }
                var at = DangerCrossingPlay.NextArea(world, self, out var a) ? a.NearSideHandle
                       : world.HasComponent<SimTransform>(self) ? world.GetComponentRO<SimTransform>(self).Position : Vector3.Zero;
                SimInstant t = (SimInstant)Fdp.Toolkit.Squad.Command.CommandPictureOps.Now(world);
                Fdp.Toolkit.Squad.Command.ReportQueue.Enqueue(world, self, new Fdp.Toolkit.Squad.Command.CommandReport
                {
                    Kind = Fdp.Toolkit.Squad.Command.ReportKind.Request, Subtype = (byte)Fdp.Toolkit.Squad.Command.RequestKind.Engineers,
                    At = at, Radius = 30f, Count = 1, ObservedAt = t,
                }, t + CommandNodes.ReportDelaySeconds);
                s.Ordered = 1;
                s.Since = (SimInstant)now;
                return NodeStatus.Running;
            }
            if (now - (double)s.Since >= LimitSeconds) return NodeStatus.Success;
            return SquadSopConditions.MinefieldAhead(ref area, self, world) ? NodeStatus.Running : NodeStatus.Success;   // the way is open again
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4624</c> MS-B1 — the trigger's <see cref="MineStruck"/> → the drill, as a REACTION (<see cref="ReactionUrgency.Hit"/>): a struck
    /// VEHICLE reacts itself; a struck MAN's unit (his commander, alive) reacts — else the man himself. Before the brain tick, on the
    /// brain's host. The gate decides (a Dash refuses it, CD-3). ⭐ A second
    /// strike while the drill runs does not restart it — <c>SopActions.React</c> refuses a running identical reaction (📐 a dedupe here was measured
    /// redundant: its mutant stayed green).
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class MineStrikeReflexSystem : IEcsModuleSystem
    {
        private readonly bool _gateOnAuthority;

        public MineStrikeReflexSystem(bool gateOnAuthority = false) => _gateOnAuthority = gateOnAuthority;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.Bus.IsRegistered<MineStruck>()) return;
            var strikes = repo.Bus.Read<MineStruck>();
            if (strikes.Length == 0) return;
            for (int i = 0; i < strikes.Length; i++)
            {
                var unit = UnitOf(repo, strikes[i].By);
                if (unit.IsNull || !repo.HasComponent<BehaviorState>(unit)) continue;
                if (_gateOnAuthority && !repo.HasAuthority<BehaviorState>(unit)) continue;
                var dto = new MineStrikeParamsJsonDto
                {
                    FieldNetId = repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>()
                                 && repo.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(strikes[i].Field)
                        ? repo.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(strikes[i].Field).Value : 0,
                    At = new Vector3(strikes[i].X, strikes[i].Y, 0f),
                };
                SopActions.React(repo, unit, BehaviorNames.MineStrike, ReactionUrgency.Hit, in dto);
            }
        }

        /// <summary>Who runs the drill for <paramref name="by"/>: a vehicle itself; a man's live commander; else the man.</summary>
        public static Entity UnitOf(EntityRepository repo, Entity by)
        {
            if (by.IsNull || !repo.IsAlive(by)) return Entity.Null;
            if (Fdp.Toolkit.Spatial.Eqs.VehicleCover.IsVehicle(repo, by)) return by;
            if (repo.IsComponentTypeRegistered<UnitSubordinate>() && repo.HasComponent<UnitSubordinate>(by))
            {
                var c = repo.GetComponentRO<UnitSubordinate>(by).Commander;
                if (!c.IsNull && repo.IsAlive(c)) return c;
            }
            return by;
        }
    }
}
