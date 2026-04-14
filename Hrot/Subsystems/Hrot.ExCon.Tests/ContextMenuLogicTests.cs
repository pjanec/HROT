using Hrot.Core.Network;
using Hrot.Core.Mission;
using Hrot.ExCon.Logic;
using Fdp.Toolkit.DER;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hrot.ExCon.Tests;

// â”€â”€â”€ Menu writer stub â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

internal sealed class PushedContextActions
{
    public int MapGroupId { get; init; }
    public IReadOnlyList<int>? ForSelection { get; init; }
    public string MenuDefinitionJson { get; init; } = string.Empty;
}

internal sealed class CapturingEgressWriters : IExConEgressWriters
{
    public List<PushedContextActions> Written { get; } = new();
    public List<MapConfigDto> WrittenConfigs { get; } = new();
    public List<CreateEntityCommand> WrittenCreateCommands { get; } = new();
    public List<int> DeletedEntityIds { get; } = new();
    public List<MapCommandDto> WrittenMapCommands { get; } = new();

    public void WriteMapConfig(MapConfigDto config)        => WrittenConfigs.Add(config);
    public void WriteCreateEntity(CreateEntityCommand cmd) => WrittenCreateCommands.Add(cmd);
    public void WriteDeleteEntity(int entityId)            => DeletedEntityIds.Add(entityId);
    public void WriteMapCommand(MapCommandDto cmd)         => WrittenMapCommands.Add(cmd);
    public void PushContextActions(int mapGroupId, IReadOnlyList<int>? forSelection, string actionsJson)
        => Written.Add(new PushedContextActions { MapGroupId = mapGroupId, ForSelection = forSelection, MenuDefinitionJson = actionsJson });
    public void Dispose() { }
}

// â”€â”€â”€ Tests â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

public class ContextMenuLogicTests
{
    // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static (ContextMenuLogic Logic, CapturingEgressWriters Writer) CreateSut()
    {
        var repo   = new DerRepo();
        var writer = new CapturingEgressWriters();
        var logic  = new ContextMenuLogic(repo, writer);
        return (logic, writer);
    }

    /// <summary>
    /// Factory that also exposes the repo so tests can seed entities.
    /// </summary>
    private static (ContextMenuLogic Logic, CapturingEgressWriters Writer, DerRepo Repo) CreateSutWithRepo()
    {
        var repo   = new DerRepo();
        var writer = new CapturingEgressWriters();
        var logic  = new ContextMenuLogic(repo, writer);
        return (logic, writer, repo);
    }

    private static SelectionChangedEventDto MakeSelectionEvent(int mapId, params int[] entityIds)
        => new SelectionChangedEventDto
        {
            MapId             = mapId,
            SelectedEntityIds = new List<int>(entityIds)
        };

    // â”€â”€ Default strategy â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void InitialStrategy_IsStandard()
    {
        var (logic, _) = CreateSut();
        Assert.Equal(MenuStrategy.Standard, logic.CurrentStrategy);
    }

    // â”€â”€ OnSelectionChanged â€“ payload routing â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void OnSelectionChanged_WritesOneUpdate()
    {
        var (logic, writer) = CreateSut();
        logic.OnSelectionChanged(MakeSelectionEvent(1, 100));

        Assert.Single(writer.Written);
    }

    [Fact]
    public void OnSelectionChanged_ForwardsMapsGroupIdFromEvent()
    {
        var (logic, writer) = CreateSut();
        logic.OnSelectionChanged(MakeSelectionEvent(mapId: 7, 101));

        Assert.Equal(7, writer.Written[0].MapGroupId);
    }

    [Fact]
    public void OnSelectionChanged_ForwardsSelectionList()
    {
        var (logic, writer) = CreateSut();
        logic.OnSelectionChanged(MakeSelectionEvent(1, 10, 20, 30));

        Assert.Equal(new List<int> { 10, 20, 30 }, writer.Written[0].ForSelection);
    }

    // â”€â”€ Standard strategy menu content â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void StandardStrategy_MenuContainsExpectedActionIds()
    {
        var (logic, writer) = CreateSut();
        logic.SetStrategy(MenuStrategy.Standard);
        logic.OnSelectionChanged(MakeSelectionEvent(1, 5));

        var items = ParseMenuItems(writer.Written[0].MenuDefinitionJson);
        var ids   = items.Select(i => (int)i["id"]!).ToList();

        Assert.Contains(ContextMenuActions.CenterOnEntity, ids);
        Assert.Contains(ContextMenuActions.Properties,     ids);
    }

    [Fact]
    public void StandardStrategy_MenuContainsExpectedLabels()
    {
        var (logic, writer) = CreateSut();
        logic.OnSelectionChanged(MakeSelectionEvent(1, 5));

        var items  = ParseMenuItems(writer.Written[0].MenuDefinitionJson);
        var labels = items.Select(i => (string)i["label"]!).ToList();

        Assert.Contains("Center on Entity", labels);
        Assert.Contains("Properties...",    labels);
    }

