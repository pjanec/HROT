using System.Numerics;
using Fdp.Kernel;
using Fbt;

namespace FDP.Toolkit.Behavior
{
    /// <summary>
    /// Per-entity execution context passed to FastBTree action nodes during <c>Interpreter.Tick</c>.
    /// Carries ECS access (current entity + world) so that node delegates can read/write
    /// components without any managed allocation.
    ///
    /// Stack-allocated once per entity inside <see cref="Systems.BTreeTickSystem.OnUpdate"/> —
    /// zero heap allocation per tick.
    /// </summary>
    public struct BTreeContext : IAIContext
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

        // ── IAIContext implementation ──────────────────────────────────────────────
        float IAIContext.DeltaTime   => _deltaTime;
        float IAIContext.Time        => _time;
        int   IAIContext.FrameCount  => _frameCount;

        // Raycast / pathfinding batching is not implemented in this adapter.
        // Nodes that require these services must be integrated via a separate
        // async request-response system (Phase 3+).
        int           IAIContext.RequestRaycast(Vector3 origin, Vector3 direction, float maxDistance) => -1;
        RaycastResult IAIContext.GetRaycastResult(int requestId)                                       => default;
        int           IAIContext.RequestPath(Vector3 from, Vector3 to)                                => -1;
        PathResult    IAIContext.GetPathResult(int requestId)                                          => default;

        float IAIContext.GetFloatParam(int index)
            => _floatParams != null && (uint)index < (uint)_floatParams.Length
               ? _floatParams[index] : 0f;

        int IAIContext.GetIntParam(int index)
            => _intParams != null && (uint)index < (uint)_intParams.Length
               ? _intParams[index] : 0;
    }
}
