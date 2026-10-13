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

        /// <summary>Stops this node's walk (a new action instance, so the next move node starts afresh) — the channel is handed back.</summary>
        private static void EndWalk(ref PickUpNodeParams p, Entity self, EntityRepository world)
        {
            if (p.Walking != 0) LocomotionMoveTo.Stop(world, self);
            p.Walking = 0;
        }
    }
}
