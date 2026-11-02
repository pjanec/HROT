using System.Text.Json;
using Fdp.Core;
using Fdp.Toolkit.Tkb;
using Hrot.Common.Constants;
using Hrot.Common.Interactions;
using Hrot.Core.Mission;
using Hrot.Map.Common;
using Hrot.Map.Definitions.Tkb;
using Hrot.Presentation.Systems;
using Hrot.UI.Common.AddEntity;
using Hrot.UI.Common.Facades;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;
using Xunit;

namespace Hrot.Presentation.Tests.AddEntity;

/// <summary>
/// ⭐ <c>CE-1017</c> S3 — the empty-map <c>Add Entity</c> submenu: a menu item → the type picker → the picked
/// type's tool armed with the menu's side. 📄 docs/DESIGN_Add_Entity_Picker.md D4, D6, D9 and the §3 sequence.
/// </summary>
public sealed class AddEntityActionTests
{
    private sealed class RecordingSpawn : ISpawnController
    {
        public readonly List<string> Calls = new();
        public void StartPlacementMode(long tkbType, string? initialPropertiesJson = null) => Calls.Add($"point {tkbType} {initialPropertiesJson}");
        public void StartAreaAuthoringMode(string styleOverrideJson = "") => Calls.Add("area");
        public void StartAreaAuthoringMode(long tkbType, string styleOverrideJson = "") => Calls.Add($"area:{tkbType}");
        public void StartRouteAuthoringMode() => Calls.Add("route");
        public void StartZoneAuthoringMode(string styleOverrideJson = "") => Calls.Add("zone");
    }

    private sealed class Rig
    {
        public readonly TkbDatabase Tkb = new();
        public readonly RecordingSpawn Spawn = new();
        public PickerRequest? Request;
        public Action<PickerResult>? OnChosen;
        public string? Suspended;
        public bool HasPicker = true;
        public readonly AddEntityAction Action;

        public Rig(GalleryData? gallery = null)
        {
            NedTkbCatalog.RegisterAll(Tkb);
            Action = new AddEntityAction(
                () => Tkb,
                () => HasPicker ? (req, cb) => { Request = req; OnChosen = cb; } : null,
                () => Spawn,
                () => Suspended, gallery: () => gallery);
        }

        public void Pick(string name)
        {
            var entry = Request!.ItemsProvider().Single(e => e.Name == name);
            OnChosen!(new PickerResult(new[] { entry }));
        }
    }

    [Fact]
    public void CE1017_HostileMenuItem_OpensTheTreePicker_AndThePickArmsAHostilePointTool()
    {
        var rig = new Rig();
        var actions = new GlobalActionRegistry();
        rig.Action.RegisterOn(actions);

        Assert.True(actions.TryGetHandler(GlobalActionIds.AddEntityHostile, out var handler));
        handler(null!, Entity.Null);

        Assert.NotNull(rig.Request);
        Assert.Equal(PickerLayout.Tree, rig.Request!.Layout);
        Assert.True(rig.Request.FoldSingleChildFolders);
        Assert.True(rig.Request.ShowPreview);
        Assert.DoesNotContain(rig.Request.ItemsProvider(), e => e.Category == EntityTypeCatalog.MapGraphicsFolder);
        Assert.Empty(rig.Spawn.Calls);   // 🔒 D4: nothing is created or armed before the pick

        rig.Pick("T-72");

        Assert.Equal($"point {TkbEntityTypes.Tank_T72} {{\"Affiliation\":\"FORCE_OPPOSING\"}}", Assert.Single(rig.Spawn.Calls));
    }

    // ── ⭐ CE-4142 GL4 — the gallery request ────────────────────────────────────

