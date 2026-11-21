using System.Runtime.CompilerServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Executors;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;

namespace Hrot.MuscleCharacter.Animation.Executors
{
    /// <summary>
    /// Executor for LookAtActionIds.LookAtPoint (ANC-P3-02, DD-1 §8).
    /// Stages look-at point intent in LookAtExecutorState for bridge to apply.
    /// </summary>
    public sealed class LookAtPointExecutor : IActionExecutor<LookAtChannelWork>
    {
        private readonly IAnimationBackend _backend;

        public LookAtPointExecutor(IAnimationBackend backend)
        {
            _backend = backend;
        }

        public unsafe void OnEnter(Entity entity, ref LookAtChannelWork channel, EntityRepository world)
        {
            LookAtPointParams p;
            fixed (byte* src = channel.Request.Params)
                p = *(LookAtPointParams*)src;

            if (world.HasComponent<LookAtExecutorState>(entity))
            {
                ref var state = ref world.GetComponentRW<LookAtExecutorState>(entity);
                state.TargetPointX = p.WorldPointX;
                state.TargetPointY = p.WorldPointY;
                state.TargetPointZ = p.WorldPointZ;
                state.BlendInWeight = 0f;
                state.BlendOutWeight = 1f;
                state.TargetType = 1; // point
            }
            // ⭐ CE-3252 (LD-E) — the wanted look, for the gait, LookFollowSystem, vision and the wire.
            Fdp.Toolkit.Perception.Look.LookOf.Want(world, entity, new System.Numerics.Vector3(p.WorldPointX, p.WorldPointY, p.WorldPointZ));

            channel.Report.Status = NodeStatus.Running;
        }

        public void Execute(Entity entity, ref LookAtChannelWork channel, EntityRepository world, float dt) { }

        public void OnExit(Entity entity, ref LookAtChannelWork channel, EntityRepository world)
        {
            if (world.HasComponent<LookAtExecutorState>(entity))
            {
                ref var state = ref world.GetComponentRW<LookAtExecutorState>(entity);
                state.TargetType = 0;
            }
            Fdp.Toolkit.Perception.Look.LookOf.Release(world, entity);   // ⭐ CE-3252
        }
    }

    /// <summary>
    /// Executor for LookAtActionIds.LookAtEntity (ANC-P3-02, DD-1 §8).
    /// Stores target entity ID in LookAtExecutorState for resolution by bridge.
    /// </summary>
    public sealed class LookAtEntityExecutor : IActionExecutor<LookAtChannelWork>
    {
        private readonly IAnimationBackend _backend;

        public LookAtEntityExecutor(IAnimationBackend backend)
        {
            _backend = backend;
        }

        public unsafe void OnEnter(Entity entity, ref LookAtChannelWork channel, EntityRepository world)
        {
            LookAtEntityParams p;
            fixed (byte* src = channel.Request.Params)
                p = *(LookAtEntityParams*)src;

            if (world.HasComponent<LookAtExecutorState>(entity))
            {
                ref var state = ref world.GetComponentRW<LookAtExecutorState>(entity);
                // Store target entity ID packed into TargetPointX as uint (reused field)
                state.TargetPointX = System.Runtime.CompilerServices.Unsafe.BitCast<uint, float>(p.TargetEntityId);
                state.BlendInWeight = 0f;
                state.BlendOutWeight = 1f;
                state.TargetType = 2; // entity
            }
            // ⭐ CE-3252 (LD-E) — follow the entity (its index is THIS node's: the intent ingress remapped it from the network id).
            var target = p.TargetEntityId <= int.MaxValue ? world.GetEntityByIndex((int)p.TargetEntityId) : Entity.Null;
            if (!target.IsNull && world.IsAlive(target))
                Fdp.Toolkit.Perception.Look.LookOf.Want(world, entity, default, target);

            channel.Report.Status = NodeStatus.Running;
        }

        public void Execute(Entity entity, ref LookAtChannelWork channel, EntityRepository world, float dt) { }

        public void OnExit(Entity entity, ref LookAtChannelWork channel, EntityRepository world)
        {
            if (world.HasComponent<LookAtExecutorState>(entity))
            {
                ref var state = ref world.GetComponentRW<LookAtExecutorState>(entity);
                state.TargetType = 0;
                state.BlendInWeight = 0f;
            }
            Fdp.Toolkit.Perception.Look.LookOf.Release(world, entity);   // ⭐ CE-3252
        }
    }

    /// <summary>
    /// Executor for LookAtActionIds.ReleaseLook (ANC-P3-02, DD-1 §8).
    /// Sets blend-out intent in LookAtExecutorState.
    /// Does NOT require CanAim capability.
    /// </summary>
    public sealed class ReleaseLookExecutor : IActionExecutor<LookAtChannelWork>
    {
        private readonly IAnimationBackend _backend;

        public ReleaseLookExecutor(IAnimationBackend backend)
        {
            _backend = backend;
        }

        public unsafe void OnEnter(Entity entity, ref LookAtChannelWork channel, EntityRepository world)
        {
            ReleaseLookParams p;
            fixed (byte* src = channel.Request.Params)
                p = *(ReleaseLookParams*)src;

            if (world.HasComponent<LookAtExecutorState>(entity))
            {
                ref var state = ref world.GetComponentRW<LookAtExecutorState>(entity);
                state.BlendOutWeight = p.BlendOutTime;
                state.TargetType = 0; // releasing
            }
            Fdp.Toolkit.Perception.Look.LookOf.Release(world, entity);   // ⭐ CE-3252 — the head returns to the body

            channel.Report.Status = NodeStatus.Success;
        }

        public void Execute(Entity entity, ref LookAtChannelWork channel, EntityRepository world, float dt) { }

        public void OnExit(Entity entity, ref LookAtChannelWork channel, EntityRepository world) { }
    }
}
