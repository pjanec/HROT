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
            // ⭐⭐ CE-10005 (AC-9, RV-14) — a TRANSIT's missile defence, through THE ONE MissileDefence (a MoveTo flight: the beam break, a helicopter
            //   drops to its evade height); while it runs the move waits, and when it ends the move is issued again from where the aircraft is
            if (Defend(ref p, self, world)) return NodeStatus.Running;
            if (p.Issued == 0)
            {
                if (!LocomotionMoveTo.Issue(world, self, PointOf(in p, world), p.Speed, p.ArrivalRadius > 0f ? p.ArrivalRadius : DefaultArrivalRadius, source: LocomotionMoveTo.OrderSource(world, self)))
                    return NodeStatus.Failure;
                p.Issued = 1;
                return NodeStatus.Running;
            }
            return LocomotionMoveTo.Status(world, self);
        }

        /// <summary>⭐ CE-10005 (AC-9) — the transit's defence: true while it runs (its beam break is on the locomotion channel); the move is
        /// re-issued (<see cref="FlyToParams.Issued"/> cleared) so it resumes when the defence ends. Only an aircraft with a defence state type
        /// and a missile warning ever enters it.</summary>
        private static bool Defend(ref FlyToParams p, Entity self, EntityRepository world)
        {
            if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.MissileWarning>()
                || !world.IsComponentTypeRegistered<Fdp.Toolkit.Motion.MissileDefenceState>()
                || (!world.HasComponent<Fdp.Toolkit.Combat.Components.MissileWarning>(self) && !world.HasComponent<Fdp.Toolkit.Motion.MissileDefenceState>(self)))
                return false;
            double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d;
            var before = MissileDefence.Load(world, self);
            var ds = before;
            Vector3 at = world.HasComponent<SimTransform>(self) ? (Vector3)world.GetComponentRO<SimTransform>(self).Position : Vector3.Zero;
            bool rotor = Fdp.Toolkit.Motion.Locomotions.Of(world, self) == Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft;
            var to = PointOf(in p, world);
            var dir = new Vector2(to.X - at.X, to.Y - at.Y);
            var flight = new MissileDefence.Flight
            {
                Fighter = false, MoveZ = rotor ? to.Z - (p.HeightAgl > 0f ? p.HeightAgl : DefaultCruiseHeightAgl) + AirAttackNodes.EvadeHeightAglRotor
                    : MathF.Max(at.Z, (Fdp.Toolkit.World.WorldQuery.Of(world)?.GroundHeightAt(at.X, at.Y) ?? 0f) + AirAttackNodes.EvadeHeightAglPlane),
                Speed = p.Speed, FallbackDir = dir.LengthSquared() > 1f ? Vector2.Normalize(dir) : Vector2.UnitY,
            };
            bool defending = MissileDefence.Run(world, self, ref ds, now, in flight, out _, out _);
            if (!AirAttackNodes.Same(in before, in ds)) MissileDefence.Store(world, self, in ds);
            if (defending) p.Issued = 0;   // the move resumes from here when the defence ends
            return defending;
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
            var at = (System.Numerics.Vector3)order.Location;
            block.Fly = new FlyToParams { X = at.X, Y = at.Y, Speed = order.Speed, ArrivalRadius = order.ArrivalRadius };
        }

        [Behavior(BehaviorNames.AirPlatformMove, Params = typeof(MoveOrder), Categories = BehaviorCategory.MilitaryApc)]
        [BTreeDefinition(BehaviorNames.AirPlatformMove, Curated = true, Group = "Library/Orders", ParamsType = typeof(AirPlatformMoveBlackboard))]
        public static BTreeBuilder<AirPlatformMoveBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<AirPlatformMoveBlackboard, BTreeContext>().Action(bb => bb.Fly, AirOrderNodes.FlyTo);
    }
}