    [Fact]
    public void CE4142_GL4_WithGalleryData_TheMenuOpensTheGallery_GridSidebarPanePlaceAndRememberedSize()
    {
        var rig = new Rig(EntityTypeCatalogTests.ShippedGallery());
        Assert.True(rig.Action.Open(GlobalActionIds.AddEntityHostile));
        var r = rig.Request!;

        Assert.Equal(PickerLayout.Grid, r.Layout);
        Assert.Equal(new[] { PickerLayout.Tree, PickerLayout.Grid }, r.LayoutChoices);   // List ⇄ Thumbnails, remembered per ContextKey by the view store
        Assert.NotNull(r.GroupSidebar);
        Assert.Equal("Recent & favourites", r.GroupSidebar!.RecentGroup);
        Assert.Equal("Recent & favourites", r.GroupSidebar.Order[0]);
        Assert.Equal(GalleryGroups.OtherName, r.GroupSidebar.Order[^1]);
        Assert.True(r.ShowSidePane);
        Assert.NotNull(r.DrawPane);
        Assert.Equal(new System.Numerics.Vector2(1100f, 700f), r.DefaultSize);
        var place = Assert.Single(r.Actions!);
        Assert.Equal(("place", "Place on map", true), (place.Id, place.Label, place.IsDefault));
        Assert.Equal("add-entity/gallery-types", r.ContextKey);

        // rows: Group/Chip categories, the tile line, side-bearing only
        var rows = r.ItemsProvider().ToList();
        var t72 = rows.Single(e => e.Name == "T-72");
        Assert.Equal("Ground vehicles/Tank", t72.Category);
        Assert.Contains("Tank", t72.Description);
        Assert.DoesNotContain(rows, e => e.Category!.StartsWith("Areas"));
    }

    [Fact]
    public void CE4142_GL4_PlaceOnMap_ArmsExactlyAsTheTreePickDid_AnotherActionDoesNot()
    {
        var rig = new Rig(EntityTypeCatalogTests.ShippedGallery());
        rig.Action.Open(GlobalActionIds.AddEntityHostile);
        var entry = rig.Request!.ItemsProvider().Single(e => e.Name == "T-72");

        rig.OnChosen!(new PickerResult(new[] { entry }) { ActionId = "something-else" });
        Assert.Empty(rig.Spawn.Calls);

        rig.OnChosen!(new PickerResult(new[] { entry }) { ActionId = AddEntityAction.PlaceActionId });
        Assert.Equal($"point {TkbEntityTypes.Tank_T72} {{\"Affiliation\":\"FORCE_OPPOSING\"}}", Assert.Single(rig.Spawn.Calls));
    }

    [Fact]
    public void CE4142_GL4_MapGraphicsGallery_GroupsTheGraphics_AndFireLineCannotBePlaced()
    {
        var rig = new Rig(EntityTypeCatalogTests.ShippedGallery());
        rig.Action.Open(GlobalActionIds.AddMapGraphic);
        var r = rig.Request!;

        Assert.Equal("add-entity/gallery-graphics", r.ContextKey);
        var rows = r.ItemsProvider().ToList();
        Assert.Equal("Areas & routes", rows.Single(e => e.Name == "Route").Category);
        var fire = rows.Single(e => e.Name == "Fire Line");
        Assert.Equal("Tactical symbols", fire.Category);
        Assert.NotNull(r.Actions![0].DisabledReason!(fire));       // Place on map says why
        Assert.Null(r.Actions[0].DisabledReason!(rows.Single(e => e.Name == "Area")));
    }

    [Fact]
    public void CE4142_GL4_SidePaneRows_NameTheSide_AndTheFactsTheTkbHolds()
    {
        var rig = new Rig(EntityTypeCatalogTests.ShippedGallery());
        rig.Action.Open(GlobalActionIds.AddEntityFriendly);
        var entry = rig.Request!.ItemsProvider().Single(e => e.Name == "T-72");

        var rows = AddEntityPane.Rows(rig.Tkb, entry, eForceIdentifier.FORCE_FRIENDLY);

        Assert.Contains(("Placed as", "Friendly"), rows);
        Assert.Contains(rows, r => r.Label == "DIS" && r.Value.StartsWith("1.1.222.1."));
        Assert.Empty(AddEntityPane.Rows(rig.Tkb, new PickerEntry("x", "x", null, null, null, null, null), null));   // not a type ⇒ nothing
    }

