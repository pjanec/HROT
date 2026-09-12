using System;

namespace Fdp.Core
{
    /// <summary>
    /// Base interface for component tables.
    /// Allows polymorphic storage of different component types.
    /// </summary>
    public interface IComponentTable : IDisposable
    {
        /// <summary>
        /// Component type ID.
        /// </summary>
        int ComponentTypeId { get; }
        
        /// <summary>
        /// Component type.
        /// </summary>
        Type ComponentType { get; }
        
        /// <summary>
        /// Size of component in bytes.
        /// </summary>
        int ComponentSize { get; }
        
        /// <summary>
        /// Efficiently checks if this table has been modified since the specified version.
        /// Uses lazy scan of chunk versions (O(chunks), typically ~100 chunks for 100k entities).
        /// PERFORMANCE: 10-50ns scan time, L1-cache friendly, no write contention.
        /// </summary>
        bool HasChanges(uint sinceVersion);

        /// <summary>
        /// Gets the version of the chunk containing the entity.
        /// </summary>
        uint GetVersionForEntity(int entityId);
        
        /// <summary>
        /// Sets component from raw bytes (used by EntityCommandBuffer).
        /// </summary>
        /// <summary>
        /// Sets component from raw bytes (used by EntityCommandBuffer).
        /// </summary>
        unsafe void SetRaw(int entityIndex, IntPtr dataPtr, int size, uint version);

        /// <summary>
        /// Ruling 14 — writes <paramref name="size"/> bytes at <paramref name="byteOffset"/> inside the
        /// component and <b>touches nothing else</b>.
        ///
        /// <para>
        /// ⭐ The surgical counterpart to <see cref="SetRaw"/>. A whole-component write is a
        /// read-modify-write whose "read" happened at some earlier moment, so it silently reverts every
        /// field the caller did not mean to touch. This one cannot: the bytes outside
        /// <c>[byteOffset, byteOffset + size)</c> are never addressed.
        /// </para>
        ///
        /// <para>
        /// ⛔ Managed component tables throw — a reference-typed component has no byte layout to patch.
        /// </para>
        /// </summary>
        unsafe void SetRawAt(int entityIndex, int byteOffset, IntPtr src, int size, uint version);

        /// <summary>
        /// Set component value using type-erased object.
        /// Used by command buffers and network ingress.
        /// </summary>
        /// <param name="index">Entity index</param>
        /// <param name="value">Component value (must match table's component type)</param>
        /// <exception cref="InvalidCastException">If value type doesn't match component type</exception>
        void SetRawObject(int index, object value);
        
        /// <summary>
        /// Get component value as type-erased object.
        /// </summary>
        /// <param name="index">Entity index</param>
        /// <returns>Component value boxed as object</returns>
        object GetRawObject(int index);

        /// <summary>
        /// Clears component data for the entity (e.g. set to null).
        /// </summary>
        void ClearRaw(int index);

        /// <summary>
        /// Clears all data in the table (all chunks).
        /// Used when resetting the repository state (e.g. loading a keyframe).
        /// </summary>
        void Clear();

        /// <summary>
        /// Gets a raw pointer to the component data for the entity.
        /// Only supported for Unmanaged components (ComponentTable{T}).
        /// Throws NotSupportedException for Managed components.
        /// </summary>
        unsafe void* GetRawPointer(int entityIndex);

        /// <summary>
        /// Synchronizes data from a source table of the same type.
        /// </summary>
        void SyncFrom(IComponentTable source);
    }
}
