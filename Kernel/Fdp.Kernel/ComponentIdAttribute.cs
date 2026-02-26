using System;

namespace Fdp.Kernel
{
    /// <summary>
    /// Marks an ECS component struct with a stable, globally unique ID.
    /// Required for deterministic component ID assignment when multiple binaries merge
    /// into a single Runner process (Phase R0 — ECS Component ID Safety).
    ///
    /// <para>
    /// Without this attribute, <see cref="ComponentTypeRegistry"/> falls back to
    /// auto-increment assignment (<c>_nextId++</c>), which is non-deterministic across
    /// different assembly load orders.  When <see cref="FdpConfig.EnforceExplicitComponentIds"/>
    /// is <c>true</c>, all component structs <b>must</b> carry this attribute or an
    /// <see cref="InvalidOperationException"/> is thrown at registration time.
    /// </para>
    ///
    /// <para>
    /// IDs are allocated in named blocks defined by <see cref="GlobalComponentIds"/>.
    /// Component IDs are limited to the range [0, 255] by the <c>BitMask256</c> capacity.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// [ComponentId(GlobalComponentIds.SimTransform)]
    /// public struct SimTransform { ... }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class ComponentIdAttribute : Attribute
    {
        /// <summary>
        /// Stable, globally unique component type ID in the range [0, 255].
        /// Must match the corresponding constant in <see cref="GlobalComponentIds"/>.
        /// </summary>
        public byte Id { get; }

        /// <summary>
        /// Initialises the attribute with a stable component ID.
        /// </summary>
        /// <param name="id">
        /// The globally unique component ID (0–255). Must not collide with any other
        /// component's ID anywhere in the codebase; a collision throws
        /// <see cref="InvalidOperationException"/> at runtime during registration.
        /// </param>
        public ComponentIdAttribute(byte id)
        {
            Id = id;
        }
    }
}
