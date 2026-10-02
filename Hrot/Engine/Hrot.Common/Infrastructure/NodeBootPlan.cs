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
/// A step either closes over the caller's locals (fine for a short slice) or hands values to later
/// steps through <see cref="NodeBootValues"/>, keyed by the same strings it declares. ⚠ The closure
/// form does NOT scale: measured on <c>CgfSubsystem</c>, whose spine values live 118-191 lines each,
/// every candidate step boundary is crossed by three to five live locals. See the
/// <c>Action&lt;NodeBootValues&gt;</c> overload of <see cref="Step(string, Action{NodeBootValues},
/// string[], string[])"/>.
/// </para>
///
/// 📄 docs/DESIGN_Subsystem_Composition_Unification.md §4.1P (design + UML), §4.1N (the
/// measured dependency graph these declarations encode).
/// </summary>
public sealed class NodeBootPlan
{
    private readonly List<BootStep> _steps  = new();
    private readonly NodeBootValues _values = new();

    private sealed record BootStep(string Key, string[] Requires, string[] Provides, Action<NodeBootValues> Run);

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
        if (run is null) throw new ArgumentNullException(nameof(run));
        return Step(key, _ => run(), requires, provides);
    }

    /// <summary>
    /// Appends a step that reads and writes VALUES through the plan instead of through closures over
    /// the caller's locals.
    ///
    /// <para>
    /// ⭐⭐⭐ Why this overload exists — measured, not anticipated. The closure form above is fine for a
    /// short slice: <c>ExConSubsystem</c>'s is six steps and two crossing locals. 📐 It does NOT scale.
    /// <c>CgfSubsystem.Initialize</c> spans <c>:509-:1192</c> and declares <b>40</b> locals, and its
    /// spine values live 118-191 lines each — <c>behaviorRegistry</c> 583→701, <c>creation</c> 630→772,
    /// <c>nodeFactory</c> 611→797, <c>replicationModule</c> 613→804, <c>newClusterSlave</c> 802→975 —
    /// so EVERY candidate step boundary is crossed by three to five live locals. With closures each
    /// crossing needs a mirrored local, which is how two of them produced five compile errors on ExCon.
    /// </para>
    ///
    /// <para>
    /// ⭐⭐ So a value that crosses a step boundary travels through <see cref="NodeBootValues"/> keyed by
    /// the SAME strings the step already declares. That makes the declaration and the data agree by
    /// construction: <see cref="NodeBootValues.Set"/> refuses a key the step does not declare it
    /// provides, and <see cref="NodeBootValues.Get{T}"/> refuses one it does not declare it requires.
    /// ⛔ The keys stop being names checked against names and become the actual channel.
    /// </para>
    ///
    /// <para>
    /// ⚠ This is the same shape the codebase already converged on twice —
    /// <c>EntityCreationPack.Build(EntityCreationContext)</c> and
    /// <c>MapInteractionPack.Build(MapInteractionContext)</c> — a context object rather than ambient
    /// locals. 📄 §4.1M ④ "Shape A", §4.1R.
    /// </para>
    /// </summary>
    public NodeBootPlan Step(
        string                  key,
        Action<NodeBootValues>  run,
        string[]?               requires = null,
        string[]?               provides = null)
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

            _values.EnterStep(step.Key, step.Requires, step.Provides);
            step.Run(_values);

            foreach (string gives in step.Provides)
                provided.Add(gives);
        }

        _values.LeaveStep();
    }

    /// <summary>Step keys in declaration order. For tests and diagnostics.</summary>
    public IReadOnlyList<string> StepKeys => _steps.Select(s => s.Key).ToArray();

    /// <summary>
    /// Reads a published value AFTER <see cref="Run"/> has completed.
    ///
    /// <para>
    /// ⭐ A host migrates its composition slice one region at a time, so the code AFTER the plan still
    /// needs the values the plan produced. Inside a step that read goes through
    /// <see cref="NodeBootValues.Get{T}"/> and is checked against the step's own <c>requires</c>;
    /// once the plan has finished there is no step to check against, so this is the honest accessor
    /// for the plan's OWNER.
    /// </para>
    ///
    /// <para>
    /// ⛔ Not a back door into a running plan — a step must still declare what it reads. This exists
    /// only for the boundary between a migrated region and the code that has not been migrated yet,
    /// and it should get smaller as a host's slice is finished. 📄 §4.1T.
    /// </para>
    /// </summary>
    public T Value<T>(string key) => _values.Published<T>(key);
}

