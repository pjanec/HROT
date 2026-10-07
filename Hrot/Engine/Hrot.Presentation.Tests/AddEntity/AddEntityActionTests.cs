using System.Text.Json;
using Fdp.Core;
using Fdp.Toolkit.Tkb;
using Hrot.Common.Constants;
using Hrot.Common.Interactions;
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

        public Rig()
        {
            NedTkbCatalog.RegisterAll(Tkb);
            Action = new AddEntityAction(
                () => Tkb,
                () => HasPicker ? (req, cb) => { Request = req; OnChosen = cb; } : null,
                () => Spawn,
                () => Suspended);
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