    [Fact]
    public void CE4142_GL4_WithoutGalleryData_TheTreeStaysAsBefore_AndARootedRequestIsAlwaysTheTree()
    {
        var rig = new Rig(gallery: null);
        rig.Action.Open(GlobalActionIds.AddEntityFriendly);
        Assert.Equal(PickerLayout.Tree, rig.Request!.Layout);
        Assert.Null(rig.Request.GroupSidebar);
        Assert.Null(rig.Request.Actions);

        var rooted = AddEntityAction.BuildRequest(rig.Tkb, null, null, EntityTypeCatalog.UnitsFolder, EntityTypeCatalogTests.ShippedGallery());
        Assert.Equal(PickerLayout.Tree, rooted.Layout);   // the ExCon ORBAT's New Unit keeps its DIS folder tree
    }

    [Fact]
    public void CE1017_MapGraphicsMenuItem_ListsOnlyGraphics_AndAnAreaPickArmsTheAreaTool()
    {
        var rig = new Rig();

        Assert.True(rig.Action.Open(GlobalActionIds.AddMapGraphic));
        Assert.All(rig.Request!.ItemsProvider(), e => Assert.Equal(EntityTypeCatalog.MapGraphicsFolder, e.Category));

        rig.Pick("Area");

        Assert.Equal("area", Assert.Single(rig.Spawn.Calls));
    }

    [Fact]
    public void CE1017_ACancelledPicker_ArmsNothing()
    {
        var rig = new Rig();
        rig.Action.Open(GlobalActionIds.AddEntityFriendly);

        rig.OnChosen!(new PickerResult(Array.Empty<PickerEntry>()));

        Assert.Empty(rig.Spawn.Calls);
    }

    [Fact]
    public void CE1017_WhileSuspended_NothingOpens()
    {
        var rig = new Rig { Suspended = "suspended in Preview" };

        Assert.False(rig.Action.Open(GlobalActionIds.AddEntityFriendly));
        Assert.Null(rig.Request);
    }

    // ── the canvas menu ──────────────────────────────────────────────────────

    private static List<JsonElement> Items(string json)
        => JsonDocument.Parse(json).RootElement.EnumerateArray().ToList();

    [Fact]
    public void CE1017_CanvasMenu_OffersAddEntity_WithTheFourItems_OnACapableHost()
    {
        var rig = new Rig();
        var json = new CanvasMenuUpdateSystem(rig.Action).CurrentJson();

        var add = Items(json)[0];
        Assert.Equal("Add Entity", add.GetProperty("label").GetString());
        Assert.True(add.GetProperty("enabled").GetBoolean());
        var ids = add.GetProperty("children").EnumerateArray()
            .Where(c => c.TryGetProperty("id", out _)).Select(c => c.GetProperty("id").GetInt32());
        Assert.Equal(new[] { GlobalActionIds.AddEntityFriendly, GlobalActionIds.AddEntityHostile,
                             GlobalActionIds.AddEntityNeutral, GlobalActionIds.AddMapGraphic }, ids);
        Assert.Contains(Items(json), i => i.TryGetProperty("id", out var id) && id.GetInt32() == GlobalActionIds.Measure);
    }

    [Fact]
    public void CE1017_CanvasMenu_GreysAddEntity_WithItsReason_WhileSuspended()
    {
        var rig = new Rig { Suspended = "suspended in Preview" };

        var add = Items(new CanvasMenuUpdateSystem(rig.Action).CurrentJson())[0];

        Assert.False(add.GetProperty("enabled").GetBoolean());
        Assert.Contains("suspended in Preview", add.GetProperty("label").GetString());
    }

    [Fact]
    public void CE1017_CanvasMenu_OmitsAddEntity_OnAHostThatCannotServiceIt()
    {
        var noPicker = new Rig { HasPicker = false };

        Assert.DoesNotContain("Add Entity", new CanvasMenuUpdateSystem(noPicker.Action).CurrentJson());
        Assert.DoesNotContain("Add Entity", new CanvasMenuUpdateSystem().CurrentJson());
    }

    [Fact]
    public void CE1017_CanvasMenu_FollowsAvailability_WhenThePickerArrivesLater()
    {
        var rig = new Rig { HasPicker = false };
        var sys = new CanvasMenuUpdateSystem(rig.Action);
        Assert.DoesNotContain("Add Entity", sys.CurrentJson());

        rig.HasPicker = true;   // the host builds its picker shell after the map

        Assert.Contains("Add Entity", sys.CurrentJson());
    }
}
