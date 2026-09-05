using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Core.Compiler.Transform;
using BlueprintDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;
using BlueprintTypeRef      = Hrot.Blueprints.Core.Assets.BlueprintTypeRef;
using ParameterDecl         = Hrot.Blueprints.Core.Assets.ParameterDecl;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// BP-74 / Q26 — collapse a selection into a Function or Macro.
///
/// <para>
/// ⭐ <b>The round-trip property is the centrepiece</b>, and the reason is Batch 31: <c>BP1661</c>
/// shipped gated on an inverted condition with the ENTIRE suite green, because every fixture encoded
/// the same wrong assumption. <b>A round-trip property cannot encode the assumption it is testing</b> —
/// collapse and expand are written independently, so agreement between them is evidence rather than
/// restatement.
/// </para>
/// </summary>
public sealed class CollapseSelectionTests
{
    private static CompileOptions DefaultOptions() => new(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    // ── fixture helpers ─────────────────────────────────────────────────────

    private static Pin P(string name, string dir, bool isExec, string typeId = "") => new()
    {
        Id = Guid.NewGuid(), Name = name, Direction = dir, IsExec = isExec,
        TypeRef = new BlueprintTypeRef { TypeId = typeId },
    };

    private static Link W(Node f, Pin fp, Node t, Pin tp) => new()
    {
        FromNodeId = f.Id, FromPinId = fp.Id, ToNodeId = t.Id, ToPinId = tp.Id,
    };

    /// <summary>An exec pass-through body node with optional data pins.</summary>
    private static PrintStringNode Body(out Pin execIn, out Pin execOut)
    {
        var n = new PrintStringNode { Id = Guid.NewGuid() };
        execIn = P("In", "In", true); execOut = P("Out", "Out", true);
        n.Pins.AddRange(new[] { execIn, execOut });
        return n;
    }

    private static BlueprintAsset Asset(params Graph[] graphs) => new()
    {
        AssetId = Guid.NewGuid(), Name = "CollapseAsset",
        Dispatch = BlueprintDispatchKind.Instance,
        Graphs = graphs.ToList(), Header = new Header(),
    };

    /// <summary>Entry → A → B → Return, with A and B the collapsible middle.</summary>
    private static Graph LinearHost(out Node a, out Node b)
    {
        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        var eOut  = P("Out", "Out", true); entry.Pins.Add(eOut);

        a = Body(out var aIn, out var aOut);
        b = Body(out var bIn, out var bOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = P("In", "In", true); ret.Pins.Add(retIn);

        return new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, a, b, ret },
            Links = { W(entry, eOut, a, aIn), W(a, aOut, b, bIn), W(b, bOut, ret, retIn) },
        };
    }

    private static CollapsePlan PlanFor(Graph host, IEnumerable<Node> selection, CollapseTarget target)
    {
        var r = CollapseAnalysis.Analyse(host, selection.Select(n => n.Id).ToList(), target);
        Assert.False(r.IsRefused,
            "expected a plan but got: " + string.Join("; ", r.Refusals.Select(x => x.Code)));
        return r.Plan!;
    }

    // ────────────────────────────────────────────────────────────────────────
    // The four boundary sets
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Analyse_LinearSelection_HasOneEntryOneExitNoData()
    {
        var host = LinearHost(out var a, out var b);
        var plan = PlanFor(host, new[] { a, b }, CollapseTarget.Macro);

        Assert.Single(plan.Entries);
        Assert.Single(plan.Exits);
        Assert.Empty(plan.Inputs);
        Assert.Empty(plan.Outputs);
        Assert.Equal(a.Id, plan.Entries[0].InteriorNodeId);   // the door is A
        Assert.Equal(b.Id, plan.Exits[0].InteriorNodeId);     // the exit leaves B
    }

    /// <summary>
    /// ⭐ Case (a). One outside producer feeding TWO selected nodes is <b>one</b> input, not two —
    /// deduplicated by the producer PIN, with both interior consumers re-tied to the same parameter.
    /// Naive code emits two parameters with the same name, which then pair positionally against a call
    /// node that has two pins for one value.
    /// </summary>
    [Fact]
    public void Analyse_OneProducerFeedingTwoSelectedNodes_IsOneInput()
    {
        var host = LinearHost(out var a, out var b);

        var lit    = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = "1" };
        var litOut = P("Value", "Out", false, "System.Int32"); lit.Pins.Add(litOut);
        var aArg   = P("X", "In", false, "System.Int32"); a.Pins.Add(aArg);
        var bArg   = P("Y", "In", false, "System.Int32"); b.Pins.Add(bArg);

