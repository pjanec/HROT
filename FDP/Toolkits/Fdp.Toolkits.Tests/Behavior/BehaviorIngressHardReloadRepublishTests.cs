using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Fbt;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests;

/// <summary>
/// S2-3 Task 2 test: verifies that on a Hard Reload, the <see cref="AiHotReloadCoordinator"/>
/// fires <see cref="AiHotReloadCoordinator.OnHardReloadCompleted"/> with the reloaded behavior IDs,
/// and that a subscriber using this event can re-publish <see cref="AssignBehaviorEvent"/>
/// for every entity running a reloaded BTree behavior — driving
/// <see cref="BehaviorIngressSystem"/> to detach old ghost slots and re-provision.
///
/// Proves:
/// <list type="number">
///   <item><c>HardReload_RepublishesAssignBehaviorEvent</c> — the coordinator fires
///         <see cref="AiHotReloadCoordinator.OnHardReloadCompleted"/>; a wired subscriber
///         publishes one <see cref="AssignBehaviorEvent"/> per entity running a reloaded behavior;
///         BehaviorIngressSystem processes the event and re-provisions the slot (confirms
///         detach+reattach path, not inline ResetSlot).</item>
/// </list>
/// </summary>
public sealed unsafe class BehaviorIngressHardReloadRepublishTests : IDisposable
{
    private readonly EntityRepository   _world;
    private readonly BehaviorRegistry   _liveRegistry;
    private readonly BlueprintRegistry  _blueprintRegistry;
    private readonly BehaviorIngressSystem _ingressSys;
    private readonly AiHotReloadCoordinator _coordinator;

    public BehaviorIngressHardReloadRepublishTests()
    {
        _world             = TestWorldFactory.Create();
        _world.RegisterComponent<BlueprintBlackboard1024>();
        _world.RegisterComponent<BlueprintBlackboard4096>();
        _world.RegisterComponent<BlueprintBlackboard16384>();
        _liveRegistry      = new BehaviorRegistry();
        _blueprintRegistry = new BlueprintRegistry();
        _ingressSys        = new BehaviorIngressSystem(_liveRegistry);
        _coordinator       = new AiHotReloadCoordinator(
            _liveRegistry, _blueprintRegistry, new AiHotReloadCoordinatorOptions());

        // Wire the hard-reload subscriber: for each reloaded behavior ID, find all entities
        // running that behavior and re-publish AssignBehaviorEvent so BehaviorIngressSystem
        // detaches/re-provisions ghost slots. This is the "injected callback" hook (S2-3 §10 Flaw 2).
        _coordinator.OnHardReloadCompleted += reloadedIds =>
        {
            var reloadedSet = new HashSet<int>(reloadedIds);
            // Enumerate entities with BehaviorState whose ActiveBehaviorHash was reloaded.
            var query = _world.Query().With<BehaviorState>().Build();
            var entitiesToRepublish = new List<(Entity e, string name)>();
            foreach (var entity in query)
            {
                ref readonly var state = ref _world.GetComponentRO<BehaviorState>(entity);
                if (!reloadedSet.Contains(state.ActiveBehaviorHash)) continue;

                // Recover behavior name from registry (JsonParams = "" is acceptable per spec:
                // ParseParams re-writes baked defaults; runtime per-assignment JSON is DEBT-AIB-021).
                if (!_liveRegistry.TryGetName(state.ActiveBehaviorHash, out var name)) continue;
                entitiesToRepublish.Add((entity, name));
            }
            foreach (var (entity, name) in entitiesToRepublish)
            {
                _world.Bus.PublishManaged(new AssignBehaviorEvent
                {
                    Entity       = entity,
                    BehaviorName = name,
                    JsonParams   = string.Empty,
                });
            }
        };
    }

    public void Dispose()
    {
        _coordinator.Dispose();
        _world.Dispose();
    }

    // ── Helper ────────────────────────────────────────────────────────────────────

