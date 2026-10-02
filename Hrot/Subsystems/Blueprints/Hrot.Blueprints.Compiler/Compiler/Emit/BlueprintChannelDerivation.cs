using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Emit;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-388</c> / <c>Q74 D-A2</c> — WHICH ACTUATOR CHANNELS DOES THIS BLUEPRINT DRIVE?</b>
/// 📄 <c>Architect_Question_74_Blueprint_Channel_Lifecycle.md</c> §4 <c>D-A</c>.
///
/// <para>🔒 <b>The user's constraint, and the whole reason this class exists rather than a
/// <c>.bp.json</c> field:</b> <i>"i want some clean and flexible solution where the most common use
/// case is a default so the user does not need to author unless he needs something extra."</i>
/// ⇒ the author declares NOTHING. Every channel-command node already carries its channel type
/// (<c>BuiltInChannelCommandCatalog.cs:73-87</c>), so the compiler already holds the fact.</para>
///
/// <para>⛔⛔ <b>What this CANNOT see, and why it fails loud instead of guessing.</b> A graph reaches
/// a channel through six <c>IrOp</c> kinds, not one — the user caught this:
/// <i>"it can call macros and functons and even hardcoded c#"</i>.
/// <list type="bullet">
///   <item>⭐ <c>IrOp_ChannelCommand</c> — <b>exact</b>, the op names its channel component.</item>
///   <item>⭐ a MACRO — <b>a non-issue</b>: <c>Stage2_5_ExpandMacros</c> is a compile-time fixpoint
///         that runs BEFORE IR and removes the <c>MacroCallNode</c>, so by the time this pass runs a
///         macro's channel commands are ordinary inlined ops in the host graph.</item>
///   <item>⭐ <c>IrOp_GraphCall</c> — a Function graph in the SAME asset, so its IR is right here;
///         this pass follows it to a fixpoint.</item>
///   <item>⛔ <c>IrOp_InlineActionCall</c> · <c>IrOp_PureCall</c> — <b>arbitrary hardcoded C#</b>.
///         The compiler holds a string FQN and nothing else, and the netstandard2.0 generator host
///         cannot load the game assemblies it is compiling.</item>
///   <item>⛔ <c>IrOp_LibraryCall</c> · <c>IrOp_AiPrimitiveCall</c> · <c>IrOp_PeerCall</c> — another
///         ASSET, whose IR this pass is not given.</item>
/// </list>
/// ⇒ the four <c>⛔</c> kinds make the result <see cref="ChannelDerivation.IsComplete"/> = false.</para>
///
/// <para>⛔⛔⛔ <b>AN INCOMPLETE SET IS NEVER SILENTLY TREATED AS EMPTY.</b> That is the
/// silent-default disease this repo keeps filing. ⚠ And the tempting alternative — <i>"when unsure,
/// assume it writes ALL three channels"</i> — is WORSE, not safer: an over-clean on state exit
/// <b>wipes a channel a PARALLEL REGION is currently driving</b>, turning a missing cleanup into a
/// cross-region defect. ⇒ the caller reports a diagnostic and the author declares explicitly.</para>
///
/// <para>⚠ <b>"undeclared" is NOT "unknown", and the distinction is deliberate.</b> An
/// <c>InlineActionCall</c> whose callee simply has no <c>[WritesChannel]</c> is not an error — the
/// attribute is opt-in and a compiler that errored on every C# call would be noise. What is reported
/// is a call this pass <b>cannot classify at all</b>. 📐 Measured `2026-09-28`: the blueprint-callable
/// C# surface is <c>ActionHosting.Shared</c> only (<c>BehaviorActionCatalog.MapHosting:196</c>) and
/// <b>no <c>[SharedAiAction]</c> in the repo writes a channel</b>, so this fires on nothing today and
/// exists as the guard rail for when one does.</para>
/// </summary>
internal static class BlueprintChannelDerivation
{
    /// <summary>The derived channel set, and whether the walk could see everything it needed to.</summary>
    internal readonly struct ChannelDerivation
    {
        /// <summary>Channel component FQNs, ordered and de-duplicated so emission is deterministic.</summary>
        public IReadOnlyList<string> ChannelComponentFqns { get; }

        /// <summary>
        /// ⭐ <c>false</c> when the walk hit a call whose channels it cannot determine.
        /// ⛔ A caller must NOT treat <c>!IsComplete</c> with an empty list as "writes nothing".
        /// </summary>
        public bool IsComplete { get; }

        /// <summary>What made it incomplete — a callee name per opaque call, for the diagnostic.</summary>
        public IReadOnlyList<string> OpaqueCalls { get; }

        public ChannelDerivation(
            IReadOnlyList<string> channels, bool isComplete, IReadOnlyList<string> opaqueCalls)
        {
            ChannelComponentFqns = channels;
            IsComplete           = isComplete;
            OpaqueCalls          = opaqueCalls;
        }
    }

