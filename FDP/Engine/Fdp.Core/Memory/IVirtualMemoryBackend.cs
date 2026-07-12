namespace Fdp.Core.Memory
{
    /// <summary>
    /// Platform-specific virtual memory syscall backend selected at runtime by
    /// NativeMemoryAllocator. Implementations perform the raw reserve/commit/decommit/free
    /// syscalls and throw the same exception types the facade has always thrown on failure:
    /// OutOfMemoryException on reserve failure and InvalidOperationException on commit failure
    /// (unconditionally), and InvalidOperationException on decommit/free failure only under
    /// FDP_PARANOID_MODE (release builds ignore decommit/free syscall failures, as before).
    /// Argument validation (FDP_PARANOID_MODE checks) stays in the facade.
    /// </summary>
    internal unsafe interface IVirtualMemoryBackend
    {
        /// <summary>
        /// Reserves address space. Physical RAM cost: 0 bytes.
        /// Returns a 64KB aligned pointer to the reserved region.
        /// </summary>
        void* Reserve(long sizeBytes);

        /// <summary>
        /// Commits a region, backing it with physical RAM.
        /// The region must have been previously reserved.
        /// </summary>
        void Commit(void* ptr, long sizeBytes);

        /// <summary>
        /// Decommits a region, releasing physical RAM but keeping address space reserved.
        /// </summary>
        void Decommit(void* ptr, long sizeBytes);

        /// <summary>
        /// Frees the entire reserved region.
        /// </summary>
        void Free(void* ptr, long originalReservedSize);
    }
}
