using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Buffers;
using System.Linq;
using System.Threading;

namespace Fdp.Core
{
    /// <summary>
    /// Central event bus for transient, one-frame events.
    /// Supports both unmanaged (Tier 1) and managed (Tier 2) events.
    /// Uses double-buffering: events published in Frame N are consumed in Frame N+1.
    /// </summary>
    public class FdpEventBus : IDisposable, IEventBus
    {
        // Separate dictionaries for native and managed streams
        // ConcurrentDictionary allows lock-free lazy initialization
        private readonly ConcurrentDictionary<int, INativeEventStream> _nativeStreams = new();
        private readonly ConcurrentDictionary<int, object> _managedStreams = new();
        
        // Track active event IDs for current frame (rebuilt during SwapBuffers)
        // READ-ONLY during module execution phase (safe for concurrent access).
        private readonly HashSet<int> _activeEventIds = new();

        // ⭐⭐ CE-3293 S4 (R-3036 rule 9; docs/blueprints/DESIGN_Paused_And_Stepped_Execution.md §1a) — every stream ONCE, in
        //   creation order, walked by index: the swap and the event history touch every stream every frame, and
        //   ConcurrentDictionary.Values builds a new collection on each call. Appended under a lock (stream creation is rare);
        //   read lock-free (the count is published after the slot).
        private readonly object _streamListLock = new();
        private INativeEventStream[] _nativeList = new INativeEventStream[16];
        private int _nativeCount;
        private IManagedEventStream[] _managedList = new IManagedEventStream[16];
        private int _managedCount;

        /// <summary>How many native streams exist (CE-3293 — the event history walks them by index).</summary>
        internal int NativeStreamCount => Volatile.Read(ref _nativeCount);
        /// <summary>The native stream created <paramref name="index"/>-th.</summary>
        internal INativeEventStream NativeStreamAt(int index) => Volatile.Read(ref _nativeList)[index];
        /// <summary>How many managed streams exist.</summary>
        internal int ManagedStreamCount => Volatile.Read(ref _managedCount);
        /// <summary>The managed stream created <paramref name="index"/>-th.</summary>
        internal IManagedEventStream ManagedStreamAt(int index) => Volatile.Read(ref _managedList)[index];

        private void AppendNative(INativeEventStream stream)   // under _streamListLock
        {
            var list = _nativeList;
            if (_nativeCount == list.Length)
            {
                Array.Resize(ref list, list.Length * 2);
                Volatile.Write(ref _nativeList, list);
            }
            list[_nativeCount] = stream;
            Volatile.Write(ref _nativeCount, _nativeCount + 1);
        }

        private void AppendManaged(IManagedEventStream stream)   // under _streamListLock
        {
            var list = _managedList;
            if (_managedCount == list.Length)
            {
                Array.Resize(ref list, list.Length * 2);
                Volatile.Write(ref _managedList, list);
            }
            list[_managedCount] = stream;
            Volatile.Write(ref _managedCount, _managedCount + 1);
        }

        private INativeEventStream AddNativeStream(int typeId, INativeEventStream created)
        {
            lock (_streamListLock)
            {
                if (_nativeStreams.TryGetValue(typeId, out var existing)) return existing;
                _nativeStreams[typeId] = created;
                AppendNative(created);
                return created;
            }
        }

        private bool _disposed;

        /// <summary>
        /// Publishes an unmanaged event to the bus.
        /// Thread-safe: multiple threads can publish concurrently.
        /// Event will be visible in the next frame (after SwapBuffers).
        /// </summary>
        /// <typeparam name="T">Unmanaged event type with [EventId] attribute</typeparam>
        /// <param name="evt">Event to publish</param>
        public void Publish<T>(T evt) where T : unmanaged
        {
            int typeId = EventType<T>.Id;
            if (FdpConfig.EnforceExplicitEventRegistration && !_nativeStreams.ContainsKey(typeId))
            {
                throw new InvalidOperationException(
                    $"Strict Mode Violation: Unmanaged event type '{typeof(T).Name}' (ID: {typeId}) " +
                    "was published without being explicitly registered. " +
                    $"You must call world.RegisterEvent<{typeof(T).Name}>() during bootstrap.");
            }

            var stream = GetOrCreateNativeStream<T>();
            stream.Write(evt);
        }

