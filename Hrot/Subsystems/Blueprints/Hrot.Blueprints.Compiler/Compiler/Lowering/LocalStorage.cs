using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Lowering;

/// <summary>
/// BP-57 / ⭐⭐ <b>Q27-A3</b> — which storage a graph's function-locals get, and the one predicate that
/// decides it.
///
/// <para>
/// ⭐ <b>Two storage classes, one designer-visible meaning.</b> A graph that cannot suspend keeps its
/// locals as plain C# locals initialised at the top of the emitted method — Q27-A1, unchanged. A graph
/// that <i>can</i> suspend gets a <b>graph-scoped blackboard slot</b> instead, reset in the ENTRY
/// BLOCK. The designer sees no difference: a local is a local, it resets once per invocation, and it
/// keeps its value across a suspension inside that invocation.
/// </para>
///
/// <para>
/// ⚠⚠ <b>Why A1 alone was wrong.</b> A suspension is <c>return NodeStatus.Running</c> — the C# frame
/// dies, and a stack local with it. A value written before a <c>Delay</c> read back as its
/// <b>default</b> after the resume, silently, with no diagnostic. <c>__phase</c> and
/// <c>__waitUntilTime</c> live in blackboard memory for precisely this reason; a local that must cross
/// the same boundary has to live there too.
/// </para>
///
/// <para>
/// ⭐ <b>"Per-invocation" was never "per-frame."</b> The entry block is reached only when
/// <c>__phase == 0</c> — a fresh logical invocation — and the phase is cleared before control reaches
/// the resume block, so the next invocation lands there again. That is the SAME rule as the stack
/// case, where an invocation happens to be one frame. A1 conflated the two because in a
/// non-suspending graph they coincide.
/// </para>
/// </summary>
internal static class LocalStorage
{
    /// <summary>
    /// ⭐⭐ <b>The one IR-level "can this suspend?" predicate.</b> <c>InstanceLowering</c>,
    /// <c>AiPrimitiveLowering</c> and the storage choice below all ask it here, so they cannot drift.
    ///
    /// <para>
    /// ⚠ <c>IrOp_InlineActionCall</c> is a member of this set and is the one everything else keeps
    /// forgetting — it comes from a <c>ChannelCommandNode</c> with <c>ActionFqn</c> set
    /// (<c>Stage5.ScheduleInlineActionNode</c>), and <c>WaitLowering</c> gives it the same
    /// suspend/resume block split as a <c>Delay</c>. See <c>MacroLatency.IsLatent</c> for the
    /// node-level mirror of this list and why the two must agree.
    /// </para>
    /// </summary>
    public static bool CanSuspend(IrGraph graph)
        => graph.Blocks
            .SelectMany(b => b.Statements)
            .Any(s => s.Operation is IrOp_LatentDelay or IrOp_WaitForChannel or IrOp_WaitForEvent
                                  or IrOp_InlineActionCall);

    /// <summary>
    /// The emitted identifier for one blackboard-resident local. ⭐ <b>Graph-qualified</b>: two graphs
    /// may each declare a local named <c>Scratch</c>, and once they share one struct <c>__loc_</c>
    /// alone no longer separates them.
    /// </summary>
    public static string SlotName(string prefix, string localName) => prefix + localName;

    /// <summary>
    /// Promotes every suspending graph's locals to blackboard slots, returning the rewritten asset.
    ///
    /// <para>
    /// ⛔⛔ <b>The slots go in their OWN list, never appended to <c>Variables</c>/<c>WorkingState</c>.</b>
    /// <c>Stage5.FindVariableIndex</c> and <c>EmissionContext.VarFieldName</c> read those three lists
    /// <b>positionally</b> and already disagree about what the integer means
    /// (<c>FINDING_Variable_Index_Space.md</c> / <c>BP-226</c>) —
    /// <c>AiPrimitiveLowering.EnsurePhaseByteInWorkingState</c> appends rather than prepends for
    /// exactly that reason. Adding a fourth source to a space that cannot express three is the
    /// direction Q27-D ruled against, so these slots are emitted into the same struct but are
    /// unreachable through that index space: they are addressed by NAME only.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>Call this BEFORE the wait lowering.</b> The reset statement is injected into the graph's
    /// <see cref="IrGraph.Entry"/> block, and <c>WaitLowering</c> repoints <c>Entry</c> at the
    /// synthesized dispatch block — after which the original entry is no longer nameable from here.
    /// </para>
    /// </summary>
    public static IrAsset PromoteSuspendingGraphLocals(IrAsset asset)
    {
        if (asset.Graphs.All(g => g.Locals.Count == 0)) return asset;

        var slots     = new List<IrField>();
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        var newGraphs = new List<IrGraph>(asset.Graphs.Count);

        foreach (var graph in asset.Graphs)
        {
            if (graph.Locals.Count == 0 || !CanSuspend(graph))
            {
                newGraphs.Add(graph);
                continue;
            }

            var prefix = UniquePrefix(graph, usedNames);
            foreach (var local in graph.Locals)
                slots.Add(local with { Name = SlotName(prefix, local.Name) });

            newGraphs.Add(InjectEntryReset(graph with { LocalSlotPrefix = prefix }));
        }

        return slots.Count == 0
            ? asset
            : asset with { Graphs = newGraphs, GraphLocalSlots = slots };
    }

    /// <summary>
    /// <c>__loc_{Graph}_</c>, disambiguated when two graph names sanitize to the same identifier.
    /// Deterministic in graph order, which is the asset's declaration order.
    /// </summary>
    private static string UniquePrefix(IrGraph graph, HashSet<string> used)
    {
        var basePrefix = "__loc_" + Sanitizer.SanitizeName(graph.Name) + "_";
        if (used.Add(basePrefix)) return basePrefix;

        for (int n = 2; ; n++)
        {
            var candidate = $"__loc_{Sanitizer.SanitizeName(graph.Name)}{n}_";
            if (used.Add(candidate)) return candidate;
        }
    }

    /// <summary>
    /// Prepends <see cref="IrOp_ResetLocals"/> to the entry block — Q27-E's "reset from the declared
    /// default on entry", relocated from the top of the method to the one block a fresh invocation
    /// enters through.
    /// </summary>
    private static IrGraph InjectEntryReset(IrGraph graph)
    {
        var blocks = new List<IrBlock>(graph.Blocks.Count);
        foreach (var block in graph.Blocks)
        {
            if (block.Id.Value != graph.Entry.Value)
            {
                blocks.Add(block);
                continue;
            }

            var stmts = new List<IrStatement>(block.Statements.Count + 1)
            {
                new IrStatement
                {
                    ResultValue = null,
                    Operation   = new IrOp_ResetLocals(),
                    Debug       = new IrDebugAnnotation
                    {
                        GraphId     = graph.Id,
                        Synthesized = "stage6-local-slot-reset",
                    },
                },
            };
            stmts.AddRange(block.Statements);
            blocks.Add(block with { Statements = stmts });
        }
        return graph with { Blocks = blocks };
    }
}
