using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Scenario;

namespace Fdp.Toolkit.ReplayBrowser.Diff
{
    public sealed class ComponentDiffService : IComponentDiffService
    {
        /// <inheritdoc/>
        public DiffNode? ComputeDiff(string name, JsonNode? oldNode, JsonNode? newNode, double epsilonTolerance)
        {
            if (oldNode is JsonObject oldObj && newNode is JsonObject newObj)
            {
                var group = new DiffObject(name);

                // Union of keys from both objects
                var allKeys = oldObj.Select(k => k.Key)
                    .Union(newObj.Select(k => k.Key))
                    .Distinct();

                foreach (string key in allKeys)
                {
                    DiffNode? childDiff = ComputeDiff(key, oldObj[key], newObj[key], epsilonTolerance);
                    if (childDiff != null)
                        group.Children.Add(childDiff);
                }

                group.EvaluateModificationState();
                return group;
            }

            // Arrays: if both are arrays and they differ at any index, emit as single modified leaf
            if (oldNode is JsonArray oldArr && newNode is JsonArray newArr)
            {
                string oldStr = oldArr.ToJsonString();
                string newStr = newArr.ToJsonString();
                bool isModified = oldStr != newStr;
                return new DiffValue(name, oldStr, newStr, JsonValueKind.Array, isModified);
            }

            // Leaf comparison
            string oldLeaf = oldNode?.ToJsonString() ?? "null";
            string newLeaf = newNode?.ToJsonString() ?? "null";
            JsonValueKind kind = newNode?.GetValueKind() ?? (oldNode?.GetValueKind() ?? JsonValueKind.Null);

            bool leafModified = oldLeaf != newLeaf;

            // Apply epsilon tolerance for numeric leaves
            if (leafModified && kind == JsonValueKind.Number)
            {
                if (double.TryParse(oldLeaf, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double oldVal)
                    && double.TryParse(newLeaf, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double newVal))
                {
                    leafModified = Math.Abs(oldVal - newVal) >= epsilonTolerance;
                }
            }

            return new DiffValue(name, oldLeaf, newLeaf, kind, leafModified);
        }

        /// <inheritdoc/>
        public IReadOnlyList<DiffNode> ComputeEntityDiff(
            Entity entity,
            EntityRepository sandboxRepo,
            ScenarioSerializer serializer,
            Action applyStepFunc)
        {
            var resolver = new Fdp.Toolkit.Diagnostics.DiagnosticGuidResolver();
            var mask = sandboxRepo.GetSnapshotableMask();
            // TODO(ecs-512): remove projection when SerializeEntity upgraded to BitMask512
            BitMask256 mask256 = Unsafe.As<BitMask512, BitMask256>(ref mask);

            // Serialize before
            JsonObject? before = null;
            if (sandboxRepo.IsAlive(entity))
            {
                before = serializer.SerializeEntity(sandboxRepo, entity, resolver, mask256);
            }

            // Apply the step (exactly once)
            applyStepFunc();

            // Serialize after
            JsonObject? after = null;
            if (sandboxRepo.IsAlive(entity))
            {
                after = serializer.SerializeEntity(sandboxRepo, entity, resolver, mask256);
            }

            if (before == null && after == null)
                return Array.Empty<DiffNode>();

            // Diff the full scenario DOM — return children of the root diff
            DiffNode? root = ComputeDiff("root", before, after, epsilonTolerance: 0.001);
            if (root is DiffObject rootObj)
                return rootObj.Children;

            return Array.Empty<DiffNode>();
        }

        /// <inheritdoc/>
        public IReadOnlyList<DiffNode> ComputeTreeDiff(JsonNode? before, JsonNode? after, double epsilonTolerance)
        {
            if (before == null && after == null)
                return Array.Empty<DiffNode>();

            if (before == null)
            {
                // Entity birth: all leaves in after are modified
                return BuildAllModified(after!, isNew: true, epsilonTolerance);
            }

            if (after == null)
            {
                // Entity death: all leaves in before are modified (new = "null")
                return BuildAllModified(before!, isNew: false, epsilonTolerance);
            }

            DiffNode? root = ComputeDiff("root", before, after, epsilonTolerance);
            if (root is DiffObject rootObj)
                return rootObj.Children;

            if (root != null)
                return new[] { root };

            return Array.Empty<DiffNode>();
        }

        // ── Private helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Recursively builds a DiffNode tree where every leaf is marked as modified,
        /// simulating an entity birth (isNew=true, old="null") or death (isNew=false, new="null").
        /// </summary>
        private static List<DiffNode> BuildAllModified(JsonNode node, bool isNew, double epsilonTolerance)
        {
            var result = new List<DiffNode>();
            BuildAllModifiedInto(result, node, isNew);
            return result;
        }

        private static void BuildAllModifiedInto(List<DiffNode> siblings, JsonNode node, bool isNew)
        {
            if (node is JsonObject obj)
            {
                var group = new DiffObject(string.Empty);
                foreach (var kvp in obj)
                {
                    if (kvp.Value == null) continue;
                    var child = BuildAllModifiedNode(kvp.Key, kvp.Value, isNew);
                    group.Children.Add(child);
                }
                group.EvaluateModificationState();
                siblings.AddRange(group.Children);
            }
            else
            {
                string valStr = node.ToJsonString();
                JsonValueKind kind = node.GetValueKind();
                var leaf = new DiffValue(string.Empty,
                    isNew ? "null" : valStr,
                    isNew ? valStr : "null",
                    kind,
                    isModified: true);
                siblings.Add(leaf);
            }
        }

        private static DiffNode BuildAllModifiedNode(string name, JsonNode node, bool isNew)
        {
            if (node is JsonObject obj)
            {
                var group = new DiffObject(name);
                foreach (var kvp in obj)
                {
                    if (kvp.Value == null) continue;
                    group.Children.Add(BuildAllModifiedNode(kvp.Key, kvp.Value, isNew));
                }
                group.EvaluateModificationState();
                return group;
            }

            string valStr = node.ToJsonString();
            JsonValueKind kind = node.GetValueKind();
            return new DiffValue(name,
                isNew ? "null" : valStr,
                isNew ? valStr : "null",
                kind,
                isModified: true);
        }
    }
}
