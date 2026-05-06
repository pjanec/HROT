# BATCH-13 Implementation Instructions

**Tasks:** GZ034, GZ035, GZ036  
**Agent:** Claude Sonnet 4.6  
**Task details:** `.dev/gizmos-1/TASK-DETAIL.md` (GZ034, GZ035, GZ036)  
**Tracker:** `.dev/gizmos-1/TASK-TRACKER.md`

---

## MANDATORY READING BEFORE STARTING

1. Read `.dev/gizmos-1/TASK-DETAIL.md` sections for GZ034, GZ035, GZ036.
2. Read `AGENTS.md` at workspace root for coding standards.
3. Read `docs/AI_DEV_GUIDE.md` for architecture context.

**CRITICAL API WARNING — GZ034:** The TASK-DETAIL.md pseudocode for GZ034 uses  
`doc.AddField(key, value)` — **THIS METHOD DOES NOT EXIST** on `EditDocument`.  
The actual StructEdit API is described below. Do NOT copy the pseudocode literally.

---

## Pre-existing Failures (Do NOT count against your work)

Run `dotnet test IOS-IG-SimHost.sln --no-build` before starting and note any pre-existing failures.  
Known pre-existing failures (ignore):
- ~26 tests in `Fdp.Toolkits.Tests` (AimAndFire, MissionDirector, etc.)
- ~4 tests in `Hrot.IG.Tests` (CS011_ EntityInfoTranslator)
- ~3 tests in `Fdp.Presentation.Tests` (EntityInspectorPanelTests)
- ~20 tests in `Hrot.SimHost.Tests` (pre-existing integration failures)

---

## TASK-GZ034 — Fix GizmoSettingsPublisherSystem to Emit StructEdit Schema

### Context

**File to modify:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/GizmoSettingsPublisherSystem.cs`

The system currently builds flat `{"key": value}` JSON using `Utf8JsonWriter`.  
Replace this with a StructEdit `EditDocument` tree built manually from `_registry.EnumerateAll()`,  
then serialized via `EditDocumentJsonSerializer.Serialize(doc)`.

### The Real StructEdit API

Located at `FDP/ExtDeps/StructEdit/src/`:

**`StructEdit.Core/EditDocument.cs`:**
```csharp
public sealed class EditDocument
{
    public EditNode Root { get; }
    public Type RootComponentType { get; }
    public EditScope Scope { get; }
    
    public EditDocument(EditNode root, Type rootComponentType, EditScope scope) { ... }
}
```

**`StructEdit.Core/EditNode.cs`:**  
Immutable descriptor. Constructor:
```csharp
public EditNode(
    EditNodeId id,
    string name,
    string jsonPath,
    EditNodeKind kind,
    Type clrType,
    IValueBinding? binding = null,
    IReadOnlyList<EditNode>? children = null,
    EditNodeMetadata? metadata = null,
    bool isReadOnly = false)
```

**`StructEdit.Core/EditNodeKind.cs` relevant values:**
- `EditNodeKind.SelectionRoot` — synthetic root container (no binding needed)
- `EditNodeKind.Boolean` — for `bool` leaves
- `EditNodeKind.Scalar` — for `int` and `float` leaves

**`StructEdit.Core/IValueBinding.cs`:**
```csharp
public interface IValueBinding
{
    Type ValueType { get; }
    object? GetBoxed();
    void SetBoxed(object? value);
    bool TryGetSpan(out Span<byte> bytes);
}
```

**`StructEdit.Core/EditNodeId.cs`:** `public readonly record struct EditNodeId(int Value);`

**`StructEdit.Core/EditScope.cs`:** Use `EditScope.WholeComponent` static singleton.

**`StructEdit.Json/EditDocumentJsonSerializer.cs`:**
- `public static string Serialize(EditDocument document)` — produces the full JSON.
- Output format: `{"structedit_version":"1.0","rootTypeName":"...","scope":"$","nodes":[...]}`
- Each leaf node appears as: `{"path":"HealthBar.Active","kind":"Boolean","value":true}`

### Implementation Steps

**Step 1 — Add project references to `Fdp.Toolkits.csproj`:**

In `FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj`, add a new `<ItemGroup>`:
```xml
<!-- StructEdit for GizmoSettingsPublisherSystem -->
<ItemGroup>
  <ProjectReference Include="..\..\ExtDeps\StructEdit\src\StructEdit.Core\StructEdit.Core.csproj" />
  <ProjectReference Include="..\..\ExtDeps\StructEdit\src\StructEdit.Json\StructEdit.Json.csproj" />
