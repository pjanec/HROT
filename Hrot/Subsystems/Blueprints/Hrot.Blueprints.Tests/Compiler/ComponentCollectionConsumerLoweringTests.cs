using System.Text.RegularExpressions;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Xunit;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// CA-07b — lowering + emit-golden coverage for the three component-collection consumer nodes
/// (<see cref="ComponentForEachNode"/>/<see cref="ComponentItemGetNode"/>/
/// <see cref="ComponentItemCountNode"/>). Each fixture wires a
/// <c>GetComponent&lt;Hrot.AI.Behaviors.BpCollectionDemo&gt;</c> collection out-pin ("Values")
/// straight into a consumer, mirroring CA-07a's <c>GetComponentPinParityTests</c>/
/// <c>NodeCoverageTests</c> demo-shape and asserting the SAME emission-contract style
/// <c>NodeCoverageTests.FlowForEach_IndexAndCount_EmitsHoistedCountAndBodyIndexCopy</c> uses:
/// compile through the real Stage1-7 pipeline (no Roslyn -- BpCollectionDemoOps lives in
/// Hrot.AI.Behaviors, a game assembly the coverage-Roslyn compile does not reference, same reason
/// FlowForEach's own coverage fixture stays ValidateOnlyStage1To7) and assert on the generated C#
/// verbatim.
/// </summary>
public sealed class ComponentCollectionConsumerLoweringTests
{
    private const string ComponentFqn = "Hrot.AI.Behaviors.BpCollectionDemo";
    private const string CountFqn     = "Hrot.AI.Behaviors.Brains.BpCollectionDemoOps.Count";
    private const string ItemFqn      = "Hrot.AI.Behaviors.Brains.BpCollectionDemoOps.Item";

    private static CompileOptions DefaultCompileOptions() => new(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static Pin ExecPin(string name, string direction) =>
        new() { Id = Guid.NewGuid(), Name = name, Direction = direction, IsExec = true, TypeRef = new() };

    private static Pin DataPin(string name, string direction, string typeId, bool isArray = false) =>
        new() { Id = Guid.NewGuid(), Name = name, Direction = direction, IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = typeId, IsArray = isArray } };

