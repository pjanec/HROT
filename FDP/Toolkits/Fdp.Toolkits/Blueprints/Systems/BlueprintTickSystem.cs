using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;

namespace Fdp.Toolkit.Blueprints.Systems;

/// <summary>
/// Ticks all active Blueprint instances each frame (Simulation phase).
/// Per Runtime DD §6 + InlinePatches Q-12.2, Q-12.3, Q-12.4, Correction 2.
/// </summary>
[UpdateInPhase(SystemPhase.Simulation)]
[UpdateBefore(typeof(LocomotionDispatcherSystem))]
[UpdateBefore(typeof(WeaponDispatcherSystem))]
[UpdateBefore(typeof(InteractionDispatcherSystem))]
public sealed class BlueprintTickSystem : IEcsModuleSystem, IProfiledSystem
{
    private readonly BlueprintRegistry _registry;
    private readonly IReloadLogSink    _logSink;

    // ⭐ O3a / B3: one query per tier, built FROM the ladder, index-aligned with
    //   BlueprintTierTable.Ascending. ⛔ Was three named fields and three named methods.
    private EntityQuery?[]? _tierQueries;

    /// <summary>
    /// Optional frame-start hook -- wire from a higher-level module at startup (e.g. DebugProbe.NewTick).
    /// Per Debug DD §9.2: called at the start of each tick before any blueprint is ticked.
    /// </summary>
    public static Action? FrameStartCallback { get; set; }

    public string ProfileName => "BlueprintTickSystem";

    public BlueprintTickSystem(BlueprintRegistry registry)
        : this(registry, NullReloadLogSink.Instance) { }

    public BlueprintTickSystem(BlueprintRegistry registry, IReloadLogSink? logSink = null)
    {
        _registry = registry;
        _logSink  = logSink ?? NullReloadLogSink.Instance;
    }

