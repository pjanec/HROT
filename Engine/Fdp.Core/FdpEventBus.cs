using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Buffers;
using System.Linq;

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
            _nativeStreams.GetOrAdd(typeId, _ => new NativeEventStream<T>());
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
        public bool HasEvent(Type type)
        {
            if (type.IsValueType)
            {
                if (!_unmanagedEventIdCache.TryGetValue(type, out int id))
                {
                    // Slow path: Reflection to get EventType<T>.Id
                    // We assume the type has [EventId] attribute or is a valid event type using Fdp.Core event system.
                    try 
                    {
                        var eventTypeGeneric = typeof(EventType<>).MakeGenericType(type);
                        var idField = eventTypeGeneric.GetField("Id", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        id = (int)idField!.GetValue(null)!;
                        _unmanagedEventIdCache[type] = id;
                    }
                    catch
                    {
                        // Fallback or error?
                        return false;
                    }
                }
                return _activeEventIds.Contains(id);
            }
            else
            {
                return _activeEventIds.Contains(type.FullName!.GetHashCode() & 0x7FFFFFFF);
            }
        }


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
            GetOrCreateManagedStream<T>();
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

            // Swap native streams
            foreach (var stream in _nativeStreams.Values)
            {
                stream.Swap();
                if (stream.GetRawBytes().Length > 0)
                {
                    _activeEventIds.Add(stream.EventTypeId);
                }
            }

            // Swap managed streams
            foreach (var streamObj in _managedStreams.Values)
            {
                if (streamObj is IManagedEventStream managedStream)
                {
                    managedStream.Swap();
                    
                    if (streamObj is IEventStreamInspector inspector && inspector.Count > 0)
                    {
                        if (streamObj is IManagedEventStreamInfo info)
                        {
                            _activeEventIds.Add(info.TypeId);
                        }
                    }
                }
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

            // Clear native streams
            foreach (var stream in _nativeStreams.Values)
            {
                stream.ClearCurrent();
            }
            
            // Clear managed streams
            foreach (var streamObj in _managedStreams.Values)
            {
                if (streamObj is IManagedEventStream managedStream)
                {
                    managedStream.ClearCurrent();
                }
            }
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
                var untypedStream = new UntypedNativeEventStream(typeId, elementSize);
                _nativeStreams.TryAdd(typeId, untypedStream);
                untypedStream.InjectIntoCurrent(data);
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
            
            var stream = _nativeStreams.GetOrAdd(typeId, _ => new NativeEventStream<T>());
            return (NativeEventStream<T>)stream;
        }

        /// <summary>
        /// Gets or creates a managed event stream for type T.
        /// Thread-safe via ConcurrentDictionary.
        /// </summary>
        private ManagedEventStream<T> GetOrCreateManagedStream<T>() // No class constraint
        {
            int typeId = GetManagedTypeId<T>();
            
            var stream = _managedStreams.GetOrAdd(typeId, _ => new ManagedEventStream<T>());
            return (ManagedEventStream<T>)stream;
        }

        /// <summary>
        /// Gets a stable type ID for managed types.
        /// Uses hash of full type name (stable across sessions).
        /// </summary>
        private int GetManagedTypeId<T>()
        {
            // Use hash of full name for stable ID
            // Note: This is simpler than requiring [EventId] on managed types
            return typeof(T).FullName!.GetHashCode() & 0x7FFFFFFF;
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
            foreach (var kvp in _nativeStreams)
            {
                var stream = kvp.Value;
                if (stream.GetPendingBytes().Length > 0)
                {
                    target.Add(stream);
                }
            }
        }
        
        /// <summary>
        /// Populates the provided list with active managed event streams that have pending events.
        /// Zero-allocation if list capacity is sufficient.
        /// </summary>
        public void PopulatePendingManagedStreams(List<IManagedEventStreamInfo> target)
        {
            target.Clear();
            foreach (var kvp in _managedStreams)
            {
                var streamObj = kvp.Value;
                if (streamObj is IManagedEventStreamInfo info && info.PendingEvents.Count > 0)
                {
                    target.Add(info);
                }
            }
        }

        /// <summary>
        /// Populates the provided list with active native event streams that have Current events.
        /// Current = read buffer (populated after SwapBuffers).
        /// Zero-allocation if list capacity is sufficient.
        /// </summary>
        public void PopulateCurrentStreams(List<INativeEventStream> target)
        {
            target.Clear();
            foreach (var kvp in _nativeStreams)
            {
                var stream = kvp.Value;
                if (stream.GetRawBytes().Length > 0)
                {
                    target.Add(stream);
                }
            }
        }

        /// <summary>
        /// Populates the provided list with active managed event streams that have Current events.
        /// Current = front buffer (populated after SwapBuffers).
        /// Zero-allocation if list capacity is sufficient.
        /// </summary>
        public void PopulateCurrentManagedStreams(List<IManagedEventStreamInfo> target)
        {
            target.Clear();
            foreach (var kvp in _managedStreams)
            {
                var streamObj = kvp.Value;
                if (streamObj is IManagedEventStreamInfo info && info.CurrentEvents.Count > 0)
                {
                    target.Add(info);
                }
            }
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
        /// Creates a snapshot of current event buffers without consuming them.
        /// Used by EventAccumulator to capture history.
        /// </summary>
        public FrameEventData SnapshotCurrentBuffers()
        {
            var data = new FrameEventData();
            data.NativeEvents = new List<(int, byte[], int, int)>();
            data.ManagedEvents = new List<(int, object[], int, Type)>();
            
            // Copy native events
            foreach (var stream in _nativeStreams.Values)
            {
                var buffer = stream.GetRawBytes(); // Gets READ buffer
                int len = buffer.Length;
                if (len > 0)
                {
                    var pooled = ArrayPool<byte>.Shared.Rent(len);
                    buffer.CopyTo(pooled);
                    data.NativeEvents.Add((stream.EventTypeId, pooled, len, stream.ElementSize));
                }
            }
            
            // Copy managed events
            foreach (var streamObj in _managedStreams.Values)
            {
                if (streamObj is IEventStreamInspector inspector && streamObj is IManagedEventStreamInfo info)
                {
                    // InspectReadBuffer uses _front (READ)
                    // We need count of _front. IEventStreamInspector.Count returns _front.Count.
                    int count = inspector.Count; 
                    if (count > 0)
                    {
                        var pooled = ArrayPool<object>.Shared.Rent(count);
                        int i = 0;
                        foreach(var item in inspector.InspectReadBuffer())
                        {
                            pooled[i++] = item;
                        }
                        
                        data.ManagedEvents.Add((info.TypeId, pooled, count, info.EventType));
                    }
                }
            }
            
            return data;
        }

        /// <summary>
        /// Clears all buffers (Read and Write) and resets active event state.
        /// Used when resetting an EntityRepository for reuse in a pool.
        /// </summary>
        public void ClearAll()
        {
            _activeEventIds.Clear();
            
            // Clear Native Streams
            foreach (var stream in _nativeStreams.Values)
            {
                stream.ClearCurrent();
                // We also need to clear pending/write buffer if any
                // Assuming stream has ClearPending or we Swap and Clear?
                // Best to expose ClearAll on stream or clear both.
                // Since NativeEventStream logic is simple, let's call ClearCurrent() 
                // and we need to clear Back buffer too.
                // Assuming NativeEventStream has reset capabilities or we just Swap and Clear again?
                // Double swap + clear handles both?
                
                // For valid robust clearing:
                stream.ClearCurrent();
                stream.Swap(); 
                stream.ClearCurrent();
                
                // If stream has dedicated "Reset" or "ClearAll", use that.
                // If not, the double-swap-clear trick works for double buffers.
            }
            
            // Clear Managed Streams
            foreach (var streamObj in _managedStreams.Values)
            {
                if (streamObj is IManagedEventStream managedStream)
                {
                    managedStream.ClearCurrent();
                    managedStream.Swap();
                    managedStream.ClearCurrent();
                }
            }
        }

        /// <summary>
        /// Injects pre-captured events into this bus.
        /// Used by EventAccumulator to replay history to replicas.
        /// </summary>
        public void InjectEvents(FrameEventData frameData)
        {
            // Inject native events
            if (frameData.NativeEvents != null)
            {
                foreach (var (typeId, buffer, length, elementSize) in frameData.NativeEvents)
                {
                    ReadOnlySpan<byte> slice = new ReadOnlySpan<byte>(buffer, 0, length);
                    InjectIntoCurrentBySize(typeId, elementSize, slice);
                }
            }
            
            // Inject managed events
            if (frameData.ManagedEvents != null)
            {
                foreach (var (typeId, objects, count, eventType) in frameData.ManagedEvents)
                {
                    var slice = new ArraySegment<object>(objects, 0, count);
                    InjectManagedIntoCurrent(typeId, eventType, slice);
                }
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

            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
    

}
