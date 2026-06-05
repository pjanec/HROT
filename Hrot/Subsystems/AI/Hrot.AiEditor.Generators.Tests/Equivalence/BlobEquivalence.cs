using System;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Kernel;
using Fhsm.Kernel.Data;
using FluentAssertions;

namespace Hrot.AiEditor.Generators.Tests.Equivalence;

/// <summary>
/// Test-only structural blob comparison helper for PU-401 migration-equivalence proof.
///
/// Compares the behaviorally-significant fields of <see cref="BehaviorTreeBlob"/> and
/// <see cref="HsmDefinitionBlob"/> element-by-element, ignoring [NonSerialized] and
/// managed-only fields that carry no runtime behavior.
///
/// BehaviorTreeBlob fields COMPARED:
///   TreeName, Version, StructureHash, ParamHash,
///   Nodes[] (NodeDefinition 8B struct, compared via MemoryMarshal.AsBytes),
///   MethodNames[], FloatParams[], IntParams[], SubtreeAssetIds[]
///
/// BehaviorTreeBlob fields IGNORED (justified):
///   CompiledDelegate  — [NonSerialized] JIT delegate; null in interpreter mode; not persisted
///   DebugMetadata     — [NonSerialized] per-node debug annotations; null for blobs from JSON;
///                       no effect on tree execution behavior
///
/// HsmDefinitionBlob fields COMPARED (via MemoryMarshal.AsBytes on fixed-layout unmanaged structs):
///   Header (HsmDefinitionHeader, all fields: Magic, FormatVersion, StructureHash, ParameterHash,
///           StateCount, TransitionCount, RegionCount, GlobalTransitionCount, EventDefinitionCount,
///           ActionCount, GuardCount)
///   States (StateDef[])
///   Transitions (TransitionDef[])
///   Regions (RegionDef[])
///   GlobalTransitions (GlobalTransitionDef[])
///   ActionTable (LinkerTableEntry[]) — FunctionId only (FunctionPointer is runtime-linked, zero at compile)
///   GuardTable  (LinkerTableEntry[]) — FunctionId only
///
/// HsmDefinitionBlob fields IGNORED (justified):
///   Metadata (MachineMetadata?) — managed sidecar used by editor projection for VisualId recovery;
///             no effect on HSM execution; populated differently by compiler vs. generator path
///   LinkerTableEntry.FunctionPointer — populated by the runtime linker AFTER initial compile;
///             always 0L in a freshly compiled blob; comparing it would be misleading noise
/// </summary>
internal static class BlobEquivalence
{
    // ── BehaviorTreeBlob ──────────────────────────────────────────────────────────

    public static void AssertEqual(BehaviorTreeBlob a, BehaviorTreeBlob b)
    {
        // Scalar identity
        a.TreeName.Should().Be(b.TreeName,
            $"BehaviorTreeBlob.TreeName must match (a='{a.TreeName}' vs b='{b.TreeName}')");

        a.Version.Should().Be(b.Version,
            $"BehaviorTreeBlob.Version must match (a={a.Version} vs b={b.Version})");

        a.StructureHash.Should().Be(b.StructureHash,
            $"BehaviorTreeBlob.StructureHash must match (a=0x{a.StructureHash:X8} vs b=0x{b.StructureHash:X8}). " +
            "Diverged structure hash means the node topology changed.");

        a.ParamHash.Should().Be(b.ParamHash,
            $"BehaviorTreeBlob.ParamHash must match (a=0x{a.ParamHash:X8} vs b=0x{b.ParamHash:X8}). " +
            "Diverged param hash means FloatParams/IntParams changed.");

        // Arrays — structural
        AssertNodeArrayEqual(a.Nodes, b.Nodes);
        AssertStringArrayEqual("MethodNames", a.MethodNames, b.MethodNames);
        AssertFloatArrayEqual("FloatParams", a.FloatParams, b.FloatParams);
        AssertIntArrayEqual("IntParams", a.IntParams, b.IntParams);
        AssertStringArrayEqual("SubtreeAssetIds", a.SubtreeAssetIds, b.SubtreeAssetIds);
    }

