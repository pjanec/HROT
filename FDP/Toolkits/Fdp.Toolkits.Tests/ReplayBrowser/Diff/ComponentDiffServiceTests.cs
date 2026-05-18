using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Diff;
using Fdp.Toolkit.Scenario;
using Xunit;

namespace Fdp.Toolkit.ReplayBrowser.Diff
{
    public class ComponentDiffServiceTests : IDisposable
    {
        private readonly ComponentDiffService _svc = new();

        public ComponentDiffServiceTests()
        {
            ComponentTypeRegistry.Clear();
        }

        public void Dispose() { }

        // ── DIF-T01: Identical objects produce IsModified==false ───────────────

        [Fact]
        public void DIF_T01_IdenticalObjects_IsModifiedFalse()
        {
            var a = JsonNode.Parse("""{"X": 1, "Y": 2}""")!;
            var b = JsonNode.Parse("""{"X": 1, "Y": 2}""")!;

            DiffNode? root = _svc.ComputeDiff("root", a, b, 0.001);

            Assert.NotNull(root);
            Assert.False(root.IsModified, "Identical objects should produce IsModified==false on root.");

            // No DiffValue with IsModified==true
            AssertNoModifiedLeaf(root);
        }

        // ── DIF-T02: Single leaf change propagates IsModified up ───────────────

        [Fact]
        public void DIF_T02_SingleLeafChange_PropagatesIsModifiedUp()
        {
            var a = JsonNode.Parse("""{"X": 1}""")!;
            var b = JsonNode.Parse("""{"X": 2}""")!;

            DiffNode? root = _svc.ComputeDiff("root", a, b, 0.001);

            Assert.NotNull(root);
            Assert.True(root.IsModified, "Root should be modified when a leaf changes.");

            var rootObj = Assert.IsType<DiffObject>(root);
            var xLeaf = Assert.IsType<DiffValue>(rootObj.Children[0]);
            Assert.Equal("X", xLeaf.Name);
            Assert.True(xLeaf.IsModified);
        }

        // ── DIF-T03: Disjoint keys ─────────────────────────────────────────────

        [Fact]
        public void DIF_T03_DisjointKeys_EmittedWithNullSides()
        {
            var a = JsonNode.Parse("""{"A": 1}""")!;
            var b = JsonNode.Parse("""{"B": 2}""")!;

            DiffNode? root = _svc.ComputeDiff("root", a, b, 0.001);

            Assert.NotNull(root);
            var rootObj = Assert.IsType<DiffObject>(root);

            // Find the "A" leaf (only in old)
            DiffValue? aLeaf = null;
            DiffValue? bLeaf = null;
            foreach (DiffNode child in rootObj.Children)
            {
                if (child is DiffValue dv)
                {
                    if (dv.Name == "A") aLeaf = dv;
                    if (dv.Name == "B") bLeaf = dv;
                }
            }

            Assert.NotNull(aLeaf);
            Assert.Equal("null", aLeaf!.NewValue);
            Assert.True(aLeaf.IsModified);

            Assert.NotNull(bLeaf);
            Assert.Equal("null", bLeaf!.OldValue);
            Assert.True(bLeaf.IsModified);
        }

        // ── DIF-T04: Numeric epsilon ───────────────────────────────────────────

        [Fact]
        public void DIF_T04_NumericEpsilon_BelowEpsilonNotModified_AboveEpsilonModified()
        {
            // Difference 0.0005 < 0.001 epsilon → not modified
            var a1 = JsonNode.Parse("""{"X": 0.1}""")!;
            var b1 = JsonNode.Parse("""{"X": 0.1005}""")!;
            DiffNode? r1 = _svc.ComputeDiff("root", a1, b1, 0.001);
            Assert.False(r1!.IsModified, "Below epsilon: should not be modified.");

            // Difference 0.002 > 0.001 epsilon → modified
            var a2 = JsonNode.Parse("""{"X": 0.1}""")!;
            var b2 = JsonNode.Parse("""{"X": 0.102}""")!;
            DiffNode? r2 = _svc.ComputeDiff("root", a2, b2, 0.001);
            Assert.True(r2!.IsModified, "Above epsilon: should be modified.");
        }

        // ── DIF-T05: Mixed type leaf ───────────────────────────────────────────

        [Fact]
        public void DIF_T05_MixedTypeLeaf_EmittedAsModified()
        {
            var a = JsonNode.Parse("""{"V": 42}""")!;
            var b = JsonNode.Parse("""{"V": "hello"}""")!;

            DiffNode? root = _svc.ComputeDiff("root", a, b, 0.001);

            Assert.NotNull(root);
            var rootObj = Assert.IsType<DiffObject>(root);
            var leaf = Assert.IsType<DiffValue>(rootObj.Children[0]);

            Assert.Equal("V", leaf.Name);
            Assert.True(leaf.IsModified);
            Assert.Equal(JsonValueKind.String, leaf.ValueType);
        }

        // ── DIF-T06: Arrays differing produce single modified leaf ─────────────

