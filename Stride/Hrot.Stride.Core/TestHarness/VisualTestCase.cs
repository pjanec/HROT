#nullable enable
using System;

namespace Hrot.Stride.Core.TestHarness;

/// <summary>
/// A single manually-triggerable visual test case for the in-app Stride test harness
/// (BATCH-12, STR-TEST-1).
///
/// <para>
/// A case is a label, a one-line description, and a <see cref="Run"/> delegate that
/// performs the action when the user clicks the case's button or presses its keyboard
/// shortcut. The delegate receives a <see cref="TestHarnessContext"/> giving it access to
/// the live <c>EditorStrideSubsystem</c> (World / ScenarioSource / VisualBindingSystem),
/// the active Stride scene + camera, an NLog-backed <see cref="TestHarnessContext.Log"/>
/// helper, and a per-frame <see cref="TestHarnessContext.Update"/> hook for continuous
/// behaviours (e.g. an orbiting entity).
/// </para>
///
/// <para>
/// <b>Why a record of delegates rather than an interface:</b> the overwhelmingly common
/// case is "one label + one action", and a record lets a future phase register a case in a
/// single line without declaring a class (see <see cref="TestHarnessRegistry"/>). If a case
/// ever needs state, it can close over locals in the lambda, or register a continuous hook
/// via <see cref="TestHarnessContext.RegisterUpdate"/> from inside <see cref="Run"/>.
/// </para>
/// </summary>
/// <param name="Label">
/// Short label shown on the button / in the on-screen list (e.g. "Spawn Infantry").
/// Keep it concise — it shares a line with the keyboard shortcut.
/// </param>
/// <param name="Description">
/// One-line human description of what the case does and what to look for on screen.
/// Logged when the case is triggered so the log file records intent.
/// </param>
/// <param name="Run">
/// The action invoked when the case is triggered (button click or keyboard shortcut).
/// Both trigger paths call this same delegate. Must not be <c>null</c>.
/// </param>
public sealed record VisualTestCase(
    string Label,
    string Description,
    Action<TestHarnessContext> Run)
{
    /// <summary>Validates the required fields. Throws if <see cref="Run"/> is null.</summary>
    public VisualTestCase EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Label))
            throw new ArgumentException("VisualTestCase.Label must be non-empty.", nameof(Label));
        if (Run is null)
            throw new ArgumentNullException(nameof(Run), "VisualTestCase.Run must be non-null.");
        return this;
    }
}
