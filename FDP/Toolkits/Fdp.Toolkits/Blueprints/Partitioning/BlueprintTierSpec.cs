using System;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Blueprints.Partitioning;

/// <summary>
/// ⭐⭐⭐ <b>One occurrence-store tier, described once.</b> <c>O3a</c> / task <c>B3</c> —
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §17.
///
/// <para><b>What it is for.</b> Before this type, every site that had to NAME a tier component type
/// spelled the ladder by hand: <c>HasComponent&lt;…16384&gt;</c> → <c>…4096</c> → <c>…1024</c>.
/// 📐 Measured <c>2026-09-20</c> (§17.1): <b>~36 such sites across 12 production files plus 3
/// renderers</b>. A fourth tier (<c>O3b</c>'s 256) is a fourth arm in every one of them.</para>
///
/// <para>⛔⛔ <b>WHAT IT IS NOT FOR — and this is the line that keeps the table small.</b> A site that
/// only needs <i>"this entity's store bytes"</i> does NOT need a tier at all: it calls
/// <see cref="OccurrenceStoreAccess"/>, and for capacity it reads
/// <see cref="BlueprintBlackboardHeader"/>, which already carries <c>MaxSlots</c>, <c>SlotCount</c>,
/// <c>PayloadSize</c> and <c>PayloadFree</c>. 📌 §17.1 <c>N3</c>: six <c>BehaviorIngressSystem</c>
/// helpers looked like tier branching and were nothing of the kind — they resolved the store of the
/// entity they were handed, from a size derived from that same entity one line earlier.
/// ⭐ <b>Reach for a spec only when a COMPONENT TYPE must be named</b> — add, remove, register,
/// build a query, or pick a target for promotion.</para>
///
/// <para>⚠ <b>The lifetime rule is unchanged and still applies to <see cref="Memory"/>.</b> The
/// pointer is native (never GC-moved) but it IS invalidated by chunk decommit and by a tier swap.
/// ⛔ Use it within the call that obtained it; never store it across a frame or across anything that
/// can add or remove a component on this entity. The full argument lives on
/// <see cref="OccurrenceStoreAccess"/>.</para>
/// </summary>
public sealed unsafe class BlueprintTierSpec
{
    /// <summary>Resolves the tier component's first byte on <paramref name="entity"/>.</summary>
    public delegate byte* MemoryResolver(EntityRepository repo, Entity entity);

    /// <summary>
    /// The <see cref="ISimulationView"/> form. ⭐ Needed because two consumers — the blueprint debug
    /// session and replay inspection — read through a VIEW, which may be a historical snapshot
    /// rather than the live repository, and a view offers no RW access at all.
    /// </summary>
    public delegate byte* ViewMemoryResolver(ISimulationView view, Entity entity);

    private readonly Func<EntityRepository, Entity, bool>   _has;
    private readonly MemoryResolver                         _memory;
    private readonly MemoryResolver                         _memoryReadOnly;
    private readonly Action<EntityRepository, Entity>       _add;
    private readonly Action<EntityRepository, Entity>       _remove;
    private readonly Action<EntityRepository>               _register;
    private readonly Func<EntityRepository, bool>           _isRegistered;
    private readonly Func<QueryBuilder, QueryBuilder>       _constrain;
    private readonly Func<EntityRepository, IntPtr>         _ensureSingleton;
    private readonly Func<ISimulationView, Entity, bool>    _viewHas;
    private readonly ViewMemoryResolver                     _viewMemoryReadOnly;

    /// <summary>The enum spelling, for the call sites that still speak <see cref="BlackboardTier"/>.</summary>
    public BlackboardTier Tier { get; }

    /// <summary>The tier component's CLR type — for diagnostics, renderers and id lookups.</summary>
    public Type ComponentType { get; }

    /// <summary>Whole component size, header included (1024 / 4096 / 16384).</summary>
    public int TotalSize { get; }

    /// <summary>Slot-table capacity. ⚠ <c>O3b</c>'s <c>W1</c> re-picks these; see §17.6.</summary>
    public int MaxSlots { get; }

    /// <summary>Usable payload bytes = <c>TotalSize − (header + slot table)</c>.</summary>
    public int PayloadSize { get; }

