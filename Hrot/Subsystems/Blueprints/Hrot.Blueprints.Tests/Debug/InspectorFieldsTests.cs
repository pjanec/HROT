using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Partitioning;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Tests.Builders;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// Tests for BF-BATCH-INSPECTOR-FIELDS: Instance state variable values
/// must be visible in the runtime inspector for latent blueprints.
/// </summary>
[Collection(nameof(DebugProbeCollection))]
public sealed class InspectorFieldsTests
{
    private static CompileOptions DebugOptions => new CompileOptions(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    // ---- Invariant #1: Compiler produces correct StateLayout offsets ----

    /// <summary>
    /// Latent Instance blueprint (Delay node): StateLayout field Count must have
    /// OffsetBytes == 16 (after the 16-byte BlueprintLatentCursor).
    /// </summary>
    [Fact]
    public void DebugMap_LatentInstance_StateLayoutHasVarAtPostCursorOffset()
    {
        // Build a latent Instance blueprint: Entry -> Delay(1.0) -> Return,
        // with a Count:int variable.
        var asset = BlueprintAssetBuilder
            .Instance("InspectorLatent")
            .WithVariable("Count", typeof(int))
            .WithGraph("Tick", g => g
                .Entry()
                .Delay(1.0f)
                .Return())
            .Build();

        var compiler = new BlueprintCompiler();
        var result   = compiler.Compile(asset, DebugOptions);

        Assert.True(result.Succeeded,
            string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var map = result.DebugMap;
        Assert.NotNull(map);
        Assert.NotEmpty(map.StateLayout.Fields);

        var countField = map.StateLayout.Fields.SingleOrDefault(f => f.Name == "Count");
        Assert.NotNull(countField);
        Assert.Equal(4, countField.SizeBytes);              // int = 4 bytes
        Assert.Equal(16, countField.OffsetBytes);            // after 16-byte cursor

        // Every Instance blueprint's state struct starts with Cursor (16 bytes).
        // Verify a simple non-suspend Instance blueprint also has Count at offset 16.
        var simpleAsset = BlueprintAssetBuilder
            .Instance("InspectorSimple")
            .WithVariable("Count", typeof(int))
            .WithGraph("Tick", g => g
                .Entry()
                .Return())
            .Build();

        var simpleResult = compiler.Compile(simpleAsset, DebugOptions);
        Assert.True(simpleResult.Succeeded);

        var simpleMap = simpleResult.DebugMap;
        var simpleCount = simpleMap.StateLayout.Fields.Single(f => f.Name == "Count");
        Assert.Equal(16, simpleCount.OffsetBytes);  // cursor always present in Instance state
    }

    // ---- Invariant #2: ReadInstanceState reads field value from buffer ----

    /// <summary>
    /// ReadInstanceState with a synthetic buffer containing cursor + Count=7 at
    /// offset 16, plus a DebugStateLayout with Count@16:int. Asserts Count == 7.
    /// </summary>
    [Fact]
    public unsafe void ReadInstanceState_WithLayout_ReturnsFieldValue()
    {
        const int blueprintId = 42;
        const int payloadOffset = 64;  // after header (32) + one slot entry (16) + alignment

        // Build a minimal blackboard buffer:
        // [0..31]  BlueprintBlackboardHeader
        // [32..47] BlueprintSlotEntry (one slot)
        // [48..63] padding to align payload
        // [64..79] BlueprintLatentCursor (16 bytes)
        // [80..83] Count = 7 (int, 4 bytes)
        int totalSize = payloadOffset + 32;  // extra space
        byte[] buffer = new byte[totalSize];

        fixed (byte* p = buffer)
        {
            // Header
            ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(p);
            header.MagicAndVersion = BlueprintBlackboardHeader.MagicValue;
            header.SlotCount = 1;
            header.MaxSlots = 1;
            header.PayloadStart = (ushort)payloadOffset;
            header.PayloadSize = (ushort)(totalSize - payloadOffset);
            header.PayloadFree = (ushort)(totalSize - payloadOffset);

            // Slot entry
            ref var slot = ref Unsafe.AsRef<BlueprintSlotEntry>(p + sizeof(BlueprintBlackboardHeader));
            slot.BlueprintId = blueprintId;
            slot.PayloadOffset = (ushort)payloadOffset;
            slot.PayloadSize = 20;  // cursor(16) + Count(4)
            slot.InstanceVersion = 1;
            slot.StructureHash = 0;

            // Write Cursor (all zeros, not used in this test)
            // Write Count = 7 at payloadOffset + 16
            *(int*)(p + payloadOffset + 16) = 7;
        }

        var stateLayout = new DebugStateLayout
        {
            Fields = new[]
            {
                new StateLayoutField("Count", "System.Int32", OffsetBytes: 16, SizeBytes: 4),
            },
        };

        var outFields = new Dictionary<string, object>();
        BlueprintDebugSession.ReadInstanceState(
            buffer, blueprintId, stateLayout, def: null, outFields, out var cursor);

        Assert.NotNull(cursor);
        Assert.True(outFields.ContainsKey("Count"),
            $"FieldValues must contain 'Count'. Actual keys: {string.Join(", ", outFields.Keys)}");
        Assert.Equal(7, (int)outFields["Count"]);
    }

    // ---- Invariant #3: StateFields fallback works when DebugMap not registered ----

    /// <summary>
    /// ReadInstanceState with stateLayout=null (simulating full build where DebugMap
    /// is not registered) must still return field values via the BlueprintDefinition.
    /// StateFields fallback. Uses synthetic buffer with cursor + Count=7 at offset 16
    /// and a BlueprintDefinition whose StateFields has Count@16:int.
    /// </summary>
    [Fact]
    public unsafe void ReadInstanceState_WithoutLayout_FallsBackToStateFields()
    {
        const int blueprintId = 42;
        const int payloadOffset = 64;

        int totalSize = payloadOffset + 32;
        byte[] buffer = new byte[totalSize];

        fixed (byte* p = buffer)
        {
            ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(p);
            header.MagicAndVersion = BlueprintBlackboardHeader.MagicValue;
            header.SlotCount = 1;
            header.MaxSlots = 1;
            header.PayloadStart = (ushort)payloadOffset;
            header.PayloadSize = (ushort)(totalSize - payloadOffset);
            header.PayloadFree = (ushort)(totalSize - payloadOffset);

            ref var slot = ref Unsafe.AsRef<BlueprintSlotEntry>(p + sizeof(BlueprintBlackboardHeader));
            slot.BlueprintId = blueprintId;
            slot.PayloadOffset = (ushort)payloadOffset;
            slot.PayloadSize = 20;
            slot.InstanceVersion = 1;
            slot.StructureHash = 0;

            *(int*)(p + payloadOffset + 16) = 7;
        }

        // Simulate full-build path: no DebugMap registered, but BlueprintDefinition
        // has StateFields populated by the registrar with correct offsets.
        var def = new BlueprintDefinition
        {
            Name          = "TestBlueprint",
            Kind          = Fdp.Toolkit.Blueprints.BlueprintDispatchKind.Instance,
            StructureHash = 0,
            StateSize     = 20,
            StateFields   = new Dictionary<string, BlueprintFieldDescriptor>(StringComparer.Ordinal)
            {
                ["Count"] = new BlueprintFieldDescriptor("Count", typeof(int), OffsetBytes: 16, SizeBytes: 4, ""),
            },
        };

        var outFields = new Dictionary<string, object>();
        BlueprintDebugSession.ReadInstanceState(
            buffer, blueprintId, stateLayout: null, def, outFields, out var cursor);

        Assert.NotNull(cursor);
        Assert.True(outFields.ContainsKey("Count"),
            $"FieldValues must contain 'Count' via StateFields fallback. Actual keys: {string.Join(", ", outFields.Keys)}");
        Assert.Equal(7, (int)outFields["Count"]);
    }
}