        /// <summary>
        /// Pre-registers a native event type so that events of this type injected during
        /// playback land in a typed <see cref="NativeEventStream{T}"/> rather than an
        /// <see cref="UntypedNativeEventStream"/>.  Typed streams implement
        /// <see cref="IEventStreamInspector"/> and are therefore visible to export tools.
        /// No-op if the type is already registered.
        /// </summary>
        public void PrepareForNativeEventReplay<T>() where T : unmanaged
        {
            int typeId = EventType<T>.Id;
            if (!_nativeStreams.ContainsKey(typeId)) AddNativeStream(typeId, new NativeEventStream<T>());
        }

        /// <summary>
        /// Publishes a managed event to the bus.
        /// Thread-safe via locking.
        /// Event will be visible in the next frame (after SwapBuffers).
        /// </summary>
        /// <typeparam name="T">Managed event type (class)</typeparam>
        /// <param name="evt">Event to publish</param>
        public void PublishManaged<T>(T evt) // No class constraint — allows managed structs
        {
            int typeId = GetManagedTypeId<T>();
            if (FdpConfig.EnforceExplicitEventRegistration && !_managedStreams.ContainsKey(typeId))
            {
                throw new InvalidOperationException(
                    $"Strict Mode Violation: Managed event type '{typeof(T).Name}' " +
                    "was published without being explicitly registered. " +
                    $"You must call world.RegisterManagedEvent<{typeof(T).Name}>() during bootstrap.");
            }

            var stream = GetOrCreateManagedStream<T>();
            stream.Write(evt);
        }

        /// <summary>
        /// Checks if an unmanaged event of type T exists in the current frame.
        /// </summary>
        public bool HasEvent<T>() where T : unmanaged
        {
            return _activeEventIds.Contains(EventType<T>.Id);
        }

        /// <summary>
        /// ⭐ <c>CE-3047</c> — has this unmanaged event type been registered (so <see cref="Publish{T}"/> is legal under
        /// <c>FdpConfig.EnforceExplicitEventRegistration</c>)? Lets a code path that runs on several hosts publish only
        /// where the event's consumer exists.
        /// </summary>
        public bool IsRegistered<T>() where T : unmanaged => _nativeStreams.ContainsKey(EventType<T>.Id);

        /// <summary>⭐ <c>CE-3035</c> — the managed twin of <see cref="IsRegistered{T}"/>.</summary>
        public bool IsRegisteredManaged<T>() => _managedStreams.ContainsKey(GetManagedTypeId<T>());

        /// <summary>
        /// Checks if a managed event of type T exists in the current frame.
        /// </summary>
        public bool HasManagedEvent<T>() // No class constraint — aligns with PublishManaged
        {
            return _activeEventIds.Contains(GetManagedTypeId<T>());
        }

        private static readonly ConcurrentDictionary<Type, int> _unmanagedEventIdCache = new();

        /// <summary>
        /// Checks if an event of the specified type exists in the current frame.
        /// </summary>
        public bool HasEvent(Type type) => TryGetTypeId(type, out int id, out _) && _activeEventIds.Contains(id);


        /// <summary>
        /// Registers a native event type to ensure the stream exists.
        /// </summary>
        public void Register<T>() where T : unmanaged
        {
            GetOrCreateNativeStream<T>();
        }

        /// <summary>
        /// Explicitly registers a managed event type to ensure the stream exists.
        /// </summary>
        public void RegisterManaged<T>()
        {
            if (FdpConfig.EnforceExplicitEventRegistration)
            {
                FlightRecorder.FdpAutoSerializer.EnsureCompiled<T>();
            }
            GetOrCreateManagedStream<T>();
        }

        /// <summary>
        /// Returns the CLR types of all currently registered managed event streams. A stream is
        /// registered when <see cref="RegisterManaged{T}"/> is called or on the first
        /// <see cref="PublishManaged{T}"/> for that type — so a managed type never registered/published
        /// is absent. Thread-safe (ConcurrentDictionary snapshot). Ported from the AI-debug API.
        /// </summary>
        public IReadOnlyList<Type> GetRegisteredManagedEventTypes()
        {
            var types = new List<Type>();
            foreach (var streamObj in _managedStreams.Values)
            {
                if (streamObj is IManagedEventStreamInfo info)
                    types.Add(info.EventType);
            }
            return types;
        }

