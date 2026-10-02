using System;
using Hrot.AiEditor.Persistence.Hsm;

namespace Hrot.AiEditor.Persistence.Emit;

/// <summary>
/// ⛔⛔⛔ <b>RETIRED <c>2026-09-23</c> (<c>CE-333</c>) — <see cref="Emit"/> ALWAYS RETURNS <c>null</c>.</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.2.1 / §32.3. ⭐ HSM sub-tree hosting is declared
/// per <b>STATE</b> now (<c>StateNode.SubtreeName</c> + <c>SubtreeAssetId</c>) and ticked every frame
/// by <c>BrainTickSystem.TickHostedChildren</c>. ⛔ The reasoning is in <see cref="Emit"/>'s body, at
/// length, because it is the kind of removal a future reader will otherwise try to undo.
/// ⚠ The type and its callers stay: a caller that gets <c>null</c> emits no file, which is exactly
/// what every shipped asset already did.
///
/// <para>⛔ HISTORY — what it was, kept so the supersession is legible:</para>
///
/// <para><b>Batch 92 (<c>92a</c>) — the HSM orchestrator emit BODY, moved off the editor model.</b>
///
/// <para>📐 <b>Why it had to move.</b> <c>HsmOrchestratorEmitter</c> emits from <c>HsmAsset</c> — an
/// editor type. ⛔ A Roslyn generator has only the <b>DTO</b>. ⇒ this core emits from
/// <see cref="HsmAssetDto"/>, so ⭐ <b>one body</b> serves both the editor sidecar path and the
/// generator's <c>{baseName}.Orchestrators.g.cs</c> (<c>92b</c>) — 📌 ruling 9.</para>
///
/// <para>⭐⭐ <b>The HSM arm is the one <c>91b</c> made meaningful.</b> HSM has no Approach-B field
/// sync at all; it hosts a sub-tree <b>only</b> through an Approach-A alias, and until <c>91b</c> those
/// aliases never survived a reload ⇒ 🔴 nothing an HSM ever loaded could emit an orchestrator.
/// ⛔ <b>Do not read this as "HSM sub-tree hosting is complete"</b> — there is still no authoring
/// gesture that creates the alias, and no blackboard aggregation behind it.</para>
///
/// <para>⭐⭐⭐ <b><c>DtoTypeId</c> is SPLIT, never resolved.</b> The editor emitter reads
/// <c>binding.DtoType.Name</c> / <c>.Namespace</c> — a live <c>System.Type</c>. ⛔ A generator cannot
/// load behavior assemblies, so this core takes <see cref="BlackboardAliasBindingDto.DtoTypeId"/>
/// (a <c>Type.FullName</c>, written by <c>91b</c>) and splits it at the last <c>'.'</c>. ⭐ That is
/// pure string work and needs no assembly.</para>
/// </summary>
public static class HsmOrchestratorEmitCore
{
    /// <summary>Fallback namespace when the asset declares none — matches the editor emitter.
    /// ⚠ Retained: callers reference it, and it is the namespace E5's emission would use.</summary>
    public const string DefaultTargetNamespace = "Hrot.AI.Behaviors.Machines";

    /// <summary>
    /// ⛔⛔ <b>Always <c>null</c> since <c>CE-333</c> — the caller emits no file.</b> See the body for
    /// why this arm was routed to <c>E5</c> rather than repaired.
    /// </summary>
    public static string? Emit(HsmAssetDto dto)
    {
        if (dto is null) throw new ArgumentNullException(nameof(dto));

        // ⭐⭐⭐ CE-333 — SUPERSEDED BY E5, AND ROUTED RATHER THAN PATCHED (2026-09-23).
        //   📄 DESIGN_Occurrence_Scoped_Storage.md §32.2.1 / §32.3.
        //
        // 🔴 What this used to emit could not compile: an [HsmAction] carrying the FastBTree ACTION
        //    signature `(ref Bb master, ref BehaviorTreeState state, ref BTreeContext ctx, int
        //    paramIndex)` returning NodeStatus. The HSM ABI is a THUNK — `&method` must convert to
        //    `delegate*<void*, void*, HsmCommandWriter*, void>` (HsmActionGenerator:405) — and it
        //    does not. Nothing caught it because Emit returns null for every shipped asset and ZERO
        //    tests compile emitted text.
        //
        // ⛔⛔ AND MAKING IT ABI-CORRECT WOULD NOT HAVE MADE IT WORK. Two measured reasons:
        //    ① An [HsmAction] is dispatched at most once per event-driven round: UpdateBatchCore
        //      advances ONE PHASE PER TICK, Idle leaves only on a non-empty queue, and Activity ends
        //      by setting Idle ⇒ on a quiescent machine, once, ever (CE-334). A hosted BTree is a
        //      CURSOR; it must advance every frame.
        //    ② An HSM thunk has no deltaTime. HsmKernelBridge carries Self, WorldHandle and a trace
        //      pointer — the kernel never hands an action its dt — so a BTreeContext built here would
        //      tick the child at dt = 0, forever.
        //   ⇒ a correct-looking thunk would have been a TRAP for the first author who used it.
        //
        // ⭐⭐ WHERE THE CAPABILITY WENT — nothing is lost. E5 hosts a child from an HSM STATE:
        //    the state carries {SubtreeAssetId, SubtreeName}, HsmBridgeEmitCore declares the child's
        //    tree-state slot and binds its interpreter, and BrainTickSystem.TickHostedChildren ticks
        //    it EVERY FRAME while the host state is active — with the real dt it already holds.
        //    🔒 One mechanism for one concept (ruling 9); this arm was the second.
        //
        // ⚠ Measured before removing the emission: 0 shipped .hsm.json carries an alias, and this
        //   type's own remarks already said there is "no authoring gesture that creates the alias,
        //   and no blackboard aggregation behind it" ⇒ nothing in the field loses a capability.
        // ⛔ The ALIAS DATA is untouched. Only this arm's HOSTING EMISSION is retired; if a future
        //   authoring gesture needs alias-driven hosting, it must route through E5's per-state table,
        //   not resurrect a parallel emitter.
        return null;
    }
}
