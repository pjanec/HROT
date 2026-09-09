using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>
/// FC-2/LV-2 (Q#19-A, review F1 + the decided ref-bind read contract) -- list-variable READS through
/// the SAME five consumer nodes the component collections use (the A1 UX the user required):
/// <list type="bullet">
///   <item>Stage5 binds a writable `ref` onto the state field (<c>ref var __tN = ref s.MyList;</c>)
///   -- NO entity resolution, NO component re-read;</item>
///   <item>emit renders the F2 defensive clamp (<c>Math.Min(__tN.Count, N)</c>) as every count/bound
///   and a guarded never-throw element read; the ForEach bound is snapshotted at entry;</item>
///   <item>the editor projects the list variable's collection out-pin (parity with Stage0), the
///   wire-bake stamps <c>Kind=BlackboardFixedList</c> + the variable name, and BP2066 is Kind-aware.</item>
/// </list>
/// </summary>
public sealed class ListVariableReadTests
{
    private static CompileOptions Options() => new(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static Pin ExecPin(string name, string dir) =>
        new() { Id = Guid.NewGuid(), Name = name, Direction = dir, IsExec = true, TypeRef = new() };

    private static Pin DataPin(string name, string dir, string typeId, bool isArray = false) =>
        new() { Id = Guid.NewGuid(), Name = name, Direction = dir, IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = typeId, IsArray = isArray } };

