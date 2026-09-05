using Xunit;

namespace Fdp.Presentation.Tests.ImGui.Panels;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — serializes every test class in this assembly that touches the process-global
/// <c>PanelSnapshot</c> singleton.</b> Mirrors <c>Hrot.Editor.AiShared.Tests.PanelSnapshotTestCollection</c>
/// and <c>Hrot.Blueprints.Tests</c>'s own copy — do not invent a different shape.
///
/// <para>⛔⛔ <b>MEASURED, not theoretical</b> (see the AiShared copy's remarks): xunit runs different test
/// classes in PARALLEL by default, and two classes that each flip <c>PanelSnapshot.CaptureEnabled</c> can
/// interleave and read the wrong snapshot. ⭐ Every test class in this assembly that reads or writes
/// <c>PanelSnapshot</c> statics carries <c>[Collection(Name)]</c>.</para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PanelSnapshotTestCollection
{
    public const string Name = "PanelSnapshot serial (Fdp.Presentation)";
}
