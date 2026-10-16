using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>The params of <see cref="EmbarkNodes.Embark"/>: which vehicle, how fast to walk to it.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct EmbarkNodeParams
    {
        /// <summary>The vehicle to board (resolved from the behaviour's <c>EntityRef</c> by its resolver).</summary>
        public Entity Vehicle;
        /// <summary>Walking speed (m/s); 0 ⇒ <see cref="EmbarkNodes.DefaultSpeed"/>.</summary>
        public float Speed;
        /// <summary>Runtime: 1 while this node's walk to the boarding point is on the locomotion channel — never authored.</summary>
        [JsonIgnore] public byte Walking;
        /// <summary>Runtime: where that walk goes (re-issued when the vehicle moved) — never authored.</summary>
        [JsonIgnore] public Vector3 WalkTo;
        /// <summary>Runtime: the interaction activation this node started (0 = none) — never authored.</summary>
        [JsonIgnore] public uint Started;
        /// <summary>⭐ <c>CE-2176</c> — the seat role to take: Passenger (0) rides; Gunner MANS a crew weapon (<c>ManWeapon</c>,
        /// Weapon Systems §4b). Appended.</summary>
        public Fdp.Toolkit.Behavior.Components.SeatRole Role;
        /// <summary>⭐ WS-6 — runtime: how many walks to the boarding point this node issued; never authored. Appended.</summary>
        [JsonIgnore] public byte Walks;
    }

    /// <summary>The params of <see cref="EmbarkNodes.Disembark"/>: the vehicle is the one the unit is aboard; HOW he leaves is authored
    /// (zero = step out beside the hull).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DisembarkNodeParams
    {
        /// <summary>Runtime: the interaction activation this node started (0 = none) — never authored.</summary>
        [JsonIgnore] public uint Started;
        /// <summary>⭐ CE-1118 H-1 (Airborne Insertion §6.5 D-18) — step out, down the rope, or out of the door. Appended.</summary>
        public Fdp.Toolkit.Motion.ExitKind Exit;
        /// <summary>⭐ H-1 — which rope or door. Appended.</summary>
        public byte AnchorIndex;
        /// <summary>⭐ H-1 — a jumper's landing point (zero = none): his <c>Descent.Target</c>. Appended.</summary>
        public Vector3 Landing;
    }

    /// <summary>Log category of the embark nodes.</summary>
    public sealed class EmbarkNodesLog { }

    /// <summary>
    /// ⭐⭐ <c>CE-1056</c> I-2d (📄 docs/DESIGN_Entity_Interactions.md §7.3) — the AI asks to board or leave a vehicle. Two shared nodes
    /// (BTree, HSM, blueprint), the shape of <c>DoorNodes</c>: walk on the locomotion channel, then ONE action on the interaction
    /// channel, and forward its answer once.
    /// <list type="bullet">
    ///   <item><description><see cref="Embark"/> — walks to <see cref="Embarkation.BoardingPoint"/> (through <see cref="LocomotionMoveTo"/>,
    ///   the one way a C# node moves its unit) until within <see cref="Embarkation.BoardingRange"/>, then runs
    ///   <see cref="EmbarkExecutor"/> (action 1), which raises the command the vehicle's owner applies. Success = aboard.</description></item>
    ///   <item><description><see cref="Disembark"/> — runs <see cref="DisembarkExecutor"/> (action 2). Success = not aboard.</description></item>
    /// </list>
    /// ⛔ Neither writes embarkation state: <c>EmbarkationSystem</c> on the vehicle's owner is the only applier (§7.1b).
    /// </summary>
    public static class EmbarkNodes
    {
        /// <summary>Walking speed when the params give none (m/s).</summary>
        public const float DefaultSpeed = 2f;
        /// <summary>A walk is re-issued when the boarding point moved this far from where it was sent (m).</summary>
        public const float RewalkMetres = 2f;
        /// <summary>⭐ WS-6 — at most this many walks to the boarding point per boarding (as <c>CarryNodes.MaxWalks</c>): a walk that
        /// ended still out of reach is walked again. 📐 Live <c>ua-weapon-systems</c>: the gunner's FIRST walk failed in its first second
        /// and the node gave up for good; the pick-up beside him (which re-walks, CE-2152 R-e) went on and took its Javelin.</summary>
        public const int MaxWalks = 3;

        [SharedAiAction]
        public static unsafe NodeStatus Embark(ref EmbarkNodeParams p, Entity self, EntityRepository world)
        {
            var vehicle = p.Vehicle;
            if (!world.IsAlive(vehicle) || !world.HasComponent<PassengerBuffer>(vehicle))
            {
                Fdp.Core.Logging.FdpLog<EmbarkNodesLog>.Warn($"Embark: no carrier {vehicle} on this node. (entity {self.Index})");
                EndWalk(ref p, self, world);
                return NodeStatus.Failure;
            }
            if (world.HasComponent<IsEmbarkedTag>(self) && world.GetComponentRO<IsEmbarkedTag>(self).VehicleEntity == vehicle)
            {
                EndWalk(ref p, self, world);
                if (p.Started != 0 && OwnsInteraction(world, self, p.Started, BehaviorConstants.ActionIdEmbark)) Release(world, self);
                p.Started = 0;
                return NodeStatus.Success;
            }
            if (!world.HasComponent<InteractionChannel>(self) || !world.HasComponent<SimTransform>(self))
            {
                Fdp.Core.Logging.FdpLog<EmbarkNodesLog>.Warn($"Embark: the entity has no InteractionChannel / SimTransform. (entity {self.Index})");
                return NodeStatus.Failure;
            }

            // The boarding action this node started: forward its answer once.
            if (p.Started != 0 && OwnsInteraction(world, self, p.Started, BehaviorConstants.ActionIdEmbark))
            {
                ref var running = ref world.GetComponentRW<InteractionChannel>(self);
                if (running.Status == NodeStatus.Running) return NodeStatus.Running;
                var done = running.Status;
                Release(world, self);
                p.Started = 0;
                return done;
            }
            p.Started = 0;

            float range = Embarkation.BoardingRange(world, vehicle);
            var at = world.GetComponentRO<SimTransform>(self).Position;
            var centre = world.GetComponentRO<SimTransform>(vehicle).Position;
            if (Vector3.Distance(at, centre) > range)
            {
                var point = Embarkation.BoardingPoint(world, vehicle);
                if (p.Walking != 0 && Vector3.Distance(point, p.WalkTo) <= RewalkMetres)
                {
                    var walk = LocomotionMoveTo.Status(world, self);
                    if (walk == NodeStatus.Running) return NodeStatus.Running;
                    p.Walking = 0;
                    if (p.Walks >= MaxWalks) { p.Walks = 0; return NodeStatus.Failure; }   // walked MaxWalks times, still out of reach
                    // ⭐ WS-6 — the walk ended (arrived short, or failed) and the unit is out of reach: walk again (below)
                }
                if (!LocomotionMoveTo.Issue(world, self, point, p.Speed > 0f ? p.Speed : DefaultSpeed, 0.5f))
                {
                    Fdp.Core.Logging.FdpLog<EmbarkNodesLog>.Warn($"Embark: the entity has no LocomotionChannel. (entity {self.Index})");
                    return NodeStatus.Failure;
                }
                p.Walking = 1;
                p.WalkTo = point;
                p.Walks++;
                return NodeStatus.Running;
            }

            EndWalk(ref p, self, world);
            p.Walks = 0;
            ref var channel = ref world.GetComponentRW<InteractionChannel>(self);
            Start(world, self, ref channel, BehaviorConstants.ActionIdEmbark);
            Unsafe.As<byte, EmbarkParams>(ref channel.Params[0]) = new EmbarkParams { VehicleEntity = vehicle, MaxBoardingRange = range, Role = p.Role };
            p.Started = channel.ActionInstanceId;
            return NodeStatus.Running;
        }

        [SharedAiAction]
        public static unsafe NodeStatus Disembark(ref DisembarkNodeParams p, Entity self, EntityRepository world)
        {
            if (p.Started != 0 && OwnsInteraction(world, self, p.Started, BehaviorConstants.ActionIdDisembark))
            {
                ref var running = ref world.GetComponentRW<InteractionChannel>(self);
                if (running.Status == NodeStatus.Running) return NodeStatus.Running;
                var done = running.Status;
                Release(world, self);
                p.Started = 0;
                return done;
            }
            p.Started = 0;
            if (!world.HasComponent<IsEmbarkedTag>(self)) return NodeStatus.Success;   // not aboard: nothing to leave
            if (!world.HasComponent<InteractionChannel>(self))
            {
                Fdp.Core.Logging.FdpLog<EmbarkNodesLog>.Warn($"Disembark: the entity has no InteractionChannel. (entity {self.Index})");
                return NodeStatus.Failure;
            }
            ref var channel = ref world.GetComponentRW<InteractionChannel>(self);
            Start(world, self, ref channel, BehaviorConstants.ActionIdDisembark);
            Unsafe.As<byte, DisembarkParams>(ref channel.Params[0]) = new DisembarkParams { Exit = p.Exit, AnchorIndex = p.AnchorIndex, Landing = p.Landing };   // ⭐ H-1 (D-18)
            p.Started = channel.ActionInstanceId;
            return NodeStatus.Running;
        }

        internal static void Start(EntityRepository world, Entity self, ref InteractionChannel channel, ushort action)
        {
            if (world.HasComponent<BehaviorState>(self)) channel.BehaviorInstanceId = world.GetComponent<BehaviorState>(self).InstanceId;
            unchecked { channel.ActionInstanceId++; if (channel.ActionInstanceId == 0) channel.ActionInstanceId = 1; }
            channel.ActiveAction = action;
            channel.Status = NodeStatus.Running;
        }

        internal static bool OwnsInteraction(EntityRepository world, Entity self, uint started, ushort action)
        {
            if (!world.HasComponent<InteractionChannel>(self)) return false;
            ref readonly var channel = ref world.GetComponentRO<InteractionChannel>(self);
            return channel.ActionInstanceId == started && channel.ActiveAction == action;
        }

        /// <summary>A node hands its channel back when its action ends (no action, a new activation id) — the <c>DoorNodes</c> rule:
        /// a finished action left on the channel would read as the NEXT node's answer.</summary>
        internal static void Release(EntityRepository world, Entity self)
        {
            ref var channel = ref world.GetComponentRW<InteractionChannel>(self);
            channel.ActiveAction = 0;
            unchecked { channel.ActionInstanceId++; if (channel.ActionInstanceId == 0) channel.ActionInstanceId = 1; }
        }

        private static void EndWalk(ref EmbarkNodeParams p, Entity self, EntityRepository world)
        {
            if (p.Walking != 0) LocomotionMoveTo.Stop(world, self);
            p.Walking = 0;
        }
    }
}
