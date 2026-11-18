using System;
using System.Collections.Generic;
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
    /// ⭐⭐ MR-5, BUILT (R-2125's interim ended — terrain's <see cref="MineBreadcrumbs"/>, MS-T2): every live man BACKS OUT along his own track to the
    /// first point outside the field (<see cref="BackOutBehavior"/>) and holds there; a struck VEHICLE (or a lone man) backs out itself (<see cref="TrailWalk"/>,
    /// reverse for a vehicle). No track ⇒ he holds where he stands. ⚠ Still not built: the followers of a struck vehicle reversing in its tracks.
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
            /// <summary>⭐ MR-5 — a unit with no roster (a vehicle, a lone man) backs out ITSELF along its own track.</summary>
            public TrailWalk Walk;
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
                FieldNetId = authored.Field.Ref.NetworkId, At = authored.At,
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
            var field = p.FieldNetId != 0 ? new Fdp.Toolkit.Replication.EntityRef(p.FieldNetId).Resolve(world) : Entity.Null;
            if (s.Ordered == 0)
            {
                // ⭐ HALT, then BACK OUT (MR-5): every live man walks his own track out of the field and holds there (no track ⇒ he holds where
                //   he stands); the casualty is cared for where he lies — nothing is carried back across the field
                if (world.HasComponent<UnitRoster>(self))
                {
                    var roster = world.GetComponentRO<UnitRoster>(self);
                    string json = BackOutBehavior.Json(p.FieldNetId);
                    for (int i = 0; i < roster.Count; i++)
                    {
                        var m = roster.SubordinateEntities[i];
                        if (world.IsAlive(m) && Injury.IsInTheFight(world, m)) CommandOrders.Assign(world, self, m, BehaviorNames.BackOut, json, newOrder: true);
                    }
                    LocomotionMoveTo.Stop(world, self);
                }
                else if (!TrailWalk.Start(ref s.Walk, world, self, field)) LocomotionMoveTo.Stop(world, self);   // a vehicle, a lone man: itself
                s.Ordered = 1;
                s.Since = (SimInstant)now;
            }
            if (s.Walk.Started != 0) TrailWalk.Step(ref s.Walk, world, self);
            // ⭐ until the side has MARKED the field (the engineers' work, MS-B2), or the hold is over
            if (!field.IsNull && world.HasComponent<EntityInfo>(self)
                && PlaceKnowledgeTable.Of(world).Marked((int)world.GetComponentRO<EntityInfo>(self).ForceId, field)) return NodeStatus.Success;
            return now - (double)s.Since >= (double)p.Hold ? NodeStatus.Success : NodeStatus.Running;
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4624</c> MS-B1 back-out (Minefields MR-5; R-2125's interim ended by terrain's <see cref="MineBreadcrumbs"/>, MS-T2) — a mover walks its OWN
    /// breadcrumbs back, newest first, to the first one OUTSIDE the struck field: safe by construction (what was under it has gone off or was never there).
    /// Copied once at the start — the live trail keeps growing while it walks. Each leg is a short (≤ ~2 m) move, so the side's high price on a known
    /// field cannot route it elsewhere. A vehicle backs (reverse allowed) and keeps its facing; a man walks. 0 B (fixed buffers).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct TrailWalk
    {
        public const float ManSpeed = 1.2f, VehicleSpeed = 2f, ArrivalMetres = 0.75f;
        public fixed float X[MineBreadcrumbs.Capacity];
        public fixed float Y[MineBreadcrumbs.Capacity];
        public byte Count, Next, Moving, Started;

        [ThreadStatic] private static List<Vector2>? _outline;

        /// <summary>Copies <paramref name="self"/>'s own trail, cut after its first point outside <paramref name="field"/> (the whole trail when the field
        /// is unknown). False when he has no trail (he then holds where he is).</summary>
        public static bool Start(ref TrailWalk t, EntityRepository world, Entity self, Entity field)
        {
            t = default;
            t.Started = 1;
            if (!world.IsComponentTypeRegistered<MineBreadcrumbs>() || !world.HasComponent<MineBreadcrumbs>(self)) return false;
            var crumbs = world.GetComponentRO<MineBreadcrumbs>(self);
            if (crumbs.Count == 0) return false;
            var outline = _outline ??= new List<Vector2>(16);
            bool known = !field.IsNull && MinefieldKnowledgeOps.TryOutline(world, field, outline, out _) && outline.Count >= 3;
            int n = crumbs.Count;
            for (int i = 0; i < n; i++)
            {
                var p = crumbs.Back(i);
                t.X[i] = p.X; t.Y[i] = p.Y;
                t.Count = (byte)(i + 1);
                if (known && i > 0 && !MinePattern.Inside(outline, p)) break;   // the first point outside: there it stops
            }
            return true;
        }

        /// <summary>One tick: the next leg (Running) until the last point is reached (Success; he is stopped there).</summary>
        public static NodeStatus Step(ref TrailWalk t, EntityRepository world, Entity self)
        {
            if (t.Next >= t.Count) { if (t.Moving != 0) { LocomotionMoveTo.Stop(world, self); t.Moving = 0; } return NodeStatus.Success; }
            if (t.Moving != 0)
            {
                var status = LocomotionMoveTo.Status(world, self);
                if (status == NodeStatus.Running) return NodeStatus.Running;
                t.Next++;                                   // arrived — or this leg failed: the next point is on the same safe trail
                t.Moving = 0;
                if (t.Next >= t.Count) { LocomotionMoveTo.Stop(world, self); return NodeStatus.Success; }
            }
            bool vehicle = Fdp.Toolkit.Spatial.Eqs.VehicleCover.IsVehicle(world, self);
            var to = new Vector3(t.X[t.Next], t.Y[t.Next], 0f);
            if (!LocomotionMoveTo.Issue(world, self, to, vehicle ? VehicleSpeed : ManSpeed, ArrivalMetres, MoveGait.Upright,
                    reverseAllowed: vehicle, onSurface: true)) return NodeStatus.Failure;
            t.Moving = 1;
            return NodeStatus.Running;
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4624</c> MS-B1 back-out (MR-5) — <c>BackOut</c>, a man's order from his unit's <see cref="MineStrikeBehavior"/>: he walks his own
    /// breadcrumbs back out of the struck field (<see cref="TrailWalk"/>), then HOLDS there for the engineers (as <c>PersonHold</c>). No trail ⇒ he holds where
    /// he stands. Never ends by itself — the drill's end (the field marked, the hold over) gives the next order.
    /// </summary>
    public static class BackOutBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct BackOutParams { public int FieldNetId; }

        [StructLayout(LayoutKind.Sequential)]
        public struct BackOutBlackboard
        {
            public BackOutParams Params;
            public TrailWalk Walk;
        }

        [BehaviorResolver(BehaviorNames.BackOut)]
        public static void Resolve(in BackOutParamsJsonDto authored, ref BackOutBlackboard block, EntityRepository world, Entity self)
        {
            block.Params = new BackOutParams { FieldNetId = authored.Field.Ref.NetworkId };
            block.Walk = default;
        }

        [Behavior(BehaviorNames.BackOut, Conduct = Fdp.Toolkit.Behavior.Conduct.LeavesRoute, Params = typeof(BackOutParamsJsonDto),
            Categories = BehaviorCategory.AllMilitary, Description = "Walks back out of a minefield along its own track, then holds.")]
        [BTreeDefinition(BehaviorNames.BackOut, Curated = true, Group = "Library/Reactions", ParamsType = typeof(BackOutBlackboard))]
        public static BTreeBuilder<BackOutBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<BackOutBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.Walk, Tick);

        public static NodeStatus Tick(ref BackOutParams p, ref TrailWalk w, Entity self, EntityRepository world)
        {
            if (w.Started == 0)
            {
                var field = p.FieldNetId != 0 ? new Fdp.Toolkit.Replication.EntityRef(p.FieldNetId).Resolve(world) : Entity.Null;
                if (!TrailWalk.Start(ref w, world, self, field)) LocomotionMoveTo.Stop(world, self);
            }
            TrailWalk.Step(ref w, world, self);
            return NodeStatus.Running;   // then holds where the trail left him
        }

        /// <summary>The order's payload for <paramref name="fieldNetId"/> (allocates: once per strike, never per frame).</summary>
        public static string Json(int fieldNetId) => "{\"Field\":" + fieldNetId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";

        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.BackOutBehavior.Tick")]
        public static void Deactivate_Tick(ref BackOutParams p, ref TrailWalk w, Entity self, EntityRepository world)
        {
            if (w.Moving != 0) LocomotionMoveTo.Stop(world, self);
            w = default;
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
            var strikes = repo.ReadEvents<MineStruck>();
            if (strikes.Length == 0) return;
            for (int i = 0; i < strikes.Length; i++)
            {
                var unit = UnitOf(repo, strikes[i].By);
                if (unit.IsNull || !repo.HasComponent<BehaviorState>(unit)) continue;
                if (_gateOnAuthority && !repo.HasAuthority<BehaviorState>(unit)) continue;
                var dto = new MineStrikeParamsJsonDto
                {
                    Field = new Fdp.Toolkit.Replication.EntityLink(new Fdp.Toolkit.Replication.EntityRef(repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>()
                                 && repo.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(strikes[i].Field)
                        ? repo.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(strikes[i].Field).Value : 0)),
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
