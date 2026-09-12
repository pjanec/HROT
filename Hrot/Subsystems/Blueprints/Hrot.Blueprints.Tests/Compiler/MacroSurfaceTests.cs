using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core;
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
using DiagnosticSeverity    = Hrot.Blueprints.Core.Compiler.Diagnostics.DiagnosticSeverity;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// BP-80 — the macro authoring surface (model + the four projection halves that must agree
/// pairwise: editor <c>NodePinSchema</c> ↔ compiler <c>Stage0_Rehydrate</c>) shipped ahead of the
/// Stage 2.5 expansion pass (BP-81). This suite locks:
/// <list type="number">
/// <item>the model round-trips through JSON (<see cref="ExecOutDecl"/> as real content, not
/// <c>{}</c> — the trap its properties-not-fields shape exists to avoid);</item>
/// <item>adding the model is additive on disk for assets that never see it;</item>
/// <item>the four projections agree, pairwise, on every shape the design calls out: the Macro
/// entry boundary, the Macro return boundary (both N&gt;0 and the N=0 degenerate), and a
/// <see cref="MacroCallNode"/> call site (both resolved and unresolved);</item>
/// <item>the fail-loud net: an unexpanded <see cref="MacroCallNode"/> that reaches Stage 5 is a
/// hard <c>BP1668</c> Error, not the silent <c>BP4004</c> warning-and-skip a macro call would
/// otherwise fall into — and a Macro graph that is merely DECLARED (no call site) stays
/// compilable, because the fail-loud rule is about an unexpanded CALL, not about the graph kind.</item>
/// </list>
/// </summary>
public sealed class MacroSurfaceTests
{
    private static CompileOptions DefaultOptions() => new(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static ParameterDecl Param(string name, string typeId) => new()
    {
        Id   = Guid.NewGuid(),
        Name = name,
        Type = new BlueprintTypeRef { TypeId = typeId },
    };

    private static BlueprintAsset MakeAsset(params Graph[] graphs) => new()
    {
        AssetId          = Guid.NewGuid(),
        Name             = "MacroSurfaceTestAsset",
        Dispatch         = BlueprintDispatchKind.Instance,
        Parameters       = new(),
        WorkingState     = new(),
        Variables        = new(),
        EventDispatchers = new(),
        CustomEvents     = new(),
        CallablePeers    = new(),
        Graphs           = new List<Graph>(graphs),
        Header           = new Header(),
    };

    /// <summary>(Name, Direction, IsExec, TypeId) tuples, in order — the same comparison shape
    /// <c>GetComponentPinParityTests</c> uses, minus <c>IsArray</c> (no macro projection sets it).</summary>
    private static List<(string Name, string Direction, bool IsExec, string? TypeId)> PinShape(
        IEnumerable<Pin> pins)
        => pins.Select(p => (p.Name, p.Direction, p.IsExec, p.TypeRef?.TypeId)).ToList();

    // ═════════════════════════════════════════════════════════════════════════
    // 1. Round-trip — ExecOutDecl + MacroCallNode survive Serialize → Deserialize
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A Macro graph with two <see cref="ExecOutDecl"/>s (one with a Tooltip) plus a
    /// <see cref="MacroCallNode"/> targeting it comes back byte-equal in the fields that matter.
    /// ⭐ Also asserts the decl survives as REAL json (contains its Name), not <c>{}</c> — the exact
    /// trap <see cref="ExecOutDecl"/>'s doc comment calls out: a field-only shape would serialize to
    /// <c>{}</c> under <c>System.Text.Json</c> (no <c>IncludeFields</c>) and every declared exec-out
    /// would silently vanish on save/reload.
    /// </summary>
    [Fact]
    public void MacroGraph_ExecOutputsAndMacroCallNode_RoundTripThroughSerialization()
    {
        var execThen      = new ExecOutDecl { Id = Guid.NewGuid(), Name = "Then" };
        var execCompleted = new ExecOutDecl { Id = Guid.NewGuid(), Name = "Completed", Tooltip = "Fires once the macro body finishes" };

        var macroGraphId = Guid.NewGuid();
        var macroGraph = new Graph
        {
            Id          = macroGraphId,
            Name        = "MyMacro",
            Kind        = GraphKind.Macro,
            Inputs      = new(),
            Outputs     = new(),
            ExecOutputs = new List<ExecOutDecl> { execThen, execCompleted },
            Nodes       = new(),
            Links       = new(),
        };

        var callNode = new MacroCallNode { Id = Guid.NewGuid(), TargetGraphId = macroGraphId.ToString() };
        var callerGraph = new Graph
        {
            Id      = Guid.NewGuid(),
            Name    = "Caller",
            Kind    = GraphKind.Function,
            Inputs  = new(),
            Outputs = new(),
            Nodes   = new List<Node> { callNode },
            Links   = new(),
        };

        var asset = MakeAsset(macroGraph, callerGraph);

        var json = BlueprintJsonServices.Serialize(asset);

        // ⭐ ExecOutDecl round-trips as real content, not `{}`.
        Assert.Contains("Then", json);
        Assert.Contains("Completed", json);
        Assert.Contains("Fires once the macro body finishes", json);
        Assert.Contains("MacroCall", json);   // the [JsonDerivedType] discriminator

        var reloaded = BlueprintJsonServices.Deserialize(json);
        Assert.NotNull(reloaded);

        var reloadedMacro = reloaded!.Graphs.Single(g => g.Id == macroGraphId);
        Assert.Equal(GraphKind.Macro, reloadedMacro.Kind);
        Assert.Equal(2, reloadedMacro.ExecOutputs.Count);

        Assert.Equal(execThen.Id, reloadedMacro.ExecOutputs[0].Id);
        Assert.Equal("Then",      reloadedMacro.ExecOutputs[0].Name);
        Assert.Null(reloadedMacro.ExecOutputs[0].Tooltip);

        Assert.Equal(execCompleted.Id, reloadedMacro.ExecOutputs[1].Id);
        Assert.Equal("Completed",      reloadedMacro.ExecOutputs[1].Name);
        Assert.Equal("Fires once the macro body finishes", reloadedMacro.ExecOutputs[1].Tooltip);

        var reloadedCaller = reloaded.Graphs.Single(g => g.Id == callerGraph.Id);
        var reloadedCall   = Assert.IsType<MacroCallNode>(Assert.Single(reloadedCaller.Nodes));
        Assert.Equal(macroGraphId.ToString(), reloadedCall.TargetGraphId);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 2. Additive on disk — a non-macro asset is unaffected
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A plain Function graph (no macro anywhere) still round-trips exactly as before: Kind, Name
    /// and structure survive, and the new (empty) <c>ExecOutputs</c> list does not disturb anything
    /// pre-existing. This is what "additive on disk" means concretely — an asset that never opts
    /// into the macro surface needs no migration.
    /// </summary>
    [Fact]
    public void NonMacroAsset_EmptyExecOutputs_StillRoundTripsUnaffected()
    {
        var graph = new Graph
        {
            Id      = Guid.NewGuid(),
            Name    = "Ordinary",
            Kind    = GraphKind.Function,
            Inputs  = new(),
            Outputs = new List<ParameterDecl> { Param("Result", "System.Int32") },
            Nodes   = new List<Node> { new EventEntryNode { Id = Guid.NewGuid() } },
            Links   = new(),
        };
        var asset = MakeAsset(graph);

        var json     = BlueprintJsonServices.Serialize(asset);
        var reloaded = BlueprintJsonServices.Deserialize(json);

        Assert.NotNull(reloaded);
        var reloadedGraph = reloaded!.Graphs.Single();
        Assert.Equal(GraphKind.Function, reloadedGraph.Kind);
        Assert.Equal("Ordinary",         reloadedGraph.Name);
        Assert.Empty(reloadedGraph.ExecOutputs);
        Assert.Single(reloadedGraph.Outputs);
        Assert.Equal("Result", reloadedGraph.Outputs[0].Name);
        Assert.IsType<EventEntryNode>(Assert.Single(reloadedGraph.Nodes));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 3. Entry projection parity — EventEntryNode on a Macro graph, 2 declared Inputs
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The Macro entry boundary (F3: reuses <see cref="EventEntryNode"/> unchanged) projects
    /// IDENTICALLY on both halves: exec-Out "Out" + one data-Out pin per declared <c>Graph.Inputs</c>
    /// entry, in declaration order.
    /// </summary>
    // ── BP-74 / Q26-A3: N exec-ins, the entry side ──────────────────────────

    private static Graph BuildMacroWithEntries(Node entry, params string[] entryNames) => new()
    {
        Id      = Guid.NewGuid(),
        Name    = "MyMacro",
        Kind    = GraphKind.Macro,
        Inputs  = new(),
        Outputs = new(),
        ExecInputs = entryNames
            .Select(n => new ExecInDecl { Id = Guid.NewGuid(), Name = n })
            .ToList(),
        Nodes   = new List<Node> { entry },
        Links   = new(),
    };

    /// <summary>
    /// ⭐ Q26-A3: the macro entry node projects one exec-OUT per declared entry, replacing the single
    /// "Out". Both halves must agree — every batch that moved one and not the other produced a silent
    /// shape mismatch.
    /// </summary>
    [Theory]
    [InlineData(0)]   // no declaration ⇒ today's single implicit entry
    [InlineData(1)]
    [InlineData(3)]
    public void MacroEntry_NExecInputs_EditorAndCompilerProjection_Agree(int entryCount)
    {
        var names = Enumerable.Range(0, entryCount).Select(i => $"Enter{i}").ToArray();

        var stage0Entry = new EventEntryNode { Id = Guid.NewGuid() };
        var stage0Asset = MakeAsset(BuildMacroWithEntries(stage0Entry, names));
        Stage0_Rehydrate.Run(stage0Asset, DefaultOptions());
        var fromStage0 = PinShape(stage0Entry.Pins);

        var editorEntry = new EventEntryNode { Id = Guid.NewGuid() };
        var editorGraph = BuildMacroWithEntries(editorEntry, names);
        var fromEditor  = PinShape(NodePinSchema.GetCanonicalPins(editorEntry, containingGraph: editorGraph));

        Assert.Equal(fromStage0, fromEditor);

        var expectedNames = entryCount == 0 ? new[] { "Out" } : names;
        Assert.Equal(expectedNames, fromEditor.Select(t => t.Item1).ToArray());
        Assert.All(fromEditor, t => Assert.Equal("Out", t.Item2));
        Assert.All(fromEditor, t => Assert.True(t.Item3));
    }

    /// <summary>The call node's mirror: one exec-IN per the TARGET's declared entries.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void MacroCall_NExecInputs_EditorAndCompilerProjection_Agree(int entryCount)
    {
        var names = Enumerable.Range(0, entryCount).Select(i => $"Enter{i}").ToArray();

        Graph BuildTarget() => BuildMacroWithEntries(new EventEntryNode { Id = Guid.NewGuid() }, names);

        var s0Target = BuildTarget();
        var s0Call   = new MacroCallNode { Id = Guid.NewGuid(), TargetGraphId = s0Target.Id.ToString() };
        var s0Host   = new Graph { Id = Guid.NewGuid(), Name = "Host", Kind = GraphKind.Function,
                                   Nodes = new List<Node> { s0Call } };
        var s0Asset  = MakeAsset(s0Target, s0Host);
        Stage0_Rehydrate.Run(s0Asset, DefaultOptions());
        var fromStage0 = PinShape(s0Call.Pins);

        var edTarget = BuildTarget();
        var edCall   = new MacroCallNode { Id = Guid.NewGuid(), TargetGraphId = edTarget.Id.ToString() };
        var edHost   = new Graph { Id = Guid.NewGuid(), Name = "Host", Kind = GraphKind.Function,
                                   Nodes = new List<Node> { edCall } };
        var edAsset  = MakeAsset(edTarget, edHost);
        var fromEditor = PinShape(
            NodePinSchema.GetCanonicalPins(edCall, asset: edAsset, containingGraph: edHost));

        Assert.Equal(fromStage0, fromEditor);

        var expectedNames = entryCount == 0 ? new[] { "In" } : names;
        var actualExecIns = fromEditor.Where(t => t.Item3 && t.Item2 == "In").ToArray();
        Assert.Equal(expectedNames, actualExecIns.Select(t => t.Item1).ToArray());
    }

    /// <summary>
    /// ⚠⚠ <b>Additive, but NOT byte-identical — and the distinction is worth stating.</b>
    ///
    /// <para>
    /// <c>ExecInputs</c> is a non-nullable list defaulting to <c>new()</c>, so it serialises as
    /// <c>"ExecInputs":[]</c> on every graph — exactly as <c>ExecOutputs</c> has since Batch 29. A
    /// re-saved asset therefore gains a field. What actually matters, and is asserted here, is that the
    /// change is <b>semantically inert and compatible in both directions</b>: an asset with no declared
    /// entries reloads with an empty list, and — the load-bearing half — JSON written BEFORE this field
    /// existed still loads, because System.Text.Json ignores unknown/missing members.
    /// </para>
    /// </summary>
    [Fact]
    public void ExecInputs_IsSemanticallyInert_AndOlderJsonWithoutItStillLoads()
    {
        var asset = MakeAsset(new Graph
        {
            Id = Guid.NewGuid(), Name = "Plain", Kind = GraphKind.Function,
            Nodes = new List<Node> { new ReturnNode { Id = Guid.NewGuid() } },
        });

        var json     = BlueprintJsonServices.Serialize(asset);
        var reloaded = BlueprintJsonServices.Deserialize(json)!;
        Assert.Empty(reloaded.Graphs.Single().ExecInputs);

        // A pre-Q26 document: the member simply is not there.
        // ⚠ U-15: removed from the DOM, not by string surgery on the compact spelling — the canonical
        //   on-disk form is now indented, and `"ExecInputs":[],` no longer occurs in it. A test that
        //   deletes a property by matching its whitespace deletes nothing the day formatting changes,
        //   and then asserts happily about a document it did not modify.
        var legacyDom = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        foreach (var graph in legacyDom["Graphs"]!.AsArray())
            graph!.AsObject().Remove("ExecInputs");
        var legacy = legacyDom.ToJsonString();
        Assert.DoesNotContain("\"ExecInputs\"", legacy);

        var fromLegacy = BlueprintJsonServices.Deserialize(legacy)!;
        Assert.Empty(fromLegacy.Graphs.Single().ExecInputs);
        Assert.Equal(asset.Graphs.Single().Id, fromLegacy.Graphs.Single().Id);
    }

    /// <summary>And when they ARE declared, they survive as real content — the fields-vs-properties trap.</summary>
    [Fact]
    public void ExecInputs_RoundTripAsRealContent_WhenDeclared()
    {
        var decl  = new ExecInDecl { Id = Guid.NewGuid(), Name = "EnterA", Tooltip = "the first door" };
        var asset = MakeAsset(new Graph
        {
            Id = Guid.NewGuid(), Name = "M", Kind = GraphKind.Macro,
            ExecInputs = new List<ExecInDecl> { decl },
            Nodes = new List<Node> { new ReturnNode { Id = Guid.NewGuid() } },
        });

        var json = BlueprintJsonServices.Serialize(asset);
        Assert.Contains("EnterA", json);
        Assert.Contains("the first door", json);

        var back = BlueprintJsonServices.Deserialize(json)!.Graphs.Single().ExecInputs.Single();
        Assert.Equal(decl.Id, back.Id);
        Assert.Equal("EnterA", back.Name);
        Assert.Equal("the first door", back.Tooltip);
    }

    [Fact]
    public void MacroEntry_TwoInputs_EditorAndCompilerProjection_Agree()
    {
        Graph BuildGraph(Node entry) => new()
        {
            Id      = Guid.NewGuid(),
            Name    = "MyMacro",
            Kind    = GraphKind.Macro,
            Inputs  = new List<ParameterDecl> { Param("A", "System.Int32"), Param("B", "System.String") },
            Outputs = new(),
            Nodes   = new List<Node> { entry },
            Links   = new(),
        };

        var stage0Entry = new EventEntryNode { Id = Guid.NewGuid() };
        var stage0Asset = MakeAsset(BuildGraph(stage0Entry));
        Stage0_Rehydrate.Run(stage0Asset, DefaultOptions());
        var fromStage0 = PinShape(stage0Entry.Pins);

        var editorEntry = new EventEntryNode { Id = Guid.NewGuid() };
        var editorGraph = BuildGraph(editorEntry);
        var fromEditor  = PinShape(NodePinSchema.GetCanonicalPins(editorEntry, containingGraph: editorGraph));

        Assert.Equal(fromStage0, fromEditor);
        Assert.Equal(new[]
        {
            ("Out", "Out", true,  (string?)""),
            ("A",   "Out", false, (string?)"System.Int32"),
            ("B",   "Out", false, (string?)"System.String"),
        }, fromEditor);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 4. Return projection — N=2 ExecOutputs
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The Macro return boundary with two declared <c>ExecOutputs</c> ("Then", "Completed") and one
    /// declared Output ("Result"): both halves project exec-IN pins named "Then" then "Completed"
    /// (declaration order — <c>Stage2_5_ExpandMacros</c>' splice rule 2 pairs them positionally), NO
    /// pin named "In", and a single data-IN "Result".
    /// </summary>
    [Fact]
    public void MacroReturn_TwoExecOutputs_ProjectsNamedExecInsInOrder_NoLegacyInPin()
    {
        Graph BuildGraph(Node ret) => new()
        {
            Id      = Guid.NewGuid(),
            Name    = "MyMacro",
            Kind    = GraphKind.Macro,
            Inputs  = new(),
            Outputs = new List<ParameterDecl> { Param("Result", "System.Int32") },
            ExecOutputs = new List<ExecOutDecl>
            {
                new() { Id = Guid.NewGuid(), Name = "Then" },
                new() { Id = Guid.NewGuid(), Name = "Completed" },
            },
            Nodes = new List<Node> { ret },
            Links = new(),
        };

        var stage0Ret   = new ReturnNode { Id = Guid.NewGuid() };
        var stage0Graph = BuildGraph(stage0Ret);
        var stage0Asset = MakeAsset(stage0Graph);
        Stage0_Rehydrate.Run(stage0Asset, DefaultOptions());
        var fromStage0 = PinShape(stage0Ret.Pins);

        var editorRet   = new ReturnNode { Id = Guid.NewGuid() };
        var editorGraph = BuildGraph(editorRet);
        var fromEditor  = PinShape(NodePinSchema.GetCanonicalPins(editorRet, containingGraph: editorGraph));

        Assert.Equal(fromStage0, fromEditor);
        Assert.Equal(new[]
        {
            ("Then",      "In", true,  (string?)""),
            ("Completed", "In", true,  (string?)""),
            ("Result",    "In", false, (string?)"System.Int32"),
        }, fromEditor);

        Assert.DoesNotContain(fromEditor, p => p.Name == "In" && p.IsExec);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 5. Return projection — N=0 degenerate
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A freshly-created Macro graph with EMPTY <c>ExecOutputs</c> keeps the single "In" exec pin on
    /// BOTH halves — deliberate: the degenerate N=0 case (a macro whose body simply ends, offering no
    /// continuation) is legitimate under Q25-D3's "N ≥ 0", and a macro with no declared exec-outs at
    /// all must still be wireable the moment it is created.
    /// </summary>
    [Fact]
    public void MacroReturn_ZeroExecOutputs_KeepsSingleInPin_BothHalvesAgree()
    {
        Graph BuildGraph(Node ret) => new()
        {
            Id          = Guid.NewGuid(),
            Name        = "EmptyMacro",
            Kind        = GraphKind.Macro,
            Inputs      = new(),
            Outputs     = new(),
            ExecOutputs = new(),
            Nodes       = new List<Node> { ret },
            Links       = new(),
        };

        var stage0Ret   = new ReturnNode { Id = Guid.NewGuid() };
        var stage0Asset = MakeAsset(BuildGraph(stage0Ret));
        Stage0_Rehydrate.Run(stage0Asset, DefaultOptions());
        var fromStage0 = PinShape(stage0Ret.Pins);

        var editorRet   = new ReturnNode { Id = Guid.NewGuid() };
        var editorGraph = BuildGraph(editorRet);
        var fromEditor  = PinShape(NodePinSchema.GetCanonicalPins(editorRet, containingGraph: editorGraph));

        Assert.Equal(fromStage0, fromEditor);
        Assert.Equal(new[] { ("In", "In", true, (string?)"") }, fromEditor);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 6. MacroCall projection parity
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A <see cref="MacroCallNode"/> targeting a resolvable macro (ExecOutputs=[Then,Completed],
    /// Inputs=[X], Outputs=[Result]) projects, on BOTH halves and in this exact order: exec-In "In";
    /// exec-Out "Then"; exec-Out "Completed"; data-In per Input; data-Out per Output — mirroring
    /// <c>FunctionGraphCallPins</c>/<c>EnrichFunctionGraphCallPins</c>'s established shape.
    /// </summary>
    [Fact]
    public void MacroCall_ResolvedTarget_EditorAndCompilerProjection_Agree()
    {
        var macroGraphId = Guid.NewGuid();
        var macroGraph = new Graph
        {
            Id      = macroGraphId,
            Name    = "Target",
            Kind    = GraphKind.Macro,
            Inputs  = new List<ParameterDecl> { Param("X", "System.Int32") },
            Outputs = new List<ParameterDecl> { Param("Result", "System.Int32") },
            ExecOutputs = new List<ExecOutDecl>
            {
                new() { Id = Guid.NewGuid(), Name = "Then" },
                new() { Id = Guid.NewGuid(), Name = "Completed" },
            },
            Nodes = new(),
            Links = new(),
        };

        MacroCallNode BuildCall() => new() { Id = Guid.NewGuid(), TargetGraphId = macroGraphId.ToString() };

        var stage0Call  = BuildCall();
        var stage0Asset = MakeAsset(macroGraph,
            new Graph { Id = Guid.NewGuid(), Name = "Caller", Kind = GraphKind.Function, Nodes = new List<Node> { stage0Call }, Inputs = new(), Outputs = new(), Links = new() });
        Stage0_Rehydrate.Run(stage0Asset, DefaultOptions());
        var fromStage0 = PinShape(stage0Call.Pins);

        var editorCall  = BuildCall();
        var editorAsset = MakeAsset(macroGraph,
            new Graph { Id = Guid.NewGuid(), Name = "Caller2", Kind = GraphKind.Function, Nodes = new List<Node> { editorCall }, Inputs = new(), Outputs = new(), Links = new() });
        var fromEditor  = PinShape(NodePinSchema.GetCanonicalPins(editorCall, asset: editorAsset));

        Assert.Equal(fromStage0, fromEditor);
        Assert.Equal(new[]
        {
            ("In",        "In",  true,  (string?)""),
            ("Then",      "Out", true,  (string?)""),
            ("Completed", "Out", true,  (string?)""),
            ("X",         "In",  false, (string?)"System.Int32"),
            ("Result",    "Out", false, (string?)"System.Int32"),
        }, fromEditor);
    }

    /// <summary>
    /// An unresolvable <c>TargetGraphId</c> (no matching Macro graph in the asset) falls back to
    /// plain exec-only In/Out on BOTH halves, WITHOUT throwing — mirrors
    /// <c>FunctionCallPinsDispatch</c>'s graceful fallback. Reporting a genuinely bad target is
    /// BP-82's job (BP1660), not this projection's.
    /// </summary>
    [Fact]
    public void MacroCall_UnresolvedTarget_FallsBackToExecOnly_NoThrow()
    {
        MacroCallNode BuildCall() => new() { Id = Guid.NewGuid(), TargetGraphId = Guid.NewGuid().ToString() };

        Graph BuildCallerGraph(Node call) => new()
        {
            Id = Guid.NewGuid(), Name = "Caller", Kind = GraphKind.Function,
            Inputs = new(), Outputs = new(), Nodes = new List<Node> { call }, Links = new(),
        };

        var stage0Call  = BuildCall();
        var stage0Asset = MakeAsset(BuildCallerGraph(stage0Call));   // no Macro graph anywhere

        var stage0Ex = Record.Exception(() => Stage0_Rehydrate.Run(stage0Asset, DefaultOptions()));
        Assert.Null(stage0Ex);
        var fromStage0 = PinShape(stage0Call.Pins);

        var editorCall  = BuildCall();
        var editorAsset = MakeAsset(BuildCallerGraph(editorCall));

        IReadOnlyList<Pin>? editorPins = null;
        var editorEx = Record.Exception(() =>
            editorPins = NodePinSchema.GetCanonicalPins(editorCall, asset: editorAsset));
        Assert.Null(editorEx);
        var fromEditor = PinShape(editorPins!);

        Assert.Equal(fromStage0, fromEditor);
        Assert.Equal(new[]
        {
            ("In",  "In",  true, (string?)""),
            ("Out", "Out", true, (string?)""),
        }, fromEditor);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 7. BP1668 fail-loud — an unexpanded macro call reaching Stage 5 is an Error
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// EventEntry → MacroCallNode → Return, wired via exec, inside an ordinary Function graph
    /// ("Tick"). Because <c>Stage2_5_ExpandMacros</c> (BP-81) does not exist yet, this call is never
    /// spliced out, so it reaches Stage 5 exactly as authored and must hit its own <c>BP1668</c> arm.
    /// </summary>
    private static BlueprintAsset MakeUnexpandedMacroCallAsset()
    {
        var call    = new MacroCallNode { Id = Guid.NewGuid(), TargetGraphId = Guid.NewGuid().ToString() };
        var callIn  = new Pin { Id = Guid.NewGuid(), Name = "In",  Direction = "In",  IsExec = true, TypeRef = new() };
        var callOut = new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true, TypeRef = new() };
        call.Pins.AddRange(new[] { callIn, callOut });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() };
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Tick",
            Kind  = GraphKind.Function,
            Nodes = { entry, call, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id, FromPinId = entryOut.Id, ToNodeId = call.Id, ToPinId = callIn.Id },
                new Link { FromNodeId = call.Id,  FromPinId = callOut.Id,  ToNodeId = ret.Id,   ToPinId = retIn.Id },
            },
        };

        return MakeAsset(graph);
    }

    [Fact]
    [CoversDiagnosticCode("BP1668")]
    public void UnexpandedMacroCall_ReachingStage5_EmitsBP1668Error()
    {
        // ⭐ REWRITTEN in Batch 30, and the reason matters. This used to drive the FULL compiler:
        // before BP-81 there was no expansion pass, so a MacroCallNode reached Stage 5 simply by
        // compiling the asset. Now Stage 2.5 splices every call away, so the full pipeline can no
        // longer produce the condition BP1668 describes -- which is the point of BP1668, not a
        // reason to delete it. It is the last-ditch net for a call that SURVIVED expansion, so the
        // test now drives Stage 5 directly, which is the only way that state can still arise.
        var (ir, sink) = RunScheduleDirectly(MakeUnexpandedMacroCallAsset());
        _ = ir;

        var bp1668 = Assert.Single(sink.All, d => d.Code == DiagnosticCodes.BP1668);
        Assert.Equal(DiagnosticSeverity.Error, bp1668.Severity);
    }

    /// <summary>
    /// The complement, and the reason the rails had to land in the same batch as the pass: driven
    /// through the FULL compiler, the same asset never reaches Stage 5 at all. Its dangling
    /// TargetGraphId is rejected by <c>BP1660</c> at Stage 2, before expansion — so the designer gets
    /// "this call does not resolve", not "something survived expansion".
    /// </summary>
    [Fact]
    [CoversDiagnosticCode("BP1660")]
    public void MacroCallWithDanglingTarget_IsRejectedByBP1660_BeforeExpansion()
    {
        var result = new BlueprintCompiler().Compile(MakeUnexpandedMacroCallAsset(), DefaultOptions());

        Assert.False(result.Succeeded);
        var bp1660 = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCodes.BP1660);
        Assert.Equal(DiagnosticSeverity.Error, bp1660.Severity);

        // And it stops there: expansion never ran, so BP1668 has nothing to report.
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCodes.BP1668);
    }

    /// <summary>Stage 5 in isolation — mirrors <c>BPC_ImplicitReturnTests.RunSchedule</c>.</summary>
    private static (Hrot.Blueprints.Core.Compiler.Ir.IrAsset ir, DiagnosticSink sink)
        RunScheduleDirectly(BlueprintAsset asset)
    {
        var sink  = new DiagnosticSink();
        var ctx   = new ValidationContext(sink, DefaultOptions());
        var typed = new TypedAsset(
            asset,
            PinTypes:   new Dictionary<Guid, Hrot.Blueprints.Core.Compiler.Ir.IrTypeRef>(),
            FieldTypes: new Dictionary<Guid, Hrot.Blueprints.Core.Compiler.Ir.IrTypeRef>());
        return (Stage5_Schedule.Run(typed, ctx), sink);
    }

    /// <summary>
    /// ⭐ The negative that makes the net meaningful: before the dedicated Stage5 arm, a
    /// <see cref="MacroCallNode"/> fell into the generic "unknown impure node kind" default branch,
    /// which emits <c>BP4004</c> -- a WARNING that emits no IR and lets the exec chain walk on. That
    /// would let the call silently vanish from any consumer without <c>TreatWarningsAsErrors</c>.
    /// Asserting BP4004's ABSENCE here is what proves the dedicated arm actually intercepts the node
    /// before the default branch ever sees it.
    /// </summary>
    [Fact]
    public void UnexpandedMacroCall_DoesNotAlsoEmitBP4004_TheWarningItWouldHaveFallenInto()
    {
        // Same Stage-5-direct drive as above, for the same reason.
        var (_, sink) = RunScheduleDirectly(MakeUnexpandedMacroCallAsset());

        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP4004);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 8. A Macro graph is not itself a compilation target
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// An asset with a normal Function graph AND a Macro graph (no call site anywhere) compiles
    /// WITHOUT any BP1668 -- the macro graph itself is simply SKIPPED by Stage 5 (pre-existing
    /// behavior), and BP1668 fires only for an unexpanded CALL surviving to Stage 5, never for the
    /// mere presence of a Macro graph. This locks the wording distinction
    /// (DiagnosticCodes.BP1668's doc comment) that keeps a future macro-library asset -- one that
    /// only DECLARES macros, with no call sites of its own -- compilable.
    /// </summary>
    [Fact]
    public void AssetWithDeclaredMacroGraph_AndNoCallSite_CompilesCleanly_NoBP1668()
    {
        var entryId = Guid.NewGuid();
        var entryOut = Guid.NewGuid();
        var retId = Guid.NewGuid();
        var retIn = Guid.NewGuid();

        var functionGraph = new Graph
        {
            Id = Guid.NewGuid(), Name = "Main", Kind = GraphKind.Function,
            Inputs = new(), Outputs = new(),
            Nodes = new List<Node>
            {
                new EventEntryNode { Id = entryId, Pins = new() { new Pin { Id = entryOut, Name = "Out", Direction = "Out", IsExec = true, TypeRef = new() } } },
                new ReturnNode     { Id = retId,   Pins = new() { new Pin { Id = retIn,    Name = "In",  Direction = "In",  IsExec = true, TypeRef = new() } } },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = entryOut, ToNodeId = retId, ToPinId = retIn },
            },
        };

        var macroGraph = new Graph
        {
            Id = Guid.NewGuid(), Name = "UnusedMacro", Kind = GraphKind.Macro,
            Inputs = new(), Outputs = new(), ExecOutputs = new(), Nodes = new(), Links = new(),
        };

        var asset = MakeAsset(functionGraph, macroGraph);

        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());

        Assert.True(result.Succeeded,
            "Compile failed: " + string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCodes.BP1668);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 9. BP1655 admits Macro
    // ═════════════════════════════════════════════════════════════════════════

    private static IReadOnlyList<Diagnostic> RunStage2(BlueprintAsset asset)
    {
        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, DefaultOptions());
        Stage2_Validate.Run(asset, ctx);
        return sink.All;
    }

