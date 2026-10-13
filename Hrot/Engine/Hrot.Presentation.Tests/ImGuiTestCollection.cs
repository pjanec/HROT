using Xunit;

namespace Hrot.Presentation.Tests;

/// <summary>
/// ⭐ <c>CE-1059</c> — serialises every test class in this assembly that drives ImGui. ImGui keeps ONE process-global current
/// context, so two classes creating, using and destroying contexts in parallel trip each other's frame
/// (measured: <c>ImGui::Begin … Assertion `g.WithinFrameScope' failed</c> crashed the test host when the order-form rails
/// ran beside <c>BehaviorParamsFormTests</c>). Same shape as <see cref="Windows.PanelSnapshotTestCollection"/>.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ImGuiTestCollection
{
    public const string Name = "ImGui serial (Hrot.Presentation)";
}