    /// <summary>
    /// Derives the channel set for <paramref name="asset"/>, following <c>IrOp_GraphCall</c> to a
    /// fixpoint from every AiPrimitive entry graph.
    ///
    /// <para>⭐ <b>Reachability, not "every graph in the asset".</b> A Function graph that is defined
    /// and never called contributes nothing — binding a cleanup for a channel this blueprint never
    /// drives is exactly the over-clean that can wipe a parallel region's command.</para>
    /// </summary>
    public static ChannelDerivation Derive(IrAsset asset)
    {
        var byId    = asset.Graphs.ToDictionary(g => g.Id);
        var visited = new HashSet<Guid>();
        var queue   = new Queue<IrGraph>();

        // Roots: everything that is not a Function graph is independently entered by the runtime
        // (the AiPrimitive tick, event handlers, construction). A Function is reached only by a call.
        foreach (var g in asset.Graphs.Where(g => g.Kind != IrGraphKind.Function))
        {
            if (visited.Add(g.Id)) queue.Enqueue(g);
        }

        // ⭐⭐⭐ CE-407 — AND THE MAIN GRAPH, WHICH IS NORMALLY Function-KINDED.
        //
        // 🔴 The defect this closes, found by RUNNING the thing and not by any test. The rule above
        //    reads as obviously right and is wrong in practice: an AiPrimitive's entry graph is
        //    kinded `Function` in every authored asset (Loco1, HsmDriveActivity, …), and
        //    `AiPrimitiveEmitter.MainGraph:325-326` compensates with EXACTLY this fallback —
        //        AiPrimitiveMain ?? first Function
        //    so it emits a TickCore from that graph. The derivation had no such fallback, so for a
        //    Function-kinded main graph it walked NOTHING, derived an EMPTY channel set, and emitted
        //    a cleanup thunk whose body is the comment "commands no actuator channel".
        //
        // ⛔⛔ It fails in the worst possible direction: `IsComplete` stays TRUE and the set is
        //    EMPTY, which §2 of the design says must never be read as "writes nothing". The thunk
        //    still compiles, still registers, and the HSM still binds its id — so every check short
        //    of running it passes while THE CHANNEL LEAKS ON STATE EXIT, which is the one thing this
        //    whole mechanism exists to prevent. 📐 Measured live: an entity that left the driving
        //    state kept `LocomotionChannel {ActiveAction=1, Status=Running}`.
        //
        // ⚠ The two sites must agree. If the emitter's main-graph resolution ever changes, this
        //   must change with it — CE407_R1 pins the agreement rather than either rule alone.
        var main = asset.Graphs.FirstOrDefault(g => g.Kind == IrGraphKind.AiPrimitiveMain)
                   ?? asset.Graphs.FirstOrDefault(g => g.Kind == IrGraphKind.Function);
        if (main != null && visited.Add(main.Id)) queue.Enqueue(main);

        var channels = new SortedSet<string>(StringComparer.Ordinal);
        var opaque   = new SortedSet<string>(StringComparer.Ordinal);

        while (queue.Count > 0)
        {
            foreach (var op in Operations(queue.Dequeue()))
            {
                switch (op)
                {
                    case IrOp_ChannelCommand cc:
                        channels.Add(cc.ChannelComponentTypeFqn);
                        break;

                    // ⭐ same asset — its IR is right here, so follow it.
                    case IrOp_GraphCall gc when byId.TryGetValue(gc.TargetGraphId, out var callee):
                        if (visited.Add(callee.Id)) queue.Enqueue(callee);
                        break;

                    // ⛔ a GraphCall whose target is not in this asset should be impossible
                    //   (Stage5 emits it only for an in-blueprint Function graph) — but an
                    //   unresolvable target is exactly the kind of thing that must not pass silently.
                    case IrOp_GraphCall gc:
                        opaque.Add($"graph {gc.TargetGraphId}");
                        break;

                    // ⛔ arbitrary C# — a string FQN and nothing else (see the class header).
                    case IrOp_InlineActionCall ia:
                        opaque.Add(ia.ActionFqn);
                        break;
                    // ⭐ S5d — a hosted child writes whatever channels IT writes; not derivable here.
                    case IrOp_RunBehavior rb:
                        opaque.Add($"behaviour {rb.BehaviorName}");
                        break;
                    case IrOp_PureCall pc:
                        opaque.Add(pc.MethodFqn);
                        break;

                    // ⛔ another ASSET, whose IR this pass is not given.
                    case IrOp_LibraryCall lc:
                        opaque.Add($"library {lc.LibraryBlueprintId}.{lc.MethodName}");
                        break;
                    case IrOp_AiPrimitiveCall ac:
                        opaque.Add($"aiprimitive {ac.AiPrimitiveBlueprintId}");
                        break;
                    case IrOp_PeerCall pk:
                        opaque.Add($"peer {pk.PeerBlueprintId}.{pk.MethodName}");
                        break;
                }
            }
        }

        return new ChannelDerivation(channels.ToArray(), opaque.Count == 0, opaque.ToArray());
    }

    private static IEnumerable<IrOperation> Operations(IrGraph graph)
    {
        foreach (var block in graph.Blocks)
            foreach (var stmt in block.Statements)
                if (stmt.Operation != null)
                    yield return stmt.Operation;
    }
}
