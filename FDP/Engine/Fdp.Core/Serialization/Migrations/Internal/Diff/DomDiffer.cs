using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fdp.Core.Serialization.Migrations.Internal
{
    /// <summary>
    /// Pure DOM-diff algorithm. Compares two <see cref="JsonNode"/> trees
    /// and returns a <see cref="DiffNode"/> tree annotated with modification flags.
    /// </summary>
    internal static class DomDiffer
    {
        /// <summary>
        /// Returns a <see cref="DiffNode"/> tree describing the differences between
        /// <paramref name="a"/> and <paramref name="b"/>.
        /// Returns <c>null</c> when the two trees are structurally identical
        /// (no modified nodes).
        /// </summary>
        /// <param name="a">The "before" JSON node (may be null).</param>
        /// <param name="b">The "after" JSON node (may be null).</param>
        /// <param name="rootName">Label for the root node in the diff tree.</param>
        /// <param name="epsilonTolerance">
        /// Numeric differences smaller than this value are treated as identical.
        /// Use 0.0 for exact byte-level comparison (default).
        /// </param>
        /// <param name="compareArraysElementWise">
        /// When <c>true</c>, arrays of objects are compared element-by-element
        /// (by positional index) rather than as a monolithic serialized string.
        /// This produces <see cref="DiffObject"/> subtrees with integer-indexed
        /// children, which <see cref="DiffToJournalConverter"/> maps to
        /// <c>$.path[N].field</c> journal paths. Defaults to <c>false</c>
        /// (existing monolithic-blob behavior, used by all callers except
        /// <see cref="UnknownsJournal.Compute"/>).
        /// </param>
        public static DiffNode? Diff(JsonNode? a, JsonNode? b, string rootName = "$",
            double epsilonTolerance = 0.0, bool compareArraysElementWise = false)
        {
            var node = DiffImpl(a, b, rootName, epsilonTolerance, compareArraysElementWise);
            return node.IsModified ? node : null;
        }

        // ---------------------------------------------------------------
        // Private recursive implementation
        // ---------------------------------------------------------------

        private static DiffNode DiffImpl(JsonNode? a, JsonNode? b, string name,
            double epsilonTolerance, bool compareArraysElementWise)
        {
            if (a is JsonObject aObj && b is JsonObject bObj)
            {
                var group = new DiffObject(name);

                var allKeys = aObj.Select(k => k.Key)
                    .Union(bObj.Select(k => k.Key))
                    .Distinct();

                foreach (string key in allKeys)
                {
                    DiffNode child = DiffImpl(aObj[key], bObj[key], key,
                        epsilonTolerance, compareArraysElementWise);
                    group.Children.Add(child);
                }

                group.EvaluateModificationState();
                return group;
            }

            // Element-wise array comparison (opt-in). Each element is diffed by
            // its positional index. This allows DiffToJournalConverter to produce
            // fine-grained $.path[N].field journal ops for arrays of objects.
            if (compareArraysElementWise && a is JsonArray aArrEW && b is JsonArray bArrEW)
            {
                var group = new DiffObject(name);
                int maxLen = Math.Max(aArrEW.Count, bArrEW.Count);
                for (int i = 0; i < maxLen; i++)
                {
                    JsonNode? elemA = i < aArrEW.Count ? aArrEW[i] : null;
                    JsonNode? elemB = i < bArrEW.Count ? bArrEW[i] : null;
                    DiffNode elemNode = DiffImpl(elemA, elemB,
                        i.ToString(CultureInfo.InvariantCulture), epsilonTolerance, true);
                    group.Children.Add(elemNode);
                }
                group.EvaluateModificationState();
                return group;
            }

            // NOTE: Arrays are compared as monolithic leaf DiffValues (by JSON serialization),
            // not element-by-element. This means DiffToJournalConverter will not produce
            // array-indexed [N] paths from natural DomDiffer output. The [N] path form is
            // supported by DiffToJournalConverter and JsonPathParser, but is only exercisable
            // via manually-constructed DiffNode trees (see T1-246). In the current use case
            // (entity dictionaries keyed by GUID), this is not a limitation.
            // Arrays: if both are arrays, compare their JSON serializations as a unit.
            if (a is JsonArray aArr && b is JsonArray bArr)
            {
                string oldStr = aArr.ToJsonString();
                string newStr = bArr.ToJsonString();
                bool isModified = oldStr != newStr;
                return new DiffValue(name, oldStr, newStr, JsonValueKind.Array, isModified);
            }

            // Leaf / mixed-type comparison.
            string oldLeaf = a?.ToJsonString() ?? "null";
            string newLeaf = b?.ToJsonString() ?? "null";
            JsonValueKind kind = b?.GetValueKind() ?? (a?.GetValueKind() ?? JsonValueKind.Null);

            bool leafModified = oldLeaf != newLeaf;

            // Apply epsilon tolerance for numeric leaves.
            if (leafModified && kind == JsonValueKind.Number)
            {
                if (double.TryParse(oldLeaf, NumberStyles.Float, CultureInfo.InvariantCulture, out double oldVal)
                    && double.TryParse(newLeaf, NumberStyles.Float, CultureInfo.InvariantCulture, out double newVal))
                {
                    leafModified = Math.Abs(oldVal - newVal) >= epsilonTolerance;
                }
            }

            return new DiffValue(name, oldLeaf, newLeaf, kind, leafModified);
        }
    }
}
