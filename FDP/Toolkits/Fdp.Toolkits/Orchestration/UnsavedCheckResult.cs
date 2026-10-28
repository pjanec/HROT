using System;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// ⭐⭐ <c>CE-4093</c> (AQ108 D20) — the answer of the master's CHECK ROUND (<see cref="ClusterOpType.CheckUnsaved"/>): has the scenario open for
    /// editing changed since it was loaded or last saved? <paramref name="Changed"/> is true when ANY participating node answers anything but
    /// an explicit "unchanged" (<c>WorldChangeCheckHandler.MeansChanged</c>), or when the master itself knows a save round FAILED after the last
    /// load (a node cannot know that — its slice was written). <paramref name="ChangedNodes"/> names the nodes that answered "changed" or
    /// "no-baseline" (empty when only the master's own fact made it changed). Travels as the op's <c>ResultPayload</c> to every host's bus.
    /// 📄 docs/blueprints/Architect_Question_108_Run_Mode_Picker.md §4a.
    /// </summary>
    public sealed record UnsavedCheckResult(bool Changed, int[] ChangedNodes)
    {
        /// <summary>Nothing differs.</summary>
        public static readonly UnsavedCheckResult Unchanged = new(false, Array.Empty<int>());

        /// <summary>
        /// ⭐ Whether a JSON object is this result — the wire carries the master's op results as untyped JSON, and the observer must not mistake it for
        /// another result type (every other result record deserialises "successfully" from any object). Keyed on the one property no other result has.
        /// </summary>
        public static bool LooksLike(System.Text.Json.JsonElement root)
        {
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
            foreach (var p in root.EnumerateObject())
                if (string.Equals(p.Name, nameof(ChangedNodes), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    /// <summary>
    /// ⭐ <c>CE-4093</c> — how a requested check round ended, as a host's cluster read model saw it: <paramref name="Result"/> when the master answered,
    /// else <paramref name="Refusal"/> (the master's reason — e.g. the cluster is not editing — or the failure's status). Never both.
    /// </summary>
    public sealed record UnsavedCheckOutcome(UnsavedCheckResult? Result, string? Refusal)
    {
        /// <summary>The master's refusal outside editing — the HTTP 409 and the UI carry it; ⭐ one text, the master and the routes read it from here.</summary>
        public const string NotEditingReason = "the unsaved-changes check is meaningful only while a scenario is open for editing";
    }
}