    private BlueprintTierSpec(
        BlackboardTier tier, Type componentType, int totalSize, int maxSlots, int payloadSize,
        Func<EntityRepository, Entity, bool> has,
        MemoryResolver memory, MemoryResolver memoryReadOnly,
        Action<EntityRepository, Entity> add, Action<EntityRepository, Entity> remove,
        Action<EntityRepository> register, Func<EntityRepository, bool> isRegistered,
        Func<QueryBuilder, QueryBuilder> constrain,
        Func<EntityRepository, IntPtr> ensureSingleton,
        Func<ISimulationView, Entity, bool> viewHas, ViewMemoryResolver viewMemoryReadOnly)
    {
        Tier = tier; ComponentType = componentType;
        TotalSize = totalSize; MaxSlots = maxSlots; PayloadSize = payloadSize;
        _has = has; _memory = memory; _memoryReadOnly = memoryReadOnly;
        _add = add; _remove = remove; _register = register; _constrain = constrain;
        _isRegistered = isRegistered;
        _ensureSingleton = ensureSingleton;
        _viewHas = viewHas; _viewMemoryReadOnly = viewMemoryReadOnly;
    }

    /// <summary>
    /// ⭐⭐ <b>The ONE place a tier component type is named.</b> Everything else in the codebase goes
    /// through the resulting spec, so adding a tier is one more call in
    /// <see cref="BlueprintTierTable"/> rather than an arm in ~36 ladders.
    ///
    /// <para>📐 <b>Why <c>Unsafe.As&lt;TTier, byte&gt;</c> and not <c>fixed (byte* m = t.Memory)</c>:</b>
    /// a generic method cannot see <c>TTier.Memory</c>, and it does not need to — every tier struct is
    /// <c>[StructLayout(Sequential)]</c> with the fixed buffer as its only field, so the struct's first
    /// byte IS the buffer's first byte. ⭐ This is not a new trick:
    /// <c>BlueprintTickSystem</c> and <c>BlueprintMaintenanceSystem</c> already resolve their memory
    /// exactly this way. ⚠ <c>fixed</c> was never pinning here — the storage is native (see
    /// <see cref="OccurrenceStoreAccess"/>).</para>
    /// </summary>
    public static BlueprintTierSpec For<TTier>(
        BlackboardTier tier, int totalSize, int maxSlots, int payloadSize)
        where TTier : unmanaged
    {
        // 🔴🔴 TWO CEILINGS ON MaxSlots, AND W1 (the re-pick, §17.6) MUST RESPECT BOTH.
        //   ① `BlueprintBlackboardPartitions.Initialize` takes a BYTE, so 255 is the hard cap.
        //   ② ⛔⛔ A3's Kind nibble array lives in the header's 8-byte `Reserved` at 4 bits per slot
        //      ⇒ MaxKindSlots = 16, EXACTLY. A tier with more slots than that has slots whose kind
        //      cannot be recorded, and BlueprintTickSystem's walker filters ON the declared kind ⇒
        //      every slot past 16 would be silently skipped. Asserted here rather than in a design
        //      note, because a note is not a gate.
        if (maxSlots < 1 || maxSlots > BlueprintBlackboardPartitions.MaxKindSlots)
            throw new ArgumentOutOfRangeException(
                nameof(maxSlots), maxSlots,
                $"A tier's MaxSlots must be 1..{BlueprintBlackboardPartitions.MaxKindSlots} — the Kind "
                + "nibble array in the header's 8-byte Reserved holds exactly that many (D1', A3). "
                + "Widening the ladder past it needs a new home for the kinds first.");

        return new(
            tier, typeof(TTier), totalSize, maxSlots, payloadSize,
            has:            static (repo, e) => repo.HasComponent<TTier>(e),
            memory:         static (repo, e) =>
            {
                ref var bb = ref repo.GetComponentRW<TTier>(e);
                return (byte*)Unsafe.AsPointer(ref Unsafe.As<TTier, byte>(ref bb));
            },
            // 🔴 RO is NOT a stylistic variant: GetRefRW writes the chunk version and GetRefRO does
            //    not, and DeltaQuery reads those versions. Resolving a read-only consumer through the
            //    RW form marks every blackboard chunk dirty on every pass.
            memoryReadOnly: static (repo, e) =>
            {
                ref readonly var bb = ref repo.GetComponentRO<TTier>(e);
                return (byte*)Unsafe.AsPointer(
                    ref Unsafe.As<TTier, byte>(ref Unsafe.AsRef(in bb)));
            },
            add:            static (repo, e) => repo.AddComponent(e, default(TTier)),
            remove:         static (repo, e) => repo.RemoveComponent<TTier>(e),
            register:       static repo => repo.RegisterComponent<TTier>(),
            isRegistered:   static repo => repo.IsComponentTypeRegistered<TTier>(),
            constrain:      static qb => qb.With<TTier>(),
            // ⭐ WORLD SINGLETON form — a blueprint declared `WorldSingleton` lives in a singleton
            //   component of its tier, not on an entity. Lazy-attach on first encounter, as before.
            ensureSingleton: static repo =>
            {
                if (!repo.HasSingleton<TTier>())
                    repo.SetSingletonUnmanaged<TTier>(default);

                ref var bb = ref repo.GetSingleton<TTier>();
                return (IntPtr)Unsafe.AsPointer(ref Unsafe.As<TTier, byte>(ref bb));
            },
            viewHas:        static (view, e) => view.HasComponent<TTier>(e),
            viewMemoryReadOnly: static (view, e) =>
            {
                ref readonly var bb = ref view.GetComponentRO<TTier>(e);
                return (byte*)Unsafe.AsPointer(
                    ref Unsafe.As<TTier, byte>(ref Unsafe.AsRef(in bb)));
            });
    }

