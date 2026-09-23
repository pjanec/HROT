using System;
using System.Collections.Generic;
using Fhsm.Kernel.Data;

namespace Fdp.Toolkit.Behavior;

/// <summary>
/// ⭐⭐⭐ <b><c>E5</c> — WHICH HSM STATES HOST A BTREE, AND UNDER WHICH SLOT KEY.</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.8 item 2.
///
/// <para>⭐⭐ <b>This is <see cref="HsmParamBindings"/>'s shape with a different payload</b>, and the
/// copy is deliberate (ruling 9): the emitter bakes authoring <c>StableId</c>s, and the join to the
/// flat state indices the kernel stamps happens HERE, at registration, through the blob's own
/// <see cref="MachineMetadata.StateStableIds"/>. ⛔ That is what keeps the emitter off the
/// flattener's ordering — the ordering is recovered from the blob, never assumed.</para>
///
/// <para>🔴🔴 <b>Why this table exists at all, rather than a generated <c>[HsmAction]</c>.</b>
/// 📐 Measured <c>2026-09-23</c> (§32.2.1): <c>HsmKernelCore.UpdateBatchCore</c> advances an instance
/// by <b>ONE PHASE PER TICK</b> (<c>:49-71</c>), <c>Idle</c> leaves only on a non-empty event queue
/// (<c>:108-116</c>), and <c>Activity</c> ends by setting <c>Idle</c> (<c>:442-458</c>). ⇒ on a
/// quiescent machine an <c>ActivityAction</c> is dispatched <b>exactly once</b> and an
/// <c>OnEntryAction</c> once per entry — ⛔ <b>neither is a per-frame hook, and a hosted BTree needs
/// a frame cursor.</b> 🔒 So the host is <c>BrainTickSystem</c>, which reads this table
/// (user, <c>2026-09-23</c>: <i>"go with b"</i>). 📋 The one-shot itself is <c>CE-334</c>.</para>
///
/// <para>⚠ <b>Startup-only, like the registries it sits beside.</b> Registration happens during the
/// <c>[BlueprintRegistrar]</c> scan, before the first tick; reads are lock-free thereafter. ⛔ Not
/// safe to mutate mid-frame, and nothing does.</para>
/// </summary>
public static class HsmHostedSubtrees
{
    /// <summary>One hosting state, resolved to the flat index the kernel stamps.</summary>
    /// <param name="StateIndex">The flat state index — what <c>GetActiveLeafIds</c> and
    /// <c>StateDef.ParentIndex</c> speak in.</param>
    /// <param name="ChildName">The child behaviour's REGISTRY name (<c>Q36-B</c> = A).</param>
    /// <param name="TreeStateSlotKey">The child's own <c>BehaviorTreeState</c> slot, from
    /// <c>OccurrenceSlotKey.ComputeTreeStateKey(host, site, child)</c>.</param>
    public readonly record struct Entry(ushort StateIndex, string ChildName, int TreeStateSlotKey);

    // machineId (the blob's StructureHash) -> the hosting states of that machine.
    private static readonly Dictionary<uint, Entry[]> _byMachine = new();

    /// <summary>
    /// ⭐⭐⭐ Registers a machine's hosting states, resolving authoring <c>StableId</c>s to flat state
    /// indices.
    ///
    /// <para>⚠ <b>A blob with no metadata registers NOTHING, silently and correctly</b> — a
    /// hand-built blob in a test has no authoring identity to resolve, and a machine with no hosting
    /// state is the overwhelmingly common case. ⛔ Not an error, and not a warning either: the
    /// emitter only emits this call when at least one state hosts something.</para>
    ///
    /// <para>⛔ <b>A state whose <c>StableId</c> the blob does not know is SKIPPED, not guessed</b> —
    /// the same <i>"fails closed"</i> rule <see cref="HsmParamBindings.Register"/> follows. A host
    /// that silently addressed the wrong state would tick the wrong child.</para>
    /// </summary>
    /// <param name="entries">
    /// <c>(state StableId, child registry name, tree-state slot key)</c> triples, baked by the
    /// generated HSM registrar from each state's <c>SubtreeName</c> and <c>SubtreeAssetId</c>.
    /// </param>
    public static void Register(
        HsmDefinitionBlob blob,
        IReadOnlyList<(Guid StableId, string ChildName, int TreeStateSlotKey)> entries)
    {
        if (blob is null)    throw new ArgumentNullException(nameof(blob));
        if (entries is null) throw new ArgumentNullException(nameof(entries));

        var stableIds = blob.Metadata?.StateStableIds;
        if (stableIds is null || stableIds.Count == 0) return;

        // Invert once: StableId -> flat index. ⚠ Tens of states, once per asset at startup.
        var indexByStableId = new Dictionary<Guid, ushort>(stableIds.Count);
        foreach (var kv in stableIds)
            indexByStableId[kv.Value] = kv.Key;

        var resolved = new List<Entry>(entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            var (stableId, childName, slotKey) = entries[i];
            if (string.IsNullOrEmpty(childName)) continue;
            if (!indexByStableId.TryGetValue(stableId, out ushort stateIndex)) continue;
            resolved.Add(new Entry(stateIndex, childName, slotKey));
        }

        if (resolved.Count == 0) return;

        _byMachine[blob.Header.StructureHash] = resolved.ToArray();
    }

    /// <summary>
    /// ⭐⭐ The hosting states of <paramref name="machineId"/>, or <c>false</c>.
    ///
    /// <para>⭐ <b><c>false</c> is the COMMON case and the caller's fast path</b> — no shipped asset
    /// hosts anything, so <c>BrainTickSystem</c> skips its whole hosted branch on one dictionary
    /// miss. ⛔ Deliberately not an empty array: <i>"has none"</i> should cost a branch, not a loop.
    /// </para>
    /// </summary>
    public static bool TryGetForMachine(uint machineId, out Entry[] entries)
        => _byMachine.TryGetValue(machineId, out entries!);

    /// <summary>
    /// ⚠ Test seam — drops every registration. ⛔ Production never calls this; the registries beside
    /// it are process-lifetime by design.
    /// </summary>
    public static void ClearForTests() => _byMachine.Clear();
}
