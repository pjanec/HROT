using System;
using System.Numerics;
using System.Runtime.InteropServices;
using CarKinem.Convoy;
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
    /// <see cref="ConvoyOrder"/>. It writes only its OWN <see cref="ConvoyMember"/> (R-4212); the kinematic owner's <c>ConvoyLeadSystem</c> /
    /// <c>ConvoyFollowSystem</c> drive the column from it. The LEAD moves to the destination — handed to SUMO (<see cref="ConvoyMode.Traffic"/>, beacon on when
    /// the order says priority) or on our own route (<see cref="ConvoyMode.Route"/> — <see cref="RoadUse.StronglyPrefer"/>, the outer lane); a FOLLOWER stops its
    /// own move and is driven along the lead's path. Success: the lead within <see cref="ArrivalRadius"/> of the destination; a follower when its lead
    /// is done. ⭐ C-10 — contact or a new order ends the column: <see cref="ConvoyMembershipSystem"/> removes the membership of a vehicle no longer running this.
    /// </summary>
    public static class PlatformConvoyBehavior
    {
        public const float DefaultSpeed = 10f, ArrivalRadius = 15f;

        [StructLayout(LayoutKind.Sequential)]
        public struct ConvoyParams
        {
            public long LeadPacked;
            public Vector3 Destination;
            public float Gap, MinGap, Speed;
            public byte Rank, Mode, Priority;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ConvoyState
        {
            public byte Issued, Stopped, Done;
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
                Gap = order.Gap > 0f ? order.Gap : ConvoyMember.DefaultGap, MinGap = ConvoyMember.DefaultMinGap,
                Speed = order.Speed > 0f ? order.Speed : DefaultSpeed,
                Rank = (byte)Math.Clamp(order.Rank, 0, ConvoyTrail.MaxRanks - 1), Mode = (byte)order.Mode, Priority = order.Priority ? (byte)1 : (byte)0,
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

        /// <summary>⭐ C-9 — the lead's road use: SUMO for <c>Traffic</c> (with its beacon when priority), our own route otherwise (<c>Auto</c> = our route: it
        /// ignores lights by construction while SumoSharp H14 is not synced — R-9012).</summary>
        public static RoadUse LeadRoadUse(in ConvoyParams p)
            => (ConvoyMode)p.Mode == ConvoyMode.Traffic ? (p.Priority != 0 ? RoadUse.InTrafficPriority : RoadUse.InTraffic) : RoadUse.StronglyPrefer;

        public static NodeStatus Tick(ref ConvoyParams p, ref ConvoyState s, Entity self, EntityRepository world)
        {
            if (!world.IsComponentTypeRegistered<ConvoyMember>() || !world.HasComponent<SimTransform>(self)) return NodeStatus.Failure;
            if (s.Done != 0) return NodeStatus.Success;
            var here = world.GetComponentRO<SimTransform>(self).Position;

            if (IsLead(in p, self))
            {
                if (!world.HasComponent<ConvoyMember>(self))
                    world.AddComponent(self, new ConvoyMember { Lead = self, Rank = 0, Gap = p.Gap, MinGap = p.MinGap, Mode = p.Mode, Flags = p.Priority });
                if (Flat(here, p.Destination) <= ArrivalRadius)
                {
                    LocomotionMoveTo.Stop(world, self);
                    world.RemoveComponent<ConvoyMember>(self);
                    s.Done = 1;
                    return NodeStatus.Success;
                }
                if (s.Issued == 0)
                {
                    if (!LocomotionMoveTo.Issue(world, self, p.Destination, p.Speed, ArrivalRadius * 0.5f, MoveGait.Keep,
                            source: LocomotionMoveTo.OrderSource(world, self), roadUse: LeadRoadUse(in p)))
                        return NodeStatus.Failure;
                    s.Issued = 1;
                }
                return NodeStatus.Running;
            }

            // a follower: driven along the lead's path by the kinematic owner; done when the lead is
            var lead = new Entity((ulong)p.LeadPacked);
            if (!world.IsAlive(lead) || (s.Stopped != 0 && !world.HasComponent<ConvoyMember>(lead)))
            {
                if (world.HasComponent<ConvoyMember>(self)) world.RemoveComponent<ConvoyMember>(self);
                s.Done = 1;
                return NodeStatus.Success;
            }
            if (!world.HasComponent<ConvoyMember>(self))
                world.AddComponent(self, new ConvoyMember { Lead = lead, Rank = p.Rank, Gap = p.Gap, MinGap = p.MinGap, Mode = p.Mode, Flags = p.Priority });
            if (s.Stopped == 0) { LocomotionMoveTo.Stop(world, self); s.Stopped = 1; }
            return NodeStatus.Running;
        }

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y));
    }

    /// <summary>
    /// ⭐ <c>CE-9004</c> (C-10, R-9009) — a vehicle's convoy membership lives exactly as long as it RUNS <c>PlatformConvoy</c>: contact (a reaction takes over,
    /// the order is only paused), a new order, or the behaviour's end removes its <see cref="ConvoyMember"/> — so the kinematic owner stops driving it in the
    /// column at once and its own SOP / the platoon's Contact phase drives it; the lead's intent changes with the new behaviour, which takes its shadow out of
    /// SUMO (AQ93 G5). Nothing is re-created. Writes only each entity's own membership (the per-entity rule); 0 B per steady frame.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [UpdateAfter(typeof(Fdp.Toolkit.Behavior.Systems.LocomotionDispatcherSystem))]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class ConvoyMembershipSystem : IEcsModuleSystem
    {
        private static readonly int ConvoyHash = BehaviorHash.FromName(BehaviorNames.PlatformConvoy);
        private readonly bool _gateOnAuthority;
        private EntityQuery? _members;

        public ConvoyMembershipSystem(bool gateOnAuthority = false) => _gateOnAuthority = gateOnAuthority;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<ConvoyMember>() || !repo.IsComponentTypeRegistered<BehaviorState>()) return;
            _members ??= repo.Query().With<ConvoyMember>().With<BehaviorState>().Build();
            foreach (var e in _members)
            {
                if (_gateOnAuthority && !repo.HasAuthority<BehaviorState>(e)) continue;
                if (repo.GetComponentRO<BehaviorState>(e).ActiveBehaviorHash == ConvoyHash) continue;
                repo.RemoveComponent<ConvoyMember>(e);
            }
        }
    }
}
