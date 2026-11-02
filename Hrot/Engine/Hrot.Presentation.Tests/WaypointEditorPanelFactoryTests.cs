using System;
using System.IO;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Scenario;
using Hrot.UI.Common.Panels;
using Xunit;

namespace Hrot.Presentation.Tests;

/// <summary>CE-4214 - the waypoint pick writes the entity's GUID on EVERY host: the one factory supplies the lookup, and nothing else builds the panel.</summary>
public class WaypointEditorPanelFactoryTests
{
    [Fact]
    public void TheFactoryBuiltPanel_HasTheGuidHook_AndItAnswersFromTheHostsWorldAndMap()
    {
        using var repo = new EntityRepository();
        repo.RegisterComponent<EntityGuid>();
        var map = new NetworkEntityMap();
        var e = repo.CreateEntity();
        var guid = Guid.NewGuid();
        repo.SetComponent(e, new EntityGuid(guid));
        map.Register(7, e);

        var panel = WaypointEditorPanelFactory.Create(() => repo, () => map);
        Assert.NotNull(panel.EntityGuidOf);
        Assert.Equal(guid, panel.EntityGuidOf!(7));
        Assert.Null(panel.EntityGuidOf!(8));   // nobody
        Assert.Null(WaypointEditorPanelFactory.Create(() => null, () => null).EntityGuidOf!(7));   // a host with no world yet
    }

    /// <summary>The silent-default rail: a production host that builds the panel itself has no hook (the S9' (e) miss). Only the factory may.</summary>
    [Fact]
    public void NoProductionCodeBuildsTheWaypointPanelExceptTheFactory()
    {
        var root = ContentLayout.TryFindSourceTree()!;
        var offenders = Directory.EnumerateFiles(Path.Combine(root, "Hrot"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .Where(f => !f.Contains("Tests") && !f.EndsWith("WaypointEditorPanelFactory.cs"))
            .Where(f => File.ReadAllText(f).Contains("new Hrot.UI.Common.Panels.WaypointEditorPanel(") || File.ReadAllText(f).Contains("new WaypointEditorPanel("))
            .Select(f => Path.GetRelativePath(root, f)).ToList();
        Assert.True(offenders.Count == 0, "build the panel with WaypointEditorPanelFactory.Create: " + string.Join(", ", offenders));
    }
}
