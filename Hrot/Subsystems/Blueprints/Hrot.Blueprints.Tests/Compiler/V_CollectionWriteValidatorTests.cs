using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// FC-1 (Q#20): validator coverage for <c>V_ComponentAccessRules</c>' collection-WRITE rules
/// (BP2067-BP2071) -- the write analog of BP2060-BP2062/BP2066. Mirrors
/// <see cref="V_ComponentAccessValidatorTests"/>' Stage2-only harness. Like the read-side tests,
/// deliberately NO <c>[BlueprintWritable]</c>/accessor-existence gate is tested -- both are
/// editor-primary (the netstandard2.0 compiler cannot reflect over game assemblies); Stage2 checks
/// stay structural.
/// </summary>
public sealed class V_CollectionWriteValidatorTests
{
    private const string ComponentFqn = "Hrot.AI.Behaviors.BpFixedListDemo";
    private const string OpsFqn       = "Hrot.AI.Behaviors.Brains.BpFixedListDemoOps";

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

    private static BlueprintAsset EmptyAsset() => BlueprintAssetBuilder
        .AiPrimitive("CollectionWriteTest")
        .WithHostings(AiPrimitiveHosting.BTreeAction)
        .WithGraph("Main", g => g.Entry().Return())
        .Build();

    private static Pin DataPin(string name, string direction, string typeId, bool isArray = false) =>
        new() { Id = Guid.NewGuid(), Name = name, Direction = direction, IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = typeId, IsArray = isArray } };

    /// <summary>Write node with an authored "Collection" in-pin (+ optionally baked FQNs), and a producing GetComponent whose collection out-pin is wired into it.</summary>
    private static (CollectionWriteNode Write, GetComponentNode Producer) AddWiredWrite(
        BlueprintAsset asset, string componentTypeFqn = ComponentFqn, string writeAccessorFqn = OpsFqn + ".SetAt")
    {
        var graph = asset.Graphs[0];

        var itemsOut = DataPin("Items", "Out", "System.Int32", isArray: true);
        var producer = new GetComponentNode { Id = Guid.NewGuid(), ComponentTypeFqn = ComponentFqn };
        producer.Pins.Add(itemsOut);

        var collectionIn = DataPin("Collection", "In", "System.Int32", isArray: true);
        var write = new CollectionWriteNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = componentTypeFqn,
            Op               = CollectionWriteOp.SetAt,
            WriteAccessorFqn = writeAccessorFqn,
            ElementTypeFqn   = "System.Int32",
        };
        write.Pins.Add(collectionIn);

        graph.Nodes.Add(producer);
        graph.Nodes.Add(write);
        graph.Links.Add(new Link
        {
            FromNodeId = producer.Id, FromPinId = itemsOut.Id,
            ToNodeId = write.Id, ToPinId = collectionIn.Id,
        });
        return (write, producer);
    }

    // ---- BP2067: wired but not baked --------------------------------------

    [Fact]
    [CoversDiagnosticCode("BP2067")]
    public void Validate_WiredButUnbakedAccessor_BP2067()
    {
        var asset = EmptyAsset();
        AddWiredWrite(asset, writeAccessorFqn: "");
        Assert.Contains(Validate(asset), d => d.Code == DiagnosticCodes.BP2067);
    }

    [Fact]
    [CoversDiagnosticCode("BP2067")]
    public void Validate_WiredButMalformedComponentFqn_BP2067()
    {
        var asset = EmptyAsset();
        AddWiredWrite(asset, componentTypeFqn: "not a type id");
        Assert.Contains(Validate(asset), d => d.Code == DiagnosticCodes.BP2067);
    }

    [Fact]
    public void Validate_UnwiredCollection_NoBakeDiagnostic()
    {
        // Unwired = legitimate not-used-yet (mirrors BP2066's wired-gating): no BP2067 even with
        // nothing baked.
        var asset = EmptyAsset();
        var write = new CollectionWriteNode { Id = Guid.NewGuid() };
        write.Pins.Add(DataPin("Collection", "In", "System.Object", isArray: true));
        asset.Graphs[0].Nodes.Add(write);
        Assert.DoesNotContain(Validate(asset), d => d.Code == DiagnosticCodes.BP2067);
    }

    // ---- BP2068: ManagedMember collections are not element-writable -------

    [Fact]
    [CoversDiagnosticCode("BP2068")]
    public void Validate_ManagedMemberKind_BP2068()
    {
        var asset = EmptyAsset();
        var (write, _) = AddWiredWrite(asset);
        write.CollectionKind = CollectionKind.ManagedMember;
        Assert.Contains(Validate(asset), d => d.Code == DiagnosticCodes.BP2068);
    }

    // ---- BP2069: "Target" pin -- self-only --------------------------------

    [Fact]
    [CoversDiagnosticCode("BP2069")]
    public void Validate_TargetPinPresent_BP2069()
    {
        var asset = EmptyAsset();
        var write = new CollectionWriteNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = ComponentFqn,
            WriteAccessorFqn = OpsFqn + ".Add",
            Op               = CollectionWriteOp.Add,
        };
        write.Pins.Add(DataPin("Target", "In", "Fdp.Core.Entity"));
        asset.Graphs[0].Nodes.Add(write);
        Assert.Contains(Validate(asset), d => d.Code == DiagnosticCodes.BP2069);
    }

    // ---- BP2070: cross-entity producer (G4) -------------------------------

    [Fact]
    [CoversDiagnosticCode("BP2070")]
    public void Validate_ProducerTargetWired_BP2070()
    {
        var asset = EmptyAsset();
        var (_, producer) = AddWiredWrite(asset);

        var targetIn = DataPin("Target", "In", "Fdp.Core.Entity");
        producer.Pins.Add(targetIn);
        var litOut = DataPin("Value", "Out", "Fdp.Core.Entity");
        var lit    = new LiteralNode { Id = Guid.NewGuid(), TypeId = "Fdp.Core.Entity", ValueJson = "default" };
        lit.Pins.Add(litOut);
        asset.Graphs[0].Nodes.Add(lit);
        asset.Graphs[0].Links.Add(new Link
        {
            FromNodeId = lit.Id, FromPinId = litOut.Id,
            ToNodeId = producer.Id, ToPinId = targetIn.Id,
        });

        Assert.Contains(Validate(asset), d => d.Code == DiagnosticCodes.BP2070);
    }

    [Fact]
    public void Validate_ProducerTargetUnwired_NoBP2070()
    {
        var asset = EmptyAsset();
        var (_, producer) = AddWiredWrite(asset);
        producer.Pins.Add(DataPin("Target", "In", "Fdp.Core.Entity"));   // present but unwired = self-default
        Assert.DoesNotContain(Validate(asset), d => d.Code == DiagnosticCodes.BP2070);
    }

    // ---- BP2071 (warning): write inside iteration of the same collection --

    private static CollectionForEachNode AddForEach(BlueprintAsset asset)
    {
        var graph = asset.Graphs[0];
        var forEach = new CollectionForEachNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = ComponentFqn,
            CountAccessorFqn = OpsFqn + ".Count",
            ItemAccessorFqn  = OpsFqn + ".Item",
            ElementTypeFqn   = "System.Int32",
        };
        forEach.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "Body", Direction = "Out", IsExec = true, TypeRef = new() });
        forEach.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "Completed", Direction = "Out", IsExec = true, TypeRef = new() });
        graph.Nodes.Add(forEach);
        return forEach;
    }

    private static void WireBody(BlueprintAsset asset, CollectionForEachNode forEach, CollectionWriteNode write)
    {
        var bodyPin = forEach.Pins.First(p => p.Name == "Body");
        var execIn = new Pin { Id = Guid.NewGuid(), Name = "In", Direction = "In", IsExec = true, TypeRef = new() };
        write.Pins.Add(execIn);
        asset.Graphs[0].Links.Add(new Link
        {
            FromNodeId = forEach.Id, FromPinId = bodyPin.Id,
            ToNodeId = write.Id, ToPinId = execIn.Id,
        });
    }

    [Fact]
    [CoversDiagnosticCode("BP2071")]
    public void Validate_WriteInsideForEachBody_SameCollection_BP2071Warning()
    {
        var asset = EmptyAsset();
        var (write, _) = AddWiredWrite(asset);
        var forEach = AddForEach(asset);
        WireBody(asset, forEach, write);

        var diag = Validate(asset).FirstOrDefault(d => d.Code == DiagnosticCodes.BP2071);
        Assert.NotNull(diag);
        Assert.False(diag!.IsError);   // G3 mandates a WARNING (start lenient)
    }

    [Fact]
    public void Validate_WriteInsideForEachBody_DifferentCollection_NoBP2071()
    {
        // Same component type but a DIFFERENT ops class (= a different collection on the same
        // component) -- must not warn.
        var asset = EmptyAsset();
        var (write, _) = AddWiredWrite(asset, writeAccessorFqn: "Some.Other.OpsClass.SetAt");
        var forEach = AddForEach(asset);
        WireBody(asset, forEach, write);

        Assert.DoesNotContain(Validate(asset), d => d.Code == DiagnosticCodes.BP2071);
    }

    [Fact]
    public void Validate_WriteAfterCompleted_NoBP2071()
    {
        // The write hangs off "Completed", not "Body" -- iterating is over, no warning.
        var asset = EmptyAsset();
        var (write, _) = AddWiredWrite(asset);
        var forEach = AddForEach(asset);
        var completedPin = forEach.Pins.First(p => p.Name == "Completed");
        var execIn = new Pin { Id = Guid.NewGuid(), Name = "In", Direction = "In", IsExec = true, TypeRef = new() };
        write.Pins.Add(execIn);
        asset.Graphs[0].Links.Add(new Link
        {
            FromNodeId = forEach.Id, FromPinId = completedPin.Id,
            ToNodeId = write.Id, ToPinId = execIn.Id,
        });

        Assert.DoesNotContain(Validate(asset), d => d.Code == DiagnosticCodes.BP2071);
    }

    // ---- Happy path --------------------------------------------------------

    [Fact]
    public void Validate_WellFormedWiredWrite_NoCollectionWriteDiagnostics()
    {
        var asset = EmptyAsset();
        AddWiredWrite(asset);
        var diags = Validate(asset);
        Assert.DoesNotContain(diags, d =>
            d.Code is DiagnosticCodes.BP2067 or DiagnosticCodes.BP2068
                   or DiagnosticCodes.BP2069 or DiagnosticCodes.BP2070 or DiagnosticCodes.BP2071);
    }
}
