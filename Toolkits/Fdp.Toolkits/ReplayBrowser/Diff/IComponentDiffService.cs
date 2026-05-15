using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Scenario;

namespace Fdp.Toolkit.ReplayBrowser.Diff
{
    public interface IComponentDiffService
    {
        /// <summary>
        /// Recursively diffs two JSON nodes and returns a single DiffNode representing the comparison.
        /// Returns null only when both inputs are null.
        /// </summary>
        DiffNode? ComputeDiff(string name, JsonNode? oldNode, JsonNode? newNode, double epsilonTolerance);

        /// <summary>
        /// Serializes the entity state, invokes applyStepFunc exactly once, re-serializes,
        /// and returns the per-component diff list.
        /// </summary>
        IReadOnlyList<DiffNode> ComputeEntityDiff(
            Entity entity,
            EntityRepository sandboxRepo,
            ScenarioSerializer serializer,
            Action applyStepFunc);

        /// <summary>
        /// Diffs two top-level JsonNodes. Handles the null baseline (entity birth) and
        /// null current (entity death) cases. Returns the list of top-level DiffNodes.
        /// </summary>
        IReadOnlyList<DiffNode> ComputeTreeDiff(JsonNode? before, JsonNode? after, double epsilonTolerance);
    }
}
