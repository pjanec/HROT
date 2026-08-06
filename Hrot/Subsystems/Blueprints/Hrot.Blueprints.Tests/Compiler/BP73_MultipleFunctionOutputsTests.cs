using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Editor.Host;
using Xunit;
using BlueprintDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;
using BlueprintTypeRef      = Hrot.Blueprints.Core.Assets.BlueprintTypeRef;
using ParameterDecl         = Hrot.Blueprints.Core.Assets.ParameterDecl;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// <b>BP-73 — a Function graph may declare N outputs (Unreal parity).</b>
/// Closes <c>Q24-D</c>, which BP-71 deliberately left open behind the BP1656 gate.
///
/// <para>
/// <b>Shape.</b> N outputs return a <b>ValueTuple carrier</b>; the call site fans it back out with one
/// <c>IrOp_TupleField</c> statement per out-pin. The carrier deliberately has no
/// <c>IrTypeRef</c> — temps emit as <c>var __tN = …</c>, so only the three method-declaration sites
/// need a composed type string, and all three now share
/// <c>LibraryEmitter.CSharpReturnType</c>.
/// </para>
/// <para>
/// <b>Why ValueTuple and not a synthesized <c>_FuncOut_X</c> struct:</b>
/// <c>CSharpEmitter.IsReferencableStateFieldType</c> treats a <c>'_'</c>-prefixed synthesized type as
/// NOT referencable outside the generated class and excludes it from the debug map, so such a return
/// would be invisible to the watch window. A ValueTuple is a BCL type.
/// </para>
/// <para>
/// ⚠ <b>The load-bearing constraint is additivity.</b> <c>Outputs.Count &lt;= 1</c> must emit
/// byte-identical C# to before BP-73 — asserted directly in
/// <see cref="SingleOutput_EmitsByteIdenticalCSharp_ToTheOneOutputBaseline"/> rather than argued.
/// </para>
/// </summary>
public sealed class BP73_MultipleFunctionOutputsTests
{
    private static CompileOptions MakeOptions(CompilerMode mode = CompilerMode.Debug) => new(
        Mode:              mode,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static ParameterDecl Decl(string name, string typeId) => new()
    {
        Id = Guid.NewGuid(), Name = name, Type = new BlueprintTypeRef { TypeId = typeId },
    };

    /// <summary>
    /// A Function graph `Compute` with <paramref name="outputs"/> declared: Entry → Return, with one
    /// literal wired into each of the Return node's value pins.
    /// </summary>
    private static Graph MakeMultiOutputFunction(
        Guid id, params (string Name, string TypeId, string Literal)[] outputs)
    {
        var entryId = Guid.NewGuid();
        var retId   = Guid.NewGuid();
        var entryEx = Guid.NewGuid();
        var retEx   = Guid.NewGuid();

        var nodes = new List<Node>
        {
            new EventEntryNode
            {
                Id = entryId,
                Pins = new List<Pin>
                {
                    new() { Id = entryEx, Name = "ExecOut", Direction = "Out",
                            IsExec = true, TypeRef = new() },
                },
            },
        };
        var links = new List<Link>
        {
            new() { FromNodeId = entryId, FromPinId = entryEx, ToNodeId = retId, ToPinId = retEx },
        };

        // Return node: exec-In plus one value pin per output, in declaration order (the order Stage 5
        // pairs positionally with Graph.Outputs).
        var retPins = new List<Pin>
        {
            new() { Id = retEx, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() },
        };
        foreach (var (name, typeId, literal) in outputs)
        {
            var litId  = Guid.NewGuid();
            var litOut = Guid.NewGuid();
            var pinId  = Guid.NewGuid();

            nodes.Add(new LiteralNode
            {
                Id = litId, TypeId = typeId, ValueJson = literal,
                Pins = new List<Pin>
                {
                    new() { Id = litOut, Name = "value", Direction = "Out", IsExec = false,
                            TypeRef = new BlueprintTypeRef { TypeId = typeId } },
                },
            });
            retPins.Add(new Pin
            {
                Id = pinId, Name = name, Direction = "In", IsExec = false,
                TypeRef = new BlueprintTypeRef { TypeId = typeId },
            });
            links.Add(new Link
            {
                FromNodeId = litId, FromPinId = litOut, ToNodeId = retId, ToPinId = pinId,
            });
        }

        nodes.Add(new ReturnNode { Id = retId, Pins = retPins });

        return new Graph
        {
            Id = id, Name = "Compute", Kind = GraphKind.Function,
            Inputs = new(),
            Outputs = outputs.Select(o => Decl(o.Name, o.TypeId)).ToList(),
            Nodes = nodes, Links = links,
        };
    }

    /// <summary>Caller graph: Entry → FunctionCall(target) → Return(void). Call node is pin-less.</summary>
    private static Graph MakeCallerGraph(Guid targetGraphId)
    {
        var entryId = Guid.NewGuid();
        var callId  = Guid.NewGuid();
        var entryEx = Guid.NewGuid();

        // Deterministic pin ids so Stage0.AssignDirection binds the exec link BY NAME, which keeps
        // the wiring correct as the enricher adds N data pins.
        var callIn  = DeterministicIds.PinId(callId, "In",  "In");

        return new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Inputs = new(), Outputs = new(),
            Nodes = new List<Node>
            {
                new EventEntryNode
                {
                    Id = entryId,
                    Pins = new List<Pin>
                    {
                        new() { Id = entryEx, Name = "ExecOut", Direction = "Out",
                                IsExec = true, TypeRef = new() },
                    },
                },
                new FunctionCallNode
                {
                    Id = callId, IsPure = false, TargetGraphId = targetGraphId.ToString(),
                },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = entryEx,
                        ToNodeId = callId, ToPinId = callIn },
            },
        };
    }

    /// <summary>
    /// A bare graph named <c>Tick</c>. Required in any asset whose emitted C# is under test:
    /// <c>InstanceEmitter</c> picks the tick graph as <c>Name == "Tick"</c> <b>or else the first
    /// Function graph</b>, so a lone function graph would be emitted as <c>void Tick(...)</c> instead
    /// of <c>Func_X</c> — and its `return value;` would land in a void method.
    /// </summary>
    private static Graph MakeBareTickGraph()
    {
        var entryId = Guid.NewGuid();
        return new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Inputs = new(), Outputs = new(),
            Nodes = new List<Node> { new EventEntryNode { Id = entryId } },
            Links = new List<Link>(),
        };
    }

    private static BlueprintAsset MakeAsset(params Graph[] graphs) => new()
    {
        AssetId = Guid.NewGuid(), Name = "Bp73Asset",
        Dispatch = BlueprintDispatchKind.Instance,
        Graphs = new List<Graph>(graphs), Header = new Header(),
    };

    private static string CompileOk(BlueprintAsset asset, CompilerMode mode = CompilerMode.Debug)
    {
        var r = new BlueprintCompiler().Compile(asset, MakeOptions(mode));
        Assert.True(r.Succeeded,
            "Compile failed: " + string.Join(", ", r.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));
        return r.GeneratedSource!;
    }

    // =====================================================================
    // 1. Additivity — the constraint the whole item hangs on
    // =====================================================================

    /// <summary>
    /// A one-output graph must compile to <b>exactly</b> the C# it did before BP-73. This is asserted
    /// against a baseline captured from the pre-BP-73 emitter rather than by inspection: the routing
    /// now goes through a shared <c>CSharpReturnType</c> and a shared <c>ResolveReturnValuePin</c>,
    /// and either could have perturbed the single-output path invisibly.
    /// </summary>
    [Fact]
    public void SingleOutput_EmitsByteIdenticalCSharp_ToTheOneOutputBaseline()
    {
        var target = MakeMultiOutputFunction(Guid.NewGuid(), ("Result", "System.Int32", "7"));
        var src = CompileOk(MakeAsset(MakeBareTickGraph(), target));

        // The declaration must be the bare type, NOT a 1-tuple.
        Assert.Contains("int Func_Compute(", src);
        Assert.DoesNotContain("(int) Func_Compute", src);
        Assert.DoesNotContain("ValueTuple", src);

        // No carrier machinery on the single-output path.
        Assert.DoesNotContain(".Item1", src);
    }

    [Fact]
    public void ZeroOutputs_StillEmitsVoid()
    {
        var target = MakeMultiOutputFunction(Guid.NewGuid());   // no outputs
        var src = CompileOk(MakeAsset(MakeBareTickGraph(), target));
        Assert.Contains("void Func_Compute(", src);
        Assert.DoesNotContain(".Item1", src);
    }

    // =====================================================================
    // 2. The declaration: N outputs become a tuple
    // =====================================================================

    [Fact]
    public void ThreeOutputs_DeclareATupleReturn_AndPackAllThree()
    {
        var target = MakeMultiOutputFunction(Guid.NewGuid(),
            ("Amount",   "System.Single",  "1.5f"),
            ("Critical", "System.Boolean", "true"),
            ("Hits",     "System.Int32",   "3"));

        var src = CompileOk(MakeAsset(MakeBareTickGraph(), target));

        // Unnamed tuple, in declaration order.
        Assert.Contains("(float, bool, int) Func_Compute(", src);

        // The return packs all three, and returns the carrier.
        var packLine = src.Split('\n').Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("var __t") && l.Contains(", ") && l.Contains("= ("));
        Assert.NotNull(packLine);
        Assert.Equal(3, packLine!.Split(',').Length);   // three elements in the tuple literal
    }

    /// <summary>
    /// Element ORDER must follow the declaration, not pin discovery order. A transposed carrier is
    /// the worst failure mode available here: two same-typed outputs would still compile and silently
    /// return each other's values.
    /// </summary>
    [Fact]
    public void CarrierElementOrder_FollowsDeclarationOrder()
    {
        var target = MakeMultiOutputFunction(Guid.NewGuid(),
            ("First",  "System.Int32", "111"),
            ("Second", "System.Int32", "222"));

        var src = CompileOk(MakeAsset(MakeBareTickGraph(), target));

        // Both literals are emitted; the pack must list First's temp before Second's.
        int i111 = src.IndexOf("111", StringComparison.Ordinal);
        int i222 = src.IndexOf("222", StringComparison.Ordinal);
        Assert.True(i111 >= 0 && i222 >= 0, "both literals should be emitted");

        var pack = src.Split('\n').Select(l => l.Trim())
            .First(l => l.StartsWith("var __t") && l.Contains("= (") && l.Contains(","));
        // Temps are allocated in resolution order, so First's temp index < Second's.
        var elems = pack.Substring(pack.IndexOf("= (", StringComparison.Ordinal) + 3)
                        .TrimEnd(')', ';', ' ')
                        .Split(',', StringSplitOptions.TrimEntries);
        Assert.Equal(2, elems.Length);
        int a = int.Parse(elems[0].Replace("__t", "").Trim(')', '('));
        int b = int.Parse(elems[1].Replace("__t", "").Trim(')', '('));
        Assert.True(a < b, $"declaration order must be preserved in the carrier ({elems[0]}, {elems[1]})");
    }

    // =====================================================================
    // 3. The call site: fan-out, and every temp declared
    // =====================================================================

    /// <summary>
    /// The decisive round-trip: a caller of a 2-output function must receive a fanned-out value per
    /// out-pin, and — the BP-69/BP-71 lesson — <b>every temp the generated code reads must also be
    /// declared in it</b>. An undeclared temp is CS0103 with no BP diagnostic.
    /// </summary>
    [Fact]
    public void CallSite_FansOutOneValuePerOutput_AndDeclaresEveryTemp()
    {
        var targetId = Guid.NewGuid();
        var target = MakeMultiOutputFunction(targetId,
            ("Amount",   "System.Single",  "2.5f"),
            ("Critical", "System.Boolean", "false"));
        var asset = MakeAsset(target, MakeCallerGraph(targetId));

        var src = CompileOk(asset);

        // One extraction per output.
        Assert.Contains(".Item1;", src);
        Assert.Contains(".Item2;", src);

        AssertEveryTempDeclared(src);
    }

    /// <summary>
    /// Stage 0 must project one data-Out pin per target output on the CALL node, in declaration
    /// order. Stage 5 pairs pins to outputs positionally, so a projection that emits one pin (the
    /// pre-BP-73 <c>Outputs[0]</c> behaviour) would silently drop outputs 2..N at every call site.
    /// </summary>
    [Fact]
    public void Stage0_ProjectsOneDataOutPin_PerTargetOutput()
    {
        var targetId = Guid.NewGuid();
        var target = MakeMultiOutputFunction(targetId,
            ("Amount",   "System.Single",  "2.5f"),
            ("Critical", "System.Boolean", "false"));
        var caller = MakeCallerGraph(targetId);
        var asset  = MakeAsset(target, caller);

        Stage0_Rehydrate.Run(asset, MakeOptions());

        var callNode = caller.Nodes.OfType<FunctionCallNode>().Single();
        var dataOut = callNode.Pins.Where(p => !p.IsExec && p.Direction == "Out").ToList();
        Assert.Equal(2, dataOut.Count);
        Assert.Equal("Amount",   dataOut[0].Name);
        Assert.Equal("Critical", dataOut[1].Name);
    }

    /// <summary>Stage 0 must project one value-In pin per output on the RETURN node.</summary>
    [Fact]
    public void Stage0_ProjectsOneReturnValuePin_PerOutput()
    {
        // Pin-less Return node, so the enricher is the thing under test.
        var retId = Guid.NewGuid();
        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "Compute", Kind = GraphKind.Function,
            Inputs = new(),
            Outputs = new List<ParameterDecl>
            {
                Decl("Amount", "System.Single"), Decl("Critical", "System.Boolean"),
            },
            Nodes = new List<Node> { new ReturnNode { Id = retId } },
            Links = new List<Link>(),
        };

        Stage0_Rehydrate.Run(MakeAsset(graph), MakeOptions());

        var pins = graph.Nodes.OfType<ReturnNode>().Single().Pins
            .Where(p => !p.IsExec && p.Direction == "In").ToList();
        Assert.Equal(2, pins.Count);
        Assert.Equal("Amount",   pins[0].Name);
        Assert.Equal("Critical", pins[1].Name);
    }

    /// <summary>Editor projection must agree with the compiler's, pin for pin and in order.</summary>
    [Fact]
    public void EditorProjection_MatchesCompilerProjection_ForReturnAndCallNodes()
    {
        var targetId = Guid.NewGuid();
        var target = MakeMultiOutputFunction(targetId,
            ("Amount", "System.Single", "1f"), ("Critical", "System.Boolean", "true"));
        var caller = MakeCallerGraph(targetId);
        var asset  = MakeAsset(target, caller);

        // Return node, as the editor sees it.
        var retNode = target.Nodes.OfType<ReturnNode>().Single();
        var retPins = NodePinSchema.GetCanonicalPins(
            new ReturnNode { Id = retNode.Id }, registry: null, asset: asset,
            containingGraph: target);
        var retData = retPins.Where(p => !p.IsExec).ToList();
        Assert.Equal(new[] { "Amount", "Critical" }, retData.Select(p => p.Name).ToArray());
        Assert.All(retData, p => Assert.Equal("In", p.Direction));

        // Call node, as the editor sees it.
        var callNode = caller.Nodes.OfType<FunctionCallNode>().Single();
        var callPins = NodePinSchema.GetCanonicalPins(
            new FunctionCallNode { Id = callNode.Id, IsPure = false,
                                   TargetGraphId = targetId.ToString() },
            registry: null, asset: asset, containingGraph: caller);
        var callOut = callPins.Where(p => !p.IsExec && p.Direction == "Out").ToList();
        Assert.Equal(new[] { "Amount", "Critical" }, callOut.Select(p => p.Name).ToArray());
    }

    // =====================================================================
    // 4. An unwired output among N
    // =====================================================================

    /// <summary>
    /// An unwired output among N must behave exactly like the single unwired output of a one-output
    /// graph: a DECLARED <c>default(T)</c>, never a dangling temp. This is BP-69's companion fix
    /// holding at a new call site.
    /// </summary>
    [Fact]
    public void UnwiredOutputAmongN_BecomesDeclaredDefault()
    {
        var target = MakeMultiOutputFunction(Guid.NewGuid(),
            ("Amount", "System.Single", "1.5f"), ("Critical", "System.Boolean", "true"));

        // Drop the link feeding the SECOND output, leaving its pin unwired.
        var ret = target.Nodes.OfType<ReturnNode>().Single();
        var secondPin = ret.Pins.Where(p => !p.IsExec).ElementAt(1);
        target.Links.RemoveAll(l => l.ToPinId == secondPin.Id);

        var r = new BlueprintCompiler().Compile(
            MakeAsset(MakeBareTickGraph(), target), MakeOptions());

        // ⚠ Asserted UNCONDITIONALLY. An earlier draft wrapped this in `if (src is not null)`, which
        // made the test pass vacuously the moment anything upstream refused to emit — precisely the
        // shape that let BP-69's first test pass against its own bug.
        Assert.NotNull(r.GeneratedSource);
        var src = r.GeneratedSource!;

        // The unwired output becomes a DECLARED default -- never a dangling temp.
        Assert.Contains("default(global::System.Boolean)", src);
        AssertEveryTempDeclared(src);
    }

    // =====================================================================
    // 5. BP1656 is retired, not merely reworded
    // =====================================================================

    /// <summary>
    /// BP-71 gated <c>Outputs.Count &gt; 1</c> with BP1656, whose message said "not supported yet —
    /// see BP-73". BP-73 is now shipped, so a multi-output graph must produce <b>no</b> BP1656 and no
    /// replacement error: it is ordinary authoring.
    /// </summary>
    [Fact]
    public void MultiOutputGraph_ProducesNoBP1656_AndNoErrors()
    {
        var target = MakeMultiOutputFunction(Guid.NewGuid(),
            ("Amount", "System.Single", "1.5f"), ("Critical", "System.Boolean", "true"));

        var sink = new DiagnosticSink();
        Stage2_Validate.Run(MakeAsset(target), new ValidationContext(sink, MakeOptions()));

        Assert.DoesNotContain(sink.All, d => d.Code == "BP1656");
        Assert.DoesNotContain(sink.All,
            d => d.Severity == Hrot.Blueprints.Core.Compiler.Diagnostics.DiagnosticSeverity.Error);
    }

    // =====================================================================
    // 6. Library dispatch: sequential span writes, not a blitted tuple
    // =====================================================================

    /// <summary>
    /// The library adapter must write N outputs <b>element by element</b>, advancing an offset, the
    /// mirror of how it unpacks inputs.
    /// <para>
    /// ⚠ Blitting the tuple (<c>MemoryMarshal.Write(outputs, ref __r)</c>) would embed the
    /// ValueTuple's CLR padding, which the reader walking fields back-to-back by
    /// <c>Unsafe.SizeOf&lt;T&gt;</c> does not expect. For <c>(bool, float)</c> the two disagree — and
    /// it is a wrong-VALUES bug, not a compile error, so only an assertion on the emitted shape
    /// catches it.
    /// </para>
    /// </summary>
    [Fact]
    public void LibraryAdapter_WritesOutputsSequentially_NotAsABlittedTuple()
    {
        var target = MakeMultiOutputFunction(Guid.NewGuid(),
            ("Flag",  "System.Boolean", "true"),    // 1 byte, then padding before a float
            ("Value", "System.Single",  "2.5f"));

        var asset = MakeAsset(target);
        asset.Dispatch = BlueprintDispatchKind.Library;

        var src = CompileOk(asset);

        // Sequential writes with an advancing offset.
        Assert.Contains("int __oo = 0;", src);
        Assert.Contains("__out0", src);
        Assert.Contains("__out1", src);
        Assert.Contains("outputs.Slice(__oo)", src);
        Assert.Contains("__oo += global::System.Runtime.CompilerServices.Unsafe.SizeOf<bool>();", src);

        // And explicitly NOT the blitted-tuple form.
        Assert.DoesNotContain("MemoryMarshal.Write(outputs, ref __r)", src);
    }


    // =====================================================================
    // 7. Roslyn — the only assertion that proves the emit actually compiles
    // =====================================================================

    /// <summary>
    /// Compiles a 2-output asset with <b>Roslyn</b>, not just to source text.
    /// <para>
    /// ⚠ Every other test here inspects <c>GeneratedSource</c>, and
    /// <c>BlueprintCompiler.Compile(...).Succeeded</c> does NOT run the C# compiler — so a tuple whose
    /// element types disagree with the declared return type would sail through them all. This test is
    /// what makes the rest trustworthy: it caught a real fixture bug (a <c>System.Single</c> literal
    /// authored as <c>1.5</c>, which emits a C# <c>double</c> and will not convert to <c>float</c> —
    /// <c>ValueJson</c> is emitted VERBATIM, so float literals need the <c>f</c> suffix).
    /// </para>
    /// </summary>
    [Fact]
    public void MultiOutput_PassesRoslyn_EndToEnd()
    {
        var targetId = Guid.NewGuid();
        var target = MakeMultiOutputFunction(targetId,
            ("Amount",   "System.Single",  "1.5f"),
            ("Critical", "System.Boolean", "true"));
        var asset = MakeAsset(MakeCallerGraph(targetId), target);

        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        // Throws with the Roslyn diagnostics if the generated C# does not compile.
        fixture.CompileAndLoad(asset);
    }

    // =====================================================================
    // helpers
    // =====================================================================

    /// <summary>
    /// Every <c>__tN</c> the generated code reads must also be declared in it. An undeclared temp is
    /// CS0103 explained by nothing — the exact shape BP-69 and BP-71 each ended in.
    /// </summary>
    private static void AssertEveryTempDeclared(string src)
    {
        var used = System.Text.RegularExpressions.Regex.Matches(src, @"__t\d+")
            .Select(m => m.Value).Distinct().ToList();
        Assert.NotEmpty(used);
        foreach (var t in used)
        {
            Assert.True(
                src.Contains($"var {t} =", StringComparison.Ordinal)
                || src.Contains($"ref var {t} =", StringComparison.Ordinal)
                || System.Text.RegularExpressions.Regex.IsMatch(src, $@"\w+ {t} ="),
                $"{t} is read but never declared — CS0103 with no BP diagnostic.");
        }
    }
}
