using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// Tests for FIX2-009: CaptureInstanceStateFromDefinition must read live slot bytes
/// from BlueprintBlackboard and project them into named field values.
/// </summary>
public sealed class FIX2_009_InstanceStateInspectionTests
{
    private static readonly Entity TestEntity = new Entity(1, 0);

    private static CompileOptions DebugOptions => new CompileOptions(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    /// <summary>
    /// FIX2-009 SC: Compile an Instance blueprint with a float variable,
    /// populate a real BlueprintBlackboard1024 component slot with a known value,
    /// pause the debug session on the entity, then assert the state snapshot
    /// contains the expected field name and value.
    /// </summary>
    [Fact]
    public unsafe void StateInspection_Instance_ReturnsNonEmptyFields()
    {
        // --- 1. Compile blueprint through the production compile path ---
        var asset = BlueprintAssetBuilder
            .Instance("StateInspectionTest")
            .WithVariable("Health", typeof(float))
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();

        var compiler = new BlueprintCompiler();
        var result   = compiler.Compile(asset, DebugOptions);
        Assert.True(result.Succeeded,
            string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var debugMap = result.DebugMap!;
        var assetGuid   = debugMap.AssetId;
        var blueprintId = result.BlueprintId;

        // StateLayout must have at least "Health" for this test to be meaningful.
        Assert.NotEmpty(debugMap.StateLayout.Fields);
        var healthField = debugMap.StateLayout.Fields.Single(f => f.Name == "Health");
        const float ExpectedHealth = 42.5f;

        // --- 2. Build a BlueprintBlackboard1024 with a real slot and written value ---
        int stateSize = healthField.OffsetBytes + healthField.SizeBytes;  // 16 + 4 = 20
        var buffer = new byte[BlueprintBlackboard1024.TotalSize];
        int payloadOffset;
        unsafe
        {
            fixed (byte* mem = buffer)
            {
                BlueprintBlackboardPartitions.Initialize(mem,
                    BlueprintBlackboard1024.TotalSize,
                    BlueprintBlackboard1024.MaxSlots);

                bool ok = BlueprintBlackboardPartitions.TryAttach(
                    mem, blueprintId, stateSize, /*structureHash*/ 0, out payloadOffset);
                Assert.True(ok, "TryAttach must succeed for a fresh blackboard");

                // Write the known Health value at the correct field offset.
                *(float*)(mem + payloadOffset + healthField.OffsetBytes) = ExpectedHealth;
            }
        }
        // Reinterpret the byte[] as BlueprintBlackboard1024 (blittable struct).
        var bb = MemoryMarshal.Read<BlueprintBlackboard1024>(buffer.AsSpan());

        // --- 3. Set up BlueprintDebugSession with the populated blackboard view ---
        var registry = new BlueprintRegistry();
        registry.RegisterInstance(blueprintId, new BlueprintDefinition
        {
            Name          = "StateInspectionTest",
            Kind          = BlueprintDispatchKind.Instance,
            StructureHash = debugMap.StructureHash,
            StateSize     = stateSize,
        });

        var view    = new BlackboardView(bb);
        var tc      = new MockTimeController();
        var session = new BlueprintDebugSession(registry, view, tc);
        session.RegisterDebugMap(debugMap);

        // --- 4. Simulate a breakpoint hit to pause the session ---
        var probeNodeGuid = Guid.NewGuid();
        session.SetBreakpoint(assetGuid, Guid.Empty, probeNodeGuid);
        session.OnNodeEnter(TestEntity, probeNodeGuid.ToString("D"));
        Assert.True(session.IsPaused, "Session must be paused after breakpoint fires");

        // --- 5. Capture state snapshot and assert fields ---
        var snapshot = session.GetCurrentStateSnapshot();
        Assert.NotNull(snapshot);
        Assert.NotEmpty(snapshot.FieldValues);
        Assert.True(snapshot.FieldValues.ContainsKey("Health"),
            "FieldValues must contain the Health field");
        Assert.Equal(ExpectedHealth, (float)snapshot.FieldValues["Health"]);
    }

    // ---- Helpers ------------------------------------------------------------

    /// <summary>
    /// Minimal ISimulationView that returns a fixed BlueprintBlackboard1024.
    /// </summary>
    private sealed class BlackboardView : ISimulationView
    {
        private BlueprintBlackboard1024 _bb;

        public BlackboardView(BlueprintBlackboard1024 bb) { _bb = bb; }

        public uint  Tick => 0;
        public float Time => 0f;

        public bool HasComponent<T>(Entity e) where T : unmanaged
            => typeof(T) == typeof(BlueprintBlackboard1024);

        public ref readonly T GetComponentRO<T>(Entity e) where T : unmanaged
        {
            if (typeof(T) == typeof(BlueprintBlackboard1024))
                return ref Unsafe.As<BlueprintBlackboard1024, T>(ref _bb);
            throw new NotImplementedException();
        }

        public bool IsAlive(Entity e) => true;
        public bool HasManagedComponent<T>(Entity e) where T : class => false;
        public T GetManagedComponentRO<T>(Entity e) where T : class
            => throw new NotImplementedException();
        public System.Collections.Generic.IReadOnlyList<T> ReadManagedEvents<T>()
            => throw new NotImplementedException();
        public ReadOnlySpan<T> ReadEvents<T>() where T : unmanaged
            => throw new NotImplementedException();
        public QueryBuilder Query() => throw new NotImplementedException();
        public IEntityCommandBuffer GetCommandBuffer() => throw new NotImplementedException();
    }
}
