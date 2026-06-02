using System;
using System.Numerics;
using Fbt;
using Fbt.Compiler;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Hrot.Editor.AiShared.Layout;

namespace Hrot.AI.Behaviors.Trees;

/// <summary>
/// Minimal sample BTree asset — used by editor contributors to verify discovery
/// and to smoke-test the layout-contract round-trip.
///
/// Structure: Root → Sequence → { Wait(1s), Wait(2s) }
/// Uses only structural/builtin nodes so no external action delegates are required.
///
/// AssetId is the FNV-1a-32 hash of "SampleScout":
///   54ef3847-0000-0000-0000-000000000000
/// </summary>
public static class SampleScout
{
    // Node visual IDs — fixed for stable layout round-trips.
    private const string RootId      = "10000000-0000-0000-0000-000000000001";
    private const string SequenceId  = "20000000-0000-0000-0000-000000000001";
    private const string Wait1Id     = "30000000-0000-0000-0000-000000000001";
    private const string Wait2Id     = "40000000-0000-0000-0000-000000000001";

    // AssetId = FNV-1a-32("SampleScout") → 0x54ef3847
    private const string AssetId = "54ef3847-0000-0000-0000-000000000000";

    /// <summary>Builds the tree; returned blob is compiled and ready for projection.</summary>
    public static BTreeBuilder<BrainBlackboard, BTreeContext> CreateBuilder() =>
        new BTreeBuilder<BrainBlackboard, BTreeContext>()
            .Sequence(s => s
                .Wait(1.0f, visualId: new Guid(Wait1Id))
                .Wait(2.0f, visualId: new Guid(Wait2Id)),
                visualId: new Guid(SequenceId));

    /// <summary>
    /// Compilable thunk discovered by <c>BTreeAssetContributor.LoadFrom</c>.
    /// Returns a <see cref="BehaviorTreeBlob"/> built from pure structural nodes.
    /// </summary>
    [BTreeDefinition("SampleScout")]
    public static BehaviorTreeBlob Build() => CreateBuilder().Compile("SampleScout");

    /// <summary>
    /// Layout snapshot; discovered by <c>LayoutDiscovery.TryGetLayout</c>.
    /// The AssetId must match the FNV-1a-32 hash of "SampleScout".
    /// </summary>
    [BTreeLayout(AssetId)]
    public static BTreeEditorLayout Layout() =>
        new BTreeEditorLayoutBuilder()
            .Canvas(new Vector2(0f, 0f), 1.0f)
            .Node(SequenceId, new Vector2(200f, 50f),  comment: "patrol sequence")
            .Node(Wait1Id,    new Vector2(100f, 200f), comment: "pause at waypoint A")
            .Node(Wait2Id,    new Vector2(300f, 200f), comment: "pause at waypoint B")
            .Build();
}