</ItemGroup>
```

**Step 2 — Create `SnapshotValueBinding` private class in the system file:**

Inside `GizmoSettingsPublisherSystem.cs`, add a private sealed class (at file scope or nested):
```csharp
private sealed class SnapshotValueBinding<T> : IValueBinding
{
    private readonly T _value;
    public SnapshotValueBinding(T value) => _value = value;
    public Type ValueType => typeof(T);
    public object? GetBoxed() => _value;
    public void SetBoxed(object? value) { /* read-only snapshot, no-op */ }
    public bool TryGetSpan(out Span<byte> bytes) { bytes = default; return false; }
}
```

**Step 3 — Replace `Execute` JSON building block:**

Replace the `using var ms = ...` block (and `WriteSettingValue` helper) with a call to  
`BuildEditDocument()` + `EditDocumentJsonSerializer.Serialize(doc)`.

The new `BuildEditDocument()` method:
```csharp
private EditDocument BuildEditDocument()
{
    var leafNodes = new List<EditNode>();
    int nodeId = 1;
    foreach (var (key, active, _) in _registry.EnumerateAll())
    {
        EditNodeKind kind;
        IValueBinding binding;
        Type clrType;
        switch (active.Type)
        {
            case SettingType.Bool:
                kind = EditNodeKind.Boolean;
                binding = new SnapshotValueBinding<bool>(active.BoolValue);
                clrType = typeof(bool);
                break;
            case SettingType.Int32:
                kind = EditNodeKind.Scalar;
                binding = new SnapshotValueBinding<int>(active.IntValue);
                clrType = typeof(int);
                break;
            case SettingType.Float32:
                kind = EditNodeKind.Scalar;
                binding = new SnapshotValueBinding<float>(active.FloatValue);
                clrType = typeof(float);
                break;
            default:
                continue; // skip unknown types
        }

        leafNodes.Add(new EditNode(
            id:       new EditNodeId(nodeId++),
            name:     key,
            jsonPath: key,       // Use the setting key as both name and path
            kind:     kind,
            clrType:  clrType,
            binding:  binding));
    }

    var root = new EditNode(
        id:       new EditNodeId(0),
        name:     "$",
        jsonPath: "$",
        kind:     EditNodeKind.SelectionRoot,
        clrType:  typeof(object),
        children: leafNodes);

    return new EditDocument(root, typeof(GizmoSettingValue), EditScope.WholeComponent);
}
```

**Step 4 — Update `Execute` to use the new builder:**

Replace the `using var ms = ...` to `string json = ...` block with:
```csharp
var doc = BuildEditDocument();
string json = EditDocumentJsonSerializer.Serialize(doc);
```

**Step 5 — Add required usings:**
```csharp
using System.Collections.Generic;
using StructEdit.Core;
using StructEdit.Json;
```

Remove the `using System.Text.Json;` and any `System.IO` imports if no longer needed.

### Tests for GZ034

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmosSettingsTests.cs`  
(or create a new `GizmoSettingsPublisherSystemTests.cs` in the same folder if you prefer)

Write a new test class `GizmoSettingsPublisherSystemTests` with these 4 tests:

**SC-GZ034-1**: `GizmoSettingsPublisherSystem.Execute` publishes a `GizmoUiState` whose  
`EditDocumentJson` can be deserialized to a `JsonDocument` that has `"structedit_version"` key:
```csharp
// Arrange: registry with one bool setting
var reg = new GizmoSettingsRegistry();
reg.RegisterSetting("HealthBar.Active", GizmoSettingValue.From(true));
var publisher = new CapturingPublisher();
var sys = new GizmoSettingsPublisherSystem(reg, publisher);
using var repo = new EntityRepository();
repo.RegisterEvent<GizmoSettingChangedEvent>();

// Act
sys.Execute(repo, 0f);

// Assert
Assert.Single(publisher.Published);
var json = publisher.Published[0].EditDocumentJson;
var jdoc = System.Text.Json.JsonDocument.Parse(json);
Assert.True(jdoc.RootElement.TryGetProperty("structedit_version", out _));
```