    /// <summary>
    /// Builds a pin-authored <c>GetComponent&lt;BpCollectionDemo&gt;</c> node with a single baked
    /// collection decl ("Values", element System.Int32) -- multi-pin shape: "Values" (Out, IsArray)
    /// + "Found" (Out, bool). No "Target" pin wired anywhere in these fixtures (self-default).
    /// </summary>
    private static (GetComponentNode Node, Pin ValuesOut) BuildGetComponentCollectionNode()
    {
        var valuesOut = DataPin("Values", "Out", "System.Int32", isArray: true);
        var foundOut  = DataPin("Found",  "Out", "System.Boolean");
        var node = new GetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = ComponentFqn,
            Fields = new List<ComponentFieldDecl>
            {
                new()
                {
                    Name             = "Values",
                    TypeId           = "",
                    IsCollection     = true,
                    ElementTypeId    = "System.Int32",
                    CountAccessorFqn = CountFqn,
                    ItemAccessorFqn  = ItemFqn,
                },
            },
        };
        node.Pins.AddRange(new[] { valuesOut, foundOut });
        return (node, valuesOut);
    }

    // ── ComponentItemCountNode ────────────────────────────────────────────────

    [Fact]
    public void ComponentItemCount_Lowering_EmitsAccessorCallOffResolvedEntity()
    {
        var (getNode, valuesOut) = BuildGetComponentCollectionNode();

        var collectionIn = DataPin("Collection", "In",  "System.Int32", isArray: true);
        var countOut     = DataPin("Count",      "Out", "System.Int32");
        var countNode = new ComponentItemCountNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = ComponentFqn,
            CountAccessorFqn = CountFqn,
        };
        countNode.Pins.AddRange(new[] { collectionIn, countOut });

        var intVarId = Guid.NewGuid();
        var intVar = new VariableDecl { Id = intVarId, Name = "CountOut", Type = new BlueprintTypeRef { TypeId = "System.Int32" } };

        var setExecIn   = ExecPin("ExecIn",  "In");
        var setExecOut  = ExecPin("ExecOut", "Out");
        var setValueIn  = DataPin("Value", "In", "System.Int32");
        var setNode = new SetVariableNode { Id = Guid.NewGuid(), VariableId = intVarId.ToString() };
        setNode.Pins.AddRange(new[] { setExecIn, setExecOut, setValueIn });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, getNode, countNode, setNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,    FromPinId = entryOut.Id,     ToNodeId = setNode.Id,   ToPinId = setExecIn.Id },
                new Link { FromNodeId = setNode.Id,  FromPinId = setExecOut.Id,   ToNodeId = ret.Id,       ToPinId = retIn.Id },
                new Link { FromNodeId = getNode.Id,  FromPinId = valuesOut.Id,    ToNodeId = countNode.Id, ToPinId = collectionIn.Id },
                new Link { FromNodeId = countNode.Id, FromPinId = countOut.Id,    ToNodeId = setNode.Id,   ToPinId = setValueIn.Id },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId   = Guid.NewGuid(),
            Name      = "ComponentItemCountCoverage",
            Dispatch  = BlueprintDispatchKind.Instance,
            Variables = { intVar },
            Graphs    = { graph },
        };

        var result = new BlueprintCompiler().Compile(asset, DefaultCompileOptions());
        Assert.True(result.Succeeded,
            "Compile failed: " + string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));
        var src = result.GeneratedSource!;
        Assert.NotNull(src);

        // Re-reads GetComponentRO<BpCollectionDemo> TWICE: once inside GetComponentNode's own
        // (CA-01) multi-pin projection (unused here besides Found), once more inside
        // ComponentItemCountNode's case, off the SAME resolved (self) entity -- proves the "re-read
        // off the resolved entity" contract, not an accidental reuse of GetComponentNode's read.
        Assert.Equal(2, Regex.Matches(src, Regex.Escape($"GetComponentRO<global::{ComponentFqn}>")).Count);
        Assert.Contains($"= global::{CountFqn}(", src);
    }

    [Fact]
    public void ComponentItemCount_UnwiredCollection_CompilesToSafeDefault_NoAccessorCallEmitted()
    {
        var collectionIn = DataPin("Collection", "In",  "System.Object", isArray: true);
        var countOut     = DataPin("Count",      "Out", "System.Int32");
        var countNode = new ComponentItemCountNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = ComponentFqn,
            CountAccessorFqn = CountFqn,
        };
        countNode.Pins.AddRange(new[] { collectionIn, countOut });

        var intVarId = Guid.NewGuid();
        var intVar = new VariableDecl { Id = intVarId, Name = "CountOut", Type = new BlueprintTypeRef { TypeId = "System.Int32" } };

        var setExecIn  = ExecPin("ExecIn",  "In");
        var setExecOut = ExecPin("ExecOut", "Out");
        var setValueIn = DataPin("Value", "In", "System.Int32");
        var setNode = new SetVariableNode { Id = Guid.NewGuid(), VariableId = intVarId.ToString() };
        setNode.Pins.AddRange(new[] { setExecIn, setExecOut, setValueIn });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, countNode, setNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,    FromPinId = entryOut.Id,   ToNodeId = setNode.Id,   ToPinId = setExecIn.Id },
                new Link { FromNodeId = setNode.Id,  FromPinId = setExecOut.Id, ToNodeId = ret.Id,       ToPinId = retIn.Id },
                new Link { FromNodeId = countNode.Id, FromPinId = countOut.Id,  ToNodeId = setNode.Id,   ToPinId = setValueIn.Id },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId   = Guid.NewGuid(),
            Name      = "ComponentItemCountUnwiredCoverage",
            Dispatch  = BlueprintDispatchKind.Instance,
            Variables = { intVar },
            Graphs    = { graph },
        };

        var result = new BlueprintCompiler().Compile(asset, DefaultCompileOptions());
        Assert.True(result.Succeeded,
            "Compile failed: " + string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));
        var src = result.GeneratedSource!;
        Assert.DoesNotContain(CountFqn, src);
        Assert.DoesNotContain($"GetComponentRO<global::{ComponentFqn}>", src);
    }

    // ── ComponentItemGetNode ──────────────────────────────────────────────────

    [Fact]
    public void ComponentItemGet_Lowering_EmitsAccessorCallWithIndexOffResolvedEntity()
    {
        var (getNode, valuesOut) = BuildGetComponentCollectionNode();

        var collectionIn = DataPin("Collection", "In",  "System.Int32", isArray: true);
        var indexIn      = DataPin("Index",      "In",  "System.Int32");
        var elementOut   = DataPin("Element",    "Out", "System.Int32");
        var getItemNode = new ComponentItemGetNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = ComponentFqn,
            ItemAccessorFqn  = ItemFqn,
            ElementTypeFqn   = "System.Int32",
        };
        getItemNode.Pins.AddRange(new[] { collectionIn, indexIn, elementOut });

        var litValueOut = DataPin("Value", "Out", "System.Int32");
        var litNode = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = "0" };
        litNode.Pins.Add(litValueOut);

        var intVarId = Guid.NewGuid();
        var intVar = new VariableDecl { Id = intVarId, Name = "ElementOut", Type = new BlueprintTypeRef { TypeId = "System.Int32" } };

        var setExecIn  = ExecPin("ExecIn",  "In");
        var setExecOut = ExecPin("ExecOut", "Out");
        var setValueIn = DataPin("Value", "In", "System.Int32");
        var setNode = new SetVariableNode { Id = Guid.NewGuid(), VariableId = intVarId.ToString() };
        setNode.Pins.AddRange(new[] { setExecIn, setExecOut, setValueIn });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, getNode, litNode, getItemNode, setNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,      FromPinId = entryOut.Id,     ToNodeId = setNode.Id,     ToPinId = setExecIn.Id },
                new Link { FromNodeId = setNode.Id,    FromPinId = setExecOut.Id,   ToNodeId = ret.Id,         ToPinId = retIn.Id },
                new Link { FromNodeId = getNode.Id,    FromPinId = valuesOut.Id,    ToNodeId = getItemNode.Id, ToPinId = collectionIn.Id },
                new Link { FromNodeId = litNode.Id,    FromPinId = litValueOut.Id,  ToNodeId = getItemNode.Id, ToPinId = indexIn.Id },
                new Link { FromNodeId = getItemNode.Id, FromPinId = elementOut.Id,  ToNodeId = setNode.Id,     ToPinId = setValueIn.Id },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId   = Guid.NewGuid(),
            Name      = "ComponentItemGetCoverage",
            Dispatch  = BlueprintDispatchKind.Instance,
            Variables = { intVar },
            Graphs    = { graph },
        };

        var result = new BlueprintCompiler().Compile(asset, DefaultCompileOptions());
        Assert.True(result.Succeeded,
            "Compile failed: " + string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));
        var src = result.GeneratedSource!;
        Assert.NotNull(src);

        Assert.Contains($"GetComponentRO<global::{ComponentFqn}>", src);
        // Item accessor call takes (component, index) -- two args.
        Assert.Matches(new Regex(Regex.Escape($"= global::{ItemFqn}(") + @"__t\d+, __t\d+\);"), src);
    }

    // ── ComponentForEachNode ──────────────────────────────────────────────────

    [Fact]
    public void ComponentForEach_Lowering_EmitsForLoopMirroringFlowForEachShape()
    {
        var (getNode, valuesOut) = BuildGetComponentCollectionNode();

        var feIn        = ExecPin("In", "In");
        var feCollection = DataPin("Collection", "In", "System.Int32", isArray: true);
        var feBody      = ExecPin("Body", "Out");
        var feCompleted = ExecPin("Completed", "Out");
        var feItem      = DataPin("CurrentItem", "Out", "System.Int32");
        var fe = new ComponentForEachNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = ComponentFqn,
            CountAccessorFqn = CountFqn,
            ItemAccessorFqn  = ItemFqn,
            ElementTypeFqn   = "System.Int32",
        };
        fe.Pins.AddRange(new[] { feIn, feCollection, feBody, feCompleted, feItem });

        var intVarId = Guid.NewGuid();
        var intVar = new VariableDecl { Id = intVarId, Name = "ItemOut", Type = new BlueprintTypeRef { TypeId = "System.Int32" } };

        var setExecIn  = ExecPin("ExecIn",  "In");
        var setExecOut = ExecPin("ExecOut", "Out");
        var setValueIn = DataPin("Value", "In", "System.Int32");
        var setNode = new SetVariableNode { Id = Guid.NewGuid(), VariableId = intVarId.ToString() };
        setNode.Pins.AddRange(new[] { setExecIn, setExecOut, setValueIn });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("In", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, getNode, fe, setNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id, FromPinId = entryOut.Id,    ToNodeId = fe.Id,      ToPinId = feIn.Id },
                new Link { FromNodeId = getNode.Id, FromPinId = valuesOut.Id, ToNodeId = fe.Id,      ToPinId = feCollection.Id },
                new Link { FromNodeId = fe.Id,    FromPinId = feBody.Id,      ToNodeId = setNode.Id, ToPinId = setExecIn.Id },
                new Link { FromNodeId = fe.Id,    FromPinId = feItem.Id,      ToNodeId = setNode.Id, ToPinId = setValueIn.Id },
                new Link { FromNodeId = fe.Id,    FromPinId = feCompleted.Id, ToNodeId = ret.Id,     ToPinId = retIn.Id },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId   = Guid.NewGuid(),
            Name      = "ComponentForEachCoverage",
            Dispatch  = BlueprintDispatchKind.Instance,
            Variables = { intVar },
            Graphs    = { graph },
        };

        var result = new BlueprintCompiler().Compile(asset, DefaultCompileOptions());
        Assert.True(result.Succeeded,
            "Compile failed: " + string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));
        var src = result.GeneratedSource!;
        Assert.NotNull(src);

        // Re-reads GetComponentRO<BpCollectionDemo> off the resolved (self) entity, distinct from
        // GetComponentNode's own read.
        Assert.Equal(2, Regex.Matches(src, Regex.Escape($"GetComponentRO<global::{ComponentFqn}>")).Count);

        // Mirrors IrOp_ForEach's exact shape (unchanged -- FlowForEachNode's own for-loop lowering):
        // `for (int __feN = 0; __feN < global::…Count(__tM); __feN++) { var __tK = global::…Item(__tM, __feN); ... }`
        Assert.Matches(new Regex(@"for \(int __fe\d+ = 0; __fe\d+ < " + Regex.Escape($"global::{CountFqn}(") + @"__t\d+\); __fe\d+\+\+\)"), src);
        Assert.Matches(new Regex(Regex.Escape($"= global::{ItemFqn}(") + @"__t\d+, __fe\d+\);"), src);
    }

    [Fact]
    public void ComponentForEach_UnwiredCollection_CompilesToEmptyLoop_NoForEachEmitted()
    {
        var feIn        = ExecPin("In", "In");
        var feCollection = DataPin("Collection", "In", "System.Object", isArray: true);
        var feBody      = ExecPin("Body", "Out");
        var feCompleted = ExecPin("Completed", "Out");
        var feItem      = DataPin("CurrentItem", "Out", "System.Int32");
        var fe = new ComponentForEachNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = ComponentFqn,
            CountAccessorFqn = CountFqn,
            ItemAccessorFqn  = ItemFqn,
            ElementTypeFqn   = "System.Int32",
        };
        fe.Pins.AddRange(new[] { feIn, feCollection, feBody, feCompleted, feItem });

        var pubIn     = ExecPin("In", "In");
        var pubOut    = ExecPin("Out", "Out");
        var pub = new PublishEventNode { Id = Guid.NewGuid(), EventId = "ClearBehaviorEvent" };
        pub.Pins.AddRange(new[] { pubIn, pubOut });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("In", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, fe, pub, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id, FromPinId = entryOut.Id,    ToNodeId = fe.Id,  ToPinId = feIn.Id },
                new Link { FromNodeId = fe.Id,    FromPinId = feBody.Id,      ToNodeId = pub.Id, ToPinId = pubIn.Id },
                new Link { FromNodeId = fe.Id,    FromPinId = feCompleted.Id, ToNodeId = ret.Id, ToPinId = retIn.Id },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "ComponentForEachUnwiredCoverage",
            Dispatch = BlueprintDispatchKind.Instance,
            Graphs   = { graph },
        };

        var result = new BlueprintCompiler().Compile(asset, DefaultCompileOptions());
        Assert.True(result.Succeeded,
            "Compile failed: " + string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));
        var src = result.GeneratedSource!;
        // No loop, no re-read, no PublishEvent call -- Body never runs (safe default: empty loop).
        Assert.DoesNotContain("for (int __fe", src);
        Assert.DoesNotContain($"GetComponentRO<global::{ComponentFqn}>", src);
    }

    // ── Stage2 validator (BP2066) ─────────────────────────────────────────────

    [Fact]
    public void WiredCollection_EmptyBakedAccessors_ReportsBP2066()
    {
        var collectionIn = DataPin("Collection", "In",  "System.Object", isArray: true);
        var countOut     = DataPin("Count",      "Out", "System.Int32");
        // ComponentTypeFqn baked, but CountAccessorFqn left empty -- structurally invalid once wired.
        var countNode = new ComponentItemCountNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = ComponentFqn,
            CountAccessorFqn = "",
        };
        countNode.Pins.AddRange(new[] { collectionIn, countOut });

        var litValueOut = DataPin("Values", "Out", "System.Int32", isArray: true);
        var litNode = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = "0" };
        litNode.Pins.Add(litValueOut);

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, litNode, countNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id, FromPinId = entryOut.Id,  ToNodeId = ret.Id,       ToPinId = retIn.Id },
                new Link { FromNodeId = litNode.Id, FromPinId = litValueOut.Id, ToNodeId = countNode.Id, ToPinId = collectionIn.Id },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "ComponentItemCountBP2066Coverage",
            Dispatch = BlueprintDispatchKind.Instance,
            Graphs   = { graph },
        };

        var result = new BlueprintCompiler().Compile(asset, DefaultCompileOptions());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.BP2066 && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void UnwiredCollection_EmptyBakedAccessors_DoesNotReportBP2066()
    {
        var collectionIn = DataPin("Collection", "In",  "System.Object", isArray: true);
        var countOut     = DataPin("Count",      "Out", "System.Int32");
        var countNode = new ComponentItemCountNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "",
            CountAccessorFqn = "",
        };
        countNode.Pins.AddRange(new[] { collectionIn, countOut });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, countNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id, FromPinId = entryOut.Id, ToNodeId = ret.Id, ToPinId = retIn.Id },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "ComponentItemCountUnwiredBP2066Coverage",
            Dispatch = BlueprintDispatchKind.Instance,
            Graphs   = { graph },
        };

        var result = new BlueprintCompiler().Compile(asset, DefaultCompileOptions());

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCodes.BP2066);
    }
}
