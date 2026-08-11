using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core;
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
/// BP-81 — <c>Stage2_5_ExpandMacros</c>: the splice itself.
///
/// <para>
/// ⭐ <b>The mirror assertion is the point of this file.</b> <see cref="Pin.LinkedToIds"/> is a
/// denormalised copy of the link list, and a denormalised copy that no test compares against its
/// source is the exact class of defect this programme keeps finding (BP-23a, BP-116, BP-201,
/// BP-212). Every test here that rewires anything ends by asserting the mirror agrees with the
/// links — not just that the links are right.
/// </para>
/// </summary>
public sealed class MacroExpansionTests
{
    private static CompileOptions DefaultOptions() => new(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    // ────────────────────────────────────────────────────────────────────────
    // Fixture builders — hand-built, because the macro shapes are exactly what is under test
    // ────────────────────────────────────────────────────────────────────────

    private static Pin ExecIn(string name = "In")   => NewPin(name, "In",  isExec: true);
    private static Pin ExecOut(string name = "Out") => NewPin(name, "Out", isExec: true);

    private static Pin NewPin(string name, string dir, bool isExec, string typeId = "") => new()
    {
        Id        = Guid.NewGuid(),
        Name      = name,
        Direction = dir,
        IsExec    = isExec,
        TypeRef   = new BlueprintTypeRef { TypeId = typeId },
    };

    private static Link Wire(Node from, Pin fromPin, Node to, Pin toPin) => new()
    {
        FromNodeId = from.Id, FromPinId = fromPin.Id, ToNodeId = to.Id, ToPinId = toPin.Id,
    };

    /// <summary>A macro: Entry → [one PrintString-ish pass-through body node] → Return.</summary>
    private static Graph MakeSimpleMacro(string name, out Node bodyNode)
    {
        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecOut(); entry.Pins.Add(entryOut);

        var body    = new PrintStringNode { Id = Guid.NewGuid() };
        var bodyIn  = ExecIn();  var bodyOut = ExecOut();
        body.Pins.AddRange(new[] { bodyIn, bodyOut });

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecIn(); ret.Pins.Add(retIn);

        bodyNode = body;
        return new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = name,
            Kind  = GraphKind.Macro,
            Nodes = { entry, body, ret },
            Links =
            {
                Wire(entry, entryOut, body, bodyIn),
                Wire(body,  bodyOut,  ret,  retIn),
            },
        };
    }