    private static void AssertNodeArrayEqual(NodeDefinition[] a, NodeDefinition[] b)
    {
        a.Length.Should().Be(b.Length,
            $"BehaviorTreeBlob.Nodes length must match (a={a.Length} vs b={b.Length})");

        var bytesA = MemoryMarshal.AsBytes(a.AsSpan());
        var bytesB = MemoryMarshal.AsBytes(b.AsSpan());

        for (int i = 0; i < bytesA.Length; i++)
        {
            if (bytesA[i] != bytesB[i])
            {
                // Find which node and field is different for a good error message
                int nodeIndex = i / 8;
                int byteInNode = i % 8;
                var fieldName = byteInNode switch
                {
                    0     => "Type",
                    1     => "ChildCount",
                    2 or 3 => "SubtreeOffset",
                    _      => "RawPayloadIndex",
                };
                var na = a[nodeIndex];
                var nb = b[nodeIndex];
                throw new Exception(
                    $"BehaviorTreeBlob.Nodes[{nodeIndex}].{fieldName} differs: " +
                    $"a=(Type={na.Type}, ChildCount={na.ChildCount}, SubtreeOffset={na.SubtreeOffset}, RawPayloadIndex={na.RawPayloadIndex}) " +
                    $"vs b=(Type={nb.Type}, ChildCount={nb.ChildCount}, SubtreeOffset={nb.SubtreeOffset}, RawPayloadIndex={nb.RawPayloadIndex})");
            }
        }
    }

    private static void AssertStringArrayEqual(string fieldName, string[] a, string[] b)
    {
        a.Length.Should().Be(b.Length,
            $"BehaviorTreeBlob.{fieldName} length must match (a={a.Length} vs b={b.Length})");

        for (int i = 0; i < a.Length; i++)
        {
            a[i].Should().Be(b[i],
                $"BehaviorTreeBlob.{fieldName}[{i}] must match (a='{a[i]}' vs b='{b[i]}')");
        }
    }

    private static void AssertFloatArrayEqual(string fieldName, float[] a, float[] b)
    {
        a.Length.Should().Be(b.Length,
            $"BehaviorTreeBlob.{fieldName} length must match (a={a.Length} vs b={b.Length})");

        for (int i = 0; i < a.Length; i++)
        {
            // Exact bit-for-bit float comparison — these are serialized values, not computed
            if (BitConverter.SingleToInt32Bits(a[i]) != BitConverter.SingleToInt32Bits(b[i]))
            {
                throw new Exception(
                    $"BehaviorTreeBlob.{fieldName}[{i}] differs: a={a[i]} vs b={b[i]}");
            }
        }
    }

    private static void AssertIntArrayEqual(string fieldName, int[] a, int[] b)
    {
        a.Length.Should().Be(b.Length,
            $"BehaviorTreeBlob.{fieldName} length must match (a={a.Length} vs b={b.Length})");

        for (int i = 0; i < a.Length; i++)
        {
            a[i].Should().Be(b[i],
                $"BehaviorTreeBlob.{fieldName}[{i}] must match (a={a[i]} vs b={b[i]})");
        }
    }

    // ── HsmDefinitionBlob ─────────────────────────────────────────────────────────

    public static void AssertEqual(HsmDefinitionBlob a, HsmDefinitionBlob b)
    {
        // Header — compare all fields individually for clear diagnostics
        AssertHeaderEqual(a.Header, b.Header);

        // Fixed-layout unmanaged span tables — byte-compare via MemoryMarshal
        AssertUnmanagedSpanEqual("States",            a.States,            b.States);
        AssertUnmanagedSpanEqual("Transitions",       a.Transitions,       b.Transitions);
        AssertUnmanagedSpanEqual("Regions",           a.Regions,           b.Regions);
        AssertUnmanagedSpanEqual("GlobalTransitions", a.GlobalTransitions, b.GlobalTransitions);

        // Linker tables — compare FunctionId only (FunctionPointer is runtime-linked, always 0 at compile time)
        AssertLinkerTableFunctionIds("ActionTable", a.ActionTable, b.ActionTable);
        AssertLinkerTableFunctionIds("GuardTable",  a.GuardTable,  b.GuardTable);
    }

