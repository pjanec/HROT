using Xunit;

namespace Fdp.Diagnostics.Contracts.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>U-obs</c> — serializes every test class in this assembly that touches the process-global
/// <c>PanelSnapshot</c> singleton.</b> Mirrors the copies in <c>Hrot.Editor.Tests</c>,
/// <c>Fdp.Presentation.Tests</c>, <c>Hrot.Presentation.Tests</c> and <c>Hrot.StrideMock.Tests</c> —
/// ⛔ do not invent a different shape.
///
/// <para>⚠ <c>PanelSnapshotTests</c> predates this and keeps its own "one class on purpose" argument;
/// it is now joined by <c>GizmoFramePanelTests</c>, so the two need the collection to stay apart.</para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PanelSnapshotTestCollection
{
    public const string Name = "PanelSnapshot serial (Fdp.Diagnostics.Contracts)";
}
