using Xunit;

namespace Hrot.Hsm.Editor.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 follow-up — serializes every test class in this assembly that touches the
/// process-global <c>PanelSnapshot</c> singleton.</b> Mirrors the copies in
/// <c>Hrot.Editor.AiShared.Tests</c>, <c>Fdp.Presentation.Tests</c>, <c>Hrot.ExCon.Tests</c> and
/// <c>Hrot.Editor.Tests</c> — first needed here by <c>HsmEventsDetailsView</c>, the first panel in this
/// assembly wired into the contract.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PanelSnapshotTestCollection
{
    public const string Name = "PanelSnapshot serial (Hrot.Hsm.Editor)";
}
