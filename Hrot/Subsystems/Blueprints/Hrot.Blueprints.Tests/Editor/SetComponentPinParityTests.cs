using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Editor.Host;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// CA-04 (Slice W1) — the CRITICAL contract batch for the write node: proves
/// <see cref="NodePinSchema.GetCanonicalPins"/>'s <c>SetComponentNode</c> projection is in EXACT
/// shape-parity with the compiler's frozen (CA-03, Opus-reviewed) <c>Stage0_Rehydrate
/// .EnrichSetComponentPins</c>. A mismatch here means a wire renders "unused" on the canvas even
/// though the compiler consumes it (or vice-versa) — mirrors <see cref="GetComponentPinParityTests"/>.
/// <para>
/// Runs the REAL <see cref="Stage0_Rehydrate"/> (internal, exposed via
/// <c>InternalsVisibleTo Hrot.Blueprints.Tests</c>) against a pin-less node to get the compiler's
/// actual output, and compares it — by (Name, Direction, IsExec, TypeId) tuple, in order — against
/// the editor's <see cref="NodePinSchema.GetCanonicalPins"/> projection for an equivalent
/// freshly-built (also pin-less) node. GUIDs are excluded from the comparison (Stage0 assigns link
/// GUIDs; NodePinSchema assigns fresh scratch GUIDs — neither is meaningful here).
/// </para>
/// </summary>
public sealed class SetComponentPinParityTests
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
    private static List<(string Name, string Direction, bool IsExec, string? TypeId)> RunStage0(SetComponentNode node)
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
    private static List<(string Name, string Direction, bool IsExec, string? TypeId)> RunEditor(SetComponentNode node)
        => NodePinSchema.GetCanonicalPins(node)
            .Select(p => (p.Name, p.Direction, p.IsExec, p.TypeRef?.TypeId))
            .ToList();

    // ── Multi-field (Fields baked) ────────────────────────────────────────────

    [Fact]
    public void MultiField_EditorProjection_MatchesStage0Enrichment_Exactly()
    {
        SetComponentNode Build() => new()
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
        // just a Stage0-vs-editor disagreement that happens to move in lockstep. Self-only (no
        // Target, ever, unlike GetComponent's optional cross-entity read).
        Assert.Equal(new[]
        {
            ("In",      "In",  true,  (string?)""),
            ("Out",     "Out", true,  (string?)""),
            ("X",       "In",  false, (string?)"System.Single"),
            ("Y",       "In",  false, (string?)"System.Single"),
            ("Written", "Out", false, (string?)"System.Boolean"),
        }, fromEditor);
    }

    [Fact]
    public void MultiField_SingleField_EditorProjection_MatchesStage0Enrichment()
    {
        SetComponentNode Build() => new()
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "Hrot.AI.Behaviors.SomeBehaviorOwnedState",
            Fields = new List<ComponentFieldDecl>
            {
                new() { Name = "Health", TypeId = "System.Int32" },
            },
        };

        Assert.Equal(RunStage0(Build()), RunEditor(Build()));
    }

    // ── No fields baked yet (freshly-dropped node, no component picked) ──────

    [Fact]
    public void NoFieldsBaked_EditorProjection_MatchesStage0Enrichment_ExecPlusWrittenOnly()
    {
        SetComponentNode Build() => new()
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "",
            Fields           = null,
        };

        var fromStage0 = RunStage0(Build());
        var fromEditor = RunEditor(Build());

        Assert.Equal(fromStage0, fromEditor);

        // "Written" projects UNCONDITIONALLY (write-if-present guard result), even with zero
        // fields baked -- unlike SetShared's legacy branch, there is no field-less "no Written"
        // state for SetComponent.
        Assert.Equal(new[]
        {
            ("In",      "In",  true,  (string?)""),
            ("Out",     "Out", true,  (string?)""),
            ("Written", "Out", false, (string?)"System.Boolean"),
        }, fromEditor);
    }

    [Fact]
    public void EmptyFieldsList_EditorProjection_MatchesStage0Enrichment_SameAsNull()
    {
        SetComponentNode Build() => new()
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "System.Numerics.Vector3",
            Fields           = new List<ComponentFieldDecl>(),
        };

        var fromStage0 = RunStage0(Build());
        var fromEditor = RunEditor(Build());

        Assert.Equal(fromStage0, fromEditor);
        Assert.Equal(new[]
        {
            ("In",      "In",  true,  (string?)""),
            ("Out",     "Out", true,  (string?)""),
            ("Written", "Out", false, (string?)"System.Boolean"),
        }, fromEditor);
    }

    // ── Managed (CA-06, Slice W2, Q#16-C) -- single "Value" pin, never per-field ──────────────

    [Fact]
    public void Managed_EditorProjection_MatchesStage0Enrichment_SingleValuePin()
    {
        SetComponentNode Build() => new()
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "Hrot.Blueprints.Tests.Fixtures.FakeManagedComponentForPinParity",
            IsManaged        = true,
        };

        var fromStage0 = RunStage0(Build());
        var fromEditor = RunEditor(Build());

        Assert.Equal(fromStage0, fromEditor);

        // Single "Value" pin (global::-stamped, mirrors SetShared's legacy whole-struct "Value"
        // pin), no field pins whatsoever -- + "Written", unconditionally. NO "Target", ever.
        Assert.Equal(new[]
        {
            ("In",      "In",  true,  (string?)""),
            ("Out",     "Out", true,  (string?)""),
            ("Value",   "In",  false, (string?)"global::Hrot.Blueprints.Tests.Fixtures.FakeManagedComponentForPinParity"),
            ("Written", "Out", false, (string?)"System.Boolean"),
        }, fromEditor);
    }

    [Fact]
    public void Managed_WithSpuriousFieldsBaked_EditorProjection_MatchesStage0_IgnoresFields()
    {
        // Defense-in-depth: even if a hand-authored/legacy asset carries BOTH IsManaged=true AND a
        // per-field Fields list (Stage2's BP2064 rejects this at validation time), Stage0/NodePinSchema
        // must still agree with EACH OTHER on the projected shape (the managed single-Value shape,
        // Fields ignored) -- this is a pin-parity test, not a validator test (see
        // V_ComponentAccessValidatorTests.Validate_ManagedSetComponentWithPerFieldFields_BP2064 for
        // the rejection itself).
        SetComponentNode Build() => new()
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "Hrot.Blueprints.Tests.Fixtures.FakeManagedComponentForPinParity",
            IsManaged        = true,
            Fields = new List<ComponentFieldDecl> { new() { Name = "Name", TypeId = "System.String" } },
        };

        var fromStage0 = RunStage0(Build());
        var fromEditor = RunEditor(Build());

        Assert.Equal(fromStage0, fromEditor);
        Assert.DoesNotContain(fromEditor, p => p.Name == "Name");
    }
}
