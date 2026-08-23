using Xunit;

namespace Hrot.ExCon.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — serializes every test class in this assembly that touches the process-global
/// <c>PanelSnapshot</c> singleton.</b> Mirrors the copies in <c>Hrot.Editor.AiShared.Tests</c>,
/// <c>Fdp.Presentation.Tests</c>, <c>Hrot.Presentation.Tests</c> and <c>Hrot.Editor.Tests</c>.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PanelSnapshotTestCollection
{
    public const string Name = "PanelSnapshot serial (Hrot.ExCon)";
}