        /// <summary>
        /// Publishes a raw event to the native stream.
        /// Used by EntityCommandBuffer playback.
        /// </summary>
        public unsafe void PublishRaw(int typeId, void* data)
        {
            if (_nativeStreams.TryGetValue(typeId, out var stream))
            {
                stream.WriteRaw(data);
            }
            else
            {
                throw new InvalidOperationException($"Event type {typeId} not registered via RegisterEvent<T>(). Cannot playback command.");
            }
        }

        /// <summary>
        /// Publishes a managed event using object reference (type-erased).
        /// Used by EntityCommandBuffer playback.
        /// </summary>
        public void PublishManagedRaw(int typeId, object evt)
        {
            if (_managedStreams.TryGetValue(typeId, out var stream))
            {
                if (stream is IManagedEventStream managedStream)
                {
                    managedStream.WriteRaw(evt);
                }
                else
                {
                    // Should not happen for valid streams, but fallback
                    throw new InvalidOperationException($"Stream for type {typeId} does not implement IManagedEventStream");
                }
            }
            else
            {
                 // Try to resolve type from object if possible, but typeId lookup failed.
                 // We require registration just like native events.
                 throw new InvalidOperationException($"Managed event type {typeId} not registered. Cannot playback command.");
            }
        }

        /// <summary>
        /// Consumes all events of type T from the previous frame.
        /// Returns empty span if no events were published.
        /// This is how systems "subscribe" to events - by calling Consume in their Update.
        /// </summary>
        /// <typeparam name="T">Unmanaged event type</typeparam>
        /// <returns>Read-only span of events from previous frame</returns>
        public unsafe ReadOnlySpan<T> Read<T>() where T : unmanaged
        {
            if (_nativeStreams.TryGetValue(EventType<T>.Id, out var stream))
            {
                // Check if it's a typed stream (normal case)
                if (stream is NativeEventStream<T> typedStream)
                {
                    return typedStream.Read();
                }
                
                // Otherwise it's an untyped stream (replay case)
                // Read raw bytes and reinterpret as T*
                var rawBytes = stream.GetRawBytes();
                fixed (byte* ptr = rawBytes)
                {
                    return new ReadOnlySpan<T>(ptr, rawBytes.Length / sizeof(T));
                }
            }
            return ReadOnlySpan<T>.Empty;
        }

        /// <summary>
        /// Q#14 (custom-event dispatch): reads the raw bytes of the native event stream for
        /// <paramref name="typeId"/> — the previous frame's buffer, exactly like <see cref="Read{T}"/> —
        /// for callers that only know the event type at runtime (the blueprint custom-event dispatch pump,
        /// which resolves subscribers by type-id). Purely additive read of data the bus already holds.
        /// <paramref name="elementSize"/> is the per-event byte size (0 when the stream is absent/empty), so
        /// callers can slice individual events out of the returned span.
        /// </summary>
        public ReadOnlySpan<byte> ReadRawByTypeId(int typeId, out int elementSize)
        {
            if (_nativeStreams.TryGetValue(typeId, out var stream))
            {
                elementSize = stream.ElementSize;
                return stream.GetRawBytes();
            }
            elementSize = 0;
            return ReadOnlySpan<byte>.Empty;
        }

        /// <summary>True when a native event of the given <paramref name="typeId"/> is present this frame.</summary>
        public bool HasEvent(int typeId) => _activeEventIds.Contains(typeId);

        /// <summary>
        /// Consumes all managed events of type T from the previous frame.
        /// Returns empty list if no events were published.
        /// </summary>
        /// <typeparam name="T">Managed event type</typeparam>
        /// <returns>Read-only list of events from previous frame</returns>
        public IReadOnlyList<T> ReadManaged<T>() // No class constraint — allows managed structs
        {
            if (_managedStreams.TryGetValue(GetManagedTypeId<T>(), out var stream))
            {
                return ((ManagedEventStream<T>)stream).Read();
            }
            return Array.Empty<T>();
        }

