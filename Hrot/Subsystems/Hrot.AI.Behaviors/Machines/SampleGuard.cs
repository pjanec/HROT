using System;
using System.Numerics;
using Fhsm.Compiler;
using Fhsm.Kernel.Attributes;
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared.Layout;

namespace Hrot.AI.Behaviors.Machines;

/// <summary>
/// Minimal sample HSM asset — used by editor contributors to verify discovery
/// and to smoke-test the layout-contract round-trip.
///
/// Structure: Idle --[Alert]--> Scanning --[Clear]--> Idle
/// Uses only built-in transitions; no external action or guard delegates required.
///
/// AssetId: 979df4a4-0000-0000-0000-000000000000
/// (this is also the FNV-1a-32 hash of "SampleGuard", kept explicit for stability)
/// </summary>
public static class SampleGuard
{
    // State stable IDs — fixed for stable layout round-trips.
    private const string IdleStateId      = "aa010000-0000-0000-0000-000000000001";
    private const string ScanningStateId  = "bb010000-0000-0000-0000-000000000001";

    // Transition visual IDs.
    private const string AlertTransId     = "cc010000-0000-0000-0000-000000000001";
    private const string ClearTransId     = "dd010000-0000-0000-0000-000000000001";

    // Explicit fixed asset GUID — matches FNV-1a-32("SampleGuard").
    private const string AssetId = "979df4a4-0000-0000-0000-000000000000";

    /// <summary>Builds the state machine graph.</summary>
    public static HsmBuilder CreateBuilder()
    {
        var builder = new HsmBuilder("SampleGuard");

        builder.Event("Alert", 1, 0, false, false);
        builder.Event("Clear", 2, 0, false, false);

        // Declare all states first so GoTo look-ups succeed regardless of order.
        var idle     = builder.State("Idle",     stableId: new Guid(IdleStateId));
        var scanning = builder.State("Scanning", stableId: new Guid(ScanningStateId));

        // Define transitions after all target states are registered.
        idle.On("Alert").GoTo("Scanning", visualId: new Guid(AlertTransId));
        scanning.On("Clear").GoTo("Idle",  visualId: new Guid(ClearTransId));

        return builder;
    }

    /// <summary>
    /// Compilable thunk discovered by <c>HsmAssetContributor.LoadFrom</c>.
    /// Returns an <see cref="HsmDefinitionBlob"/> with two states and two transitions.
    /// </summary>
    [HsmDefinition("SampleGuard", AssetId = AssetId)]
    public static HsmDefinitionBlob Compile() => CreateBuilder().Build().Compile();

    /// <summary>
    /// Layout snapshot; discovered by <c>LayoutDiscovery.TryGetLayout</c>.
    /// The AssetId must match the AssetId on the [HsmDefinition] attribute above.
    /// </summary>
    [HsmLayout(AssetId)]
    public static HsmEditorLayout Layout() =>
        new HsmEditorLayoutBuilder()
            .Canvas(new Vector2(0f, 0f), 1.0f)
            .State(IdleStateId,     new Vector2(100f, 100f), comment: "guard is at rest")
            .State(ScanningStateId, new Vector2(400f, 100f), comment: "guard is scanning the area")
            .Transition(AlertTransId, new[] { new Vector2(250f, 80f) },  comment: "threat detected")
            .Transition(ClearTransId, new[] { new Vector2(250f, 120f) }, comment: "area secured")
            .Build();
}
