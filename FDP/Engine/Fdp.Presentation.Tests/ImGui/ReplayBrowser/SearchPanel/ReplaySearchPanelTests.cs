using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Fdp.Core;
using Fdp.Presentation.Editing;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Presentation.Panels.ReplayBrowser.Drawers;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.ReplayBrowser.Search;
using StructEdit.Core;
using Xunit;

namespace Fdp.Presentation.ReplayBrowser.SearchPanel;

// ── SR-T32 ────────────────────────────────────────────────────────────────
// ⛔ DELETED (CE-4206 R2): the stub tests of ISpatialPickerContext — the search area is edited through the ONE IMapPickContext
// (RequestAreaEdit / TryConsumeAreaEdit; rails in ComponentEditDrawerTests R2_* and MapPickBrokerTests).

// ── SR-T33 ────────────────────────────────────────────────────────────────
// BehaviorHashFieldDrawer: registering "Combat" in a test registry and asking
// TryGetId returns the expected int. Tests the underlying logic, not ImGui combo.
public class BehaviorHashFieldDrawerTests
{
    [Fact]
    public void SR_T33_BehaviorRegistry_TryGetId_ReturnsHashForCombat()
    {
        var registry = new BehaviorRegistry();
        const int combatId = 42;
        registry.Register(combatId, "Combat", new BehaviorDefinition { Name = "Combat" });

        bool found = registry.TryGetId("Combat", out int actualId);

        Assert.True(found);
        Assert.Equal(combatId, actualId);
    }

    [Fact]
    public void SR_T33_BehaviorHashFieldDrawer_TargetType_IsInt()
    {
        // [PARTIAL -- combo rendering requires ImGui context]
        // Structural: verify the drawer targets int.
        var registry = new BehaviorRegistry();
        registry.Register(1, "Alpha", new BehaviorDefinition { Name = "Alpha" });
        var drawer = new BehaviorHashFieldDrawer(registry);
        Assert.Equal(typeof(int), drawer.TargetType);
    }

    [Fact]
    public void SR_T33_BehaviorRegistry_GetRegisteredNames_ContainsCombat()
    {
        var registry = new BehaviorRegistry();
        registry.Register(99, "Combat", new BehaviorDefinition { Name = "Combat" });
        IReadOnlyList<string> names = registry.GetRegisteredNames();
        Assert.Contains("Combat", names);
    }
}

// ── FilteredTypeComboFieldDrawer filter logic ─────────────────────────────

public class FilteredTypeComboFieldDrawerTests
{
    private static readonly Type[] _testTypes =
    {
        typeof(System.String),   // "String"
        typeof(System.Boolean),  // "Boolean"
        typeof(System.Int32),    // "Int32"
    };