    public void Execute(ISimulationView view, float deltaTime)
    {
        // Respect the engine's paused state: when the time controller sets deltaTime=0
        // (e.g. during a debug breakpoint), skip blueprint execution to freeze the
        // rewound pre-tick snapshot. Mirrors BTreeTickSystem and HsmTickSystem.
        if (deltaTime <= 0f) return;

        // Per Debug DD §9.2: notify the debug session of the tick boundary before running blueprints.
        FrameStartCallback?.Invoke();

        var repo = (EntityRepository)view;
        var ecb  = view.GetCommandBuffer();

        var tiers = BlueprintTierTable.Ascending;

        // ⭐⭐ O7c-②: the cached per-tier queries now come from BlueprintTierTable, so BTreeTickSystem
        //   (and later the HSM tick) enumerate store-carrying entities the SAME way. 📄 §31.7.
        // ⛔ It also CLOSES A GAP this loop had: it never checked IsRegistered, while that member's own
        //   doc says it exists because "a table-driven walk that QUERIES every tier must skip the ones
        //   this world never registered". ⚠ Harmless here in practice — With<T>() only sets a mask bit,
        //   so an unregistered tier yields an EMPTY query rather than throwing — but relying on that is
        //   relying on an implementation detail of QueryBuilder, and the guard costs one branch once.
        _tierQueries ??= BlueprintTierTable.BuildTierQueries(repo);

        // ⚠ Smallest-first, which is the order the three named calls ran in. An entity carries at
        //   most one tier, so the order is not observable — it is preserved anyway, because
        //   "not observable" is a claim and preserving it costs nothing.
        for (int t = 0; t < tiers.Count; t++)
        {
            var q = _tierQueries[t];
            if (q is null) continue;            // tier not registered on this world
            TickTier(repo, view, ecb, deltaTime, tiers[t], q);
        }

        TickWorldSingletons(repo, view, ecb, deltaTime);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>ONE tier walk.</b> <c>O3a</c> / task <c>B3</c> — 📄 <c>DESIGN §17.2</c>.
    ///
    /// <para>📐 This body was <b>three verbatim ~78-line copies</b> — <c>TickTier_1024</c>,
    /// <c>_4096</c>, <c>_16384</c> — differing only in the component type named on two lines.
    /// (Verified verbatim by normalising the tier number before the collapse.) A fourth tier was a
    /// fourth copy.</para>
    ///
    /// <para>⚠ The memory resolution moved to <see cref="BlueprintTierSpec.Memory"/>, which uses the
    /// same <c>Unsafe.As&lt;TTier, byte&gt;</c> this method already used. ⛔ The pointer is used only
    /// within this call, and nothing here adds or removes a component on the entity.</para>
    /// </summary>
    private unsafe void TickTier(
        EntityRepository repo, ISimulationView view, IEntityCommandBuffer ecb, float deltaTime,
        BlueprintTierSpec spec, EntityQuery query)
    {
        foreach (var entity in query)
        {
            byte* memory = spec.Memory(repo, entity);

            ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(memory);
            if (header.MagicAndVersion != BlueprintBlackboardHeader.MagicValue) continue;

            int   slotCount = header.SlotCount;
            byte* slotTable = memory + sizeof(BlueprintBlackboardHeader);

            for (int i = 0; i < slotCount; i++)
            {
                ref var slot = ref Unsafe.AsRef<BlueprintSlotEntry>(
                    slotTable + i * BlueprintBlackboardPartitions.SlotEntrySize);

                // ⭐⭐⭐ A4/O0 + D1' — FILTER ON THE DECLARED KIND, NEVER ON A REGISTRY MISS.
                //   📐 The line below used to be the whole filter, and it worked ONLY because
                //      BlueprintRegistry happens not to know an FNV stateful slot key (F7) — an
                //      accident, not a filter. This walker now runs on CGF as well as the Editor,
                //      beside BTree/HSM occurrences in the SAME store, so the filter has to be a
                //      declaration. A3 made every production attach declare its kind.
                if (BlueprintBlackboardPartitions.GetSlotKind(memory, i) != OccurrenceKind.Blueprint)
                    continue;

                // ⚠ Still looked up, but it is no longer the filter: for a slot DECLARED Blueprint a
                //   miss means the definition is absent from this host's registry, which is a real
                //   condition (an asset this node did not compile) rather than "not ours".
                if (!_registry.TryGetById(slot.BlueprintId, out var def)) continue;

                if (slot.StructureHash != (uint)def!.StructureHash) // DEBT-014 truncation
                {
                    ulong oldHash = slot.StructureHash;
                    BlueprintBlackboardPartitions.ResetSlot(memory, i, def.StructureHash);
                    if (def.InitDefault is not null)
                    {
                        var initSpan = new Span<byte>(
                            memory + slot.PayloadOffset, slot.PayloadSize);
                        def.InitDefault(initSpan);
                    }
                    _logSink.OnHardReset(slot.BlueprintId, entity, oldHash, (ulong)def!.StructureHash);
                }

                // Q#14: dispatch any custom events this instance subscribes to (before Tick, so the tick
                // sees handler effects). HasEvent-gated inside DispatchForSlot — absent events cost nothing.
                if (def.EventHandlers is not null && def.EventHandlers.Count > 0)
                {
                    var evSpan = new Span<byte>(
                        memory + slot.PayloadOffset, slot.PayloadSize);
                    BlueprintEventDispatch.DispatchForSlot(
                        def, evSpan, repo.Bus, view, ecb, entity, view.Time, deltaTime);
                }

                if (def.Tick is not null)
                {
                    var tickSpan = new Span<byte>(
                        memory + slot.PayloadOffset, slot.PayloadSize);
                    def.Tick(tickSpan, view, ecb, entity,
                             view.Time, deltaTime, slot.InstanceVersion);

                    // ⭐⭐⭐ C-tick: ONE non-frozen tick of THIS asset on THIS entity.
                    // ⭐ Frozen comes free -- Execute returns at `deltaTime <= 0f`, so this line is
                    //   unreachable while paused, which is exactly what the ruling needs.
                    // ⛔ AFTER def.Tick, not before: the counter means "a tick HAS RUN", and the
                    //   monitor diffs the value the tick produced.
                    BlueprintAssetTick.Bump(slot.BlueprintId, entity);
                }
            }
        }
    }

    private unsafe void TickWorldSingletons(
        EntityRepository repo, ISimulationView view, IEntityCommandBuffer ecb, float deltaTime)
    {
        foreach (var (blueprintId, tier) in _registry.GetAllWorldSingletons())
        {
            if (!_registry.TryGetById(blueprintId, out var def)) continue;

            // ⭐ O3a / B3: was a three-arm switch, each arm naming a tier type and re-quoting its
            //   TotalSize and MaxSlots. The spec carries all three.
            EnsureAndTickSingleton(
                repo, view, ecb, blueprintId, def!, BlueprintTierTable.ByTier(tier), deltaTime);
        }
    }

    private unsafe void EnsureAndTickSingleton(
        EntityRepository repo, ISimulationView view, IEntityCommandBuffer ecb,
        int blueprintId, BlueprintDefinition def, BlueprintTierSpec spec,
        float deltaTime)
    {
        // Lazy attach -- first encounter creates the singleton component
        byte* memory = spec.EnsureSingletonMemory(repo);

        // Initialize header if not yet done
        ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(memory);
        if (header.MagicAndVersion != BlueprintBlackboardHeader.MagicValue)
            BlueprintBlackboardPartitions.Initialize(memory, spec.TotalSize, (byte)spec.MaxSlots);

        // Attach slot if not yet attached
        if (!BlueprintBlackboardPartitions.TryGetSlotOffset(memory, blueprintId, out int payloadOffset))
        {
            // A3/D1': declared, not inferred — O0's walker must filter on this Kind rather than on a
            // BlueprintRegistry miss (F7: that only ever worked because the registry happens not to
            // know an FNV stateful key).
            if (!BlueprintBlackboardPartitions.TryAttach(
                    memory, blueprintId, def.StateSize, def.StructureHash,
                    OccurrenceKind.Blueprint, out payloadOffset))
                return; // tier capacity exhausted

            if (def.InitDefault is not null)
            {
                var initSpan = new Span<byte>(memory + payloadOffset, def.StateSize);
                def.InitDefault(initSpan);
            }
        }

        // Locate slot for reconciliation + tick
        byte* slotTable = memory + sizeof(BlueprintBlackboardHeader);
        int slotIndex = FindSlotIndex(slotTable, header.SlotCount, blueprintId);
        ref var slot = ref Unsafe.AsRef<BlueprintSlotEntry>(
            slotTable + slotIndex * BlueprintBlackboardPartitions.SlotEntrySize);

        // Reload reconciliation
        if (slot.StructureHash != (uint)def.StructureHash) // DEBT-014 truncation
        {
            ulong oldHash = slot.StructureHash;
            BlueprintBlackboardPartitions.ResetSlot(memory, slotIndex, def.StructureHash);
            if (def.InitDefault is not null)
            {
                var resetSpan = new Span<byte>(memory + slot.PayloadOffset, slot.PayloadSize);
                def.InitDefault(resetSpan);
            }
            _logSink.OnHardReset(blueprintId, Entity.Null, oldHash, (ulong)def.StructureHash);
        }

        if (def.Tick is not null)
        {
            var tickSpan = new Span<byte>(memory + slot.PayloadOffset, slot.PayloadSize);
            def.Tick(tickSpan, view, ecb, Entity.Null,
                     view.Time, deltaTime, slot.InstanceVersion);

            // ⭐ C-tick, world-singleton arm. ⚠ Entity.Null IS this instance's identity here -- a
            //   world singleton has exactly one instance, and keying it by Entity.Null keeps the
            //   (asset, entity) shape rather than inventing a second one.
            BlueprintAssetTick.Bump(blueprintId, Entity.Null);
        }
    }

    private static unsafe int FindSlotIndex(byte* slotTable, int slotCount, int blueprintId)
    {
        for (int i = 0; i < slotCount; i++)
        {
            ref var s = ref Unsafe.AsRef<BlueprintSlotEntry>(
                slotTable + i * BlueprintBlackboardPartitions.SlotEntrySize);
            if (s.BlueprintId == blueprintId) return i;
        }
        return -1;
    }
}
