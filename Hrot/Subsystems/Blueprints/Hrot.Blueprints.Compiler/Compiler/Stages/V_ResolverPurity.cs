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
        typeof(SetSharedNode),            // writes the cross-entity shared region
        typeof(SetComponentNode),         // writes a component on an entity
        typeof(CollectionWriteNode),      // mutates a component collection
        typeof(ListWriteNode),            // mutates a fixed-capacity list variable

        // ── dispatch: the value leaves this graph and something else acts on it ─
        typeof(ChannelCommandNode),
        typeof(PublishEventNode),
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
        typeof(WhenNode),

        // ── see gap ② in the class doc ────────────────────────────────────────
        typeof(MacroCallNode),
    };

    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        // ⭐⭐⭐ E8a / R-149 — TWO KINDS OF RESOLVER GRAPH, and the difference is WHOSE params it refines.
        //
        //   ① a LIBRARY asset's Construction graph — a REUSABLE resolver. It declares the DTO it
        //      refines (one in, one out, same type: BP1677), and something else names it.
        //   ② an ASSET'S OWN Construction graph — it refines THIS asset's parameters.
        //
        // 🔴 ② cannot declare its DTO, and that is a MEASURED fact rather than a simplification: an
        //    AiPrimitive's params struct is GENERATED (`{Class}.Params`, built from the asset's own
        //    Parameters by AiPrimitiveEmitter.EmitParamsStruct), so its FQN embeds the BlueprintId
        //    hash and no authored TypeId could name it without coupling the asset to its own emitted
        //    class name. ⇒ its subject is IMPLIED, and it declares NOTHING.
        var ownResolverGraphs = new List<Graph>();

        foreach (var graph in asset.Graphs)
        {
            if (graph.Kind != GraphKind.Construction) continue;

            bool isLibrary = asset.Dispatch == BlueprintDispatchKind.Library;
            if (!isLibrary) ownResolverGraphs.Add(graph);

            // ── BP1676 — there must be something for this graph to resolve ────────
            //
            // ⭐⭐ REVISED for E8a. It used to read "Construction is only supported on a Library
            // asset", which was right while a resolver could only be a SEPARATE asset. ⛔ Under
            // R-149 the params-owning region names its resolver, and the simplest such naming is
            // "my own graph" — so an asset that HAS parameters may carry one.
            //
            // ⭐ The rule's real job is unchanged and is what survives: REFUSE A RESOLVER NOTHING
            // WILL EVER CALL. On a Library that cannot happen here (the binding lives elsewhere);
            // on any other kind it happens exactly when the asset declares no parameters.
            // ⚠ Through the SANCTIONED view — see ParamsOf below for why a compiler stage may not
            //   read a declaration view directly. 📌 U-11's rail caught this file on its first full
            //   run, which is the rail doing exactly its job.
            if (!isLibrary && !ParamsOf(asset).Any())
            {
                ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1676,
                    $"Graph '{graph.Name}' is a Construction graph, which on a '{asset.Dispatch}' "
                    + "asset resolves that asset's own parameters — but this asset declares none, so "
                    + "nothing would ever call it. Declare the parameters it should refine, or move "
                    + "the graph to a Library asset to make it a reusable resolver.",
                    asset.AssetId, graph.Id));
                continue;
            }

            // ── BP1677 — the signature, which differs by KIND ─────────────────────
            if (isLibrary) ValidateReusableSignature(asset, graph, ctx);
            else           ValidateOwnResolverSignature(asset, graph, ctx);

            // ── BP1675 — purity ───────────────────────────────────────────────────
            foreach (var node in graph.Nodes)
            {
                if (!SideEffectingNodeTypes.Contains(node.GetType())) continue;

                // ⭐⭐⭐ THE ONE EXEMPTION, and it is not a loosening.
                //
                // An own-asset resolver's OUTPUT *is* its parameters region — the emitted method
                // takes `ref Params p` and the graph writes through it. ⇒ a SetVariable that targets
                // a PARAMETER is this graph's return value, not an escape from the ingress shadow.
                // ⛔ A SetVariable targeting STATE is still refused, and so is every other denied
                // node, because those genuinely outlive a failed parse (Q43 §4).
                if (!isLibrary && node is SetVariableNode sv && TargetsAParameter(asset, sv))
                    continue;

                ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1675,
                    $"Resolver graph '{graph.Name}' contains a '{node.GetType().Name}', which has an "
                    + "effect outside the parameters it returns. A resolver runs inside the behaviour "
                    + "ingress's shadow parse: anything it writes elsewhere survives even when the "
                    + "parse fails, leaving the entity half-switched. Compute the value and return it "
                    + "through the graph's output instead.",
                    asset.AssetId, graph.Id, node.Id));
            }
        }

        // ── BP1676 (second arm) — ONE resolver per params region ──────────────────
        //
        // ⭐ R-149: "one field, one value, so two resolvers for one region cannot be authored." An
        // asset's own parameters are ONE region, so two Construction graphs on it would be exactly
        // the competition the selection model exists to make unrepresentable.
        // ⚠ A LIBRARY may carry many — they are separate reusable resolvers, each named separately.
        if (ownResolverGraphs.Count > 1)
        {
            ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1676,
                $"This asset declares {ownResolverGraphs.Count} Construction graphs "
                + $"({string.Join(", ", ownResolverGraphs.Select(g => "'" + g.Name + "'"))}), but its "
                + "parameters are ONE region and a region names exactly one resolver. Keep one, or "
                + "move the others to a Library asset as separately-named reusable resolvers.",
                asset.AssetId, ownResolverGraphs[1].Id));
        }
    }

    /// <summary>
    /// ⭐ <b>① a REUSABLE resolver on a Library asset</b> — it declares the DTO it refines.
    ///
    /// <para>
    /// ⭐⭐ <c>Q43-D</c>/<c>R-81</c>: <i>"the graph takes the current DTO as an input and returns the
    /// modified one"</i> is what makes a resolver REFINE rather than REPLACE. ⛔ A resolver that only
    /// PRODUCED a value would silently discard the scenario's JSON override — the exact defect
    /// <c>BP-275</c> fixed on the generated path.
    /// </para>
    /// </summary>
    private static void ValidateReusableSignature(BlueprintAsset asset, Graph graph, ValidationContext ctx)
    {
        if (graph.Inputs.Count != 1 || graph.Outputs.Count != 1)
        {
            ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1677,
                $"Resolver graph '{graph.Name}' must declare exactly one input and one output "
                + $"(the parameters DTO in, the refined DTO out); it declares "
                + $"{graph.Inputs.Count} input(s) and {graph.Outputs.Count} output(s).",
                asset.AssetId, graph.Id));
            return;
        }

        if (!string.Equals(graph.Inputs[0].Type.TypeId, graph.Outputs[0].Type.TypeId,
                StringComparison.Ordinal))
        {
            ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1677,
                $"Resolver graph '{graph.Name}' takes '{graph.Inputs[0].Type.TypeId}' but returns "
                + $"'{graph.Outputs[0].Type.TypeId}'. A resolver REFINES the parameters it is "
                + "given, so its input and output must be the same type.",
                asset.AssetId, graph.Id));
        }
    }

    /// <summary>
    /// ⭐⭐ <b>② an OWN-asset resolver</b> — it declares NOTHING, because its subject is implied.
    ///
    /// <para>
    /// ⛔ <b>Not laxity — the opposite.</b> The DTO is this asset's generated <c>Params</c> struct, so
    /// a declared input could only ever be WRONG or a restatement. Refusing the declaration keeps the
    /// one true shape unambiguous, and it means the emitter never has to reconcile an authored type
    /// against a generated one.
    /// </para>
    /// </summary>
    private static void ValidateOwnResolverSignature(BlueprintAsset asset, Graph graph, ValidationContext ctx)
    {
        if (graph.Inputs.Count == 0 && graph.Outputs.Count == 0) return;

        ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1677,
            $"Resolver graph '{graph.Name}' resolves this asset's OWN parameters, so it must declare "
            + $"no inputs and no outputs; it declares {graph.Inputs.Count} input(s) and "
            + $"{graph.Outputs.Count} output(s). Read the parameters with Get Parameter nodes and "
            + "write the refined values back with Set Variable nodes targeting those parameters.",
            asset.AssetId, graph.Id));
    }

    /// <summary>
    /// Does this <c>SetVariable</c> target one of the asset's PARAMETERS (rather than its state)?
    /// ⚠ Matches by id first and name second, mirroring <c>Stage5_Schedule.FindVariableIndex</c> — if
    /// the two disagreed, a write the validator allowed could land somewhere else entirely.
    /// </summary>
    private static bool TargetsAParameter(BlueprintAsset asset, SetVariableNode node)
    {
        if (string.IsNullOrEmpty(node.VariableId)) return false;

        if (Guid.TryParse(node.VariableId, out var id))
            return ParamsOf(asset).Any(p => p.Id == id);

        return ParamsOf(asset)
            .Any(p => string.Equals(p.Name, node.VariableId, StringComparison.Ordinal));
    }

    /// <summary>
    /// The asset's parameter declarations, through the SANCTIONED accessor.
    ///
    /// <para>
    /// ⛔ <b>A compiler stage may not read the per-kind declaration VIEWS directly.</b> <c>U-11</c>'s
    /// <c>ViewsAreUnreadTests</c> greps for exactly that, because <c>U-12</c> deletes those three
    /// properties on the strength of <i>"nothing reads them any more"</i> — so a direct read here
    /// would turn that deletion into the batch that finds out.
    /// </para>
    /// </summary>
    private static IEnumerable<BlueprintDeclaration> ParamsOf(BlueprintAsset asset)
        => asset.Declarations.Of(DeclarationKind.Parameter);
}