        /// <summary>
        /// Swaps all event buffers.
        /// MUST be called at the end of each frame (PostSimulation phase).
        /// After swap:
        /// - Events from current frame become readable
        /// - Previous frame's events are cleared
        /// </summary>
        /// <summary>
        /// Swaps all event buffers.
        /// MUST be called at the end of each frame (PostSimulation phase).
        /// Rebuilds the _activeEventIds set for fast lookups in the next frame.
        /// </summary>
        public void SwapBuffers()
        {
            _activeEventIds.Clear();

            // ⭐ CE-3293 S4 — by index, not ConcurrentDictionary.Values (which allocates every call)
            var natives = Volatile.Read(ref _nativeList);
            int nativeCount = Volatile.Read(ref _nativeCount);
            for (int i = 0; i < nativeCount; i++)
            {
                var stream = natives[i];
                stream.Swap();
                if (stream.GetRawBytes().Length > 0)
                    _activeEventIds.Add(stream.EventTypeId);
            }

            var manageds = Volatile.Read(ref _managedList);
            int managedCount = Volatile.Read(ref _managedCount);
            for (int i = 0; i < managedCount; i++)
            {
                var stream = manageds[i];
                stream.Swap();
                if (stream.ReadCount > 0)
                    _activeEventIds.Add(stream.TypeId);
            }
        }

        /// <summary>
        /// Returns all active native event streams.
        /// Used by recorder for serialization (future feature).
        /// </summary>
        public IEnumerable<INativeEventStream> GetAllActiveStreams()
        {
            return _nativeStreams.Values;
        }

        // ========== FLIGHT RECORDER INTEGRATION ==========

        /// <summary>
        /// Returns all active native event streams that have pending events.
        /// Used by Flight Recorder during PostSimulation to capture events.
        /// </summary>
        public IEnumerable<INativeEventStream> GetAllPendingStreams()
        {
            foreach (var stream in _nativeStreams.Values)
            {
                // Only return streams that have data in their pending (write) buffer
                if (stream.GetPendingBytes().Length > 0)
                {
                    yield return stream;
                }
            }
        }

        /// <summary>
        /// Gets debug inspectors for all active event streams.
        /// Use this to populate an Event Inspector UI.
        /// </summary>
        public IEnumerable<IEventStreamInspector> GetDebugInspectors()
        {
            // 1. Native Streams
            foreach (var stream in _nativeStreams.Values)
            {
                if (stream is IEventStreamInspector inspector)
                    yield return inspector;
            }

            // 2. Managed Streams
            foreach (var stream in _managedStreams.Values)
            {
                if (stream is IEventStreamInspector inspector)
                    yield return inspector;
            }
        }

        /// <summary>
        /// Clears all Current (read) buffers.
        /// Must be called before injecting replay data to prevent mixing old/new events.
        /// </summary>
        public void ClearCurrentBuffers()
        {
            // Also clear the active-event lookup so HasEvent is consistent with
            // the cleared buffers (important for playback: new frame starts clean).
            _activeEventIds.Clear();

            var natives = Volatile.Read(ref _nativeList);
            int nativeCount = Volatile.Read(ref _nativeCount);
            for (int i = 0; i < nativeCount; i++) natives[i].ClearCurrent();

            var manageds = Volatile.Read(ref _managedList);
            int managedCount = Volatile.Read(ref _managedCount);
            for (int i = 0; i < managedCount; i++) manageds[i].ClearCurrent();
        }

        /// <summary>
        /// Injects event data directly into the Current (read) buffer of a specific event type.
        /// Used by Flight Recorder during replay to bypass normal Publish/Swap flow.
        /// NOTE: The event type must have been registered by calling Publish<T>() at least once,
        /// or the stream won't exist and the data will be silently ignored.
        /// </summary>
        /// <param name="typeId">Event type ID</param>
        /// <param name="data">Raw event bytes from recording</param>
        public void InjectIntoCurrent(int typeId, ReadOnlySpan<byte> data)
        {
            if (_nativeStreams.TryGetValue(typeId, out var stream))
            {
                stream.InjectIntoCurrent(data);
                // Mark event type as active so HasEvent returns true after injection.
                _activeEventIds.Add(typeId);
            }
            else
            {
                // Stream doesn't exist - this event type was never published on replay side
                // We can't create it without knowing the generic type T
                // This is expected behavior: if you want to replay events, you must
                // call Publish<EventType>() at least once before playback to register the stream
                //
                // Alternative: Maintain a type registry, but that's more complex
                // For now, document this requirement
            }
        }

