using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Replication.Utilities;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Replication.Patching;

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
    /// ⭐⭐⭐ <b><c>Q59-E</c> — the world's component→descriptor map, supplied by the NETWORK layer.</b>
    ///
    /// <para>🔒 User ruling <c>2026-08-26</c>: *"attributes are entity-related, network agnostic. In contrary,
    /// descriptors are Ned network concept."</para>
    ///
    /// <para>⛔ This used to be a <c>Dictionary&lt;Type, long&gt;</c> built from the JSON routing table's
    /// <c>descriptorOrdinal:</c> argument — i.e. FDP code naming a NED grouping. ⭐ Now the applier records the
    /// COMPONENT it wrote and <see cref="DescriptorOwnershipMap"/> — the EXISTING single source of truth,
    /// fed by <c>IDescriptorTranslator.{DescriptorOrdinal, TargetComponentIds}</c> — says which descriptors
    /// cover it. ⇒ <c>Fdp.Toolkit.Replication.DescriptorOrdinal</c> and its conversion were deleted outright.</para>
    ///
    /// <para>⚠ A component maps to a SET of ordinals — measured: <c>SimTransform</c> is covered by both
    /// <c>BdcWorldPosTranslator</c> and <c>GeoSpatialEgressTranslator</c>.</para>
    /// </summary>
    private readonly Services.DescriptorOwnershipMap _descriptorMap;

    /// <summary>
    /// Ordinals that have been touched during this compilation session.
    /// HashSet semantics ensure automatic deduplication.
    /// </summary>
    private readonly HashSet<long> _touchedOrdinals = new();

    /// <summary>
    /// Component type IDs that were actually written during this session
    /// (i.e. authority check passed and the setter was dispatched).
    /// Used by <see cref="HasAppliedAny"/> and the ACK bitmask builder.
    /// </summary>
    private readonly HashSet<int> _appliedComponentIds = new();

    // ── Standalone factory (no routing table) ────────────────────────────

    /// <summary>
    /// Empty routing table used by <see cref="Create"/> so that the binary-interpreter
    /// path can construct an <see cref="EcsPatchContext"/> without a
    /// <see cref="JsonAttributeCompiler"/> dependency.
    /// </summary>
    private static readonly IReadOnlyDictionary<ulong, RoutingEntry> s_emptyRoutes =
        new Dictionary<ulong, RoutingEntry>(0);

    /// <summary>
    /// ⭐ Creates a bare <see cref="EcsPatchContext"/> for the binary-interpreter path.
    ///
    /// <para>⭐⭐⭐ <b><c>Q59-E</c> — this is no longer the crippled factory it was.</b> Its doc used to say
    /// *"the ordinal map is empty so the call is a no-op for dirty-descriptor propagation — the installers
    /// drive SmartEgress themselves"*. 🔴 That described the <c>AX-015</c> DEFECT as if it were the design:
    /// nothing reached SmartEgress and a binary rename was never republished.</para>
    ///
    /// <para>⭐ Both factories now resolve the SAME per-world map, so the binary and JSON paths are identical
    /// in this respect — there is no longer a "with routing table" and a "without" variant as far as egress
    /// is concerned.</para>
    /// </summary>
    /// <param name="repo">Live ECS world.</param>
    /// <param name="entity">Entity being patched.</param>
    public static EcsPatchContext Create(EntityRepository repo, Entity entity)
        => new EcsPatchContext(repo, entity, s_emptyRoutes);

    /// <param name="repo">Live ECS world.</param>
    /// <param name="entity">Entity being patched.</param>
    /// <param name="routes">
    ///   The same routing table the compiler uses — used to look up descriptor ordinals
    ///   when <see cref="FlushDirtyMarks"/> is called.
    /// </param>
    /// <remarks>
    /// The constructor is <c>internal</c> because <see cref="RoutingEntry"/> is an internal type.
    /// External callers should obtain <see cref="EcsPatchContext"/> through the compiler
    /// returned by <see cref="AttributeCompilerBuilder.Build"/>, or via
    /// <see cref="Create(EntityRepository, Entity)"/> for binary-only paths.
    /// </remarks>
    internal EcsPatchContext(
        EntityRepository repo,
        Entity entity,
        IReadOnlyDictionary<ulong, RoutingEntry> routes)
    {
        _repo = repo;
        _entity = entity;

        // ⭐⭐⭐ Q59-E — the ordinals come from the WORLD (contributed by the network layer), not from the
        //    routing table. ⛔ The `routes` argument no longer carries any descriptor information; it is
        //    retained only because the JSON compiler hands its table in for future use.
        _descriptorMap = Attributes.AttributeInterpreterProvider.GetDescriptorMap(repo);
    }

    // ── IEntityPatchContext ──────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// Returns <c>ref T</c> directly from the live ECS chunk via
    /// <c>repo.GetComponentRW&lt;T&gt;(entity)</c>.
    /// Also records the component type's ordinal as touched for
    /// <see cref="FlushDirtyMarks"/>, and records the ECS component ID as applied.
    /// </remarks>
    public ref T GetUnmanagedComponent<T>() where T : struct
    {
        RecordOrdinal(typeof(T));
        _appliedComponentIds.Add(ComponentTypeRegistry.GetOrRegisterManaged(typeof(T)));
        return ref _repo.GetComponentRW<T>(_entity);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns the managed component instance via the <see cref="ISimulationView"/> interface.
    /// Also records the component type's ordinal as touched for <see cref="FlushDirtyMarks"/>,
    /// and records the ECS component ID as applied.
    /// </remarks>
    public T GetManagedComponent<T>() where T : class
    {
        RecordOrdinal(typeof(T));
        _appliedComponentIds.Add(ManagedComponentType<T>.ID);
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


    /// <inheritdoc/>
    /// <remarks>
    /// Looks up the ECS component type ID via the registry (O(1) cached fast-path) and
    /// checks the entity's <c>AuthorityMask</c> — the same bit used by
    /// <c>ValidateWriteAccess&lt;T&gt;</c>, preventing any mismatch between the invoker
    /// guard and the kernel write-protection boundary.
    /// </remarks>
    public bool CanWrite<T>() where T : struct
        => _repo.HasAuthority(_entity, ComponentTypeRegistry.GetOrRegisterManaged(typeof(T)));

    /// <inheritdoc/>
    /// <remarks>
    /// Checks the <c>AuthorityMask</c> for the managed component type ID
    /// (<c>ManagedComponentType&lt;T&gt;.ID</c>) — same bit the kernel uses when
    /// a managed component slot is protected.
    /// </remarks>
    public bool CanWriteManaged<T>() where T : class
        => _repo.HasAuthority(_entity, ManagedComponentType<T>.ID);

    // ── Additional API (used by UpdateEntityAttributeRequestSystem) ──────

    /// <summary>
    /// Returns true if at least one component was actually written during this compilation
    /// session (i.e. authority check passed and the setter was dispatched).
    /// Used to implement the "silent bystander" rule.
    /// </summary>
    public bool HasAppliedAny => _appliedComponentIds.Count > 0;

    /// <summary>
    /// The set of ECS component type IDs that were authorised and written during this session.
    /// Used by <see cref="UpdateEntityAttributeRequestSystem"/> to build the 32-byte OpaqueData bitmask.
    /// </summary>
    public IReadOnlyCollection<int> AppliedComponentIds => _appliedComponentIds;

    // ── Helpers ──────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ Records every descriptor that covers <paramref name="componentType"/>.
    /// ⚠ Zero matches is normal — a networkless host has no translators, so nothing is republishable.
    /// </summary>
    private void RecordOrdinal(Type componentType)
    {
        int componentId = ComponentTypeRegistry.GetOrRegisterManaged(componentType);

        foreach (long ordinal in _descriptorMap.GetDescriptorsForComponentId(componentId))
            _touchedOrdinals.Add(ordinal);
    }
}
