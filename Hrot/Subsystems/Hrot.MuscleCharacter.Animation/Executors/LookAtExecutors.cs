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
    public sealed class LookAtPointExecutor : IActionExecutor<LookAtChannel>
    {
        private readonly IAnimationBackend _backend;

        public LookAtPointExecutor(IAnimationBackend backend)
        {
            _backend = backend;
        }

        public unsafe void OnEnter(Entity entity, ref LookAtChannel channel, EntityRepository world)
        {
            LookAtPointParams p;
            fixed (byte* src = channel.Params)
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

            channel.Status = NodeStatus.Running;
        }

        public void Execute(Entity entity, ref LookAtChannel channel, EntityRepository world, float dt) { }

        public void OnExit(Entity entity, ref LookAtChannel channel, EntityRepository world)
        {
            if (world.HasComponent<LookAtExecutorState>(entity))
            {
                ref var state = ref world.GetComponentRW<LookAtExecutorState>(entity);
                state.TargetType = 0;
            }
        }
    }

    /// <summary>
    /// Executor for LookAtActionIds.LookAtEntity (ANC-P3-02, DD-1 §8).
    /// Stores target entity ID in LookAtExecutorState for resolution by bridge.
    /// </summary>
    public sealed class LookAtEntityExecutor : IActionExecutor<LookAtChannel>
    {
        private readonly IAnimationBackend _backend;

        public LookAtEntityExecutor(IAnimationBackend backend)
        {
            _backend = backend;
        }

        public unsafe void OnEnter(Entity entity, ref LookAtChannel channel, EntityRepository world)
        {
            LookAtEntityParams p;
            fixed (byte* src = channel.Params)
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

            channel.Status = NodeStatus.Running;
        }

        public void Execute(Entity entity, ref LookAtChannel channel, EntityRepository world, float dt) { }

        public void OnExit(Entity entity, ref LookAtChannel channel, EntityRepository world)
        {
            if (world.HasComponent<LookAtExecutorState>(entity))
            {
                ref var state = ref world.GetComponentRW<LookAtExecutorState>(entity);
                state.TargetType = 0;
                state.BlendInWeight = 0f;
            }
        }
    }

    /// <summary>
    /// Executor for LookAtActionIds.ReleaseLook (ANC-P3-02, DD-1 §8).
    /// Sets blend-out intent in LookAtExecutorState.
    /// Does NOT require CanAim capability.
    /// </summary>
    public sealed class ReleaseLookExecutor : IActionExecutor<LookAtChannel>
    {
        private readonly IAnimationBackend _backend;

        public ReleaseLookExecutor(IAnimationBackend backend)
        {
            _backend = backend;
        }

        public unsafe void OnEnter(Entity entity, ref LookAtChannel channel, EntityRepository world)
        {
            ReleaseLookParams p;
            fixed (byte* src = channel.Params)
                p = *(ReleaseLookParams*)src;

            if (world.HasComponent<LookAtExecutorState>(entity))
            {
                ref var state = ref world.GetComponentRW<LookAtExecutorState>(entity);
                state.BlendOutWeight = p.BlendOutTime;
                state.TargetType = 0; // releasing
            }

            channel.Status = NodeStatus.Success;
        }

        public void Execute(Entity entity, ref LookAtChannel channel, EntityRepository world, float dt) { }

        public void OnExit(Entity entity, ref LookAtChannel channel, EntityRepository world) { }
    }
}