    [Fact]
    public void SR_T40_FilterTypes_EmptyFilter_ReturnsAll()
    {
        var result = FilteredTypeComboFieldDrawer.FilterTypes(_testTypes, "").ToList();
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void SR_T41_FilterTypes_NullFilter_ReturnsAll()
    {
        var result = FilteredTypeComboFieldDrawer.FilterTypes(_testTypes, null).ToList();
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void SR_T42_FilterTypes_MatchingFilter_ReturnsOnlyMatching()
    {
        // "bool" matches "Boolean" (OrdinalIgnoreCase)
        var result = FilteredTypeComboFieldDrawer.FilterTypes(_testTypes, "bool").ToList();
        Assert.Single(result);
        Assert.Equal(typeof(System.Boolean), result[0]);
    }

    [Fact]
    public void SR_T43_FilterTypes_NoMatch_ReturnsEmpty()
    {
        var result = FilteredTypeComboFieldDrawer.FilterTypes(_testTypes, "XYZ").ToList();
        Assert.Empty(result);
    }

    /// <summary>
    /// ⭐ CE-4220 (rules view R5b) — the rule editor's <c>Type</c> combo is the SAME list the Replay search shows: <c>Auto</c> offers the component types for a <c>ComponentType</c> field (exactly the Component mode's list)
    /// and the event types for an <c>EventType</c> field (exactly the Event mode's). ✅ Red-proof: Auto always picking the component list ⇒ the event field's list differs ⇒ red.
    /// </summary>
    [Fact]
    public void CE4220_Auto_OffersTheReplaySearchsOwnLists_ByTheFieldsName()
    {
        ComponentTypeRegistry.GetOrRegisterManaged(typeof(Fdp.Toolkit.ScenarioRules.ObjectiveStatus));   // both registries must hold something, or "the same list" proves nothing
        _ = EventType<Fdp.Toolkit.Combat.Contracts.HitEvent>.Id;
        Assert.NotEmpty(ComponentTypeRegistry.GetAllRegistered()); Assert.NotEmpty(Fdp.Core.EventType.GetAllRegistered());
        EditNode Field(string name) => new(new EditNodeId(1), name, "$." + name, EditNodeKind.Custom, typeof(Type));
        var auto = new FilteredTypeComboFieldDrawer(TypeComboMode.Auto);
        var component = new FilteredTypeComboFieldDrawer(TypeComboMode.Component);
        var @event = new FilteredTypeComboFieldDrawer(TypeComboMode.Event);
        Assert.Equal(component.TypesFor(Field("PositionComponentType")), auto.TypesFor(Field("PositionComponentType")));
        Assert.Equal(@event.TypesFor(Field("EventType")), auto.TypesFor(Field("EventType")));
        Assert.Equal(Fdp.Core.ComponentTypeRegistry.GetAllRegistered().OrderBy(t => t.Name), component.TypesFor(Field("ComponentType")));
        Assert.Equal(Fdp.Core.EventType.GetAllRegistered().OrderBy(t => t.Name), @event.TypesFor(Field("EventType")));
    }

    [Fact]
    public void SR_T44_FilteredTypeComboFieldDrawer_TargetType_IsTypeType()
    {
        // [PARTIAL -- combo rendering requires ImGui context]
        var drawer = new FilteredTypeComboFieldDrawer(TypeComboMode.Component);
        Assert.Equal(typeof(Type), drawer.TargetType);
    }
}

// ── SR-T39 ────────────────────────────────────────────────────────────────
// Panel decoupling: no field of forbidden types; seek/select intents fire correctly.
public class ReplaySearchPanelDecouplingTests
{
    // Minimal stubs to satisfy the constructor.
    private sealed class NopEditService : IComponentEditService
    {
        public IEditSession Open(object component, Type componentType, EditScope? scope = null, EditContext? context = null)
            => new NopSession();
    }

    private sealed class NopSession : IEditSession
    {
        public EditDocument Document => null!;
        public bool IsDirty => false;
        public EditRebuildState RebuildState => EditRebuildState.Stable;
        public void MarkStructuralChange() { }
        public void RebuildDocument() { }
        public ValidationResult Validate() => ValidationResult.Ok();
        public object Commit() => new object();
        public void Cancel() { }
        public void Dispose() { }
    }

    private sealed class NopSearchService : IRecordingSearchService
    {
        public IReadOnlyList<SearchResultDto> ExecuteSearch(string fdpPath, SearchPredicateDto root, TargetEntityFilter? entityFilter = null, CancellationToken ct = default)
            => Array.Empty<SearchResultDto>();
        public IReadOnlyList<LifecycleSearchResultDto> ExecuteLifecycleSearch(string fdpPath, LifecyclePredicateDto criteria, TargetEntityFilter? entityFilter = null, CancellationToken ct = default)
            => Array.Empty<LifecycleSearchResultDto>();
    }

    private static ReplaySearchPanel BuildPanel(
        out List<int> seekLog, out List<Entity> selectLog)
    {
        seekLog   = new List<int>();
        selectLog = new List<Entity>();

        var log1 = seekLog;
        var log2 = selectLog;

        return new ReplaySearchPanel(
            new NopEditService(),
            new NopSearchService(),
            f => log1.Add(f),
            e => log2.Add(e),
            (f, e) =>
            {
                log1.Add(f);
                log2.Add(e);
            });
    }

    [Fact]
    public void SR_T39_Panel_HasNoFieldOfForbiddenHistoryTypes()
    {
        // [STRICT] The panel must not reference PlaybackHistoryTracker or
        // EntitySelectionHistory directly -- all history wiring is the composition root's job.
        var fields = typeof(ReplaySearchPanel)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        foreach (var f in fields)
        {
            Assert.False(
                f.FieldType.Name == "PlaybackHistoryTracker",
                $"ReplaySearchPanel has forbidden field '{f.Name}' of type PlaybackHistoryTracker");
            Assert.False(
                f.FieldType.Name == "EntitySelectionHistory",
                $"ReplaySearchPanel has forbidden field '{f.Name}' of type EntitySelectionHistory");
        }
    }

    [Fact]
    public void SR_T39_InvokeSeekRequested_CallsDelegate_ExactlyOnce()
    {
        var panel = BuildPanel(out var seekLog, out _);

        panel.InvokeSeekRequested(42);

        Assert.Single(seekLog);
        Assert.Equal(42, seekLog[0]);
    }

    [Fact]
    public void SR_T39_InvokeEntitySelected_CallsDelegate_ExactlyOnce()
    {
        var panel = BuildPanel(out _, out var selectLog);
        var e = new Entity(7, 3);

        panel.InvokeEntitySelected(e);

        Assert.Single(selectLog);
        Assert.Equal(e, selectLog[0]);
    }

    [Fact]
    public void SR_T39_MultipleInvocations_AccumulateInLog()
    {
        var panel = BuildPanel(out var seekLog, out var selectLog);

        panel.InvokeSeekRequested(10);
        panel.InvokeSeekRequested(20);
        panel.InvokeEntitySelected(new Entity(1, 1));

        Assert.Equal(2, seekLog.Count);
        Assert.Single(selectLog);
    }
}

// ── RBF-P4T7: IsMergedViewActive property on ReplaySearchPanel ────────────

public class ReplaySearchPanelMergedViewTests
{
    private sealed class NopEditService : IComponentEditService
    {
        private sealed class NopSession : IEditSession
        {
            public EditDocument Document => null!;
            public bool IsDirty => false;
            public EditRebuildState RebuildState => EditRebuildState.Stable;
            public void MarkStructuralChange() { }
            public void RebuildDocument() { }
            public ValidationResult Validate() => ValidationResult.Ok();
            public object Commit() => new object();
            public void Cancel() { }
            public void Dispose() { }
        }

        public IEditSession Open(object component, Type componentType, EditScope? scope = null, EditContext? context = null)
            => new NopSession();
    }

    private sealed class NopSearchService : IRecordingSearchService
    {
        public IReadOnlyList<SearchResultDto> ExecuteSearch(string fdpPath, SearchPredicateDto root, TargetEntityFilter? entityFilter = null, CancellationToken ct = default)
            => Array.Empty<SearchResultDto>();
        public IReadOnlyList<LifecycleSearchResultDto> ExecuteLifecycleSearch(string fdpPath, LifecyclePredicateDto criteria, TargetEntityFilter? entityFilter = null, CancellationToken ct = default)
            => Array.Empty<LifecycleSearchResultDto>();
    }

    private static ReplaySearchPanel MakeSearchPanel()
        => new ReplaySearchPanel(
            new NopEditService(),
            new NopSearchService(),
            _ => { }, _ => { }, (_, _) => { });

    [Fact]
    public void RBF_P4T7_IsMergedViewActive_DefaultsFalse()
    {
        var panel = MakeSearchPanel();
        Assert.False(panel.IsMergedViewActive);
    }

    [Fact]
    public void RBF_P4T7_SetMergedViewActive_True_PropertyReflectsChange()
    {
        var panel = MakeSearchPanel();
        panel.IsMergedViewActive = true;
        Assert.True(panel.IsMergedViewActive);
    }

    [Fact]
    public void RBF_P4T7_SetMergedViewActive_False_PropertyReflectsChange()
    {
        var panel = MakeSearchPanel();
        panel.IsMergedViewActive = true;
        panel.IsMergedViewActive = false;
        Assert.False(panel.IsMergedViewActive);
    }
}
