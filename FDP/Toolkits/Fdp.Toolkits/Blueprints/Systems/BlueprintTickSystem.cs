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

    private EntityQuery? _query1024;
    private EntityQuery? _query4096;
    private EntityQuery? _query16384;

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

        _query1024  ??= repo.Query().With<BlueprintBlackboard1024>().Build();
        _query4096  ??= repo.Query().With<BlueprintBlackboard4096>().Build();
        _query16384 ??= repo.Query().With<BlueprintBlackboard16384>().Build();

        TickTier_1024(repo, view, ecb, deltaTime);
        TickTier_4096(repo, view, ecb, deltaTime);
        TickTier_16384(repo, view, ecb, deltaTime);

        TickWorldSingletons(repo, view, ecb, deltaTime);
    }

    private unsafe void TickTier_1024(
        EntityRepository repo, ISimulationView view, IEntityCommandBuffer ecb, float deltaTime)
    {
        foreach (var entity in _query1024!)
        {
            ref var bb      = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
            ref byte memRef = ref Unsafe.As<BlueprintBlackboard1024, byte>(ref bb);
            byte* memory    = (byte*)Unsafe.AsPointer(ref memRef);

            ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(memory);
            if (header.MagicAndVersion != BlueprintBlackboardHeader.MagicValue) continue;

            int   slotCount = header.SlotCount;
            byte* slotTable = memory + sizeof(BlueprintBlackboardHeader);

            for (int i = 0; i < slotCount; i++)
            {
                ref var slot = ref Unsafe.AsRef<BlueprintSlotEntry>(
                    slotTable + i * BlueprintBlackboardPartitions.SlotEntrySize);

                if (!_registry.TryGetById(slot.BlueprintId, out var def)) continue;

                if (slot.StructureHash != (uint)def!.StructureHash) // DEBT-014 truncation
                {
                    ulong oldHash = slot.StructureHash;
                    BlueprintBlackboardPartitions.ResetSlot(memory, i, def.StructureHash);
                    if (def.InitDefault is not null)
                    {
                        var initSpan = MemoryMarshal.CreateSpan(
                            ref Unsafe.Add(ref memRef, slot.PayloadOffset),
                            slot.PayloadSize);
                        def.InitDefault(initSpan);
                    }
                    _logSink.OnHardReset(slot.BlueprintId, entity, oldHash, (ulong)def!.StructureHash);
                }

                if (def.Tick is not null)
                {
                    var tickSpan = MemoryMarshal.CreateSpan(
                        ref Unsafe.Add(ref memRef, slot.PayloadOffset),
                        slot.PayloadSize);
                    def.Tick(tickSpan, view, ecb, entity,
                             view.Time, deltaTime, slot.InstanceVersion);
                }
            }
        }
    }

    private unsafe void TickTier_4096(
        EntityRepository repo, ISimulationView view, IEntityCommandBuffer ecb, float deltaTime)
    {
        foreach (var entity in _query4096!)
        {
            ref var bb      = ref repo.GetComponentRW<BlueprintBlackboard4096>(entity);
            ref byte memRef = ref Unsafe.As<BlueprintBlackboard4096, byte>(ref bb);
            byte* memory    = (byte*)Unsafe.AsPointer(ref memRef);

            ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(memory);
            if (header.MagicAndVersion != BlueprintBlackboardHeader.MagicValue) continue;

            int   slotCount = header.SlotCount;
            byte* slotTable = memory + sizeof(BlueprintBlackboardHeader);

            for (int i = 0; i < slotCount; i++)
            {
                ref var slot = ref Unsafe.AsRef<BlueprintSlotEntry>(
                    slotTable + i * BlueprintBlackboardPartitions.SlotEntrySize);

                if (!_registry.TryGetById(slot.BlueprintId, out var def)) continue;

                if (slot.StructureHash != (uint)def!.StructureHash) // DEBT-014 truncation
                {
                    ulong oldHash = slot.StructureHash;
                    BlueprintBlackboardPartitions.ResetSlot(memory, i, def.StructureHash);
                    if (def.InitDefault is not null)
                    {
                        var initSpan = MemoryMarshal.CreateSpan(
                            ref Unsafe.Add(ref memRef, slot.PayloadOffset),
                            slot.PayloadSize);
                        def.InitDefault(initSpan);
                    }
                    _logSink.OnHardReset(slot.BlueprintId, entity, oldHash, (ulong)def!.StructureHash);
                }

                if (def.Tick is not null)
                {
                    var tickSpan = MemoryMarshal.CreateSpan(
                        ref Unsafe.Add(ref memRef, slot.PayloadOffset),
                        slot.PayloadSize);
                    def.Tick(tickSpan, view, ecb, entity,
                             view.Time, deltaTime, slot.InstanceVersion);
                }
            }
        }
    }

    private unsafe void TickTier_16384(
        EntityRepository repo, ISimulationView view, IEntityCommandBuffer ecb, float deltaTime)
    {
        foreach (var entity in _query16384!)
        {
            ref var bb      = ref repo.GetComponentRW<BlueprintBlackboard16384>(entity);
            ref byte memRef = ref Unsafe.As<BlueprintBlackboard16384, byte>(ref bb);
            byte* memory    = (byte*)Unsafe.AsPointer(ref memRef);

            ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(memory);
            if (header.MagicAndVersion != BlueprintBlackboardHeader.MagicValue) continue;

            int   slotCount = header.SlotCount;
            byte* slotTable = memory + sizeof(BlueprintBlackboardHeader);

            for (int i = 0; i < slotCount; i++)
            {
                ref var slot = ref Unsafe.AsRef<BlueprintSlotEntry>(
                    slotTable + i * BlueprintBlackboardPartitions.SlotEntrySize);

                if (!_registry.TryGetById(slot.BlueprintId, out var def)) continue;

                if (slot.StructureHash != (uint)def!.StructureHash) // DEBT-014 truncation
                {
                    ulong oldHash = slot.StructureHash;
                    BlueprintBlackboardPartitions.ResetSlot(memory, i, def.StructureHash);
                    if (def.InitDefault is not null)
                    {
                        var initSpan = MemoryMarshal.CreateSpan(
                            ref Unsafe.Add(ref memRef, slot.PayloadOffset),
                            slot.PayloadSize);
                        def.InitDefault(initSpan);
                    }
                    _logSink.OnHardReset(slot.BlueprintId, entity, oldHash, (ulong)def!.StructureHash);
                }

                if (def.Tick is not null)
                {
                    var tickSpan = MemoryMarshal.CreateSpan(
                        ref Unsafe.Add(ref memRef, slot.PayloadOffset),
                        slot.PayloadSize);
                    def.Tick(tickSpan, view, ecb, entity,
                             view.Time, deltaTime, slot.InstanceVersion);
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

            switch (tier)
            {
                case BlackboardTier.B1024:
                    EnsureAndTickSingleton<BlueprintBlackboard1024>(
                        repo, view, ecb, blueprintId, def!,
                        BlueprintBlackboard1024.TotalSize,
                        (byte)BlueprintBlackboard1024.MaxSlots,
                        deltaTime);
                    break;
                case BlackboardTier.B4096:
                    EnsureAndTickSingleton<BlueprintBlackboard4096>(
                        repo, view, ecb, blueprintId, def!,
                        BlueprintBlackboard4096.TotalSize,
                        (byte)BlueprintBlackboard4096.MaxSlots,
                        deltaTime);
                    break;
                case BlackboardTier.B16384:
                    EnsureAndTickSingleton<BlueprintBlackboard16384>(
                        repo, view, ecb, blueprintId, def!,
                        BlueprintBlackboard16384.TotalSize,
                        (byte)BlueprintBlackboard16384.MaxSlots,
                        deltaTime);
                    break;
            }
        }
    }

    private unsafe void EnsureAndTickSingleton<TBB>(
        EntityRepository repo, ISimulationView view, IEntityCommandBuffer ecb,
        int blueprintId, BlueprintDefinition def, int totalSize, byte maxSlots,
        float deltaTime)
        where TBB : unmanaged
    {
        // Lazy attach -- first encounter creates the singleton component
        if (!repo.HasSingleton<TBB>())
            repo.SetSingletonUnmanaged<TBB>(default);

        ref var bb      = ref repo.GetSingleton<TBB>();
        ref byte memRef = ref Unsafe.As<TBB, byte>(ref bb);
        byte* memory    = (byte*)Unsafe.AsPointer(ref memRef);

        // Initialize header if not yet done
        ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(memory);
        if (header.MagicAndVersion != BlueprintBlackboardHeader.MagicValue)
            BlueprintBlackboardPartitions.Initialize(memory, totalSize, maxSlots);

        // Attach slot if not yet attached
        if (!BlueprintBlackboardPartitions.TryGetSlotOffset(memory, blueprintId, out int payloadOffset))
        {
            if (!BlueprintBlackboardPartitions.TryAttach(
                    memory, blueprintId, def.StateSize, def.StructureHash, out payloadOffset))
                return; // tier capacity exhausted

            if (def.InitDefault is not null)
            {
                var initSpan = MemoryMarshal.CreateSpan(
                    ref Unsafe.Add(ref memRef, payloadOffset),
                    def.StateSize);
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
                var resetSpan = MemoryMarshal.CreateSpan(
                    ref Unsafe.Add(ref memRef, slot.PayloadOffset),
                    slot.PayloadSize);
                def.InitDefault(resetSpan);
            }
            _logSink.OnHardReset(blueprintId, Entity.Null, oldHash, (ulong)def.StructureHash);
        }

        if (def.Tick is not null)
        {
            var tickSpan = MemoryMarshal.CreateSpan(
                ref Unsafe.Add(ref memRef, slot.PayloadOffset),
                slot.PayloadSize);
            def.Tick(tickSpan, view, ecb, Entity.Null,
                     view.Time, deltaTime, slot.InstanceVersion);
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
