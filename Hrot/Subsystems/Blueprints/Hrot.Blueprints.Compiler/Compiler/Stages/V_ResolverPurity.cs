using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Diagnostics;

namespace Hrot.Blueprints.Core.Compiler.Stages;

/// <summary>
/// ⭐⭐⭐ <b><c>Q43-C1</c> — purity for a blueprint-authored parameter resolver, ENFORCED.</b>
/// 📄 <c>Architect_Question_43</c> §5 <c>Q43-C</c> · <c>Behavior_Parameter_Resolver_Detailed_Design.md</c> §8.1.
///
/// <para>
/// 🔴 <b>Why this is a validator and not a paragraph.</b> <c>Q43</c> §4: a resolver runs inside
/// <c>BehaviorIngressSystem</c>'s <b>shadow parse</b> — the blackboard is shadow-copied, parsed into,
/// and committed only on success, so <i>"a parse failure leaves the entity 100% on its old
/// behaviour."</i> ⛔ <b>A side effect ESCAPES that shadow and survives a failed parse.</b> That is
/// silent corruption, not a lint ⇒ <c>Q43-C2</c> ("document it and trust the author") was rejected.
/// </para>
///
/// <para>
/// ⭐⭐ <b>The shape is a DENY-LIST plus a completeness rail</b>, not a whitelist (<c>Q43-C3</c>
/// rejected: <i>"safer and it ROTS — every new pure node is blocked by default until someone remembers
/// the list"</i>). <see cref="SideEffectingNodeTypes"/> names what a resolver may not contain; the rail
/// <c>V_ResolverPurityTests.EveryNodeKind_IsClassified</c> asserts that <b>every</b> concrete
/// <see cref="Node"/> subclass in the assembly is on one list or the other ⇒ ⭐ a new node kind cannot
/// be silently forgotten: the rail fails until someone classifies it.
/// </para>
///
/// <para>
/// ⚠⚠ <b>TWO HONEST GAPS, stated rather than papered over.</b>
/// <list type="number">
///   <item><b><see cref="FunctionCallNode"/> in its CLR-method mode is ALLOWED</b>, and this validator
///   cannot see inside the callee. ⭐ Refusing it would kill the motivating case: the geo-authored
///   <c>PlatoonHillAttack</c> params need <c>IGeographicTransform.ToCartesian</c>, and §8.2 <c>E3</c>
///   names the CLR-method escape hatch as exactly how a graph reaches such a conversion. ⇒ the purity
///   of a registered CLR method is the responsibility of whoever registered it.</item>
///   <item><b><see cref="MacroCallNode"/> is DENIED</b> for the opposite reason — macros expand at
///   Stage 5, <i>after</i> this Stage-2 check, so an allowed macro could smuggle in any node this list
///   denies. ⛔ Denying it keeps the check honest; a resolver that wants shared logic calls a Function
///   graph, which is checked where it is declared.</item>
/// </list>
/// </para>
/// </summary>
internal sealed class V_ResolverPurity : IValidator
{
    /// <summary>
    /// ⛔ The node kinds a <c>Construction</c> graph may not contain. Everything NOT here is pure by
    /// declaration, and the completeness rail proves the two sets partition the vocabulary.
    /// </summary>
    internal static readonly IReadOnlyList<Type> SideEffectingNodeTypes = new[]
    {
        // ── writes that outlive the shadow parse ──────────────────────────────
        typeof(SetVariableNode),          // writes asset-scope state
        typeof(SetVariablesNode),         // CE-433: the same writes, many at once — same exemption below
        typeof(SetComponentNode),         // writes a component on an entity
        typeof(CollectionWriteNode),      // mutates a component collection
        typeof(ListWriteNode),            // mutates a fixed-capacity list variable

        // ── dispatch: the value leaves this graph and something else acts on it ─
        typeof(ChannelCommandNode),
        typeof(PublishEventNode),
        typeof(SendIntentNode),           // CE-472: publishes AssignTacticalIntentEvent
        typeof(SopOrderNode),             // CE-2083: assigns a behaviour to the unit (SopActions)
        typeof(CallEventDispatcherNode),
        typeof(BindEventDispatcherNode),
        typeof(CallCustomEventNode),
        typeof(CallPeerBlueprintNode),
        typeof(SpawnEqsSensorNode),

        // ── commander / partition mutations ───────────────────────────────────
        typeof(AcquireSlotNode),
        typeof(AssignRolesNode),
        typeof(AdvancePhaseNode),
        typeof(PartitionElementsNode),
        typeof(ScoreDecisionNode),

        // ── latent: a resolver is a plain static method with nowhere to suspend ─
        //    ⚠ Also refused for Library dispatch by BP9001/V_LatentRules. Listed anyway so the
        //    classification is total and the message a designer sees names THE RESOLVER rule.
        typeof(LatentDelayNode),
        typeof(WaitForChannelNode),
        typeof(WaitForEventNode),
        typeof(RunBehaviorNode),          // S5d: runs a child behaviour across frames
        typeof(BehaviorTaskAbortNode),    // S7a: compile-time only (Stage 2.6) — stops a Behaviour Task's child
        typeof(BehaviorTaskStartNode),    // S7b: compile-time only (Stage 2.6) — starts a task fiber
        typeof(WhenNode),

        // ── see gap ② in the class doc ────────────────────────────────────────
        typeof(MacroCallNode),
    };

    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        // ⭐⭐⭐ R-155 — a resolver belongs to a BEHAVIOUR. ⛔ CE-445 retired the own-asset resolver of an
        //   AiPrimitive (actions and conditions read their host live, DESIGN_Parameter_Model §P.4); ⛔ CE-448
        //   retired the REUSABLE Library resolver (a named DTO→DTO graph nothing bound). ONE kind remains:
        //   a behaviour resolver asset (a Library with a ResolverSubject) — the ONE resolver a behaviour names.
        var ownResolverGraphs = new List<Graph>();