    // â”€â”€ Admin strategy â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void AdminStrategy_MenuContainsDeleteAndTeleport()
    {
        var (logic, writer) = CreateSut();
        logic.SetStrategy(MenuStrategy.Admin);
        logic.OnSelectionChanged(MakeSelectionEvent(1, 5));

        var items = ParseMenuItems(writer.Written[0].MenuDefinitionJson);
        var ids   = items.Select(i => (int)i["id"]!).ToList();

        // Delete was moved to Standard strategy; Admin only contains Teleport
        Assert.DoesNotContain(ContextMenuActions.Delete,   ids);
        Assert.Contains(ContextMenuActions.Teleport, ids);
    }

    [Fact]
    public void AdminStrategy_DeleteItem_HasDestructiveStyle()
    {
        // Delete is in Standard strategy (moved from Admin in BATCH-01)
        var (logic, writer) = CreateSut();
        logic.SetStrategy(MenuStrategy.Standard);
        logic.OnSelectionChanged(MakeSelectionEvent(1, 5));

        var items = ParseMenuItems(writer.Written[0].MenuDefinitionJson);
        var delete = items.Single(i => (int)i["id"]! == ContextMenuActions.Delete);

        Assert.Equal("destructive", (string?)delete["style"]);
    }

    // â”€â”€ DamageControl strategy â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void DamageControlStrategy_MenuContainsRepairAndReinforce()
    {
        var (logic, writer) = CreateSut();
        logic.SetStrategy(MenuStrategy.DamageControl);
        logic.OnSelectionChanged(MakeSelectionEvent(1, 5));

        var ids = ParseMenuItems(writer.Written[0].MenuDefinitionJson)
                    .Select(i => (int)i["id"]!).ToList();

        Assert.Contains(ContextMenuActions.Repair,    ids);
        Assert.Contains(ContextMenuActions.Reinforce, ids);
    }

    // â”€â”€ Logistics strategy â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void LogisticsStrategy_MenuContainsResupplyAndTransfer()
    {
        var (logic, writer) = CreateSut();
        logic.SetStrategy(MenuStrategy.Logistics);
        logic.OnSelectionChanged(MakeSelectionEvent(1, 5));

        var ids = ParseMenuItems(writer.Written[0].MenuDefinitionJson)
                    .Select(i => (int)i["id"]!).ToList();

        Assert.Contains(ContextMenuActions.Resupply, ids);
        Assert.Contains(ContextMenuActions.Transfer, ids);
    }

    // â”€â”€ SetStrategy changes output â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void SetStrategy_ChangesMenuOnNextSelectionChange()
    {
        var (logic, writer) = CreateSut();

        logic.SetStrategy(MenuStrategy.Standard);
        logic.OnSelectionChanged(MakeSelectionEvent(1, 5));
        var standardIds = ParseMenuItems(writer.Written[0].MenuDefinitionJson)
                            .Select(i => (int)i["id"]!).ToList();

        logic.SetStrategy(MenuStrategy.Admin);
        logic.OnSelectionChanged(MakeSelectionEvent(1, 5));
        var adminIds = ParseMenuItems(writer.Written[1].MenuDefinitionJson)
                         .Select(i => (int)i["id"]!).ToList();

        // Delete is in Standard (moved from Admin in BATCH-01); Admin has Teleport instead
        Assert.Contains(ContextMenuActions.Delete,          standardIds);
        Assert.DoesNotContain(ContextMenuActions.Delete,    adminIds);
        Assert.Contains(ContextMenuActions.Teleport,        adminIds);
        Assert.DoesNotContain(ContextMenuActions.Teleport,  standardIds);
    }

    [Fact]
    public void SetStrategy_UpdatesCurrentStrategyProperty()
    {
        var (logic, _) = CreateSut();
        logic.SetStrategy(MenuStrategy.Logistics);
        Assert.Equal(MenuStrategy.Logistics, logic.CurrentStrategy);
    }

    // â”€â”€ JSON serialisation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void MenuDefinitionJson_IsValidJson()
    {
        var (logic, writer) = CreateSut();
        logic.OnSelectionChanged(MakeSelectionEvent(1, 5));

        var json = writer.Written[0].MenuDefinitionJson;
        var ex   = Record.Exception(() => JArray.Parse(json));

        Assert.Null(ex);
    }

