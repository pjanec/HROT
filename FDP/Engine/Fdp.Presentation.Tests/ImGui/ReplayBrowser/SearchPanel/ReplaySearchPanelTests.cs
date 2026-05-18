using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
// ISpatialPickerContext stub compiles, all methods callable without error,
// and TryConsumeBoundingBoxPick returns the stored box.
public class ISpatialPickerContextTests
{
    private sealed class StubSpatialPickerContext : ISpatialPickerContext
    {
        private readonly Dictionary<string, BoundingBox2D> _pending = new();
        public List<string> RequestedPaths { get; } = new();

        public void RequestBoundingBoxPick(string jsonPath)
            => RequestedPaths.Add(jsonPath);

        public bool TryConsumeBoundingBoxPick(string jsonPath, out BoundingBox2D box)
        {
            if (_pending.TryGetValue(jsonPath, out box))
            {
                _pending.Remove(jsonPath);
                return true;
            }
            return false;
        }

        public void SetPending(string jsonPath, BoundingBox2D box)
            => _pending[jsonPath] = box;
    }

    [Fact]
    public void SR_T32_StubSpatialPickerContext_AllMethodsInvocableWithoutError()
    {
        ISpatialPickerContext ctx = new StubSpatialPickerContext();
        ctx.RequestBoundingBoxPick("$.Bounds");
        ctx.TryConsumeBoundingBoxPick("$.Bounds", out _);
    }

    [Fact]
    public void SR_T32_StubSpatialPickerContext_TryConsume_ReturnsStoredBox()
    {
        var stub = new StubSpatialPickerContext();
        var expected = new BoundingBox2D
        {
            Min = new System.Numerics.Vector2(1f, 2f),
            Max = new System.Numerics.Vector2(10f, 20f)
        };
        stub.SetPending("$.Bounds", expected);

        bool result = stub.TryConsumeBoundingBoxPick("$.Bounds", out var actual);

        Assert.True(result);
        Assert.Equal(expected.Min, actual.Min);
        Assert.Equal(expected.Max, actual.Max);
    }

    [Fact]
    public void SR_T32_StubSpatialPickerContext_TryConsume_NoMatch_ReturnsFalse()
    {
        ISpatialPickerContext ctx = new StubSpatialPickerContext();
        bool result = ctx.TryConsumeBoundingBoxPick("$.Bounds", out var box);
        Assert.False(result);
        Assert.Equal(default(BoundingBox2D), box);
    }

    [Fact]
    public void SR_T32_StubSpatialPickerContext_RequestedPath_IsRecorded()
    {
        var stub = new StubSpatialPickerContext();
        stub.RequestBoundingBoxPick("$.Area");
        Assert.Contains("$.Area", stub.RequestedPaths);
    }
}

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
        public IReadOnlyList<SearchResultDto> ExecuteSearch(string fdpPath, SearchPredicateDto root)
            => Array.Empty<SearchResultDto>();
        public IReadOnlyList<LifecycleSearchResultDto> ExecuteLifecycleSearch(string fdpPath, LifecyclePredicateDto criteria)
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