    private static void AssertHeaderEqual(HsmDefinitionHeader a, HsmDefinitionHeader b)
    {
        a.Magic.Should().Be(b.Magic,
            $"HsmDefinitionBlob.Header.Magic differs (a=0x{a.Magic:X8} vs b=0x{b.Magic:X8})");
        a.FormatVersion.Should().Be(b.FormatVersion,
            $"HsmDefinitionBlob.Header.FormatVersion differs (a={a.FormatVersion} vs b={b.FormatVersion})");
        a.StructureHash.Should().Be(b.StructureHash,
            $"HsmDefinitionBlob.Header.StructureHash differs (a=0x{a.StructureHash:X8} vs b=0x{b.StructureHash:X8}). " +
            "Diverged structure hash means HSM topology changed.");
        a.ParameterHash.Should().Be(b.ParameterHash,
            $"HsmDefinitionBlob.Header.ParameterHash differs (a=0x{a.ParameterHash:X8} vs b=0x{b.ParameterHash:X8}). " +
            "Diverged parameter hash means action/guard FunctionIds changed.");
        a.StateCount.Should().Be(b.StateCount,
            $"HsmDefinitionBlob.Header.StateCount differs (a={a.StateCount} vs b={b.StateCount})");
        a.TransitionCount.Should().Be(b.TransitionCount,
            $"HsmDefinitionBlob.Header.TransitionCount differs (a={a.TransitionCount} vs b={b.TransitionCount})");
        a.RegionCount.Should().Be(b.RegionCount,
            $"HsmDefinitionBlob.Header.RegionCount differs (a={a.RegionCount} vs b={b.RegionCount})");
        a.GlobalTransitionCount.Should().Be(b.GlobalTransitionCount,
            $"HsmDefinitionBlob.Header.GlobalTransitionCount differs (a={a.GlobalTransitionCount} vs b={b.GlobalTransitionCount})");
        a.EventDefinitionCount.Should().Be(b.EventDefinitionCount,
            $"HsmDefinitionBlob.Header.EventDefinitionCount differs (a={a.EventDefinitionCount} vs b={b.EventDefinitionCount})");
        a.ActionCount.Should().Be(b.ActionCount,
            $"HsmDefinitionBlob.Header.ActionCount differs (a={a.ActionCount} vs b={b.ActionCount})");
        a.GuardCount.Should().Be(b.GuardCount,
            $"HsmDefinitionBlob.Header.GuardCount differs (a={a.GuardCount} vs b={b.GuardCount})");
    }

    private static void AssertUnmanagedSpanEqual<T>(
        string fieldName,
        ReadOnlySpan<T> a,
        ReadOnlySpan<T> b)
        where T : unmanaged
    {
        a.Length.Should().Be(b.Length,
            $"HsmDefinitionBlob.{fieldName} length must match (a={a.Length} vs b={b.Length})");

        var bytesA   = MemoryMarshal.AsBytes(a);
        var bytesB   = MemoryMarshal.AsBytes(b);
        int elemSize = a.Length > 0 ? bytesA.Length / a.Length : Marshal.SizeOf<T>();

        for (int i = 0; i < bytesA.Length; i++)
        {
            if (bytesA[i] != bytesB[i])
            {
                int elemIndex  = i / elemSize;
                int byteInElem = i % elemSize;
                throw new Exception(
                    $"HsmDefinitionBlob.{fieldName}[{elemIndex}] byte offset {byteInElem} differs: " +
                    $"a=0x{bytesA[i]:X2} vs b=0x{bytesB[i]:X2} (element size={elemSize} bytes)");
            }
        }
    }

    private static void AssertLinkerTableFunctionIds(
        string fieldName,
        ReadOnlySpan<LinkerTableEntry> a,
        ReadOnlySpan<LinkerTableEntry> b)
    {
        a.Length.Should().Be(b.Length,
            $"HsmDefinitionBlob.{fieldName} length must match (a={a.Length} vs b={b.Length})");

        for (int i = 0; i < a.Length; i++)
        {
            a[i].FunctionId.Should().Be(b[i].FunctionId,
                $"HsmDefinitionBlob.{fieldName}[{i}].FunctionId must match " +
                $"(a=0x{a[i].FunctionId:X4} vs b=0x{b[i].FunctionId:X4}). " +
                "FunctionPointer is NOT compared — it is populated by the runtime linker (always 0L at compile time).");
        }
    }
}