        foreach (var graph in asset.Graphs)
        {
            if (graph.Kind != GraphKind.Construction) continue;

            bool isLibrary = asset.Dispatch == BlueprintDispatchKind.Library;
            bool isSubject = isLibrary && asset.ResolverSubject is not null;

            // ── ⭐ CE-446 (Q77 §3 B) — a blueprint BEHAVIOUR's own Construction graph IS its ONE resolver. It reads its
            //   Parameters (the authored input, read-only) and writes its Variables (the block's state). It declares no
            //   inputs/outputs — the block is injected (`ref State s`), exactly like a behaviour resolver asset.
            if (asset.Dispatch == BlueprintDispatchKind.Behavior)
            {
                ownResolverGraphs.Add(graph);
                if (graph.Inputs.Count != 0 || graph.Outputs.Count != 0)
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1677,
                        $"Resolver graph '{graph.Name}' of a blueprint behaviour must declare no inputs and no outputs; read "
                        + "the Parameters with Get Parameter and write the Variables with Set Variable.",
                        asset.AssetId, graph.Id));
                CheckPurity(asset, graph, ctx);
                continue;
            }
            if (!isLibrary)
            {
                ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1676,
                    $"Graph '{graph.Name}' is a Construction graph (a resolver) on a '{asset.Dispatch}' asset. "
                    + "Only behaviours have resolvers (R-155): an action or condition reads its host's variable "
                    + "live and has none. Move the conversion into the behaviour's resolver asset.",
                    asset.AssetId, graph.Id));
                continue;
            }

            // ── BP1676 — ⛔ CE-448: no REUSABLE resolver. A Library Construction graph must belong to a behaviour ─
            if (!isSubject)
            {
                ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1676,
                    $"Graph '{graph.Name}' is a Construction graph (a resolver) on a Library asset that resolves "
                    + "no behaviour. Reusable resolvers are retired (CE-448): a resolver is the ONE optional stage a "
                    + "behaviour names (R-155). Create it as that behaviour's resolver asset, or make this graph a "
                    + "Function graph and call it from one.",
                    asset.AssetId, graph.Id));
                continue;
            }

            ownResolverGraphs.Add(graph);

            // ── BP1677 — the signature: both subjects are injected, nothing is declared ─────
            ValidateSubjectSignature(asset, graph, ctx);

            CheckPurity(asset, graph, ctx);
        }

        // ── BP1676 (second arm) — ONE resolver per params region ──────────────────
        //
        // ⭐ R-149: "one field, one value, so two resolvers for one region cannot be authored." An
        // asset's own parameters are ONE region, so two Construction graphs on it would be exactly
        // the competition the selection model exists to make unrepresentable.
        // ⭐ CE-428 — a behaviour resolver asset exists to hold ONE resolver; none is a resolver nothing runs.
        if (asset.ResolverSubject is not null && asset.Dispatch == BlueprintDispatchKind.Library
            && ownResolverGraphs.Count == 0)
        {
            ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1676,
                $"This asset is a resolver for behaviour '{asset.ResolverSubject.BehaviorName}' but holds no "
                + "Construction graph, so there is nothing to run. Add exactly one.",
                asset.AssetId));
        }

        if (ownResolverGraphs.Count > 1)
        {
            ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1676,
                $"This asset declares {ownResolverGraphs.Count} resolver (Construction) graphs "
                + $"({string.Join(", ", ownResolverGraphs.Select(g => "'" + g.Name + "'"))}), but a behaviour "
                + "has exactly one resolver (R-152). Keep one.",
                asset.AssetId, ownResolverGraphs[1].Id));
        }
    }

    /// <summary>
    /// ⭐ BP1675 — the purity check, shared by a behaviour resolver asset (CE-428) and a blueprint behaviour's own
    /// resolver (CE-446): both write ONLY their block's Variables, and both run inside the ingress shadow.
    /// </summary>
    private static void CheckPurity(BlueprintAsset asset, Graph graph, ValidationContext ctx)
    {
        // ── BP1675 — purity ───────────────────────────────────────────────────
        foreach (var node in graph.Nodes)
        {
            if (!SideEffectingNodeTypes.Contains(node.GetType())) continue;

            // ⭐⭐ THE ONE EXEMPTION — CE-428 ③: a behaviour resolver asset's Variables ARE the behaviour's
            //   block (the injected `ref TBlock`), and the resolve runs inside the ingress shadow — so a write
            //   to ANY of them is the resolver's result, never an escape. ⭐ CE-443: its Parameters are the
            //   `in` authored DTO — READ-ONLY, so a write to one is refused like any other denied write.
            // ⛔ CE-445: the AiPrimitive own-resolver exemption (CE-432) is gone with the own resolver.
            if (node is SetVariableNode subjectWrite
                && TargetsDeclaration(asset.Declarations.Of(DeclarationKind.Variable), subjectWrite))
                continue;

            // ⭐ CE-433 — Set Variables writes ONLY pinned Variables (BP1670 refuses any other pin).
            if (node is SetVariablesNode)
                continue;

            ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1675,
                $"Resolver graph '{graph.Name}' contains a '{node.GetType().Name}', which has an "
                + "effect outside the block it resolves. A resolver runs inside the behaviour "
                + "ingress's shadow parse: anything it writes elsewhere survives even when the "
                + "parse fails, leaving the entity half-switched. Compute the value and return it "
                + "through the graph's output instead.",
                asset.AssetId, graph.Id, node.Id));
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-428</c> — <b>③ a BEHAVIOUR RESOLVER asset</b>: like ②, it declares NOTHING — both subjects
    /// are injected (<c>in authored</c>, <c>ref block</c>) from its <c>ResolverSubject</c>. 📄 <c>Q76</c> §12.10b.
    /// </summary>
    private static void ValidateSubjectSignature(BlueprintAsset asset, Graph graph, ValidationContext ctx)
    {
        if (graph.Inputs.Count == 0 && graph.Outputs.Count == 0) return;

        ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1677,
            $"Resolver graph '{graph.Name}' refines behaviour '{asset.ResolverSubject!.BehaviorName}''s block, "
            + $"so it must declare no inputs and no outputs; it declares {graph.Inputs.Count} input(s) and "
            + $"{graph.Outputs.Count} output(s). Read and write the block with Get/Set Variable on the "
            + "asset's Variables — the block and the authored DTO are injected.",
            asset.AssetId, graph.Id));
    }

    /// <summary>
    /// ⭐ <c>CE-432</c> — does this <c>SetVariable</c> target one of <paramref name="declarations"/>?
    /// ⛔ An unresolvable target matches nothing and stays refused.
    /// ⚠ Matches by id first and name second, mirroring <c>Stage5_Schedule.FindVariableIndex</c> — if
    /// the two disagreed, a write the validator allowed could land somewhere else entirely.
    /// </summary>
    private static bool TargetsDeclaration(IEnumerable<BlueprintDeclaration> declarations, SetVariableNode node)
    {
        if (string.IsNullOrEmpty(node.VariableId)) return false;

        if (Guid.TryParse(node.VariableId, out var id))
            return declarations.Any(d => d.Id == id);

        return declarations.Any(d => string.Equals(d.Name, node.VariableId, StringComparison.Ordinal));
    }
}
