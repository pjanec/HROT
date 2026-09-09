using Xunit;

namespace Hrot.Utility.Editor.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — serializes every test class in this assembly that touches the process-global
/// <c>PanelSnapshot</c> singleton.</b> Mirrors the copies in every other converted-panel test assembly.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PanelSnapshotTestCollection
{
    public const string Name = "PanelSnapshot serial (Hrot.Utility.Editor)";
}
