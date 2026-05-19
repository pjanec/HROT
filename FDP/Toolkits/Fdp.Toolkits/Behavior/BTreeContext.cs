using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Diagnostics;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// Per-entity execution context passed to FastBTree action nodes during <c>Interpreter.Tick</c>.
    /// Carries ECS access (current entity + world) so that node delegates can read/write
    /// components without any managed allocation.
    ///
    /// Stack-allocated once per entity inside <see cref="Systems.BTreeTickSystem.OnUpdate"/> —
    /// zero heap allocation per tick.
    ///
    /// Implements <see cref="ITreeTracer"/> so that the FastBTree interpreter emits structural
    /// trace events via a constrained generic call (JIT devirtualizes the dispatch).
    /// </summary>
    public unsafe struct BTreeContext : IAIContext, ITreeTracer
    {
        /// <summary>The entity whose brain is currently being ticked.</summary>
        public Entity Self;

        /// <summary>
        /// Reference to the ECS world so that node delegates can call
        /// <c>World.GetComponentRW&lt;T&gt;(Self)</c> to read/write components.
        /// </summary>
        public EntityRepository World;

        // ── Time ──────────────────────────────────────────────────────────────────
        internal float _deltaTime;
        internal float _time;
        internal int   _frameCount;

        // ── Blob parameter tables (float/int params defined in the tree asset) ────
        internal float[]? _floatParams;
        internal int[]?   _intParams;

        // ── Diagnostics ──────────────────────────────────────────────────────────
        /// <summary>
        /// Optional pointer to the per-entity unmanaged trace ring buffer.
        /// Null when <c>DebugState.Behavior &amp; EnableTraceBuffer == 0</c>.
        /// Stamped by <c>BTreeTickSystem</c> when constructing the context.
        /// </summary>
        public BTreeTraceWorkingMemory1024* TraceBuffer;

        /// <summary>The <c>BehaviorState.InstanceId</c> of the active brain, copied for
        /// stamping into trace records.</summary>
        internal uint _instanceId;

        // ── IAIContext implementation ──────────────────────────────────────────────
        float IAIContext.DeltaTime   => _deltaTime;
        float IAIContext.Time        => _time;
        int   IAIContext.FrameCount  => _frameCount;

        float IAIContext.GetFloatParam(int index)
            => _floatParams != null && (uint)index < (uint)_floatParams.Length
               ? _floatParams[index] : 0f;

        int IAIContext.GetIntParam(int index)
            => _intParams != null && (uint)index < (uint)_intParams.Length
               ? _intParams[index] : 0;

        // ── Physics/Pathfinding stubs (no-op: real implementations live in
        //    PhysicsQueryActionNode / PathfindingActionNode inside their own toolkits) ──
        int IAIContext.RequestRaycast(Vector3 origin, Vector3 direction, float maxDistance) => -1;
        RaycastResult IAIContext.GetRaycastResult(int requestId) => default;
        int IAIContext.RequestPath(Vector3 from, Vector3 to) => -1;
        PathResult IAIContext.GetPathResult(int requestId) => default;

        // ── ITreeTracer implementation ────────────────────────────────────────────
        // Each call is a single null-check on a ref-struct field; the JIT predicts the
        // null path and emits near-zero overhead when tracing is disabled.

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void TraceNodeEvaluated(int nodeIndex, NodeStatus status)
        {
            if (TraceBuffer != null)
                TraceBuffer->WriteNodeEvaluated(nodeIndex, status, (ushort)_frameCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void TraceScopePushed(ushort newStackDepth)
        {
            if (TraceBuffer != null)
                TraceBuffer->WriteScopePushed(newStackDepth, (ushort)_frameCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void TraceScopePopped(ushort newStackDepth)
        {
            if (TraceBuffer != null)
                TraceBuffer->WriteScopePopped(newStackDepth, (ushort)_frameCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void TraceWaitStarted(int nodeIndex, float duration)
        {
            if (TraceBuffer != null)
                TraceBuffer->WriteWaitStarted(nodeIndex, duration, (ushort)_frameCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void TraceWaitCompleted(int nodeIndex, float duration)
        {
            if (TraceBuffer != null)
                TraceBuffer->WriteWaitCompleted(nodeIndex, duration, (ushort)_frameCount);
        }
    }
}
