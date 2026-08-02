using System.Text.RegularExpressions;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Xunit;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// CA-07d-2 — lowering coverage for MANAGED component collections (Q#18-C/D). A managed (class)
/// component (<c>Hrot.AI.Behaviors.BpManagedCollectionDemo</c>) exposes a plain <c>List&lt;int&gt;</c>
/// member ("MemberIds") as a collection out-pin with NO curated accessor pair -- the five consumers
/// (ForEach / ItemGet / ItemCount / Contains / Find) all work off it unchanged, but the compiler emits
/// NATIVE member access via an <c>IReadOnlyList&lt;int&gt;</c> null-safe local instead of
/// <c>global::…Ops.Count/Item(comp[,i])</c> curated calls (<see cref="CollectionKind.ManagedMember"/>).
/// <para>
/// Mirrors <see cref="ComponentCollectionConsumerLoweringTests"/> / <see cref="ComponentSearchLoweringTests"/>
/// (same real Stage1-7 <c>Compile</c> + verbatim-C# assertions). The compiler is reflection-free
/// ("trust the string"), so the emitted shape is exercised without Roslyn-compiling against the real
/// managed type; the demo component exists so the EDITOR reflector has a concrete managed collection to
/// discover. The component is read via <c>GetManagedComponentRO</c> (never the unmanaged
/// <c>GetComponentRO</c>) both by GetComponent's own multi-pin projection (IsManaged = true) and by each
/// consumer's re-read off the resolved entity.
/// </para>
/// </summary>
public sealed class ComponentManagedCollectionLoweringTests
{
    private const string ComponentFqn = "Hrot.AI.Behaviors.BpManagedCollectionDemo";
    private const string FieldName    = "MemberIds";
    private const string ElementFqn   = "System.Int32";

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
    /// A MANAGED <c>GetComponent&lt;BpManagedCollectionDemo&gt;</c> (IsManaged = true) with a single
    /// managed-collection decl ("Members", <see cref="CollectionKind.ManagedMember"/>, field "MemberIds",
    /// element System.Int32 -- NO accessor FQNs). Out-pins: "Members" (Out, IsArray) + "Found" (Out, bool).
    /// </summary>
    private static (GetComponentNode Node, Pin MembersOut) BuildManagedGetComponentCollectionNode()
    {
        var membersOut = DataPin("Members", "Out", ElementFqn, isArray: true);
        var foundOut   = DataPin("Found",   "Out", "System.Boolean");
        var node = new GetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = ComponentFqn,
            IsManaged        = true,
            Fields = new List<ComponentFieldDecl>
            {
                new()
                {
                    Name                = "Members",
                    TypeId              = "",
                    IsCollection        = true,
                    ElementTypeId       = ElementFqn,
                    CollectionKind      = CollectionKind.ManagedMember,
                    CollectionFieldName = FieldName,
                },
            },
        };
        node.Pins.AddRange(new[] { membersOut, foundOut });
        return (node, membersOut);
    }

    private static BlueprintAsset Wrap(string name, Graph graph, params VariableDecl[] vars)
    {
        var asset = new BlueprintAsset
        {
            AssetId = Guid.NewGuid(), Name = name,
            Dispatch = BlueprintDispatchKind.Instance, Graphs = { graph },
        };
        foreach (var v in vars) asset.Variables.Add(v);
        return asset;
    }

    private static string CompileOk(BlueprintAsset asset)
    {
        var result = new BlueprintCompiler().Compile(asset, DefaultCompileOptions());
        Assert.True(result.Succeeded,
            "Compile failed: " + string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));
        return result.GeneratedSource!;
    }

    // The __ml local is typed IReadOnlyList<int> so a List<int>/IReadOnlyList<int>/int[] field all
    // expose .Count/[i] uniformly; resolved once, null-safe, off the (nullable) managed component.
    private static readonly Regex MlDecl = new(
        Regex.Escape($"global::System.Collections.Generic.IReadOnlyList<global::{ElementFqn}> __ml") + @"\d+ = __t\d+\?\." + FieldName + ";");

    // ── ComponentItemCount ────────────────────────────────────────────────────

    [Fact]
    public void ManagedItemCount_EmitsNativeCount_NoCuratedAccessor()
    {
        var (getNode, membersOut) = BuildManagedGetComponentCollectionNode();

        var collectionIn = DataPin("Collection", "In",  ElementFqn, isArray: true);
        var countOut     = DataPin("Count",      "Out", "System.Int32");
        var countNode = new ComponentItemCountNode
        {
            Id = Guid.NewGuid(), ComponentTypeFqn = ComponentFqn,
            CollectionKind = CollectionKind.ManagedMember, CollectionFieldName = FieldName, ElementTypeFqn = ElementFqn,
        };
        countNode.Pins.AddRange(new[] { collectionIn, countOut });

        var vId = Guid.NewGuid();
        var v = new VariableDecl { Id = vId, Name = "CountOut", Type = new BlueprintTypeRef { TypeId = "System.Int32" } };
        var setIn = ExecPin("ExecIn", "In"); var setOut = ExecPin("ExecOut", "Out"); var setVal = DataPin("Value", "In", "System.Int32");
        var setNode = new SetVariableNode { Id = Guid.NewGuid(), VariableId = vId.ToString() };
        setNode.Pins.AddRange(new[] { setIn, setOut, setVal });
        var entry = new EventEntryNode { Id = Guid.NewGuid() }; var entryOut = ExecPin("ExecOut", "Out"); entry.Pins.Add(entryOut);
        var ret = new ReturnNode { Id = Guid.NewGuid() }; var retIn = ExecPin("ExecIn", "In"); ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "Main", Kind = GraphKind.Function,
            Nodes = { entry, getNode, countNode, setNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,     FromPinId = entryOut.Id,   ToNodeId = setNode.Id,   ToPinId = setIn.Id },
                new Link { FromNodeId = setNode.Id,   FromPinId = setOut.Id,     ToNodeId = ret.Id,       ToPinId = retIn.Id },
                new Link { FromNodeId = getNode.Id,   FromPinId = membersOut.Id, ToNodeId = countNode.Id, ToPinId = collectionIn.Id },
                new Link { FromNodeId = countNode.Id, FromPinId = countOut.Id,   ToNodeId = setNode.Id,   ToPinId = setVal.Id },
            },
        };

        var src = CompileOk(Wrap("ManagedItemCountCoverage", graph, v));

        // Managed read path (never the unmanaged GetComponentRO for this component).
        Assert.Contains($"GetManagedComponentRO<global::{ComponentFqn}>", src);
        Assert.DoesNotContain($"GetComponentRO<global::{ComponentFqn}>", src);
        // Native member access via the IReadOnlyList<int> local -> (__ml?.Count ?? 0); no curated call.
        Assert.Matches(MlDecl, src);
        Assert.Matches(new Regex(@"= \(__ml\d+\?\.Count \?\? 0\);"), src);
        Assert.DoesNotContain("Ops.Count(", src);
    }

    // ── ComponentItemGet ──────────────────────────────────────────────────────

    [Fact]
    public void ManagedItemGet_EmitsNullAndBoundsGuardedIndexer_NoCuratedAccessor()
    {
        var (getNode, membersOut) = BuildManagedGetComponentCollectionNode();

        var collectionIn = DataPin("Collection", "In",  ElementFqn, isArray: true);
        var indexIn      = DataPin("Index",      "In",  "System.Int32");
        var elementOut   = DataPin("Element",    "Out", ElementFqn);
        var getItem = new ComponentItemGetNode
        {
            Id = Guid.NewGuid(), ComponentTypeFqn = ComponentFqn, ElementTypeFqn = ElementFqn,
            CollectionKind = CollectionKind.ManagedMember, CollectionFieldName = FieldName,
        };
        getItem.Pins.AddRange(new[] { collectionIn, indexIn, elementOut });

        var litOut = DataPin("Value", "Out", "System.Int32");
        var lit = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = "0" };
        lit.Pins.Add(litOut);

        var vId = Guid.NewGuid();
        var v = new VariableDecl { Id = vId, Name = "ElementOut", Type = new BlueprintTypeRef { TypeId = "System.Int32" } };
        var setIn = ExecPin("ExecIn", "In"); var setOut = ExecPin("ExecOut", "Out"); var setVal = DataPin("Value", "In", "System.Int32");
        var setNode = new SetVariableNode { Id = Guid.NewGuid(), VariableId = vId.ToString() };
        setNode.Pins.AddRange(new[] { setIn, setOut, setVal });
        var entry = new EventEntryNode { Id = Guid.NewGuid() }; var entryOut = ExecPin("ExecOut", "Out"); entry.Pins.Add(entryOut);
        var ret = new ReturnNode { Id = Guid.NewGuid() }; var retIn = ExecPin("ExecIn", "In"); ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "Main", Kind = GraphKind.Function,
            Nodes = { entry, getNode, lit, getItem, setNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,    FromPinId = entryOut.Id,   ToNodeId = setNode.Id,  ToPinId = setIn.Id },
                new Link { FromNodeId = setNode.Id,  FromPinId = setOut.Id,     ToNodeId = ret.Id,      ToPinId = retIn.Id },
                new Link { FromNodeId = getNode.Id,  FromPinId = membersOut.Id, ToNodeId = getItem.Id,  ToPinId = collectionIn.Id },
                new Link { FromNodeId = lit.Id,      FromPinId = litOut.Id,     ToNodeId = getItem.Id,  ToPinId = indexIn.Id },
                new Link { FromNodeId = getItem.Id,  FromPinId = elementOut.Id, ToNodeId = setNode.Id,  ToPinId = setVal.Id },
            },
        };

        var src = CompileOk(Wrap("ManagedItemGetCoverage", graph, v));

        Assert.Contains($"GetManagedComponentRO<global::{ComponentFqn}>", src);
        Assert.Matches(MlDecl, src);
        // Standalone read -> null + (uint)-bounds guarded indexer, degrades to default (never throws).
        Assert.Matches(new Regex(@"__ml\d+ != null && \(uint\)__t\d+ < \(uint\)__ml\d+\.Count\) \? __ml\d+\[__t\d+\] : default;"), src);
        Assert.DoesNotContain("Ops.Item(", src);
    }

    // ── ComponentForEach ──────────────────────────────────────────────────────

    [Fact]
    public void ManagedForEach_EmitsNativeLoop_NoCuratedAccessor()
    {
        var (getNode, membersOut) = BuildManagedGetComponentCollectionNode();

        var feIn        = ExecPin("In", "In");
        var feCollection = DataPin("Collection", "In", ElementFqn, isArray: true);
        var feBody      = ExecPin("Body", "Out");
        var feCompleted = ExecPin("Completed", "Out");
        var feItem      = DataPin("CurrentItem", "Out", ElementFqn);
        var fe = new ComponentForEachNode
        {
            Id = Guid.NewGuid(), ComponentTypeFqn = ComponentFqn, ElementTypeFqn = ElementFqn,
            CollectionKind = CollectionKind.ManagedMember, CollectionFieldName = FieldName,
        };
        fe.Pins.AddRange(new[] { feIn, feCollection, feBody, feCompleted, feItem });

        var vId = Guid.NewGuid();
        var v = new VariableDecl { Id = vId, Name = "ItemOut", Type = new BlueprintTypeRef { TypeId = "System.Int32" } };
        var setIn = ExecPin("ExecIn", "In"); var setOut = ExecPin("ExecOut", "Out"); var setVal = DataPin("Value", "In", "System.Int32");
        var setNode = new SetVariableNode { Id = Guid.NewGuid(), VariableId = vId.ToString() };
        setNode.Pins.AddRange(new[] { setIn, setOut, setVal });
        var entry = new EventEntryNode { Id = Guid.NewGuid() }; var entryOut = ExecPin("ExecOut", "Out"); entry.Pins.Add(entryOut);
        var ret = new ReturnNode { Id = Guid.NewGuid() }; var retIn = ExecPin("In", "In"); ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "Main", Kind = GraphKind.Function,
            Nodes = { entry, getNode, fe, setNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,   FromPinId = entryOut.Id,   ToNodeId = fe.Id,      ToPinId = feIn.Id },
                new Link { FromNodeId = getNode.Id, FromPinId = membersOut.Id, ToNodeId = fe.Id,      ToPinId = feCollection.Id },
                new Link { FromNodeId = fe.Id,      FromPinId = feBody.Id,     ToNodeId = setNode.Id, ToPinId = setIn.Id },
                new Link { FromNodeId = fe.Id,      FromPinId = feItem.Id,     ToNodeId = setNode.Id, ToPinId = setVal.Id },
                new Link { FromNodeId = fe.Id,      FromPinId = feCompleted.Id, ToNodeId = ret.Id,    ToPinId = retIn.Id },
            },
        };

        var src = CompileOk(Wrap("ManagedForEachCoverage", graph, v));

        Assert.Contains($"GetManagedComponentRO<global::{ComponentFqn}>", src);
        Assert.DoesNotContain($"GetComponentRO<global::{ComponentFqn}>", src);
        Assert.Matches(MlDecl, src);
        // Loop bound = native null-safe count; element = __ml![i]. No curated Count/Item calls.
        Assert.Matches(new Regex(@"for \(int __fe\d+ = 0; __fe\d+ < \(__ml\d+\?\.Count \?\? 0\); __fe\d+\+\+\)"), src);
        Assert.Matches(new Regex(@"= __ml\d+!\[__fe\d+\];"), src);
        Assert.DoesNotContain("Ops.Item(", src);
        Assert.DoesNotContain("Ops.Count(", src);
    }

    // ── ComponentContains ─────────────────────────────────────────────────────

    [Fact]
    public void ManagedContains_EmitsNativeSearchLoop_WithEqualityComparer()
    {
        var (getNode, membersOut) = BuildManagedGetComponentCollectionNode();

        var collectionIn = DataPin("Collection", "In",  ElementFqn, isArray: true);
        var itemIn       = DataPin("Item",       "In",  ElementFqn);
        var resultOut    = DataPin("Result",     "Out", "System.Boolean");
        var contains = new ComponentContainsNode
        {
            Id = Guid.NewGuid(), ComponentTypeFqn = ComponentFqn, ElementTypeFqn = ElementFqn,
            CollectionKind = CollectionKind.ManagedMember, CollectionFieldName = FieldName,
        };
        contains.Pins.AddRange(new[] { collectionIn, itemIn, resultOut });

        var litOut = DataPin("Value", "Out", "System.Int32");
        var lit = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = "42" };
        lit.Pins.Add(litOut);

        var vId = Guid.NewGuid();
        var v = new VariableDecl { Id = vId, Name = "HasIt", Type = new BlueprintTypeRef { TypeId = "System.Boolean" } };
        var setIn = ExecPin("ExecIn", "In"); var setOut = ExecPin("ExecOut", "Out"); var setVal = DataPin("Value", "In", "System.Boolean");
        var setNode = new SetVariableNode { Id = Guid.NewGuid(), VariableId = vId.ToString() };
        setNode.Pins.AddRange(new[] { setIn, setOut, setVal });
        var entry = new EventEntryNode { Id = Guid.NewGuid() }; var entryOut = ExecPin("ExecOut", "Out"); entry.Pins.Add(entryOut);
        var ret = new ReturnNode { Id = Guid.NewGuid() }; var retIn = ExecPin("ExecIn", "In"); ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "Main", Kind = GraphKind.Function,
            Nodes = { entry, getNode, lit, contains, setNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,    FromPinId = entryOut.Id,   ToNodeId = setNode.Id,  ToPinId = setIn.Id },
                new Link { FromNodeId = setNode.Id,  FromPinId = setOut.Id,     ToNodeId = ret.Id,      ToPinId = retIn.Id },
                new Link { FromNodeId = getNode.Id,  FromPinId = membersOut.Id, ToNodeId = contains.Id, ToPinId = collectionIn.Id },
                new Link { FromNodeId = lit.Id,      FromPinId = litOut.Id,     ToNodeId = contains.Id, ToPinId = itemIn.Id },
                new Link { FromNodeId = contains.Id, FromPinId = resultOut.Id,  ToNodeId = setNode.Id,  ToPinId = setVal.Id },
            },
        };

        var src = CompileOk(Wrap("ManagedContainsCoverage", graph, v));

        Assert.Contains($"GetManagedComponentRO<global::{ComponentFqn}>", src);
        Assert.Matches(MlDecl, src);
        Assert.Matches(new Regex(@"for \(int __csI = 0, __csN = \(__ml\d+\?\.Count \?\? 0\); __csI < __csN; __csI\+\+\)"), src);
        Assert.Matches(new Regex(Regex.Escape($"global::System.Collections.Generic.EqualityComparer<global::{ElementFqn}>.Default.Equals(") + @"__ml\d+!\[__csI\], "), src);
        Assert.DoesNotContain("Ops.Item(", src);
        Assert.DoesNotContain("Ops.Count(", src);
    }

    // ── ComponentFind ─────────────────────────────────────────────────────────

    [Fact]
    public void ManagedFind_EmitsNativeSearchLoop_IndexAndFound()
    {
        var (getNode, membersOut) = BuildManagedGetComponentCollectionNode();

        var collectionIn = DataPin("Collection", "In",  ElementFqn, isArray: true);
        var itemIn       = DataPin("Item",       "In",  ElementFqn);
        var indexOut     = DataPin("Index",      "Out", "System.Int32");
        var foundOut     = DataPin("Found",      "Out", "System.Boolean");
        var find = new ComponentFindNode
        {
            Id = Guid.NewGuid(), ComponentTypeFqn = ComponentFqn, ElementTypeFqn = ElementFqn,
            CollectionKind = CollectionKind.ManagedMember, CollectionFieldName = FieldName,
        };
        find.Pins.AddRange(new[] { collectionIn, itemIn, indexOut, foundOut });

        var litOut = DataPin("Value", "Out", "System.Int32");
        var lit = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = "7" };
        lit.Pins.Add(litOut);

        var idxId = Guid.NewGuid();
        var idxVar = new VariableDecl { Id = idxId, Name = "FoundIndex", Type = new BlueprintTypeRef { TypeId = "System.Int32" } };
        var fndId = Guid.NewGuid();
        var fndVar = new VariableDecl { Id = fndId, Name = "WasFound", Type = new BlueprintTypeRef { TypeId = "System.Boolean" } };

        var sIdxIn = ExecPin("ExecIn", "In"); var sIdxOut = ExecPin("ExecOut", "Out"); var sIdxVal = DataPin("Value", "In", "System.Int32");
        var setIdx = new SetVariableNode { Id = Guid.NewGuid(), VariableId = idxId.ToString() };
        setIdx.Pins.AddRange(new[] { sIdxIn, sIdxOut, sIdxVal });
        var sFndIn = ExecPin("ExecIn", "In"); var sFndOut = ExecPin("ExecOut", "Out"); var sFndVal = DataPin("Value", "In", "System.Boolean");
        var setFnd = new SetVariableNode { Id = Guid.NewGuid(), VariableId = fndId.ToString() };
        setFnd.Pins.AddRange(new[] { sFndIn, sFndOut, sFndVal });
        var entry = new EventEntryNode { Id = Guid.NewGuid() }; var entryOut = ExecPin("ExecOut", "Out"); entry.Pins.Add(entryOut);
        var ret = new ReturnNode { Id = Guid.NewGuid() }; var retIn = ExecPin("ExecIn", "In"); ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "Main", Kind = GraphKind.Function,
            Nodes = { entry, getNode, lit, find, setIdx, setFnd, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,   FromPinId = entryOut.Id,   ToNodeId = setIdx.Id, ToPinId = sIdxIn.Id },
                new Link { FromNodeId = setIdx.Id,  FromPinId = sIdxOut.Id,    ToNodeId = setFnd.Id, ToPinId = sFndIn.Id },
                new Link { FromNodeId = setFnd.Id,  FromPinId = sFndOut.Id,    ToNodeId = ret.Id,    ToPinId = retIn.Id },
                new Link { FromNodeId = getNode.Id, FromPinId = membersOut.Id, ToNodeId = find.Id,   ToPinId = collectionIn.Id },
                new Link { FromNodeId = lit.Id,     FromPinId = litOut.Id,     ToNodeId = find.Id,   ToPinId = itemIn.Id },
                new Link { FromNodeId = find.Id,    FromPinId = indexOut.Id,   ToNodeId = setIdx.Id, ToPinId = sIdxVal.Id },
                new Link { FromNodeId = find.Id,    FromPinId = foundOut.Id,   ToNodeId = setFnd.Id, ToPinId = sFndVal.Id },
            },
        };

        var src = CompileOk(Wrap("ManagedFindCoverage", graph, idxVar, fndVar));

        Assert.Contains($"GetManagedComponentRO<global::{ComponentFqn}>", src);
        Assert.Matches(MlDecl, src);
        Assert.Contains("= -1;", src);
        Assert.Matches(new Regex(@"for \(int __csI = 0, __csN = \(__ml\d+\?\.Count \?\? 0\); __csI < __csN; __csI\+\+\)"), src);
        Assert.Matches(new Regex(@"__ml\d+!\[__csI\]"), src);
        // One search loop even though both Index+Found out-pins are consumed (multi-out cached).
        Assert.Single(Regex.Matches(src, "for \\(int __csI = 0"));
        Assert.DoesNotContain("Ops.Item(", src);
    }

    // ── Stage2 validator (BP2066) — managed requires CollectionFieldName ───────

    [Fact]
    [CoversDiagnosticCode("BP2066")]
    public void ManagedContains_WiredCollection_EmptyFieldName_ReportsBP2066()
    {
        var collectionIn = DataPin("Collection", "In",  "System.Object", isArray: true);
        var itemIn       = DataPin("Item",       "In",  "System.Object");
        var resultOut    = DataPin("Result",     "Out", "System.Boolean");
        // ManagedMember but CollectionFieldName left null -> invalid once Collection is wired.
        var contains = new ComponentContainsNode
        {
            Id = Guid.NewGuid(), ComponentTypeFqn = ComponentFqn, CollectionKind = CollectionKind.ManagedMember,
        };
        contains.Pins.AddRange(new[] { collectionIn, itemIn, resultOut });

        var litOut = DataPin("Values", "Out", "System.Int32", isArray: true);
        var lit = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = "0" };
        lit.Pins.Add(litOut);
        var entry = new EventEntryNode { Id = Guid.NewGuid() }; var entryOut = ExecPin("ExecOut", "Out"); entry.Pins.Add(entryOut);
        var ret = new ReturnNode { Id = Guid.NewGuid() }; var retIn = ExecPin("ExecIn", "In"); ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "Main", Kind = GraphKind.Function,
            Nodes = { entry, lit, contains, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id, FromPinId = entryOut.Id, ToNodeId = ret.Id,      ToPinId = retIn.Id },
                new Link { FromNodeId = lit.Id,   FromPinId = litOut.Id,   ToNodeId = contains.Id, ToPinId = collectionIn.Id },
            },
        };

        var result = new BlueprintCompiler().Compile(Wrap("ManagedContainsBP2066Coverage", graph), DefaultCompileOptions());
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.BP2066 && d.Severity == DiagnosticSeverity.Error);
    }
}
