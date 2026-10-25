using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Survey;
using Fdp.Toolkit.Squad;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-2374</c> (AQ104 B) — the area clearing's memory: the order, the area survey's clock, the area's buildings and each one's state,
    /// the one in hand, and the building play's params for it (its state is the blackboard's). 📄 docs/designs/group-maneuvers/DESIGN_Building_Clearing.md §7.4.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ClearAreaParams
    {
        public const int MaxBuildings = WorldSurveyRequest.MaxBuildings;
        /// <summary>CA-4 — a known enemy within this of a cleared building's centre sends the squad through it once more (m).</summary>
        public const float ReclearMetres = 10f;

        public Vector3 Centre;
        public float Radius, Speed;
        public ClearIntensity Intensity;
        public byte Suspected;
        public byte Asked, Planned;
        public double AskedAt;
        public fixed float BuildingX[MaxBuildings];
        public fixed float BuildingY[MaxBuildings];
        /// <summary>Per building: <see cref="RoomOrder.Unknown"/>, <c>Cleared</c> or <c>Blocked</c>.</summary>
        public fixed byte State[MaxBuildings];
        /// <summary>Per building: 1 once CA-4 sent the squad through it again.</summary>
        public fixed byte Recleared[MaxBuildings];
        public byte BuildingCount;
        public int Current;
        public ClearBuildingParams Inner;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2374</c> (AQ104 B, R-2079) — <c>ClearArea</c>: a squad clears an AREA — one survey of the area's buildings, then
    /// <see cref="ClearBuildingPlay"/> on each, the one nearest the squad next (CA-2); a building it cannot enter is Blocked and it goes on
    /// (CA-3); when all are done, a known enemy still at a cleared building sends it through that one once more (CA-4). A SEQUENCER over the
    /// existing building play (CA-1): one <see cref="ClearBuildingParams"/> run by the same <see cref="SquadManeuverRunner"/>.
    /// 📄 docs/designs/group-maneuvers/DESIGN_Building_Clearing.md §7.4.
    /// </summary>
    public static class ClearAreaBehavior
    {
        /// <summary>The area survey's site · given up after this long (s) · the radius when the order gives none (m).</summary>
        public const int AreaSite = 0x23740001;
        public const float AskTimeoutSeconds = 5f, DefaultRadius = 30f;

        [StructLayout(LayoutKind.Sequential)]
        public struct ClearAreaBlackboard
        {
            public ClearAreaParams Order;
            /// <summary>The building play's own state (the one in hand) — fresh for each building.</summary>
            public SquadPlayState State;
        }

        [BehaviorResolver(BehaviorNames.ClearArea)]
        public static void Resolve(in ClearAreaParamsJsonDto authored, ref ClearAreaBlackboard block, EntityRepository world, Entity self)
        {
            block.Order = new ClearAreaParams
            {
                Centre = authored.Centre, Radius = authored.Radius > 0f ? authored.Radius : DefaultRadius, Speed = authored.Speed > 0f ? authored.Speed : 2f,
                Intensity = authored.Intensity, Suspected = authored.Suspected ? (byte)1 : (byte)0, Current = -1,
            };
        }

        [Behavior(BehaviorNames.ClearArea, Params = typeof(global::Hrot.Map.Definitions.Behavior.ClearAreaParamsJsonDto), Categories = BehaviorCategory.Commander)]
        [BTreeDefinition(BehaviorNames.ClearArea, Curated = true, Group = "Library/Squad", ParamsType = typeof(ClearAreaBlackboard))]
        public static BTreeBuilder<ClearAreaBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<ClearAreaBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, Run);

        public static unsafe NodeStatus Run(ref ClearAreaParams p, ref SquadPlayState ws, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<UnitRoster>(self) || !world.HasComponent<SquadCognitiveState>(self) || world.GetComponentRO<UnitRoster>(self).Count == 0)
                return Lone(ref p, self, world);
            var roster = world.GetComponentRO<UnitRoster>(self);
            var ctx = new SquadPlayContext(world, self, SquadManeuverRunner.Now(world), in roster);
            if (!SquadManeuverRunner.AnyLive(in ctx)) return NodeStatus.Failure;
            AreaAlert.KeepInArea(in ctx, p.Centre, p.Radius);   // CA-5 — the whole area stays alerted
            if (p.Planned == 0) return Survey(ref p, in ctx);

            if (p.Current < 0)
            {
                p.Current = Next(ref p, in ctx);
                if (p.Current < 0)
                {
                    int again = Reclear(ref p, in ctx);
                    if (again < 0) return Finished(ref p);
                    p.Current = again;
                }
                p.Inner = new ClearBuildingParams
                {
                    Point = new Vector3(p.BuildingX[p.Current], p.BuildingY[p.Current], p.Centre.Z), Speed = p.Speed, Current = -1,
                    Intensity = p.Intensity, Suspected = p.Suspected,
                };
                ws = default;
            }
            var status = SquadManeuverRunner.Tick<ClearBuildingPlay, ClearBuildingParams>(ref p.Inner, ref ws, self, world);
            if (status == NodeStatus.Running) return NodeStatus.Running;
            p.State[p.Current] = status == NodeStatus.Success ? RoomOrder.Cleared : RoomOrder.Blocked;   // CA-3
            ClearBuildingPlay.DropSurvey(world, self);   // the next building asks its own
            p.Current = -1;
            return NodeStatus.Running;
        }

        /// <summary>⭐ <c>CE-2375</c> (AQ104 A-1, R-2081) — the order on a LONE man: his squad is asked for (once) and gets this ClearArea when it
        /// exists; he waits on it (its member order replaces this task). Not lone (a commander with no men) ⇒ Failure as before.</summary>
        private static NodeStatus Lone(ref ClearAreaParams p, Entity self, EntityRepository world)
        {
            if (!OneManCommand.IsLone(world, self)) return NodeStatus.Failure;
            if (world.HasManagedComponent<PendingUnitCommand>(self) || p.Asked != 0) return NodeStatus.Running;
            p.Asked = 1;
            var json = System.Text.Json.JsonSerializer.Serialize(new ClearAreaParamsJsonDto
            {
                Centre = p.Centre, Radius = p.Radius, Speed = p.Speed, Intensity = p.Intensity, Suspected = p.Suspected != 0,
            }, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);
            return OneManCommand.Request(world, self, BehaviorNames.ClearArea, json) ? NodeStatus.Running : NodeStatus.Failure;
        }

        /// <summary>Leaving the node: both surveys go.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.ClearAreaBehavior.Run")]
        public static void Deactivate_Run(ref ClearAreaParams p, ref SquadPlayState ws, Entity self, EntityRepository world)
        {
            ClearBuildingPlay.DropSurvey(world, self);
            DropSurvey(world, self);
        }

        /// <summary>CA-2 — one survey of the area; its non-solid buildings (≤ <see cref="ClearAreaParams.MaxBuildings"/>). None ⇒ nothing to
        /// clear (Success); no answer in time ⇒ Failure.</summary>
        private static unsafe NodeStatus Survey(ref ClearAreaParams p, in SquadPlayContext ctx)
        {
            if (p.Asked == 0)
            {
                WorldSurveyChildSensor.Ensure(ctx.World, ctx.Commander, AreaSite, WorldSurveyRequest.OfArea(new Vector2(p.Centre.X, p.Centre.Y), p.Radius));
                p.Asked = 1;
                p.AskedAt = ctx.Now;
                return NodeStatus.Running;
            }
            var child = Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(ctx.World, ctx.Commander, AreaSite);
            var read = WorldSurveyChildSensor.Read(ctx.World, child, ctx.Now);
            if (!read.Ready || read.Payload == null)
            {
                if (ctx.Now - p.AskedAt < AskTimeoutSeconds) return NodeStatus.Running;
                DropSurvey(ctx.World, ctx.Commander);
                return NodeStatus.Failure;
            }
            foreach (var b in read.Payload.Buildings)
            {
                if (b.Solid || p.BuildingCount >= ClearAreaParams.MaxBuildings) continue;
                p.BuildingX[p.BuildingCount] = b.CentreX;
                p.BuildingY[p.BuildingCount] = b.CentreY;
                p.BuildingCount++;
            }
            DropSurvey(ctx.World, ctx.Commander);
            p.Planned = 1;
            return p.BuildingCount == 0 ? NodeStatus.Success : NodeStatus.Running;
        }

        /// <summary>CA-2 — the building not yet done nearest the squad's centre; -1 when none is left.</summary>
        private static unsafe int Next(ref ClearAreaParams p, in SquadPlayContext ctx)
        {
            var at = RoomDrill.SquadCentre(in ctx);
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int k = 0; k < p.BuildingCount; k++)
            {
                if (p.State[k] != RoomOrder.Unknown) continue;
                float d = Vector2.Distance(new Vector2(p.BuildingX[k], p.BuildingY[k]), new Vector2(at.X, at.Y));
                if (d < bestDistance) { bestDistance = d; best = k; }
            }
            return best;
        }

        /// <summary>CA-4 — a cleared building, not yet cleared twice, with a live non-neutral enemy the squad knows within
        /// <see cref="ClearAreaParams.ReclearMetres"/> of its centre; -1 when none.</summary>
        private static unsafe int Reclear(ref ClearAreaParams p, in SquadPlayContext ctx)
        {
            ref readonly var squad = ref ctx.World.GetComponentRO<SquadCognitiveState>(ctx.Commander);
            var pool = MemoryMarshal.CreateReadOnlySpan(
                ref System.Runtime.CompilerServices.Unsafe.As<SquadContactPoolSlots, SquadContact>(
                    ref System.Runtime.CompilerServices.Unsafe.AsRef(in squad.Contacts.Contacts)), 16);
            for (int k = 0; k < p.BuildingCount; k++)
            {
                if (p.State[k] != RoomOrder.Cleared || p.Recleared[k] != 0) continue;
                for (int c = 0; c < squad.Contacts.Count && c < 16; c++)
                {
                    if ((pool[c].Flags & SquadContact.AnonymousFlag) != 0) continue;
                    var enemy = new Entity((ulong)pool[c].EntityId);
                    if (!ctx.World.IsAlive(enemy) || Fdp.Toolkit.Perception.ThreatDanger.IsOutOfTheFight(ctx.World, enemy)
                        || Fdp.Toolkit.Perception.ThreatDanger.IsNeutral(ctx.World, enemy)) continue;
                    if (Vector2.Distance(new Vector2(pool[c].PositionX, pool[c].PositionY), new Vector2(p.BuildingX[k], p.BuildingY[k])) > ClearAreaParams.ReclearMetres) continue;
                    p.Recleared[k] = 1;
                    p.State[k] = RoomOrder.Unknown;
                    return k;
                }
            }
            return -1;
        }

        /// <summary>CA-3 — done: Success when any building was cleared, Failure when none could be.</summary>
        private static unsafe NodeStatus Finished(ref ClearAreaParams p)
        {
            for (int k = 0; k < p.BuildingCount; k++) if (p.State[k] == RoomOrder.Cleared) return NodeStatus.Success;
            return NodeStatus.Failure;
        }

        private static void DropSurvey(EntityRepository world, Entity self)
        {
            var child = Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(world, self, AreaSite);
            if (!child.IsNull) WorldSurveyChildSensor.Release(world, child);
        }
    }
}
