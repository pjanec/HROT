using Fdp.Toolkit.Tkb;
using Hrot.Core.Mission;
using Hrot.Map.Common;
using Hrot.Map.Definitions.Tkb;
using Hrot.UI.Common.AddEntity;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Panels;
using NodeEditor.UI.Picker;
using Xunit;

namespace Hrot.Presentation.Tests.AddEntity;

/// <summary>
/// ⭐ <c>CE-1017</c> S4 — the spawner panel stays and calls the Add Entity picker: a pick selects the type AND arms the
/// tool with the panel's side (Neutral included). 📄 docs/DESIGN_Add_Entity_Picker.md D6, S4.
/// </summary>
public sealed class SpawnerPanelPickerTests
{
    private sealed class RecordingSpawn : ISpawnController
    {
        public readonly List<string> Calls = new();
        public void StartPlacementMode(long tkbType, string? initialPropertiesJson = null) => Calls.Add($"point {tkbType} {initialPropertiesJson}");
        public void StartAreaAuthoringMode(string styleOverrideJson = "") => Calls.Add("area");
        public void StartRouteAuthoringMode() => Calls.Add("route");
        public void StartZoneAuthoringMode(string styleOverrideJson = "") => Calls.Add("zone");
    }

    private static TkbDatabase Tkb()
    {
        var db = new TkbDatabase();
        NedTkbCatalog.RegisterAll(db);
        return db;
    }

    [Fact]
    public void CE1017_ANeutralPick_SelectsTheType_AndArmsANeutralPlacement()
    {
        var tkb = Tkb();
        PickerRequest? req = null; Action<PickerResult>? cb = null;
        var panel = new SpawnerPanel(EntityTypeCatalog.SpawnerEntries(tkb))
        {
            Tkb = () => tkb,
            OpenPicker = () => (r, c) => { req = r; cb = c; },
        };
        var spawn = new RecordingSpawn();
        panel.HandleAffiliationChange(eForceIdentifier.FORCE_NEUTRAL);

        Assert.True(panel.HasPicker);
        Assert.True(panel.HandleChooseType(spawn));
        Assert.Equal("Add Neutral Entity", req!.Title);
        Assert.True(req.FoldSingleChildFolders && req.ShowPreview);
        Assert.DoesNotContain(req.ItemsProvider(), e => e.Category == EntityTypeCatalog.MapGraphicsFolder);

        cb!(new PickerResult(new[] { req.ItemsProvider().Single(e => e.Name == "HMMWV") }));

        Assert.Equal(TkbEntityTypes.Truck_HMMWV, panel.SelectedType);
        Assert.Equal($"point {TkbEntityTypes.Truck_HMMWV} {{\"Affiliation\":\"FORCE_NEUTRAL\"}}", Assert.Single(spawn.Calls));
    }

    [Fact]
    public void CE1017_WithoutAPicker_ThePanelKeepsItsFlatList()
    {
        var panel = new SpawnerPanel(EntityTypeCatalog.SpawnerEntries(Tkb()));

        Assert.False(panel.HasPicker);
        Assert.False(panel.HandleChooseType(new RecordingSpawn()));
        Assert.Contains(panel.FilteredEntries, e => e.Name == "T-72");
        Assert.DoesNotContain(panel.FilteredEntries, e => e.TkbId == TkbEntityTypes.TacGraphic_Area);
    }

    [Fact]
    public void CE1017_TheFallbackList_IsBuiltFromTheTkb_NotAHandList()
    {
        Assert.Empty(EntityTypeCatalog.SpawnerEntries(null));
        Assert.Equal(
            EntityTypeCatalog.Build(Tkb()).Count(e => e.HasSide),
            EntityTypeCatalog.SpawnerEntries(Tkb()).Count);
    }
}
