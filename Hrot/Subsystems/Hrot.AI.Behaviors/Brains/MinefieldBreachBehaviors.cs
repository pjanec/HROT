using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-4624</c> MS-B3 (MR-11; R-2126) — the mine clearer's EQUIPMENT SWITCH, the one seam the brain uses: terrain's
    /// <see cref="Fdp.Toolkit.Terrain.MineClearing"/> marker on the clearer ITSELF (component 325, recorded; a per-entity write) — <c>Active</c>, and
    /// on switching on the <c>Tool</c> and the swath's <c>WidthM</c>. The engine's swath clear (the Muscle) reads it and clears the mines under it,
    /// and raises <c>LaneCleared</c>. ⛔ The brain never writes the field. A clearer without the marker (not a TKB 116) answers false and clears nothing.
    /// ⛔ HISTORY: a stand-in that returned false until terrain named the switch (`NOTE_Terrain_To_Behaviors_Minefield_Switch.md`).
    /// </summary>
    public static class ClearerEquipment
    {
        /// <summary>Switch <paramref name="clearer"/>'s tool <paramref name="on"/> / off; true when the clearer carries the marker.</summary>
        public static bool SetActive(EntityRepository world, Entity clearer, bool on,
                                     Fdp.Toolkit.Terrain.MineClearingTool tool = Fdp.Toolkit.Terrain.MineClearingTool.Flail, float widthM = ClearLaneBehavior.DefaultWidth)
        {
            if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Terrain.MineClearing>() || !world.HasComponent<Fdp.Toolkit.Terrain.MineClearing>(clearer)) return false;
            ref var m = ref world.GetComponentRW<Fdp.Toolkit.Terrain.MineClearing>(clearer);
            m.Active = on ? (byte)1 : (byte)0;
            if (on) { m.Tool = tool; m.WidthM = widthM; }
            return true;
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4624</c> MS-B3 (Minefields MF-9, MR-11, MR-15; 📄 docs/designs/minefields/DESIGN_Minefields.md §11) — <c>ClearLane</c>, run by the mine
    /// clearer: drive to the lane's start (its own move channel), switch the tool ON (<see cref="ClearerEquipment"/>), drive the lane to its end at the
    /// TOOL's pace (a flail ~0.5 m/s), switch it OFF, Success. ⛔ It never writes the field: the swath's mines are cleared by the engine.
    /// </summary>
    public static class ClearLaneBehavior
    {
        public const float DefaultWidth = 4.5f, DefaultPace = 0.5f, DefaultSpeed = 5f, StartRadius = 3f, EndRadius = 2f;
        public const byte ToStart = 0, Clearing = 1;

        [StructLayout(LayoutKind.Sequential)]
        public struct ClearLaneParams
        {
            public int FieldNetId;
            public Vector3 Start, End;
            public float Width, Pace, Speed;
            /// <summary>Terrain's <c>MineClearingTool</c>: 1 plough, 2 flail.</summary>
            public byte Tool;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ClearLaneState
        {
            public byte Phase, Issued;
            /// <summary>1 while the brain has asked the tool ON.</summary>
            public byte ToolOn;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ClearLaneBlackboard
        {
            public ClearLaneParams Params;
            public ClearLaneState State;
        }

        [BehaviorResolver(BehaviorNames.ClearLane)]
        public static void Resolve(in ClearLaneParamsJsonDto a, ref ClearLaneBlackboard block, EntityRepository world, Entity self)
        {
            block.Params = new ClearLaneParams
            {
                FieldNetId = a.Field.Ref.NetworkId, Start = a.Start, End = a.End,
                Width = a.Width > 0f ? a.Width : DefaultWidth, Pace = a.Pace > 0f ? a.Pace : DefaultPace, Speed = a.Speed > 0f ? a.Speed : DefaultSpeed,
                Tool = a.Tool == (byte)Fdp.Toolkit.Terrain.MineClearingTool.Plough ? a.Tool : (byte)Fdp.Toolkit.Terrain.MineClearingTool.Flail,
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.ClearLane, Params = typeof(ClearLaneParamsJsonDto), Categories = BehaviorCategory.MilitaryApc,
            Description = "The mine clearer drives a lane through a minefield with its plough / flail on.")]
        [BTreeDefinition(BehaviorNames.ClearLane, Curated = true, Group = "Library/Engineers", ParamsType = typeof(ClearLaneBlackboard))]
        public static BTreeBuilder<ClearLaneBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<ClearLaneBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.State, Tick);

        public static NodeStatus Tick(ref ClearLaneParams p, ref ClearLaneState s, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<SimTransform>(self)) return NodeStatus.Failure;
            var here = world.GetComponentRO<SimTransform>(self).Position;
            var source = LocomotionMoveTo.OrderSource(world, self);
            if (s.Phase == ToStart)
            {
                if (Flat(here, p.Start) <= StartRadius)
                {
                    ClearerEquipment.SetActive(world, self, true, (Fdp.Toolkit.Terrain.MineClearingTool)p.Tool, p.Width);   // the tool on at the lane's mouth
                    s.ToolOn = 1;
                    s.Phase = Clearing;
                    s.Issued = 0;
                }
                else if (s.Issued == 0)
                {
                    if (!LocomotionMoveTo.Issue(world, self, p.Start, p.Speed, StartRadius * 0.5f, MoveGait.Keep, source: source)) return NodeStatus.Failure;
                    s.Issued = 1;
                    return NodeStatus.Running;
                }
                else return NodeStatus.Running;
            }
            if (Flat(here, p.End) <= EndRadius)
            {
                LocomotionMoveTo.Stop(world, self);
                ClearerEquipment.SetActive(world, self, false);
                s.ToolOn = 0;
                return NodeStatus.Success;
            }
            if (s.Issued == 0)
            {
                if (!LocomotionMoveTo.Issue(world, self, p.End, p.Pace, EndRadius * 0.5f, MoveGait.Keep, source: source)) return NodeStatus.Failure;
                s.Issued = 1;
            }
            return NodeStatus.Running;
        }

        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.ClearLaneBehavior.Tick")]
        public static void Deactivate(ref ClearLaneParams p, ref ClearLaneState s, Entity self, EntityRepository world)
        {
            if (s.ToolOn != 0) ClearerEquipment.SetActive(world, self, false);   // a cut-off clearing never leaves the tool on
            s.ToolOn = 0;
        }

        internal static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y));
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4624</c> MS-B3 (MF-11) — <c>CrossLane</c>, a squad's crossing of a cleared lane in SINGLE FILE: every live man to the ASSEMBLY point on
    /// the lane's axis <see cref="AssemblyMetres"/> short of its start (the side he came from); then man i goes once man i − 1 has gone
    /// <c>Spacing</c> on, STRAIGHT down the axis to <see cref="ExitMetres"/> past its end — so no man ever leaves the lane. Orders through
    /// <see cref="CommandOrders.Assign"/>. Success once every live man is out.
    /// </summary>
    public static class CrossLaneBehavior
    {
        public const float DefaultSpacing = 5f, DefaultSpeed = 1.5f, AssemblyMetres = 10f, ExitMetres = 10f, ThereMetres = 4f, LimitSeconds = 600f;

        [StructLayout(LayoutKind.Sequential)]
        public struct CrossLaneParams
        {
            public Vector3 Start, End;
            public float Spacing, Speed;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CrossLaneState
        {
            public byte Assembled, Ordered, Released;
            public SimInstant Since;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CrossLaneBlackboard
        {
            public CrossLaneParams Params;
            public CrossLaneState State;
        }

        [BehaviorResolver(BehaviorNames.CrossLane)]
        public static void Resolve(in CrossLaneParamsJsonDto a, ref CrossLaneBlackboard block, EntityRepository world, Entity self)
        {
            block.Params = new CrossLaneParams
            {
                Start = a.Start, End = a.End, Spacing = a.Spacing > 0f ? a.Spacing : DefaultSpacing, Speed = a.Speed > 0f ? a.Speed : DefaultSpeed,
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.CrossLane, Params = typeof(CrossLaneParamsJsonDto), Categories = BehaviorCategory.Commander,
            Description = "A squad crosses a cleared minefield lane in single file along its axis.")]
        [BTreeDefinition(BehaviorNames.CrossLane, Curated = true, Group = "Library/Engineers", ParamsType = typeof(CrossLaneBlackboard))]
        public static BTreeBuilder<CrossLaneBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<CrossLaneBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.State, Tick);

        /// <summary>The lane's axis (unit, flat), start → end.</summary>
        public static Vector3 Axis(in CrossLaneParams p)
        {
            var d = new Vector2(p.End.X - p.Start.X, p.End.Y - p.Start.Y);
            d = d.LengthSquared() > 1e-4f ? Vector2.Normalize(d) : Vector2.UnitX;
            return new Vector3(d, 0f);
        }

        public static Vector3 Assembly(in CrossLaneParams p) => p.Start - Axis(in p) * AssemblyMetres;
        public static Vector3 Exit(in CrossLaneParams p) => p.End + Axis(in p) * ExitMetres;

        public static NodeStatus Tick(ref CrossLaneParams p, ref CrossLaneState s, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<UnitRoster>(self)) return NodeStatus.Failure;
            var roster = world.GetComponentRO<UnitRoster>(self);
            int n = roster.Count;
            if (n == 0) return NodeStatus.Failure;
            double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : world.SimulationTime;
            if (s.Ordered == 0) { s.Since = (SimInstant)now; s.Ordered = 1; Order(world, self, in roster, Assembly(in p), p.Speed, 0, n); return NodeStatus.Running; }
            if (now - (double)s.Since >= LimitSeconds) return NodeStatus.Failure;
            var assembly = Assembly(in p);
            if (s.Assembled == 0)
            {
                for (int i = 0; i < n; i++)
                {
                    var m = roster.SubordinateEntities[i];
                    if (!Live(world, m)) continue;
                    if (ClearLaneBehavior.Flat(world.GetComponentRO<SimTransform>(m).Position, assembly) > ThereMetres) return NodeStatus.Running;
                }
                s.Assembled = 1;
            }
            // ⭐ single file: man i goes once man i − 1 is Spacing down the lane from the assembly point
            while (s.Released < n)
            {
                int i = s.Released;
                if (i > 0)
                {
                    var ahead = roster.SubordinateEntities[i - 1];
                    if (Live(world, ahead) && ClearLaneBehavior.Flat(world.GetComponentRO<SimTransform>(ahead).Position, assembly) < p.Spacing) break;
                }
                if (Live(world, roster.SubordinateEntities[i])) Order(world, self, in roster, Exit(in p), p.Speed, i, i + 1);
                s.Released++;
            }
            if (s.Released < n) return NodeStatus.Running;
            var exit = Exit(in p);
            for (int i = 0; i < n; i++)
            {
                var m = roster.SubordinateEntities[i];
                if (Live(world, m) && ClearLaneBehavior.Flat(world.GetComponentRO<SimTransform>(m).Position, exit) > ThereMetres) return NodeStatus.Running;
            }
            return NodeStatus.Success;
        }

        private static bool Live(EntityRepository world, Entity m)
            => !m.IsNull && world.IsAlive(m) && world.HasComponent<SimTransform>(m) && Injury.IsInTheFight(world, m);

        private static void Order(EntityRepository world, Entity self, in UnitRoster roster, Vector3 to, float speed, int from, int until)
        {
            for (int i = from; i < until; i++)
            {
                var m = roster.SubordinateEntities[i];
                if (Live(world, m)) CommandOrders.Assign(world, self, m, BehaviorNames.MoveToLocation, MoveJson(to, speed), newOrder: true);
            }
        }

        internal static string MoveJson(Vector3 to, float speed)
            => string.Create(CultureInfo.InvariantCulture, $"{{\"target\":[{to.X:0.##},{to.Y:0.##},{to.Z:0.##}],\"speed\":{speed:0.##},\"arrivalRadius\":2}}");
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4624</c> MS-B3 (Minefields MF-11; the assault's breach, applied) — <c>Breach</c>, a commander's play over its roster: the VEHICLE is the
    /// clearer, the LAST foot unit the assault, every other foot unit the support. ① Suppress: each support unit <c>SupportByFire</c> on the FAR side
    /// (<see cref="FarMetres"/> past the lane's end) and SMOKE is asked there (the company's fire request); ② the clearer <c>ClearLane</c>; ③ once its
    /// order has ended, the assault <c>CrossLane</c>; Success when it has crossed (its order ended). Orders through <see cref="CommandOrders.Assign"/>.
    /// </summary>
    public static class BreachBehavior
    {
        public const float FarMetres = 60f, LimitSeconds = 1800f, SupportBackMetres = 40f;
        public const byte Suppress = 0, Clear = 1, Cross = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct BreachParams
        {
            public int FieldNetId;
            public Vector3 Start, End;
            public float Width;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BreachState
        {
            public byte Phase, Ordered;
            public SimInstant Since, PhaseSince;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BreachBlackboard
        {
            public BreachParams Params;
            public BreachState State;
        }

        [BehaviorResolver(BehaviorNames.Breach)]
        public static void Resolve(in BreachParamsJsonDto a, ref BreachBlackboard block, EntityRepository world, Entity self)
        {
            block.Params = new BreachParams { FieldNetId = a.Field.Ref.NetworkId, Start = a.Start, End = a.End, Width = a.Width > 0f ? a.Width : ClearLaneBehavior.DefaultWidth };
            block.State = default;
        }

        [Behavior(BehaviorNames.Breach, Params = typeof(BreachParamsJsonDto), Categories = BehaviorCategory.Commander,
            Description = "Breaches a minefield: fire and smoke on the far side, the clearer cuts a lane, the assault crosses in single file.")]
        [BTreeDefinition(BehaviorNames.Breach, Curated = true, Group = "Library/Engineers", ParamsType = typeof(BreachBlackboard))]
        public static BTreeBuilder<BreachBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<BreachBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.State, Tick);

        /// <summary>The far side's aim point.</summary>
        public static Vector3 Far(in BreachParams p)
        {
            var d = new Vector2(p.End.X - p.Start.X, p.End.Y - p.Start.Y);
            d = d.LengthSquared() > 1e-4f ? Vector2.Normalize(d) : Vector2.UnitX;
            return p.End + new Vector3(d * FarMetres, 0f);
        }

        public static NodeStatus Tick(ref BreachParams p, ref BreachState s, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<UnitRoster>(self)) return NodeStatus.Failure;
            var roster = world.GetComponentRO<UnitRoster>(self);
            int clearer = -1, assault = -1;
            for (int i = 0; i < roster.Count; i++)
            {
                var m = roster.SubordinateEntities[i];
                if (m.IsNull || !world.IsAlive(m)) continue;
                if (clearer < 0 && Fdp.Toolkit.Motion.Locomotions.IsVehicle(world, m)) clearer = i;
                else if (!Fdp.Toolkit.Motion.Locomotions.IsVehicle(world, m)) assault = i;
            }
            if (clearer < 0 || assault < 0) return NodeStatus.Failure;   // a breach needs a clearer and someone to cross
            double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : world.SimulationTime;
            if (s.Since <= 0d) s.Since = (SimInstant)now;
            if (now - (double)s.Since >= LimitSeconds) return NodeStatus.Failure;
            var json = Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;
            if (s.Ordered == 0)
            {
                s.Ordered = 1;
                s.PhaseSince = (SimInstant)now;
                if (s.Phase == Suppress)
                {
                    var far = Far(in p);
                    var back = p.Start - Vector3.Normalize(new Vector3(p.End.X - p.Start.X, p.End.Y - p.Start.Y, 0f) + new Vector3(1e-4f, 0f, 0f)) * SupportBackMetres;
                    for (int i = 0; i < roster.Count; i++)
                    {
                        var m = roster.SubordinateEntities[i];
                        if (i == clearer || i == assault || m.IsNull || !world.IsAlive(m)) continue;
                        CommandOrders.Assign(world, self, m, BehaviorNames.SupportByFire,
                            System.Text.Json.JsonSerializer.Serialize(new SupportByFireParamsJsonDto { Position = back, Post1 = far }, json), newOrder: true);
                    }
                    CompanyAttackBehavior.AskFires(world, self, far, Fdp.Toolkit.Squad.Command.FireSupportRound.Smoke);
                    s.Phase = Clear;
                    s.Ordered = 0;
                    return NodeStatus.Running;
                }
                if (s.Phase == Clear)
                {
                    CommandOrders.Assign(world, self, roster.SubordinateEntities[clearer], BehaviorNames.ClearLane,
                        System.Text.Json.JsonSerializer.Serialize(new ClearLaneParamsJsonDto { Field = new Fdp.Toolkit.Replication.EntityLink(new Fdp.Toolkit.Replication.EntityRef(p.FieldNetId)), Start = p.Start, End = p.End, Width = p.Width }, json), newOrder: true);
                    return NodeStatus.Running;
                }
                CommandOrders.Assign(world, self, roster.SubordinateEntities[assault], BehaviorNames.CrossLane,
                    System.Text.Json.JsonSerializer.Serialize(new CrossLaneParamsJsonDto { Start = p.Start, End = p.End }, json), newOrder: true);
                return NodeStatus.Running;
            }
            // the phase's unit is done once its order no longer runs (an order lands a tick later: a 2 s grace)
            if (now - (double)s.PhaseSince < 2d) return NodeStatus.Running;
            var who = roster.SubordinateEntities[s.Phase == Clear ? clearer : assault];
            var order = Hrot.AI.Behaviors.Orders.CurrentOrders.Of(world, who);
            if (order.Exists && !order.Paused) return NodeStatus.Running;
            if (s.Phase == Cross) return NodeStatus.Success;
            s.Phase = Cross;
            s.Ordered = 0;
            return NodeStatus.Running;
        }
    }
}