    /// <summary>Asset with list var "MyList" (int × 4, initial 2) + int var "Out"; graph wires GetVariable(MyList) into the given consumer, consumer's <paramref name="resultPinName"/> into SetVariable(Out).</summary>
    private static BlueprintAsset BuildReadAsset(Node consumer, Pin collectionIn, Pin resultOut, string resultTypeId, IEnumerable<(Node node, Pin from, Pin to)>? extraDataWires = null)
    {
        var asset = BlueprintAssetBuilder.Instance("ListReadBp")
            .WithVariable("MyList", typeof(int), "0")
            .WithVariable("Out", typeof(int), "0")
            .Build();
        asset.Variables[0].Type = new BlueprintTypeRef { TypeId = "System.Int32", Capacity = 4, InitialLength = 2 };
        var listVarId = asset.Variables[0].Id;
        var outVarId  = asset.Variables[1].Id;

        var gvOut = DataPin("Value", "Out", "System.Int32", isArray: true);
        var gv = new GetVariableNode { Id = Guid.NewGuid(), VariableId = listVarId.ToString() };
        gv.Pins.Add(gvOut);

        var entryOut = ExecPin("Out", "Out");
        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        entry.Pins.Add(entryOut);

        var svIn  = ExecPin("In", "In");
        var svOut = ExecPin("Out", "Out");
        var svVal = DataPin("Value", "In", resultTypeId);
        var setVar = new SetVariableNode { Id = Guid.NewGuid(), VariableId = outVarId.ToString() };
        setVar.Pins.AddRange(new[] { svIn, svOut, svVal });

        var retIn = ExecPin("In", "In");
        var ret = new ReturnNode { Id = Guid.NewGuid() };
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "Main", Kind = GraphKind.Function,
            Nodes = { entry, gv, consumer, setVar, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,    FromPinId = entryOut.Id, ToNodeId = setVar.Id,   ToPinId = svIn.Id },
                new Link { FromNodeId = setVar.Id,   FromPinId = svOut.Id,    ToNodeId = ret.Id,      ToPinId = retIn.Id },
                new Link { FromNodeId = gv.Id,       FromPinId = gvOut.Id,    ToNodeId = consumer.Id, ToPinId = collectionIn.Id },
                new Link { FromNodeId = consumer.Id, FromPinId = resultOut.Id, ToNodeId = setVar.Id,  ToPinId = svVal.Id },
            },
        };
        if (extraDataWires != null)
            foreach (var (node, from, to) in extraDataWires)
            {
                graph.Nodes.Add(node);
                graph.Links.Add(new Link { FromNodeId = node.Id, FromPinId = from.Id, ToNodeId = consumer.Id, ToPinId = to.Id });
            }
        asset.Graphs.Add(graph);
        return asset;
    }

    private static string Compile(BlueprintAsset asset)
    {
        var result = new BlueprintCompiler().Compile(asset, Options());
        Assert.True(result.Succeeded,
            "Compile failed: " + string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));
        return result.GeneratedSource!;
    }

    // ---- lowering/emit ------------------------------------------------------

    [Fact]
    public void ItemCount_OverListVariable_RefBindsStateField_ClampedCount_NoComponentRead()
    {
        var collectionIn = DataPin("Collection", "In", "System.Object", isArray: true);
        var countOut     = DataPin("Count", "Out", "System.Int32");
        var count = new ComponentItemCountNode
        {
            Id = Guid.NewGuid(),
            // The editor wire-bake state (TryBakeCollectionConsumer's GetVariable branch).
            CollectionKind = CollectionKind.BlackboardFixedList,
            CollectionFieldName = "MyList",
        };
        count.Pins.AddRange(new[] { collectionIn, countOut });

        var src = Compile(BuildReadAsset(count, collectionIn, countOut, "System.Int32"));

        Assert.Contains("ref var __t", src);
        Assert.Contains("= ref s.MyList;", src);
        Assert.Matches(@"global::System\.Math\.Min\(__t\d+\.Count, 4\)", src);
        Assert.DoesNotContain("GetComponentRO", src);
        Assert.DoesNotContain("HasComponent", src);
    }

    [Fact]
    public void ItemGet_OverListVariable_GuardedNeverThrowElementRead()
    {
        var collectionIn = DataPin("Collection", "In", "System.Int32", isArray: true);
        var indexIn      = DataPin("Index", "In", "System.Int32");
        var elementOut   = DataPin("Element", "Out", "System.Int32");
        var get = new ComponentItemGetNode
        {
            Id = Guid.NewGuid(),
            CollectionKind = CollectionKind.BlackboardFixedList,
            CollectionFieldName = "MyList",
            ElementTypeFqn = "System.Int32",
        };
        get.Pins.AddRange(new[] { collectionIn, indexIn, elementOut });

        var idxOut = DataPin("Value", "Out", "System.Int32");
        var idxLit = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = "1" };
        idxLit.Pins.Add(idxOut);

        var src = Compile(BuildReadAsset(get, collectionIn, elementOut, "System.Int32",
            new[] { ((Node)idxLit, idxOut, indexIn) }));

        Assert.Contains("= ref s.MyList;", src);
        Assert.Matches(@"\(uint\)__t\d+ < \(uint\)global::System\.Math\.Min\(__t\d+\.Count, 4\) \? __t\d+\.Items\[__t\d+\] : default\(int\)", src);
    }

    [Fact]
    public void Contains_OverListVariable_SearchLoopWithClampedBound()
    {
        var collectionIn = DataPin("Collection", "In", "System.Int32", isArray: true);
        var itemIn       = DataPin("Item", "In", "System.Int32");
        var resultOut    = DataPin("Result", "Out", "System.Boolean");
        var contains = new ComponentContainsNode
        {
            Id = Guid.NewGuid(),
            CollectionKind = CollectionKind.BlackboardFixedList,
            CollectionFieldName = "MyList",
            ElementTypeFqn = "System.Int32",
        };
        contains.Pins.AddRange(new[] { collectionIn, itemIn, resultOut });

        var qOut = DataPin("Value", "Out", "System.Int32");
        var qLit = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = "7" };
        qLit.Pins.Add(qOut);

        var asset = BuildReadAsset(contains, collectionIn, resultOut, "System.Boolean",
            new[] { ((Node)qLit, qOut, itemIn) });
        asset.Variables[1].Type = new BlueprintTypeRef { TypeId = "System.Boolean" };

        var src = Compile(asset);
        Assert.Contains("= ref s.MyList;", src);
        Assert.Matches(@"__csN = global::System\.Math\.Min\(__t\d+\.Count, 4\)", src);
        Assert.Contains("EqualityComparer<global::System.Int32>.Default", src);
        Assert.DoesNotContain("GetComponentRO", src);
    }

    [Fact]
    public void ForEach_OverListVariable_SnapshottedBound_CompilesThroughRoslyn()
    {
        // ForEach is an EXEC node -- full asset with a body, compiled through real Roslyn+ALC to
        // prove the whole emit (ref-bind + __feb snapshot bound + clamped element reads) compiles.
        var asset = BlueprintAssetBuilder.Instance("ListForEachBp")
            .WithVariable("MyList", typeof(int), "0")
            .WithVariable("Sum", typeof(int), "0")
            .Build();
        asset.Variables[0].Type = new BlueprintTypeRef { TypeId = "System.Int32", Capacity = 4, InitialLength = 3 };
        var listVarId = asset.Variables[0].Id;
        var sumVarId  = asset.Variables[1].Id;

        var gvOut = DataPin("Value", "Out", "System.Int32", isArray: true);
        var gv = new GetVariableNode { Id = Guid.NewGuid(), VariableId = listVarId.ToString() };
        gv.Pins.Add(gvOut);

        var feIn    = ExecPin("In", "In");
        var feColl  = DataPin("Collection", "In", "System.Int32", isArray: true);
        var feBody  = ExecPin("Body", "Out");
        var feDone  = ExecPin("Completed", "Out");
        var feItem  = DataPin("CurrentItem", "Out", "System.Int32");
        var forEach = new ComponentForEachNode
        {
            Id = Guid.NewGuid(),
            CollectionKind = CollectionKind.BlackboardFixedList,
            CollectionFieldName = "MyList",
            ElementTypeFqn = "System.Int32",
        };
        forEach.Pins.AddRange(new[] { feIn, feColl, feBody, feDone, feItem });

        var svIn  = ExecPin("In", "In");
        var svOut = ExecPin("Out", "Out");
        var svVal = DataPin("Value", "In", "System.Int32");
        var setSum = new SetVariableNode { Id = Guid.NewGuid(), VariableId = sumVarId.ToString() };
        setSum.Pins.AddRange(new[] { svIn, svOut, svVal });

        var entryOut = ExecPin("Out", "Out");
        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        entry.Pins.Add(entryOut);
        var retIn = ExecPin("In", "In");
        var ret = new ReturnNode { Id = Guid.NewGuid() };
        ret.Pins.Add(retIn);

        asset.Graphs.Add(new Graph
        {
            Id = Guid.NewGuid(), Name = "Main", Kind = GraphKind.Function,
            Nodes = { entry, gv, forEach, setSum, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,   FromPinId = entryOut.Id, ToNodeId = forEach.Id, ToPinId = feIn.Id },
                new Link { FromNodeId = gv.Id,      FromPinId = gvOut.Id,    ToNodeId = forEach.Id, ToPinId = feColl.Id },
                new Link { FromNodeId = forEach.Id, FromPinId = feBody.Id,   ToNodeId = setSum.Id,  ToPinId = svIn.Id },
                new Link { FromNodeId = forEach.Id, FromPinId = feItem.Id,   ToNodeId = setSum.Id,  ToPinId = svVal.Id },
                new Link { FromNodeId = forEach.Id, FromPinId = feDone.Id,   ToNodeId = ret.Id,     ToPinId = retIn.Id },
            },
        });

        var result = new BlueprintCompiler().Compile(asset, Options());
        Assert.True(result.Succeeded,
            "Compile failed: " + string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));
        var src = result.GeneratedSource!;
        Assert.Contains("= ref s.MyList;", src);
        Assert.Matches(@"var __feb\d+ = global::System\.Math\.Min\(__t\d+\.Count, 4\);", src);   // snapshotted bound
        Assert.DoesNotContain("GetComponentRO", src);

        // Real Roslyn+ALC: the emitted ref-bind + inline-array reads must actually compile.
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        Assert.NotNull(fixture.CompileAndLoad(asset));
    }

    // ---- editor: pin projection parity + wire-bake + BP2066 -----------------

    [Fact]
    public void GetVariable_ListVariable_ProjectsCollectionOutPin_ParityWithStage0()
    {
        var asset = BlueprintAssetBuilder.Instance("P")
            .WithVariable("MyList", typeof(int), "0").Build();
        asset.Variables[0].Type = new BlueprintTypeRef { TypeId = "System.Int32", Capacity = 4 };
        var gv = new GetVariableNode { Id = Guid.NewGuid(), VariableId = asset.Variables[0].Id.ToString() };

        var editorPins = NodePinSchema.GetCanonicalPins(gv, asset: asset)
            .Select(p => (p.Name, p.Direction, p.TypeRef?.TypeId, p.TypeRef?.IsArray ?? false)).ToList();
        Assert.Equal(new[] { ("Value", "Out", (string?)"System.Int32", true) }, editorPins);

        var graph = new Graph { Id = Guid.NewGuid(), Name = "G", Kind = GraphKind.Function, Nodes = { gv } };
        asset.Graphs.Add(graph);
        Stage0_Rehydrate.Run(asset, Options());
        var stage0Pins = gv.Pins
            .Select(p => (p.Name, p.Direction, p.TypeRef?.TypeId, p.TypeRef?.IsArray ?? false)).ToList();
        Assert.Equal(editorPins, stage0Pins);
    }

    [Fact]
    [Compiler.CoversDiagnosticCode("BP2066")]
    public void BP2066_ListKind_RequiresVariableName_NotComponentFqn()
    {
        var asset = BlueprintAssetBuilder.Instance("V")
            .WithGraph("Main", g => g.Entry().Return()).Build();
        var graph = asset.Graphs[0];

        var gvOut = DataPin("Value", "Out", "System.Int32", isArray: true);
        var gv = new GetVariableNode { Id = Guid.NewGuid(), VariableId = Guid.NewGuid().ToString() };
        gv.Pins.Add(gvOut);
        var collectionIn = DataPin("Collection", "In", "System.Object", isArray: true);
        var count = new ComponentItemCountNode
        {
            Id = Guid.NewGuid(),
            CollectionKind = CollectionKind.BlackboardFixedList,   // list kind, but NO variable name baked
        };
        count.Pins.Add(collectionIn);
        graph.Nodes.Add(gv);
        graph.Nodes.Add(count);
        graph.Links.Add(new Link { FromNodeId = gv.Id, FromPinId = gvOut.Id, ToNodeId = count.Id, ToPinId = collectionIn.Id });

        var sink = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink, Options()));
        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP2066);

        count.CollectionFieldName = "MyList";                      // baked -> clean
        var sink2 = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink2, Options()));
        Assert.DoesNotContain(sink2.All, d => d.Code == DiagnosticCodes.BP2066);
    }
}