    /// <summary>
    /// A <see cref="GraphKind.Macro"/> graph that declares an output whose Return node has nothing
    /// wired into its value pin emits <see cref="DiagnosticCodes.BP1655"/> — mirrors
    /// <c>BP71_FunctionReturnValueTests.BP1655_UnwiredReturnValue_IsAnError</c>, retargeted at Macro:
    /// <c>V_FunctionGraphReturnValue</c> admits Macro DELIBERATELY (not by omission) because a macro
    /// reuses <see cref="ReturnNode"/> as its output boundary, so "declares an output but nothing is
    /// wired into it" is the identical defect with the identical consequence.
    /// </summary>
    [Fact]
    [CoversDiagnosticCode("BP1655")]
    public void MacroGraph_UnwiredReturnValue_IsAnError()
    {
        var entryId = Guid.NewGuid();
        var entryOut = Guid.NewGuid();
        var retId = Guid.NewGuid();
        var retExecIn = Guid.NewGuid();
        var retValue = Guid.NewGuid();

        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "UnwiredMacro", Kind = GraphKind.Macro,
            Inputs = new(), Outputs = new List<ParameterDecl> { Param("Result", "System.Int32") },
            ExecOutputs = new(),   // N=0: Return keeps the single "In" exec pin.
            Nodes = new List<Node>
            {
                new EventEntryNode { Id = entryId, Pins = new() { new Pin { Id = entryOut, Name = "Out", Direction = "Out", IsExec = true, TypeRef = new() } } },
                new ReturnNode
                {
                    Id = retId,
                    Pins = new()
                    {
                        new Pin { Id = retExecIn, Name = "In",     Direction = "In", IsExec = true,  TypeRef = new() },
                        new Pin { Id = retValue,  Name = "Result", Direction = "In", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },
                    },
                },
            },
            // The exec wire is present (an authored, non-stub graph); the VALUE pin is deliberately
            // left unwired -- that is the defect under test.
            Links = new List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = entryOut, ToNodeId = retId, ToPinId = retExecIn },
            },
        };
        var asset = MakeAsset(graph);

        var diags = RunStage2(asset);

        var d = Assert.Single(diags, x => x.Code == DiagnosticCodes.BP1655);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains("Macro", d.Message);
        Assert.Contains("Result", d.Message);
    }

    /// <summary>Negative control: the identical Macro graph, but the value pin IS wired -- no BP1655.</summary>
    [Fact]
    public void MacroGraph_WiredReturnValue_DoesNotEmitBP1655()
    {
        var entryId = Guid.NewGuid();
        var entryOut = Guid.NewGuid();
        var litId = Guid.NewGuid();
        var litOut = Guid.NewGuid();
        var retId = Guid.NewGuid();
        var retExecIn = Guid.NewGuid();
        var retValue = Guid.NewGuid();

        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "WiredMacro", Kind = GraphKind.Macro,
            Inputs = new(), Outputs = new List<ParameterDecl> { Param("Result", "System.Int32") },
            ExecOutputs = new(),
            Nodes = new List<Node>
            {
                new EventEntryNode { Id = entryId, Pins = new() { new Pin { Id = entryOut, Name = "Out", Direction = "Out", IsExec = true, TypeRef = new() } } },
                new LiteralNode
                {
                    Id = litId, TypeId = "System.Int32", ValueJson = "3",
                    Pins = new() { new Pin { Id = litOut, Name = "value", Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } } },
                },
                new ReturnNode
                {
                    Id = retId,
                    Pins = new()
                    {
                        new Pin { Id = retExecIn, Name = "In",     Direction = "In", IsExec = true,  TypeRef = new() },
                        new Pin { Id = retValue,  Name = "Result", Direction = "In", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },
                    },
                },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = entryOut, ToNodeId = retId, ToPinId = retExecIn },
                new() { FromNodeId = litId,   FromPinId = litOut,   ToNodeId = retId, ToPinId = retValue },
            },
        };
        var asset = MakeAsset(graph);

        Assert.Empty(RunStage2(asset).Where(x => x.Code == DiagnosticCodes.BP1655));
    }
}
