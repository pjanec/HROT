using Fdp.Interfaces;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core.Mission;
using Hrot.Core.Tkb;
using Hrot.Map.Common;
using Hrot.Map.Definitions.Tkb;
using Hrot.UI.Common.AddEntity;
using Hrot.UI.Common.Facades;
using Xunit;

namespace Hrot.Presentation.Tests.AddEntity;

/// <summary>
/// ⭐ <c>CE-1017</c> S1 — the Add Entity catalog is BUILT FROM THE TKB and grouped by DIS name, and every pick arms
/// the type's tool. 📄 docs/DESIGN_Add_Entity_Picker.md D2, D4, D6, D7.
/// </summary>
public sealed class EntityTypeCatalogTests
{
    private static TkbDatabase BuiltIn()
    {
        var db = new TkbDatabase();
        NedTkbCatalog.RegisterAll(db);
        UrbanCombatTkbCatalog.RegisterAll(db);
        return db;
    }

    private static EntityTypeEntry Of(IReadOnlyList<EntityTypeEntry> c, long tkbType) => Assert.Single(c, e => e.TkbType == tkbType);

    [Fact]
    public void CE1017_APhysicalType_IsGroupedByItsNamedDisPath_WithItsIcon()
    {
        var c = EntityTypeCatalog.Build(BuiltIn());

        var t72 = Of(c, TkbEntityTypes.Tank_T72);
        Assert.Equal("T-72", t72.Name);
        Assert.Equal("Platform/Land/Russia/Tank", t72.Category);
        Assert.Equal("t72", t72.IconName);
        Assert.Equal(PlacementToolKind.Point, t72.Tool);
        Assert.True(t72.HasSide);
        Assert.StartsWith("1.1.222.1.", t72.DisText);
    }

    [Fact]
    public void CE1017_ACompositeUnit_IsListedUnderUnits()
    {
        var c = EntityTypeCatalog.Build(BuiltIn());

        Assert.Equal("Units/United States", Of(c, TkbEntityTypes.Unit_TankPlatoon).Category);
    }

    [Fact]
    public void CE1017_MapGraphics_ComeFromTheRegistry_OnceEach_SideLess_FireLineDisabled()
    {
        var c = EntityTypeCatalog.Build(BuiltIn());

        // Area and Route ARE registered as templates (without a master) — still listed exactly once.
        foreach (var g in PlacementToolRegistry.Graphics)
        {
            var e = Of(c, g.TkbType);
            Assert.Equal(EntityTypeCatalog.MapGraphicsFolder, e.Category);
            Assert.False(e.HasSide);
            Assert.Equal(g.Tool, e.Tool);
        }
        Assert.False(Of(c, TkbEntityTypes.TacGraphic_FireLine).IsEnabled);
        Assert.True(Of(c, TkbEntityTypes.TacGraphic_Area).IsEnabled);
    }

    [Fact]
    public void CE1017_AHiddenType_AndAMasterlessTemplate_AreNotListed()
    {
        var db = new TkbDatabase();
        var hidden = new TkbTemplate("Sensor Part", 90001);
        hidden.AddDescriptor(new TkbMasterDto { CustomName = "Sensor Part", HideFromPalette = true });
        db.Register(hidden);
        db.Register(new TkbTemplate("No Master", 90002));
        var shown = new TkbTemplate("Thing", 90003, categoryPath: "Props\\Crates");
        shown.AddDescriptor(new TkbMasterDto { CustomName = "Crate" });
        db.Register(shown);

        var c = EntityTypeCatalog.Build(db);

        Assert.DoesNotContain(c, e => e.TkbType is 90001 or 90002);
        var crate = Of(c, 90003);
        Assert.Equal("Crate", crate.Name);
        Assert.Equal("Props/Crates", crate.Category);   // no DIS ⇒ the template's own category path
        Assert.Equal("entity/_point", EntityTypeCatalog.IconKeyOf(crate));
    }

    [Fact]
    public void CE1017_PickerEntries_SplitSideBearingFromMapGraphics()
    {
        var c = EntityTypeCatalog.Build(BuiltIn());

        var sided = EntityTypeCatalog.ToPickerEntries(c, sideBearing: true).ToList();
        var graphics = EntityTypeCatalog.ToPickerEntries(c, sideBearing: false).ToList();

        Assert.Contains(sided, p => p.Name == "T-72" && p.IconKey == "entity/t72" && p.Category == "Platform/Land/Russia/Tank");
        Assert.DoesNotContain(sided, p => p.Category == EntityTypeCatalog.MapGraphicsFolder);
        Assert.Equal(PlacementToolRegistry.Graphics.Count, graphics.Count);
        Assert.All(graphics, p => Assert.IsType<EntityTypeEntry>(p.Tag));
        Assert.Contains(graphics, p => p.Name == "Fire Line" && !p.IsEnabled && p.Description is { Length: > 0 });
    }

    // ── PlacementToolRegistry.Arm ────────────────────────────────────────────

    private sealed class RecordingSpawn : ISpawnController
    {
        public readonly List<string> Calls = new();
        public void StartPlacementMode(long tkbType, string? initialPropertiesJson = null) => Calls.Add($"point {tkbType} {initialPropertiesJson}");
        public void StartAreaAuthoringMode(string styleOverrideJson = "") => Calls.Add("area");
        public void StartRouteAuthoringMode() => Calls.Add("route");
        public void StartZoneAuthoringMode(string styleOverrideJson = "") => Calls.Add("zone");
    }

    [Fact]
    public void CE1017_Arm_APhysicalType_ArmsThePointToolWithTheSide()
    {
        var spawn = new RecordingSpawn();

        Assert.True(PlacementToolRegistry.Arm(spawn, TkbEntityTypes.Tank_T72, eForceIdentifier.FORCE_OPPOSING));

        Assert.Equal($"point {TkbEntityTypes.Tank_T72} {{\"Affiliation\":\"FORCE_OPPOSING\"}}", Assert.Single(spawn.Calls));
    }

    [Theory]
    [InlineData(TkbEntityTypes.TacGraphic_Area, "area")]
    [InlineData(TkbEntityTypes.TacGraphic_Route, "route")]
    [InlineData(TkbEntityTypes.TerrainZone, "zone")]
    public void CE1017_Arm_AGraphic_ArmsItsOwnTool_NoSideAsked(long tkbType, string expected)
    {
        var spawn = new RecordingSpawn();

        Assert.True(PlacementToolRegistry.Arm(spawn, tkbType, eForceIdentifier.FORCE_FRIENDLY));

        Assert.Equal(expected, Assert.Single(spawn.Calls));
    }

    [Fact]
    public void CE1017_Arm_ATypeWithNoTool_ArmsNothing()
    {
        var spawn = new RecordingSpawn();

        Assert.False(PlacementToolRegistry.Arm(spawn, TkbEntityTypes.TacGraphic_FireLine, eForceIdentifier.FORCE_FRIENDLY));

        Assert.Empty(spawn.Calls);
    }
}
