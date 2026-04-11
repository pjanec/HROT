using System;

namespace Fdp.Kernel
{
    /// <summary>
    /// Type-erased interface for native event streams.
    /// Allows recorder to access event data without knowing generic type T.
    /// </summary>
    public unsafe interface INativeEventStream
    {
        /// <summary>
        /// Writes a single event from raw memory.
        /// </summary>
        void WriteRaw(void* data);
        /// <summary>
        /// Event type ID from [EventId] attribute.
        /// </summary>
        int EventTypeId { get; }

        /// <summary>
        /// Size of a single event in bytes (sizeof(T)).
        /// </summary>
        int ElementSize { get; }

        /// <summary>
        /// Gets raw byte representation of all events in the stream.
        /// Used by recorder for serialization.
        /// </summary>
        ReadOnlySpan<byte> GetRawBytes();

        /// <summary>
        /// Swaps read/write buffers (called at end of frame).
        /// </summary>
        void Swap();

        /// <summary>
        /// Clears the current write buffer and frees graveyard.
        /// </summary>
        void Clear();

        // ========== FLIGHT RECORDER INTEGRATION ==========

        /// <summary>
        /// Gets raw bytes from the WRITE (Pending) buffer.
        /// Used during recording to capture events that just happened.
        /// </summary>
        ReadOnlySpan<byte> GetPendingBytes();

        /// <summary>
        /// Injects raw bytes directly into the READ (Current) buffer.
        /// Used during replay to make events immediately consumable.
        /// </summary>
        void InjectIntoCurrent(ReadOnlySpan<byte> data);

        /// <summary>
        /// Clears the READ (Current) buffer.
        /// Used during replay to prevent mixing old events.
        /// </summary>
        void ClearCurrent();

    }
}
