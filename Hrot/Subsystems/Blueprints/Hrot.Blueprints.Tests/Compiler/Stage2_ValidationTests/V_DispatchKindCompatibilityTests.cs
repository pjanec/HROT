using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

public sealed class V_DispatchKindCompatibilityTests
{
    // ---- helpers --------------------------------------------------------

    private static IReadOnlyList<Diagnostic> Validate(BlueprintAsset asset,
        IReadOnlyList<BlueprintSignature>? siblings = null)
    {
        var sink = new DiagnosticSink();
        var opts = DefaultOptions(siblings);
        Stage2_Validate.Run(asset, new ValidationContext(sink, opts));
        return sink.All;
    }

    private static CompileOptions DefaultOptions(IReadOnlyList<BlueprintSignature>? siblings = null) =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: siblings ?? Array.Empty<BlueprintSignature>());

    // ---- Library dispatch tests -----------------------------------------

    [Fact]
    [CoversDiagnosticCode("BP1010")]
    public void Library_WithPrimitiveBlock_EmitsBP1010()
    {
        var asset = BlueprintAssetBuilder
            .Library("L")
            .WithGraph("G", g => g.Entry().Return())
            .Build();
        asset.Primitive = new AiPrimitiveDecl
        {
            Intent = AiPrimitiveIntent.Action,
            Hostings = new List<AiPrimitiveHosting> { AiPrimitiveHosting.BTreeAction },
        };

        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1010);
    }

    [Fact]
    [CoversDiagnosticCode("BP1011")]
    public void Library_WithVariable_EmitsBP1011()
    {
        var asset = BlueprintAssetBuilder
            .Library("L")
            .WithVariable("x", typeof(int))
            .Build();

        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1011);
    }

    /// <summary>
    /// ⭐ <b><c>U-12</c> — <c>BP1011</c> restated to <i>any</i> asset-scope declaration.</b> A Library
    /// carrying a <c>Parameter</c> or a working-state entry was quietly legal before, for no stated
    /// reason: the rule named <c>Variables</c> only because the lists were separate.
    ///
    /// <para>⭐⭐ <b>Batch 86 — RESTATED, not deleted.</b> The second row said <c>WorkingState</c>;
    /// <c>R-01</c> makes that <c>Variable</c>. ⚠ <b>The AUTHORING path is deliberately unchanged</b> —
    /// it still builds through <c>WithWorkingStateField</c>, which is now the retired alias onto the
    /// leading part of the one state run. ⇒ the row still covers the entry point a v1-shaped asset
    /// arrives through, which is the only reason this row differs from
    /// <c>Library_WithVariable_EmitsBP1011</c> above.</para>
    /// </summary>
    [Theory]
    [InlineData(DeclarationKind.Parameter)]
    [InlineData(DeclarationKind.Variable)]
    [CoversDiagnosticCode("BP1011")]
    public void Library_WithAnyAssetScopeDeclaration_EmitsBP1011(DeclarationKind kind)
    {
        var builder = BlueprintAssetBuilder.Library("L");
        var asset   = (kind == DeclarationKind.Parameter
                        ? builder.WithParameter("x", typeof(int))
                        : builder.WithWorkingStateField("x", typeof(int)))
                      .Build();

        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1011);
    }

    /// <summary>⛔ And a Library that declares nothing is still fine — the widening must not turn
    /// into "a Library is refused".</summary>
    [Fact]
    public void Library_DeclaringNothing_DoesNotEmitBP1011()
    {
        var asset = BlueprintAssetBuilder
            .Library("L")
            .WithGraph("G", g => g.Entry().Return())
            .Build();

        var diags = Validate(asset);

        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP1011);
    }

    [Fact]
    [CoversDiagnosticCode("BP1012")]
    public void Library_WithCustomEvent_EmitsBP1012()
    {
        var asset = BlueprintAssetBuilder
            .Library("L")
            .WithCustomEvent("OnFoo")
            .Build();

        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1012);
    }

    [Fact]
    [CoversDiagnosticCode("BP1013")]
    public void Library_WithEventGraph_EmitsBP1013()
    {
        var asset = BlueprintAssetBuilder
            .Library("L")
            .WithEventGraph("SomeEvent", g => g.Entry().Return())
            .Build();

        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1013);
    }

    // ---- AiPrimitive dispatch tests -------------------------------------

    [Fact]
    [CoversDiagnosticCode("BP1020")]
    public void AiPrimitive_WithoutPrimitiveBlock_EmitsBP1020()
    {
        // Build a valid AiPrimitive then strip the primitive block.
        var asset = BlueprintAssetBuilder
            .AiPrimitive("A")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .Build();
        asset.Primitive = null;

        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1020);
    }

    [Fact]
    [CoversDiagnosticCode("BP1021")]
    public void AiPrimitive_WithNoHostings_EmitsBP1021()
    {
        // AiPrimitive() initialises Hostings as empty list.
        var asset = BlueprintAssetBuilder
            .AiPrimitive("A")
            .Build();

        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1021);
    }

    [Fact]
    [CoversDiagnosticCode("BP1022")]
    public void AiPrimitive_ActionWithConditionHosting_EmitsBP1022()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("A")
            .WithIntent(AiPrimitiveIntent.Action)
            .WithHostings(AiPrimitiveHosting.BTreeCondition)
            .Build();

        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1022);
    }

    [Fact]
    [CoversDiagnosticCode("BP1023")]
    public void AiPrimitive_ConditionWithActionHosting_EmitsBP1023()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("A")
            .WithIntent(AiPrimitiveIntent.Condition)
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .Build();

        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1023);
    }

    /// <summary>
    /// ⭐⭐ <b><c>U-12</c> Pass 1 — <c>BP1024</c> is RETIRED.</b> It refused an AiPrimitive that
    /// declared a <c>Variable</c>, on the reasoning that <i>"AiPrimitive uses parameters and
    /// workingState"</i>. ⛔ Under the unified model <c>Variable</c> and <c>WorkingState</c> are the
    /// <b>same cell</b>, <c>(State, Asset)</c> — the rule was enforcing a spelling, not a semantic.
    ///
    /// <para>
    /// ⚠ This test is the inverse of the one it replaces: it asserts the diagnostic <b>does not</b>
    /// fire, so the retirement cannot be silently undone. The gate's own wording — ⭐ <i>"an
    /// AiPrimitive with (State, Asset) entries compiles"</i> — is the second assertion.
    /// </para>
    /// </summary>
    [Fact]
    public void AiPrimitive_WithStateAssetEntriesOfBothSpellings_Compiles()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("A")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithWorkingStateField("ws", typeof(int))
            .WithVariable("v", typeof(int))
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        var diags = Validate(asset);

        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP1024);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [CoversDiagnosticCode("BP1025")]
    public void AiPrimitive_WithEventGraph_EmitsBP1025()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("A")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithEventGraph("SomeEvent", g => g.Entry().Return())
            .Build();

        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1025);
    }

    // ---- Instance dispatch tests ----------------------------------------

    [Fact]
    [CoversDiagnosticCode("BP1030")]
    public void Instance_WithPrimitiveBlock_EmitsBP1030()
    {
        var asset = BlueprintAssetBuilder
            .Instance("I")
            .Build();
        asset.Primitive = new AiPrimitiveDecl
        {
            Intent = AiPrimitiveIntent.Action,
            Hostings = new List<AiPrimitiveHosting> { AiPrimitiveHosting.BTreeAction },
        };

        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1030);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 70 — <c>BP1031</c> is RETIRED, and this assertion is INVERTED.</b>
    ///
    /// <para>
    /// <c>U-12</c> kept the <c>Parameter</c> half on the reasoning <i>"a spawn-time input the Instance
    /// dispatch has no way to supply"</i> — the rule's own message said so. 🔴 <b>The Instance params
    /// seam supplies it</b> (<c>DESIGN_Parameter_Model.md</c> §3.3): the attach event carries the JSON,
    /// <c>BlueprintDefinition.ParseParams</c> resolves it through the SAME delegate a behaviour uses,
    /// and the payload reserves <c>[Cursor 16][Params N][State M]</c>.
    /// </para>
    ///
    /// <para>
    /// ⛔ Leaving it standing would have made the seam unreachable — the "inert rule" shape this
    /// programme keeps filing rather than shipping. ⭐ Inverted rather than deleted, so the retirement
    /// is asserted rather than merely uncommented.
    /// </para>
    /// </summary>
    [Fact]
    public void Instance_WithParams_NoLongerEmitsBP1031()
    {
        var asset = BlueprintAssetBuilder
            .Instance("I")
            .WithParameter("p", typeof(int))
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();

        var diags = Validate(asset);

        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP1031);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>
    /// ⭐⭐ <b><c>U-12</c> Pass 2 — the half that was SPLIT OFF.</b> Refusing an Instance's
    /// <c>WorkingState</c> was the same spelling rule as <c>BP1024</c>: <c>WorkingState</c> and
    /// <c>Variable</c> are one cell. ⛔ It must no longer fire.
    /// </summary>
    [Fact]
    public void Instance_WithWorkingState_NoLongerEmitsBP1031()
    {
        var asset = BlueprintAssetBuilder
            .Instance("I")
            .WithWorkingStateField("ws", typeof(int))
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();

        var diags = Validate(asset);

        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP1031);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
    }

    // ---- Catalog reference tests ----------------------------------------

    [Fact]
    [CoversDiagnosticCode("BP1400")]
    public void Instance_GraphWithUnknownEvent_EmitsBP1400()
    {
        var asset = BlueprintAssetBuilder
            .Instance("I")
            .WithEventGraph("OnFoo", g => g.Entry().Return())
            .Build();

        // Patch the EventEntryNode to reference a non-Guid, unknown event type.
        var entryNode = asset.Graphs[0].Nodes.OfType<EventEntryNode>().First();
        entryNode.EventTypeId = "UnknownEventType";

        // Use a non-empty engine event catalog with a DIFFERENT event type.
        var catalog = new SingleEntryEngineEventCatalog();
        var sink = new DiagnosticSink();
        var opts = new CompileOptions(
            Mode: CompilerMode.Debug,
            NodeRegistry: BuiltInNodeRegistry.Instance,
            TypeRegistry: StaticTypeRegistry.Instance,
            EngineEvents: catalog,
            ChannelCommands: BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives: BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

        Stage2_Validate.Run(asset, new ValidationContext(sink, opts));

        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP1400);
    }

    [Fact]
    [CoversDiagnosticCode("BP1401")]
    public void AiPrimitive_WithUnknownChannelCommand_EmitsBP1401()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("A")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .ChannelCommand("UnknownChannel", "UnknownAction")
                .Return())
            .Build();

        // Non-empty catalog with a KNOWN command (different from the one in the asset).
        var catalog = new SingleEntryChannelCommandCatalog();
        var opts = new CompileOptions(
            Mode: CompilerMode.Debug,
            NodeRegistry: BuiltInNodeRegistry.Instance,
            TypeRegistry: StaticTypeRegistry.Instance,
            EngineEvents: BuiltInEngineEventCatalog.Instance,
            ChannelCommands: catalog,
            WaitPrimitives: BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

        var sink = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink, opts));

        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP1401);
    }

    [Fact]
    [CoversDiagnosticCode("BP1402")]
    public void AiPrimitive_WithUnknownWaitTarget_EmitsBP1402()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("A")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .WaitForChannel("UnknownChannel")
                .Return())
            .Build();

        // Non-empty catalog with a KNOWN wait target (different from the one in the asset).
        var waitCatalog = new SingleEntryWaitPrimitiveCatalog();
        var opts = new CompileOptions(
            Mode: CompilerMode.Debug,
            NodeRegistry: BuiltInNodeRegistry.Instance,
            TypeRegistry: StaticTypeRegistry.Instance,
            EngineEvents: BuiltInEngineEventCatalog.Instance,
            ChannelCommands: BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives: waitCatalog,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

        var sink = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink, opts));

        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP1402);
    }

    // ---- Graph structure tests ------------------------------------------

    [Fact]
    public void Library_GraphWithNoReturn_CompilesWithoutBP1601()
    {
        // Entry node present but no ReturnNode — implicit return is now synthesized.
        // BP1601 relaxed; graph should compile without that error.
        var asset = BlueprintAssetBuilder
            .Library("L")
            .WithGraph("G", g => g.Entry())
            .Build();

        var diags = Validate(asset);

        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP1601);
    }

    [Fact]
    [CoversDiagnosticCode("BP1602")]
    public void Library_GraphWithNoEntryNode_EmitsBP1602()
    {
        // Graph contains only a ReturnNode with no entry node.
        var asset = BlueprintAssetBuilder
            .Library("L")
            .WithGraph("G", g => g.Return())
            .Build();

        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1602);
    }

    [Fact]
    public void Library_HappyPath_NoDiagnostics()
    {
        var asset = BlueprintAssetBuilder
            .Library("L")
            .WithGraph("Add", g => g.Entry().Return())
            .Build();

        var diags = Validate(asset);

        Assert.Empty(diags.Where(d => d.IsError));
    }

    // ---- Inline test catalogs ------------------------------------------

    // Dummy types whose Type.Name matches the expected catalog strings.
    private sealed class KnownChannel { }
    private sealed class KnownWaitTarget { }
    private sealed class KnownEventType { }

    private sealed class SingleEntryChannelCommandCatalog : IChannelCommandCatalog
    {
        public IReadOnlyList<ChannelCommandCatalogEntry> GetEntries() =>
            new[] { new ChannelCommandCatalogEntry("KnownAction", typeof(KnownChannel).FullName!, 0, typeof(void).FullName!) };
    }

    private sealed class SingleEntryWaitPrimitiveCatalog : IWaitPrimitiveCatalog
    {
        public IReadOnlyList<WaitPrimitiveCatalogEntry> GetEntries() =>
            new[] { new WaitPrimitiveCatalogEntry("Known", WaitKind.Channel, typeof(KnownWaitTarget).FullName!) };
    }

    private sealed class SingleEntryEngineEventCatalog : IEngineEventCatalog
    {
        public IReadOnlyList<EngineEventCatalogEntry> GetEntries() =>
            new[] { new EngineEventCatalogEntry("Known", typeof(KnownEventType).FullName!) };
    }
}