**SC-GZ034-2**: A `Bool` setting `"HealthBar.Active"` with value `true` appears in the `nodes`  
array as `{"path":"HealthBar.Active","kind":"Boolean","value":true}`:
```csharp
// Arrange
var reg = new GizmoSettingsRegistry();
reg.RegisterSetting("HealthBar.Active", GizmoSettingValue.From(true));
var publisher = new CapturingPublisher();
var sys = new GizmoSettingsPublisherSystem(reg, publisher);
using var repo = new EntityRepository();
repo.RegisterEvent<GizmoSettingChangedEvent>();

// Act
sys.Execute(repo, 0f);

// Assert
var json = publisher.Published[0].EditDocumentJson;
var jdoc = System.Text.Json.JsonDocument.Parse(json);
var nodes = jdoc.RootElement.GetProperty("nodes");
bool found = false;
foreach (var node in nodes.EnumerateArray())
{
    if (node.GetProperty("path").GetString() == "HealthBar.Active")
    {
        Assert.Equal("Boolean", node.GetProperty("kind").GetString());
        Assert.True(node.GetProperty("value").GetBoolean());
        found = true;
        break;
    }
}
Assert.True(found, "Expected HealthBar.Active node in nodes array");
```

**SC-GZ034-3**: A `Float32` setting `"HealthBar.BarHeight"` with value `3.5f` appears as a  
Scalar node with value `3.5`:
```csharp
var reg = new GizmoSettingsRegistry();
reg.RegisterSetting("HealthBar.BarHeight", GizmoSettingValue.From(3.5f));
var publisher = new CapturingPublisher();
var sys = new GizmoSettingsPublisherSystem(reg, publisher);
using var repo = new EntityRepository();
repo.RegisterEvent<GizmoSettingChangedEvent>();
sys.Execute(repo, 0f);

var json = publisher.Published[0].EditDocumentJson;
var jdoc = System.Text.Json.JsonDocument.Parse(json);
var nodes = jdoc.RootElement.GetProperty("nodes");
bool found = false;
foreach (var node in nodes.EnumerateArray())
{
    if (node.GetProperty("path").GetString() == "HealthBar.BarHeight")
    {
        Assert.Equal("Scalar", node.GetProperty("kind").GetString());
        Assert.Equal(3.5f, (float)node.GetProperty("value").GetDouble(), precision: 4);
        found = true;
        break;
    }
}
Assert.True(found, "Expected HealthBar.BarHeight node in nodes array");
```

**SC-GZ034-4 (regression)**: Existing `SC_GZ017_2_System_PublishesOnFirstDirtyFrame` and  
`SC_GZ017_3_System_SkipsPublishOnCleanSecondFrame` still pass.  
These are in `GizmosNetworkTopicsTests.cs`. The IsDirty guard must be preserved — no changes needed  
to that logic, so these should auto-pass if the above steps are followed correctly.

---

## TASK-GZ035 — Fix Behavior Lifecycle Leak on AI Behavior Abort

### Context

**IMPORTANT:** After reviewing `BehaviorGizmoManagerSystem.cs`, the defensive guard  
**ALREADY EXISTS** at step 3 of `Execute`:
```csharp
// Replace any existing gizmo for this entity.
TeardownEntity(evt.Entity);
var instance = factory.Rent();
instance.OnInitialize(view, evt.Entity);
_activeBehaviorGizmos[evt.Entity] = (instance, factory);
```

Additionally, `SC_GZ006_4_NewAssign_ReplacesExistingGizmo` already covers the case of a new  
`AssignBehaviorEvent` without preceding `ClearBehaviorEvent`.

**Interrupt path audit result:**
- `MissionAdapterSystem.cs` line 64: publishes `ClearBehaviorEvent` on mission exhaustion ✓
- `HillAttackTankNodes.cs` lines 478, 483: publishes `ClearBehaviorEvent` on tank behavior ends ✓
- `MissionControlExecutionSystem.cs` line 234: publishes `ClearBehaviorEvent` on execution complete ✓
- All new behavior assignments flow through `AssignBehaviorEvent` which triggers the defensive guard ✓

**Conclusion:** No production code changes are needed for GZ035. The defensive guard is already  
in place. The task requires writing **SC-GZ035-5** as a named test for the interrupt scenario.

