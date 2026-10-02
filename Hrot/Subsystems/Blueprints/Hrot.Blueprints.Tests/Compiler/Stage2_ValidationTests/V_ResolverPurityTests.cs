using System.Reflection;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// ⭐⭐⭐ <b><c>Q43-C1</c> — the rails for <c>V_ResolverPurity</c>.</b>
/// 📄 <c>Architect_Question_43</c> §5 <c>Q43-C</c>.
///
/// <para>
/// ⭐⭐ <b><see cref="EveryNodeKind_IsClassifiedAsPureOrSideEffecting"/> is the load-bearing one.</b>
/// <c>Q43-C3</c> rejected a whitelist because it ROTS — every new pure node would be blocked until
/// someone remembered the list. ⛔ But a bare deny-list rots the OTHER way: a new SIDE-EFFECTING node
/// is silently allowed. ⇒ the completeness rail is what makes the deny-list safe, and it is the reason
/// <c>C1</c> beat <c>C2</c>/<c>C3</c> rather than being a third flavour of the same problem.
/// </para>
/// </summary>
public sealed class V_ResolverPurityTests
{
    // ---- helpers --------------------------------------------------------

    private static IReadOnlyList<Diagnostic> Validate(BlueprintAsset asset)
    {
        var sink = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink, new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>())));
        return sink.All;
    }

    private const string Dto = "global::Hrot.AI.Behaviors.Brains.CgfNodes.MoveToLocationParams";

    // ---- BP1675 — purity ------------------------------------------------
    //  ⛔ CE-448: the reusable Library resolver is retired, so every purity rail runs on the ONE kind left — a
    //  behaviour resolver asset (SubjectAsset, below). Its exemption is narrow: a write to its OWN block.

    [Fact]
    [CoversDiagnosticCode("BP1675")]
    public void Resolver_WithASideEffectingNode_EmitsBP1675()
    {
        // ⚠ A ChannelCommand dispatches out of the graph — the REAL hazard: Q43 §4, an effect outside the block
        //   escapes the ingress's shadow parse and survives a FAILED parse, leaving the entity half-switched.
        var asset = SubjectAsset(g => g.Entry().Return());
        asset.Graphs.Single().Nodes.Add(new ChannelCommandNode { Id = Guid.NewGuid() });

        Assert.Contains(Validate(asset), d => d.Code == DiagnosticCodes.BP1675);
    }

    /// <summary>⭐ <c>CE-433</c> — in a behaviour-resolver subject, <c>Set Variables</c> writes its block: legal.</summary>
    [Fact]
    public void CE433_SetVariables_InABehaviourResolver_IsLegal()
    {
        var asset = SubjectAsset(g => g.Entry().Return());
        asset.Graphs.Single().Nodes.Add(new SetVariablesNode { Id = Guid.NewGuid() });
        Assert.DoesNotContain(Validate(asset), d => d.Code == DiagnosticCodes.BP1675);
    }

    [Fact]
    public void APureResolver_EmitsNoPurityDiagnostic()
    {
        var diags = Validate(SubjectAsset(g => g.Entry().Return()));

        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP1675);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP1676);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP1677);
    }

    // ---- BP1676 — a resolver belongs to a behaviour ----------------------

    [Fact]
    [CoversDiagnosticCode("BP1676")]
    public void ConstructionGraph_OnANonLibraryAsset_EmitsBP1676()
    {
        // ⭐ Q43-A2′ warns against SQUATTING on `Construction`: on an Instance asset it would mean
        //   "configure the instance", which nothing consumes — so the graph would compile to a method
        //   nobody calls. ⛔ Refuse it loudly instead of shipping a silent no-op.
        var asset = BlueprintAssetBuilder
            .Instance("I")
            .WithGraph("Tick", g => g.Entry().Return())
            .WithGraph("Setup", GraphKind.Construction, g => g.Entry().Return())
            .Build();

        Assert.Contains(Validate(asset), d => d.Code == DiagnosticCodes.BP1676);
    }

    /// <summary>
    /// ⛔ <c>CE-448</c> — a Library Construction graph with no <c>ResolverSubject</c> (the retired reusable resolver,
    /// shaped exactly as <c>Q43-D</c> once required) is <c>BP1676</c>, and NOT also judged on the old signature.
    /// </summary>
    [Fact]
    public void CE448_AReusableLibraryResolver_EmitsBP1676_AndNoSignatureDiagnostic()
    {
        var asset = BlueprintAssetBuilder
            .Library("R")
            .WithGraph("Resolve", GraphKind.Construction, g =>
            {
                g.WithInput("Dto", Dto).WithOutput("Result", Dto);
                g.Entry().Return();
            })
            .Build();

        var diags = Validate(asset);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1676);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP1677);
    }

    // ---- the completeness rail ------------------------------------------

    /// <summary>
    /// ⭐⭐⭐ <b>Every concrete node kind is classified, so a NEW one cannot be silently allowed.</b>
    ///
    /// <para>
    /// ⛔ This is the rail <c>Q43-C</c> asks for by name: <i>"a DENY-LIST of side-effecting op kinds,
    /// plus a RAIL that every op kind is either on the list or explicitly marked pure"</i> ⇒
    /// <i>"a new op cannot be silently forgotten — the rail fails until someone classifies it."</i>
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>Adding a node kind will redden this, and that is the point.</b> The fix is one line in
    /// either <see cref="V_ResolverPurity.SideEffectingNodeTypes"/> or <see cref="KnownPureNodeTypes"/>
    /// below — ⛔ never a widening of the rail.
    /// </para>
    /// </summary>
    // ---- CE-428 — shape ③, a BEHAVIOUR RESOLVER asset ----------------------------------------------
    //  📄 Architect_Question_76 §12.20. A Library asset with a ResolverSubject: its Variables are the
    //  behaviour's BLOCK, both subjects are injected, and a write to the block is its result.

    private static BlueprintAsset SubjectAsset(Action<GraphBuilder> body, bool withParameter = false)
    {
        var b = BlueprintAssetBuilder.Library("BehResolver")
            .WithVariable("Speed", typeof(float))
            .WithVariable("Doubled", typeof(float))
            .WithGraph("Resolve", GraphKind.Construction, body);
        if (withParameter) b = b.WithParameter("Nope", typeof(float));
        var asset = b.Build();
        asset.ResolverSubject = new ResolverSubjectDecl
        {
            BehaviorName   = "SomeBehaviour",
            BlockTypeId    = "Demo.SomeBehaviour_Block",
            StateVariables = { "Doubled" },
        };
        return asset;
    }

    /// <summary>⭐⭐ <c>CE-428</c> — a behaviour resolver may <c>SetVariable</c> its block (the injected <c>ref</c>
    /// subject, shadowed by the ingress) — no BP1675, and it may declare Variables despite being a Library (no BP1011).
    /// <para>⚠ Inverse-edit red-proof: drop the <c>isSubject</c> exemption in <c>V_ResolverPurity</c> and this reds with BP1675.</para></summary>
    [Fact]
    public void CE428_ABehaviourResolver_MayWriteItsBlock()
    {
        var diags = Validate(SubjectAsset(g => g.Entry().SetVariable("Doubled", "1").Return()));
        Assert.DoesNotContain(diags, d => d.Code is "BP1675" or "BP1011" or "BP1677" or "BP1676");
    }

    /// <summary>⛔ <c>CE-428</c> — the injected subjects are NOT declared: a graph input is BP1677.</summary>
    [Fact]
    [CoversDiagnosticCode("BP1677")]
    public void CE428_ABehaviourResolverGraph_ThatDeclaresAnInput_EmitsBP1677()
    {
        var diags = Validate(SubjectAsset(g => { g.WithInput("Dto", Dto); g.Entry().Return(); }));
        Assert.Contains(diags, d => d.Code == "BP1677");
    }

    /// <summary>⭐ <c>CE-443</c> — a resolver's Parameters declare its AUTHORED input (§P.7): allowed, no BP1011.</summary>
    [Fact]
    public void CE443_ABehaviourResolver_DeclaringAParameter_IsAllowed()
    {
        var diags = Validate(SubjectAsset(g => g.Entry().Return(), withParameter: true));
        Assert.DoesNotContain(diags, d => d.Code == "BP1011");
    }

    /// <summary>⛔ <c>CE-443</c> — the authored input is `in`: writing a Parameter is refused (BP1675).</summary>
    [Fact]
    public void CE443_ABehaviourResolver_WritingAParameter_EmitsBP1675()
    {
        var diags = Validate(SubjectAsset(g => g.Entry().SetVariable("Nope", "1").Return(), withParameter: true));
        Assert.Contains(diags, d => d.Code == "BP1675");
    }

    /// <summary>⛔ <c>CE-428</c> — a write OUTSIDE the block (to a name that is no declared variable) stays refused.</summary>
    [Fact]
    public void CE428_ABehaviourResolver_WritingOutsideItsBlock_StillEmitsBP1675()
    {
        var diags = Validate(SubjectAsset(g => g.Entry().SetVariable("NotABlockField", "1").Return()));
        Assert.Contains(diags, d => d.Code == "BP1675");
    }

    [Fact]
    public void EveryNodeKind_IsClassifiedAsPureOrSideEffecting()
    {
        var all = typeof(Node).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(Node).IsAssignableFrom(t) && t != typeof(Node))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

        var denied = V_ResolverPurity.SideEffectingNodeTypes.ToHashSet();
        var unclassified = all
            .Where(t => !denied.Contains(t) && !KnownPureNodeTypes.Contains(t.Name))
            .Select(t => t.Name)
            .ToList();

        Assert.True(unclassified.Count == 0,
            "These node kinds are classified neither side-effecting nor pure for a resolver graph:\n  "
            + string.Join("\n  ", unclassified)
            + "\n\nAdd each to V_ResolverPurity.SideEffectingNodeTypes (it can write, dispatch, spawn "
            + "or suspend) or to KnownPureNodeTypes in this file (it only computes a value). "
            + "Q43 §4: a side effect in a resolver survives a FAILED parse.");

        // ⭐ And the reverse: a name in the pure list that no longer exists is rot, not safety.
        var stale = KnownPureNodeTypes.Except(all.Select(t => t.Name)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.True(stale.Count == 0,
            "KnownPureNodeTypes names node kinds that no longer exist:\n  " + string.Join("\n  ", stale));
    }

    /// <summary>
    /// ⭐ The PURE half of the partition, by name so the rail reads as a decision rather than as
    /// "whatever is left over". ⚠ Kept as strings deliberately: several of these types are only
    /// reachable through the polymorphic JSON discriminator, and a `typeof` list here would grow an
    /// import for every one of them.
    /// </summary>
    private static readonly IReadOnlySet<string> KnownPureNodeTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        // ── reads and computation ────────────────────────────────────────
        "ArrayGetNode", "ArrayMakeNode", "BinaryOpNode", "BooleanOpNode", "BreakStructNode",
        "CastNode", "CompareNode", "FormatStringNode", "LiteralNode", "MakeStructNode",
        "NotNode", "SetMembersNode",
        "ToJsonNode", "FromJsonNode",   // CE-472: compute a value (FromJson never throws)
        "GetTimeNode",   // CE-470: reads a clock, writes nothing (and BP1679 refuses it in a resolver anyway)
        // ⚠ SetMembersNode is PURE despite the name: it emits `var __t2 = __t0; __t2.F = __t1;`
        //   — a copy-with-changes into a new temp, never a write to anything the caller owns.

        // ── reads of state the resolver is entitled to see ───────────────
        "GetAllParametersNode", "GetAllVariablesNode", "GetParameterNode", "GetVariableNode",
        "GetComponentNode", "ComponentContainsNode", "ComponentFindNode", "ComponentForEachNode",
        "ComponentItemCountNode", "ComponentItemGetNode",
        "ReadEqsResultNode", "ReadRankedResultNode",

        // ── control flow ─────────────────────────────────────────────────
        "BranchNode", "SequenceNode", "FlowForEachNode", "EventEntryNode", "ReturnNode",

        // ── the two judgement calls, both argued in V_ResolverPurity's header ──
        // FunctionCallNode: allowed because the motivating case (geo-authored params) needs the
        //   CLR-method escape hatch; the callee's purity is its registrant's responsibility.
        "FunctionCallNode",
        // PrintStringNode: writes to a log, never to simulation state, so it cannot survive a failed
        //   parse in any way that corrupts an entity — and it is a designer's only debugging
        //   affordance inside a graph that may do nothing else.
        "PrintStringNode",
    };
}