        [Fact]
        public void DIF_T06_ArrayDiff_ProducesSingleModifiedLeaf()
        {
            var a = JsonNode.Parse("""{"Arr": [1, 2, 3]}""")!;
            var b = JsonNode.Parse("""{"Arr": [1, 2, 4]}""")!;

            DiffNode? root = _svc.ComputeDiff("root", a, b, 0.001);

            Assert.NotNull(root);
            var rootObj = Assert.IsType<DiffObject>(root);

            // Should produce exactly ONE child for the array (not three leaf values)
            Assert.Equal(1, rootObj.Children.Count);
            var arrLeaf = Assert.IsType<DiffValue>(rootObj.Children[0]);
            Assert.Equal("Arr", arrLeaf.Name);
            Assert.True(arrLeaf.IsModified);

            // OldValue and NewValue should be full array JSON strings
            Assert.Contains("[", arrLeaf.OldValue);
            Assert.Contains("[", arrLeaf.NewValue);
        }

        // ── DIF-T07: ComputeEntityDiff calls applyStepFunc exactly once ────────

        [Fact]
        public void DIF_T07_ComputeEntityDiff_CallsApplyStepFuncExactlyOnce()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<HarnessDiffPos>();
            var entity = repo.CreateEntity();
            repo.SetComponent(entity, new HarnessDiffPos { X = 1f });

            var serializer = new ScenarioSerializerBuilder("DiffTest").Build();

            int callCount = 0;
            _svc.ComputeEntityDiff(entity, repo, serializer, () => callCount++);

            Assert.Equal(1, callCount);
        }

        // ── DIF-T08: ComputeEntityDiff with no living entity returns empty list ─

        [Fact]
        public void DIF_T08_ComputeEntityDiff_DeadEntity_ReturnsEmptyList()
        {
            using var repo = new EntityRepository();
            // Fresh repo with no entities
            var serializer = new ScenarioSerializerBuilder("DiffTest").Build();
            Entity ghost = new Entity(99, 1); // does not exist in repo

            IReadOnlyList<DiffNode> result = _svc.ComputeEntityDiff(ghost, repo, serializer, () => { });

            Assert.Empty(result);
        }

        // ── DIF-T09: Allocation budget ─────────────────────────────────────────

        [Fact]
        public void DIF_T09_AllocationBudget_1000Calls_Under300MB()
        {
            // Pre-build the JSON string once — parse it per iteration to satisfy JsonNode's
            // ownership model (each JsonNode can belong to only one parent at a time).
            // This isolates the measurement to parse + diff + output, not to JsonObject
            // builder calls.
            var sb = new System.Text.StringBuilder();
            sb.Append("{");
            for (int i = 0; i < 200; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"Prop{i}\":{(double)i}");
            }
            sb.Append("}");
            string jsonStr = sb.ToString();

            // Warm up JIT
            {
                var a = JsonNode.Parse(jsonStr)!;
                var b = JsonNode.Parse(jsonStr)!;
                _svc.ComputeDiff("root", a, b, 0.001);
            }

            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            long allocBefore = GC.GetTotalAllocatedBytes(true);

            for (int i = 0; i < 1000; i++)
            {
                var a = JsonNode.Parse(jsonStr)!;
                var b = JsonNode.Parse(jsonStr)!;
                _svc.ComputeDiff("root", a, b, 0.001);
            }

            long allocAfter = GC.GetTotalAllocatedBytes(true);
            long allocatedBytes = allocAfter - allocBefore;

