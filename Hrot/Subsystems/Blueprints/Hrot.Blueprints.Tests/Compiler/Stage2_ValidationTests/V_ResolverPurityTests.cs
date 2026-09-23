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

    /// <summary>A Library asset with one Construction graph shaped exactly as `Q43-D` requires.</summary>
    private static BlueprintAsset ResolverAsset(Action<Graph>? tweak = null)
    {
        var asset = BlueprintAssetBuilder
            .Library("R")
            .WithGraph("Resolve", GraphKind.Construction, g =>
            {
                g.WithInput("Dto", Dto);
                g.Entry().Return();
            })
            .Build();

        var graph = asset.Graphs.Single();
        graph.Outputs.Add(new ParameterDecl
        {
            Id = Guid.NewGuid(), Name = "Result", Type = new BlueprintTypeRef { TypeId = Dto },
        });

        tweak?.Invoke(graph);
        return asset;
    }

    // ---- BP1675 — purity ------------------------------------------------

    [Fact]
    [CoversDiagnosticCode("BP1675")]
    public void Resolver_WithASideEffectingNode_EmitsBP1675()
    {
        // ⚠ A SetVariable is the cheapest witness, and it is also the REAL hazard: Q43 §4 — a write
        //   outside the returned value escapes the ingress's shadow parse and survives a FAILED parse,
        //   leaving the entity half-switched.
        var asset = BlueprintAssetBuilder
            .Library("R")
            .WithGraph("Resolve", GraphKind.Construction, g =>
            {
                g.WithInput("Dto", Dto);
                g.Entry().SetVariable("Whatever", "1").Return();
            })
            .Build();
        asset.Graphs.Single().Outputs.Add(new ParameterDecl
        {
            Id = Guid.NewGuid(), Name = "Result", Type = new BlueprintTypeRef { TypeId = Dto },
        });

        Assert.Contains(Validate(asset), d => d.Code == DiagnosticCodes.BP1675);
    }

    [Fact]
    public void APureResolver_EmitsNoPurityDiagnostic()
    {
        var diags = Validate(ResolverAsset());

        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP1675);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP1676);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP1677);
    }

    // ---- BP1676 — Construction is a Library shape today -----------------

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

    // ---- BP1677 — the resolver signature --------------------------------

    [Fact]
    [CoversDiagnosticCode("BP1677")]
    public void Resolver_WithNoOutput_EmitsBP1677()
    {
        var asset = ResolverAsset(g => g.Outputs.Clear());

        Assert.Contains(Validate(asset), d => d.Code == DiagnosticCodes.BP1677);
    }

    [Fact]
    public void Resolver_WhoseOutputTypeDiffersFromItsInput_EmitsBP1677()
    {
        // ⭐ Q43-D / R-81: a resolver REFINES what bake+overlay produced. A different output type is a
        //   resolver that REPLACES — which silently discards the scenario's JSON override, the exact
        //   defect BP-275 fixed on the generated path.
        var asset = ResolverAsset(g => g.Outputs[0].Type = new BlueprintTypeRef { TypeId = "System.Int32" });

        Assert.Contains(Validate(asset), d => d.Code == DiagnosticCodes.BP1677);
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
        // ⚠ SetMembersNode is PURE despite the name: it emits `var __t2 = __t0; __t2.F = __t1;`
        //   — a copy-with-changes into a new temp, never a write to anything the caller owns.

        // ── reads of state the resolver is entitled to see ───────────────
        "GetAllParametersNode", "GetParameterNode", "GetVariableNode", "GetSharedNode",
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
