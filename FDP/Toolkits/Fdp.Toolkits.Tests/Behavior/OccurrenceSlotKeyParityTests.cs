using System;
using System.Text;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints.Partitioning;
using Xunit;

namespace Fdp.Toolkits.Tests.Behavior
{
    /// <summary>
    /// A1 (<c>PLAN_Occurrence_Storage_Build</c>) — the rails for collapsing THREE hand-copied slot-key
    /// spellings into one.
    ///
    /// <para>⛔⛔ <b>Why byte-identity is the whole point.</b> Slot keys are a PERSISTED IDENTITY: live
    /// slot tables, emitted registrars and saved scenarios all carry keys produced by the previous
    /// copies. A changed key does not throw — <c>TryGetSlotOffset</c> simply never finds the slot, and
    /// the behaviour silently runs on default state. So the oracle below is the OLD algorithm, spelled
    /// out in full: if the shared implementation ever drifts from it, these go red.</para>
    ///
    /// <para>⭐ This is deliberately a restatement of the spec in a test. ⛔ It is NOT a fourth copy to
    /// call from production — nothing outside this file may reference it.</para>
    /// </summary>
    public class OccurrenceSlotKeyParityTests
    {
        private static readonly Guid AssetA = new Guid("1a200000-0000-0000-0000-0000000000dd");
        private static readonly Guid AssetB = new Guid("2b300000-0000-0000-0000-0000000000ee");
        private static readonly Guid NodeA  = new Guid("1a200000-0000-0000-0000-0000000000a1");
        private static readonly Guid NodeB  = new Guid("1a200000-0000-0000-0000-0000000000a2");

        // ── THE ORACLE — the algorithm exactly as it stood before A1 ────────────────────────────
        private static int LegacyKey(Guid assetId, StatefulSlotScope scope, Guid nodeVisualId, string variableId)
        {
            unchecked
            {
                const uint prime = 16777619u;
                uint hash = 2166136261u;
                switch (scope)
                {
                    case StatefulSlotScope.Node:
                        foreach (byte b in assetId.ToByteArray())      { hash ^= b; hash *= prime; }
                        foreach (byte b in nodeVisualId.ToByteArray()) { hash ^= b; hash *= prime; }
                        return (int)(hash & 0x7FFFFFFFu);
                    case StatefulSlotScope.Behavior:
                        foreach (byte b in assetId.ToByteArray())                   { hash ^= b; hash *= prime; }
                        foreach (byte b in Encoding.UTF8.GetBytes(variableId))      { hash ^= b; hash *= prime; }
                        return (int)(hash & 0x7FFFFFFFu);
                    case StatefulSlotScope.Entity:
                        foreach (byte b in Encoding.UTF8.GetBytes(variableId))      { hash ^= b; hash *= prime; }
                        return (int)(hash & 0x7FFFFFFFu);
                    default:
                        throw new ArgumentOutOfRangeException(nameof(scope));
                }
            }
        }

        // ── R1 — BYTE IDENTITY, all three scopes ────────────────────────────────────────────────

        [Theory]
        [InlineData(StatefulSlotScope.Node)]
        [InlineData(StatefulSlotScope.Behavior)]
        [InlineData(StatefulSlotScope.Entity)]
        public void A1_R1_TheUnifiedKey_IsByteIdenticalToTheLegacyAlgorithm(StatefulSlotScope scope)
        {
            const string variableId = "HillAttackState";

            Assert.Equal(
                LegacyKey(AssetA, scope, NodeA, variableId),
                StatefulBTreeActionBinder.ComputeStatefulSlotKey(AssetA, scope, NodeA, variableId));
        }

        /// <summary>
        /// ⚠ ANTI-VACUITY for R1. If the oracle and the implementation both returned a constant —
        /// or both collapsed every scope to one value — R1 would pass over nothing forever.
        /// </summary>
        [Fact]
        public void A1_R1b_TheThreeScopes_ProduceThreeDifferentKeys()
        {
            const string v = "HillAttackState";
            int node     = StatefulBTreeActionBinder.ComputeStatefulSlotKey(AssetA, StatefulSlotScope.Node,     NodeA, v);
            int behavior = StatefulBTreeActionBinder.ComputeStatefulSlotKey(AssetA, StatefulSlotScope.Behavior, NodeA, v);
            int entity   = StatefulBTreeActionBinder.ComputeStatefulSlotKey(AssetA, StatefulSlotScope.Entity,   NodeA, v);

            Assert.NotEqual(node, behavior);
            Assert.NotEqual(behavior, entity);
            Assert.NotEqual(node, entity);
            Assert.All(new[] { node, behavior, entity }, k => Assert.True(k > 0, "a key must be a positive int"));
        }

        /// <summary>
        /// ⛔ The <c>Entity</c> scope EXCLUDES the asset id on purpose — an owner and a member entity
        /// must agree on the key from the variable name alone (<c>BlueprintSharedState</c>).
        /// ⭐ This is the <c>R-137</c> carve-out: the unification may not cost that feature.
        /// </summary>
        [Fact]
        public void A1_R1c_EntityScope_IgnoresTheAssetId_SoTwoAssetsShareTheSlot()
        {
            const string v = "SharedContactPool";

            Assert.Equal(
                StatefulBTreeActionBinder.ComputeStatefulSlotKey(AssetA, StatefulSlotScope.Entity, Guid.Empty, v),
                StatefulBTreeActionBinder.ComputeStatefulSlotKey(AssetB, StatefulSlotScope.Entity, Guid.Empty, v));

            // ...and Behavior scope does NOT — the two scopes must not have collapsed into one.
            Assert.NotEqual(
                StatefulBTreeActionBinder.ComputeStatefulSlotKey(AssetA, StatefulSlotScope.Behavior, Guid.Empty, v),
                StatefulBTreeActionBinder.ComputeStatefulSlotKey(AssetB, StatefulSlotScope.Behavior, Guid.Empty, v));
        }

