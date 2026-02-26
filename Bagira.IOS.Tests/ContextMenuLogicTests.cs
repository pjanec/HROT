using Bagira.BDC.SSTM;
using Bagira.IOS.Logic;
using Bagira.IOS.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bagira.IOS.Tests;

// ─── Menu writer stub ─────────────────────────────────────────────────────────

internal sealed class CapturingMenuWriter : IDdsWriter<ContextActionsUpdate>
{
    public List<ContextActionsUpdate> Written { get; } = new();
    public void Write(ContextActionsUpdate sample) => Written.Add(sample);
}

// ─── Tests ────────────────────────────────────────────────────────────────────

public class ContextMenuLogicTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (ContextMenuLogic Logic, CapturingMenuWriter Writer) CreateSut()
    {
        var writer = new CapturingMenuWriter();
        var logic  = new ContextMenuLogic(writer);
        return (logic, writer);
    }

    private static SelectionChangedEvent MakeSelectionEvent(int mapId, params int[] entityIds)
        => new SelectionChangedEvent
        {
            MapId             = mapId,
            SelectedEntityIds = new List<int>(entityIds)
        };

    // ── Default strategy ──────────────────────────────────────────────────────

    [Fact]
    public void InitialStrategy_IsStandard()
    {
        var (logic, _) = CreateSut();
        Assert.Equal(MenuStrategy.Standard, logic.CurrentStrategy);
    }

    // ── OnSelectionChanged – payload routing ──────────────────────────────────

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

    // ── Standard strategy menu content ────────────────────────────────────────

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

    // ── Admin strategy ────────────────────────────────────────────────────────

    [Fact]
    public void AdminStrategy_MenuContainsDeleteAndTeleport()
    {
        var (logic, writer) = CreateSut();
        logic.SetStrategy(MenuStrategy.Admin);
        logic.OnSelectionChanged(MakeSelectionEvent(1, 5));

        var items = ParseMenuItems(writer.Written[0].MenuDefinitionJson);
        var ids   = items.Select(i => (int)i["id"]!).ToList();

        Assert.Contains(ContextMenuActions.Delete,   ids);
        Assert.Contains(ContextMenuActions.Teleport, ids);
    }

    [Fact]
    public void AdminStrategy_DeleteItem_HasDestructiveStyle()
    {
        var (logic, writer) = CreateSut();
        logic.SetStrategy(MenuStrategy.Admin);
        logic.OnSelectionChanged(MakeSelectionEvent(1, 5));

        var items = ParseMenuItems(writer.Written[0].MenuDefinitionJson);
        var delete = items.Single(i => (int)i["id"]! == ContextMenuActions.Delete);

        Assert.Equal("destructive", (string?)delete["style"]);
    }

    // ── DamageControl strategy ────────────────────────────────────────────────

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

    // ── Logistics strategy ────────────────────────────────────────────────────

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

    // ── SetStrategy changes output ────────────────────────────────────────────

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

        Assert.DoesNotContain(ContextMenuActions.Delete, standardIds);
        Assert.Contains(ContextMenuActions.Delete,       adminIds);
    }

    [Fact]
    public void SetStrategy_UpdatesCurrentStrategyProperty()
    {
        var (logic, _) = CreateSut();
        logic.SetStrategy(MenuStrategy.Logistics);
        Assert.Equal(MenuStrategy.Logistics, logic.CurrentStrategy);
    }

    // ── JSON serialisation ────────────────────────────────────────────────────

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

    // ── OnActionInvoked / event ───────────────────────────────────────────────

    [Fact]
    public void OnActionInvoked_FiresActionInvokedEvent()
    {
        var (logic, _) = CreateSut();
        ContextActionInvoked? captured = null;
        logic.ActionInvoked += evt => captured = evt;

        var action = new ContextActionInvoked { ActionId = ContextMenuActions.CenterOnEntity, MapId = 3 };
        logic.OnActionInvoked(action);

        Assert.NotNull(captured);
        Assert.Equal(ContextMenuActions.CenterOnEntity, captured!.Value.ActionId);
        Assert.Equal(3, captured.Value.MapId);
    }

    [Fact]
    public void OnActionInvoked_NoSubscriber_DoesNotThrow()
    {
        var (logic, _) = CreateSut();
        var ex = Record.Exception(() =>
            logic.OnActionInvoked(new ContextActionInvoked { ActionId = ContextMenuActions.Properties }));

        Assert.Null(ex);
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static List<JObject> ParseMenuItems(string json)
        => JArray.Parse(json).Cast<JObject>().ToList();
}
