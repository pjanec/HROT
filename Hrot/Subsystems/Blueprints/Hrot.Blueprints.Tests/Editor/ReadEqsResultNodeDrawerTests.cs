using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class ReadEqsResultNodeDrawerTests
{
    private static ReadEqsResultNodeDrawer MakeDrawer() => new();

    private static BlueprintAsset MakeInstanceAsset(params VariableDecl[] vars) => new()
    {
        AssetId  = Guid.NewGuid(),
        Name     = "TestBp",
        Dispatch = BlueprintDispatchKind.Instance,
        Variables = new List<VariableDecl>(vars),
    };

    [Fact]
    public void Drawer_HandlesReadEqsResultNode()
    {
        var drawer = MakeDrawer();
        Assert.True(drawer.Handles(new ReadEqsResultNode { Id = Guid.NewGuid() }));
    }

    [Fact]
    public void Drawer_HandlesReadEqsResultNode_ExcludesOtherTypes()
    {
        var drawer = MakeDrawer();
        Assert.False(drawer.Handles(new WhenNode { Id = Guid.NewGuid() }));
        Assert.False(drawer.Handles(new SpawnEqsSensorNode { Id = Guid.NewGuid() }));
    }

    [Fact]
    public void Drawer_SensorPicker_OnlyShowsEqsSensorHandleVars()
    {
        var sensorVar = new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = "MySensor",
            Type = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" },
        };
        var otherVar = new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = "SomeInt",
            Type = new BlueprintTypeRef { TypeId = "System.Int32" },
        };

        var asset = MakeInstanceAsset(sensorVar, otherVar);
        var node  = new ReadEqsResultNode { Id = Guid.NewGuid() };
        var session = (ReadEqsResultNodeSession)MakeDrawer().CreateSession(node, asset);

        var names = session.GetSensorVariableNamesForTest();

        Assert.Single(names);
        Assert.Equal("MySensor", names[0]);
    }

    [Fact]
    public void Drawer_DispatchGuard_SessionCreated_ForNonInstance()
    {
        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Dispatch = BlueprintDispatchKind.Library,
        };
        var node = new ReadEqsResultNode { Id = Guid.NewGuid() };
        using var session = MakeDrawer().CreateSession(node, asset);
        Assert.NotNull(session);
    }
}
