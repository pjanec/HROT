namespace Fdp.Toolkit.Behavior.Shared
{
    /// <summary>
    /// Scope of a stateful slot, in the ONE spelling both sides of the netstandard/net8 wall agree on.
    ///
    /// <para>⛔⛔ The numeric values are <b>ABI</b>. They must equal
    /// <c>Fdp.Toolkit.Blueprints.Partitioning.StatefulSlotScope</c> (runtime) and
    /// <c>Hrot.AiEditor.Persistence.WorkingStateScope</c> (authoring) value for value, because both
    /// sides cast to this enum and a divergence silently changes every key computed on one side.
    /// <c>OccurrenceSlotKeyParityTests</c> pins all three.</para>
    /// </summary>
    internal enum OccurrenceSlotScope
    {
        /// <summary>Per-node local slot. The key folds in the node's visual id.</summary>
        Node = 0,

        /// <summary>Shared by every node in ONE asset that binds the same variable.</summary>
        Behavior = 1,

        /// <summary>
        /// Shared across behaviours on an entity — the key folds in the variable name ONLY.
        /// ⛔ <b>Deliberate</b>: an owner and a member entity must agree on the key from the name
        /// alone (<c>BlueprintSharedState</c>). This is the carve-out to
        /// <c>DESIGN_Occurrence_Scoped_Storage</c> §3's thesis sentence — the occurrence owns memory
        /// EXCEPT where a scope deliberately shares it (<c>R-137</c>: unification may not cost a feature).
        /// </summary>
        Entity = 2,
    }

    /// <summary>
    /// THE one spelling of "which slot do these bytes live in".
    ///
    /// <para><b>Why this file exists.</b> The same FNV-1a-32 was hand-copied into <b>three</b> entry
    /// points across <b>two</b> enums — <c>StatefulBTreeActionBinder.ComputeStatefulSlotKey</c>
    /// (runtime, <c>StatefulSlotScope</c>), <c>BTreeBridgeEmitCore.ComputeStatefulSlotKey</c>
    /// (authoring, <c>WorkingStateScope</c>) and its 2-arg overload — plus four more copies in test
    /// fixtures. A compile-time key and a runtime key that disagree by one byte do not fail loudly:
    /// the slot is simply never found. This is <c>DESIGN_Occurrence_Scoped_Storage</c> §3's <c>F5</c>,
    /// and paying it down is task <c>A1</c> of <c>PLAN_Occurrence_Storage_Build</c>.</para>
    ///
    /// <para><b>One home, not a mirror.</b> Compiled into <c>Fdp.Toolkits</c> (net8.0) and
    /// <b>linked</b> into <c>Hrot.AiEditor.Persistence</c> (netstandard2.0) — a linked source file
    /// crosses the wall an assembly reference cannot, because the authoring assembly deliberately
    /// carries NO project references. Same pattern and same reasoning as
    /// <c>BlackboardParamsExpression</c> (<c>BP-306</c>). It is <c>internal</c> on both sides, so a
    /// project referencing both sees neither copy and no <c>CS0436</c> arises; each assembly exposes
    /// its own thin <c>public</c> wrapper.</para>
    ///
    /// <para>⚠ <b>Scope of this file: the STATEFUL SLOT key only.</b> ⛔ It is NOT the whole FNV
    /// story in this repo — <c>Fdp.Toolkits.Analyzers</c> carries four more 32-bit FNV-1a copies
    /// (<c>HsmActionKey:36</c>, <c>BTreeActionGenerator:786</c>, <c>UtilityInputGenerator:249</c>,
    /// <c>UtilityDecisionGenerator:350</c>). Those hash ACTION NAMES and utility inputs, not slots,
    /// so they are a different identity and out of <c>A1</c>'s scope. ⭐ Recorded here because they
    /// are the same duplication shape and will read as candidates to anyone tidying this area.</para>
    ///
    /// <para>⛔ <b>netstandard2.0 subset only.</b> No <c>Span</c>, no <c>System.HashCode</c>, no
    /// net8-only BCL. <c>Guid.ToByteArray()</c> and <c>Encoding.UTF8.GetBytes</c> are the whole
    /// surface, deliberately.</para>
    /// </summary>
    internal static class OccurrenceSlotKey
    {
        private const uint FnvOffsetBasis = 2166136261u;
        private const uint FnvPrime       = 16777619u;

        /// <summary>
        /// The ROOT slot key — byte-identical to every spelling this file replaces.
        ///
        /// <para>⛔⛔ <b>The output of this method is a PERSISTED IDENTITY.</b> Live slot tables,
        /// emitted registrars and saved scenarios all carry keys produced by the previous copies, so
        /// a changed value orphans every existing slot silently. <c>OccurrenceSlotKeyParityTests</c>
        /// pins the exact integers for all three scopes; ⛔ do not "tidy" the byte order, the mask or
        /// the seed.</para>
        /// </summary>
        /// <param name="assetId">Ignored for <see cref="OccurrenceSlotScope.Entity"/>.</param>
        /// <param name="nodeVisualId">Consumed ONLY for <see cref="OccurrenceSlotScope.Node"/>.</param>
        /// <param name="variableId">Consumed for <see cref="OccurrenceSlotScope.Behavior"/> and
        /// <see cref="OccurrenceSlotScope.Entity"/>. Pass <see cref="string.Empty"/> for Node.</param>
        internal static int Compute(
            System.Guid assetId,
            OccurrenceSlotScope scope,
            System.Guid nodeVisualId,
            string variableId)
        {
            unchecked
            {
                uint hash = FnvOffsetBasis;

                switch (scope)
                {
                    case OccurrenceSlotScope.Node:
                        hash = FoldBytes(hash, assetId.ToByteArray());
                        hash = FoldBytes(hash, nodeVisualId.ToByteArray());
                        break;

                    case OccurrenceSlotScope.Behavior:
                        hash = FoldBytes(hash, assetId.ToByteArray());
                        hash = FoldBytes(hash, System.Text.Encoding.UTF8.GetBytes(variableId ?? string.Empty));
                        break;

                    case OccurrenceSlotScope.Entity:
                        // ⛔ assetId is EXCLUDED on purpose — see OccurrenceSlotScope.Entity.
                        hash = FoldBytes(hash, System.Text.Encoding.UTF8.GetBytes(variableId ?? string.Empty));
                        break;

                    default:
                        throw new System.ArgumentOutOfRangeException(
                            nameof(scope), scope, "Unknown stateful slot scope.");
                }

                return Mask(hash);
            }
        }

        /// <summary>
        /// The NESTED occurrence key — <c>DESIGN_Occurrence_Scoped_Storage</c> §3's
        /// <c>(assetId, hostPath)</c>, where <c>hostPath</c> is the chain of
        /// <c>(hostOccurrence, siteId)</c> pairs from the root.
        ///
        /// <para>⭐⭐ <b>The chain is folded RECURSIVELY through the parent's key</b>, which is why an
        /// arbitrary depth needs no format change and no wider field: the parent key is already a
        /// 31-bit int, so depth N costs the same as depth 1. <c>hostKey</c> for a child is simply the
        /// value this method returned for its host.</para>
        ///
        /// <para>⭐⭐⭐ <b><paramref name="hostKey"/> == 0 means ROOT and returns
        /// <see cref="Compute"/> verbatim</b> — that is what keeps every existing key byte-identical.
        /// ⛔ A root occurrence must never be given a synthetic non-zero host.</para>
        ///
        /// <para>⚠ <b>Collision domain.</b> The result is 31 bits, as it has always been. The slot
        /// population per entity is bounded by the tier's <c>MaxSlots</c> (16 today), so a collision
        /// is remote — ⛔ but the failure mode is silent cross-occurrence aliasing, which is precisely
        /// what this design exists to eliminate. <c>TryAttach</c> therefore asserts that a key it is
        /// about to add is not already present with a DIFFERENT structure hash
        /// (<c>PLAN_Occurrence_Storage_Build</c> §3-W2).</para>
        /// </summary>
        /// <param name="hostKey">The host occurrence's key, or 0 for a root occurrence.</param>
        /// <param name="siteId">
        /// Which hosting site within the host — an HSM <c>(regionSlotIndex, stateId)</c> pair packed
        /// by the caller, or a BTree node ordinal. ⛔ Must be stable across a recompile, or the child's
        /// slot moves; <c>StructureHash</c> catches the drift but the state is lost.
        /// </param>
        // ── O4: the RESERVED names and the occurrence arithmetic ────────────────────────────
        //
        // ⭐⭐⭐ THESE LIVE HERE, IN THE LINKED FILE, FOR THE SAME REASON THE KEY ITSELF DOES.
        //   The generated orchestrator (netstandard2.0, Hrot.AiEditor.Persistence) and the runtime
        //   (net8.0, Fdp.Toolkits) must compute byte-identical keys — D3 says the emitter's bake is
        //   an optimisation over the same arithmetic a hand-written host runs. ⛔ Spelling "$occ."
        //   on both sides of the wall is precisely the duplication A1 exists to kill, and
        //   BlueprintTierLadder already paid this bill once for the tier numbers (B3②, §17.5).

        /// <summary>The prefix no author-chosen blackboard variable can carry.</summary>
        internal const string ReservedPrefix = "$occ.";

        /// <summary>A hosted occurrence's own <c>BehaviorTreeState</c>.</summary>
        internal const string TreeStateVariableId = ReservedPrefix + "treeState";

        private const string IdentityVariableId = ReservedPrefix + "identity";
        private const string SiteVariableId     = ReservedPrefix + "site";

        /// <summary>
        /// ⭐⭐ An occurrence's IDENTITY — a number, never storage.
        /// ⛔ Must be NON-ZERO: <see cref="ComputeNested"/> treats <c>hostKey == 0</c> as ROOT and
        /// drops <c>siteId</c>, which would collide two sites hosting the same child asset.
        /// </summary>
        internal static int ComputeIdentity(System.Guid assetId)
            => Compute(assetId, OccurrenceSlotScope.Behavior, System.Guid.Empty, IdentityVariableId);

        /// <summary>⭐ <c>D5</c> — the hosting SITE, from the author's stable node id. ⛔ Not an ordinal.</summary>
        internal static int ComputeSiteId(System.Guid nodeVisualId)
            => Compute(nodeVisualId, OccurrenceSlotScope.Node, nodeVisualId, SiteVariableId);

        /// <summary>⭐⭐ The hosted child's tree-state slot key — THE one function both paths call.</summary>
        internal static int ComputeTreeStateKey(
            System.Guid hostAssetId, System.Guid siteNodeVisualId, System.Guid childAssetId)
            => ComputeNested(
                   ComputeIdentity(hostAssetId),
                   ComputeSiteId(siteNodeVisualId),
                   childAssetId,
                   OccurrenceSlotScope.Behavior,
                   System.Guid.Empty,
                   TreeStateVariableId);

        internal static int ComputeNested(
            int hostKey,
            int siteId,
            System.Guid assetId,
            OccurrenceSlotScope scope,
            System.Guid nodeVisualId,
            string variableId)
        {
            // ⭐ The root case is the identity, not a special case bolted on: it is what makes A1's
            //    "existing keys come out byte-identical" success condition provable.
            if (hostKey == 0)
                return Compute(assetId, scope, nodeVisualId, variableId);

            unchecked
            {
                uint hash = FnvOffsetBasis;
                hash = FoldInt32(hash, hostKey);
                hash = FoldInt32(hash, siteId);
                // Fold the occurrence's OWN identity through the same scope rules, so a hosted child
                // keyed Node/Behavior/Entity still means what it means at the root.
                hash = FoldInt32(hash, Compute(assetId, scope, nodeVisualId, variableId));
                return Mask(hash);
            }
        }

        private static uint FoldBytes(uint hash, byte[] bytes)
        {
            unchecked
            {
                for (int i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= FnvPrime;
                }
                return hash;
            }
        }

        private static uint FoldInt32(uint hash, int value)
        {
            unchecked
            {
                uint v = (uint)value;
                for (int shift = 0; shift < 32; shift += 8)
                {
                    hash ^= (byte)(v >> shift);
                    hash *= FnvPrime;
                }
                return hash;
            }
        }

        /// <summary>Masked to a non-negative int — 0 is reserved for "no host" / "unused slot".</summary>
        private static int Mask(uint hash) => unchecked((int)(hash & 0x7FFFFFFFu));
    }
}