    private static BehaviorDefinition MakeStatefulDefinition(
        string name, int id, IReadOnlyList<StatefulSlotInfo> slots)
    {
        var actionReg = new ActionRegistry<BrainBlackboard, BTreeContext>();
        var blob = new BehaviorTreeBlob
        {
            TreeName    = name,
            Nodes       = new[] { new NodeDefinition { Type = NodeType.Action, RawPayloadIndex = 0, SubtreeOffset = 1 } },
            MethodNames = new[] { "noop" },
            FloatParams = Array.Empty<float>(),
            IntParams   = Array.Empty<int>(),
        };
        var interpreter = new Interpreter<BrainBlackboard, BTreeContext>(blob, actionReg);
        return new BehaviorDefinition
        {
            Name                 = name,
            BrainTier            = BehaviorConstants.BrainTierBTree,
            BTreeInterpreter     = interpreter,
            StatefulWorkingSlots = slots,
        };
    }

    private void ApplyIngressEvents()
    {
        _world.Bus.SwapBuffers();
        _ingressSys.Execute(_world, 0.016f);
    }

    // ── Test ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// S2-3 Task 2: on a Hard Reload, the coordinator fires OnHardReloadCompleted; the wired
    /// subscriber re-publishes AssignBehaviorEvent for the affected entity; BehaviorIngressSystem
    /// processes it and re-provisions the slot at the new (larger) manifest size.
    ///
    /// Also proves that inline ResetSlot (which zeroes payload but keeps old PayloadSize) is
    /// NOT taken — the slot is detach+reattached at the correct new size.
    /// </summary>
    [Fact]
    public void HardReload_RepublishesAssignBehaviorEvent()
    {
        const string BehaviorName = "HardReloadRepublishBehavior";
        const int    BehaviorId   = 8401;
        int slotKey = 0x3DD04;
        const uint hashV1 = 0xFEDC1234u;

        // ── Initial assignment: behavior with one 4-byte stateful slot ────────────
        var slotsV1 = new StatefulSlotInfo[]
        {
            new StatefulSlotInfo(slotKey, 4, hashV1),
        };
        _liveRegistry.Register(BehaviorId, BehaviorName, MakeStatefulDefinition(BehaviorName, BehaviorId, slotsV1));

        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new BehaviorState());
        _world.AddComponent(entity, new BrainBlackboard());
        _world.AddComponent(entity, new BrainBTreeState());

        // Fire initial assign event.
        _world.Bus.PublishManaged(new AssignBehaviorEvent
        {
            Entity       = entity,
            BehaviorName = BehaviorName,
            JsonParams   = string.Empty,
        });
        ApplyIngressEvents();

        // Verify entity is running the behavior and slot is attached at 4 bytes.
        Assert.Equal(BehaviorId, _world.GetComponentRO<BehaviorState>(entity).ActiveBehaviorHash);

