using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Partitioning;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>
/// Test double for a single-instance Blueprint with a TickCount field.
/// Registered under a fixed blueprint ID (0xDEADBEEF).
/// </summary>
public static class FakeInstanceBp
{
    // AssetGuid drives BlueprintId so that BlueprintIdHash.Compute(MakeAsset().AssetId) == BlueprintId.
    private static readonly Guid AssetGuid = new Guid("DEADBEEF-0000-0000-0000-000000000000");
    public static readonly int BlueprintId = BlueprintIdHash.Compute(AssetGuid);
    public const ulong StructureHash = 0x0123456789ABCDEFU;

    [StructLayout(LayoutKind.Sequential)]
    public struct State { public BlueprintLatentCursor Cursor; public int TickCount; }

    public static int StateSize => Unsafe.SizeOf<State>();

    public static void InitDefault(Span<byte> bytes) => bytes.Clear();

    public static void Tick(Span<byte> bytes, ISimulationView view, IEntityCommandBuffer ecb,
        Entity self, float time, float deltaTime, uint instanceVersion)
    {
        ref var s = ref Unsafe.As<byte, State>(ref MemoryMarshal.GetReference(bytes));
        s.TickCount++;
    }

    public static BlueprintDefinition MakeDefinition() => new BlueprintDefinition
    {
        Name = "FakeInstance",
        Kind = Fdp.Toolkit.Blueprints.BlueprintDispatchKind.Instance,
        StructureHash = StructureHash,
        StateSize = StateSize,
        InitDefault = InitDefault,
        Tick = Tick,
        StateFields = new Dictionary<string, BlueprintFieldDescriptor>(StringComparer.Ordinal)
        {
            ["TickCount"] = new BlueprintFieldDescriptor(
                "TickCount", typeof(int),
                OffsetBytes: Unsafe.SizeOf<BlueprintLatentCursor>(),
                SizeBytes:   sizeof(int),
                CategoryOrEmpty: ""),
        },
    };

    public static BlueprintAsset MakeAsset() => new BlueprintAsset
        { AssetId = AssetGuid, Name = "FakeInstance" };

    public static void Register(BlueprintRegistry registry)
    {
        var staging = registry.BeginStaging();
        staging.Add(BlueprintId, MakeDefinition());
        registry.CommitStaging(staging);
    }
}

/// <summary>
/// Test double for a world-singleton Blueprint with a TickCount field.
/// Registered under a fixed blueprint ID (0xCAFEBABE).
/// </summary>
public static class FakeWorldSingletonBp
{
    public const int   BlueprintId   = unchecked((int)0xCAFEBABE);
    public const ulong StructureHash = 0xFEDCBA9876543210U;

    [StructLayout(LayoutKind.Sequential)]
    public struct State { public BlueprintLatentCursor Cursor; public int TickCount; }

    public static int StateSize => Unsafe.SizeOf<State>();

    public static void InitDefault(Span<byte> bytes) => bytes.Clear();

    public static void Tick(Span<byte> bytes, ISimulationView view, IEntityCommandBuffer ecb,
        Entity self, float time, float deltaTime, uint instanceVersion)
    {
        ref var s = ref Unsafe.As<byte, State>(ref MemoryMarshal.GetReference(bytes));
        s.TickCount++;
    }

    public static BlueprintDefinition MakeDefinition() => new BlueprintDefinition
    {
        Name = "FakeWorldSingleton",
        Kind = Fdp.Toolkit.Blueprints.BlueprintDispatchKind.Instance,
        StructureHash = StructureHash,
        StateSize = StateSize,
        InitDefault = InitDefault,
        Tick = Tick,
        StateFields = new Dictionary<string, BlueprintFieldDescriptor>(StringComparer.Ordinal)
        {
            ["TickCount"] = new BlueprintFieldDescriptor(
                "TickCount", typeof(int),
                OffsetBytes: Unsafe.SizeOf<BlueprintLatentCursor>(),
                SizeBytes:   sizeof(int),
                CategoryOrEmpty: ""),
        },
    };

    public static void Register(BlueprintRegistry registry,
        BlackboardTier tier = BlackboardTier.B1024)
    {
        var staging = registry.BeginStaging();
        staging.Add(BlueprintId, MakeDefinition());
        staging.AddWorldSingleton(BlueprintId, tier);
        registry.CommitStaging(staging);
    }
}
