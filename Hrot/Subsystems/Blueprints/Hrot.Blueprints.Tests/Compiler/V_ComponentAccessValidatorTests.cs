using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// CA-03 (Slice W1): validator coverage for <c>V_ComponentAccessRules</c> (BP2060-BP2062) --
/// <c>SetComponentNode</c>'s STRUCTURAL rules (ComponentTypeFqn well-formed, self-only). Deliberately
/// does NOT test a <c>[BlueprintWritable]</c> gate -- the compiler cannot reflect over the real
/// component type (see <c>V_ComponentAccessRules</c>'s doc comment), so no such check exists here;
/// that gate is enforced editor-side only (CA-04).
/// </summary>
public sealed class V_ComponentAccessValidatorTests
{
    private static CompileOptions DefaultOptions() =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static IReadOnlyList<Diagnostic> Validate(BlueprintAsset asset)
    {
        var sink = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink, DefaultOptions()));
        return sink.All;
    }

    // ---- BP2060: ComponentTypeFqn empty ----------------------------------

    [Fact]
    [CoversDiagnosticCode("BP2060")]
    public void Validate_EmptyComponentTypeFqn_BP2060()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("SetComponentTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(new SetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "",
        });

        var diags = Validate(asset);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2060);
    }

    // ---- BP2061: ComponentTypeFqn not well-formed -------------------------

    [Fact]
    [CoversDiagnosticCode("BP2061")]
    public void Validate_MalformedComponentTypeFqn_BP2061()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("SetComponentTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(new SetComponentNode
        {
            Id               = Guid.NewGuid(),
            // Neither a StaticTypeRegistry primitive nor a well-formed dotted FQN -- unresolvable.
            ComponentTypeFqn = "not a type id",
        });

        var diags = Validate(asset);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2061);
    }

    // ---- BP2062: "Target" pin present -- self-only ------------------------

    [Fact]
    [CoversDiagnosticCode("BP2062")]
    public void Validate_TargetPinPresent_BP2062()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("SetComponentTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        var setComp = new SetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "System.Numerics.Vector3",
        };
        // SetComponent never gets a "Target" pin from Stage0 enrichment (self-only by
        // construction) -- author one directly to prove a hand-authored/legacy asset is caught.
        setComp.Pins.Add(new Pin
        {
            Id = Guid.NewGuid(), Name = "Target", Direction = "In", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "Fdp.Core.Entity" },
        });
        asset.Graphs[0].Nodes.Add(setComp);

        var diags = Validate(asset);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2062);
    }

    // ---- Happy path: valid SetComponent, no errors ------------------------

    [Fact]
    public void Validate_ValidComponentTypeFqn_NoComponentAccessErrors()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("SetComponentTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(new SetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "System.Numerics.Vector3",
        });

        var diags = Validate(asset);
        Assert.DoesNotContain(diags, d =>
            d.Code == DiagnosticCodes.BP2060
            || d.Code == DiagnosticCodes.BP2061
            || d.Code == DiagnosticCodes.BP2062);
    }

    // ---- Also accepts a primitive TypeId (e.g. System.Int32) --------------

    [Fact]
    public void Validate_PrimitiveComponentTypeFqn_NoBP2061()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("SetComponentTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(new SetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "System.Int32",
        });

        var diags = Validate(asset);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP2061);
    }

    // ---- BP2063 (CA-05, Slice 1b): managed GetComponent field -> persisting sink -------------

    private const string ManagedFqn = "Hrot.Blueprints.Tests.Fixtures.FakeManagedComponentForValidator";

    /// <summary>Builds a managed multi-pin GetComponentNode with one field pin "Name" + "Found".</summary>
    private static GetComponentNode MakeManagedGetComponent(out Pin namePin, out Pin foundPin)
    {
        namePin  = new Pin { Id = Guid.NewGuid(), Name = "Name",  Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.String" } };
        foundPin = new Pin { Id = Guid.NewGuid(), Name = "Found", Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Boolean" } };
        var node = new GetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = ManagedFqn,
            IsManaged        = true,
            Fields           = new List<ComponentFieldDecl> { new() { Name = "Name", TypeId = "System.String" } },
        };
        node.Pins.AddRange(new[] { namePin, foundPin });
        return node;
    }

    [Fact]
    [CoversDiagnosticCode("BP2063")]
    public void Validate_ManagedFieldWiredIntoSetVariable_BP2063()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("ManagedReadTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        var getComp = MakeManagedGetComponent(out var namePin, out _);
        var setVar  = new SetVariableNode { Id = Guid.NewGuid(), VariableId = "SomeVar" };
        var setValueIn = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "In", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.String" } };
        setVar.Pins.Add(setValueIn);

        asset.Graphs[0].Nodes.Add(getComp);
        asset.Graphs[0].Nodes.Add(setVar);
        asset.Graphs[0].Links.Add(new Link { FromNodeId = getComp.Id, FromPinId = namePin.Id, ToNodeId = setVar.Id, ToPinId = setValueIn.Id });

        var diags = Validate(asset);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2063);
    }

    [Fact]
    [CoversDiagnosticCode("BP2063")]
    public void Validate_ManagedFieldWiredIntoSetShared_BP2063()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("ManagedReadTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        var getComp  = MakeManagedGetComponent(out var namePin, out _);
        var setShared = new SetSharedNode { Id = Guid.NewGuid(), VariableId = "SomeSlot", SharedTypeId = "System.String" };
        var setValueIn = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "In", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.String" } };
        setShared.Pins.Add(setValueIn);

        asset.Graphs[0].Nodes.Add(getComp);
        asset.Graphs[0].Nodes.Add(setShared);
        asset.Graphs[0].Links.Add(new Link { FromNodeId = getComp.Id, FromPinId = namePin.Id, ToNodeId = setShared.Id, ToPinId = setValueIn.Id });

        var diags = Validate(asset);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2063);
    }

    [Fact]
    public void Validate_ManagedFieldWiredIntoFunctionCall_NoBP2063_LegitimatePassThrough()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("ManagedReadTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        var getComp = MakeManagedGetComponent(out var namePin, out _);
        var call    = new FunctionCallNode { Id = Guid.NewGuid(), TargetTypeId = "Some.Library", MethodName = "Consume", IsPure = true };
        var callArgIn = new Pin { Id = Guid.NewGuid(), Name = "Arg0", Direction = "In", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = ManagedFqn } };
        call.Pins.Add(callArgIn);

        asset.Graphs[0].Nodes.Add(getComp);
        asset.Graphs[0].Nodes.Add(call);
        asset.Graphs[0].Links.Add(new Link { FromNodeId = getComp.Id, FromPinId = namePin.Id, ToNodeId = call.Id, ToPinId = callArgIn.Id });

        var diags = Validate(asset);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP2063);
    }

    [Fact]
    public void Validate_ManagedFoundPinWiredIntoSetVariable_NoBP2063_FoundIsNeverManaged()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("ManagedReadTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        var getComp = MakeManagedGetComponent(out _, out var foundPin);
        var setVar  = new SetVariableNode { Id = Guid.NewGuid(), VariableId = "FoundVar" };
        var setValueIn = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "In", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Boolean" } };
        setVar.Pins.Add(setValueIn);

        asset.Graphs[0].Nodes.Add(getComp);
        asset.Graphs[0].Nodes.Add(setVar);
        asset.Graphs[0].Links.Add(new Link { FromNodeId = getComp.Id, FromPinId = foundPin.Id, ToNodeId = setVar.Id, ToPinId = setValueIn.Id });

        var diags = Validate(asset);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP2063);
    }

    [Fact]
    public void Validate_UnmanagedGetComponent_FieldWiredIntoSetVariable_NoBP2063()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("UnmanagedReadTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        var xPin = new Pin { Id = Guid.NewGuid(), Name = "X", Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } };
        var getComp = new GetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "System.Numerics.Vector3",
            IsManaged        = false,
            Fields           = new List<ComponentFieldDecl> { new() { Name = "X", TypeId = "System.Single" } },
        };
        getComp.Pins.Add(xPin);

        var setVar = new SetVariableNode { Id = Guid.NewGuid(), VariableId = "FloatVar" };
        var setValueIn = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "In", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } };
        setVar.Pins.Add(setValueIn);

        asset.Graphs[0].Nodes.Add(getComp);
        asset.Graphs[0].Nodes.Add(setVar);
        asset.Graphs[0].Links.Add(new Link { FromNodeId = getComp.Id, FromPinId = xPin.Id, ToNodeId = setVar.Id, ToPinId = setValueIn.Id });

        var diags = Validate(asset);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP2063);
    }

    // ---- BP2064 (CA-06, Slice W2, Q#16-C): managed SetComponent carries per-field Fields -------

    private const string ManagedWriteFqn = "Hrot.Blueprints.Tests.Fixtures.FakeManagedComponentForSetValidator";

    [Fact]
    [CoversDiagnosticCode("BP2064")]
    public void Validate_ManagedSetComponentWithPerFieldFields_BP2064()
    {
        var asset = BlueprintAssetBuilder
            .Instance("ManagedSetComponentTest")
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(new SetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = ManagedWriteFqn,
            IsManaged        = true,
            // A managed node must NEVER carry per-field Fields (whole-replace only) -- authored here
            // to prove a hand-authored/legacy/editor-bug asset is caught.
            Fields = new List<ComponentFieldDecl> { new() { Name = "Name", TypeId = "System.String" } },
        });

        var diags = Validate(asset);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2064);
    }

    [Fact]
    public void Validate_ManagedSetComponentWholeValueShape_NoBP2064()
    {
        var asset = BlueprintAssetBuilder
            .Instance("ManagedSetComponentTest")
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(new SetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = ManagedWriteFqn,
            IsManaged        = true,
            Fields           = null,
        });

        var diags = Validate(asset);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP2064);
    }

    [Fact]
    public void Validate_UnmanagedSetComponentWithPerFieldFields_NoBP2064()
    {
        var asset = BlueprintAssetBuilder
            .Instance("UnmanagedSetComponentTest")
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(new SetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "System.Numerics.Vector3",
            IsManaged        = false,
            Fields = new List<ComponentFieldDecl> { new() { Name = "X", TypeId = "System.Single" } },
        });

        var diags = Validate(asset);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP2064);
    }

    // ---- BP2065 (CA-06, Slice W2): managed SetComponent in AiPrimitive dispatch -- no ECB --------

    [Fact]
    [CoversDiagnosticCode("BP2065")]
    public void Validate_ManagedSetComponentInAiPrimitive_BP2065()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("ManagedSetComponentAiPrimitiveTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(new SetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = ManagedWriteFqn,
            IsManaged        = true,
        });

        var diags = Validate(asset);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2065);
    }

    [Fact]
    public void Validate_ManagedSetComponentInInstanceDispatch_NoBP2065()
    {
        var asset = BlueprintAssetBuilder
            .Instance("ManagedSetComponentInstanceTest")
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(new SetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = ManagedWriteFqn,
            IsManaged        = true,
        });

        var diags = Validate(asset);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP2065);
    }

    [Fact]
    public void Validate_UnmanagedSetComponentInAiPrimitive_NoBP2065()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("UnmanagedSetComponentAiPrimitiveTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(new SetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "System.Numerics.Vector3",
            IsManaged        = false,
        });

        var diags = Validate(asset);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP2065);
    }
}
