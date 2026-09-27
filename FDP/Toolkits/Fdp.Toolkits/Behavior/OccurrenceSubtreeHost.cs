using System;
using Fbt;
using Fbt.Runtime;

namespace Fdp.Toolkit.Behavior;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-366</c> — what actually runs a <c>NodeType.Subtree</c> node.</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §33.
///
/// <para>⭐ <b>The kernel's <see cref="ISubtreeHost{TBlackboard,TContext}"/>, implemented here</b> so
/// the kernel never learns what an occurrence slot is. It is a two-line body: ask
/// <see cref="BTreeHostedSites"/> which slot this SITE owns, then hand off to
/// <see cref="HostedSubtree"/> — the same body the HSM host reaches through
/// <c>BrainTickSystem.TickHostedChildren</c>. ⛔ One hosting mechanism, two entry points.</para>
///
/// <para>⭐ <b>Stateless and shared.</b> Every site's identity arrives in the call, so there is
/// nothing per-host to keep; <see cref="Instance"/> is handed to every interpreter that can host.</para>
///
/// <para>⚠⚠ <b>ONLY the <c>byte</c> blackboard is hosted, and that is not a limitation to route
/// around.</b> Since <c>P4</c> the brain's root params region IS a byte region and
/// <c>HostedChildren</c> stores <c>Interpreter&lt;byte, BTreeContext&gt;</c>. A differently-typed
/// host tree reaching here means something registered a hosting site against a tree the runtime
/// cannot tick, so it FAILS rather than guessing.</para>
/// </summary>
public sealed class OccurrenceSubtreeHost : ISubtreeHost<byte, BTreeContext>
{
    /// <summary>⭐ The shared instance. Stateless — every identity comes in through the call.</summary>
    public static readonly OccurrenceSubtreeHost Instance = new();

    private OccurrenceSubtreeHost() { }

    /// <inheritdoc/>
    public NodeStatus Tick(ref byte blackboard, ref BTreeContext context, BehaviorTreeBlob blob, int nodeIndex)
    {
        // ⛔⛔ A SITE WITH NO ENTRY IS A HARD FAILURE, not a quiet Failure status — §19.6 ⑤.
        //    A host that silently does nothing reads as "the subtree just fails", which is the exact
        //    silent miss this programme keeps paying for. If this throws, the host's registrar did
        //    not call BTreeHostedSites.Bind for this blob.
        if (!BTreeHostedSites.TryGetKey(blob, nodeIndex, out int key))
            throw new InvalidOperationException(
                $"BTree node {nodeIndex} of '{blob?.TreeName}' is a hosting site with no registered " +
                "slot key. Its registrar must call BTreeHostedSites.Bind(beh, blob, plan) with the " +
                "plan whose Slots it also put on the BehaviorDefinition.");

        // ⚠ The blackboard argument is deliberately IGNORED: the child reads the ENTITY's root params
        //   region, resolved inside TickFromContext, not the host tree's own projection. That is the
        //   single spelling of the rule (CE-362) and the HSM arm reaches the same one.
        return HostedSubtree.TickFromContext(ref context, key);
    }

    /// <inheritdoc/>
    public void Reset(ref BTreeContext context, BehaviorTreeBlob blob, int nodeIndex)
    {
        // ⭐ F14 — the host abandoned a child that may still be Running.
        // ⚠ SKIP rather than throw here, unlike Tick: a reset runs during the exit sweep, and a
        //   teardown that throws would take the whole tick with it. A missing binding has already
        //   been reported loudly by Tick, which must have run first for the node to be on the path.
        if (BTreeHostedSites.TryGetKey(blob, nodeIndex, out int key))
            HostedSubtree.Reset(ref context, key);
    }
}
