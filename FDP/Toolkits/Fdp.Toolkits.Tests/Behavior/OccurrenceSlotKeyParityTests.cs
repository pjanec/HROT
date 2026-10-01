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
                    default:
                        throw new ArgumentOutOfRangeException(nameof(scope));
                }
            }
        }

        // ── R1 — BYTE IDENTITY, both scopes ────────────────────────────────────────────────

        [Theory]
        [InlineData(StatefulSlotScope.Node)]
        [InlineData(StatefulSlotScope.Behavior)]
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
        public void A1_R1b_TheTwoScopes_ProduceTwoDifferentKeys()
        {
            const string v = "HillAttackState";
            int node     = StatefulBTreeActionBinder.ComputeStatefulSlotKey(AssetA, StatefulSlotScope.Node,     NodeA, v);
            int behavior = StatefulBTreeActionBinder.ComputeStatefulSlotKey(AssetA, StatefulSlotScope.Behavior, NodeA, v);

            Assert.NotEqual(node, behavior);
            Assert.All(new[] { node, behavior }, k => Assert.True(k > 0, "a key must be a positive int"));
        }

        // ⛔ HISTORY — A1_R1c pinned that the Entity scope ignored the asset id (the BlueprintSharedState carve-out,
        //   R-137). Entity was removed by CE-441 slice 1 (Q76 §12.25) after CE-440 removed its only consumer.

        // ── R2 — the ROOT case is the identity ──────────────────────────────────────────────────

        /// <summary>
        /// ⭐⭐⭐ <c>hostKey == 0</c> means ROOT and must return the legacy key verbatim. This is what
        /// makes "every existing key is unchanged" provable rather than asserted.
        /// </summary>
        [Theory]
        [InlineData(StatefulSlotScope.Node)]
        [InlineData(StatefulSlotScope.Behavior)]
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
            // ⛔ 2 was Entity — retired by CE-441 slice 1, never to be reused.
            Assert.False(Enum.IsDefined(typeof(StatefulSlotScope), (byte)2));
        }

        // ══ CE-505 — THE KEYS ARE COMPUTED WITHOUT ALLOCATING, AND STILL BYTE-IDENTICAL ══════════
        //   🔒 User 2026-10-01: "there should be no allocation on the hot path." Every brain tick computes
        //   root keys per entity, and every HSM activity/guard computes an occurrence key per call; the
        //   folds used Guid.ToByteArray() + Encoding.UTF8.GetBytes. ⭐ The oracles below are the ARRAY
        //   form, spelled out — the in-place folds must reproduce them for every surface and every input.

        private static int LegacyNest(int hostKey, int siteId, int ownKey)
        {
            if (hostKey == 0) return ownKey;
            unchecked
            {
                const uint prime = 16777619u;
                uint hash = 2166136261u;
                foreach (int v in new[] { hostKey, siteId, ownKey })
                    foreach (byte b in BitConverter.GetBytes(v)) { hash ^= b; hash *= prime; }
                return (int)(hash & 0x7FFFFFFFu);
            }
        }

        private static Guid LegacyHashGuid(int behaviourHash)
        {
            var b = new byte[16];
            b[0] = (byte)(behaviourHash & 0xFF);         b[1] = (byte)((behaviourHash >> 8) & 0xFF);
            b[2] = (byte)((behaviourHash >> 16) & 0xFF); b[3] = (byte)((behaviourHash >> 24) & 0xFF);
            return new Guid(b);
        }

        private static int LegacyHsmHost(uint machine)
            => LegacyKey(new Guid(machine, 0, 0, 0x4F, 0x43, 0x43, 0x48, 0x4F, 0x53, 0x54, 0x00),
                         StatefulSlotScope.Behavior, Guid.Empty, "$occ.identity");

        private static int LegacyHsmSite(int region, ushort state)
        {
            var pair = new Guid((uint)region, state, 0, 0x4F, 0x43, 0x43, 0x48, 0x53, 0x4D, 0x00, 0x00);
            return LegacyKey(pair, StatefulSlotScope.Node, pair, "$occ.hsmSite");
        }

        /// <summary>Strings that exercise every UTF-8 width, a surrogate pair and both lone-surrogate cases.</summary>
        private static readonly string[] Utf8Cases =
        {
            "", "HillAttackState", "Größe", "日本語", "x😀y", "\uD800lone", "tail\uDC00", "\uDBFF", "߿ࠀ￿",
        };

        [Fact]
        public void CE505_R1_EveryKeySurface_IsByteIdenticalToTheArrayForm_OverManyInputs()
        {
            var rng = new Random(505);
            Guid G() { var b = new byte[16]; rng.NextBytes(b); return new Guid(b); }

            int checkedCount = 0;
            for (int i = 0; i < 400; i++)
            {
                Guid a = G(), n = G(), c = G();
                int h = rng.Next(int.MinValue, int.MaxValue);
                uint machine = (uint)rng.Next() ^ ((uint)rng.Next() << 1);
                int region = rng.Next(0, 8);
                ushort state = (ushort)rng.Next(0, 65536);
                string v = Utf8Cases[i % Utf8Cases.Length] + (i % 3 == 0 ? "" : i.ToString());

                Assert.Equal(LegacyKey(a, StatefulSlotScope.Node, n, v),
                             StatefulBTreeActionBinder.ComputeStatefulSlotKey(a, StatefulSlotScope.Node, n, v));
                Assert.Equal(LegacyKey(a, StatefulSlotScope.Behavior, n, v),
                             StatefulBTreeActionBinder.ComputeStatefulSlotKey(a, StatefulSlotScope.Behavior, n, v));

                if (h != 0)
                {
                    Assert.Equal(LegacyKey(LegacyHashGuid(h), StatefulSlotScope.Behavior, Guid.Empty, "$occ.rootParams"),
                                 RootParamsAccess.KeyForBehaviour(h));
                    Assert.Equal(LegacyKey(LegacyHashGuid(h), StatefulSlotScope.Behavior, Guid.Empty, "$occ.rootState"),
                                 RootStateAccess.KeyForBehaviour(h));
                    Assert.Equal(LegacyKey(LegacyHashGuid(h), StatefulSlotScope.Behavior, Guid.Empty, "$occ.rootHsm"),
                                 RootHsmAccess.KeyForBehaviour(h));
                }

                Assert.Equal(LegacyNest(LegacyHsmHost(machine), LegacyHsmSite(region, state),
                                        LegacyKey(c, StatefulSlotScope.Behavior, Guid.Empty, "$occ.hsmState")),
                             HsmOccurrence.KeyFor(machine, c, region, state));
                Assert.Equal(LegacyNest(LegacyHsmHost(machine), LegacyHsmSite(region, state),
                                        LegacyKey(Guid.Empty, StatefulSlotScope.Behavior, Guid.Empty, "$occ.curated." + v)),
                             HsmOccurrence.KeyForCurated(machine, v, region, state));
                Assert.Equal(LegacyNest(LegacyKey(a, StatefulSlotScope.Behavior, Guid.Empty, "$occ.identity"),
                                        LegacyKey(n, StatefulSlotScope.Node, n, "$occ.site"),
                                        LegacyKey(c, StatefulSlotScope.Behavior, Guid.Empty, "$occ.treeState")),
                             OccurrenceSlots.TreeStateKeyFor(a, n, c));
                Assert.Equal(LegacyKey(a, StatefulSlotScope.Behavior, Guid.Empty, "$occ.standaloneState"),
                             OccurrenceSlots.StandaloneStateKeyFor(a));
                checkedCount++;
            }
            Assert.Equal(400, checkedCount);   // non-vacuity: the loop really ran
        }

        [Fact]
        public void CE505_R2_ComputingAKey_AllocatesNothing()
        {
            Guid a = AssetA, n = NodeA;
            const string v = "x😀Größe";
            const string ck = "Hrot.AI.Behaviors.Brains.Nodes.Action_X@8";
            int sink = 0;

            void Run()
            {
                for (int i = 0; i < 200; i++)
                {
                    sink ^= StatefulBTreeActionBinder.ComputeStatefulSlotKey(a, StatefulSlotScope.Node, n, v);
                    sink ^= StatefulBTreeActionBinder.ComputeStatefulSlotKey(a, StatefulSlotScope.Behavior, n, v);
                    sink ^= RootParamsAccess.KeyForBehaviour(i + 1);
                    sink ^= RootStateAccess.KeyForBehaviour(i + 1);
                    sink ^= RootHsmAccess.KeyForBehaviour(i + 1);
                    sink ^= HsmOccurrence.KeyFor(0xC0FFEEu, a, 1, (ushort)i);
                    sink ^= HsmOccurrence.KeyForCurated(0xC0FFEEu, ck, 1, (ushort)i);
                    sink ^= OccurrenceSlots.TreeStateKeyFor(a, n, AssetB);
                }
            }

            Run();   // JIT + statics outside the measured window
            long before = GC.GetAllocatedBytesForCurrentThread();
            Run();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.True(allocated == 0, $"computing occurrence keys allocated {allocated} bytes over 1600 calls (sink {sink})");
        }
    }
}