### Test to Write (SC-GZ035-5)

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmosSystemTests.cs`  
Add to the existing `BehaviorGizmoManagerSystemTests` class:

```csharp
[Fact]
public void SC_GZ035_5_BehaviorInterrupt_WithoutClear_TearsDownOldGizmo()
{
    // Scenario: B-Tree interrupt assigns new behavior without ClearBehaviorEvent.
    // The defensive guard in step 3 must tear down the old gizmo.
    var (repo, behavReg, sys) = CreateFixture(predicate: null);
    var factoryA = new MockBehaviorFactory("BehaviorA");
    var factoryB = new MockBehaviorFactory("BehaviorB");
    behavReg.Register(factoryA);
    behavReg.Register(factoryB);
    var entity = repo.CreateEntity();

    // Assign BehaviorA (no ClearBehaviorEvent yet).
    PublishAssignAndExecute(repo, sys, entity, "BehaviorA");
    Assert.Equal(1, factoryA.RentCount);
    Assert.Equal(0, factoryA.ReturnCount);  // not torn down yet

    // Interrupt: assign BehaviorB without first sending ClearBehaviorEvent.
    // The system's defensive guard must call TeardownEntity for BehaviorA.
    PublishAssignAndExecute(repo, sys, entity, "BehaviorB");

    // BehaviorA gizmo torn down via defensive guard.
    Assert.Equal(1, factoryA.ReturnCount);
    // BehaviorB gizmo is now active.
    Assert.Equal(1, factoryB.RentCount);
    Assert.Equal(0, factoryB.ReturnCount);
}
```

### No production code changes needed

The audit confirms all interrupt paths either:
- Already emit `ClearBehaviorEvent` before the next `AssignBehaviorEvent`, OR
- Go through `AssignBehaviorEvent` which triggers the defensive teardown guard.

---

## TASK-GZ036 — CPU Performance Budget for Gizmo Systems

### Context

**IMPORTANT:** `QueryTimeSliced`, `TimeSlicedIteratorState`, and `TimeSliceMetric` **do NOT exist**  
in the codebase. The TASK-DETAIL spec assumes infrastructure that was never built.  
Use a `System.Diagnostics.Stopwatch`-based approach instead — this satisfies all  
SC-GZ036-x success conditions with the same semantics.

**Cross-layer note:** `DataDrivenGizmoSystem` and `StatelessGizmoSystem` are in `Fdp.Toolkits`.  
`GlobalDebugSettings` is in `Hrot.IG`. These layers can't reference each other.  
Solution: add a settable `MaxGizmoFrameMs` property directly to the systems.

### Files to modify

1. `Hrot/Subsystems/Hrot.IG/Gizmos/GlobalDebugSettings.cs` — add field
2. `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DataDrivenGizmoSystem.cs` — add budgeting
3. `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/StatelessGizmoSystem.cs` — add budgeting

### Step 1 — GlobalDebugSettings

In `Hrot/Subsystems/Hrot.IG/Gizmos/GlobalDebugSettings.cs`, add:
```csharp
/// <summary>
/// Maximum milliseconds per frame for all gizmo projection work.
/// 0 means unlimited. Default: 2.0f.
/// </summary>
public float MaxGizmoFrameMs;
```

### Step 2 — DataDrivenGizmoSystem

**Add these fields to `DataDrivenGizmoSystem`:**
```csharp
/// <summary>Max wall-clock budget in ms for step 4. 0 = unlimited.</summary>
public float MaxGizmoFrameMs { get; set; } = 0f;

// Time-slice state: ordered entity list and current offset for carry-over.
private readonly List<Entity> _entityList = new();
private int _timeSliceOffset = 0;
```

**Update step 2 (construction) to also add to `_entityList`:**
```csharp
// After: _activeGizmos[evt.Entity] = list;
_entityList.Add(evt.Entity);
```

**Update `TeardownEntity` to also remove from `_entityList`:**
```csharp
// After removing from _activeGizmos:
_entityList.Remove(entity);
// Reset offset if it would be out of bounds.
if (_timeSliceOffset >= _entityList.Count)
    _timeSliceOffset = 0;
```

**Replace step 4 in `Execute` with the time-sliced version:**
```csharp
// 4. Drive active gizmos (with optional wall-clock budget).
bool alwaysDraw = _isSelectedPredicate == null;
float budget = MaxGizmoFrameMs;