/// <summary>
/// The values a boot plan's steps hand to one another, keyed by the SAME strings the steps declare
/// in their <c>Requires</c>/<c>Provides</c>.
///
/// <para>
/// ⭐⭐⭐ The point is that the declaration and the data cannot drift apart: a step may only
/// <see cref="Set"/> a key it declared it provides, and may only <see cref="Get{T}"/> a key it
/// declared it requires. So "the keys are honest" is enforced rather than reviewed.
/// </para>
///
/// <para>
/// ⛔ Deliberately NOT a general service locator. It is scoped to one plan, the keys are the plan's
/// own, and every access is checked against the declaring step — a bag anyone can read from and write
/// to at any time is exactly the ambient-coupling this work exists to remove.
/// </para>
///
/// 📄 docs/DESIGN_Subsystem_Composition_Unification.md §4.1R.
/// </summary>
public sealed class NodeBootValues
{
    private readonly Dictionary<string, object?> _bag = new(StringComparer.Ordinal);

    private string   _stepKey  = "(outside any step)";
    private string[] _requires = Array.Empty<string>();
    private string[] _provides = Array.Empty<string>();

    internal void EnterStep(string key, string[] requires, string[] provides)
    {
        _stepKey  = key;
        _requires = requires;
        _provides = provides;
    }

    internal void LeaveStep()
    {
        _stepKey  = "(outside any step)";
        _requires = Array.Empty<string>();
        _provides = Array.Empty<string>();
    }

    /// <summary>
    /// Publishes a value under <paramref name="key"/>. The running step must have declared that key
    /// in its <c>provides</c>.
    /// </summary>
    public void Set<T>(string key, T value)
    {
        if (Array.IndexOf(_provides, key) < 0)
            throw new InvalidOperationException(
                $"Boot step '{_stepKey}' set '{key}', which it does not declare in its provides " +
                $"[{string.Join(", ", _provides)}]. Add it there, so the declared graph stays the real one.");

        _bag[key] = value;
    }

    /// <summary>
    /// Reads a published value without a step guard. Only <see cref="NodeBootPlan.Value{T}"/> calls
    /// this, and only after the plan has run — see its remarks for why that is not a back door.
    /// </summary>
    internal T Published<T>(string key)
    {
        if (!_bag.TryGetValue(key, out object? raw))
            throw new InvalidOperationException(
                $"No boot step published '{key}'. Known keys: [{string.Join(", ", _bag.Keys.OrderBy(k => k, StringComparer.Ordinal))}].");

        if (raw is null) return default!;
        if (raw is not T typed)
            throw new InvalidOperationException(
                $"'{key}' was published as {raw.GetType().Name}, not {typeof(T).Name}.");
        return typed;
    }

    /// <summary>
    /// Reads a value published by an earlier step. The running step must have declared
    /// <paramref name="key"/> in its <c>requires</c>.
    /// </summary>
    public T Get<T>(string key)
    {
        if (Array.IndexOf(_requires, key) < 0)
            throw new InvalidOperationException(
                $"Boot step '{_stepKey}' read '{key}', which it does not declare in its requires " +
                $"[{string.Join(", ", _requires)}]. Add it there, so the declared graph stays the real one.");

        if (!_bag.TryGetValue(key, out object? raw))
            throw new InvalidOperationException(
                $"Boot step '{_stepKey}' requires '{key}', but the step that provides it never Set it. " +
                "A step that declares a key in provides must publish it.");

        if (raw is null) return default!;

        if (raw is not T typed)
            throw new InvalidOperationException(
                $"Boot step '{_stepKey}' read '{key}' as {typeof(T).Name}, but it was published as " +
                $"{raw.GetType().Name}.");

        return typed;
    }
}
