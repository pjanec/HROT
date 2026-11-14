using System;
using System.Collections.Generic;
using Fhsm.Kernel.Data;

namespace Fdp.Toolkit.Behavior;

/// <summary>
/// ⭐⭐⭐ <b><c>E3b-0</c> — WHICH BLACKBOARD VARIABLE DOES THIS HSM STATE'S OCCURRENCE SEED FROM?</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §28.6 / §28.6a.
///
/// <para>🔴🔴 <b>The gap this closes.</b> <c>E3a</c> gave every hosted occurrence its own params BYTES,
/// but they all seeded from <c>BehaviorParameters[0] + 0</c> — the first packed variable — so two
/// parallel HSM regions running one asset still got the SAME authored value. ⛔ The BTree bridge does
/// not have this problem because it emits <b>one adapter per node</b> at a per-site key
/// <c>{MethodFqn}@{offset}</c>; the HSM dispatcher registers <b>one thunk per <c>ushort</c> action
/// id</b>, so there is nowhere to bake a per-site offset.</para>
///
/// <para>⭐⭐⭐ <b>What makes it tractable is that the params already MOVED.</b> The binding does not
/// need to reach the thunk's address computation at all — only the SEED (§28.4), which already runs
/// inside the thunk and already holds <c>O6</c>'s <c>(region, state)</c> stamp. ⇒ this table is read
/// ONCE per occurrence, on first attach, and never on a steady-state dispatch.</para>
///
/// <para>⭐⭐ <b>The HSM learns nothing about blueprints</b> (🔒 user ruling, <c>2026-09-21</c>): the
/// generated HSM registrar maps <b>its own states</b> to <b>its own blackboard variables</b>. The
/// blueprint side only asks <i>"what offset for this (machine, state)?"</i>.</para>
///
/// <para>⚠ <b>Startup-only, like the registries it sits beside.</b> Registration happens during the
/// <c>[BlueprintRegistrar]</c> scan, before the first tick; reads are lock-free thereafter. ⛔ Not
/// safe to mutate mid-frame, and nothing does.</para>
/// </summary>
public static class HsmParamBindings
{
    /// <summary>The seed offset for an UNBOUND state — byte-for-byte the pre-<c>E3b-0</c> behaviour.</summary>
    public const int UnboundOffset = 0;

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-414</c> — the SITE that carries no blueprint identity.</b> A state's own
    /// <c>ExpressionTargetField</c> registers under this, and every unmatched site FALLS BACK to it.
    ///
    /// <para>⭐ It is what keeps a curated <c>[SharedAiAction]</c> — which has an action id and a
    /// compound key but no asset Guid — and every asset authored before <c>CE-414</c> answering
    /// exactly what they answered before.</para>
    /// </summary>
    public static readonly Guid StateWideSite = Guid.Empty;

    // (machineId, stateIndex, siteId) -> packed byte offset of the bound variable.
    //
    // ⭐⭐⭐ CE-414: the SITE is the hosted blueprint's ASSET GUID, and adding it is what lets two
    //   blueprints dispatched under ONE state stamp seed from DIFFERENT variables. 🔴 The case that
    //   forced it is a POLLED GUARD: the kernel stamps a guard with its SOURCE STATE
    //   (HsmKernelCore.EvaluateGuard:768), so a state's activity blueprint and the guard blueprint on
    //   its outgoing transition arrive with the SAME (machine, state) — and before this they read the
    //   same bytes through two DIFFERENT Params types. ⛔ A type-pun with no validator behind it.
    // ⭐ The Guid needs no new plumbing: the emitted thunk already holds it and already passes it to
    //   HsmOccurrence.KeyFor one line above the seed. The SLOT was per-site before the SEED was.
    private static readonly Dictionary<(uint MachineId, ushort StateId, Guid SiteId), int> _offsets = new();
    /// <summary>
    /// ⭐ <c>CE-442</c> — every WRITE takes this lock, so two registrars running at once (parallel test
    /// classes today) cannot corrupt the table. ⛔ Reads stay unlocked: registration finishes before the
    /// first tick, and the read side is on the per-tick path. Same rule as <c>HsmActionDispatcher</c>.
    /// </summary>
    private static readonly object WriteLock = new();


