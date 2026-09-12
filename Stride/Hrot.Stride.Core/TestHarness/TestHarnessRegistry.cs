#nullable enable
using System;
using System.Collections.Generic;

namespace Hrot.Stride.Core.TestHarness;

/// <summary>
/// The extensible registry of <see cref="VisualTestCase"/>s for the in-app Stride test
/// harness (BATCH-12, STR-TEST-1). <b>This is the key cross-phase deliverable:</b> every
/// subsequent phase (P4 animation, P5 gizmos, P6 networking) adds its manual test cases by
/// calling <see cref="Register(VisualTestCase)"/> — no harness changes required.
///
/// <para>
/// <b>One-line registration pattern (copy this into a future phase's wiring):</b>
/// <code>
/// registry.Register(new VisualTestCase(
///     "My Case Label",
///     "What it does / what to look for on screen.",
///     ctx => { /* ctx.ScenarioSource.Enqueue(...) / ctx.World... / ctx.Log("...") */ }));
/// </code>
/// For a continuous (per-frame) case, register a hook from inside <c>Run</c>:
/// <code>
/// registry.Register(new VisualTestCase(
///     "Orbit Demo", "Moves an entity in a circle each frame.",
///     ctx => ctx.RegisterUpdate(dt => { /* move something; */ return true; })));
/// </code>
/// </para>
///
/// <para>
/// The registry preserves insertion order; the harness assigns keyboard shortcuts
/// (D1–D9) to the first nine registered cases in that order and lays out one button per
/// case top-to-bottom.
/// </para>
/// </summary>
public sealed class TestHarnessRegistry
{
    private readonly List<VisualTestCase> _cases = new();

    /// <summary>The registered cases in insertion order.</summary>
    public IReadOnlyList<VisualTestCase> Cases => _cases;

    /// <summary>Number of registered cases.</summary>
    public int Count => _cases.Count;

    /// <summary>
    /// Registers a test case. Returns the same <see cref="TestHarnessRegistry"/> so calls
    /// can be chained. The case is validated (non-empty label, non-null <c>Run</c>).
    /// </summary>
    /// <param name="testCase">The case to add. Must not be null.</param>
    public TestHarnessRegistry Register(VisualTestCase testCase)
    {
        if (testCase == null) throw new ArgumentNullException(nameof(testCase));
        _cases.Add(testCase.EnsureValid());
        return this;
    }

    /// <summary>
    /// Convenience overload that constructs and registers a case in one call.
    /// </summary>
    /// <param name="label">Short button/list label.</param>
    /// <param name="description">One-line description (logged on trigger).</param>
    /// <param name="run">The action to run when triggered.</param>
    public TestHarnessRegistry Register(string label, string description, Action<TestHarnessContext> run)
        => Register(new VisualTestCase(label, description, run));

    /// <summary>
    /// Invokes the case at <paramref name="index"/> with the supplied context, returning the
    /// case that ran (or <c>null</c> if the index is out of range). Used by both the button
    /// click handler and the keyboard-shortcut path so they share one trigger path.
    /// </summary>
    /// <param name="index">Zero-based case index.</param>
    /// <param name="context">The execution context to pass to <see cref="VisualTestCase.Run"/>.</param>
    public VisualTestCase? Trigger(int index, TestHarnessContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (index < 0 || index >= _cases.Count)
            return null;

        var testCase = _cases[index];
        testCase.Run(context);
        return testCase;
    }
}