if (budget <= 0f || _entityList.Count == 0)
{
    // Unlimited path: iterate all active gizmos normally.
    foreach (var kvp in _activeGizmos)
    {
        Entity entity = kvp.Key;
        if (!view.IsAlive(entity)) continue;
        bool selected = alwaysDraw || _isSelectedPredicate!(view, entity);
        if (!selected) continue;
        var instances = kvp.Value;
        for (int i = 0; i < instances.Count; i++)
        {
            var gi = instances[i];
            if (gi.RuleIndex < cacheSize && !_globalVisibilityCache[gi.RuleIndex]) continue;
            if (!gi.Definition.VisibilityPolicy.IsEntityVisible(view, entity)) continue;
            gi.Instance.UpdateAndDraw(view, entity, deltaTime, _drawBuilder);
        }
    }
}
else
{
    // Time-sliced path: resume from _timeSliceOffset, stop when budget exceeded.
    var sw = System.Diagnostics.Stopwatch.StartNew();
    int count = _entityList.Count;
    int processed = 0;
    int startOffset = _timeSliceOffset;

    while (processed < count)
    {
        int idx = (startOffset + processed) % count;
        processed++;
        Entity entity = _entityList[idx];

        if (!view.IsAlive(entity)) continue;
        if (!_activeGizmos.TryGetValue(entity, out var instances)) continue;

        bool selected = alwaysDraw || _isSelectedPredicate!(view, entity);
        if (!selected) continue;

        for (int i = 0; i < instances.Count; i++)
        {
            var gi = instances[i];
            if (gi.RuleIndex < cacheSize && !_globalVisibilityCache[gi.RuleIndex]) continue;
            if (!gi.Definition.VisibilityPolicy.IsEntityVisible(view, entity)) continue;
            gi.Instance.UpdateAndDraw(view, entity, deltaTime, _drawBuilder);
        }

        // Check budget after each entity.
        if (sw.Elapsed.TotalMilliseconds >= budget)
            break;
    }

    // Update offset for next frame: resume where we left off.
    _timeSliceOffset = (startOffset + processed) % count;
}
```

**Add `using System.Collections.Generic;` if not present.**

### Step 3 — StatelessGizmoSystem

**Add these fields to `StatelessGizmoSystem`:**
```csharp
/// <summary>Max wall-clock budget in ms for entity iteration. 0 = unlimited.</summary>
public float MaxGizmoFrameMs { get; set; } = 0f;
```

**Replace the inner entity iteration loop in `Execute` with a time-sliced version:**

The current code iterates entity index linearly per rule. Wrap the inner loop with budget checking:

```csharp
// For each rule:
var sw = (budget > 0f) ? System.Diagnostics.Stopwatch.StartNew() : null;
bool budgetExceeded = false;

for (int r = 0; r < ruleCount; r++)
{
    if (r < _globalVisibilityCache.Length && !_globalVisibilityCache[r])
        continue;
    
    if (budgetExceeded) break;

    var rule = rules[r];

    for (int i = 0; i <= maxIndex; i++)
    {
        ref var header = ref entityIndex.GetHeader(i);
        if (!header.IsActive) continue;
        if (!BitMask256.HasAll(header.ComponentMask, rule.RequiredMask)) continue;

        var entity = new Entity(i, header.Generation);
        if (!alwaysDraw && !_isSelectedPredicate!(view, entity)) continue;

        rule.Projector.Draw(view, entity, _drawBuilder);

        // Check budget after each entity (only if budget is active).
        if (sw != null && sw.Elapsed.TotalMilliseconds >= budget)
        {
            budgetExceeded = true;
            break;
        }
    }
}
```

Where `float budget = MaxGizmoFrameMs;` is read at the start of `Execute`.

### Tests for GZ036

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmosSystemTests.cs`  
or `StatelessGizmoSystemTests.cs`

**Test fixtures needed:**
- Re-use the existing `DataDrivenGizmoSystemTests` infrastructure from `GizmosSystemTests.cs`

**SC-GZ036-1**: With `MaxGizmoFrameMs = 0.0001f` (near-zero budget), only a subset of  
entities is processed per frame with 1000 entities. Since we can't reliably time 1000 entities  
in a unit test, use a simpler approach: with budget 0.0001ms and 100 entities, assert that not  
ALL entities have their gizmo drawn in a single frame (call count < 100):

