using Fdp.Presentation.Panels;
using Fdp.Toolkit.DER;
using Xunit;

namespace Fdp.Presentation.Tests.ImGui.Panels;

/// <summary>
/// ⭐⭐ <b>U-obs-5 — <c>DerEntityInspectorPanel.BuildViewModel</c>, the BUILD half only.</b>
/// 📄 <c>docs/blueprints/batches/QUEUE_Panel_Observability_Sweep.md</c> group 4.
///
/// <para>⚠ <b>No <c>PanelSnapshot</c> rails here on purpose.</b> This panel has no window host inside
/// <c>Fdp.Presentation</c> — its only production caller is <c>Hrot.ExCon.ExConMock</c> (group 6), a
/// non-<c>ManagedWindow</c> root class. Per the queue's caller-registers rule, the
/// <c>DeclareInstrumented</c>/<c>Register</c> rails belong to that caller once group 6 wires it up; this
/// class only pins the pure projection so the build cannot silently drift from
/// <see cref="DerEntityInspectorPanel.GetEntityListRows"/>'s own filter.</para>
/// </summary>
public sealed class DerEntityInspectorPanelBuildsItsModelTests
{
    [Fact]
    public void TheDump_CarriesTheSelectedEntitysDescriptorHeaders()
    {
        var repo = new DerRepo();
        var e0 = repo.CreateEntity(1, tkbType: 42);
        e0.SetDescriptor(new SampleDescriptor { Value = 5 });
        repo.CreateEntity(2, tkbType: 42);

        var panel = new DerEntityInspectorPanel();
        // Select entity 1 the same way DrawEntityList does — through the private field, since
        // selection only happens via a click; mirror that with reflection for a headless build.
        typeof(DerEntityInspectorPanel)
            .GetField("_selectedEntityId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(panel, 1);

        var vm = panel.BuildViewModel(repo, "der-test", "der-entity-inspector");

        Assert.Equal(2, vm.TotalEntityCount);
        Assert.Equal(new[] { 1, 2 }, vm.EntityIds);
        Assert.Equal(1, vm.SelectedEntityId);
        Assert.Contains(nameof(SampleDescriptor), vm.SelectedDescriptorHeaders);
    }

    [Fact]
    public void ANumericFilter_MatchesGetEntityListRows()
    {
        var repo = new DerRepo();
        repo.CreateEntity(1, tkbType: 1);
        repo.CreateEntity(2, tkbType: 1);
        var panel = new DerEntityInspectorPanel();
        typeof(DerEntityInspectorPanel)
            .GetField("_searchFilter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(panel, "2");

        var vm = panel.BuildViewModel(repo, "der-test", "der-entity-inspector");

        Assert.Equal(DerEntityInspectorPanel.GetEntityListRows(repo, "2"), vm.EntityIds);
        Assert.Equal(new[] { 2 }, vm.EntityIds);
    }

    private struct SampleDescriptor { public int Value; }
}
