using Fdp.Toolkit.Runner.Testing;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>QA-028</c> — assert a <see cref="HeadlessTestExecutor"/> run PASSED, and say why when it
/// did not.</b>
///
/// <para>📌 <b>The defect this replaces.</b> Every script-driven test in this assembly ended with
/// <c>Assert.Equal(0, result)</c>. <see cref="HeadlessTestExecutor.RunAsync"/> collapses every failure
/// — a handler that threw, an assertion step that tripped, a missing action handler — into the integer
/// <c>1</c>, and routes the human-readable reason to the injected <c>ILogger</c>. ⛔ These tests inject
/// <c>NullLogger.Instance</c>, so the reason was <b>discarded</b> and the red read, in full:
/// <i>"Assert.Equal() Failure: Expected 0, Actual 1"</i>.</para>
///
/// <para>📐 <b>Measured <c>2026-08-26</c>:</b> triaging <c>QA-017</c> required knowing whether the
/// cluster had reached <c>OperatingLive</c>. The handler builds a precise message naming the slave's
/// actual state id and the roster size — and not one character of it survived to the test output.</para>
///
/// <para>⭐⭐ <b>Why a helper and not a per-test tweak.</b> There are six call sites across two classes;
/// a helper makes the next one correct by default. ⚠ It deliberately asserts on
/// <see cref="HeadlessTestExecutor.AssertionFailures"/> rather than the exit code, because the failure
/// LIST is the diagnostic and the integer is only its cardinality.</para>
/// </summary>
internal static class ScriptRunAssert
{
    /// <summary>
    /// Asserts the run recorded no failures. On failure the xUnit message carries every recorded
    /// reason, one per line, so the red is a repro rather than a shrug (<c>R-131</c>).
    /// </summary>
    /// <param name="executor">The executor that has already completed a <c>RunAsync</c>.</param>
    /// <param name="exitCode">Its return value — cross-checked so the two can never disagree silently.</param>
    public static void Passed(HeadlessTestExecutor executor, int exitCode)
    {
        if (executor.AssertionFailures.Count == 0)
        {
            // The list and the exit code must agree; a mismatch is a defect in the executor itself.
            Assert.Equal(0, exitCode);
            return;
        }

        Assert.Fail(
            $"Headless script run failed with exit code {exitCode} and " +
            $"{executor.AssertionFailures.Count} recorded reason(s):" +
            System.Environment.NewLine +
            "  - " + string.Join(System.Environment.NewLine + "  - ", executor.AssertionFailures));
    }
}