    [Fact]
    public void MenuDefinitionJson_EachItemHasIdAndLabelFields()
    {
        var (logic, writer) = CreateSut();
        logic.OnSelectionChanged(MakeSelectionEvent(1, 5));

        var items = ParseMenuItems(writer.Written[0].MenuDefinitionJson);
        foreach (var item in items)
        {
            Assert.True(item.ContainsKey("id"),    "item missing 'id' field");
            Assert.True(item.ContainsKey("label"), "item missing 'label' field");
        }
    }

    // â”€â”€ OnActionInvoked / event â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void OnActionInvoked_FiresActionInvokedEvent()
    {
        var (logic, _) = CreateSut();
        ContextActionInvokedDto? captured = null;
        logic.ActionInvoked += evt => captured = evt;

        var action = new ContextActionInvokedDto { ActionId = ContextMenuActions.CenterOnEntity, MapId = 3 };
        logic.OnActionInvoked(action);

        Assert.NotNull(captured);
        Assert.Equal(ContextMenuActions.CenterOnEntity, captured!.ActionId);
        Assert.Equal(3, captured.MapId);
    }

    [Fact]
    public void OnActionInvoked_NoSubscriber_DoesNotThrow()
    {
        var (logic, _) = CreateSut();
        var ex = Record.Exception(() =>
            logic.OnActionInvoked(new ContextActionInvokedDto { ActionId = ContextMenuActions.Properties }));

        Assert.Null(ex);
    }

    // â”€â”€ Helper â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static List<JObject> ParseMenuItems(string json)
        => JArray.Parse(json).Cast<JObject>().ToList();

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // Map-canvas (empty selection) context menu
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    /// <summary>
    /// When no entity ID is in the selection, the ExCon must return the map-canvas
    /// menu which contains the "Measure..." action and nothing else.
    /// </summary>
    [Fact]
    public void EmptySelection_ReturnsMeasureAction()
    {
        var (logic, writer) = CreateSut();
        // Pass an event with an empty (non-null) list â€” simulates right-click on empty space.
        logic.OnSelectionChanged(new SelectionChangedEventDto
        {
            MapId             = 1,
            SelectedEntityIds = new List<int>()
        });

        var ids = ParseMenuItems(writer.Written[0].MenuDefinitionJson)
                    .Select(i => (int)i["id"]!).ToList();

        Assert.Contains(ContextMenuActions.Measure, ids);
    }

    [Fact]
    public void EmptySelection_DoesNotReturnEntityActions()
    {
        var (logic, writer) = CreateSut();
        logic.OnSelectionChanged(new SelectionChangedEventDto
        {
            MapId             = 1,
            SelectedEntityIds = new List<int>()
        });

        var ids = ParseMenuItems(writer.Written[0].MenuDefinitionJson)
                    .Select(i => (int)i["id"]!).ToList();

        Assert.DoesNotContain(ContextMenuActions.CenterOnEntity, ids);
        Assert.DoesNotContain(ContextMenuActions.Properties,     ids);
    }