        // ── R2 — the ROOT case is the identity ──────────────────────────────────────────────────

        /// <summary>
        /// ⭐⭐⭐ <c>hostKey == 0</c> means ROOT and must return the legacy key verbatim. This is what
        /// makes "every existing key is unchanged" provable rather than asserted.
        /// </summary>
        [Theory]
        [InlineData(StatefulSlotScope.Node)]
        [InlineData(StatefulSlotScope.Behavior)]
        [InlineData(StatefulSlotScope.Entity)]
        public void A1_R2_AHostKeyOfZero_IsExactlyTheRootKey(StatefulSlotScope scope)
        {
            const string v = "HillAttackState";

            Assert.Equal(
                LegacyKey(AssetA, scope, NodeA, v),
                StatefulBTreeActionBinder.ComputeOccurrenceSlotKey(
                    hostKey: 0, siteId: 0, AssetA, scope, NodeA, v));
        }

        // ── R3/R4 — NESTING, which is the capability A1 adds ────────────────────────────────────

        /// <summary>
        /// The same asset hosted under TWO different hosts must land in two different slots. This is
        /// the property <c>DESIGN_Occurrence_Scoped_Storage</c> §7's "two occurrences, distinct bytes"
        /// rail rests on, one layer down.
        /// </summary>
        [Fact]
        public void A1_R3_TheSameAssetUnderTwoHosts_GetsTwoDistinctKeys()
        {
            int hostA = StatefulBTreeActionBinder.ComputeStatefulSlotKey(AssetA, StatefulSlotScope.Node, NodeA, "");
            int hostB = StatefulBTreeActionBinder.ComputeStatefulSlotKey(AssetA, StatefulSlotScope.Node, NodeB, "");
            Assert.NotEqual(hostA, hostB);

            int childOfA = StatefulBTreeActionBinder.ComputeOccurrenceSlotKey(hostA, 0, AssetB, StatefulSlotScope.Node, NodeA, "");
            int childOfB = StatefulBTreeActionBinder.ComputeOccurrenceSlotKey(hostB, 0, AssetB, StatefulSlotScope.Node, NodeA, "");

            Assert.NotEqual(childOfA, childOfB);
        }

        /// <summary>
        /// ⭐⭐ Two hosting SITES within one host — the HSM two-regions case. Region 0 and region 1
        /// running the same child asset must not share a slot; that collision is the defect the whole
        /// occurrence model exists to remove.
        /// </summary>
        [Fact]
        public void A1_R4_TwoSitesInOneHost_GetTwoDistinctKeys()
        {
            int host = StatefulBTreeActionBinder.ComputeStatefulSlotKey(AssetA, StatefulSlotScope.Node, NodeA, "");

            int site0 = StatefulBTreeActionBinder.ComputeOccurrenceSlotKey(host, 0, AssetB, StatefulSlotScope.Node, NodeA, "");
            int site1 = StatefulBTreeActionBinder.ComputeOccurrenceSlotKey(host, 1, AssetB, StatefulSlotScope.Node, NodeA, "");

            Assert.NotEqual(site0, site1);
        }

        /// <summary>Depth 2 — a grandchild is distinct from its parent and from a same-site sibling chain.</summary>
        [Fact]
        public void A1_R5_NestingIsRecursive_SoDepthTwoIsRepresentable()
        {
            int root  = StatefulBTreeActionBinder.ComputeStatefulSlotKey(AssetA, StatefulSlotScope.Node, NodeA, "");
            int child = StatefulBTreeActionBinder.ComputeOccurrenceSlotKey(root,  1, AssetB, StatefulSlotScope.Node, NodeA, "");
            int grand = StatefulBTreeActionBinder.ComputeOccurrenceSlotKey(child, 1, AssetB, StatefulSlotScope.Node, NodeA, "");

            Assert.NotEqual(root, child);
            Assert.NotEqual(child, grand);
            Assert.NotEqual(root, grand);
        }

        /// <summary>Determinism — the same inputs give the same key across calls. A key is an identity.</summary>
        [Fact]
        public void A1_R6_TheKeyIsDeterministic()
        {
            int host = StatefulBTreeActionBinder.ComputeStatefulSlotKey(AssetA, StatefulSlotScope.Node, NodeA, "");

            Assert.Equal(
                StatefulBTreeActionBinder.ComputeOccurrenceSlotKey(host, 3, AssetB, StatefulSlotScope.Behavior, NodeB, "S"),
                StatefulBTreeActionBinder.ComputeOccurrenceSlotKey(host, 3, AssetB, StatefulSlotScope.Behavior, NodeB, "S"));
        }

        /// <summary>
        /// ⛔⛔ ENUM PARITY. The shared implementation casts <c>StatefulSlotScope</c> to its own enum by
        /// VALUE. If the numbering ever diverges, every key on one side of the wall changes silently.
        /// ⚠ <c>WorkingStateScope</c> (authoring) is pinned to the same numbering by its own assembly's
        /// tests — it is not referenceable from here.
        /// </summary>
        [Fact]
        public void A1_R7_TheScopeEnumNumbering_IsAbi()
        {
            Assert.Equal(0, (int)StatefulSlotScope.Node);
            Assert.Equal(1, (int)StatefulSlotScope.Behavior);
            Assert.Equal(2, (int)StatefulSlotScope.Entity);
        }
    }
}
