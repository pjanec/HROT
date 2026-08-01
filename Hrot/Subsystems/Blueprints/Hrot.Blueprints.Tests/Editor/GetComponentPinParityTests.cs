using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Editor.Host;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// CA-02 (Slice 1a) — the CRITICAL contract batch: proves
/// <see cref="NodePinSchema.GetCanonicalPins"/>'s <c>GetComponentNode</c> projection is in EXACT
/// shape-parity with the compiler's frozen (CA-01, Opus-reviewed) <c>Stage0_Rehydrate
/// .EnrichGetComponentPins</c>, for BOTH the multi-pin (<c>Fields</c> baked) and legacy
/// (<c>Fields == null</c>) shapes. A mismatch here means a wire renders "unused" on the canvas even
/// though the compiler consumes it (or vice-versa) -- see
/// <c>docs/blueprints/Blueprint_Component_Access_Design.md</c>'s "Keep in lockstep with
/// NodePinSchema" note.
/// <para>
/// Runs the REAL <see cref="Stage0_Rehydrate"/> (internal, exposed via
/// <c>InternalsVisibleTo Hrot.Blueprints.Tests</c>) against a pin-less node to get the compiler's
/// actual output, and compares it — by (Name, Direction, IsExec, TypeId) tuple, in order — against
/// the editor's <see cref="NodePinSchema.GetCanonicalPins"/> projection for an equivalent
/// freshly-built (also pin-less) node. GUIDs are excluded from the comparison (Stage0 assigns link
/// GUIDs; NodePinSchema assigns fresh scratch GUIDs — neither is meaningful here).
/// </para>
/// </summary>
public sealed class GetComponentPinParityTests
{
    private static CompileOptions DefaultOptions() => new CompileOptions(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    /// <summary>Runs Stage0 pin rehydration on a single-node graph and returns the rehydrated pins as
    /// (Name, Direction, IsExec, TypeId) tuples, in order.</summary>
    private static List<(string Name, string Direction, bool IsExec, string? TypeId)> RunStage0(GetComponentNode node)
    {
        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = new List<Node> { node }, Links = new List<Link>(),
            Inputs = new(), Outputs = new(),
        };
        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "ParityTest",
            Dispatch = BlueprintDispatchKind.Instance,
            Graphs   = { graph },
        };

        Stage0_Rehydrate.Run(asset, DefaultOptions());

        return node.Pins
            .Select(p => (p.Name, p.Direction, p.IsExec, p.TypeRef?.TypeId))
            .ToList();
    }

    /// <summary>Runs the editor's projection on an equivalent (freshly-built, pin-less) node.</summary>
    private static List<(string Name, string Direction, bool IsExec, string? TypeId)> RunEditor(GetComponentNode node)
        => NodePinSchema.GetCanonicalPins(node)
            .Select(p => (p.Name, p.Direction, p.IsExec, p.TypeRef?.TypeId))
            .ToList();

    // ── Multi-pin (Fields baked) ──────────────────────────────────────────────

    [Fact]
    public void MultiPin_EditorProjection_MatchesStage0Enrichment_Exactly()
    {
        GetComponentNode Build() => new()
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "System.Numerics.Vector3",
            Fields = new List<ComponentFieldDecl>
            {
                new() { Name = "X", TypeId = "System.Single" },
                new() { Name = "Y", TypeId = "System.Single" },
            },
        };

        var fromStage0 = RunStage0(Build());
        var fromEditor = RunEditor(Build());

        Assert.Equal(fromStage0, fromEditor);

        // Pin down the exact expected shape too, so a shape drift in EITHER side is caught, not
        // just a Stage0-vs-editor disagreement that happens to move in lockstep.
        Assert.Equal(new[]
        {
            ("Target", "In",  false, (string?)"Fdp.Core.Entity"),
            ("X",      "Out", false, (string?)"System.Single"),
            ("Y",      "Out", false, (string?)"System.Single"),
            ("Found",  "Out", false, (string?)"System.Boolean"),
        }, fromEditor);
    }

    [Fact]
    public void MultiPin_SingleField_EditorProjection_MatchesStage0Enrichment()
    {
        GetComponentNode Build() => new()
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "Fdp.Core.SimTransform",
            Fields = new List<ComponentFieldDecl>
            {
                new() { Name = "PositionX", TypeId = "System.Single" },
            },
        };

        Assert.Equal(RunStage0(Build()), RunEditor(Build()));
    }

    // ── Legacy (Fields == null) ───────────────────────────────────────────────

    [Fact]
    public void Legacy_EditorProjection_MatchesStage0Enrichment_Exactly()
    {
        GetComponentNode Build() => new()
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "System.Numerics.Vector3",
            FieldName        = "X",
            FieldTypeFqn     = "System.Single",
            Fields           = null,
        };

        var fromStage0 = RunStage0(Build());
        var fromEditor = RunEditor(Build());

        Assert.Equal(fromStage0, fromEditor);

        // Legacy is self-only: no Target, no Found -- just the single "Value" out-pin, FieldTypeFqn
        // used VERBATIM (not "global::"-stamped -- see Stage0's EnrichGetComponentPins doc comment).
        Assert.Equal(new[] { ("Value", "Out", false, (string?)"System.Single") }, fromEditor);
    }

    [Fact]
    public void Legacy_EmptyFieldTypeFqn_FallsBackToSystemObject_BothSidesAgree()
    {
        GetComponentNode Build() => new()
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "System.Numerics.Vector3",
            FieldName        = "X",
            FieldTypeFqn     = "",
            Fields           = null,
        };

        var fromStage0 = RunStage0(Build());
        var fromEditor = RunEditor(Build());

        Assert.Equal(fromStage0, fromEditor);
        Assert.Equal(new[] { ("Value", "Out", false, (string?)"System.Object") }, fromEditor);
    }
}