    [Fact]
    public void NullSelection_ReturnsMeasureAction()
    {
        var (logic, writer) = CreateSut();
        logic.OnSelectionChanged(new SelectionChangedEventDto
        {
            MapId             = 1,
            SelectedEntityIds = null
        });

        var ids = ParseMenuItems(writer.Written[0].MenuDefinitionJson)
                    .Select(i => (int)i["id"]!).ToList();

        Assert.Contains(ContextMenuActions.Measure, ids);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // Edit Drawing (editable MapOverlayDescriptor)
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    /// <summary>
    /// When the selected entity has a <see cref="MapOverlayDescriptor"/> with
    /// <c>IsEditable = true</c>, "Edit Drawing" (ID 100) must appear in the menu.
    /// </summary>
    [Fact]
    public void EditableOverlay_AddsEditDrawingAction()
    {
        var (logic, writer, repo) = CreateSutWithRepo();

        var entity = repo.CreateEntity(42, tkbType: 0);
        entity.SetDescriptor(new MapOverlayDescriptor { IsEditable = true });

        logic.OnSelectionChanged(MakeSelectionEvent(1, 42));

        var ids = ParseMenuItems(writer.Written[0].MenuDefinitionJson)
                    .Select(i => (int)i["id"]!).ToList();

        Assert.Contains(ContextMenuActions.EditOverlay, ids);
    }

    [Fact]
    public void EditableOverlay_EditDrawingLabel_IsCorrect()
    {
        var (logic, writer, repo) = CreateSutWithRepo();

        var entity = repo.CreateEntity(42, tkbType: 0);
        entity.SetDescriptor(new MapOverlayDescriptor { IsEditable = true });

        logic.OnSelectionChanged(MakeSelectionEvent(1, 42));

        var items = ParseMenuItems(writer.Written[0].MenuDefinitionJson);
        var editItem = items.SingleOrDefault(i => (int)i["id"]! == ContextMenuActions.EditOverlay);

        Assert.NotNull(editItem);
        // Label was renamed "Edit Drawing" â†’ "Edit Shape" in X005 to align with SharedContextMenuPopulator.
        Assert.Equal("Edit Shape", (string)editItem!["label"]!);
    }

    /// <summary>
    /// An overlay with <c>IsEditable = false</c> must NOT produce "Edit Drawing".
    /// </summary>
    [Fact]
    public void NonEditableOverlay_DoesNotAddEditDrawingAction()
    {
        var (logic, writer, repo) = CreateSutWithRepo();

        var entity = repo.CreateEntity(42, tkbType: 0);
        entity.SetDescriptor(new MapOverlayDescriptor { IsEditable = false });

        logic.OnSelectionChanged(MakeSelectionEvent(1, 42));

        var ids = ParseMenuItems(writer.Written[0].MenuDefinitionJson)
                    .Select(i => (int)i["id"]!).ToList();

        Assert.DoesNotContain(ContextMenuActions.EditOverlay, ids);
    }

    /// <summary>
    /// An entity without a MapOverlayDescriptor must NOT produce "Edit Drawing".
    /// </summary>
    [Fact]
    public void EntityWithoutOverlay_DoesNotAddEditDrawingAction()
    {
        var (logic, writer, repo) = CreateSutWithRepo();
        repo.CreateEntity(42, tkbType: 0); // no overlay descriptor

        logic.OnSelectionChanged(MakeSelectionEvent(1, 42));

        var ids = ParseMenuItems(writer.Written[0].MenuDefinitionJson)
                    .Select(i => (int)i["id"]!).ToList();

        Assert.DoesNotContain(ContextMenuActions.EditOverlay, ids);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // Edit Route (route entities â€” TkbType == TacGraphic_Route)
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    /// <summary>
    /// A route entity (TkbType == TacGraphic_Route = 8802) must include
    /// "Edit Route" (id = 101) in its context menu.
    /// </summary>
    [Fact]
    public void RouteEntity_AddsEditRouteAction()
    {
        var (logic, writer, repo) = CreateSutWithRepo();
        repo.CreateEntity(55, tkbType: Hrot.Map.Common.TkbEntityTypes.TacGraphic_Route);

        logic.OnSelectionChanged(MakeSelectionEvent(1, 55));

        var ids = ParseMenuItems(writer.Written[0].MenuDefinitionJson)
                    .Select(i => (int)i["id"]!).ToList();

        Assert.Contains(ContextMenuActions.EditRoute, ids);
    }

    /// <summary>
    /// A route entity must NOT include "Edit Personal Route" in its menu
    /// (personal routes only apply to vehicle-type entities).
    /// </summary>
    [Fact]
    public void RouteEntity_DoesNotAddEditPersonalRouteAction()
    {
        var (logic, writer, repo) = CreateSutWithRepo();
        repo.CreateEntity(55, tkbType: Hrot.Map.Common.TkbEntityTypes.TacGraphic_Route);

        logic.OnSelectionChanged(MakeSelectionEvent(1, 55));

        var ids = ParseMenuItems(writer.Written[0].MenuDefinitionJson)
                    .Select(i => (int)i["id"]!).ToList();

        Assert.DoesNotContain(ContextMenuActions.EditPersonalRoute, ids);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // Edit Personal Route (vehicle/unit entities)
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    /// <summary>
    /// A non-TacGraphic entity (e.g. a tank) should include
    /// "Edit Personal Route" (id = 102) in its context menu.
    /// </summary>
    [Fact]
    public void VehicleEntity_AddsEditPersonalRouteAction()
    {
        var (logic, writer, repo) = CreateSutWithRepo();
        repo.CreateEntity(66, tkbType: Hrot.Map.Common.TkbEntityTypes.Tank_M1Abrams);

        logic.OnSelectionChanged(MakeSelectionEvent(1, 66));

        var ids = ParseMenuItems(writer.Written[0].MenuDefinitionJson)
                    .Select(i => (int)i["id"]!).ToList();

        Assert.Contains(ContextMenuActions.EditPersonalRoute, ids);
    }

    /// <summary>
    /// A TacGraphic_Area (area overlay) entity must NOT include "Edit Personal Route".
    /// </summary>
    [Fact]
    public void AreaOverlayEntity_DoesNotAddEditPersonalRouteAction()
    {
        var (logic, writer, repo) = CreateSutWithRepo();
        repo.CreateEntity(77, tkbType: Hrot.Map.Common.TkbEntityTypes.TacGraphic_Area);

        logic.OnSelectionChanged(MakeSelectionEvent(1, 77));

        var ids = ParseMenuItems(writer.Written[0].MenuDefinitionJson)
                    .Select(i => (int)i["id"]!).ToList();

        Assert.DoesNotContain(ContextMenuActions.EditPersonalRoute, ids);
    }
}