        /// <summary>
        /// Injects event data with element size - creates stream dynamically if needed.
        /// This is the CORRECT way to inject during replay - NO pre-registration required!
        /// </summary>
        /// <param name="typeId">Event type ID</param>
        /// <param name="elementSize">Size of each event in bytes</param>
        /// <param name="data">Raw event bytes from recording</param>
        public void InjectIntoCurrentBySize(int typeId, int elementSize, ReadOnlySpan<byte> data)
        {
            // Try to get existing stream
            if (_nativeStreams.TryGetValue(typeId, out var stream))
            {
                // Stream exists - verify size matches
                if (stream.ElementSize != elementSize)
                {
                    throw new InvalidOperationException(
                        $"Element size mismatch for event {typeId}: " +
                        $"stream has {stream.ElementSize}, data has {elementSize}");
                }
                
                stream.InjectIntoCurrent(data);
            }
            else
            {
                // Stream doesn't exist - create untyped stream dynamically
                AddNativeStream(typeId, new UntypedNativeEventStream(typeId, elementSize)).InjectIntoCurrent(data);
            }

            // Mark event type as active so HasEvent returns true immediately after injection.
            _activeEventIds.Add(typeId);
        }

        /// <summary>
        /// Gets or creates a native event stream for type T.
        /// Thread-safe via ConcurrentDictionary.
        /// </summary>
        private NativeEventStream<T> GetOrCreateNativeStream<T>() where T : unmanaged
        {
            int typeId = EventType<T>.Id; // Validates [EventId] attribute

            if (_nativeStreams.TryGetValue(typeId, out var stream)) return (NativeEventStream<T>)stream;
            return (NativeEventStream<T>)AddNativeStream(typeId, new NativeEventStream<T>());
        }

        /// <summary>
        /// Gets or creates a managed event stream for type T.
        /// Thread-safe via ConcurrentDictionary.
        /// </summary>
        private ManagedEventStream<T> GetOrCreateManagedStream<T>() // No class constraint
        {
            int typeId = GetManagedTypeId<T>();

            if (_managedStreams.TryGetValue(typeId, out var stream)) return (ManagedEventStream<T>)stream;
            lock (_streamListLock)
            {
                if (_managedStreams.TryGetValue(typeId, out stream)) return (ManagedEventStream<T>)stream;
                var created = new ManagedEventStream<T>();
                _managedStreams[typeId] = created;
                AppendManaged(created);
                return created;
            }
        }

        /// <summary>
        /// Gets a stable type ID for managed types: the FNV-1a hash of the full type name (<see cref="StableHash"/>).
        /// ⛔ It was <c>string.GetHashCode()</c>, which is randomised per process — "stable across sessions" was not true (CE-3302).
        /// </summary>
        private int GetManagedTypeId<T>() => ManagedTypeId<T>.Id;

        /// <summary>
        /// The stable id of a managed event type: the hash of its full name (simpler than requiring [EventId] on managed types).
        /// Cached per type — CE-3293 S4: it is read on every swap.
        /// </summary>
        internal static class ManagedTypeId<T>
        {
            public static readonly int Id = StableHash.NonNegativeId(typeof(T).FullName!);   // ⭐ CE-3302 — the same in every process
        }

