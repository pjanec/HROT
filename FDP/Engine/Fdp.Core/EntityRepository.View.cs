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

        // ⭐⭐ CE-3293 (R-3036; docs/blueprints/DESIGN_Paused_And_Stepped_Execution.md rules 4–5) — THE way a system, and any code a
        //   system calls, reads world events: never Bus.Read*. With no step window open they answer the current frame (the bus
        //   itself, zero copy). While the scheduler runs a step-only system whose last run was before the previous frame, they
        //   answer every event since that run, from the live world's event history — the system does not know.

        private EventAccumulator? _eventHistory;
        private int _windowReader = -1;

        /// <summary>The live world's event history (the kernel attaches it; a snapshot or a bare world has none).</summary>
        public EventAccumulator? EventHistory => _eventHistory;

        /// <summary>Attaches the event history that serves this world's step windows (the kernel's, once).</summary>
        public void AttachEventHistory(EventAccumulator? history) => _eventHistory = history;

        /// <summary>
        /// Opens <paramref name="reader"/>'s step window around one system's run (the scheduler, main thread). A window that is
        /// only the current frame opens nothing — the plain bus read already answers it. Returns the window to restore.
        /// </summary>
        public int OpenStepWindow(int reader)
        {
            int previous = _windowReader;
            _windowReader = _eventHistory != null && reader >= 0 && !_eventHistory.WindowIsCurrentFrame(reader) ? reader : -1;
            return previous;
        }

        /// <summary>Closes the window <see cref="OpenStepWindow"/> opened, restoring <paramref name="previous"/>.</summary>
        public void CloseStepWindow(int previous) => _windowReader = previous;

        /// <summary>Every event of type <typeparamref name="T"/> the running system is owed (rule 5), oldest first.</summary>
        public ReadOnlySpan<T> ReadEvents<T>() where T : unmanaged
            => _windowReader >= 0 ? _eventHistory!.ReadWindow<T>(_windowReader) : Bus.Read<T>();

        /// <summary>The managed twin of <see cref="ReadEvents{T}"/>.</summary>
        public System.Collections.Generic.IReadOnlyList<T> ReadManagedEvents<T>()
            => _windowReader >= 0 ? _eventHistory!.ReadManagedWindow<T>(_windowReader) : Bus.ReadManaged<T>();

        /// <summary>Is there any managed event of type <typeparamref name="T"/> for this reader (rule 4)?</summary>
        public bool HasManagedEvents<T>() => ReadManagedEvents<T>().Count > 0;

        /// <summary>The raw bytes of every event of a type id (the blueprint dispatch, which knows types only by id) — rule 4.</summary>
        public ReadOnlySpan<byte> ReadRawEventsByTypeId(int typeId, out int elementSize)
            => _windowReader >= 0 ? _eventHistory!.ReadRawWindow(_windowReader, typeId, out elementSize) : Bus.ReadRawByTypeId(typeId, out elementSize);

        /// <summary>Is there any event of a type id for this reader — the cheap gate in front of <see cref="ReadRawEventsByTypeId"/>.</summary>
        public bool HasEventsByTypeId(int typeId)
            => _windowReader >= 0 ? _eventHistory!.HasInWindow(_windowReader, typeId, native: true) : Bus.HasEvent(typeId);

        /// <summary>Is there any event of a runtime type for this reader (the replay search and breakpoint scanners) — rule 4.</summary>
        public bool HasEvents(Type eventType)
        {
            if (_windowReader < 0) return Bus.HasEvent(eventType);
            return FdpEventBus.TryGetTypeId(eventType, out int id, out bool native) && _eventHistory!.HasInWindow(_windowReader, id, native);
        }
        
        QueryBuilder ISimulationView.Query()
        {
            return Query();
        }
    }
}