    /// <summary><see langword="true"/> when <paramref name="entity"/> carries THIS tier.</summary>
    public bool Has(EntityRepository repo, Entity entity) => _has(repo, entity);

    /// <summary>⛔ Read the lifetime rule above before storing the result.</summary>
    public byte* Memory(EntityRepository repo, Entity entity) => _memory(repo, entity);

    /// <summary>The read-only resolution — ⛔ use this whenever the caller only READS.</summary>
    public byte* MemoryReadOnly(EntityRepository repo, Entity entity) => _memoryReadOnly(repo, entity);

    /// <summary>
    /// The WORLD SINGLETON of this tier, creating it on first call. ⛔ Same lifetime rule.
    /// </summary>
    public byte* EnsureSingletonMemory(EntityRepository repo) => (byte*)_ensureSingleton(repo);

    /// <summary><see langword="true"/> when the entity carries this tier IN THE GIVEN VIEW, which
    /// may be a historical snapshot rather than the live world.</summary>
    public bool HasInView(ISimulationView view, Entity entity) => _viewHas(view, entity);

    /// <summary>Read-only resolution through a view. ⛔ Same lifetime rule.</summary>
    public byte* MemoryReadOnlyInView(ISimulationView view, Entity entity)
        => _viewMemoryReadOnly(view, entity);

    /// <summary>The whole component as bytes, read-only, through a view — what the debug session's
    /// slot maths consumes.</summary>
    public ReadOnlySpan<byte> BytesInView(ISimulationView view, Entity entity)
        => new(MemoryReadOnlyInView(view, entity), TotalSize);

    /// <summary>Adds a zeroed tier component. ⚠ Zeroed is NOT initialised — the header magic is
    /// written by <c>BlueprintBlackboardPartitions.Initialize</c>.</summary>
    public void Add(EntityRepository repo, Entity entity) => _add(repo, entity);

    /// <summary>Removes the tier component (the second half of a promotion).</summary>
    public void Remove(EntityRepository repo, Entity entity) => _remove(repo, entity);

    /// <summary>Registers the component type on a world. Idempotent, as <c>RegisterComponent</c> is.</summary>
    public void Register(EntityRepository repo) => _register(repo);

    /// <summary>
    /// Whether this tier's component type is registered on <paramref name="repo"/>.
    /// ⭐ <c>B4</c>: several scratch worlds deliberately carry only the small tiers
    /// (<see cref="BlueprintTierTable.RegisterUpTo"/>), so a table-driven walk that QUERIES every
    /// tier must skip the ones this world never registered — querying an unregistered component
    /// throws. ⛔ Not a capability probe: use <see cref="Has(EntityRepository, Entity)"/> for an
    /// entity.
    /// </summary>
    public bool IsRegistered(EntityRepository repo) => _isRegistered(repo);

    /// <summary>
    /// ⭐ Adds <c>.With&lt;TTier&gt;()</c> to a query under construction — the composable form, so a
    /// PAIR query (<c>BlueprintMaintenanceSystem</c>'s "holds both tiers" promotion signal) can be
    /// built from two specs without either of them knowing about the other.
    /// </summary>
    public QueryBuilder Constrain(QueryBuilder builder) => _constrain(builder);

    /// <summary>A query matching every entity carrying this tier.</summary>
    public EntityQuery BuildQuery(EntityRepository repo) => Constrain(repo.Query()).Build();

    /// <inheritdoc/>
    public override string ToString() => $"{ComponentType.Name}(total={TotalSize}, slots={MaxSlots}, payload={PayloadSize})";
}
