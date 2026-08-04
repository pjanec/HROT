using System.Text.RegularExpressions;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Xunit;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// CA-07d-1 — lowering coverage for the two component-collection SEARCH nodes
/// (<see cref="CollectionContainsNode"/> / <see cref="CollectionFindNode"/>). Each wires a
/// <c>GetComponent&lt;BpCollectionDemo&gt;</c> "Values" collection out-pin into the consumer + a
/// Literal query, and asserts the emitted bounded search loop uses the SAME baked Count/Item accessors
/// as ForEach plus <c>EqualityComparer&lt;T&gt;.Default.Equals</c> (Q#18-A). Same ValidateOnlyStage1To7
/// / verbatim-C# style as <see cref="ComponentCollectionConsumerLoweringTests"/>.
/// </summary>
public sealed class ComponentSearchLoweringTests
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
                    Name = "Values", TypeId = "", IsCollection = true, ElementTypeId = "System.Int32",
                    CountAccessorFqn = CountFqn, ItemAccessorFqn = ItemFqn,
                },
            },
        };
        node.Pins.AddRange(new[] { valuesOut, foundOut });
        return (node, valuesOut);
    }

    // ── ComponentContains ─────────────────────────────────────────────────────

    [Fact]
    public void ComponentContains_Lowering_EmitsSearchLoop_WithEqualityComparer()
    {
        var (getNode, valuesOut) = BuildGetComponentCollectionNode();

        var collectionIn = DataPin("Collection", "In",  "System.Int32", isArray: true);
        var itemIn       = DataPin("Item",       "In",  "System.Int32");
        var resultOut    = DataPin("Result",     "Out", "System.Boolean");
        var contains = new CollectionContainsNode
        {
            Id = Guid.NewGuid(), ComponentTypeFqn = ComponentFqn,
            CountAccessorFqn = CountFqn, ItemAccessorFqn = ItemFqn, ElementTypeFqn = "System.Int32",
        };
        contains.Pins.AddRange(new[] { collectionIn, itemIn, resultOut });

        var litOut  = DataPin("Value", "Out", "System.Int32");
        var litNode = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = "42" };
        litNode.Pins.Add(litOut);

        var boolVarId = Guid.NewGuid();
        var boolVar = new VariableDecl { Id = boolVarId, Name = "HasIt", Type = new BlueprintTypeRef { TypeId = "System.Boolean" } };

        var setExecIn  = ExecPin("ExecIn",  "In");
        var setExecOut = ExecPin("ExecOut", "Out");
        var setValueIn = DataPin("Value", "In", "System.Boolean");
        var setNode = new SetVariableNode { Id = Guid.NewGuid(), VariableId = boolVarId.ToString() };
        setNode.Pins.AddRange(new[] { setExecIn, setExecOut, setValueIn });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "Main", Kind = GraphKind.Function,
            Nodes = { entry, getNode, litNode, contains, setNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,    FromPinId = entryOut.Id,   ToNodeId = setNode.Id,  ToPinId = setExecIn.Id },
                new Link { FromNodeId = setNode.Id,  FromPinId = setExecOut.Id, ToNodeId = ret.Id,      ToPinId = retIn.Id },
                new Link { FromNodeId = getNode.Id,  FromPinId = valuesOut.Id,  ToNodeId = contains.Id, ToPinId = collectionIn.Id },
                new Link { FromNodeId = litNode.Id,  FromPinId = litOut.Id,     ToNodeId = contains.Id, ToPinId = itemIn.Id },
                new Link { FromNodeId = contains.Id, FromPinId = resultOut.Id,  ToNodeId = setNode.Id,  ToPinId = setValueIn.Id },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId = Guid.NewGuid(), Name = "ComponentContainsCoverage",
            Dispatch = BlueprintDispatchKind.Instance, Variables = { boolVar }, Graphs = { graph },
        };

        var result = new BlueprintCompiler().Compile(asset, DefaultCompileOptions());
        Assert.True(result.Succeeded,
            "Compile failed: " + string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));
        var src = result.GeneratedSource!;

        // Re-reads the component off the resolved entity, then a bounded search loop over the SAME
        // Count/Item accessors, comparing with EqualityComparer<int>.Default.
        Assert.Contains($"GetComponentRO<global::{ComponentFqn}>", src);
        Assert.Matches(new Regex(@"for \(int __csI = 0, __csN = " + Regex.Escape($"global::{CountFqn}(") + @"__t\d+\);"), src);
        Assert.Contains("global::System.Collections.Generic.EqualityComparer<global::System.Int32>.Default.Equals(", src);
        Assert.Matches(new Regex(Regex.Escape($"global::{ItemFqn}(") + @"__t\d+, __csI\)"), src);
    }

    [Fact]
    public void ComponentContains_UnwiredCollection_CompilesToSafeDefault_NoLoop()
    {
        var collectionIn = DataPin("Collection", "In",  "System.Object", isArray: true);
        var itemIn       = DataPin("Item",       "In",  "System.Object");
        var resultOut    = DataPin("Result",     "Out", "System.Boolean");
        var contains = new CollectionContainsNode
        {
            Id = Guid.NewGuid(), ComponentTypeFqn = ComponentFqn,
            CountAccessorFqn = CountFqn, ItemAccessorFqn = ItemFqn, ElementTypeFqn = "System.Int32",
        };
        contains.Pins.AddRange(new[] { collectionIn, itemIn, resultOut });

        var boolVarId = Guid.NewGuid();
        var boolVar = new VariableDecl { Id = boolVarId, Name = "HasIt", Type = new BlueprintTypeRef { TypeId = "System.Boolean" } };

        var setExecIn  = ExecPin("ExecIn",  "In");
        var setExecOut = ExecPin("ExecOut", "Out");
        var setValueIn = DataPin("Value", "In", "System.Boolean");
        var setNode = new SetVariableNode { Id = Guid.NewGuid(), VariableId = boolVarId.ToString() };
        setNode.Pins.AddRange(new[] { setExecIn, setExecOut, setValueIn });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "Main", Kind = GraphKind.Function,
            Nodes = { entry, contains, setNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,    FromPinId = entryOut.Id,   ToNodeId = setNode.Id, ToPinId = setExecIn.Id },
                new Link { FromNodeId = setNode.Id,  FromPinId = setExecOut.Id, ToNodeId = ret.Id,     ToPinId = retIn.Id },
                new Link { FromNodeId = contains.Id, FromPinId = resultOut.Id,  ToNodeId = setNode.Id, ToPinId = setValueIn.Id },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId = Guid.NewGuid(), Name = "ComponentContainsUnwiredCoverage",
            Dispatch = BlueprintDispatchKind.Instance, Variables = { boolVar }, Graphs = { graph },
        };

        var result = new BlueprintCompiler().Compile(asset, DefaultCompileOptions());
        Assert.True(result.Succeeded,
            "Compile failed: " + string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));
        var src = result.GeneratedSource!;
        Assert.DoesNotContain("__csI", src);
        Assert.DoesNotContain($"GetComponentRO<global::{ComponentFqn}>", src);
    }

    // ── ComponentFind ─────────────────────────────────────────────────────────

    [Fact]
    public void ComponentFind_Lowering_EmitsSearchLoop_IndexAndFound()
    {
        var (getNode, valuesOut) = BuildGetComponentCollectionNode();

        var collectionIn = DataPin("Collection", "In",  "System.Int32", isArray: true);
        var itemIn       = DataPin("Item",       "In",  "System.Int32");
        var indexOut     = DataPin("Index",      "Out", "System.Int32");
        var foundOut     = DataPin("Found",      "Out", "System.Boolean");
        var find = new CollectionFindNode
        {
            Id = Guid.NewGuid(), ComponentTypeFqn = ComponentFqn,
            CountAccessorFqn = CountFqn, ItemAccessorFqn = ItemFqn, ElementTypeFqn = "System.Int32",
        };
        find.Pins.AddRange(new[] { collectionIn, itemIn, indexOut, foundOut });

        var litOut  = DataPin("Value", "Out", "System.Int32");
        var litNode = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = "7" };
        litNode.Pins.Add(litOut);

        var idxVarId  = Guid.NewGuid();
        var idxVar  = new VariableDecl { Id = idxVarId,  Name = "FoundIndex", Type = new BlueprintTypeRef { TypeId = "System.Int32" } };
        var fndVarId  = Guid.NewGuid();
        var fndVar  = new VariableDecl { Id = fndVarId,  Name = "WasFound",  Type = new BlueprintTypeRef { TypeId = "System.Boolean" } };

        var setIdxIn   = ExecPin("ExecIn",  "In");
        var setIdxOut  = ExecPin("ExecOut", "Out");
        var setIdxVal  = DataPin("Value", "In", "System.Int32");
        var setIdxNode = new SetVariableNode { Id = Guid.NewGuid(), VariableId = idxVarId.ToString() };
        setIdxNode.Pins.AddRange(new[] { setIdxIn, setIdxOut, setIdxVal });

        var setFndIn   = ExecPin("ExecIn",  "In");
        var setFndOut  = ExecPin("ExecOut", "Out");
        var setFndVal  = DataPin("Value", "In", "System.Boolean");
        var setFndNode = new SetVariableNode { Id = Guid.NewGuid(), VariableId = fndVarId.ToString() };
        setFndNode.Pins.AddRange(new[] { setFndIn, setFndOut, setFndVal });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "Main", Kind = GraphKind.Function,
            Nodes = { entry, getNode, litNode, find, setIdxNode, setFndNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,      FromPinId = entryOut.Id,   ToNodeId = setIdxNode.Id, ToPinId = setIdxIn.Id },
                new Link { FromNodeId = setIdxNode.Id, FromPinId = setIdxOut.Id,  ToNodeId = setFndNode.Id, ToPinId = setFndIn.Id },
                new Link { FromNodeId = setFndNode.Id, FromPinId = setFndOut.Id,  ToNodeId = ret.Id,        ToPinId = retIn.Id },
                new Link { FromNodeId = getNode.Id,    FromPinId = valuesOut.Id,  ToNodeId = find.Id,       ToPinId = collectionIn.Id },
                new Link { FromNodeId = litNode.Id,    FromPinId = litOut.Id,     ToNodeId = find.Id,       ToPinId = itemIn.Id },
                new Link { FromNodeId = find.Id,       FromPinId = indexOut.Id,   ToNodeId = setIdxNode.Id, ToPinId = setIdxVal.Id },
                new Link { FromNodeId = find.Id,       FromPinId = foundOut.Id,   ToNodeId = setFndNode.Id, ToPinId = setFndVal.Id },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId = Guid.NewGuid(), Name = "ComponentFindCoverage",
            Dispatch = BlueprintDispatchKind.Instance, Variables = { idxVar, fndVar }, Graphs = { graph },
        };

        var result = new BlueprintCompiler().Compile(asset, DefaultCompileOptions());
        Assert.True(result.Succeeded,
            "Compile failed: " + string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));
        var src = result.GeneratedSource!;

        // Both results declared (index seeded -1, found seeded false), one shared search loop.
        Assert.Contains("= -1;", src);
        Assert.Matches(new Regex(@"for \(int __csI = 0, __csN = " + Regex.Escape($"global::{CountFqn}(") + @"__t\d+\);"), src);
        Assert.Contains("global::System.Collections.Generic.EqualityComparer<global::System.Int32>.Default.Equals(", src);
        // Only ONE search loop even though both out-pins are consumed (multi-out cached).
        Assert.Single(Regex.Matches(src, "for \\(int __csI = 0"));
    }

    // ── Stage2 validator (BP2066) covers the search nodes too ─────────────────

    [Fact]
    public void ComponentFind_WiredCollection_EmptyBakedAccessors_ReportsBP2066()
    {
        var collectionIn = DataPin("Collection", "In",  "System.Object", isArray: true);
        var itemIn       = DataPin("Item",       "In",  "System.Object");
        var indexOut     = DataPin("Index",      "Out", "System.Int32");
        var foundOut     = DataPin("Found",      "Out", "System.Boolean");
        // ComponentTypeFqn baked but accessor FQNs empty -> invalid once Collection is wired.
        var find = new CollectionFindNode { Id = Guid.NewGuid(), ComponentTypeFqn = ComponentFqn };
        find.Pins.AddRange(new[] { collectionIn, itemIn, indexOut, foundOut });

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
            Id = Guid.NewGuid(), Name = "Main", Kind = GraphKind.Function,
            Nodes = { entry, litNode, find, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,   FromPinId = entryOut.Id,     ToNodeId = ret.Id,  ToPinId = retIn.Id },
                new Link { FromNodeId = litNode.Id, FromPinId = litValueOut.Id,  ToNodeId = find.Id, ToPinId = collectionIn.Id },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId = Guid.NewGuid(), Name = "ComponentFindBP2066Coverage",
            Dispatch = BlueprintDispatchKind.Instance, Graphs = { graph },
        };

        var result = new BlueprintCompiler().Compile(asset, DefaultCompileOptions());
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.BP2066 && d.Severity == DiagnosticSeverity.Error);
    }
}
