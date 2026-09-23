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

        /// <summary>O7 — an HSM-hosted occurrence's working state, per (region, state).</summary>
        internal const string HsmWorkingStateVariableId = ReservedPrefix + "hsmState";

        /// <summary>O7d — a STANDALONE-hosted AiPrimitive's working state, per ASSET.</summary>
        internal const string StandaloneWorkingStateVariableId = ReservedPrefix + "standaloneState";
        private const string HsmSiteVariableId          = ReservedPrefix + "hsmSite";

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

        /// <summary>
        /// ⭐⭐⭐ <c>O7</c> / <c>E3</c> — <b>an HSM-hosted occurrence's working-state slot key, keyed by
        /// the HOSTING MACHINE and by the REGION AND STATE the kernel stamps.</b>
        ///
        /// <para>🔴🔴 <b>The defect this closes (<c>BP-297</c>).</b> Every HSM thunk resolves its
        /// working state as <c>GetComponentRW&lt;Blackboard1024&gt;(bridge-&gt;Self)</c> at a hard-coded
        /// <c>memory + 8</c> — <b>one working state per ENTITY</b>. ⇒ two concurrently-active HSM
        /// regions running the same asset write the SAME bytes, silently. ⭐ The BTree hosting path has
        /// been occurrence-keyed since <c>S2</c>; this is the seam law again — the mechanism existed and
        /// the HSM path was never brought onto it.</para>
        ///
        /// <para>⭐⭐ <b>The site is the PAIR, which is exactly what <c>O6</c> made available.</b>
        /// <c>HsmCommandWriter.OccurrenceRegionSlotIndex</c> + <c>.OccurrenceStateId</c> are stamped
        /// before every dispatch, so a thunk can compute this key without the kernel knowing anything
        /// about the allocator. ⛔ <c>Q35-B</c> ruled the pair rather than a pre-hashed key precisely so
        /// the arithmetic stays here, outside ExtDeps.</para>
        ///
        /// <para>⚠ <b>Both halves are load-bearing.</b> Two regions in the SAME state and one region
        /// moving BETWEEN states are different occurrences; folding only the region would alias the
        /// second, and folding only the state would alias the first.</para>
        /// </summary>
        /// <summary>
        /// ⭐⭐⭐ <b><c>P2</c> — the identity of a CURATED <c>[SharedAiAction]</c>'s occurrence.</b>
        ///
        /// <para>⛔ A curated action has <b>no asset Guid</b> — <c>SharedAiEntry</c> carries a method
        /// FQN, a field type and an offset, and nothing else. ⭐ But it already HAS an identity:
        /// <c>HsmActionKey.CompoundKeyName</c> = <c>fqn + "@" + fieldOffset</c>, which is what the
        /// dispatcher's <c>ushort</c> id is hashed from. ⇒ fold THAT where the Guid goes rather than
        /// inventing a second identity scheme for the same thing (<c>R-132</c>).</para>
        ///
        /// <para>⭐⭐ <b>No new hashing.</b> It reuses <see cref="Compute"/> at
        /// <see cref="OccurrenceSlotScope.Behavior"/> with an EMPTY asset id and the compound key as
        /// the variable id — so a curated occurrence can never collide with a blueprint's, which
        /// always folds a non-empty asset Guid together with <see cref="HsmWorkingStateVariableId"/>.</para>
        ///
        /// <para>⚠ <b>The <c>@offset</c> in the compound key stays MEANINGFUL after <c>P2</c></b> —
        /// it is the field's position inside the action's own declared slot struct, never a
        /// blackboard address, so it survives the params move with only its anchor changed.</para>
        /// </summary>
        /// <summary>
        /// ⭐⭐⭐ <b><c>P3</c> — the ROOT behaviour's own params slot.</b>
        ///
        /// <para>🔒 <b>User, <c>2026-09-21</c>:</b> <i>"the key that leads to the slot allocated for the
        /// behavior params… where is this key stored? maybe where the <c>BrainTier</c> is?"</i>
        /// ⭐⭐ <b>It is not stored at all</b> — <c>BehaviorState.ActiveBehaviorHash</c> already
        /// identifies the root behaviour and is already on the entity, and every key in this file is
        /// COMPUTED by its reader. Storing one would be the novel thing, not computing it.</para>
        ///
        /// <para>⛔ <b>There is no SITE.</b> A hosted occurrence folds a host identity and a site; the
        /// root is the host, so it folds neither — <see cref="ComputeNested"/>'s <c>hostKey == 0</c>
        /// arm returns the plain identity, which is exactly right here and is why no new arithmetic
        /// is introduced.</para>
        /// </summary>
        internal const string RootParamsVariableId = ReservedPrefix + "rootParams";

        /// <summary>
        /// ⭐⭐⭐ <b><c>O7c</c>-② / <c>CE-319</c> — the ROOT behaviour's own EXECUTION STATE slot.</b>
        ///
        /// <para>⭐ The exact shape of <see cref="ComputeRootParamsKey"/>, differing ONLY in the
        /// reserved variable id. ⇒ the two keys cannot collide for the same behaviour hash, because
        /// the variable id is folded into the hash — and no new arithmetic is introduced, which is
        /// what keeps every existing key byte-identical.</para>
        ///
        /// <para>⛔ <b>Not to be confused with <see cref="ComputeTreeStateKey"/></b>, which keys a
        /// HOSTED subtree's state and folds a host identity and a SITE. The root is the host, so it
        /// folds neither — the same reason <see cref="ComputeRootParamsKey"/> takes no site.</para>
        ///
        /// <para>⚠ <b>Why the ROOT state needs a key at all, when a component needed none:</b> a
        /// component is addressed by its TYPE, so there could only ever be one per entity — which is
        /// precisely the limit that stopped a hosted subtree owning its own cursor. A keyed slot is
        /// what makes "multiple trees on one entity" expressible.</para>
        /// </summary>
        internal const string RootStateVariableId = ReservedPrefix + "rootState";

        internal static int ComputeRootStateKey(int behaviourHash)
            => Compute(
                   BehaviourHashAsGuid(behaviourHash),
                   OccurrenceSlotScope.Behavior,
                   System.Guid.Empty,
                   RootStateVariableId);

        /// <summary>
        /// ⭐⭐⭐ <b><c>O7c</c>-④ — the ROOT behaviour's HSM INSTANCE slot.</b>
        ///
        /// <para>⛔⛔ <b>A DISTINCT variable id from <see cref="RootStateVariableId"/>, and the reason is
        /// a MEMORY-SAFETY hazard rather than tidiness.</b> An entity is BTree-tier or HSM-tier, so the
        /// two root execution-state slots never coexist and ONE shared id would look economical. ⚠ But
        /// the <c>ClearBehaviorEvent</c> handler calls <c>RootStateAccess.ResetState</c>
        /// UNCONDITIONALLY — it runs for BTree and HSM brains alike — and that resolves its slot by key
        /// and writes <c>default(BehaviorTreeState)</c>, 64 bytes, through a
        /// <c>BehaviorTreeState*</c>. ⇒ with a shared id it would find an HSM instance and zero the
        /// first 64 bytes of it while reading it as a tree cursor. ⭐ Distinct ids make that
        /// mis-resolution unexpressible rather than merely unlikely.</para>
        ///
        /// <para>⭐ Otherwise the exact shape of <see cref="ComputeRootParamsKey"/>: no new arithmetic,
        /// so every existing key stays byte-identical.</para>
        /// </summary>
        internal const string RootHsmVariableId = ReservedPrefix + "rootHsm";

        internal static int ComputeRootHsmKey(int behaviourHash)
            => Compute(
                   BehaviourHashAsGuid(behaviourHash),
                   OccurrenceSlotScope.Behavior,
                   System.Guid.Empty,
                   RootHsmVariableId);

        internal static int ComputeRootParamsKey(int behaviourHash)
            => Compute(
                   BehaviourHashAsGuid(behaviourHash),
                   OccurrenceSlotScope.Behavior,
                   System.Guid.Empty,
                   RootParamsVariableId);

        /// <summary>
        /// ⚠ The behaviour hash widened into the <c>Guid</c> slot <see cref="Compute"/> folds.
        /// ⛔ Not an identity of its own — just the 4 bytes placed deterministically so both sides of
        /// the netstandard/net8 wall produce the same key.
        /// </summary>
        private static System.Guid BehaviourHashAsGuid(int behaviourHash)
        {
            var b = new byte[16];
            b[0] = (byte)(behaviourHash        & 0xFF);
            b[1] = (byte)((behaviourHash >>  8) & 0xFF);
            b[2] = (byte)((behaviourHash >> 16) & 0xFF);
            b[3] = (byte)((behaviourHash >> 24) & 0xFF);
            return new System.Guid(b);
        }

        internal static string CuratedVariableId(string compoundKey)
            => ReservedPrefix + "curated." + (compoundKey ?? string.Empty);

        internal static int ComputeHsmStateKeyForCurated(
            uint hostMachineId, int regionSlotIndex, ushort stateId, string compoundKey)
            => ComputeNested(
                   ComputeHsmHostIdentity(hostMachineId),
                   ComputeHsmSiteId(regionSlotIndex, stateId),
                   System.Guid.Empty,
                   OccurrenceSlotScope.Behavior,
                   System.Guid.Empty,
                   CuratedVariableId(compoundKey));

        internal static int ComputeHsmStateKey(
            uint hostMachineId, int regionSlotIndex, ushort stateId, System.Guid childAssetId)
            => ComputeNested(
                   ComputeHsmHostIdentity(hostMachineId),
                   ComputeHsmSiteId(regionSlotIndex, stateId),
                   childAssetId,
                   OccurrenceSlotScope.Behavior,
                   System.Guid.Empty,
                   HsmWorkingStateVariableId);

        /// <summary>
        /// ⭐ The HSM hosting SITE, folded from the kernel's <c>(region, state)</c> pair.
        ///
        /// <para>⛔ <b>Not a node <c>Guid</c>, and that is the difference from <c>D5</c>.</b> A BTree site
        /// is an author-placed node with a stable id; an HSM site is a <i>position in the compiled
        /// machine</i>, and the only thing the kernel can hand a thunk is the pair. ⚠ That makes the key
        /// sensitive to a recompile that renumbers states — which is what <c>StructureHash</c> on the
        /// slot is for: the mismatch is detected and the state resets rather than being misread.</para>
        ///
        /// <para>⚠ Folded through the same FNV path as every other site so a site id can never collide
        /// with a raw region index, and never returns 0 for the same reason
        /// <see cref="ComputeIdentity"/> must not.</para>
        /// </summary>
        /// <summary>
        /// ⭐⭐ The HOSTING MACHINE's identity, from the kernel's own <c>InstanceHeader.MachineId</c>
        /// (the HSM definition's <c>StructureHash</c>).
        ///
        /// <para>⛔ <b>Not a <c>Guid</c>, and that is deliberate.</b> The BTree side identifies its host
        /// by asset id because the emitter bakes it; an HSM thunk has no such literal — but it DOES
        /// receive the instance pointer every dispatch, and its header already carries the machine
        /// hash. ⇒ ⭐ <b>a host identity for free, with no new plumbing</b> (§24.9).</para>
        ///
        /// <para>⚠ <b>Why include the host at all when <c>Q4</c> says one machine per entity?</b> It
        /// costs nothing and it is what <c>O8</c> (HSM hosting HSM) will need. ⭐ And it adds no extra
        /// key churn: an HSM edit that changes the structure hash also renumbers state ids, so the
        /// <c>(region, state)</c> half moves anyway.</para>
        /// </summary>
        /// <summary>
        /// ⭐⭐⭐ <c>O7d</c> — <b>a STANDALONE-hosted AiPrimitive's working-state slot key, scoped to the
        /// ASSET.</b>
        ///
        /// <para>⛔⛔ <b>Asset-scoped, and that is FORCED, not chosen.</b> 📐 Measured: the interpreter
        /// hands an action delegate only <c>node.PayloadIndex</c> (<c>Interpreter.cs:655</c>) — <b>no
        /// node identity</b>. ⇒ a single shared thunk CANNOT tell which node it is running for, so it
        /// cannot be keyed per-occurrence without an ExtDeps signature change.</para>
        ///
        /// <para>⭐⭐ <b>And it does not need one: per-node IS the BRIDGE's job.</b>
        /// <c>BTreeBridgeEmitCore</c> emits one adapter per node with the slot key <b>baked</b>
        /// (<c>{MethodFqn}@{offset}@{slotKey}</c>). The standalone thunk is the degenerate
        /// single-occurrence case — exactly what the <c>@0</c> in its own registration key says. ⇒ this
        /// gives that case honest per-ASSET storage instead of a per-ENTITY component.</para>
        ///
        /// <para>🔴 <b>What it replaces.</b> <c>Blackboard1024</c> at a hard-coded <c>memory + 8</c> —
        /// one region for <b>every</b> asset on the entity, in a component production adds to
        /// <b>no entity at all</b>, so the thunk would have thrown if it were ever bound.</para>
        /// </summary>
        internal static int ComputeStandaloneStateKey(System.Guid assetId)
            => Compute(assetId, OccurrenceSlotScope.Behavior, System.Guid.Empty,
                       StandaloneWorkingStateVariableId);

        internal static int ComputeHsmHostIdentity(uint hostMachineId)
        {
            unchecked
            {
                var machine = new System.Guid(
                    hostMachineId, 0, 0,
                    0x4F, 0x43, 0x43, 0x48, 0x4F, 0x53, 0x54, 0x00);   // "OCCHOST"
                return Compute(machine, OccurrenceSlotScope.Behavior, System.Guid.Empty, IdentityVariableId);
            }
        }

        internal static int ComputeHsmSiteId(int regionSlotIndex, ushort stateId)
        {
            unchecked
            {
                // Fold the pair into a deterministic Guid so it goes through the ONE hashing path
                // rather than growing a second, subtly different one.
                var pair = new System.Guid(
                    (uint)regionSlotIndex, stateId, 0,
                    0x4F, 0x43, 0x43, 0x48, 0x53, 0x4D, 0x00, 0x00);   // "OCCHSM"
                return Compute(pair, OccurrenceSlotScope.Node, pair, HsmSiteVariableId);
            }
        }

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
