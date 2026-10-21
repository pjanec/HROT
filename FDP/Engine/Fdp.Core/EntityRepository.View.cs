using System;
using System.Threading;
using Fdp.Core;
using Fdp.Core.Internal;
using Fdp.ModuleHost.Abstractions;
using Fdp.Interfaces;

namespace Fdp.Core
{
    public sealed partial class EntityRepository : ISimulationView
    {
        // Thread-local command buffer for modules
        internal readonly ThreadLocal<EntityCommandBuffer> _perThreadCommandBuffer = new(() => new EntityCommandBuffer(), trackAllValues: true);

        // Optional override for tests and advanced scenarios.
        // When set, GetCommandBuffer() returns this instead of the per-thread buffer.
        private IEntityCommandBuffer? _commandBufferOverride;

        /// <summary>
        /// Overrides the command buffer returned by GetCommandBuffer().
        /// Pass null to restore the default per-thread buffer.
        /// Intended for test fixtures that need EAGER entity creation semantics.
        /// </summary>
        public void SetCommandBufferOverride(IEntityCommandBuffer? ecb) => _commandBufferOverride = ecb;

        // Properties
        uint ISimulationView.Tick => _simulationTick;
        
        float ISimulationView.Time => _simulationTime;
        
        // Methods
        
        IEntityCommandBuffer ISimulationView.GetCommandBuffer()
        {
            return _commandBufferOverride ?? _perThreadCommandBuffer.Value!;
        }

        /// <summary>
        /// Plays back all pending per-thread command buffer operations into the repository.
        /// Call at the sync phase, after simulation systems have finished recording deferred ops.
        /// In production the scheduler calls this; in tests call it from TickFrame.
        /// </summary>
        public void FlushCommandBuffers()
        {
            foreach (var buffer in _perThreadCommandBuffer.Values)
                buffer.Playback(this);
        }

        ref readonly T ISimulationView.GetComponentRO<T>(Entity e)
        {
            // Delegate to existing internal methods via UnsafeShim or direct if accessible
            // Since we are in EntityRepository, we can call internal methods directly if we know them.
            // But GetComponentRO logic differs for managed/unmanaged.
            // ISimulationView splits them.
            
            // For unmanaged T:
            return ref GetUnmanagedComponentRO<T>(e);
        }
        
        /// <remarks>
        /// The diagnostic on the null path is load-bearing, not noise: <c>Has=true</c> with a null
        /// payload means the repository's presence bits and its managed storage have diverged, and
        /// naming both halves is what made HN-001 (the preview-rewind crash) diagnosable at all.
        /// It goes through the logger rather than <c>Console.WriteLine</c> so a host that captures
        /// its output — the editor, a test harness, a headless runner — actually receives it.
        /// </remarks>
        T ISimulationView.GetManagedComponentRO<T>(Entity e)
        {
            // Call internal method directly
            var val = GetManagedComponentRO<T>(e);
            if (val == null)
            {
                bool has = HasManagedComponent<T>(e);
                Logging.FdpLog<EntityRepository>.Error(
                    $"Entity {e} GetManagedComponentRO<{typeof(T).Name}> returned null, but Has={has}. Idx={e.Index}");
                throw new InvalidOperationException($"Entity {e} missing component {typeof(T).Name}");
            }
            return val;
        }
        
        bool ISimulationView.IsAlive(Entity e)
        {
            return IsAlive(e);
        }

        bool ISimulationView.HasComponent<T>(Entity e)
        {
            return HasUnmanagedComponent<T>(e);
        }

        bool ISimulationView.HasManagedComponent<T>(Entity e)
        {
            return HasManagedComponent<T>(e);
        }
        
        ReadOnlySpan<T> ISimulationView.ReadEvents<T>() => ReadEvents<T>();

        System.Collections.Generic.IReadOnlyList<T> ISimulationView.ReadManagedEvents<T>() => ReadManagedEvents<T>();

        // ⭐⭐ CE-3293 S3 (R-3036; docs/blueprints/DESIGN_Paused_And_Stepped_Execution.md rule 4) — THE way a system, and any code a
        //   system calls, reads world events: never Bus.Read*. Today these answer the current frame; from S4/S5 they answer every
        //   event since the calling system's last run (the step window) — which is why no system may go around them.

        /// <summary>Every event of type <typeparamref name="T"/> this world's reader is owed (rule 4; today: this frame's).</summary>
        public ReadOnlySpan<T> ReadEvents<T>() where T : unmanaged => Bus.Read<T>();

        /// <summary>The managed twin of <see cref="ReadEvents{T}"/>.</summary>
        public System.Collections.Generic.IReadOnlyList<T> ReadManagedEvents<T>() => Bus.ReadManaged<T>();

        /// <summary>Is there any managed event of type <typeparamref name="T"/> for this reader (rule 4)?</summary>
        public bool HasManagedEvents<T>() => ReadManagedEvents<T>().Count > 0;

        /// <summary>The raw bytes of every event of a type id (the blueprint dispatch, which knows types only by id) — rule 4.</summary>
        public ReadOnlySpan<byte> ReadRawEventsByTypeId(int typeId, out int elementSize) => Bus.ReadRawByTypeId(typeId, out elementSize);

        /// <summary>Is there any event of a type id for this reader — the cheap gate in front of <see cref="ReadRawEventsByTypeId"/>.</summary>
        public bool HasEventsByTypeId(int typeId) => Bus.HasEvent(typeId);

        /// <summary>Is there any event of a runtime type for this reader (the replay search and breakpoint scanners) — rule 4.</summary>
        public bool HasEvents(Type eventType) => Bus.HasEvent(eventType);
        
        QueryBuilder ISimulationView.Query()
        {
            return Query();
        }
    }
}