            // 300 MB budget: each call parses two 200-field JSON objects + runs the diff.
            // This guards against algorithmic allocation regressions without being
            // sensitive to JsonNode's baseline overhead (~200 KB/call from JSON parsing).
            Assert.True(allocatedBytes < 300L * 1024 * 1024,
                $"Allocated {allocatedBytes / 1024} KB for 1000 calls; expected < 300 MB.");
        }

        // ── DIF-T10: Same tree diffed twice produces no modifications ──────────

        [Fact]
        public void DIF_T10_SameTree_DiffedTwice_NoModificationsSecondTime()
        {
            var a = JsonNode.Parse("""{"X": 1, "Y": 2}""")!;
            var b = JsonNode.Parse("""{"X": 1, "Y": 2}""")!;

            DiffNode? r1 = _svc.ComputeDiff("root", a, b, 0.001);
            DiffNode? r2 = _svc.ComputeDiff("root", a, b, 0.001);

            Assert.False(r1!.IsModified);
            Assert.False(r2!.IsModified);
        }

        // ── DIF-T11: ComputeTreeDiff(null, postState, e) — entity birth ────────

        [Fact]
        public void DIF_T11_ComputeTreeDiff_NullBefore_AllModified()
        {
            var post = JsonNode.Parse("""{"X": 1, "Y": 2, "Z": 3}""")!;

            IReadOnlyList<DiffNode> diffs = _svc.ComputeTreeDiff(null, post, 0.001);

            // All leaves should be modified
            Assert.NotEmpty(diffs);
            int modifiedCount = CountModifiedLeaves(diffs);
            Assert.True(modifiedCount >= 3, $"Expected at least 3 modified leaves, got {modifiedCount}.");

            // All should have OldValue == "null"
            AssertAllOldValueNull(diffs);
        }

        // ── DIF-T12: ComputeTreeDiff(preState, null, e) — entity death ─────────

        [Fact]
        public void DIF_T12_ComputeTreeDiff_NullAfter_AllModifiedWithNullNewValue()
        {
            var pre = JsonNode.Parse("""{"X": 1, "Y": 2}""")!;

            IReadOnlyList<DiffNode> diffs = _svc.ComputeTreeDiff(pre, null, 0.001);

            Assert.NotEmpty(diffs);

            // All leaves should have NewValue == "null"
            AssertAllNewValueNull(diffs);
        }

        // ── DIF-T13: Hide-unchanged pruning rule ───────────────────────────────

        [Fact]
        public void DIF_T13_HideUnchangedPruning_VisitsOnlyModifiedChain()
        {
            // Build the tree manually:
            // DiffObject("root")
            //   DiffObject("SimTransform")
            //     DiffObject("Position")
            //       DiffObject("Inner")
            //         DiffValue("X", "1", "2", IsModified=true)   ← the only modified node
            //         DiffValue("Y", "0", "0", IsModified=false)   ← should be pruned

            var innerX = new DiffValue("X", "1", "2", JsonValueKind.Number, isModified: true);
            var innerY = new DiffValue("Y", "0", "0", JsonValueKind.Number, isModified: false);
            var inner = new DiffObject("Inner");
            inner.Children.Add(innerX);
            inner.Children.Add(innerY);
            inner.EvaluateModificationState();

            var position = new DiffObject("Position");
            position.Children.Add(inner);
            position.EvaluateModificationState();

            var simTransform = new DiffObject("SimTransform");
            simTransform.Children.Add(position);
            simTransform.EvaluateModificationState();

            var root = new DiffObject("root");
            root.Children.Add(simTransform);
            root.EvaluateModificationState();

            // Verify chain is modified
            Assert.True(root.IsModified);
            Assert.True(simTransform.IsModified);
            Assert.True(position.IsModified);
            Assert.True(inner.IsModified);
            Assert.True(innerX.IsModified);
            Assert.False(innerY.IsModified);

            // Simulate "hide unchanged" tree walker — visits only modified nodes
            var visited = new List<string>();
            WalkHideUnchanged(root, visited);

            // Should visit the chain: root → SimTransform → Position → Inner → X
            // Should NOT visit Y (not modified)
            Assert.Contains("root", visited);
            Assert.Contains("SimTransform", visited);
            Assert.Contains("Position", visited);
            Assert.Contains("Inner", visited);
            Assert.Contains("X", visited);
            Assert.DoesNotContain("Y", visited);
            Assert.Equal(5, visited.Count);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static void AssertNoModifiedLeaf(DiffNode node)
        {
            if (node is DiffValue leaf)
                Assert.False(leaf.IsModified, $"Leaf '{leaf.Name}' should not be modified.");
            else if (node is DiffObject obj)
                foreach (DiffNode child in obj.Children)
                    AssertNoModifiedLeaf(child);
        }

        private static int CountModifiedLeaves(IReadOnlyList<DiffNode> nodes)
        {
            int count = 0;
            foreach (DiffNode n in nodes)
                count += CountModifiedLeavesNode(n);
            return count;
        }

        private static int CountModifiedLeavesNode(DiffNode node)
        {
            if (node is DiffValue leaf) return leaf.IsModified ? 1 : 0;
            if (node is DiffObject obj)
            {
                int c = 0;
                foreach (DiffNode child in obj.Children)
                    c += CountModifiedLeavesNode(child);
                return c;
            }
            return 0;
        }

        private static void AssertAllOldValueNull(IReadOnlyList<DiffNode> nodes)
        {
            foreach (DiffNode n in nodes)
                AssertAllOldValueNullNode(n);
        }

        private static void AssertAllOldValueNullNode(DiffNode node)
        {
            if (node is DiffValue leaf)
                Assert.Equal("null", leaf.OldValue);
            else if (node is DiffObject obj)
                foreach (DiffNode child in obj.Children)
                    AssertAllOldValueNullNode(child);
        }

        private static void AssertAllNewValueNull(IReadOnlyList<DiffNode> nodes)
        {
            foreach (DiffNode n in nodes)
                AssertAllNewValueNullNode(n);
        }

        private static void AssertAllNewValueNullNode(DiffNode node)
        {
            if (node is DiffValue leaf)
                Assert.Equal("null", leaf.NewValue);
            else if (node is DiffObject obj)
                foreach (DiffNode child in obj.Children)
                    AssertAllNewValueNullNode(child);
        }

        /// <summary>Simulates a "hide unchanged" tree walk — visits only modified nodes.</summary>
        private static void WalkHideUnchanged(DiffNode node, List<string> visited)
        {
            if (!node.IsModified) return;

            visited.Add(node.Name);

            if (node is DiffObject obj)
            {
                foreach (DiffNode child in obj.Children)
                    WalkHideUnchanged(child, visited);
            }
        }

        // ── Test component ────────────────────────────────────────────────────────

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        [ComponentId(210)]
        private struct HarnessDiffPos { public float X, Y, Z; }
    }
}
