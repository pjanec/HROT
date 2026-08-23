using Fdp.Toolkit.DER;
using Hrot.ExCon.Panels;
using Moq;
using Xunit;

namespace Hrot.ExCon.Tests;

/// <summary>
/// ⭐⭐ <b>U-obs-5 — <c>InspectorPanel.BuildViewModel</c> and <c>DataMonitorPanel.BuildViewModel</c>,
/// the BUILD half only.</b> 📄 <c>docs/blueprints/batches/QUEUE_Panel_Observability_Sweep.md</c>
/// group 6 ("two are [Obsolete] but live"). ⚠⚠ <b>Correction:</b> both are <c>[Obsolete]</c> AND
/// measured to have ZERO production constructions anywhere in the repository — the queue's "live" did
/// not hold up under `grep -rn "new InspectorPanel(" / "new DataMonitorPanel("`. No
/// <c>PanelSnapshot</c> rails: there is no host to declare/register from.
/// </summary>
#pragma warning disable CS0618 // testing the [Obsolete] BuildViewModel additions directly
public sealed class InspectorAndDataMonitorPanelsBuildTheirModelsTests
{
    [Fact]
    public void InspectorPanel_TheDump_CarriesTheCachedSelection()
    {
        var repo = new DerRepo();
        var entity = repo.CreateEntity(1, tkbType: 1);
        var panel = new InspectorPanel();

        panel.NotifySelectionChanged(entity);
        var vm = panel.BuildViewModel("inspector-test", "excon-inspector");

        Assert.Equal(1, vm.CachedEntityId);
    }

    [Fact]
    public void DataMonitorPanel_TheDump_CarriesTheEntityList()
    {
        var repo = new DerRepo();
        repo.CreateEntity(1, tkbType: 1);
        repo.CreateEntity(2, tkbType: 1);
        var logic = new Mock<IExConLogic>();
        logic.Setup(l => l.Repo).Returns(repo);
        var panel = new DataMonitorPanel();

        var vm = panel.BuildViewModel(logic.Object, "datamonitor-test", "excon-data-monitor");

        Assert.Equal(new[] { 1, 2 }, vm.EntityIds);
    }
}
#pragma warning restore CS0618
