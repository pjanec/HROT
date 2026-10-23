using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Hrot.AI.Behaviors.Orders;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>The params of <see cref="AirOrderNodes.FlyTo"/>: a point and the height above the ground to hold over it.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FlyToParams
    {
        public float X;
        public float Y;
        /// <summary>Height above the ground (m); 0 ⇒ <see cref="AirOrderNodes.DefaultCruiseHeightAgl"/>.</summary>
        public float HeightAgl;
        /// <summary>Speed (m/s); 0 ⇒ the aircraft's own.</summary>
        public float Speed;
        /// <summary>Arrival radius (m); 0 ⇒ <see cref="AirOrderNodes.DefaultArrivalRadius"/>.</summary>
        public float ArrivalRadius;
        /// <summary>Runtime: 1 once this node's move is on the locomotion channel — never authored.</summary>
        [JsonIgnore] public byte Issued;
    }

    /// <summary>
    /// ⭐ <c>CE-2173</c> (B4, R-2015; 📄 docs/designs/air-attack/DESIGN_Air_Operations.md §7) — what an aircraft does with an order. One node
    /// so far: <see cref="FlyTo"/> — a MOVE through the air to the point at a height above the ground there. 📐 Before it an aircraft's
    /// Move fell to <c>PlatformMove</c>, which moves ON THE SURFACE: a helicopter flew down to the ground point.
    /// </summary>
    public static class AirOrderNodes
    {
        /// <summary>The height an order flies at when it names none (m above the ground).</summary>
        public const float DefaultCruiseHeightAgl = 120f;
        /// <summary>The arrival radius when the order names none (m).</summary>
        public const float DefaultArrivalRadius = 20f;

        /// <summary>The 3-D point <paramref name="p"/> means: (X, Y) at <c>HeightAgl</c> above the ground the world query reports there.</summary>
        public static Vector3 PointOf(in FlyToParams p, EntityRepository world)
        {
            float ground = Fdp.Toolkit.World.WorldQuery.Of(world)?.GroundHeightAt(p.X, p.Y) ?? 0f;   // no terrain on this node ⇒ flat at 0
            return new Vector3(p.X, p.Y, ground + (p.HeightAgl > 0f ? p.HeightAgl : DefaultCruiseHeightAgl));
        }

        [SharedAiAction]
        public static NodeStatus FlyTo(ref FlyToParams p, Entity self, EntityRepository world)
        {
            if (p.Issued == 0)
            {
                if (!LocomotionMoveTo.Issue(world, self, PointOf(in p, world), p.Speed, p.ArrivalRadius > 0f ? p.ArrivalRadius : DefaultArrivalRadius, source: LocomotionMoveTo.OrderSource(world, self)))
                    return NodeStatus.Failure;
                p.Issued = 1;
                return NodeStatus.Running;
            }
            return LocomotionMoveTo.Status(world, self);
        }
    }

    /// <summary>⭐ <c>CE-2173</c> — MOVE for an aircraft (<c>AirPlatform</c>, every DIS air platform): fly to the order's point at the
    /// cruise height above the ground there. More specific than <c>PlatformMove</c> (R-278), so no aircraft moves on the surface.</summary>
    public static class AirPlatformMoveBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct AirPlatformMoveBlackboard
        {
            public FlyToParams Fly;
        }

        [BehaviorResolver(BehaviorNames.AirPlatformMove)]
        public static void Resolve(in MoveOrder order, ref AirPlatformMoveBlackboard block, EntityRepository world, Entity self)
        {
            var at = OrderGeo.ToLocal(world, order.Location, 0f);
            block.Fly = new FlyToParams { X = at.X, Y = at.Y, Speed = order.Speed, ArrivalRadius = order.ArrivalRadius };
        }

        [Behavior(BehaviorNames.AirPlatformMove, Params = typeof(MoveOrder), Categories = BehaviorCategory.MilitaryApc)]
        [BTreeDefinition(BehaviorNames.AirPlatformMove, Curated = true, Group = "Library/Orders", ParamsType = typeof(AirPlatformMoveBlackboard))]
        public static BTreeBuilder<AirPlatformMoveBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<AirPlatformMoveBlackboard, BTreeContext>().Action(bb => bb.Fly, AirOrderNodes.FlyTo);
    }
}
