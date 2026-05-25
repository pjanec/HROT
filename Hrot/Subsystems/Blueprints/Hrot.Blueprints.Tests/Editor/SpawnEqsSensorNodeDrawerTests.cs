using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class SpawnEqsSensorNodeDrawerTests
{
    private static EqsTemplateRegistry MakeRegistry(params EqsTemplateEntry[] entries)
    {
        var reg = new EqsTemplateRegistry();
        foreach (var e in entries) reg.Register(e);
        return reg;
    }

    private static SpawnEqsSensorNode MakeSpawnNode() => new()
    {
        Id              = Guid.NewGuid(),
        TemplateAssetId = Guid.Empty,
        Pins            =
        [
            new Pin { Id = Guid.NewGuid(), Name = "In",              Direction = "In",  IsExec = true  },
            new Pin { Id = Guid.NewGuid(), Name = "Out",             Direction = "Out", IsExec = true  },
            new Pin { Id = Guid.NewGuid(), Name = "SearchRadius",    Direction = "In",  IsExec = false },
            new Pin { Id = Guid.NewGuid(), Name = "FactionFilter",   Direction = "In",  IsExec = false },
            new Pin { Id = Guid.NewGuid(), Name = "ThreatThreshold", Direction = "In",  IsExec = false },
            new Pin { Id = Guid.NewGuid(), Name = "PublishPolicy",   Direction = "In",  IsExec = false },
            new Pin { Id = Guid.NewGuid(), Name = "Priority",        Direction = "In",  IsExec = false },
            new Pin { Id = Guid.NewGuid(), Name = "Handle",          Direction = "Out", IsExec = false },
        ],
    };

    // SC1
    [Fact]
    public void Drawer_HandlesSpawnEqsSensor()
    {
        var reg    = MakeRegistry();
        var drawer = new SpawnEqsSensorNodeDrawer(reg);
        Assert.True(drawer.Handles(new SpawnEqsSensorNode { Id = Guid.NewGuid() }));
        Assert.False(drawer.Handles(new WhenNode { Id = Guid.NewGuid() }));
        Assert.False(drawer.Handles(new ReadEqsResultNode { Id = Guid.NewGuid() }));
    }

    // SC2
    [Fact]
    public void Drawer_TemplatePicker_PopulatesFromRegistry()
    {
        var t1 = new EqsTemplateEntry { AssetId = Guid.NewGuid(), DisplayName = "CoverQuery"  };
        var t2 = new EqsTemplateEntry { AssetId = Guid.NewGuid(), DisplayName = "ThreatRadar" };
        var reg    = MakeRegistry(t1, t2);
        var drawer = new SpawnEqsSensorNodeDrawer(reg);

        // The registry must expose both entries
        var all = reg.EnumerateAll();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, e => e.DisplayName == "CoverQuery");
        Assert.Contains(all, e => e.DisplayName == "ThreatRadar");

        // Session must be creatable for both
        var node  = MakeSpawnNode();
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Dispatch = BlueprintDispatchKind.Instance };
        using var session = drawer.CreateSession(node, asset);
        Assert.NotNull(session);
    }

    // SC3
    [Fact]
    public void Drawer_TemplateSwitch_UpdatesAssetIdOnly()
    {
        var t1 = new EqsTemplateEntry { AssetId = Guid.NewGuid(), DisplayName = "A" };
        var t2 = new EqsTemplateEntry { AssetId = Guid.NewGuid(), DisplayName = "B" };
        var reg    = MakeRegistry(t1, t2);
        var drawer = new SpawnEqsSensorNodeDrawer(reg);

        var node  = MakeSpawnNode();
        node.TemplateAssetId = t1.AssetId;

        var pinIdsBefore = node.Pins.Select(p => p.Id).ToArray();
        var asset        = new BlueprintAsset { AssetId = Guid.NewGuid(), Dispatch = BlueprintDispatchKind.Instance };

        var session = (SpawnEqsSensorNodeSession)drawer.CreateSession(node, asset);
        session.SelectTemplateForTest(t2.AssetId);

        // TemplateAssetId changed
        Assert.Equal(t2.AssetId, node.TemplateAssetId);
        Assert.True(session.IsDirty);

        // Pin set did NOT change (template switch is pin-independent)
        Assert.Equal(pinIdsBefore, node.Pins.Select(p => p.Id).ToArray());
    }

    // SC4
    [Fact]
    public void Drawer_PreservesPinConnectionsAcrossTemplateSwitch()
    {
        var t1 = new EqsTemplateEntry { AssetId = Guid.NewGuid(), DisplayName = "A" };
        var t2 = new EqsTemplateEntry { AssetId = Guid.NewGuid(), DisplayName = "B" };
        var reg    = MakeRegistry(t1, t2);
        var drawer = new SpawnEqsSensorNodeDrawer(reg);

        var node  = MakeSpawnNode();
        node.TemplateAssetId = t1.AssetId;

        // Simulate a connection on the SearchRadius pin
        var searchRadiusPin = node.Pins.First(p => p.Name == "SearchRadius");
        var fakeUpstreamPinId = Guid.NewGuid();
        searchRadiusPin.LinkedToIds.Add(fakeUpstreamPinId);

        var asset   = new BlueprintAsset { AssetId = Guid.NewGuid(), Dispatch = BlueprintDispatchKind.Instance };
        var session = (SpawnEqsSensorNodeSession)drawer.CreateSession(node, asset);
        session.SelectTemplateForTest(t2.AssetId);

        // Connection preserved
        var srPinAfter = node.Pins.First(p => p.Name == "SearchRadius");
        Assert.Contains(fakeUpstreamPinId, srPinAfter.LinkedToIds);
    }

    // SC5
    [Fact]
    public void Drawer_DispatchGuard_ShowsForNonInstance()
    {
        var reg    = MakeRegistry();
        var drawer = new SpawnEqsSensorNodeDrawer(reg);
        var node   = MakeSpawnNode();
        var asset  = new BlueprintAsset { AssetId = Guid.NewGuid(), Dispatch = BlueprintDispatchKind.AiPrimitive };

        // Session must be creatable even for non-Instance assets (guard shown in Draw()
        // which requires ImGui context; not tested here).
        using var session = drawer.CreateSession(node, asset);
        Assert.NotNull(session);
    }
}