        // Verify slot attached and record initial InstanceVersion.
        uint versionBeforeReload = 0;
        if (_world.HasComponent<BlueprintBlackboard16384>(entity))
        {
            ref var tier = ref _world.GetComponentRW<BlueprintBlackboard16384>(entity);
            fixed (byte* mem = tier.Memory)
            {
                Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out _),
                    "Slot must be attached after initial assign");
                ref var hdr = ref Unsafe.AsRef<BlueprintBlackboardHeader>(mem);
                byte* tbl = mem + Unsafe.SizeOf<BlueprintBlackboardHeader>();
                for (int i = 0; i < hdr.SlotCount; i++)
                {
                    ref var e = ref Unsafe.AsRef<BlueprintSlotEntry>(
                        tbl + i * BlueprintBlackboardPartitions.SlotEntrySize);
                    if (e.BlueprintId == slotKey) { versionBeforeReload = e.InstanceVersion; break; }
                }
            }
        }
        else if (_world.HasComponent<BlueprintBlackboard4096>(entity))
        {
            ref var tier = ref _world.GetComponentRW<BlueprintBlackboard4096>(entity);
            fixed (byte* mem = tier.Memory)
            {
                Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out _),
                    "Slot must be attached after initial assign");
                ref var hdr = ref Unsafe.AsRef<BlueprintBlackboardHeader>(mem);
                byte* tbl = mem + Unsafe.SizeOf<BlueprintBlackboardHeader>();
                for (int i = 0; i < hdr.SlotCount; i++)
                {
                    ref var e = ref Unsafe.AsRef<BlueprintSlotEntry>(
                        tbl + i * BlueprintBlackboardPartitions.SlotEntrySize);
                    if (e.BlueprintId == slotKey) { versionBeforeReload = e.InstanceVersion; break; }
                }
            }
        }
        else
        {
            ref var tier = ref _world.GetComponentRW<BlueprintBlackboard1024>(entity);
            fixed (byte* mem = tier.Memory)
            {
                Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out _),
                    "Slot must be attached after initial assign");
                ref var hdr = ref Unsafe.AsRef<BlueprintBlackboardHeader>(mem);
                byte* tbl = mem + Unsafe.SizeOf<BlueprintBlackboardHeader>();
                for (int i = 0; i < hdr.SlotCount; i++)
                {
                    ref var e = ref Unsafe.AsRef<BlueprintSlotEntry>(
                        tbl + i * BlueprintBlackboardPartitions.SlotEntrySize);
                    if (e.BlueprintId == slotKey) { versionBeforeReload = e.InstanceVersion; break; }
                }
            }
        }

        // ── Simulate Hard Reload: use EnqueueReloadForTest with a synthetic registrar ─
        // The registrar injects the updated (V2, larger) behavior definition into the
        // staging BehaviorRegistry that ApplyReload builds internally.
        const uint hashV2 = 0x9988AABBu; // different hash (struct changed layout)
        var slotsV2 = new StatefulSlotInfo[]
        {
            new StatefulSlotInfo(slotKey, 16, hashV2), // grew from 4 to 16 bytes
        };

        // Set static state for our test registrar helper before enqueuing.
        TestRegistrarHelper.Name  = BehaviorName;
        TestRegistrarHelper.Id    = BehaviorId;
        TestRegistrarHelper.Slots = slotsV2;

        var registerMethod = typeof(TestRegistrarHelper).GetMethod(
            nameof(TestRegistrarHelper.Register),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
        var parameters = registerMethod.GetParameters()
            .Select((p, i) => new RegistrarParameter(p.Name ?? $"arg{i}", p.ParameterType, i))
            .ToList();
        var resolvedRegistrars = new[] { new ResolvedRegistrar(typeof(TestRegistrarHelper), registerMethod, parameters) };

        var testAlc = new AssemblyLoadContext("TestHardReload", isCollectible: true);
        _coordinator.EnqueueReloadForTest(resolvedRegistrars, testAlc);

        // Attach listener BEFORE DrainPendingCallbacks.
        bool hardReloadEventFired = false;
        IReadOnlyList<int>? receivedIds = null;
        _coordinator.OnHardReloadCompleted += ids =>
        {
            hardReloadEventFired = true;
            receivedIds = ids;
        };

        // DrainPendingCallbacks calls ApplyReload → fires OnHardReloadCompleted → subscriber
        // publishes AssignBehaviorEvent into the bus.
        _coordinator.DrainPendingCallbacks();

        // (1) Assert the hard-reload event was fired with the correct behavior ID.
        Assert.True(hardReloadEventFired, "OnHardReloadCompleted must be fired on hard reload");
        Assert.NotNull(receivedIds);
        Assert.Contains(BehaviorId, receivedIds!);

        // (2) Apply the BehaviorIngress events published by the subscriber.
        ApplyIngressEvents();

        // (3) Assert the slot was re-provisioned at the new size (16 bytes, aligned to 16).
        // This proves the detach+reattach path was taken (not inline ResetSlot which would
        // keep the old 4-byte PayloadSize).
        void AssertSlotReprovisioned(byte* mem)
        {
            Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out _),
                "Slot must be re-attached after hard reload");

            ref var hdr = ref Unsafe.AsRef<BlueprintBlackboardHeader>(mem);
            byte* tbl = mem + Unsafe.SizeOf<BlueprintBlackboardHeader>();
            ushort newPayloadSize = 0;
            uint   newVersion     = 0;
            for (int i = 0; i < hdr.SlotCount; i++)
            {
                ref var e = ref Unsafe.AsRef<BlueprintSlotEntry>(
                    tbl + i * BlueprintBlackboardPartitions.SlotEntrySize);
                if (e.BlueprintId == slotKey)
                {
                    newPayloadSize = e.PayloadSize;
                    newVersion     = e.InstanceVersion;
                    break;
                }
            }

            // Slot must now have the new (larger) payload size: 16 bytes (already aligned).
            // NOT the old 4 bytes (which inline ResetSlot would have kept).
            Assert.Equal(16, (int)newPayloadSize);

            // InstanceVersion must have been reset to 1 by TryAttach (detach+reattach path).
            // After detach+reattach, TryAttach always sets InstanceVersion = 1.
            Assert.Equal(1u, newVersion);
        }

        if (_world.HasComponent<BlueprintBlackboard16384>(entity))
        {
            ref var tier = ref _world.GetComponentRW<BlueprintBlackboard16384>(entity);
            fixed (byte* mem = tier.Memory) AssertSlotReprovisioned(mem);
        }
        else if (_world.HasComponent<BlueprintBlackboard4096>(entity))
        {
            ref var tier = ref _world.GetComponentRW<BlueprintBlackboard4096>(entity);
            fixed (byte* mem = tier.Memory) AssertSlotReprovisioned(mem);
        }
        else
        {
            ref var tier = ref _world.GetComponentRW<BlueprintBlackboard1024>(entity);
            fixed (byte* mem = tier.Memory) AssertSlotReprovisioned(mem);
        }
    }

    // ── TestRegistrarHelper ───────────────────────────────────────────────────────

    /// <summary>
    /// Test-seam registrar: invoked by the coordinator's ApplyReload path (test-seam variant)
    /// to populate the staging BehaviorRegistry with a known behavior definition.
    /// Static state is set before EnqueueReloadForTest is called.
    /// </summary>
    private static class TestRegistrarHelper
    {
        public static string Name  { get; set; } = "TestBehavior";
        public static int    Id    { get; set; } = 0;
        public static IReadOnlyList<StatefulSlotInfo> Slots { get; set; } = Array.Empty<StatefulSlotInfo>();

        // Signature: BehaviorRegistry, BlueprintRegistryStaging, ActionRegistry<BrainBlackboard, BTreeContext>
        // — exactly the three supported injectable types in ResolveRegistrarArgument.
        public static void Register(
            BehaviorRegistry behaviorRegistry,
            BlueprintRegistryStaging blueprintStaging,
            ActionRegistry<BrainBlackboard, BTreeContext> actionRegistry)
        {
            var actionReg = new ActionRegistry<BrainBlackboard, BTreeContext>();
            var blob = new BehaviorTreeBlob
            {
                TreeName    = Name,
                Nodes       = new[] { new NodeDefinition { Type = NodeType.Action, RawPayloadIndex = 0, SubtreeOffset = 1 } },
                MethodNames = new[] { "noop" },
                FloatParams = Array.Empty<float>(),
                IntParams   = Array.Empty<int>(),
            };
            var interpreter = new Interpreter<BrainBlackboard, BTreeContext>(blob, actionReg);
            behaviorRegistry.Register(Id, Name, new BehaviorDefinition
            {
                Name                 = Name,
                BrainTier            = BehaviorConstants.BrainTierBTree,
                BTreeInterpreter     = interpreter,
                StatefulWorkingSlots = Slots,
            });
        }
    }
}
