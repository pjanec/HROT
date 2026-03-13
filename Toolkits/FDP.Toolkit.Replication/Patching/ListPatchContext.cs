using System;
using System.Collections.Generic;

namespace FDP.Toolkit.Replication.Patching;

/// <summary>
/// <see cref="IEntityPatchContext"/> implementation used during entity creation.
/// Wraps a seed <see cref="List{T}"/> of components (from <c>DescriptorMapper.MapToComponents</c>)
/// and returns <c>ref</c> or instance handles for each component type, creating defaults on demand.
/// </summary>
/// <remarks>
/// <para>
/// For unmanaged struct components a <see cref="ComponentSlot{T}"/> object is allocated once per
/// component type (at first access) to provide a stable heap address for the <c>ref T</c> return.
/// This single allocation happens during initialisation, not on the hot path.
/// </para>
/// <para>
/// <see cref="FlushDirtyMarks"/> is a no-op: the spawning pipeline drives egress, not the
/// attribute compiler.
/// </para>
/// </remarks>
public sealed class ListPatchContext : IEntityPatchContext
{
    // ── Internal slot types ──────────────────────────────────

    private interface IComponentSlot
    {
        /// <summary>Returns the stored value boxed as <c>object</c> for list serialisation.</summary>
        object GetBoxed();
    }

    private sealed class ComponentSlot<T> : IComponentSlot where T : struct
    {
        public T Value;
        public object GetBoxed() => Value;
    }

    // ── Fields ───────────────────────────────────────────────

    private readonly List<object>? _baseComponents;

    /// <summary>
    /// Per-type stable slots for unmanaged struct components.
    /// Key = typeof(T), Value = ComponentSlot&lt;T&gt;.
    /// </summary>
    private readonly Dictionary<Type, IComponentSlot> _unmanagedSlots = new();

    /// <summary>
    /// Per-type instances for managed class components.
    /// Key = typeof(T), Value = T instance.
    /// </summary>
    private readonly Dictionary<Type, object> _managedComponents = new();

    // ── Constructor ──────────────────────────────────────────

    /// <summary>
    /// Initialises the context with an optional seed list from <c>DescriptorMapper</c>.
    /// A <c>null</c> or empty list is valid; missing components are created on first access.
    /// </summary>
    public ListPatchContext(List<object>? baseComponents)
    {
        _baseComponents = baseComponents;
    }

    // ── IEntityPatchContext ──────────────────────────────────

    /// <inheritdoc/>
    public ref T GetUnmanagedComponent<T>() where T : struct
    {
        if (!_unmanagedSlots.TryGetValue(typeof(T), out var slotObj))
        {
            var slot = new ComponentSlot<T>();

            // Seed from base list if a matching component is present.
            if (_baseComponents != null)
            {
                foreach (var component in _baseComponents)
                {
                    if (component is T existing)
                    {
                        slot.Value = existing;
                        break;
                    }
                }
            }

            _unmanagedSlots[typeof(T)] = slot;
            slotObj = slot;
        }

        return ref ((ComponentSlot<T>)slotObj).Value;
    }

    /// <inheritdoc/>
    public T GetManagedComponent<T>() where T : class
    {
        if (_managedComponents.TryGetValue(typeof(T), out var existing))
            return (T)existing;

        // Search the seed list first.
        if (_baseComponents != null)
        {
            foreach (var component in _baseComponents)
            {
                if (component is T found)
                {
                    _managedComponents[typeof(T)] = found;
                    return found;
                }
            }
        }

        // Create a default instance (lazy, cached).
        T created = Activator.CreateInstance<T>();
        _managedComponents[typeof(T)] = created;
        return created;
    }

    /// <inheritdoc/>
    /// <remarks>No-op: creation context has no egress to trigger.</remarks>
    public void FlushDirtyMarks() { /* intentional no-op */ }

    /// <inheritdoc/>
    /// <remarks>
    /// Always returns <c>true</c>: the entity creation path always has full authority over
    /// all components being initialised.
    /// </remarks>
    public bool HasAuthority(int componentId) => true;

    // ── Additional API ────────────────────────────────────────

    /// <summary>
    /// Returns the final component list with all touched components replaced or inserted.
    /// Each component type appears exactly once (per-component-compilation invariant).
    /// </summary>
    public List<object> FlushComponents()
    {
        // Start with a copy of the base list (minus any types we have updated versions for).
        var result = new List<object>();

        if (_baseComponents != null)
        {
            foreach (var component in _baseComponents)
            {
                Type type = component.GetType();

                // Skip components that we have a newer version for (either slot or managed).
                if (_unmanagedSlots.ContainsKey(type) || _managedComponents.ContainsKey(type))
                    continue;

                result.Add(component);
            }
        }

        // Append updated/new unmanaged components.
        foreach (var slot in _unmanagedSlots.Values)
            result.Add(slot.GetBoxed());

        // Append updated/new managed components.
        foreach (var component in _managedComponents.Values)
            result.Add(component);

        return result;
    }
}