        /// <summary>
        /// ⭐ CE-3293 S4 — the bus type id of a runtime event type: <c>EventType&lt;T&gt;.Id</c> for a value type with an id, else
        /// the managed full-name hash (the same answer <see cref="HasEvent(Type)"/> uses). False when a value type has no id.
        /// </summary>
        internal static bool TryGetTypeId(Type type, out int id, out bool native)
        {
            native = type.IsValueType;
            if (!native) { id = StableHash.NonNegativeId(type.FullName!); return true; }
            if (_unmanagedEventIdCache.TryGetValue(type, out id)) return true;
            try
            {
                var idField = typeof(EventType<>).MakeGenericType(type)
                    .GetField("Id", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                id = (int)idField!.GetValue(null)!;
                _unmanagedEventIdCache[type] = id;
                return true;
            }
            catch
            {
                id = 0;
                return false;
            }
        }

        /// <summary>
        /// ⭐ CE-3293 S4 — appends a typed list to <typeparamref name="T"/>'s READ buffer (the event history's flush into a
        /// background module's snapshot): no reflection, no boxing. Creates the stream if this bus has none yet.
        /// </summary>
        internal void InjectManagedIntoCurrent<T>(List<T> events)
        {
            GetOrCreateManagedStream<T>().InjectIntoCurrent(events);
            if (events.Count > 0) _activeEventIds.Add(ManagedTypeId<T>.Id);
        }

        /// <summary>
        /// Disposes the event bus and all streams.
        /// </summary>
        /// <summary>
        /// Populates the provided list with active native event streams that have pending events.
        /// Zero-allocation if list capacity is sufficient.
        /// </summary>
        public void PopulatePendingStreams(List<INativeEventStream> target)
        {
            target.Clear();
            var natives = Volatile.Read(ref _nativeList);
            int count = Volatile.Read(ref _nativeCount);
            for (int i = 0; i < count; i++)
                if (natives[i].GetPendingBytes().Length > 0) target.Add(natives[i]);
        }
        
        /// <summary>
        /// Populates the provided list with active managed event streams that have pending events.
        /// Zero-allocation if list capacity is sufficient.
        /// </summary>
        public void PopulatePendingManagedStreams(List<IManagedEventStreamInfo> target)
        {
            target.Clear();
            var manageds = Volatile.Read(ref _managedList);
            int count = Volatile.Read(ref _managedCount);
            for (int i = 0; i < count; i++)
                if (manageds[i] is IManagedEventStreamInfo info && info.PendingEvents.Count > 0) target.Add(info);
        }

        /// <summary>
        /// Populates the provided list with active native event streams that have Current events.
        /// Current = read buffer (populated after SwapBuffers).
        /// Zero-allocation if list capacity is sufficient.
        /// </summary>
        public void PopulateCurrentStreams(List<INativeEventStream> target)
        {
            target.Clear();
            var natives = Volatile.Read(ref _nativeList);
            int count = Volatile.Read(ref _nativeCount);
            for (int i = 0; i < count; i++)
                if (natives[i].GetRawBytes().Length > 0) target.Add(natives[i]);
        }

        /// <summary>
        /// Populates the provided list with active managed event streams that have Current events.
        /// Current = front buffer (populated after SwapBuffers).
        /// Zero-allocation if list capacity is sufficient.
        /// </summary>
        public void PopulateCurrentManagedStreams(List<IManagedEventStreamInfo> target)
        {
            target.Clear();
            var manageds = Volatile.Read(ref _managedList);
            int count = Volatile.Read(ref _managedCount);
            for (int i = 0; i < count; i++)
                if (manageds[i] is IManagedEventStreamInfo info && info.CurrentEvents.Count > 0) target.Add(info);
        }
        
        /// <summary>
        /// Injects managed events into the current buffer.
        /// </summary>
        public void InjectManagedIntoCurrent(int typeId, Type eventType, IReadOnlyList<object> events)
        {
            if (eventType == null) return;
            
            // Dynamic dispatch to helper
            var method = typeof(FdpEventBus).GetMethod(nameof(InjectManagedInternal), 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .MakeGenericMethod(eventType);
            
            method.Invoke(this, new object[] { typeId, events });
        }
        
        private void InjectManagedInternal<T>(int typeId, IReadOnlyList<object> events) // No class constraint
        {
            var stream = GetOrCreateManagedStream<T>(); // Ensure stream exists
            // Verify ID matches? (should match hash)
            
            // Use internal access to inject
            stream.InjectIntoCurrent(events.Cast<T>());
        }

        /// <summary>
        /// Clears all buffers (Read and Write) and resets active event state.
        /// Used when resetting an EntityRepository for reuse in a pool.
        /// </summary>
        public void ClearAll()
        {
            _activeEventIds.Clear();
            
            // ⭐ CE-3293 S4 — by index. Clear the read buffer, swap, clear again: both halves of each double buffer.
            var natives = Volatile.Read(ref _nativeList);
            int nativeCount = Volatile.Read(ref _nativeCount);
            for (int i = 0; i < nativeCount; i++)
            {
                natives[i].ClearCurrent();
                natives[i].Swap();
                natives[i].ClearCurrent();
            }

            var manageds = Volatile.Read(ref _managedList);
            int managedCount = Volatile.Read(ref _managedCount);
            for (int i = 0; i < managedCount; i++)
            {
                manageds[i].ClearCurrent();
                manageds[i].Swap();
                manageds[i].ClearCurrent();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            // Dispose native streams
            foreach (var stream in _nativeStreams.Values)
            {
                if (stream is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }

            _nativeStreams.Clear();
            _managedStreams.Clear();
            lock (_streamListLock)
            {
                Volatile.Write(ref _nativeCount, 0);
                Volatile.Write(ref _managedCount, 0);
            }

            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
    

}
