# BATCH-06 — Search UI: Custom Drawers, ReplaySearchPanel, Global Registration

**Tasks covered**: RB-4.8, RB-4.9, RB-4.10, RB-4.11, RB-5.1  
**Tests to add**: SR-T28, SR-T29, SR-T32, SR-T33, SR-T39, FND-T11  
**Blocking**: All SR-T01..SR-T38 are green (BATCH-05+05C committed). This batch is the final code batch.

**Reference documents** (read before implementing):
- DESIGN.md §6.5 (StructEdit plumbing), §6.7 (search panel wireframes)
- TASK-DETAILS.md RB-4.8, RB-4.9, RB-4.10, RB-4.11, RB-5.1

---

## Repository Layout Reminder

- FDP is a **git submodule** at `d:\Work\IOS-IG-SimHost-FDP-2\FDP\`
- `Hrot/` lives in the parent repo
- Both need separate commits (FDP first, then parent)

## Pre-existing Errors (Do NOT fix)

`Hrot.SimHost.Tests` has 2 pre-existing errors (`AreaQueryBatchData`, `EqsTargetPool`) — ignore them.

---

## Task A — `ISpatialPickerContext` (new interface)

**File**: `FDP/Engine/Fdp.Presentation/ImGui/Editing/ISpatialPickerContext.cs`

Pattern: mirrors `IComponentPickerContext.cs` in the same directory.

```csharp
using Fdp.Toolkit.ReplayBrowser.Search;

namespace Fdp.Presentation.Editing
{
    /// <summary>
    /// Brokers async spatial area pick requests between the search panel
    /// and the map's bounding-box gizmo. Keys are the node's stable JsonPath.
    /// </summary>
    public interface ISpatialPickerContext
    {
        /// <summary>Initiates a bounding-box pick for the field at <paramref name="jsonPath"/>.</summary>
        void RequestBoundingBoxPick(string jsonPath);

        /// <summary>
        /// Attempts to consume a completed bounding-box pick.
        /// Returns <see langword="true"/> and sets <paramref name="box"/> when a result is available.
        /// </summary>
        bool TryConsumeBoundingBoxPick(string jsonPath, out BoundingBox2D box);
    }
}
```

---

## Task B — Update `ComponentEditDrawer` to handle bounding-box picker

**File**: `FDP/Engine/Fdp.Presentation/ImGui/Editing/ComponentEditDrawer.cs`

**Changes** (minimal diff):

1. Add `_spatialPickerCtx` field and extend the constructor:
   ```csharp
   private readonly ISpatialPickerContext? _spatialPickerCtx;

   internal ComponentEditDrawer(
       IEditSession session,
       IComponentPickerContext? pickerCtx,
       IReadOnlyDictionary<Type, IImGuiFieldDrawer>? customDrawers = null,
       ISpatialPickerContext? spatialPickerCtx = null)
   {
       _session            = session;
       _pickerCtx          = pickerCtx;
       _customDrawers      = customDrawers ?? new Dictionary<Type, IImGuiFieldDrawer>();
       _spatialPickerCtx   = spatialPickerCtx;
   }
   ```
   The existing `ComponentEditWindow` call `new ComponentEditDrawer(session, pickerCtx, customDrawers)` still compiles — the new parameter is optional.

2. In `DrawLeafNode`, after the world-location picker block and before `ImGuiApi.PopID()`, add:
   ```csharp
   // Picker: bounding box area (spatial search).
   var bboxAttr = node.Metadata.CustomAttributes
       .OfType<MapPickableBoundingBoxAttribute>().FirstOrDefault();
   if (bboxAttr != null && _spatialPickerCtx != null)
   {
       ImGuiApi.SameLine();
       if (ImGuiApi.Button($"Pick Area##{node.Id.Value}"))
           _spatialPickerCtx.RequestBoundingBoxPick(node.JsonPath);

       if (_spatialPickerCtx.TryConsumeBoundingBoxPick(node.JsonPath, out var pickedBox))
       {
           node.Binding?.SetBoxed(pickedBox);
           changed = true;
       }
   }
   ```

3. Add the required `using` at the top:
   ```csharp
   using Fdp.Toolkit.ReplayBrowser.Search;
   ```
   (Already has `using System.Linq;` for the `.OfType<>()` calls on entity/location pickers.)

---

## Task C — `BoundingBoxFieldDrawer` (new custom drawer)

**File**: `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/Drawers/BoundingBoxFieldDrawer.cs`

Renders two `DragFloat2` rows for `Min` and `Max`.

```csharp
using System;
using System.Numerics;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.ReplayBrowser.Search;
using ImGuiNET;
using StructEdit.Core;

