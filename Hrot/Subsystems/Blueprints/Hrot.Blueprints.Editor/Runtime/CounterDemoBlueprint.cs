using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.Runtime;

/// <summary>
/// Minimal, code-defined Instance Blueprint used as the observable demo for the
/// Blueprint-runtime MVE. Its <see cref="Tick"/> increments a single
/// <c>Count:int</c> working-state field once per frame, giving a trivially
/// verifiable "the blueprint actually ran" signal: after pumping <c>N</c> frames
/// through the real kernel, <c>Count == N</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the production analogue of the test-only <c>FakeInstanceBp</c>. It lives
/// in <c>Hrot.Blueprints.Editor</c> (not a test project) so that BOTH the headless
/// real-kernel integration test AND the future MVE-03 toolbar button can run the
/// same asset without taking a dependency on <c>Hrot.Blueprints.Tests</c>.
/// </para>
/// <para>
/// It is registered directly into a <see cref="BlueprintRegistry"/> via
/// <see cref="Register"/> (no Roslyn compile step) — compile-on-demand of a
/// <c>.bp.json</c> is a separate slice (MVE-02/DESIGN). The <see cref="MakeAsset"/>
/// GUID is chosen so that <c>BlueprintIdHash.Compute(asset.AssetId) == BlueprintId</c>,
/// so the asset routes to this definition through the normal attach/lookup path.
/// </para>
/// </remarks>
public static class CounterDemoBlueprint
{
    /// <summary>The asset GUID that drives this blueprint's runtime id.</summary>
    public static readonly Guid AssetGuid =
        new Guid("C0117E72-0000-0000-0000-000000000001");

    /// <summary>Stable display name of the demo blueprint.</summary>
    public const string AssetName = "CounterDemo";

    /// <summary>The runtime 32-bit id, derived from <see cref="AssetGuid"/>.</summary>
    public static readonly int BlueprintId = BlueprintIdHash.Compute(AssetGuid);

    /// <summary>Arbitrary but fixed structure hash for this code-defined definition.</summary>
    public const ulong StructureHash = 0xC0117E72C0117E72UL;

    /// <summary>The observable field name incremented once per tick.</summary>
    public const string CountFieldName = "Count";

    /// <summary>
    /// Working-state layout. A <see cref="BlueprintLatentCursor"/> always occupies the
    /// head of an Instance blueprint's state (the generated layout reserves it for
    /// latent/yield bookkeeping); the observable <see cref="Count"/> follows it.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct State
    {
        public BlueprintLatentCursor Cursor;
        public int Count;
    }

    /// <summary>Byte offset of <see cref="State.Count"/> within the payload.</summary>
    public static readonly int CountOffset = Unsafe.SizeOf<BlueprintLatentCursor>();

    /// <summary>Total state size in bytes.</summary>
    public static int StateSize => Unsafe.SizeOf<State>();

    private static void InitDefault(Span<byte> bytes) => bytes.Clear();

    private static void Tick(
        Span<byte> bytes,
        ISimulationView view,
        IEntityCommandBuffer ecb,
        Entity self,
        float time,
        float deltaTime,
        uint instanceVersion)
    {
        ref var s = ref Unsafe.As<byte, State>(ref MemoryMarshal.GetReference(bytes));
        s.Count++;
    }

    /// <summary>Builds the runtime definition for this blueprint.</summary>
    public static BlueprintDefinition MakeDefinition() => new BlueprintDefinition
    {
        Name          = AssetName,
        Kind          = Fdp.Toolkit.Blueprints.BlueprintDispatchKind.Instance,
        StructureHash = StructureHash,
        StateSize     = StateSize,
        AssetId       = AssetGuid,
        InitDefault   = InitDefault,
        Tick          = Tick,
        StateFields   = new Dictionary<string, BlueprintFieldDescriptor>(StringComparer.Ordinal)
        {
            [CountFieldName] = new BlueprintFieldDescriptor(
                CountFieldName, typeof(int),
                OffsetBytes: CountOffset,
                SizeBytes:   sizeof(int),
                CategoryOrEmpty: ""),
        },
    };

    /// <summary>
    /// Builds the authoring-side asset whose <see cref="BlueprintAsset.AssetId"/> hashes to
    /// <see cref="BlueprintId"/>. This is the asset the attach service and the future toolbar
    /// button hand around. Only identity fields are populated; the runtime behavior comes from
    /// the registered <see cref="BlueprintDefinition"/>, not from the asset's variable list.
    /// </summary>
    public static BlueprintAsset MakeAsset() => new BlueprintAsset
    {
        AssetId  = AssetGuid,
        Name     = AssetName,
        Dispatch = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Instance,
    };

    /// <summary>
    /// Registers (or re-registers) this blueprint into <paramref name="registry"/> by
    /// committing a staging buffer that contains the demo definition. The commit fully
    /// replaces the registry snapshot, so callers that need other blueprints to coexist
    /// must register them in the same staging batch.
    /// </summary>
    public static void Register(BlueprintRegistry registry)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        var staging = registry.BeginStaging();
        staging.Add(BlueprintId, MakeDefinition());
        registry.CommitStaging(staging);
    }
}