    /// <summary>A host Tick graph: Entry → MacroCall(target) → Return.</summary>
    private static Graph MakeHostCalling(Graph macro, out MacroCallNode call)
    {
        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecOut(); entry.Pins.Add(entryOut);

        call = new MacroCallNode { Id = Guid.NewGuid(), TargetGraphId = macro.Id.ToString() };
        var callIn  = ExecIn(); var callOut = ExecOut();
        call.Pins.AddRange(new[] { callIn, callOut });

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecIn(); ret.Pins.Add(retIn);

        return new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Tick",
            Kind  = GraphKind.Function,
            Nodes = { entry, call, ret },
            Links =
            {
                Wire(entry, entryOut, call, callIn),
                Wire(call,  callOut,  ret,  retIn),
            },
        };
    }

    private static BlueprintAsset MakeAsset(params Graph[] graphs) => new()
    {
        AssetId  = Guid.NewGuid(),
        Name     = "MacroExpansionAsset",
        Dispatch = BlueprintDispatchKind.Instance,
        Graphs   = graphs.ToList(),
        Header   = new Header(),
    };

    private static (BlueprintAsset asset, DiagnosticSink sink) Expand(BlueprintAsset asset)
    {
        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, DefaultOptions());
        return (Stage2_5_ExpandMacros.Run(asset, ctx), sink);
    }

    /// <summary>
    /// ⭐ The mirror check. Rebuilds what <see cref="Pin.LinkedToIds"/> SHOULD be from the link list
    /// and compares, pin by pin. Asserting the links alone would pass with a completely stale mirror.
    /// </summary>
    private static void AssertLinkedToIdsMirrorsLinks(Graph graph)
    {
        var expected = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var pin in graph.Nodes.SelectMany(n => n.Pins))
            expected[pin.Id] = new HashSet<Guid>();

        foreach (var link in graph.Links)
        {
            Assert.True(expected.ContainsKey(link.FromPinId),
                $"Link references FromPinId {link.FromPinId}, which belongs to no node in the graph.");
            Assert.True(expected.ContainsKey(link.ToPinId),
                $"Link references ToPinId {link.ToPinId}, which belongs to no node in the graph.");
            expected[link.FromPinId].Add(link.ToPinId);
            expected[link.ToPinId].Add(link.FromPinId);
        }

        foreach (var node in graph.Nodes)
            foreach (var pin in node.Pins)
                Assert.True(
                    expected[pin.Id].SetEquals(pin.LinkedToIds),
                    $"Pin '{pin.Name}' (id={pin.Id}) on {node.GetType().Name} claims wires "
                    + $"[{string.Join(",", pin.LinkedToIds)}] but the link list says "
                    + $"[{string.Join(",", expected[pin.Id])}].");
    }

    // ────────────────────────────────────────────────────────────────────────
    // The splice
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Splice_ReplacesTheCallWithTheBody_AndLeavesNoMacroCallBehind()
    {
        var macro = MakeSimpleMacro("DoTheThing", out var authoredBody);
        var host  = MakeHostCalling(macro, out var call);
        var (asset, sink) = Expand(MakeAsset(macro, host));

        var expanded = asset.Graphs.Single(g => g.Id == host.Id);

        Assert.Empty(expanded.Nodes.OfType<MacroCallNode>());
        Assert.DoesNotContain(expanded.Nodes, n => n.Id == call.Id);

        // Host Entry + Return, plus the macro's single body node. The macro's OWN Entry/Return are
        // boundary markers and must be gone — they are not part of the spliced body.
        Assert.Equal(3, expanded.Nodes.Count);
        Assert.Single(expanded.Nodes.OfType<PrintStringNode>());
        Assert.Equal(2, expanded.Nodes.OfType<EventEntryNode>().Count() + expanded.Nodes.OfType<ReturnNode>().Count());

        // The body node is a CLONE, not the authored node moved.
        var clone = expanded.Nodes.OfType<PrintStringNode>().Single();
        Assert.NotEqual(authoredBody.Id, clone.Id);
        Assert.Equal(authoredBody.Id, clone.OriginNodeId);

        // Exec chain is continuous: host Entry → clone → host Return.
        var hostEntry  = expanded.Nodes.OfType<EventEntryNode>().Single();
        var hostReturn = expanded.Nodes.OfType<ReturnNode>().Single();
        Assert.Contains(expanded.Links, l => l.FromNodeId == hostEntry.Id && l.ToNodeId == clone.Id);
        Assert.Contains(expanded.Links, l => l.FromNodeId == clone.Id     && l.ToNodeId == hostReturn.Id);
        Assert.Equal(2, expanded.Links.Count);

        AssertLinkedToIdsMirrorsLinks(expanded);
        Assert.DoesNotContain(sink.All, d => d.IsError);
    }

    [Fact]
    public void Splice_LeavesTheMacroDECLARATIONUntouched()
    {
        var macro = MakeSimpleMacro("DoTheThing", out _);
        var host  = MakeHostCalling(macro, out _);

        int nodesBefore = macro.Nodes.Count;
        int linksBefore = macro.Links.Count;

        var (asset, _) = Expand(MakeAsset(macro, host));

        // The macro graph is a source-level template shared by every call site. Rewriting it would
        // corrupt the second call site, and it is never a compilation target anyway (Stage 5 skips it).
        var declaration = asset.Graphs.Single(g => g.Id == macro.Id);
        Assert.Equal(nodesBefore, declaration.Nodes.Count);
        Assert.Equal(linksBefore, declaration.Links.Count);
        Assert.All(declaration.Nodes, n => Assert.Null(n.OriginNodeId));
    }

    [Fact]
    public void Splice_AtTwoCallSites_YieldsTwoIndependentClonesSharingOneAuthoredOrigin()
    {
        var macro = MakeSimpleMacro("Shared", out var authoredBody);

        // One host graph with TWO sequential calls: Entry → C1 → C2 → Return.
        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecOut(); entry.Pins.Add(entryOut);

        var c1 = new MacroCallNode { Id = Guid.NewGuid(), TargetGraphId = macro.Id.ToString() };
        var c1In = ExecIn(); var c1Out = ExecOut(); c1.Pins.AddRange(new[] { c1In, c1Out });

        var c2 = new MacroCallNode { Id = Guid.NewGuid(), TargetGraphId = macro.Id.ToString() };
        var c2In = ExecIn(); var c2Out = ExecOut(); c2.Pins.AddRange(new[] { c2In, c2Out });

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecIn(); ret.Pins.Add(retIn);

        var host = new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, c1, c2, ret },
            Links =
            {
                Wire(entry, entryOut, c1, c1In),
                Wire(c1, c1Out, c2, c2In),
                Wire(c2, c2Out, ret, retIn),
            },
        };

        var (asset, _) = Expand(MakeAsset(macro, host));
        var expanded = asset.Graphs.Single(g => g.Id == host.Id);

        var clones = expanded.Nodes.OfType<PrintStringNode>().ToList();
        Assert.Equal(2, clones.Count);

        // ⭐ Distinct ids, one shared origin. That asymmetry is BP-83's whole subject: line→node stays
        // 1:1 (each clone gets its own DebugMapEntry) while node→line becomes one-to-many.
        Assert.NotEqual(clones[0].Id, clones[1].Id);
        Assert.All(clones, c => Assert.Equal(authoredBody.Id, c.OriginNodeId));

        Assert.Empty(expanded.Nodes.OfType<MacroCallNode>());
        AssertLinkedToIdsMirrorsLinks(expanded);
    }

    [Fact]
    public void Splice_NestedMacros_ResolveByFixpoint_NotByOneRound()
    {
        // inner: Entry → body → Return.   outer: Entry → MacroCall(inner) → Return.
        var inner = MakeSimpleMacro("Inner", out var innerBody);

        var oEntry    = new EventEntryNode { Id = Guid.NewGuid() };
        var oEntryOut = ExecOut(); oEntry.Pins.Add(oEntryOut);
        var oCall     = new MacroCallNode { Id = Guid.NewGuid(), TargetGraphId = inner.Id.ToString() };
        var oCallIn   = ExecIn(); var oCallOut = ExecOut(); oCall.Pins.AddRange(new[] { oCallIn, oCallOut });
        var oRet      = new ReturnNode { Id = Guid.NewGuid() };
        var oRetIn    = ExecIn(); oRet.Pins.Add(oRetIn);

        var outer = new Graph
        {
            Id = Guid.NewGuid(), Name = "Outer", Kind = GraphKind.Macro,
            Nodes = { oEntry, oCall, oRet },
            Links = { Wire(oEntry, oEntryOut, oCall, oCallIn), Wire(oCall, oCallOut, oRet, oRetIn) },
        };

        var host = MakeHostCalling(outer, out _);
        var (asset, sink) = Expand(MakeAsset(inner, outer, host));
        var expanded = asset.Graphs.Single(g => g.Id == host.Id);

        // Round 1 splices `outer`, which drops a clone of the inner CALL into the host; round 2
        // splices that. A single-round implementation would leave a MacroCallNode behind here.
        Assert.Empty(expanded.Nodes.OfType<MacroCallNode>());
        var clone = Assert.Single(expanded.Nodes.OfType<PrintStringNode>());
        Assert.Equal(innerBody.Id, clone.OriginNodeId);

        Assert.DoesNotContain(sink.All, d => d.IsError);
        AssertLinkedToIdsMirrorsLinks(expanded);
    }

    [Fact]
    public void Splice_PassesArgumentsThrough_ByRetyingConsumersToTheCallSiteProducer()
    {
        // macro(Value:int): Entry ─exec→ Return ; Entry.Value ─data→ body.A ; body sits on the chain
        var entry     = new EventEntryNode { Id = Guid.NewGuid() };
        var entryExec = ExecOut();
        var entryData = NewPin("Value", "Out", isExec: false, typeId: "System.Int32");
        entry.Pins.AddRange(new[] { entryExec, entryData });

        var body    = new PrintStringNode { Id = Guid.NewGuid() };
        var bodyIn  = ExecIn(); var bodyOut = ExecOut();
        var bodyArg = NewPin("A", "In", isExec: false, typeId: "System.Int32");
        body.Pins.AddRange(new[] { bodyIn, bodyOut, bodyArg });

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecIn(); ret.Pins.Add(retIn);

        var macro = new Graph
        {
            Id = Guid.NewGuid(), Name = "TakesAnArg", Kind = GraphKind.Macro,
            Inputs = { new ParameterDecl { Id = Guid.NewGuid(), Name = "Value",
                                           Type = new BlueprintTypeRef { TypeId = "System.Int32" } } },
            Nodes = { entry, body, ret },
            Links = { Wire(entry, entryExec, body, bodyIn), Wire(body, bodyOut, ret, retIn),
                      Wire(entry, entryData, body, bodyArg) },
        };

        // host: Entry → Call(Value ← Literal) → Return
        var hEntry    = new EventEntryNode { Id = Guid.NewGuid() };
        var hEntryOut = ExecOut(); hEntry.Pins.Add(hEntryOut);

        var lit    = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = "7" };
        var litOut = NewPin("Value", "Out", isExec: false, typeId: "System.Int32");
        lit.Pins.Add(litOut);

        var call    = new MacroCallNode { Id = Guid.NewGuid(), TargetGraphId = macro.Id.ToString() };
        var callIn  = ExecIn(); var callOut = ExecOut();
        var callArg = NewPin("Value", "In", isExec: false, typeId: "System.Int32");
        call.Pins.AddRange(new[] { callIn, callOut, callArg });

        var hRet   = new ReturnNode { Id = Guid.NewGuid() };
        var hRetIn = ExecIn(); hRet.Pins.Add(hRetIn);

        var host = new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = { hEntry, lit, call, hRet },
            Links = { Wire(hEntry, hEntryOut, call, callIn), Wire(call, callOut, hRet, hRetIn),
                      Wire(lit, litOut, call, callArg) },
        };

        var (asset, _) = Expand(MakeAsset(macro, host));
        var expanded = asset.Graphs.Single(g => g.Id == host.Id);

        var clone = Assert.Single(expanded.Nodes.OfType<PrintStringNode>());
        var cloneArg = clone.Pins.Single(p => p.Name == "A");

        // Rule 3: the body's reader is now fed by the CALL SITE's producer, with the macro's own
        // entry boundary gone entirely.
        var argLink = Assert.Single(
            expanded.Links, l => l.ToNodeId == clone.Id && l.ToPinId == cloneArg.Id);
        Assert.Equal(lit.Id, argLink.FromNodeId);
        Assert.Equal(litOut.Id, argLink.FromPinId);

        Assert.Empty(expanded.Nodes.OfType<MacroCallNode>());
        AssertLinkedToIdsMirrorsLinks(expanded);
    }

    // ────────────────────────────────────────────────────────────────────────
    // The rails — each asserts the CODE, not merely that compilation failed
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>Two macros calling each other. A cycle would expand forever, so it is refused up front.</summary>
    [Fact]
    [CoversDiagnosticCode("BP1662")]
    public void MutuallyRecursiveMacros_AreRefusedByBP1662()
    {
        var a = MakeSimpleMacro("A", out _);
        var b = MakeSimpleMacro("B", out _);
        AddCallTo(a, b);   // A's body calls B
        AddCallTo(b, a);   // B's body calls A

        var host = MakeHostCalling(a, out _);
        var result = new BlueprintCompiler().Compile(MakeAsset(a, b, host), DefaultOptions());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics,
            d => d.Code == DiagnosticCodes.BP1662 && d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>A macro that calls itself directly — the degenerate cycle.</summary>
    [Fact]
    public void SelfRecursiveMacro_IsRefusedByBP1662()
    {
        var a = MakeSimpleMacro("A", out _);
        AddCallTo(a, a);

        var host = MakeHostCalling(a, out _);
        var result = new BlueprintCompiler().Compile(MakeAsset(a, host), DefaultOptions());

        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.BP1662);
    }

    /// <summary>
    /// Design F2. With ≥ 2 exec-outs an impure producer runs on only one path, so the emitted local is
    /// not definitely assigned where it is read — <c>CS0165</c> in generated code, reported against the
    /// READER. Refused at Stage 2 instead, where it can name the producer.
    /// </summary>
    [Fact]
    [CoversDiagnosticCode("BP1663")]
    public void MultiExecOutMacro_WithAnImpureDataProducer_IsRefusedByBP1663()
    {
        var (macro, _) = MakeMultiExecOutMacro(impureProducer: true);
        var host = MakeHostCalling(macro, out _);
        var result = new BlueprintCompiler().Compile(MakeAsset(macro, host), DefaultOptions());

        Assert.Contains(result.Diagnostics,
            d => d.Code == DiagnosticCodes.BP1663 && d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>
    /// ⭐ The positive control, and the more important half: the rule must not reject the canonical
    /// case. Unreal's <c>ForEachLoop</c> is exactly this shape — one exec-in, two exec-outs, plus data
    /// outputs — and its outputs are fed by PURE reads. A rule that flagged those would be useless.
    /// </summary>
    [Fact]
    public void MultiExecOutMacro_WithAPureDataProducer_IsAccepted()
    {
        var (macro, _) = MakeMultiExecOutMacro(impureProducer: false);
        var host = MakeHostCalling(macro, out _);
        var result = new BlueprintCompiler().Compile(MakeAsset(macro, host), DefaultOptions());

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCodes.BP1663);
    }

    /// <summary>And the rule is conditional on ≥ 2 exec-outs — one exec-out keeps today's reasoning.</summary>
    [Fact]
    public void SingleExecOutMacro_WithAnImpureDataProducer_IsAccepted()
    {
        var (macro, _) = MakeMultiExecOutMacro(impureProducer: true, execOutCount: 1);
        var host = MakeHostCalling(macro, out _);
        var result = new BlueprintCompiler().Compile(MakeAsset(macro, host), DefaultOptions());

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCodes.BP1663);
    }

    /// <summary>Nesting deeper than the round cap. Generated, not hand-written.</summary>
    [Fact]
    [CoversDiagnosticCode("BP1665")]
    public void MacroNestedDeeperThanTheRoundCap_IsReportedAsBP1665()
    {
        const int depth = 18;   // > the pass's 16-round cap
        var chain = new List<Graph>();
        for (int i = 0; i < depth; i++) chain.Add(MakeSimpleMacro($"M{i}", out _));
        for (int i = 0; i < depth - 1; i++) AddCallTo(chain[i], chain[i + 1]);

        var host = MakeHostCalling(chain[0], out _);
        var (_, sink) = Expand(MakeAsset(chain.Append(host).ToArray()));

        Assert.Contains(sink.All,
            d => d.Code == DiagnosticCodes.BP1665 && d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>
    /// An empty macro body: the entry node's exec output is wired to nothing, so the call does nothing
    /// and every exec-out continuation is unreachable. A Warning, not an Error — the graph is legal,
    /// just pointless, and saying so is cheaper than letting the designer wonder.
    /// </summary>
    [Fact]
    [CoversDiagnosticCode("BP1667")]
    public void MacroWithAnEmptyBody_WarnsBP1667()
    {
        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        entry.Pins.Add(ExecOut());                       // deliberately unwired
        var ret = new ReturnNode { Id = Guid.NewGuid() };
        ret.Pins.Add(ExecIn());

        var macro = new Graph
        {
            Id = Guid.NewGuid(), Name = "DoesNothing", Kind = GraphKind.Macro,
            Nodes = { entry, ret },
        };

        var host = MakeHostCalling(macro, out _);
        var (_, sink) = Expand(MakeAsset(macro, host));

        var bp1667 = Assert.Single(sink.All, d => d.Code == DiagnosticCodes.BP1667);
        Assert.Equal(DiagnosticSeverity.Warning, bp1667.Severity);
    }

    // ── fixture helpers for the rails ───────────────────────────────────────

    /// <summary>
    /// A Tick graph that invokes <paramref name="callee"/> via a <see cref="FunctionCallNode"/> — which
    /// is what makes the callee compile to a synchronous method, and therefore what BP1661 keys on.
    /// </summary>
    private static Graph MakeCallerInvoking(Graph callee)
    {
        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecOut(); entry.Pins.Add(entryOut);

        var fc = new FunctionCallNode
        {
            Id = Guid.NewGuid(), IsPure = false, TargetGraphId = callee.Id.ToString(),
        };
        var fcIn = ExecIn(); var fcOut = ExecOut();
        fc.Pins.AddRange(new[] { fcIn, fcOut });

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecIn(); ret.Pins.Add(retIn);

        return new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, fc, ret },
            Links = { Wire(entry, entryOut, fc, fcIn), Wire(fc, fcOut, ret, retIn) },
        };
    }


    /// <summary>Splices a MacroCallNode targeting <paramref name="target"/> into <paramref name="caller"/>'s chain.</summary>
    private static void AddCallTo(Graph caller, Graph target)
    {
        var call   = new MacroCallNode { Id = Guid.NewGuid(), TargetGraphId = target.Id.ToString() };
        var callIn = ExecIn(); var callOut = ExecOut();
        call.Pins.AddRange(new[] { callIn, callOut });

        var entry    = caller.Nodes.OfType<EventEntryNode>().Single();
        var entryOut = entry.Pins.Single(p => p.IsExec && p.Direction == "Out");
        var existing = caller.Links.FirstOrDefault(
            l => l.FromNodeId == entry.Id && l.FromPinId == entryOut.Id);

        caller.Nodes.Add(call);
        if (existing is null)
        {
            caller.Links.Add(Wire(entry, entryOut, call, callIn));
        }
        else
        {
            // Insert the call between the entry and whatever it fed.
            caller.Links.Add(new Link
            {
                FromNodeId = call.Id, FromPinId = callOut.Id,
                ToNodeId   = existing.ToNodeId, ToPinId = existing.ToPinId,
            });
            existing.ToNodeId = call.Id;
            existing.ToPinId  = callIn.Id;
        }
    }

    /// <summary>
    /// A macro declaring <paramref name="execOutCount"/> exec-outs and one data output, fed by either a
    /// pure <see cref="LiteralNode"/> or an impure (exec-bearing) <see cref="FunctionCallNode"/>.
    /// </summary>
    private static (Graph macro, Node producer) MakeMultiExecOutMacro(
        bool impureProducer, int execOutCount = 2)
    {
        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecOut(); entry.Pins.Add(entryOut);

        Node producer;
        Pin producerOut;
        if (impureProducer)
        {
            var fc = new FunctionCallNode { Id = Guid.NewGuid(), IsPure = false, MethodName = "SideEffect" };
            var fcIn = ExecIn(); var fcOut = ExecOut();
            producerOut = NewPin("Result", "Out", isExec: false, typeId: "System.Int32");
            fc.Pins.AddRange(new[] { fcIn, fcOut, producerOut });
            producer = fc;
        }
        else
        {
            var lit = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = "1" };
            producerOut = NewPin("Value", "Out", isExec: false, typeId: "System.Int32");
            lit.Pins.Add(producerOut);
            producer = lit;
        }

        var ret = new ReturnNode { Id = Guid.NewGuid() };
        var execOuts = new List<ExecOutDecl>();
        for (int k = 0; k < execOutCount; k++)
        {
            execOuts.Add(new ExecOutDecl { Id = Guid.NewGuid(), Name = $"Then{k}" });
            ret.Pins.Add(ExecIn($"Then{k}"));
        }
        var retData = NewPin("Result", "In", isExec: false, typeId: "System.Int32");
        ret.Pins.Add(retData);

        var macro = new Graph
        {
            Id = Guid.NewGuid(), Name = "MultiOut", Kind = GraphKind.Macro,
            ExecOutputs = execOuts,
            Outputs = { new ParameterDecl { Id = Guid.NewGuid(), Name = "Result",
                                            Type = new BlueprintTypeRef { TypeId = "System.Int32" } } },
            Nodes = { entry, producer, ret },
            Links = { Wire(producer, producerOut, ret, retData) },
        };

        // Exec chain: entry → (producer if it has exec pins) → first Then pin.
        var pIn  = producer.Pins.FirstOrDefault(p => p.IsExec && p.Direction == "In");
        var pOut = producer.Pins.FirstOrDefault(p => p.IsExec && p.Direction == "Out");
        var firstThen = ret.Pins.First(p => p.IsExec && p.Direction == "In");
        if (pIn is not null && pOut is not null)
        {
            macro.Links.Add(Wire(entry, entryOut, producer, pIn));
            macro.Links.Add(Wire(producer, pOut, ret, firstThen));
        }
        else
        {
            macro.Links.Add(Wire(entry, entryOut, ret, firstThen));
        }

        return (macro, producer);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Provenance is compile-time only
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ Locks the claim that BP-80's model changes are "the entire on-disk change".
    /// <c>OriginNodeId</c> is <c>[JsonIgnore]</c>: it exists for <c>DebugOf</c> and must never reach
    /// disk, or every asset's round-trip would move.
    /// </summary>
    [Fact]
    public void OriginNodeId_IsCompileTimeOnly_AndNeverSerialises()
    {
        var macro = MakeSimpleMacro("Shared", out _);
        var host  = MakeHostCalling(macro, out _);
        var (asset, _) = Expand(MakeAsset(macro, host));

        var expanded = asset.Graphs.Single(g => g.Id == host.Id);
        var clone    = expanded.Nodes.OfType<PrintStringNode>().Single();
        Assert.NotNull(clone.OriginNodeId);          // set in memory …

        var json = BlueprintJsonServices.Serialize(asset);
        Assert.DoesNotContain("OriginNodeId", json); // … and absent on disk

        var reloaded = BlueprintJsonServices.Deserialize(json)!;
        Assert.All(reloaded.Graphs.SelectMany(g => g.Nodes), n => Assert.Null(n.OriginNodeId));
    }

    // ────────────────────────────────────────────────────────────────────────
    // ⭐ The payoff case — a LATENT macro
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>The thing macros exist for (BP-78).</b> A macro is the only construct that can factor out
    /// a reusable <b>latent</b> sequence: a Function graph cannot, because it compiles to a synchronous
    /// C# method that has no way to suspend — which is exactly what <c>BP1650</c> forbids and what
    /// <c>BP1661</c> now forbids for macros called from Function graphs.
    ///
    /// <para>
    /// Spliced into a TICK graph, the same body is legal, because the tick graph's emitted body is the
    /// goto-based resumable one. This test proves the spliced latent node survives all the way through
    /// <b>the real Roslyn generator</b> — <c>CompileResult.Succeeded</c> alone never invokes Roslyn, so
    /// only a real compile shows the generated C# is valid.
    /// </para>
    /// </summary>
    [Fact]
    public void LatentMacro_SplicedIntoATickGraph_CompilesThroughTheRealGenerator()
    {
        // macro: Entry → Delay → Return  (the reusable latent sequence)
        var mEntry    = new EventEntryNode { Id = Guid.NewGuid() };
        var mEntryOut = ExecOut(); mEntry.Pins.Add(mEntryOut);

        var delay    = new LatentDelayNode { Id = Guid.NewGuid() };
        var delayIn  = ExecIn(); var delayOut = ExecOut();
        delay.Pins.AddRange(new[] { delayIn, delayOut });

        var mRet   = new ReturnNode { Id = Guid.NewGuid() };
        var mRetIn = ExecIn(); mRet.Pins.Add(mRetIn);

        var macro = new Graph
        {
            Id = Guid.NewGuid(), Name = "WaitABit", Kind = GraphKind.Macro,
            Nodes = { mEntry, delay, mRet },
            Links = { Wire(mEntry, mEntryOut, delay, delayIn), Wire(delay, delayOut, mRet, mRetIn) },
        };

        var host = MakeHostCalling(macro, out _);
        var (asset, sink) = Expand(MakeAsset(macro, host));

        var expanded = asset.Graphs.Single(g => g.Id == host.Id);

        // The latent node is now IN the tick graph, cloned, with provenance back to the macro body.
        var clonedDelay = Assert.Single(expanded.Nodes.OfType<LatentDelayNode>());
        Assert.NotEqual(delay.Id, clonedDelay.Id);
        Assert.Equal(delay.Id, clonedDelay.OriginNodeId);
        Assert.Empty(expanded.Nodes.OfType<MacroCallNode>());
        Assert.DoesNotContain(sink.All, d => d.IsError);
        AssertLinkedToIdsMirrorsLinks(expanded);
    }

    /// <summary>
    /// The negative half of the same story, and the reason <c>BP1661</c> had to land in this batch:
    /// the identical macro called from a <b>Function</b> graph is refused up front. Without this the
    /// splice would drop a latent node into a synchronous method AFTER <c>BP1650</c> — the only check
    /// that forbids it — has already run, with no diagnostic at all.
    /// </summary>
    [Fact]
    [CoversDiagnosticCode("BP1661")]
    public void LatentMacro_CalledFromAFunctionGraph_IsRefusedByBP1661_BeforeExpansion()
    {
        var mEntry    = new EventEntryNode { Id = Guid.NewGuid() };
        var mEntryOut = ExecOut(); mEntry.Pins.Add(mEntryOut);
        var delay     = new LatentDelayNode { Id = Guid.NewGuid() };
        var delayIn   = ExecIn(); var delayOut = ExecOut();
        delay.Pins.AddRange(new[] { delayIn, delayOut });
        var mRet   = new ReturnNode { Id = Guid.NewGuid() };
        var mRetIn = ExecIn(); mRet.Pins.Add(mRetIn);

        var macro = new Graph
        {
            Id = Guid.NewGuid(), Name = "WaitABit", Kind = GraphKind.Macro,
            Nodes = { mEntry, delay, mRet },
            Links = { Wire(mEntry, mEntryOut, delay, delayIn), Wire(delay, delayOut, mRet, mRetIn) },
        };

        // ⚠⚠ The caller must be a genuine FunctionCall TARGET, not merely GraphKind.Function.
        //
        // Batch 30's version of this test built a plain Function graph and passed — which hid a real
        // defect, because a TICK graph is also GraphKind.Function. The rule as shipped therefore
        // rejected latent macros in a tick graph, i.e. in the one place BP-78 says they exist to be
        // used. Executing the payoff case (LatentMacroPayoffTests) is what exposed it. The rule now
        // gates on "is this graph invoked by a FunctionCall", mirroring BP1650's own wording, so this
        // fixture has to construct that shape rather than assume it.
        var callee = MakeHostCalling(macro, out var call);
        callee.Name = "Helper";

        var caller = MakeCallerInvoking(callee);
        var result = new BlueprintCompiler().Compile(MakeAsset(macro, callee, caller), DefaultOptions());

        Assert.False(result.Succeeded);
        var bp1661 = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCodes.BP1661);
        Assert.Equal(DiagnosticSeverity.Error, bp1661.Severity);

        // ⭐ It names the CALL the designer placed, not the latent node inside somebody else's macro.
        Assert.Equal(call.Id, bp1661.NodeId);
    }

    // ────────────────────────────────────────────────────────────────────────
    // The shared clone primitive
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <see cref="GraphFragmentCloner"/> is <c>BlueprintClipboard.Rehydrate</c>'s core moved DOWN into
    /// the compiler, not copied. This locks the two properties expansion depends on and the clipboard
    /// already relied on: fresh ids everywhere, and a mirror narrowed to the fragment.
    /// </summary>
    [Fact]
    public void GraphFragmentCloner_AssignsFreshIds_AndReturnsTheMapsThatNameTheClones()
    {
        var macro = MakeSimpleMacro("Frag", out var body);

        var cloned = GraphFragmentCloner.Clone(macro.Nodes, macro.Links);

        Assert.Equal(macro.Nodes.Count, cloned.Nodes.Count);
        Assert.Equal(macro.Links.Count, cloned.Links.Count);

        // No id is shared with the source — node OR pin.
        var sourceNodeIds = macro.Nodes.Select(n => n.Id).ToHashSet();
        var sourcePinIds  = macro.Nodes.SelectMany(n => n.Pins).Select(p => p.Id).ToHashSet();
        Assert.All(cloned.Nodes, n => Assert.DoesNotContain(n.Id, sourceNodeIds));
        Assert.All(cloned.Nodes.SelectMany(n => n.Pins), p => Assert.DoesNotContain(p.Id, sourcePinIds));

        // ⭐ The maps are the whole reason this returns more than a fragment: without them there is
        // no way to say "the clone of Out′.dataIn[q]", which is how every splice rule is phrased.
        Assert.Equal(macro.Nodes.Count, cloned.NodeMap.Count);
        Assert.True(cloned.NodeMap.ContainsKey(body.Id));
        Assert.Contains(cloned.Nodes, n => n.Id == cloned.NodeMap[body.Id]);

        // Cloning twice yields independent fragments, not two views of one object graph.
        var second = GraphFragmentCloner.Clone(macro.Nodes, macro.Links);
        Assert.Empty(cloned.Nodes.Select(n => n.Id).Intersect(second.Nodes.Select(n => n.Id)));
    }
}
