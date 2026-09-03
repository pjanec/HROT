using System;
using System.Collections.Generic;
using System.Linq;

namespace Hrot.Common.Infrastructure;

/// <summary>
/// Thrown when a boot step declares a dependency that no earlier step provided.
/// </summary>
/// <remarks>
/// Deliberately a throw and not a log. Every ordering defect this type exists to catch
/// fails SILENTLY today: a component registered after the serializer is simply absent from
/// serialization; a translator built from a null <c>GhostCreationSystem</c> is wired to
/// nothing. A control plane that is wired but inert survives a whole test run looking
/// healthy — see docs/DESIGN_Subsystem_Composition_Unification.md §4.1N ②.
/// </remarks>
public sealed class BootDependencyException : InvalidOperationException
{
    /// <summary>The step whose dependency was unmet.</summary>
    public string StepKey { get; }

    /// <summary>The dependency key that no earlier step provided.</summary>
    public string MissingKey { get; }

    internal BootDependencyException(string owner, string stepKey, string missingKey, IEnumerable<string> provided)
        : base(
            $"[{owner}] boot step '{stepKey}' requires '{missingKey}', which no earlier step provides. " +
            $"Provided so far: [{string.Join(", ", provided)}]. " +
            "Either the step list is out of order, or a step that produces this value is missing. " +
            "See docs/DESIGN_Subsystem_Composition_Unification.md §4.1P.")
    {
        StepKey    = stepKey;
        MissingKey = missingKey;
    }
}

/// <summary>
/// An ordered list of node-boot steps, each declaring what it <b>requires</b> and what it
/// <b>provides</b>, run in declaration order with the declarations checked.
///
/// <para>
/// <b>It verifies; it does not sort.</b> That is the whole point of the first step of the
/// composition-unification work and it is not an accident of implementation:
/// </para>
/// <list type="bullet">
///   <item>a runner that topologically SORTED would change the boot order, and
///     §4.1N measured that three of the base's real dependencies are invisible —
///     sorting on an incomplete declaration reorders into a silent break;</item>
///   <item>a runner that VERIFIES keeps the caller's exact order, so it is
///     behaviour-preserving by construction, while turning every declared edge into
///     something checked on every boot of every node.</item>
/// </list>
///
/// <para>
/// The value of this type is therefore not the execution — a plain sequence of statements
/// executes just as well — it is that the orderings stop being a comment. An edit that moves
/// a phase now throws with the missing key named, instead of failing silently.
/// </para>
///
/// <para>
/// Steps close over the caller's locals, so no state carrier is needed; the plan moves
/// declarations, not data.
/// </para>
///
/// 📄 docs/DESIGN_Subsystem_Composition_Unification.md §4.1P (design + UML), §4.1N (the
/// measured dependency graph these declarations encode).
/// </summary>
public sealed class NodeBootPlan
{
    private readonly List<BootStep> _steps = new();

    private sealed record BootStep(string Key, string[] Requires, string[] Provides, Action Run);

    /// <summary>
    /// Appends a step. Order of declaration is order of execution — this type never reorders.
    /// </summary>
    /// <param name="key">Stable name of the step, used in diagnostics.</param>
    /// <param name="run">The work. Typically a closure over the caller's locals.</param>
    /// <param name="requires">Keys that an earlier step must have provided.</param>
    /// <param name="provides">Keys this step makes available to later steps.</param>
    public NodeBootPlan Step(
        string    key,
        Action    run,
        string[]? requires = null,
        string[]? provides = null)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Step key is required.", nameof(key));
        if (run is null)                    throw new ArgumentNullException(nameof(run));

        _steps.Add(new BootStep(key, requires ?? Array.Empty<string>(), provides ?? Array.Empty<string>(), run));
        return this;
    }

    /// <summary>
    /// Runs every step in declaration order. Before each, asserts its <c>Requires</c> were
    /// provided by an earlier step; after each, records its <c>Provides</c>.
    /// </summary>
    /// <param name="owner">Name of the bootstrapper, for diagnostics.</param>
    /// <exception cref="BootDependencyException">A step's dependency was not provided.</exception>
    public void Run(string owner)
    {
        var provided = new HashSet<string>(StringComparer.Ordinal);

        foreach (BootStep step in _steps)
        {
            foreach (string need in step.Requires)
            {
                if (!provided.Contains(need))
                    throw new BootDependencyException(owner, step.Key, need, provided.OrderBy(p => p, StringComparer.Ordinal));
            }

            step.Run();

            foreach (string gives in step.Provides)
                provided.Add(gives);
        }
    }

    /// <summary>Step keys in declaration order. For tests and diagnostics.</summary>
    public IReadOnlyList<string> StepKeys => _steps.Select(s => s.Key).ToArray();
}
