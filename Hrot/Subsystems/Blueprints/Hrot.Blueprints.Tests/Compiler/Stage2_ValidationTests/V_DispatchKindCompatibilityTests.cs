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

    [Fact]
    [CoversDiagnosticCode("BP1024")]
    public void AiPrimitive_WithVariable_EmitsBP1024()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("A")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithVariable("x", typeof(int))
            .Build();

        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1024);
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

    [Fact]
    [CoversDiagnosticCode("BP1031")]
    public void Instance_WithParams_EmitsBP1031()
    {
        var asset = BlueprintAssetBuilder
            .Instance("I")
            .WithParameter("p", typeof(int))
            .Build();

        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1031);
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