namespace Fdp.Presentation.Panels.ReplayBrowser.Drawers;

using ImGuiApi = ImGuiNET.ImGui;

/// <summary>
/// Custom <see cref="IImGuiFieldDrawer"/> for <see cref="BoundingBox2D"/> fields.
/// Renders two DragFloat2 controls for Min and Max. The "Pick Area" button is
/// handled by <see cref="ComponentEditDrawer"/> via <see cref="ISpatialPickerContext"/>
/// when the field carries <see cref="MapPickableBoundingBoxAttribute"/>.
/// </summary>
internal sealed class BoundingBoxFieldDrawer : IImGuiFieldDrawer
{
    public Type TargetType => typeof(BoundingBox2D);

    public bool DrawInput(ref object value, EditNodeMetadata meta)
    {
        var box  = value is BoundingBox2D b ? b : default;
        var min  = box.Min;
        var max  = box.Max;
        bool changed = false;

        ImGuiApi.SetNextItemWidth(-float.Epsilon);
        if (ImGuiApi.DragFloat2("Min##bbox", ref min, 0.5f))
        {
            box     = new BoundingBox2D { Min = min, Max = box.Max };
            value   = box;
            changed = true;
        }

        ImGuiApi.SetNextItemWidth(-float.Epsilon);
        if (ImGuiApi.DragFloat2("Max##bbox", ref max, 0.5f))
        {
            box     = new BoundingBox2D { Min = box.Min, Max = max };
            value   = box;
            changed = true;
        }

        return changed;
    }
}
```

---

## Task D — `BehaviorHashFieldDrawer` (new custom drawer)

**File**: `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/Drawers/BehaviorHashFieldDrawer.cs`

Handles `typeof(int)` fields that carry `[BehaviorHashPickerAttribute]`. Falls back to `ImGuiApi.InputInt` when the attribute is absent.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.ReplayBrowser.Search;
using ImGuiNET;
using StructEdit.Core;

namespace Fdp.Presentation.Panels.ReplayBrowser.Drawers;

using ImGuiApi = ImGuiNET.ImGui;

/// <summary>
/// Custom <see cref="IImGuiFieldDrawer"/> for <c>int</c> fields decorated with
/// <see cref="BehaviorHashPickerAttribute"/>. Shows a filterable combo of registered
/// behavior names and maps the selection back to its stable integer ID.
/// Falls back to <c>InputInt</c> for int fields without the attribute.
/// </summary>
internal sealed class BehaviorHashFieldDrawer : IImGuiFieldDrawer
{
    private readonly BehaviorRegistry _registry;
    private IReadOnlyList<string>? _cachedNames;
    private string _filter = string.Empty;

    public BehaviorHashFieldDrawer(BehaviorRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public Type TargetType => typeof(int);

    public bool DrawInput(ref object value, EditNodeMetadata meta)
    {
        bool hasPicker = meta.CustomAttributes.Any(a => a is BehaviorHashPickerAttribute);
        if (!hasPicker)
        {
            int v = value is int i ? i : 0;
            bool ok = ImGuiApi.InputInt("##bhv", ref v);
            if (ok) value = v;
            return ok;
        }

        _cachedNames ??= _registry.GetRegisteredNames();
        int current = value is int hash ? hash : 0;

        // Find display name for the current hash.
        string currentName = _cachedNames.FirstOrDefault(
            n => _registry.TryGetId(n, out int id) && id == current) ?? current.ToString();

        bool changed = false;
        if (ImGuiApi.BeginCombo("##bhvcombo", currentName))
        {
            ImGuiApi.InputTextWithHint("##bhvfilter", "Filter...", ref _filter, 128);

            foreach (var name in _cachedNames)
            {
                if (_filter.Length > 0 &&
                    name.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                bool selected = string.Equals(name, currentName, StringComparison.Ordinal);
                if (selected)
                    ImGuiApi.SetItemDefaultFocus();

                if (ImGuiApi.Selectable(name, selected))
                {
                    if (_registry.TryGetId(name, out int newId))
                    {
                        value   = newId;
                        changed = true;
                    }
                }
            }
            ImGuiApi.EndCombo();
        }
        return changed;
    }
}
```

