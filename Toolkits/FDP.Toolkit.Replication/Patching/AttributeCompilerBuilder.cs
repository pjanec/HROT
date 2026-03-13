using System;
using System.Collections.Generic;
using System.Text.Json;
using Fdp.Kernel;

namespace FDP.Toolkit.Replication.Patching;

/// <summary>
/// Fluent builder that registers JSON attribute paths and their typed setter delegates,
/// then produces an immutable <see cref="JsonAttributeCompiler"/>.
/// </summary>
/// <remarks>
/// All FNV-1a hashing is performed at registration time — never during compilation.
/// </remarks>
public sealed class AttributeCompilerBuilder
{
    private readonly Dictionary<ulong, RoutingEntry> _routes = new();

    /// <summary>
    /// Registers a JSON path for a struct-based (unmanaged) ECS component.
    /// The <paramref name="jsonPath"/> is hashed at registration time using the same
    /// FNV-1a algorithm used by <see cref="JsonAttributeCompiler.Compile"/>.
    /// </summary>
    /// <typeparam name="T">Unmanaged struct component type.</typeparam>
    /// <param name="jsonPath">
    /// Dot-separated JSON path, e.g. <c>"GeoPosition"</c> or <c>"Weapons.*.Ammo.Count"</c>.
    /// Use <c>*</c> as a wildcard for numeric array indices.
    /// </param>
    /// <param name="setter">Delegate that mutates the component in-place.</param>
    /// <param name="descriptorOrdinal">
    /// Descriptor ordinal stored in the routing entry. Used by
    /// <see cref="EcsPatchContext.FlushDirtyMarks"/> to call
    /// <c>SmartEgressUtil.MarkDirty</c> after all mutations are applied,
    /// bypassing coarse chunk-level ticks.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="jsonPath"/> is null or empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="jsonPath"/> maps to a hash that is already registered.</exception>
    public AttributeCompilerBuilder RegisterValuePath<T>(
        string jsonPath,
        ValueAttributeSetter<T> setter,
        long descriptorOrdinal = 0) where T : struct
    {
        if (string.IsNullOrEmpty(jsonPath))
            throw new ArgumentNullException(nameof(jsonPath), "JSON path must not be null or empty.");

        ulong hash = JsonAttributeCompiler.HashPath(jsonPath);
        if (_routes.ContainsKey(hash))
            throw new InvalidOperationException(
                $"A route for path '{jsonPath}' (hash {hash}) is already registered. Duplicate paths are not allowed.");

        _routes[hash] = new RoutingEntry(new ValueInvoker<T>(setter), ComponentTypeRegistry.GetOrRegisterManaged(typeof(T)), descriptorOrdinal);
        return this;
    }

    /// <summary>
    /// Registers a JSON path for a class-based (managed) ECS component.
    /// </summary>
    /// <typeparam name="T">Managed class component type.</typeparam>
    /// <param name="jsonPath">Dot-separated JSON path, e.g. <c>"Name"</c> or <c>"Affiliation"</c>.</param>
    /// <param name="setter">Delegate that mutates the component.</param>
    /// <param name="descriptorOrdinal">
    /// Descriptor ordinal stored in the routing entry for use by <see cref="EcsPatchContext.FlushDirtyMarks"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="jsonPath"/> is null or empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="jsonPath"/> maps to a hash that is already registered.</exception>
    public AttributeCompilerBuilder RegisterReferencePath<T>(
        string jsonPath,
        ReferenceAttributeSetter<T> setter,
        long descriptorOrdinal = 0) where T : class
    {
        if (string.IsNullOrEmpty(jsonPath))
            throw new ArgumentNullException(nameof(jsonPath), "JSON path must not be null or empty.");

        ulong hash = JsonAttributeCompiler.HashPath(jsonPath);
        if (_routes.ContainsKey(hash))
            throw new InvalidOperationException(
                $"A route for path '{jsonPath}' (hash {hash}) is already registered. Duplicate paths are not allowed.");

        _routes[hash] = new RoutingEntry(new ReferenceInvoker<T>(setter), ManagedComponentType<T>.ID, descriptorOrdinal);
        return this;
    }

    /// <summary>
    /// Builds the immutable <see cref="JsonAttributeCompiler"/> with all registered routes.
    /// </summary>
    public JsonAttributeCompiler Build()
        => new JsonAttributeCompiler(_routes);
}
