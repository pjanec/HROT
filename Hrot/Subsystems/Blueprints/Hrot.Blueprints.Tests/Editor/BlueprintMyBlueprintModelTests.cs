using System.Numerics;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.Windows;
using Hrot.Editor.AiShared;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// Behavioral tests for <see cref="BlueprintMyBlueprintModel"/> (AIE-047).
/// All tests are headless — no ImGui calls.
/// </summary>
public sealed class BlueprintMyBlueprintModelTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static BlueprintMyBlueprintModel MakeModel(BlueprintAsset? asset = null)
    {
        var model = new BlueprintMyBlueprintModel();
        if (asset != null)
        {
            var editable = new FakeEditableAsset(asset.AssetId, asset.Name);
            model.Retarget(editable, asset);
        }
        return model;
    }

    private static BlueprintAsset MakeAsset() =>
        new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "TestBP" };

    // ── AIE-047 SC1: fixed section order ─────────────────────────────────────

    [Fact]
    public void MyBlueprintModel_Sections_FixedOrder()
    {
        var model = MakeModel(MakeAsset());

        var sections = model.Sections;

        // BP-12c: the Event Dispatchers section is gone. Dispatchers were superseded by
        // PublishEvent/EventEntry (BP-09 deleted their node kinds); the section was display-only
        // over a field nothing consumes and no shipped asset populates.
        // BP-57: six — Local Variables appended, so the five above keep the D.6.2 sort order.
        Assert.Equal(6, sections.Count);

        // Fixed order: Graphs, Functions, Macros, Custom Events, Variables, Local Variables.
        Assert.Equal(BlueprintMyBlueprintModel.SectionGraphs,         sections[0].Id);
        Assert.Equal(BlueprintMyBlueprintModel.SectionFunctions,      sections[1].Id);
        Assert.Equal(BlueprintMyBlueprintModel.SectionMacros,         sections[2].Id);
        Assert.Equal(BlueprintMyBlueprintModel.SectionCustomEvents,   sections[3].Id);
        Assert.Equal(BlueprintMyBlueprintModel.SectionVariables,      sections[4].Id);
        Assert.Equal(BlueprintMyBlueprintModel.SectionLocalVariables, sections[5].Id);
        Assert.DoesNotContain(sections, s => s.Id == "dispatchers");

        // SortOrder must match position.
        for (int i = 0; i < sections.Count; i++)
            Assert.Equal(i, sections[i].SortOrder);
    }

    // ── AIE-047 SC2: variables projected with name/type/accent ───────────────

    [Fact]
    public void MyBlueprintModel_Variables_ProjectAssetVariables()
    {
        var asset = MakeAsset();
        asset.Variables.Add(new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = "Health",
            Type = new BlueprintTypeRef { TypeId = BlueprintTypeSystem.Single },
        });
        asset.Variables.Add(new VariableDecl
        {
            Id       = Guid.NewGuid(),
            Name     = "PlayerName",
            Type     = new BlueprintTypeRef { TypeId = BlueprintTypeSystem.String },
            Category = "Info",
        });

        var model = MakeModel(asset);
        var items = model.GetItems(BlueprintMyBlueprintModel.SectionVariables);

        Assert.Equal(2, items.Count);

        // First variable.
        Assert.Equal("Health",  items[0].DisplayName);
        Assert.NotNull(items[0].AccentColor);
        // Float accent must match the BlueprintTypeSystem palette for Single.
        var expectedFloatColor = BlueprintTypeSystem.GetAccentColorForTypeId(BlueprintTypeSystem.Single);
        Assert.Equal(expectedFloatColor, items[0].AccentColor);

        // Second variable.
        Assert.Equal("PlayerName", items[1].DisplayName);
        Assert.Equal("Info",       items[1].CategoryPath);
        var expectedStringColor = BlueprintTypeSystem.GetAccentColorForTypeId(BlueprintTypeSystem.String);
        Assert.Equal(expectedStringColor, items[1].AccentColor);
    }

    // ── AIE-047 SC3: graphs projected ────────────────────────────────────────

    [Fact]
    public void MyBlueprintModel_Graphs_ProjectAssetGraphs()
    {
        var asset = MakeAsset();
        asset.Graphs.Add(new Graph { Id = Guid.NewGuid(), Name = "EventGraph", Kind = GraphKind.Event });
        asset.Graphs.Add(new Graph { Id = Guid.NewGuid(), Name = "UpdateGraph", Kind = GraphKind.Function });

        var model = MakeModel(asset);

        // BP-24 split the graph rows Unreal-style: Function graphs live in the Functions
        // section (real since graphs became creatable), everything else stays under Graphs.
        var graphItems = model.GetItems(BlueprintMyBlueprintModel.SectionGraphs);
        Assert.Equal("EventGraph", Assert.Single(graphItems).DisplayName);

        var functionItems = model.GetItems(BlueprintMyBlueprintModel.SectionFunctions);
        Assert.Equal("UpdateGraph", Assert.Single(functionItems).DisplayName);

        // Graphs are host-defined, not user-deletable.
        Assert.True(graphItems[0].IsHostDefined);
        Assert.False(graphItems[0].IsDeletable);
    }

    // ── AIE-047 SC4: custom events projected ─────────────────────────────────

    [Fact]
    public void MyBlueprintModel_CustomEvents_Projected()
    {
        var asset = MakeAsset();
        asset.CustomEvents.Add(new CustomEventDecl { Id = Guid.NewGuid(), Name = "OnEnemyKilled" });
        asset.CustomEvents.Add(new CustomEventDecl { Id = Guid.NewGuid(), Name = "OnLevelUp" });

        var model = MakeModel(asset);

        var evtItems = model.GetItems(BlueprintMyBlueprintModel.SectionCustomEvents);

        Assert.Equal(2, evtItems.Count);
        Assert.Equal("OnEnemyKilled", evtItems[0].DisplayName);
        Assert.Equal("OnLevelUp",     evtItems[1].DisplayName);
    }

    /// <summary>
    /// BP-12c — a dispatcher declaration on a hand-authored asset must not resurrect the section.
    /// The field still round-trips; the panel simply no longer offers the abandoned concept.
    /// </summary>
    [Fact]
    public void MyBlueprintModel_Dispatchers_AreNotProjected()
    {
        var asset = MakeAsset();
        asset.EventDispatchers.Add(new EventDispatcherDecl { Id = Guid.NewGuid(), Name = "OnHealthChanged" });

        var model = MakeModel(asset);

        Assert.Empty(model.GetItems("dispatchers"));
        Assert.DoesNotContain(model.Sections, s => s.CreateCommandId == "editor.create-event-dispatcher");
    }

    // ── AIE-047 SC5: faked sections return empty, no throw ───────────────────

    [Fact]
    public void MyBlueprintModel_FakedSections_ReturnEmpty_NoThrow()
    {
        var asset = MakeAsset();
        var model = MakeModel(asset);

        // Functions and Macros are faked/empty in v1.
        var functions = model.GetItems(BlueprintMyBlueprintModel.SectionFunctions);
        var macros    = model.GetItems(BlueprintMyBlueprintModel.SectionMacros);

        Assert.Empty(functions);
        Assert.Empty(macros);
    }

    // ── AIE-047 SC6: Changed fires on asset mutation ──────────────────────────

    [Fact]
    public void MyBlueprintModel_FiresChanged_OnAssetMutation()
    {
        var asset    = MakeAsset();
        var editable = new FakeEditableAsset(asset.AssetId, asset.Name);
        var model    = new BlueprintMyBlueprintModel();
        model.Retarget(editable, asset);

        int changedCount = 0;
        model.Changed += () => changedCount++;

        // Simulate asset mutation by firing the editable asset's Changed event.
        editable.FireChanged();

        Assert.Equal(1, changedCount);
    }

    // ── Retarget on null clears and fires Changed ─────────────────────────────

    [Fact]
    public void MyBlueprintModel_Retarget_ToNull_FiresChangedAndReturnsEmpty()
    {
        var asset    = MakeAsset();
        var editable = new FakeEditableAsset(asset.AssetId, asset.Name);
        var model    = new BlueprintMyBlueprintModel();
        model.Retarget(editable, asset);

        int changedCount = 0;
        model.Changed += () => changedCount++;

        model.Retarget(null, null);

        Assert.Equal(1, changedCount);
        Assert.Empty(model.GetItems(BlueprintMyBlueprintModel.SectionVariables));
        Assert.Empty(model.GetItems(BlueprintMyBlueprintModel.SectionGraphs));
    }

    // ── Old asset's Changed no longer fires after Retarget ────────────────────

    [Fact]
    public void MyBlueprintModel_Retarget_UnsubscribesOldAsset()
    {
        var asset1    = MakeAsset();
        var editable1 = new FakeEditableAsset(asset1.AssetId, asset1.Name);
        var asset2    = MakeAsset();
        var editable2 = new FakeEditableAsset(asset2.AssetId, asset2.Name);

        var model = new BlueprintMyBlueprintModel();
        model.Retarget(editable1, asset1);

        // Retarget to asset2.
        model.Retarget(editable2, asset2);

        // Firing old asset should NOT call Changed.
        int changedCount = 0;
        model.Changed += () => changedCount++;

        editable1.FireChanged();

        Assert.Equal(0, changedCount);

        // New asset SHOULD.
        editable2.FireChanged();
        Assert.Equal(1, changedCount);
    }

    // ── Section count does not change after Retarget ──────────────────────────

    [Fact]
    public void MyBlueprintModel_Sections_SameInstanceAlways()
    {
        var model = new BlueprintMyBlueprintModel();

        var sectionsA = model.Sections;
        model.Retarget(new FakeEditableAsset(Guid.NewGuid(), "X"), MakeAsset());
        var sectionsB = model.Sections;

        Assert.Same(sectionsA, sectionsB);
        // BP-57: six. ⚠ Still ONE static list — the Local Variables section is graph-scoped in its
        // CONTENTS, not in its existence, so the descriptor list stays shared and constant.
        Assert.Equal(6, sectionsB.Count);
    }

    // ── Inner fake ────────────────────────────────────────────────────────────

    private sealed class FakeEditableAsset : Hrot.Editor.AiShared.IEditableAsset
    {
        public Guid   AssetId        { get; }
        public string Name           { get; }
        public Hrot.Editor.AiShared.AssetKind Kind => Hrot.Editor.AiShared.AssetKind.Blueprint;
        public string SourceFilePath => "";
        public bool   IsDirty        => false;
        public bool   IsEditorOwned  => false;

        public event System.Action? Changed;

        public FakeEditableAsset(Guid id, string name)
        {
            AssetId = id;
            Name    = name;
        }

        public void FireChanged() => Changed?.Invoke();
    }
}