---

## Task E — `FilteredTypeComboFieldDrawer` (new custom drawer)

**File**: `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/Drawers/FilteredTypeComboFieldDrawer.cs`

Handles `typeof(Type)` fields. Uses either `ComponentTypeRegistry.GetAllRegistered()` or `EventType.GetAllRegistered()` depending on `Mode`. Has an embedded text filter with OrdinalIgnoreCase matching.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Presentation.Editing;
using ImGuiNET;
using StructEdit.Core;

namespace Fdp.Presentation.Panels.ReplayBrowser.Drawers;

using ImGuiApi = ImGuiNET.ImGui;

/// <summary>
/// Modes that control which type list populates the combo.
/// </summary>
internal enum TypeComboMode { Component, Event }

/// <summary>
/// Custom <see cref="IImGuiFieldDrawer"/> for <c>Type</c> fields.
/// Shows a filterable combo of component or event types.
/// </summary>
internal sealed class FilteredTypeComboFieldDrawer : IImGuiFieldDrawer
{
    private readonly TypeComboMode _mode;
    private IReadOnlyList<Type>? _cachedTypes;
    private string _filter = string.Empty;

    public FilteredTypeComboFieldDrawer(TypeComboMode mode)
    {
        _mode = mode;
    }

    public Type TargetType => typeof(Type);

