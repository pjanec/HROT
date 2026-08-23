using Xunit;

namespace Hrot.StrideMock.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — serializes every test class in this assembly that touches the process-global
/// <c>PanelSnapshot</c> singleton.</b> Mirrors the copies in <c>Hrot.ExCon.Tests</c>,
/// <c>Hrot.Editor.Tests</c>, <c>Hrot.IG.Tests</c>, <c>Fdp.Presentation.Tests</c> and
/// <c>Hrot.Presentation.Tests</c>.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PanelSnapshotTestCollection
{
    public const string Name = "PanelSnapshot serial (Hrot.StrideMock)";
}
