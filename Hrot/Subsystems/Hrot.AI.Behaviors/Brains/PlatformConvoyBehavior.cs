using System;
using System.Numerics;
using System.Runtime.InteropServices;
using CarKinem.Convoy;
using CarKinem.Formation;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Services;
using Hrot.AI.Behaviors.Orders;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-9004</c> (C-9, C-10, R-9008 / R-9009; 📄 docs/designs/vehicle-tactics/DESIGN_Road_Convoy.md §3, §4.2–§4.3) — a vehicle fulfilling a
    /// <see cref="ConvoyOrder"/>. It writes only its OWN formation components (R-4212): the lead its <see cref="FormationController"/> (a <see cref="FormationType.Column"/>), a follower its <see cref="FormationFollower"/> slot with an explicit leader; the kinematic owner's <c>ConvoyLeadSystem</c> /
    /// <c>ConvoyFollowSystem</c> drive the column from it. The LEAD moves to the destination — handed to SUMO (<see cref="ConvoyMode.Traffic"/>, beacon on when
    /// the order says priority) or on our own route (<see cref="ConvoyMode.Route"/> — <see cref="RoadUse.StronglyPrefer"/>, the outer lane); a FOLLOWER stops its
    /// own move and is driven along the lead's path. Success: the lead within <see cref="ArrivalRadius"/> of the destination; a follower when its lead
    /// is done. ⭐ C-10 — contact or a new order ends the column: <see cref="ConvoyMembershipSystem"/> removes the column slot of a vehicle no longer running this, and ends the column of a lead that stopped.
    /// </summary>
    public static class PlatformConvoyBehavior
    {
        public const float DefaultSpeed = 10f, ArrivalRadius = 15f;

        [StructLayout(LayoutKind.Sequential)]
        public struct ConvoyParams
        {
            public long LeadPacked;
            public Vector3 Destination;
            /// <summary>⭐ CE-9012 — the authored route the lead follows (its network id); 0 = the lead routes to <see cref="Destination"/>.</summary>
            public int RouteNet;
            public float Gap, Speed;
            public byte Rank, Mode, Priority;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ConvoyState
        {
            public byte Issued, Stopped, Done;
            /// <summary>⭐ CE-9015 — the locomotion action instance this behaviour last wrote (the lead's move, a follower's stop). Another instance in the
            /// channel means a reaction wrote it while this order was paused: on resume the move is issued (the stop applied) again.</summary>
            public uint MoveInstance;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ConvoyBlackboard
        {
            public ConvoyParams Params;
            public ConvoyState State;
        }

        [BehaviorResolver(BehaviorNames.PlatformConvoy)]
        public static void Resolve(in ConvoyOrder order, ref ConvoyBlackboard block, EntityRepository world, Entity self)
        {
            block = default;
            long lead = 0;
            if (!order.Lead.IsNone)
            {
                var map = world.HasSingletonManaged<NetworkEntityMap>() ? world.GetSingletonManaged<NetworkEntityMap>() : null;
                if (map != null && map.TryGetEntity(order.Lead.Ref.NetworkId, out var e) && world.IsAlive(e)) lead = (long)e.PackedValue;
                else Hrot.AI.Behaviors.Logging.BehaviorLog.ParseWarn("PlatformConvoy lead=" + order.Lead.Ref.NetworkId + " not found in entity map; this vehicle leads.");
            }
            block.Params = new ConvoyParams
            {
                LeadPacked = lead, Destination = (Vector3)order.Destination,
                Gap = order.Gap > 0f ? order.Gap : CarKinem.Systems.ConvoyLeadSystem.DefaultGap,
                Speed = order.Speed > 0f ? order.Speed : DefaultSpeed,
                Rank = (byte)Math.Clamp(order.Rank, 0, ConvoyTrail.MaxRanks - 1), Mode = (byte)order.Mode, Priority = order.Priority ? (byte)1 : (byte)0,
                RouteNet = order.Route.IsNone ? 0 : order.Route.Ref.NetworkId,
            };
        }

        [Behavior(BehaviorNames.PlatformConvoy, Params = typeof(ConvoyOrder), Fulfils = nameof(OrderKind.Convoy), Categories = BehaviorCategory.MilitaryApc | BehaviorCategory.Civilian,
            Description = "Drives in a road convoy: the lead to the destination (in SUMO traffic or on our own route), the rest on its path, never split.")]
        [BTreeDefinition(BehaviorNames.PlatformConvoy, Curated = true, Group = "Library/Orders", ParamsType = typeof(ConvoyBlackboard))]
        public static BTreeBuilder<ConvoyBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<ConvoyBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.State, Tick);

        /// <summary>Is <paramref name="self"/> the column's lead under <paramref name="p"/>?</summary>
        public static bool IsLead(in ConvoyParams p, Entity self) => p.Rank == 0 || p.LeadPacked == 0 || p.LeadPacked == (long)self.PackedValue;

        /// <summary>⭐ C-9 — the lead's road use: SUMO (with its beacon when priority) for <c>Traffic</c> and <c>Auto</c> WHEN the lead can be handed to SUMO
        /// (<paramref name="trafficRuns"/>), else our own route (<see cref="RoadUse.StronglyPrefer"/>, the outer lane); <c>Route</c> is always our own route.
        /// ⭐ CE-9010 — 📌 measured: a lead asked <c>InTraffic</c> on a scenario with no traffic settings (R-4047: SUMO does no work) is handed to nobody and
        /// stands still for ever; so the order falls back to our route instead.</summary>
        public static RoadUse LeadRoadUse(in ConvoyParams p, bool trafficRuns)
            => (ConvoyMode)p.Mode == ConvoyMode.Route || !trafficRuns ? RoadUse.StronglyPrefer
             : p.Priority != 0 ? RoadUse.InTrafficPriority : RoadUse.InTraffic;

        /// <summary>⭐ CE-9010 — can a vehicle be handed to SUMO here: the terrain has a SUMO net AND the scenario carries the traffic settings (R-4047)?
        /// One query, read once per order (when the lead issues its move).</summary>
        public static bool TrafficRuns(EntityRepository world)
        {
            if (!world.HasSingletonManaged<Fdp.Toolkit.Terrain.TerrainDefinition>() || world.GetSingletonManaged<Fdp.Toolkit.Terrain.TerrainDefinition>()?.SumoNet is null) return false;
            if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Traffic.TrafficDemand>()) return false;
            foreach (var _ in world.Query().With<Fdp.Toolkit.Traffic.TrafficDemand>().Build()) return true;
            return false;
        }

        public static NodeStatus Tick(ref ConvoyParams p, ref ConvoyState s, Entity self, EntityRepository world)
        {
            if (!world.IsComponentTypeRegistered<FormationController>() || !world.IsComponentTypeRegistered<FormationFollower>()
                || !world.HasComponent<SimTransform>(self)) return NodeStatus.Failure;
            if (s.Done != 0) return NodeStatus.Success;
            var here = world.GetComponentRO<SimTransform>(self).Position;

            if (IsLead(in p, self))
            {
                // ⭐ CE-9004 — the lead leads a COLUMN formation: its own controller says so (every vehicle carries one; written whole, own entity)
                if (s.Issued == 0) SetController(world, self, Column(in p));
                // ⭐ CE-9014 — the lead's move FAILED (a mobility kill, no path): the column cannot go on behind it — the order fails, its column ends
                //   (📌 cv-ambush: an IED stopped the lead for good and the convoy "ran" for ever, its followers parked in the kill zone)
                if (s.Issued != 0 && OurMove(world, self, in s) && world.GetComponentRO<LocomotionChannel>(self).Status == NodeStatus.Failure)
                {
                    SetController(world, self, new FormationController());
                    s.Done = 1;
                    return NodeStatus.Failure;
                }
                if (Flat(here, p.Destination) <= ArrivalRadius)
                {
                    LocomotionMoveTo.Stop(world, self);
                    SetController(world, self, new FormationController());   // the column ends: its followers see a lead that no longer leads one
                    s.Done = 1;
                    return NodeStatus.Success;
                }
                // ⭐ CE-9015 — paused by a reaction (it wrote the channel): issue the move again on resume (📌 cv-ambush: the lead resumed and stood for good)
                if (s.Issued != 0 && !OurMove(world, self, in s)) s.Issued = 0;
                if (s.Issued == 0)
                {
                    if (p.RouteNet > 0)
                    {
                        // ⭐ CE-9012 — an authored route (a platoon's convoy): the lead follows it as FollowRoute does — the one channel writer
                        if (!world.HasComponent<LocomotionChannel>(self)) return NodeStatus.Failure;
                        CgfNodes.WriteFollowRoute(world, self, CgfNodes.TryFindRouteTrajectory(world, p.RouteNet, out int trajectory) ? trajectory : 0, p.RouteNet, loop: false);
                        s.Issued = 1;
                        s.MoveInstance = world.GetComponentRO<LocomotionChannel>(self).ActionInstanceId;
                        return NodeStatus.Running;
                    }
                    bool traffic = TrafficRuns(world);
                    if ((ConvoyMode)p.Mode == ConvoyMode.Traffic && !traffic)
                        Hrot.AI.Behaviors.Logging.BehaviorLog.Warn(self, world, "PlatformConvoy: Mode Traffic, but no SUMO traffic runs here (a SUMO net and the scenario's traffic settings) — the lead drives our own route.");
                    if (!LocomotionMoveTo.Issue(world, self, p.Destination, p.Speed, ArrivalRadius * 0.5f, MoveGait.Keep,
                            source: LocomotionMoveTo.OrderSource(world, self), roadUse: LeadRoadUse(in p, traffic)))
                        return NodeStatus.Failure;
                    s.Issued = 1;
                    s.MoveInstance = world.GetComponentRO<LocomotionChannel>(self).ActionInstanceId;
                }
                return NodeStatus.Running;
            }

            // a follower: its slot in the lead's column (the kinematic owner drives it along the lead's path); done when the lead's column has ended
            var lead = new Entity((ulong)p.LeadPacked);
            bool leading = world.IsAlive(lead) && world.HasComponent<FormationController>(lead) && Leads(in world.GetComponentRO<FormationController>(lead));
            if (leading) s.Issued = 1;   // the lead's column seen at least once
            if (!world.IsAlive(lead) || (s.Issued != 0 && !leading))
            {
                Leave(world, self, lead);
                s.Done = 1;
                return NodeStatus.Success;
            }
            if (!world.HasComponent<FormationFollower>(self) || world.GetComponentRO<FormationFollower>(self).Leader != lead)
            {
                var slot = new FormationFollower { SlotIndex = p.Rank, Leader = lead, IsInFormation = 1, State = FormationMemberState.Rejoining };
                if (world.HasComponent<FormationFollower>(self)) world.SetComponent(self, slot); else world.AddComponent(self, slot);
            }
            if (s.Stopped != 0 && !OurMove(world, self, in s)) s.Stopped = 0;   // ⭐ CE-9015 — a reaction drove it while paused: stop its move again
            if (s.Stopped == 0)
            {
                LocomotionMoveTo.Stop(world, self);
                s.Stopped = 1;
                if (world.HasComponent<LocomotionChannel>(self)) s.MoveInstance = world.GetComponentRO<LocomotionChannel>(self).ActionInstanceId;
            }
            return NodeStatus.Running;
        }

        /// <summary>⭐ CE-9015 — does the unit's locomotion channel still carry the action this behaviour wrote?</summary>
        private static bool OurMove(EntityRepository world, Entity self, in ConvoyState s)
            => world.HasComponent<LocomotionChannel>(self) && world.GetComponentRO<LocomotionChannel>(self).ActionInstanceId == s.MoveInstance;

        /// <summary>The lead's column: <see cref="FormationType.Column"/> at the order's spacing (the minimum gap is half of it).</summary>
        public static FormationController Column(in ConvoyParams p)
            => new() { Type = FormationType.Column, Params = new FormationParams { Spacing = p.Gap } };

        /// <summary>Does this controller lead a convoy column (a column with a spacing; a vehicle's default controller has none)?</summary>
        public static bool Leads(in FormationController c) => c.Type == FormationType.Column && c.Params.Spacing > 0f;

        private static void SetController(EntityRepository world, Entity self, FormationController c)
        {
            if (world.HasComponent<FormationController>(self)) world.SetComponent(self, c); else world.AddComponent(self, c);
        }

        /// <summary>Leaves the column of <paramref name="lead"/> (its slot only — never a slot under another leader).</summary>
        public static void Leave(EntityRepository world, Entity self, Entity lead)
        {
            if (world.HasComponent<FormationFollower>(self) && world.GetComponentRO<FormationFollower>(self).Leader == lead) world.RemoveComponent<FormationFollower>(self);
        }

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y));
    }

    /// <summary>
    /// ⭐ <c>CE-9004</c> (C-10, R-9009) — a vehicle's place in a convoy column lives exactly as long as it RUNS <c>PlatformConvoy</c>: contact (a reaction takes
    /// over, the order is only paused), a new order, or the behaviour's end removes its explicitly-led <see cref="FormationFollower"/> (a slot with a
    /// <see cref="FormationFollower.Leader"/> — only the convoy sets one; a commander-led slot is left to the command hierarchy) — so the kinematic owner stops
    /// driving it in the column at once and its own SOP / the platoon's Contact phase drives it; the lead's intent changes with its new behaviour, which
    /// takes its shadow out of SUMO (AQ93 G5). Nothing is re-created. Writes only each entity's own slot (the per-entity rule); 0 B per steady frame.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [UpdateAfter(typeof(Fdp.Toolkit.Behavior.Systems.LocomotionDispatcherSystem))]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class ConvoyMembershipSystem : IEcsModuleSystem
    {
        private static readonly int ConvoyHash = BehaviorHash.FromName(BehaviorNames.PlatformConvoy);
        private readonly bool _gateOnAuthority;
        private EntityQuery? _followers, _leads;

        public ConvoyMembershipSystem(bool gateOnAuthority = false) => _gateOnAuthority = gateOnAuthority;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<FormationFollower>() || !repo.IsComponentTypeRegistered<BehaviorState>()) return;
            _followers ??= repo.Query().With<FormationFollower>().With<BehaviorState>().Build();
            foreach (var e in _followers)
            {
                if (_gateOnAuthority && !repo.HasAuthority<BehaviorState>(e)) continue;
                if (repo.GetComponentRO<FormationFollower>(e).Leader.IsNull) continue;          // a commander-led slot: not the convoy's
                if (repo.GetComponentRO<BehaviorState>(e).ActiveBehaviorHash == ConvoyHash) continue;
                repo.RemoveComponent<FormationFollower>(e);
            }
            // a LEAD that stopped running the convoy ends its column (its followers then see no column and finish — C-10: the whole column reacts)
            if (!repo.IsComponentTypeRegistered<FormationController>()) return;
            _leads ??= repo.Query().With<FormationController>().With<BehaviorState>().Build();
            foreach (var e in _leads)
            {
                if (_gateOnAuthority && !repo.HasAuthority<BehaviorState>(e)) continue;
                if (!PlatformConvoyBehavior.Leads(in repo.GetComponentRO<FormationController>(e))) continue;
                if (repo.GetComponentRO<BehaviorState>(e).ActiveBehaviorHash == ConvoyHash) continue;
                repo.SetComponent(e, new FormationController());
            }
        }
    }
}
