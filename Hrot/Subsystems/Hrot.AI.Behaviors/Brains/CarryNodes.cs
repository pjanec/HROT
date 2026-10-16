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
    /// <summary>The params of <see cref="CarryNodes.PickUp"/>: whom, how, how fast to walk to him.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PickUpNodeParams
    {
        /// <summary>The man to pick up (resolved from the behaviour's <c>EntityRef</c> by its resolver).</summary>
        public Entity Casualty;
        /// <summary>Carried or dragged; <see cref="CarryMode.Seated"/> ⇒ dragged.</summary>
        public CarryMode Mode;
        /// <summary>Walking speed to him (m/s); 0 ⇒ <see cref="CarryNodes.DefaultSpeed"/>.</summary>
        public float Speed;
        /// <summary>Runtime: 1 while this node's walk to him is on the locomotion channel — never authored.</summary>
        [JsonIgnore] public byte Walking;
        /// <summary>Runtime: the interaction activation this node started (0 = none) — never authored.</summary>
        [JsonIgnore] public uint Started;
        /// <summary>⭐ <c>CE-2152</c> R-e — runtime: how many walks to him this node issued; never authored.</summary>
        [JsonIgnore] public byte Walks;
    }

    /// <summary>The params of <see cref="CarryNodes.PutDown"/>: nothing authored — the man is the one the unit carries.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PutDownNodeParams
    {
        /// <summary>Runtime: the interaction activation this node started (0 = none) — never authored.</summary>
        [JsonIgnore] public uint Started;
        /// <summary>⭐ <c>CE-2173</c> slice 3c — 1 ⇒ keep him (Success, nothing put down); set by <c>CarryCasualty</c>'s resolver from <c>keep</c>.</summary>
        [JsonIgnore] public byte Skip;
    }

    /// <summary>The params of <see cref="CarryNodes.LoadCasualty"/>: the vehicle to seat the carried man in, how fast to walk to it.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LoadCasualtyNodeParams
    {
        /// <summary>The vehicle (resolved from the behaviour's <c>EntityRef</c> by its resolver).</summary>
        public Entity Vehicle;
        /// <summary>Walking speed to the vehicle (m/s); 0 ⇒ <see cref="EmbarkNodes.DefaultSpeed"/>.</summary>
        public float Speed;
        /// <summary>Runtime: the man being loaded (remembered once put down) — never authored.</summary>
        [JsonIgnore] public Entity Casualty;
        /// <summary>Runtime: 1 while this node's walk to the vehicle is on the locomotion channel — never authored.</summary>
        [JsonIgnore] public byte Walking;
        /// <summary>Runtime: where that walk goes — never authored.</summary>
        [JsonIgnore] public Vector3 WalkTo;
        /// <summary>Runtime: the interaction activation this node started (0 = none) — never authored.</summary>
        [JsonIgnore] public uint Started;
        /// <summary>Runtime: which action <see cref="Started"/> is (put down / embark) — never authored.</summary>
        [JsonIgnore] public ushort StartedAction;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2152</c> C-b (C6, R-2011; 📄 docs/DESIGN_Entity_Interactions.md §7.4.10) — a soldier picks up and puts down a man who is
    /// down. Two shared nodes (BTree, HSM, blueprint) in the shape of <see cref="EmbarkNodes"/>: walk on the locomotion channel, then ONE
    /// action on the interaction channel, and forward its answer once.
    /// <list type="bullet">
    ///   <item><description><see cref="PickUp"/> — walks to the casualty (through <see cref="LocomotionMoveTo"/>) until within
    ///   <see cref="PickUpRange"/>, STOPS the walk (so a following move node does not read its old Success as "arrived"), then runs
    ///   <see cref="PickUpExecutor"/> (action 9). Success = he is carried by this unit.</description></item>
    ///   <item><description><see cref="PutDown"/> — runs <see cref="PutDownExecutor"/> (action 10). Success = carrying nobody.</description></item>
    /// </list>
    /// ⛔ Neither writes embarkation state: <c>EmbarkationSystem</c> on the carrier's owner is the only applier.
    /// </summary>
    public static class CarryNodes
    {
        /// <summary>Walking speed to the casualty when the params give none (m/s).</summary>
        public const float DefaultSpeed = 3f;
        /// <summary>How close the carrier must stand to the casualty to pick him up (m) — <see cref="PickUpExecutor"/>'s reach.</summary>
        public const float PickUpRange = PickUpExecutor.DefaultReach;
        /// <summary>The walk's arrival radius (m): inside <see cref="PickUpRange"/>, short of his body.</summary>
        public const float WalkArrival = 1f;
        /// <summary>⭐ <c>CE-2152</c> R-e — a walk that ended this close to him, still out of reach, is walked again (m).</summary>
        public const float RewalkMetres = 6f;
        /// <summary>⭐ <c>CE-2152</c> R-e — at most this many walks to him per pick-up.</summary>
        public const int MaxWalks = 3;

        [SharedAiAction]
        public static unsafe NodeStatus PickUp(ref PickUpNodeParams p, Entity self, EntityRepository world)
        {
            var casualty = p.Casualty;
            if (!world.IsAlive(casualty) || !Embarkation.IsPersonCarrier(world, self) || !world.HasComponent<SimTransform>(casualty))
            {
                EndWalk(ref p, self, world);
                return NodeStatus.Failure;
            }
            if (world.HasComponent<IsEmbarkedTag>(casualty))
            {
                bool mine = world.GetComponentRO<IsEmbarkedTag>(casualty).VehicleEntity == self;
                if (mine && p.Started != 0 && EmbarkNodes.OwnsInteraction(world, self, p.Started, BehaviorConstants.ActionIdPickUp)) EmbarkNodes.Release(world, self);
                if (mine) { EndWalk(ref p, self, world); p.Started = 0; return NodeStatus.Success; }
                if (p.Started == 0) { EndWalk(ref p, self, world); return NodeStatus.Failure; }   // C-b4: someone else has him
            }
            if (!world.HasComponent<InteractionChannel>(self) || !world.HasComponent<SimTransform>(self)) return NodeStatus.Failure;

            // The pick-up this node started: forward its answer once.
            if (p.Started != 0 && EmbarkNodes.OwnsInteraction(world, self, p.Started, BehaviorConstants.ActionIdPickUp))
            {
                ref var running = ref world.GetComponentRW<InteractionChannel>(self);
                if (running.Status == NodeStatus.Running) return NodeStatus.Running;
                var done = running.Status;
                EmbarkNodes.Release(world, self);
                p.Started = 0;
                return done;
            }
            p.Started = 0;

            var at = world.GetComponentRO<SimTransform>(self).Position;
            var him = world.GetComponentRO<SimTransform>(casualty).Position;
            if (Vector3.Distance(at, him) > PickUpRange)
            {
                if (p.Walking != 0)
                {
                    var walk = LocomotionMoveTo.Status(world, self);
                    if (walk == NodeStatus.Running) return NodeStatus.Running;
                    EndWalk(ref p, self, world);
                    // ⭐ CE-2152 R-e — 📐 measured live (ua-casualty-care): the medic arrived 1.7 m from the man (his body had been
                    //   nudged as he came) and the node gave up for good. Close, and not walked too often ⇒ walk to him again.
                    if (p.Walks >= MaxWalks || Vector3.Distance(at, him) > RewalkMetres) return NodeStatus.Failure;   // still out of reach
                }
                if (!LocomotionMoveTo.Issue(world, self, him, p.Speed > 0f ? p.Speed : DefaultSpeed, WalkArrival)) return NodeStatus.Failure;
                p.Walking = 1;
                p.Walks++;
                return NodeStatus.Running;
            }

            EndWalk(ref p, self, world);
            ref var channel = ref world.GetComponentRW<InteractionChannel>(self);
            EmbarkNodes.Start(world, self, ref channel, BehaviorConstants.ActionIdPickUp);
            Unsafe.As<byte, PickUpParams>(ref channel.Params[0]) = new PickUpParams
            {
                Casualty = casualty,
                Mode = p.Mode == CarryMode.Carried ? CarryMode.Carried : CarryMode.Dragged,   // C-b3
                Reach = PickUpRange,
            };
            p.Started = channel.ActionInstanceId;
            return NodeStatus.Running;
        }

        [SharedAiAction]
        public static NodeStatus PutDown(ref PutDownNodeParams p, Entity self, EntityRepository world)
        {
            if (p.Skip != 0) return NodeStatus.Success;   // ⭐ CE-2173 slice 3c — keep carrying him
            if (p.Started != 0 && EmbarkNodes.OwnsInteraction(world, self, p.Started, BehaviorConstants.ActionIdPutDown))
            {
                ref var running = ref world.GetComponentRW<InteractionChannel>(self);
                if (running.Status == NodeStatus.Running) return NodeStatus.Running;
                var done = running.Status;
                EmbarkNodes.Release(world, self);
                p.Started = 0;
                return done;
            }
            p.Started = 0;
            if (Embarkation.LoadOf(world, self).IsNull) return NodeStatus.Success;   // carrying nobody: nothing to put down
            if (!world.HasComponent<InteractionChannel>(self)) return NodeStatus.Failure;
            ref var channel = ref world.GetComponentRW<InteractionChannel>(self);
            EmbarkNodes.Start(world, self, ref channel, BehaviorConstants.ActionIdPutDown);
            p.Started = channel.ActionInstanceId;
            return NodeStatus.Running;
        }

        /// <summary>
        /// ⭐ <c>CE-2173</c> slice 3 (B10; 📄 docs/designs/air-attack/DESIGN_Air_Operations.md §9) — a soldier carrying a man seats him in
        /// <paramref name="p"/>'s vehicle: walk to its boarding point (the man carried), put him down (action 10), then board HIM (action 1
        /// with <see cref="EmbarkParams.Passenger"/> = the man — <see cref="Embarkation.TryEmbark"/> seats an incapacitated man in a
        /// vehicle). Success = he is aboard the vehicle; carrying nobody to begin with = Success at once. ⭐ Not by boarding while
        /// carrying: that would nest him inside his carrier (measured, Air Operations §1).
        /// </summary>
        [SharedAiAction]
        public static unsafe NodeStatus LoadCasualty(ref LoadCasualtyNodeParams p, Entity self, EntityRepository world)
        {
            var vehicle = p.Vehicle;
            if (!world.IsAlive(vehicle) || !world.HasComponent<PassengerBuffer>(vehicle) || !world.HasComponent<SimTransform>(vehicle))
            {
                EndWalk(ref p, self, world);
                return NodeStatus.Failure;
            }
            if (!world.HasComponent<InteractionChannel>(self) || !world.HasComponent<SimTransform>(self)) return NodeStatus.Failure;

            // The action this node started: forward its answer once.
            if (p.Started != 0 && EmbarkNodes.OwnsInteraction(world, self, p.Started, p.StartedAction))
            {
                ref var running = ref world.GetComponentRW<InteractionChannel>(self);
                if (running.Status == NodeStatus.Running) return NodeStatus.Running;
                var done = running.Status;
                EmbarkNodes.Release(world, self);
                p.Started = 0;
                if (done != NodeStatus.Success) return NodeStatus.Failure;
            }
            p.Started = 0;

            var load = Embarkation.LoadOf(world, self);
            if (!load.IsNull) p.Casualty = load;
            if (p.Casualty.IsNull) { EndWalk(ref p, self, world); return NodeStatus.Success; }   // carrying nobody: nothing to load
            if (!world.IsAlive(p.Casualty)) { EndWalk(ref p, self, world); return NodeStatus.Failure; }
            if (world.HasComponent<IsEmbarkedTag>(p.Casualty) && world.GetComponentRO<IsEmbarkedTag>(p.Casualty).VehicleEntity == vehicle)
            {
                EndWalk(ref p, self, world);
                return NodeStatus.Success;                                                          // seated
            }

            float range = Embarkation.BoardingRange(world, vehicle);
            var at = world.GetComponentRO<SimTransform>(self).Position;
            var centre = world.GetComponentRO<SimTransform>(vehicle).Position;
            if (!load.IsNull && Vector3.Distance(at, centre) > range)
            {
                var point = Embarkation.BoardingPoint(world, vehicle);
                if (p.Walking != 0 && Vector3.Distance(point, p.WalkTo) <= EmbarkNodes.RewalkMetres)
                {
                    if (LocomotionMoveTo.Status(world, self) == NodeStatus.Running) return NodeStatus.Running;
                    p.Walking = 0;
                    return NodeStatus.Failure;   // the walk ended and the vehicle is still out of reach
                }
                if (!LocomotionMoveTo.Issue(world, self, point, p.Speed > 0f ? p.Speed : EmbarkNodes.DefaultSpeed, 0.5f)) return NodeStatus.Failure;
                p.Walking = 1;
                p.WalkTo = point;
                return NodeStatus.Running;
            }

            EndWalk(ref p, self, world);
            ref var channel = ref world.GetComponentRW<InteractionChannel>(self);
            if (!load.IsNull)
            {
                EmbarkNodes.Start(world, self, ref channel, BehaviorConstants.ActionIdPutDown);   // first: put him down beside the vehicle
                p.StartedAction = BehaviorConstants.ActionIdPutDown;
            }
            else
            {
                EmbarkNodes.Start(world, self, ref channel, BehaviorConstants.ActionIdEmbark);    // then: seat him
                Unsafe.As<byte, EmbarkParams>(ref channel.Params[0]) = new EmbarkParams { VehicleEntity = vehicle, MaxBoardingRange = range, Passenger = p.Casualty };
                p.StartedAction = BehaviorConstants.ActionIdEmbark;
            }
            p.Started = channel.ActionInstanceId;
            return NodeStatus.Running;
        }

        private static void EndWalk(ref LoadCasualtyNodeParams p, Entity self, EntityRepository world)
        {
            if (p.Walking != 0) LocomotionMoveTo.Stop(world, self);
            p.Walking = 0;
        }

        /// <summary>Stops this node's walk (a new action instance, so the next move node starts afresh) — the channel is handed back.</summary>
        private static void EndWalk(ref PickUpNodeParams p, Entity self, EntityRepository world)
        {
            if (p.Walking != 0) LocomotionMoveTo.Stop(world, self);
            p.Walking = 0;
        }
    }
}
