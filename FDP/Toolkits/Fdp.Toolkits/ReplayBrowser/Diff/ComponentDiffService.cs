using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Scenario;
using CoreDiff = Fdp.Core.Serialization.Migrations.Internal;

namespace Fdp.Toolkit.ReplayBrowser.Diff
{
    public sealed class ComponentDiffService : IComponentDiffService
    {
        /// <inheritdoc/>
        public DiffNode? ComputeDiff(string name, JsonNode? oldNode, JsonNode? newNode, double epsilonTolerance)
        {
            var core = CoreDiff.DomDiffer.Diff(oldNode, newNode, name, epsilonTolerance);
            return ConvertNode(core);
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

            // Serialize before
            JsonObject? before = null;
            if (sandboxRepo.IsAlive(entity))
            {
                before = serializer.SerializeEntity(sandboxRepo, entity, resolver, mask);
            }

            // Apply the step (exactly once)
            applyStepFunc();

            // Serialize after
            JsonObject? after = null;
            if (sandboxRepo.IsAlive(entity))
            {
                after = serializer.SerializeEntity(sandboxRepo, entity, resolver, mask);
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
        /// Converts a Core-internal <see cref="CoreDiff.DiffNode"/> to the Toolkit
        /// <see cref="DiffNode"/> that callers expect. Returns null when the input is null.
        /// </summary>
        private static DiffNode? ConvertNode(CoreDiff.DiffNode? coreNode)
        {
            if (coreNode is null)
                return null;

            if (coreNode is CoreDiff.DiffObject coreObj)
            {
                var tkObj = new DiffObject(coreNode.Name);
                foreach (var child in coreObj.Children)
                {
                    DiffNode? converted = ConvertNode(child);
                    if (converted != null)
                        tkObj.Children.Add(converted);
                }
                tkObj.EvaluateModificationState();
                return tkObj;
            }

            if (coreNode is CoreDiff.DiffValue coreVal)
                return new DiffValue(coreVal.Name, coreVal.OldValue, coreVal.NewValue,
                    coreVal.ValueType, coreVal.IsModified);

            // Unreachable; guard against future subtypes.
            throw new InvalidOperationException(
                $"Unexpected CoreDiff.DiffNode subtype: {coreNode.GetType().Name}");
        }

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
