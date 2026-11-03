using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.ScenarioRules
{
    /// <summary>⭐ rules V-4 — what an instructor / an agent asks of a rule entity (a trigger or an objective). <c>NoRequest</c> = nothing (⚠ not <c>None</c>: this assembly's enums share one IDL scope).</summary>
    public enum RuleRequest : byte { NoRequest = 0, Fire = 1, Disarm = 2, Rearm = 3, Reset = 4, Activate = 5, Deactivate = 6 }

    /// <summary>⭐ rules V-4 — who asked, for the journal text ("Disarm by instructor").</summary>
    public enum RuleOrigin : byte { Unknown = 0, DebugApi = 1, Instructor = 2 }

    /// <summary>
    /// ⭐ rules V-4 (📄 docs/DESIGN_Scenario_Rules.md §00.10) — a runtime request to a rule entity, written through the EXISTING attribute path
    /// (<c>EntityWriteRouter.For(repo).Write</c>: applied here when this node owns the entity, a change-request to the owner elsewhere).
    /// The owner's <c>ObjectiveHost.Enter</c> honours a <see cref="Seq"/> it has not seen (<see cref="Done"/>) ONCE and journals it.
    /// Brain-local (no descriptor), NoScenario.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.RuleControl)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct RuleControl
    {
        /// <summary>A <see cref="RuleRequest"/>.</summary>
        public byte Request;
        /// <summary>A <see cref="RuleOrigin"/>.</summary>
        public byte Origin;
        /// <summary>The requester's sequence number: a new value = a new request.</summary>
        public int Seq;
        /// <summary>The last <see cref="Seq"/> the owner acted on (written by the owner only — never through the attribute path).</summary>
        public int Done;
    }
    /// <summary>
    /// ⭐ rules ② (📄 docs/DESIGN_Scenario_Rules.md §00.11a) — a LIVE EDIT of a rule's params, written through the attribute path (<c>Rule.Params</c> / <c>Rule.EditSeq</c> /
    /// <c>Rule.EditOrigin</c>): the owner's <c>RuleEditSystem</c> applies a <see cref="Seq"/> it has not seen (<see cref="Done"/>) ONCE, keeping the rule's state.
    /// <para>⭐ MANAGED, so an IMMUTABLE record replaced whole (CLAUDE.md "NEW COMPONENTS"): each attribute handler sets <c>old with { … }</c> through
    /// <c>IEntityPatchContext.SetManagedComponent</c>; the owner records <see cref="Done"/> the same way. Brain-local, <c>NoReplay</c> (a change request — the status and the
    /// journal carry the effect), <c>NoScenario</c> (SR-109: "this run only").</para>
    /// </summary>
    [ComponentId(GlobalComponentIds.RuleEdit)]
    [DataPolicy(DataPolicy.NoReplay | DataPolicy.NoScenario)]
    public sealed record RuleEdit
    {
        /// <summary>The rule's new params (the JSON a load takes).</summary>
        public string Json { get; init; } = "";
        /// <summary>The requester's sequence number: a new value = a new edit.</summary>
        public int Seq { get; init; }
        /// <summary>A <c>BehaviorOrigin</c> value — who asked, for the journal.</summary>
        public int Origin { get; init; }
        /// <summary>The last <see cref="Seq"/> the owner applied or refused (written by the owner only — never through the attribute path).</summary>
        public int Done { get; init; }
    }
}