        host.Nodes.Add(lit);
        host.Links.Add(W(lit, litOut, a, aArg));
        host.Links.Add(W(lit, litOut, b, bArg));

        var plan = PlanFor(host, new[] { a, b }, CollapseTarget.Macro);

        Assert.Single(plan.Inputs);
        Assert.Equal(litOut.Id, plan.Inputs[0].SourcePinId);
    }

    /// <summary>⭐ Case (b). One selected node feeding three outside consumers is <b>one</b> output.</summary>
    [Fact]
    public void Analyse_OneSelectedNodeFeedingThreeOutsideConsumers_IsOneOutput()
    {
        var host = LinearHost(out var a, out var b);
        var aVal = P("Result", "Out", false, "System.Int32"); a.Pins.Add(aVal);

        for (int i = 0; i < 3; i++)
        {
            var sink    = new PrintStringNode { Id = Guid.NewGuid() };
            var sinkArg = P("V", "In", false, "System.Int32"); sink.Pins.Add(sinkArg);
            host.Nodes.Add(sink);
            host.Links.Add(W(a, aVal, sink, sinkArg));
        }

        var plan = PlanFor(host, new[] { a }, CollapseTarget.Macro);

        Assert.Single(plan.Outputs);
        Assert.Equal(aVal.Id, plan.Outputs[0].SourcePinId);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Refusals — each asserts the RULE and the nodes named
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ Case (c), the one refusal the four-set table does not reveal. The selection feeds an outside
    /// node that feeds back in, so the extracted graph would have to produce an output before it
    /// receives an input — unrepresentable as a call, in either direction.
    /// </summary>
    [Fact]
    public void Analyse_CyclicBoundary_IsRefused_AndNamesTheOutsideNode()
    {
        var host = LinearHost(out var a, out var b);

        var outside    = new PrintStringNode { Id = Guid.NewGuid() };
        var outsideIn  = P("V",   "In",  false, "System.Int32");
        var outsideOut = P("Res", "Out", false, "System.Int32");
        outside.Pins.AddRange(new[] { outsideIn, outsideOut });

        var aVal  = P("Result", "Out", false, "System.Int32"); a.Pins.Add(aVal);
        var bArg  = P("Back",   "In",  false, "System.Int32"); b.Pins.Add(bArg);

        host.Nodes.Add(outside);
        host.Links.Add(W(a, aVal, outside, outsideIn));       // selection → outside
        host.Links.Add(W(outside, outsideOut, b, bArg));      // outside → selection

        var r = CollapseAnalysis.Analyse(host, new[] { a.Id, b.Id }, CollapseTarget.Macro);

        Assert.True(r.IsRefused);
        var reason = Assert.Single(r.Refusals, x => x.Code == CollapseAnalysis.RefusalCodes.CyclicBoundary);
        Assert.Contains(outside.Id, reason.NodeIds);
    }

    /// <summary>⭐ Case (d). The host's own boundary nodes are its boundary, not movable content.</summary>
    [Fact]
    public void Analyse_SelectionContainingTheHostsEntryNode_IsRefused()
    {
        var host  = LinearHost(out var a, out _);
        var entry = host.Nodes.OfType<EventEntryNode>().Single();

        var r = CollapseAnalysis.Analyse(host, new[] { entry.Id, a.Id }, CollapseTarget.Macro);

        Assert.True(r.IsRefused);
        var reason = Assert.Single(r.Refusals,
            x => x.Code == CollapseAnalysis.RefusalCodes.BoundaryNodeSelected);
        Assert.Contains(entry.Id, reason.NodeIds);
    }

    /// <summary>
    /// Q26-F: a Function cannot suspend, so a latent selection is refused for Function — ⭐ but
    /// ACCEPTED for Macro, which is the whole point of the ruling. Unreal refuses both.
    /// </summary>
    [Fact]
    public void Analyse_LatentSelection_RefusedForFunction_ButAcceptedForMacro()
    {
        var host = LinearHost(out var a, out var b);

        var delay = new LatentDelayNode { Id = Guid.NewGuid() };
        var dIn   = P("In", "In", true); var dOut = P("Out", "Out", true);
        delay.Pins.AddRange(new[] { dIn, dOut });

        // splice the delay between A and B
        var aToB = host.Links.Single(l => l.FromNodeId == a.Id && l.ToNodeId == b.Id);
        host.Nodes.Add(delay);
        host.Links.Add(new Link { FromNodeId = delay.Id, FromPinId = dOut.Id,
                                  ToNodeId = aToB.ToNodeId, ToPinId = aToB.ToPinId });
        aToB.ToNodeId = delay.Id; aToB.ToPinId = dIn.Id;

        var sel = new[] { a.Id, delay.Id, b.Id };

        var asFunction = CollapseAnalysis.Analyse(host, sel, CollapseTarget.Function);
        Assert.True(asFunction.IsRefused);
        var reason = Assert.Single(asFunction.Refusals,
            x => x.Code == CollapseAnalysis.RefusalCodes.FunctionLatent);
        Assert.Contains(delay.Id, reason.NodeIds);

        var asMacro = CollapseAnalysis.Analyse(host, sel, CollapseTarget.Macro);
        Assert.False(asMacro.IsRefused);
    }

    /// <summary>A Function returns once, so two exec exits cannot be expressed.</summary>
    [Fact]
    public void Analyse_TwoExitsToFunction_IsRefused()
    {
        var host = LinearHost(out var a, out var b);

        // Give A a second exec-out leaving the selection.
        var aAlt = P("Alt", "Out", true); a.Pins.Add(aAlt);
        var tail = Body(out var tailIn, out _);
        host.Nodes.Add(tail);
        host.Links.Add(W(a, aAlt, tail, tailIn));

        var r = CollapseAnalysis.Analyse(host, new[] { a.Id, b.Id }, CollapseTarget.Function);

        Assert.True(r.IsRefused);
        Assert.Single(r.Refusals, x => x.Code == CollapseAnalysis.RefusalCodes.FunctionMultipleExits);

        // ⭐ …and the same selection IS collapsible to a Macro, which declares one exec output per exit.
        var asMacro = CollapseAnalysis.Analyse(host, new[] { a.Id, b.Id }, CollapseTarget.Macro);
        Assert.False(asMacro.IsRefused);
        Assert.Equal(2, asMacro.Plan!.Exits.Count);
    }

    /// <summary>
    /// ⚠ Not in the handoff, and the same class of hole as the exits rule: a <c>FunctionCallNode</c>
    /// has ONE exec-in, so a Function built from a two-entry selection would silently lose every path
    /// but the first.
    /// </summary>
    [Fact]
    public void Analyse_TwoEntriesToFunction_IsRefused()
    {
        var host = LinearHost(out var a, out var b);

        var second = Body(out var sIn, out var sOut);
        var bIn2   = P("In2", "In", true); b.Pins.Add(bIn2);
        host.Nodes.Add(second);
        host.Links.Add(W(second, sOut, b, bIn2));   // a second door into the selection
        _ = sIn;

        var r = CollapseAnalysis.Analyse(host, new[] { a.Id, b.Id }, CollapseTarget.Function);

        Assert.True(r.IsRefused);
        Assert.Single(r.Refusals, x => x.Code == CollapseAnalysis.RefusalCodes.FunctionMultipleEntries);

        var asMacro = CollapseAnalysis.Analyse(host, new[] { a.Id, b.Id }, CollapseTarget.Macro);
        Assert.False(asMacro.IsRefused);
        Assert.Equal(2, asMacro.Plan!.Entries.Count);
    }

    // ────────────────────────────────────────────────────────────────────────
    // ⭐ The round-trip property (Q26-E1)
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>collapse → expand → structurally equivalent.</b> The definition of "equivalent" lives in
    /// <see cref="CanonicalGraphShape"/> and is quoted in the report: kinds, topology and declarations
    /// by name+type; ids, pin ids, positions and declaration order ignored.
    ///
    /// <para>
    /// ⚠ <b>Scope, stated honestly:</b> this binds the MACRO path only. <c>Stage2_5_ExpandMacros</c> is
    /// its inverse; there is no function-inlining pass, so the Function path gets the weaker
    /// compile-and-run proof instead.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("linear")]
    [InlineData("two-entries")]
    [InlineData("two-exits")]
    [InlineData("shared-input")]
    [InlineData("fanout-output")]
    public void CollapseToMacro_ThenExpand_IsStructurallyEquivalentToTheOriginal(string shape)
    {
        var host = BuildShape(shape, out var selection);
        var before = CanonicalGraphShape.Describe(host);

        var plan = PlanFor(host, selection, CollapseTarget.Macro);
        var edit = CollapseEmitter.Emit(host, plan, CollapseTarget.Macro, "Extracted");

        var asset = Asset(edit.RewrittenHost, edit.Extracted);

        var sink = new DiagnosticSink();
        var expanded = Stage2_5_ExpandMacros.Run(asset, new ValidationContext(sink, DefaultOptions()));

        Assert.DoesNotContain(sink.All, d => d.IsError);

        var after = CanonicalGraphShape.Describe(
            expanded.Graphs.Single(g => g.Id == edit.RewrittenHost.Id));

        Assert.Equal(before, after);
    }

    private static Graph BuildShape(string shape, out List<Node> selection)
    {
        var host = LinearHost(out var a, out var b);
        selection = new List<Node> { a, b };

        switch (shape)
        {
            case "linear":
                break;

            case "two-entries":
            {
                var second = Body(out _, out var sOut);
                var bIn2   = P("In2", "In", true); b.Pins.Add(bIn2);
                host.Nodes.Add(second);
                host.Links.Add(W(second, sOut, b, bIn2));
                break;
            }
            case "two-exits":
            {
                var aAlt = P("Alt", "Out", true); a.Pins.Add(aAlt);
                var tail = Body(out var tailIn, out _);
                host.Nodes.Add(tail);
                host.Links.Add(W(a, aAlt, tail, tailIn));
                break;
            }
            case "shared-input":
            {
                var lit    = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = "1" };
                var litOut = P("Value", "Out", false, "System.Int32"); lit.Pins.Add(litOut);
                var aArg   = P("X", "In", false, "System.Int32"); a.Pins.Add(aArg);
                var bArg   = P("Y", "In", false, "System.Int32"); b.Pins.Add(bArg);
                host.Nodes.Add(lit);
                host.Links.Add(W(lit, litOut, a, aArg));
                host.Links.Add(W(lit, litOut, b, bArg));
                break;
            }
            case "fanout-output":
            {
                var aVal = P("Result", "Out", false, "System.Int32"); a.Pins.Add(aVal);
                for (int i = 0; i < 3; i++)
                {
                    var s    = new PrintStringNode { Id = Guid.NewGuid() };
                    var sArg = P("V", "In", false, "System.Int32"); s.Pins.Add(sArg);
                    host.Nodes.Add(s);
                    host.Links.Add(W(a, aVal, s, sArg));
                }
                break;
            }
            default: throw new ArgumentOutOfRangeException(nameof(shape), shape, null);
        }
        return host;
    }

    /// <summary>
    /// The comparator must actually be able to say NO — a describe-everything-as-equal comparator
    /// would make every round-trip test above vacuous.
    /// </summary>
    [Fact]
    public void CanonicalGraphShape_DistinguishesADifferentTopology()
    {
        var one = LinearHost(out var a1, out _);
        var two = LinearHost(out var a2, out var b2);

        // Break the chain in `two`: A no longer feeds B.
        two.Links.RemoveAll(l => l.FromNodeId == a2.Id && l.ToNodeId == b2.Id);
        _ = a1;

        Assert.NotEqual(CanonicalGraphShape.Describe(one), CanonicalGraphShape.Describe(two));
    }

    /// <summary>…and must ignore the things it promises to ignore: fresh ids and positions.</summary>
    [Fact]
    public void CanonicalGraphShape_IgnoresIdsAndPositions()
    {
        var host = LinearHost(out _, out _);
        var clone = GraphFragmentCloner.Clone(host.Nodes, host.Links);
        var cloned = host.WithNodesAndLinks(clone.Nodes.ToList(), clone.Links.ToList());

        foreach (var n in cloned.Nodes) { n.EditorMetadata.X += 37; n.EditorMetadata.Y -= 11; }

        Assert.Equal(CanonicalGraphShape.Describe(host), CanonicalGraphShape.Describe(cloned));
    }
}