    /// <summary>
    /// ⭐⭐⭐ <b>Registers a machine's per-state seed offsets, resolving authoring Guids to the flat
    /// state indices the kernel stamps.</b>
    ///
    /// <para>⭐ <b>The join is <see cref="MachineMetadata.StateStableIds"/></b> — flat index →
    /// authoring <c>StableId</c>, already populated by the compiler for the editor projection layer.
    /// ⛔ That is why the emitter can bake <c>StableId</c>s and never needs the flattener's ordering:
    /// the ordering is recovered here, at runtime, from the blob itself.</para>
    ///
    /// <para>⚠ <b>A blob with no metadata registers NOTHING, silently and correctly</b> — every state
    /// then seeds from <see cref="UnboundOffset"/>, which is the pre-<c>E3b-0</c> behaviour. ⛔ It is
    /// not an error: a hand-built blob in a test has no authoring identity to resolve.</para>
    /// </summary>
    /// <param name="bindings">
    /// <c>(state StableId, packed byte offset)</c> pairs, baked by the generated HSM registrar from the
    /// state's <c>ExpressionTargetField</c> and the asset's packed blackboard variables.
    /// </param>
    public static void Register(HsmDefinitionBlob blob, IReadOnlyList<(Guid StableId, int Offset)> bindings)
    {
        if (bindings is null) throw new ArgumentNullException(nameof(bindings));

        var sited = new (Guid, Guid, int)[bindings.Count];
        for (int i = 0; i < bindings.Count; i++)
            sited[i] = (bindings[i].StableId, StateWideSite, bindings[i].Offset);

        Register(blob, sited);
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-414</c> — the same registration, PER HOSTING SITE.</b>
    ///
    /// <para>⭐ <c>SiteId</c> is the hosted blueprint's asset Guid, or <see cref="StateWideSite"/> for
    /// the state's own field — the default every unmatched site resolves to. ⇒ a state's activity
    /// blueprint and the guard blueprint on its outgoing transition, which the kernel stamps with the
    /// SAME state, now seed from their OWN variables.</para>
    ///
    /// <para>⚠ <b>Last writer wins per <c>(state, site)</c>, deliberately.</b> ⛔ The one case it does
    /// not separate is the SAME blueprint asset bound twice at one state (say activity AND timer) — but
    /// that already collides at the occurrence SLOT, whose key is the same <c>(machine, region, state,
    /// childAsset)</c> triple, so this is a pre-existing property of the storage model and not one this
    /// registration introduces. 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §28.6c.</para>
    /// </summary>
    public static void Register(
        HsmDefinitionBlob blob, IReadOnlyList<(Guid StableId, Guid SiteId, int Offset)> bindings)
    {
        if (blob is null) throw new ArgumentNullException(nameof(blob));
        if (bindings is null) throw new ArgumentNullException(nameof(bindings));

        var stableIds = blob.Metadata?.StateStableIds;
        if (stableIds is null || stableIds.Count == 0) return;

        uint machineId = blob.MachineId;   // ⭐ CE-2001 — two same-shape machines no longer share seeds

        // Invert once: StableId -> flat index. ⚠ A machine has tens of states, so this is trivial —
        //   and it happens once per asset at startup, never per frame.
        var indexByStableId = new Dictionary<Guid, ushort>(stableIds.Count);
        foreach (var kv in stableIds)
            indexByStableId[kv.Value] = kv.Key;

        lock (WriteLock)
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                var (stableId, siteId, offset) = bindings[i];
                if (!indexByStableId.TryGetValue(stableId, out ushort stateIndex)) continue;
                _offsets[(machineId, stateIndex, siteId)] = offset;
            }
        }
    }

    /// <summary>
    /// ⭐⭐ The seed offset for the occurrence the kernel has stamped, or <see cref="UnboundOffset"/>.
    ///
    /// <para>⛔ <b>Never throws and never reports "unknown".</b> An unbound state is the COMMON case —
    /// every asset authored before <c>E3b-0</c>, and every state that does not host anything — and its
    /// answer, offset <c>0</c>, is exactly what the seed did before. ⚠ A sentinel here would push a
    /// decision onto the emitted thunk, which is the one place that must stay trivial.</para>
    /// </summary>
    public static int SeedOffsetFor(uint machineId, ushort stateId)
        => SeedOffsetFor(machineId, stateId, StateWideSite);

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-414</c> — the seed offset for ONE HOSTING SITE at the stamped state.</b>
    ///
    /// <para>⭐ <b>Exact match, then the state-wide default, then <see cref="UnboundOffset"/>.</b> The
    /// middle step is what makes this purely additive: an asset that binds only its states registers
    /// under <see cref="StateWideSite"/>, every site resolves to it, and the answer is byte-for-byte
    /// the pre-<c>CE-414</c> one.</para>
    ///
    /// <para>⛔ <b>Never throws and never reports "unknown"</b>, for the reason the overload above
    /// gives: a sentinel would push a decision onto the emitted thunk.</para>
    /// </summary>
    public static int SeedOffsetFor(uint machineId, ushort stateId, Guid siteId)
    {
        if (siteId != StateWideSite && _offsets.TryGetValue((machineId, stateId, siteId), out int sited))
            return sited;

        return _offsets.TryGetValue((machineId, stateId, StateWideSite), out int stateWide)
            ? stateWide
            : UnboundOffset;
    }

    /// <summary>
    /// Drops every registration. ⚠ For hot reload and for test isolation — the registries beside this
    /// one have the same escape hatch, and for the same reason.
    /// </summary>
    public static void ClearAll()
    {
        lock (WriteLock)
        {
            _offsets.Clear();
        }
    }

    /// <summary>How many bindings are registered. ⭐ For rails and the diagnostics surface.</summary>
    public static int Count => _offsets.Count;
}