    /// <summary>
    /// Filters <paramref name="types"/> by name containing <paramref name="filter"/>
    /// (OrdinalIgnoreCase). Returns all when filter is empty or null.
    /// Exposed internal for unit testing.
    /// </summary>
    internal static IEnumerable<Type> FilterTypes(IEnumerable<Type> types, string? filter)
    {
        if (string.IsNullOrEmpty(filter))
            return types;
        return types.Where(
            t => t.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    public bool DrawInput(ref object value, EditNodeMetadata meta)
    {
        _cachedTypes ??= LoadTypes();

        Type? current = value as Type;
        string currentName = current?.Name ?? "(none)";
        bool changed = false;

        if (ImGuiApi.BeginCombo("##typecombo", currentName))
        {
            ImGuiApi.InputTextWithHint("##typefilter", "Filter...", ref _filter, 128);

            foreach (var t in FilterTypes(_cachedTypes, _filter))
            {
                bool selected = t == current;
                if (selected)
                    ImGuiApi.SetItemDefaultFocus();

                if (ImGuiApi.Selectable(t.Name, selected))
                {
                    value   = t;
                    changed = true;
                }
            }
            ImGuiApi.EndCombo();
        }
        return changed;
    }

    private IReadOnlyList<Type> LoadTypes()
    {
        return _mode == TypeComboMode.Event
            ? EventType.GetAllRegistered().ToList()
            : ComponentTypeRegistry.GetAllRegistered().ToList();
    }
}
```

---

## Task F — Full `ReplaySearchPanel` Implementation (replace stub)

**File**: `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ReplaySearchPanel.cs`

Replace the current stub entirely.

### Constructor contract (from DESIGN.md §6.7.3)

```csharp
public sealed class ReplaySearchPanel
{
    public ReplaySearchPanel(
        IComponentEditService editService,
        IRecordingSearchService searchService,
        Action<int> onSeekRequested,
        Action<Entity> onEntitySelected)
```

**The panel must NOT reference `PlaybackHistoryTracker` or `EntitySelectionHistory`** (SR-T39 asserts this via reflection). It invokes the raw delegates directly on button clicks.

### Mode enum and DTO management

```csharp
private enum SearchMode { Component, Event, Lifecycle, Spatial, Structural, Compound }
```

Per mode, the panel holds one DTO instance:
- `Component` → `PropertyMatchDto`
- `Event` → `TransientEventPredicateDto`
- `Lifecycle` → `LifecyclePredicateDto`
- `Spatial` → `SpatialBoundingPredicateDto`
- `Structural` → `StructuralPredicateDto`
- `Compound` → `CompoundPredicateDto`

When the mode changes (radio button click), rebuild `_predicateSession`:
```csharp
_predicateSession?.Dispose();
_predicateSession = _editService.Open(DtoForMode(_mode), DtoForMode(_mode).GetType());
_componentEditDrawer = BuildDrawer(_predicateSession);
```

### StructEdit session (RB-4.8)

```csharp
private IEditSession? _predicateSession;
private ComponentEditDrawer? _componentEditDrawer;
private ISpatialPickerContext? _spatialPickerCtx; // set externally by subsystem if needed
```

On each draw frame:
```csharp
if (_predicateSession?.RebuildState == EditRebuildState.RebuildRequired)
    _predicateSession.RebuildDocument();
```

Save Preset button:
```csharp
string json = _predicateSession.ToJson();   // StructEdit.Json extension
// write to file via IFileDialogService (optional — can stub as TODO if IFileDialogService
// is not wired; the test SR-T28 only tests the JSON round-trip, not the file dialog)
```

Load Preset button:
```csharp
_predicateSession.LoadJson(json);
_predicateSession.MarkStructuralChange();
_predicateSession.RebuildDocument();
```

### Custom drawers for ComponentEditDrawer

```csharp
private ComponentEditDrawer BuildDrawer(IEditSession session)
{
    var registry = new BehaviorRegistry(); // empty in replay context; real instance injected by subsystem
    var drawers = new Dictionary<Type, IImGuiFieldDrawer>
    {
        [typeof(BoundingBox2D)]  = new BoundingBoxFieldDrawer(),
        [typeof(int)]            = new BehaviorHashFieldDrawer(registry),
        [typeof(Type)]           = new FilteredTypeComboFieldDrawer(TypeComboMode.Component),
    };
    return new ComponentEditDrawer(session, pickerCtx: null, drawers, _spatialPickerCtx);
}
```

Note: `BehaviorRegistry` is empty by default in the replay browser context. The subsystem can inject a real registry if needed, but SR-T33 verifies the hash lookup independently.

### Draw loop

```csharp
public void DrawContent()
{
    // 1. Mode radio bar
    DrawModeRadio();

    // 2. Save/Load Preset toolbar
    DrawPresetToolbar();

    // 3. StructEdit criteria via ComponentEditDrawer
    if (_predicateSession != null && _componentEditDrawer != null)
    {
        if (ImGuiApi.BeginTable("SearchCriteria", 2, ...))
        {
            _componentEditDrawer.DrawEditNode(_predicateSession.Document.Root);
            ImGuiApi.EndTable();
        }
    }

    // 4. Execute Search button + status
    DrawExecuteButton();

    // 5. Results grid
    DrawResultsGrid();
}
```

### Results grid

For lifecycle mode, use 4-column table: `Frame`, `Entity`, `End Frame`, `Context`.  
For all other modes, use 3-column table: `Frame`, `Entity`, `Event Type / Context`.

Frame cell:
```csharp
if (ImGuiApi.SmallButton($"Frame {r.FrameIndex}##seek{i}"))
    _onSeekRequested(r.FrameIndex);
```

Entity cell:
```csharp
if (ImGuiEntityLink.Draw(r.Entity))
    _onEntitySelected(r.Entity);
```

### Background search

```csharp
private Task? _searchTask;
private IReadOnlyList<SearchResultDto> _results = Array.Empty<SearchResultDto>();
private IReadOnlyList<LifecycleSearchResultDto> _lifecycleResults = ...;
private string _statusLine = string.Empty;

// On Execute Search button click:
if (_mode == SearchMode.Lifecycle)
{
    string path = _context?.CurrentFilePath ?? string.Empty;
    var pred = (LifecyclePredicateDto)DtoForMode(SearchMode.Lifecycle);
    _searchTask = Task.Run(() =>
    {
        var r = _searchService.ExecuteLifecycleSearch(path, pred);
        // post to main thread (simple lock or Interlocked.Exchange)
    });
}
else { /* similar for ExecuteSearch */ }
```

For simplicity: `_searchTask = null` if no recording path is set. Show `"No recording loaded."` status.

The `ReplaySearchPanel` needs access to the current recording path. The simplest approach: accept a `Func<string?>` delegate `getFilePath` in the constructor, or expose a `CurrentFilePath` property set by the subsystem after `LoadRecording`. Either approach is fine — choose whichever compiles cleanly.

**Preferred**: Add a mutable `public string? CurrentFilePath { get; set; }` property on the panel. The subsystem sets it after `_context.LoadRecording(path)`.

### Test seams (required for SR-T39)

Expose these `internal` methods so tests can simulate button clicks without ImGui:

```csharp
/// <summary>Simulates clicking a seek (Frame N) button. For unit testing only.</summary>
internal void InvokeSeekRequested(int frameIndex) => _onSeekRequested(frameIndex);

/// <summary>Simulates clicking an entity deep-link. For unit testing only.</summary>
internal void InvokeEntitySelected(Entity entity) => _onEntitySelected(entity);
```

---

## Task G — Update `ReplayBrowserSubsystem.WireDelegates`

**File**: `Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs`

The `ReplaySearchPanel` constructor changed to accept `(editService, searchService, seekIntent, selectIntent)`. Update `WireDelegates` (and the corresponding test seam `WireDelegatesForTest`):

```csharp
private void WireDelegates()
{
    var (seekIntent, selectIntent) = WireDelegatesForTest(
        _entityHistory, _playbackHistory, _inspectorState!, _context, _diffPanel!, _eventPanel!);

    _inspectorPanel!.OnEntitySelected = selectIntent;
    _inspectorPanel.ChainToMap = true;

    // Build search services.
    var editSvc = new ComponentEditServiceBuilder().Build();
    var predicateCompiler = new PredicateCompiler(editSvc);
    var eventScannerCompiler = new EventScannerCompiler(editSvc);
    var searchSvc = new RecordingSearchService(predicateCompiler, eventScannerCompiler);

    _searchPanel = new ReplaySearchPanel(editSvc, searchSvc, seekIntent, selectIntent);
}
```

Add the required `using` directives:
```csharp
using Fdp.Toolkit.ReplayBrowser.Search;
using StructEdit.Reflection;
```

(`ComponentEditServiceBuilder` is in `StructEdit.Reflection`.)

**Note**: If the subsystem's `.csproj` does not reference `StructEdit.Reflection` directly, add it. Check `Hrot.ReplayBrowser.csproj`. If `Fdp.Presentation` already transitively provides it, no `.csproj` change is needed — verify with a build.

---

## Task H — RB-5.1: Add `Hrot.ReplayBrowser` to `Hrot.ClusterRunner.csproj`

**File**: `Hrot/Runner/Hrot.ClusterRunner/Hrot.ClusterRunner.csproj`

In the `<ItemGroup>` that lists subsystem libraries, add:
```xml
<ProjectReference Include="..\..\Subsystems\Hrot.ReplayBrowser\Hrot.ReplayBrowser.csproj" />
```

After the build, `ScanForSubsystems()` will discover `ReplayBrowserSubsystem` automatically because `Hrot.ReplayBrowser.dll` is now loaded into the AppDomain.

---

## Task I — Tests

### SR-T28 + SR-T29: Preset round-trip and session rebuild  
**File**: `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Search/PresetRoundTripTests.cs`

```csharp
using System.Collections.Generic;
using System.IO;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Search;
using StructEdit.Core;
using StructEdit.Json;
using StructEdit.Reflection;
using Xunit;

namespace Fdp.Toolkits.Tests.ReplayBrowser.Search;

public class PresetRoundTripTests
{
    private static IComponentEditService BuildEditService() =>
        new ComponentEditServiceBuilder().Build();

    // ── SR-T28 ────────────────────────────────────────────────────────────
    // 3-level nested compound: ToJson → LoadJson → Commit → compare fields.
    // Does not require a .fdp fixture: verifies that the round-tripped DTO
    // has the same logical shape as the original (Operator, Conditions count).
    [Fact]
    public void SR_T28_PresetRoundTrip_ThreeLevelNested_ReconstructsEquivalentDto()
    {
        // Build a 3-level compound: Compound(And) [ Compound(Or) [ Numeric, String ] ]
        var inner = new CompoundPredicateDto
        {
            Operator = LogicalOperator.Or,
            Conditions = new List<SearchPredicateDto>
            {
                new NumericPredicateDto { MinValue = 1.0, MaxValue = 99.0 },
                new StringPredicateDto  { Substring = "combat" }
            }
        };
        var root = new CompoundPredicateDto
        {
            Operator   = LogicalOperator.And,
            Conditions = new List<SearchPredicateDto> { inner }
        };

        var editSvc = BuildEditService();
        string json;
        using (var session = editSvc.Open(root, typeof(CompoundPredicateDto)))
            json = session.ToJson();

        // Reload into a fresh DTO instance.
        var fresh = new CompoundPredicateDto();
        using var reloadSession = editSvc.Open(fresh, typeof(CompoundPredicateDto));
        reloadSession.LoadJson(json);
        reloadSession.MarkStructuralChange();
        reloadSession.RebuildDocument();
        var reloaded = (CompoundPredicateDto)reloadSession.Commit();

        // Verify structural equivalence.
        Assert.Equal(LogicalOperator.And, reloaded.Operator);
        Assert.Single(reloaded.Conditions);
        var reloadedInner = Assert.IsType<CompoundPredicateDto>(reloaded.Conditions[0]);
        Assert.Equal(LogicalOperator.Or, reloadedInner.Operator);
        Assert.Equal(2, reloadedInner.Conditions.Count);
        Assert.IsType<NumericPredicateDto>(reloadedInner.Conditions[0]);
        Assert.IsType<StringPredicateDto>(reloadedInner.Conditions[1]);
        var reloadedNum = (NumericPredicateDto)reloadedInner.Conditions[0];
        Assert.Equal(1.0, reloadedNum.MinValue);
        Assert.Equal(99.0, reloadedNum.MaxValue);
        var reloadedStr = (StringPredicateDto)reloadedInner.Conditions[1];
        Assert.Equal("combat", reloadedStr.Substring);
    }

    // ── SR-T29 ────────────────────────────────────────────────────────────
    // Resizing Conditions List causes RebuildRequired; after RebuildDocument
    // the new child is present and state returns to Stable.
    [Fact]
    public void SR_T29_ResizeConditions_SetsRebuildRequired_ThenStableAfterRebuild()
    {
        var dto = new CompoundPredicateDto
        {
            Conditions = new List<SearchPredicateDto>
            {
                new NumericPredicateDto { MinValue = 0, MaxValue = 10 },
                new StringPredicateDto  { Substring = "x" }
            }
        };
        var editSvc = BuildEditService();
        using var session = editSvc.Open(dto, typeof(CompoundPredicateDto));

        Assert.Equal(EditRebuildState.Stable, session.RebuildState);

        // Find the Conditions node (DynamicArray) in the document.
        EditNode? conditionsNode = FindNode(session.Document.Root, "Conditions");
        Assert.NotNull(conditionsNode);
        var containerBinding = Assert.IsAssignableFrom<IContainerBinding>(conditionsNode!.Binding);
        Assert.Equal(2, containerBinding.Count);

        // Add one element — caller must manually mark structural change.
        containerBinding.Resize(3);
        session.MarkStructuralChange();

        Assert.Equal(EditRebuildState.RebuildRequired, session.RebuildState);

        session.RebuildDocument();

        Assert.Equal(EditRebuildState.Stable, session.RebuildState);

        // After rebuild, the document must reflect 3 conditions.
        conditionsNode = FindNode(session.Document.Root, "Conditions");
        Assert.NotNull(conditionsNode);
        var cb2 = Assert.IsAssignableFrom<IContainerBinding>(conditionsNode!.Binding);
        Assert.Equal(3, cb2.Count);
    }

    // Depth-first search for a node by name.
    private static EditNode? FindNode(EditNode root, string name)
    {
        if (root.Name == name) return root;
        foreach (var child in root.Children)
        {
            var found = FindNode(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
```

### SR-T32, SR-T33, SR-T39: Presentation-layer tests  
**File**: `FDP/Engine/Fdp.Presentation.Tests/ImGui/ReplayBrowser/SearchPanel/ReplaySearchPanelTests.cs`

```csharp
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
        registry.Register(combatId, "Combat", new BehaviorDefinition(null));

        bool found = registry.TryGetId("Combat", out int actualId);

        Assert.True(found);
        Assert.Equal(combatId, actualId);
    }

    [Fact]
    public void SR_T33_BehaviorHashFieldDrawer_TargetType_IsInt()
    {
        // [PARTIAL — combo rendering requires ImGui context]
        // Structural: verify the drawer targets int.
        var registry = new BehaviorRegistry();
        registry.Register(1, "Alpha", new BehaviorDefinition(null));
        var drawer = new BehaviorHashFieldDrawer(registry);
        Assert.Equal(typeof(int), drawer.TargetType);
    }

    [Fact]
    public void SR_T33_BehaviorRegistry_GetRegisteredNames_ContainsCombat()
    {
        var registry = new BehaviorRegistry();
        registry.Register(99, "Combat", new BehaviorDefinition(null));
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
    public void FilterTypes_EmptyFilter_ReturnsAll()
    {
        var result = FilteredTypeComboFieldDrawer.FilterTypes(_testTypes, "").ToList();
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void FilterTypes_NullFilter_ReturnsAll()
    {
        var result = FilteredTypeComboFieldDrawer.FilterTypes(_testTypes, null).ToList();
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void FilterTypes_MatchingFilter_ReturnsOnlyMatching()
    {
        // "bool" matches "Boolean" (OrdinalIgnoreCase)
        var result = FilteredTypeComboFieldDrawer.FilterTypes(_testTypes, "bool").ToList();
        Assert.Single(result);
        Assert.Equal(typeof(System.Boolean), result[0]);
    }

    [Fact]
    public void FilterTypes_NoMatch_ReturnsEmpty()
    {
        var result = FilteredTypeComboFieldDrawer.FilterTypes(_testTypes, "XYZ").ToList();
        Assert.Empty(result);
    }

    [Fact]
    public void FilteredTypeComboFieldDrawer_TargetType_IsTypeType()
    {
        // [PARTIAL — combo rendering requires ImGui context]
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
            e => log2.Add(e));
    }

    [Fact]
    public void SR_T39_Panel_HasNoFieldOfForbiddenHistoryTypes()
    {
        // [STRICT] The panel must not reference PlaybackHistoryTracker or
        // EntitySelectionHistory directly — all history wiring is the composition root's job.
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
```

### FND-T11: ScanForSubsystems discovers ReplayBrowserSubsystem  
**File**: `Hrot/Runner/Hrot.ClusterRunner.Tests/ReplayBrowserSubsystemDiscoveryTests.cs`

```csharp
using System;
using System.Linq;
using System.Reflection;
using Hrot.ReplayBrowser;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// FND-T11: Verifies that ReplayBrowserSubsystem is discoverable by ScanForSubsystems.
/// </summary>
public class ReplayBrowserSubsystemDiscoveryTests
{
    [Fact]
    public void FND_T11_ReplayBrowserSubsystem_ImplementsISubsystem()
    {
        // Ensure the type exists and implements ISubsystem.
        var t = typeof(ReplayBrowserSubsystem);
        Assert.True(typeof(ISubsystem).IsAssignableFrom(t));
        Assert.False(t.IsAbstract);
    }

    [Fact]
    public void FND_T11_ReplayBrowserSubsystem_HasINetworkFactoryCtor()
    {
        // ScanForSubsystems creates subsystems via ctor(INetworkFactory).
        var t = typeof(ReplayBrowserSubsystem);
        var ctor = t.GetConstructor(new[] { typeof(INetworkFactory) });
        Assert.NotNull(ctor);
    }

    [Fact]
    public void FND_T11_ReplayBrowserSubsystem_CLIName_IsReplayBrowser()
    {
        // ScanForSubsystems strips "Subsystem" suffix to derive the CLI name.
        string typeName = typeof(ReplayBrowserSubsystem).Name;
        const string suffix = "Subsystem";
        string cliName = typeName.EndsWith(suffix)
            ? typeName[..^suffix.Length]
            : typeName;

        Assert.Equal("ReplayBrowser", cliName);
    }

    [Fact]
    public void FND_T11_ReplayBrowserSubsystem_Assembly_IsLoadedInAppDomain()
    {
        // When Hrot.ReplayBrowser.csproj is referenced by Hrot.ClusterRunner, its
        // assembly is loaded into the AppDomain and ScanForSubsystems can find it.
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        bool found = assemblies.Any(a => a.GetName().Name == "Hrot.ReplayBrowser");
        Assert.True(found, "Hrot.ReplayBrowser assembly must be loaded in the AppDomain.");
    }
}
```

---

## Build and Test Verification

After implementing all tasks, run:

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2\FDP
dotnet test FDP.sln --no-build -l "console;verbosity=normal" 2>&1 | Select-String "passed|failed|error"
```

Then run the Hrot tests:
```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet test Hrot\Runner\Hrot.ClusterRunner.Tests\Hrot.ClusterRunner.Tests.csproj
```

**Expected**: All previously passing tests (113) still pass; new tests SR-T28, SR-T29, SR-T32 (×4), SR-T33 (×3), FilteredTypeCombo (×5), SR-T39 (×4), FND-T11 (×4) also pass.

---

## Commit Order

1. Commit FDP submodule (all `FDP/` changes):
   ```powershell
   cd d:\Work\IOS-IG-SimHost-FDP-2\FDP
   git add -A
   git commit -m "feat(search-ui): Add custom drawers, ISpatialPickerContext, and full ReplaySearchPanel (RB-4.8..4.10)"
   ```

2. Commit parent repo (`Hrot/` changes + FDP pointer update):
   ```powershell
   cd d:\Work\IOS-IG-SimHost-FDP-2
   git add FDP Hrot
   git commit -m "feat(replay-browser): Wire ReplaySearchPanel into subsystem; add Hrot.ReplayBrowser to ClusterRunner (RB-5.1)"
   ```

---

## Implementation Notes

### `BoundingBox2D` struct initialization
`BoundingBox2D` is a `struct` in `Fdp.Toolkit.ReplayBrowser.Search`. Use:
```csharp
new BoundingBox2D { Min = new Vector2(x, y), Max = new Vector2(w, h) }
```

### `BehaviorDefinition` constructor
From `BehaviorRegistry.Register(int id, string name, BehaviorDefinition definition)`, `BehaviorDefinition` wraps a params-DTO type. Use `new BehaviorDefinition(null)` for registry tests that only need the name lookup (no params DTO).  
**Verify the actual constructor signature** by reading `FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorRegistry.cs` — adjust if needed.

### `EditScope.WholeComponent` vs default
`IComponentEditService.Open` accepts `EditScope? scope = null` where null defaults to `WholeComponent`. No need to pass `EditScope.WholeComponent` explicitly unless desired for clarity.

### `ReplaySearchPanel` uses `StructEdit.Json` extension methods
`IEditSession.ToJson()` and `LoadJson()` are extension methods from `StructEdit.Json`. Since `Fdp.Presentation` does NOT reference `StructEdit.Json` directly, you must **either**:
1. Add `StructEdit.Json` reference to `Fdp.Presentation.csproj`, OR
2. Implement the preset serialization inline using `System.Text.Json.JsonSerializer` on the DTO directly (skipping the StructEdit session round-trip for save/load)

**Preferred**: Option 2 — serialize the current DTO via `System.Text.Json.JsonSerializer.Serialize(dto)` and deserialize back. This avoids adding a new project reference and is functionally equivalent because `SearchPredicateDto` is already `[JsonPolymorphic]`.

For SR-T28, `Fdp.Toolkits.Tests` already has the `StructEdit.Json` reference and the test opens sessions directly — no change needed to test project references.

### `NopEditService` in SR-T39 tests
The `ReplaySearchPanel` constructor calls `editService.Open(...)` to create the initial session. The `NopEditService` must return a valid (but minimal) `IEditSession`. The `NopSession` above returns `null!` for `Document` — this may cause a `NullReferenceException` if the panel immediately accesses `Document.Root` during construction. 

**Fix if needed**: The panel should only access `_predicateSession.Document` inside `DrawContent()`, NOT in the constructor. If the constructor calls `Open(...)` synchronously, ensure `NopSession.Document` returns a minimal valid `EditDocument`. The simplest fix: return `new EditDocument(null!, Array.Empty<EditNode>())` from `NopSession.Document` — but verify the `EditDocument` constructor signature first.

Alternatively, do NOT open a session in the constructor — open it lazily on first `DrawContent()` call. This simplifies testing. Either approach is acceptable.

### `EventType.GetAllRegistered()` availability
`EventType.GetAllRegistered()` exists in `Fdp.Core`. Verify the method name — it may be `GetRegistered()` or similar. Check the actual `EventType` class in `FDP/Engine/Fdp.Core/`.
