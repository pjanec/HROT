using Xunit;

namespace Hrot.Editor.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — serializes every test class in this assembly that touches PROCESS-GLOBAL state.</b>
/// Mirrors the copies in <c>Hrot.Editor.AiShared.Tests</c>, <c>Fdp.Presentation.Tests</c> and
/// <c>Hrot.Presentation.Tests</c> — do not invent a different shape.
///
/// <para>⭐ <b>WIDENED <c>2026-09-09</c> from "the <c>PanelSnapshot</c> singleton" to any process-global,
/// and the NAME deliberately did not change</b> *(it is mirrored across four assemblies)*. 📐 The second
/// occupant is <c>TheViewportInteractionIsSharedTests</c>, which flips
/// <c>FdpConfig.EnforceExplicitEventRegistration</c> — a process-global — to prove the shared viewport
/// events are registered. 🔴 <b>Measured:</b> with xUnit running classes in parallel, that flip leaked into
/// <c>JsonEntityContextMenuHandlerTests</c>, which publishes a managed event and threw
/// <c>"Strict Mode Violation: ... 'ContextActionTriggered'"</c> — while passing <b>6/6 in isolation</b>.
/// ⚠ The race PRE-DATES the rail that exposed it *(the same shape as <c>CE-099</c>'s <c>AssetRoots</c>
/// race)*; three tests added on <c>2026-09-09</c> merely widened the window until it bit every run.
/// ⇒ 🔒 <b>reusing this collection is the seam-law answer</b> — the mechanism existed and was
/// under-adopted; a second collection for "the other global" would be the duplication.</para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PanelSnapshotTestCollection
{
    public const string Name = "PanelSnapshot serial (Hrot.Editor)";
}
