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

    // (machineId, stateIndex) -> packed byte offset of the bound variable.
    private static readonly Dictionary<(uint MachineId, ushort StateId), int> _offsets = new();

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
        if (blob is null) throw new ArgumentNullException(nameof(blob));
        if (bindings is null) throw new ArgumentNullException(nameof(bindings));

        var stableIds = blob.Metadata?.StateStableIds;
        if (stableIds is null || stableIds.Count == 0) return;

        uint machineId = blob.Header.StructureHash;

        // Invert once: StableId -> flat index. ⚠ A machine has tens of states, so this is trivial —
        //   and it happens once per asset at startup, never per frame.
        var indexByStableId = new Dictionary<Guid, ushort>(stableIds.Count);
        foreach (var kv in stableIds)
            indexByStableId[kv.Value] = kv.Key;

        for (int i = 0; i < bindings.Count; i++)
        {
            var (stableId, offset) = bindings[i];
            if (!indexByStableId.TryGetValue(stableId, out ushort stateIndex)) continue;
            _offsets[(machineId, stateIndex)] = offset;
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
        => _offsets.TryGetValue((machineId, stateId), out int offset) ? offset : UnboundOffset;

    /// <summary>
    /// Drops every registration. ⚠ For hot reload and for test isolation — the registries beside this
    /// one have the same escape hatch, and for the same reason.
    /// </summary>
    public static void ClearAll() => _offsets.Clear();

    /// <summary>How many bindings are registered. ⭐ For rails and the diagnostics surface.</summary>
    public static int Count => _offsets.Count;
}