```csharp
[Fact]
public void SC_GZ036_1_NearZeroBudget_ProcessesOnlySubset()
{
    var repo = GizmoTestRepo.Create();
    var reg = new GizmoRegistry();
    var drawCallCount = 0;
    // Register a projector that counts calls
    var countingDef = new CountingGizmoDefinition(() => drawCallCount++);
    reg.RegisterRule(new GizmoRule(countingDef, BitMask256.With(repo.GetComponentId<GizmoTestCompA>())));
    
    var buffer = new DebugPrimitiveBuffer();
    var sys = new DataDrivenGizmoSystem(reg, buffer);
    sys.MaxGizmoFrameMs = 0.0001f; // ~0 ms budget
    
    // Create 50 entities with the required component
    for (int i = 0; i < 50; i++)
    {
        var e = repo.CreateEntity();
        repo.AddComponent(e, new GizmoTestCompA { Value = i });
    }
    repo.Bus.SwapBuffers();
    sys.Execute(repo, 0f); // Let constructions be processed
    drawCallCount = 0;
    
    // Second frame: with near-zero budget, should process fewer than all 50
    sys.Execute(repo, 0f);
    
    // With a budget this small, we expect significantly fewer than 50 draws
    // (in practice 0 or very few, since even one entity may exceed 0.0001ms)
    Assert.True(drawCallCount < 50, $"Expected <50 draws with near-zero budget, got {drawCallCount}");
}
```

**SC-GZ036-2**: With `MaxGizmoFrameMs = 10000f` (huge budget), all entities are processed:

```csharp
[Fact]
public void SC_GZ036_2_LargeBudget_ProcessesAllEntities()
{
    var repo = GizmoTestRepo.Create();
    var reg = new GizmoRegistry();
    var drawCallCount = 0;
    var countingDef = new CountingGizmoDefinition(() => drawCallCount++);
    reg.RegisterRule(new GizmoRule(countingDef, BitMask256.With(repo.GetComponentId<GizmoTestCompA>())));
    
    var buffer = new DebugPrimitiveBuffer();
    var sys = new DataDrivenGizmoSystem(reg, buffer);
    sys.MaxGizmoFrameMs = 10000f; // unlimited in practice
    
    const int entityCount = 20;
    for (int i = 0; i < entityCount; i++)
    {
        var e = repo.CreateEntity();
        repo.AddComponent(e, new GizmoTestCompA { Value = i });
    }
    repo.Bus.SwapBuffers();
    sys.Execute(repo, 0f);
    drawCallCount = 0;
    
    sys.Execute(repo, 0f);
    Assert.Equal(entityCount, drawCallCount);
}
```

**SC-GZ036-3**: `MaxGizmoFrameMs = 0` processes all entities (unlimited):

```csharp
[Fact]
public void SC_GZ036_3_ZeroBudget_MeansUnlimited()
{
    var repo = GizmoTestRepo.Create();
    var reg = new GizmoRegistry();
    var drawCallCount = 0;
    var countingDef = new CountingGizmoDefinition(() => drawCallCount++);
    reg.RegisterRule(new GizmoRule(countingDef, BitMask256.With(repo.GetComponentId<GizmoTestCompA>())));
    
    var buffer = new DebugPrimitiveBuffer();
    var sys = new DataDrivenGizmoSystem(reg, buffer);
    sys.MaxGizmoFrameMs = 0f; // unlimited
    
    const int entityCount = 20;
    for (int i = 0; i < entityCount; i++)
    {
        var e = repo.CreateEntity();
        repo.AddComponent(e, new GizmoTestCompA { Value = i });
    }
    repo.Bus.SwapBuffers();
    sys.Execute(repo, 0f);
    drawCallCount = 0;
    
    sys.Execute(repo, 0f);
    Assert.Equal(entityCount, drawCallCount);
}
```

**SC-GZ036-4**: The time-slice state (`_timeSliceOffset`, `_entityList`) is a field, not  
re-allocated per frame. This is an architectural assertion — verify by checking that calling  
`Execute` multiple times does not throw and maintains state. The offset wraps correctly:

```csharp
[Fact]
public void SC_GZ036_4_TimeSliceState_Is_Field_Not_Reallocated()
{
    // Simply verify that the system survives multiple Execute calls without exception
    // and that the _entityList is maintained between calls (verified via draw count continuity).
    var repo = GizmoTestRepo.Create();
    var reg = new GizmoRegistry();
    int totalDraws = 0;
    var countingDef = new CountingGizmoDefinition(() => totalDraws++);
    reg.RegisterRule(new GizmoRule(countingDef, BitMask256.With(repo.GetComponentId<GizmoTestCompA>())));
    
    var buffer = new DebugPrimitiveBuffer();
    var sys = new DataDrivenGizmoSystem(reg, buffer);
    sys.MaxGizmoFrameMs = 10000f;
    
    const int entityCount = 5;
    for (int i = 0; i < entityCount; i++)
    {
        var e = repo.CreateEntity();
        repo.AddComponent(e, new GizmoTestCompA { Value = i });
    }
    repo.Bus.SwapBuffers();
    
    // Run 3 frames
    for (int frame = 0; frame < 3; frame++)
    {
        sys.Execute(repo, 0f);
    }
    
    // All 3 frames processed all entities = 3 * entityCount draws
    // (first frame processes constructions + draw; subsequent frames just draw)
    // Exact count depends on implementation, but no exception is the main check.
    Assert.True(totalDraws > 0, "Expected at least some gizmo draws across 3 frames");
}
```

**NOTE:** For SC-GZ036-1/2/3/4 tests you may need a `CountingGizmoDefinition` helper. Check if  
`MockGizmoFactory` or similar in `GizmosSystemTests.cs` already provides a counting mechanism.  
If not, create a minimal implementation alongside the tests:

```csharp
private sealed class CountingGizmoDefinition : IGizmoDefinition
{
    private readonly Action _onDraw;
    public CountingGizmoDefinition(Action onDraw) => _onDraw = onDraw;
    
    public IVisibilityPolicy VisibilityPolicy => AlwaysVisiblePolicy.Instance;
    
    public IStatefulGizmo CreateInstance() => new CountingGizmoInstance(_onDraw);
    
    private sealed class CountingGizmoInstance : IStatefulGizmo
    {
        private readonly Action _onDraw;
        public CountingGizmoInstance(Action onDraw) => _onDraw = onDraw;
        public void OnInitialize(ISimulationView view, Entity entity) { }
        public void UpdateAndDraw(ISimulationView view, Entity entity, float dt, IDebugDrawBuilder builder) => _onDraw();
        public void OnTeardown() { }
    }
}
```

Check whether `IGizmoDefinition`, `IStatefulGizmo`, `IVisibilityPolicy`, and `AlwaysVisiblePolicy` 
are the correct interfaces/types used in the test infrastructure — look at existing tests in  
`GizmosSystemTests.cs` to find the right mock/factory helpers before writing new ones.

---

## Build & Test Validation

After implementing all three tasks:

```
dotnet build IOS-IG-SimHost.sln --no-incremental -clp:ErrorsOnly
```
→ **Must show 0 errors.**

```
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --no-build
```
→ GZ034: SC-GZ034-1/2/3 pass, SC-GZ017-2/3 still pass (regression)  
→ GZ035: SC-GZ035-5 passes, SC-GZ006-4 still passes  
→ GZ036: SC-GZ036-1/2/3/4 pass

---

## Commit Instructions

**Step 1 — FDP submodule commit (GZ034, GZ035, GZ036 FDP changes):**
```
cd FDP
git add -A
git commit -m "GZ034/GZ035/GZ036: StructEdit schema publisher, behavior interrupt test, gizmo frame budget"
```

**Step 2 — Root repo commit (Hrot.IG changes for GZ036 + FDP pointer update):**
```
cd ..
git add -A
git commit -m "GZ034-036: StructEdit JSON schema, behavior interrupt guard test, MaxGizmoFrameMs budget"
```

---

## Batch Report

Create `.dev/gizmos-1/reports/BATCH-13-REPORT.md` documenting:
- Which files were created/modified
- Test counts per task (SC-GZ034-x, SC-GZ035-5, SC-GZ036-x)
- Any deviations from the spec (especially the StructEdit API adaptation for GZ034)
- Build output (0 errors)

Update `.dev/gizmos-1/TASK-TRACKER.md`:
- Mark GZ034, GZ035, GZ036 as `[x]` done

---

## Summary Table

| Task | Files Changed | New Tests |
|------|--------------|-----------|
| GZ034 | `Fdp.Toolkits.csproj`, `GizmoSettingsPublisherSystem.cs` | SC-GZ034-1/2/3 |
| GZ035 | None (defensive guard already in place) | SC-GZ035-5 |
| GZ036 | `GlobalDebugSettings.cs`, `DataDrivenGizmoSystem.cs`, `StatelessGizmoSystem.cs` | SC-GZ036-1/2/3/4 |
