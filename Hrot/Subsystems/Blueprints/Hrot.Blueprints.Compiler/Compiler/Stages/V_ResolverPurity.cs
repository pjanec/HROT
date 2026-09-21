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
        foreach (var graph in asset.Graphs)
        {
            if (graph.Kind != GraphKind.Construction) continue;

            // ── BP1676 — a Construction graph is a LIBRARY shape today ────────────
            //
            // ⭐⭐ Q43-A2′ is explicit that `Construction` must NOT be redefined as "the resolver
            // graph" — it means "runs once at setup", and `Dispatch × GraphKind` says what that means:
            // on a Library asset it resolves parameters; on an Instance asset it would configure the
            // instance. ⛔ But NOTHING consumes the Instance sense today, so a Construction graph on an
            // Instance asset would compile to a method nobody calls — a silent no-op, which is the
            // failure shape this programme keeps filing. ⇒ refuse it LOUDLY and keep the meaning free
            // for whoever builds that consumer.
            if (asset.Dispatch != BlueprintDispatchKind.Library)
            {
                ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1676,
                    $"Graph '{graph.Name}' is a Construction graph, which is only supported on a "
                    + $"Library asset today (it compiles to a parameter resolver). This asset's "
                    + $"dispatch is '{asset.Dispatch}', where nothing would ever call it. Move the "
                    + "graph to a Library asset, or make it a Function graph.",
                    asset.AssetId, graph.Id));
                continue;
            }

            // ── BP1677 — the resolver signature ───────────────────────────────────
            //
            // ⭐⭐ Q43-D, and it is not a style rule: "the graph takes the current DTO as an input and
            // returns the modified one" is what makes the resolver REFINE rather than REPLACE (R-81).
            // ⛔ A resolver that only PRODUCED a value would silently discard the scenario's JSON
            // override — the exact defect BP-275 fixed on the generated path.
            //
            // ⚠ It is also what makes `BlueprintDefinition.Resolvers` callable generically: one
            // blittable in, the same type out, so a binding site marshals a single DTO both ways
            // without knowing the asset.
            if (graph.Inputs.Count != 1 || graph.Outputs.Count != 1)
            {
                ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1677,
                    $"Resolver graph '{graph.Name}' must declare exactly one input and one output "
                    + $"(the parameters DTO in, the refined DTO out); it declares "
                    + $"{graph.Inputs.Count} input(s) and {graph.Outputs.Count} output(s).",
                    asset.AssetId, graph.Id));
            }
            else if (!string.Equals(graph.Inputs[0].Type.TypeId, graph.Outputs[0].Type.TypeId,
                         StringComparison.Ordinal))
            {
                ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1677,
                    $"Resolver graph '{graph.Name}' takes '{graph.Inputs[0].Type.TypeId}' but returns "
                    + $"'{graph.Outputs[0].Type.TypeId}'. A resolver REFINES the parameters it is "
                    + "given, so its input and output must be the same type.",
                    asset.AssetId, graph.Id));
            }

            // ── BP1675 — purity ───────────────────────────────────────────────────
            foreach (var node in graph.Nodes)
            {
                if (!SideEffectingNodeTypes.Contains(node.GetType())) continue;

                ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1675,
                    $"Resolver graph '{graph.Name}' contains a '{node.GetType().Name}', which has an "
                    + "effect outside the parameters it returns. A resolver runs inside the behaviour "
                    + "ingress's shadow parse: anything it writes elsewhere survives even when the "
                    + "parse fails, leaving the entity half-switched. Compute the value and return it "
                    + "through the graph's output instead.",
                    asset.AssetId, graph.Id, node.Id));
            }
        }
    }
}
