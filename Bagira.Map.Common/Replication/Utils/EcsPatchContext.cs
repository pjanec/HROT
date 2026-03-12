using System;
using System.Collections.Generic;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Utilities;
using ModuleHost.Core.Abstractions;

namespace Bagira.Map.Common.Replication.Utils;

/// <summary>
/// <see cref="IEntityPatchContext"/> implementation used during live entity attribute updates.
/// Wraps a live <see cref="EntityRepository"/> and <see cref="Entity"/>, forwarding component
/// read/write calls directly to the ECS world.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="GetUnmanagedComponent{T}"/> delegates to <c>repo.GetComponentRW&lt;T&gt;(entity)</c>.
/// The chunk-level version tick is bumped by this call, but egress is driven exclusively
/// by <see cref="FlushDirtyMarks"/> — not by chunk ticks — to guarantee per-entity precision
/// (see ATTR-DESIGN.md §3.10).
/// </para>
/// <para>
/// <see cref="FlushDirtyMarks"/> deduplicates ordinals so that patching both <c>Name</c> and
/// <c>Affiliation</c> (both mapped to <c>dtEntityInfo</c>) emits only a single
/// <c>SmartEgressUtil.MarkDirty</c> call.
/// </para>
/// </remarks>
public sealed class EcsPatchContext : IEntityPatchContext
{
    private readonly EntityRepository _repo;
    private readonly Entity _entity;

    /// <summary>
    /// Maps component type → descriptor ordinal, derived from the routing table at construction.
    /// Used to look up which ordinal to dirty when a component is accessed.
    /// </summary>
    private readonly Dictionary<Type, long> _ordinalByType;

    /// <summary>
    /// Ordinals that have been touched during this compilation session.
    /// HashSet semantics ensure automatic deduplication.
    /// </summary>
    private readonly HashSet<long> _touchedOrdinals = new();

    /// <param name="repo">Live ECS world.</param>
    /// <param name="entity">Entity being patched.</param>
    /// <param name="routes">
    ///   The same routing table the compiler uses — used to look up descriptor ordinals
    ///   when <see cref="FlushDirtyMarks"/> is called.
    /// </param>
    /// <remarks>
    /// The constructor is <c>internal</c> because <see cref="RoutingEntry"/> is an internal type.
    /// External callers should obtain <see cref="EcsPatchContext"/> through the compiler
    /// returned by <see cref="AttributeCompilerBuilder.Build"/>.
    /// </remarks>
    internal EcsPatchContext(
        EntityRepository repo,
        Entity entity,
        IReadOnlyDictionary<ulong, RoutingEntry> routes)
    {
        _repo = repo;
        _entity = entity;

        // Pre-compute a type→ordinal lookup from the routing table.
        _ordinalByType = new Dictionary<Type, long>();
        foreach (var (_, entry) in routes)
        {
            if (entry.DescriptorOrdinal != 0)
                _ordinalByType[entry.ComponentType] = entry.DescriptorOrdinal;
        }
    }

    // ── IEntityPatchContext ──────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// Returns <c>ref T</c> directly from the live ECS chunk via
    /// <c>repo.GetComponentRW&lt;T&gt;(entity)</c>.
    /// Also records the component type's ordinal as touched for
    /// <see cref="FlushDirtyMarks"/>.
    /// </remarks>
    public ref T GetUnmanagedComponent<T>() where T : struct
    {
        RecordOrdinal(typeof(T));
        return ref _repo.GetComponentRW<T>(_entity);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns the managed component instance via the <see cref="ISimulationView"/> interface.
    /// Also records the component type's ordinal as touched for <see cref="FlushDirtyMarks"/>.
    /// </remarks>
    public T GetManagedComponent<T>() where T : class
    {
        RecordOrdinal(typeof(T));
        return ((ISimulationView)_repo).GetManagedComponentRO<T>(_entity);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Iterates all distinct touched descriptor ordinals and calls
    /// <see cref="SmartEgressUtil.MarkDirty"/> once per ordinal, guaranteeing per-entity
    /// egress precision (no reliance on coarse chunk-level version ticks).
    /// </remarks>
    public void FlushDirtyMarks()
    {
        foreach (long ordinal in _touchedOrdinals)
            SmartEgressUtil.MarkDirty(_repo, _entity, ordinal);
    }

    // ── Helpers ──────────────────────────────────────────────

    private void RecordOrdinal(Type componentType)
    {
        if (_ordinalByType.TryGetValue(componentType, out long ordinal))
            _touchedOrdinals.Add(ordinal);
    }
}
