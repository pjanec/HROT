# Edge Cases and Mitigations Reference

**Version:** 1.1  
**Date:** 2026-02-14  
**Last Updated:** 2026-03-05  
**Purpose:** Comprehensive catalog of identified gaps, edge cases, and logical flaws with fixes

This document consolidates all critical edge cases identified during design review and tracks their resolution across all design documents and task details.

---

## Status Key

- ✅ **DOCUMENTED** - Issue documented in design docs with mitigation strategy
- 🔨 **TASK ADDED** - Specific implementation task created in TASK-DETAILS
- ⬜ **PENDING** - Identified but not yet fully addressed

---

## 1. Architectural Gaps (System-Wide)

### 1.1 Terrain Height Discrepancy

**Status**: ✅ DOCUMENTED + 🔨 TASK ADDED

**The Issue:** IG renders 3D terrain, but SimHost (CarKinem) runs 2D physics. If IG places entity at Alt=500m, SimHost might snap it to Z=0, causing it to disappear underground.

**The Risk:** Visual glitches, entity teleportation, physics-rendering desync

**Fix Implemented:**
- **Design Doc**: [DESIGN-SIMHOST.md Section 6.1](./DESIGN-SIMHOST.md#61-terrain-height-preservation)
- **Code Pattern**: `WorldPosBridgeSystem` preserves existing altitude when updating XY position
- **Task Added**: SIMHOST-S3.X (See below)

```csharp
var existingGeo = world.TryGetComponent<WorldPosComponent>(entity);
float altitude = existingGeo?.Pos.Altitude ?? 0.0f;
var cartesian = new CartesianCoordinate { X = pos.X, Y = pos.Y, Z = altitude };
```

**Future Enhancement:** Integrate with `ITerrainService` for dynamic height lookup

---

### 1.2 JSON Merge Patch Array Behavior

**Status**: ✅ DOCUMENTED

**The Issue:** RFC 7396 treats arrays as atomic (REPLACE), not incremental. Sending `{"layers": ["NewLayer"]}` disables all other layers.

**The Risk:** IOS toggling one layer accidentally disables all others

**Fix Implemented:**
- **Design Doc**: [DESIGN-SHARED.md MapInteractionConfig](./DESIGN-SHARED.md#map-configuration-from-mapdescriptorscs)
- **Design Doc**: [DESIGN-IOS.md Section 7.2](./DESIGN-IOS.md#72-json-merge-patch-array-semantics)
- **Data Structure**: Use `Dictionary<string, bool>` for layers, NOT `List<string>`

**Correct Structure:**
```json
{
  "view": {
    "layers": {"Terrain": true, "Units": true, "Overlays": false}
  }
}
```

**Verification**: Check `Hrot.NED` implementation during Phase P2

---

### 1.3 Non-Deterministic ECS Component IDs Across Binaries

**Status**: 🔨 TASK ADDED

**The Issue:** `ComponentTypeRegistry` in `Fdp.Kernel` assigns integer IDs to component structs using a static counter (`_nextId++`). The ID assigned to any given struct depends on the order in which `ComponentType<T>.Id` is first accessed during process startup — which is determined by static constructor execution order.

In a standalone binary (e.g., `SimHost.exe`), struct `SimTransform` might receive ID `0`. In an aggregated `Runner.exe` that loads all three applications into a single process, the same struct might receive a completely different ID because dozens of IG and IOS component static constructors that were absent in the standalone binary now execute first.

**The Risk:** Catastrophic Flight Recorder data corruption. The Flight Recorder writes raw integer component type IDs into every record frame. When a recording is played back in a binary with different ID assignments, `PlaybackController` injects component bytes into the wrong memory tables. The failure mode is silent: values appear in the wrong fields, presenting as bizarre simulation replay behaviour rather than a clear error or crash. **The Runner project cannot progress until this is resolved.**

**Fix Implemented:**
- **Design Doc**: [DESIGN-RUNNER.md Section 11.2](./DESIGN-RUNNER.md#112-solution-explicit-componentid-attribute) — `[ComponentId(byte)]` attribute mirroring `[EventId(int)]` pattern
- **Design Doc**: [DESIGN-RUNNER.md Section 11.3](./DESIGN-RUNNER.md#113-solution-globalcomponentids-central-catalog) — `GlobalComponentIds` block-allocation catalog
- **Task Added**: [R0.1](./TASK-DETAILS-RUNNER.md#r01-make-component-ids-deterministic) — Implement attribute, catalog, registry enforcement, apply to all existing components

**Code Pattern** (after fix):
```csharp
// GlobalComponentIds.cs in Fdp.Kernel
public static class GlobalComponentIds
{
    public const byte SimTransform = 0; // Fdp.Kernel block: 0–19
    public const byte SimVelocity  = 1;
    // ...
}

// SimComponents.cs
[ComponentId(GlobalComponentIds.SimTransform)]
public struct SimTransform { ... }
```

**Constraint**: IDs must be `byte` (0–255) due to the `BitMask256` constraint in `EntityHeader.ComponentMask`.

---

### 1.4 Silent Flight Recorder Memory Corruption on Schema Drift

**Status**: 🔨 TASK ADDED

**The Issue:** The Flight Recorder writes raw component bytes at field offsets directly into component memory tables. If a component struct's memory layout changes between the version that recorded a file and the version playing it back (field added, field reordered, padding changed, alignment attribute modified), `PlaybackController` reads mismatched bytes at the old offsets and injects them into the wrong fields.

**The Risk:** Incorrect simulation replay state with no diagnostic output. Entity positions, velocities, and health values from one component silently overwrite another. The failure is especially insidious because it only manifests during Flight Recorder playback, not during live simulation — making it a hard-to-reproduce, hard-to-diagnose bug class. This is distinct from the ID mismatch problem (1.3): a struct can have a stable, correct ID _and_ a changed layout — both failure modes must be independently guarded.

**Fix Implemented:**
- **Design Doc**: [DESIGN-RUNNER.md Section 11.4](./DESIGN-RUNNER.md#114-the-second-problem-silent-flight-recorder-schema-drift) — `ComponentSchemaInfo`, `ComponentLayoutHasher`, `SchemaValidator`, `RecordingMetadata.SchemaManifest`
- **Task Added**: [R0.2](./TASK-DETAILS-RUNNER.md#r02-implement-flight-recorder-schema-manifest) — Implement hasher, validator, wire into `AsyncRecorder.Dispose()` and `PlaybackController` constructor

**Code Pattern** (after fix):
```csharp
// In AsyncRecorder.Dispose(): save manifest
_metadata.SchemaManifest = ComponentTypeRegistry
    .GetRecordableTypeIds()
    .ToDictionary(id => id, id => new ComponentSchemaInfo
    {
        Name       = ComponentTypeRegistry.GetType(id).FullName,
        Size       = Marshal.SizeOf(ComponentTypeRegistry.GetType(id)),
        LayoutHash = ComponentLayoutHasher.ComputeHash(ComponentTypeRegistry.GetType(id)),
        IsManaged  = ComponentTypeRegistry.IsManaged(id)
    });

// In PlaybackController constructor: validate before touching any bytes
SchemaValidator.Validate(_metadata); // throws on mismatch, warns on null manifest
```

**Graceful degradation**: Recordings that pre-date this feature have `SchemaManifest = null`. `SchemaValidator` detects `null` and logs a warning instead of throwing, so old recordings remain playable.

---

## 2. IOS Mock Gaps

### 2.1 Initial State Synchronization (Late Join)

**Status**: ✅ DOCUMENTED + 🔨 TASK ADDED

**The Issue:** When IOS starts after IG, `MapInteractionConfig` (Volatile) might be missed. IOS UI defaults might not match IG's actual state.

**The Risk:** User toggles checkbox based on wrong assumption, sends incorrect patch

**Fix Implemented:**
- **Design Doc**: [DESIGN-IOS.md Section 7.1](./DESIGN-IOS.md#71-late-join-synchronization)
- **Task Added**: IOS-P9.3 (See below)
- **Pattern**: Read `MapConfigStatus` on startup, hydrate UI before allowing interaction

```csharp
public async Task SynchronizeWithIg(int timeoutMs = 5000)
{
    var statusReader = new DdsReader<MapConfigStatus>(_participant, "MapConfigStatus");
    // ... wait for status sample
    var config = JsonConvert.DeserializeObject<MapConfig>(status.Data.CurrentSettingsJson);
    _uiState.SelectedTool = config.Tool;
    _uiState.VisibleLayers = config.View.Layers;
    _isSynchronized = true;
}
```

---

### 2.2 DER Type Safety

**Status**: ✅ DOCUMENTED

**The Issue:** `DerRepo` stores descriptors as `object`. Schema changes cause runtime cast failures.

**The Risk:** SimHost updates `EntityInfo` field, IOS crashes

**Fix Implemented:**
- **Design Doc**: [DESIGN-IOS.md Section 7.3](./DESIGN-IOS.md#73-der-type-safety)
- **Pattern**: Add schema version checking in `SetDescriptor<T>()`
- **Graceful Degradation**: Log warning on version mismatch, don't crash

```csharp
public void SetDescriptor<T>(T descriptor) where T : class
{
    var versionProp = typeof(T).GetProperty("SchemaVersion");
    if (versionProp != null)
    {
        var version = (int)versionProp.GetValue(descriptor);
        if (version != GetExpectedVersion<T>())
        {
            _logger.LogWarning($"Schema version mismatch for {typeof(T).Name}");
        }
    }
    _descriptors[typeof(T).Name] = descriptor;
}
```

---

## 3. IG Mock Gaps

### 3.1 Tool Stack Preemption

**Status**: ✅ DOCUMENTED + 🔨 TASK ADDED

**The Issue:** User mid-interaction (2nd click pending) when IOS forces tool change

**The Risk:** Ghost entities left behind, corrupt tool state

**Fix Implemented:**
- **Design Doc**: [DESIGN-IG.md Section 9.2](./DESIGN-IG.md#92-tool-preemption--cleanup)
- **Task Added**: IG-3.6 (See below)
- **Pattern**: `ToolManager.SwitchTool()` calls `OnExit()` before switching

```csharp
public void SwitchTool(IMapTool newTool)
{
    if (_currentTool != null)
    {
        _currentTool.OnExit();  // CRITICAL: cleanup before switching
        _currentTool.Dispose();
    }
    _currentTool = newTool;
    _currentTool?.OnEnter();
}
```

---

### 3.2 Interaction Dead Zones (ImGui Click-Through)

**Status**: ✅ DOCUMENTED + 🔨 TASK ADDED

**The Issue:** ImGui panels overlay Raylib map. Clicking button might also trigger `MapClickEvent`.

**The Risk:** Accidental map interactions while using UI

**Fix Implemented:**
- **Design Doc**: [DESIGN-IG.md Section 9.1](./DESIGN-IG.md#91-imgui-input-blocking)
- **Task Added**: IG-1.5 (See below)
- **Pattern**: Check `ImGui.GetIO().WantCaptureMouse` before processing map input

```csharp
if (!ImGui.GetIO().WantCaptureMouse)
{
    _canvas?.Update(dt);  // Only process map input if ImGui not capturing
}
```

---

### 3.3 Headless Camera API

**Status**: ✅ DOCUMENTED + 🔨 TASK ADDED

**The Issue:** Headless mode skips Raylib window, but code calls `Raylib.GetMousePosition()` → crash

**The Risk:** Automated tests fail or return garbage data

**Fix Implemented:**
- **Design Doc**: [DESIGN-IG.md Section 9.3](./DESIGN-IG.md#93-headless-camera-abstraction)
- **Design Doc**: [DESIGN-RUNNER.md Section 9.2](./DESIGN-RUNNER.md#92-headless-rendering-abstraction)
- **Task Added**: RUNNER-R3.6 (See below)
- **Pattern**: Abstract camera behind `ICameraService`, inject `HeadlessCamera` in headless mode

```csharp
public interface ICameraService
{
    Vector2 ScreenToWorld(Vector2 screenPos);
    Rectangle GetViewBounds();
}

public class HeadlessCamera : ICameraService
{
    public Vector2 ScreenToWorld(Vector2 screenPos) => screenPos;  // Identity
    public Rectangle GetViewBounds() => new Rectangle(0, 0, 1920, 1080);
}
```

---

## 4. SimHost Mock Gaps

### 4.1 Physics Initialization Jitter

**Status**: ✅ DOCUMENTED + 🔨 TASK ADDED

**The Issue:** First frame after entity creation might have dt=0 or uninitialized velocities

**The Risk:** Entity teleportation, velocity spikes in first network packet

**Fix Implemented:**
- **Design Doc**: [DESIGN-SIMHOST.md Section 6.2](./DESIGN-SIMHOST.md#62-physics-initialization-jitter)
- **Task Added**: SIMHOST-S3.X (covered under terrain task)
- **Pattern**: Initialize `VehicleState` with zeros, skip physics on first frame

```csharp
// In EntityFactorySystem
entity.SetComponent(new VehicleState 
{
    Position = startPos,
    Speed = 0.0f,
    Accel = 0.0f
});
entity.SetComponent(new FirstFrameFlag());  // Marker component

// In CarKinematicsSystem
foreach (var entity in query)
{
    if (world.HasComponent<FirstFrameFlag>(entity))
    {
        world.RemoveComponent<FirstFrameFlag>(entity);
        continue;  // Skip physics on first frame
    }
    // ... normal physics
}
```

---

### 4.2 Mission Re-Entrancy

**Status**: ✅ DOCUMENTED + 🔨 TASK ADDED

**The Issue:** IOS sends `CMD_JUMP_TO_TASK` to currently executing task, resets state

**The Risk:** "Wait 30s" timer resets to 0 indefinitely if user keeps clicking

**Fix Implemented:**
- **Design Doc**: [DESIGN-SIMHOST.md Section 6.3](./DESIGN-SIMHOST.md#63-mission-command-re-entrancy)
- **Task Added**: SIMHOST-S3.X (covered under terrain task)
- **Pattern**: Check if target task already active, only reset if `ForceRestart=true`

```csharp
private void HandleJumpCommand(int entityId, int targetTaskIndex, bool forceRestart)
{
    var mission = GetMissionComponent(entityId);
    
    if (mission.ActiveTaskIndex == targetTaskIndex && !forceRestart)
    {
        _logger.LogWarning($"Task {targetTaskIndex} already active, ignoring");
        return;
    }
    
    mission.ActiveTaskIndex = targetTaskIndex;
    mission.TaskStartTime = DateTime.UtcNow;
}
```

---

## 5. Runner / Integration Gaps

### 5.1 Waiting Room Deadlock

**Status**: ✅ DOCUMENTED

**The Issue:** Circular dependency: SimHost waits for IG, IG waits for SimHost

**The Risk:** Deadlock, no subsystem starts

**Fix Implemented:**
- **Design Doc**: [DESIGN-RUNNER.md Section 9.1](./DESIGN-RUNNER.md#91-waiting-room-deadlock-prevention)
- **Validation**: Orchestrator validates dependency graph before starting
- **Hierarchy**: SimHost waits for none → IG waits for SimHost → IOS waits for IG

```csharp
public void ValidateWaitingRoomConfig()
{
    var deps = new Dictionary<string, List<string>>();
    foreach (var subsystem in _subsystems)
        deps[subsystem.Name] = subsystem.GetWaitForList();
    
    if (HasCycle(deps))
        throw new InvalidOperationException("Circular waiting room dependency detected");
}
```

---

### 5.2 Test Script Timing Precision

**Status**: ✅ DOCUMENTED

**The Issue:** If headless loop lags, actions at `"time": 5.0` might miss their window

**The Risk:** Flaky tests, missed actions

**Fix Implemented:**
- **Design Doc**: [DESIGN-RUNNER.md Section 9.3](./DESIGN-RUNNER.md#93-test-script-timing-precision)
- **Pattern**: Use priority queue, execute ALL actions where `time <= currentTime`

```csharp
while (actionQueue.Count > 0 && actionQueue.Peek().Time <= currentTime)
{
    var step = actionQueue.Dequeue();
    await ExecuteStep(step);
}
```

---

## 6. New Tasks Added

### SIMHOST-S3.X: Terrain Height & Physics Initialization

**File**: TASK-DETAILS-SIMHOST.md (to be added to Phase S3)

**Estimated**: 0.5 days

**Description**: Implement terrain height preservation and physics initialization guards

**Success Criteria**:
1. `WorldPosBridgeSystem` preserves altitude from existing `WorldPosComponent`
2. `EntityFactorySystem` initializes `VehicleState` with `Speed=0`, `Accel=0`
3. `CarKinematicsSystem` skips physics if `dt <= 0` or `dt > 0.1`
4. Add `FirstFrameFlag` component to defer physics for 1 frame
5. `HandleJumpCommand` checks if task already active before resetting

**Testing**:
```csharp
// Test 1: Altitude preservation
var entity = CreateEntityAt(lat: 50.0, lon: 14.0, alt: 500.0);
SimulatePhysics(entity, frames: 100);
Assert.Equal(500.0, entity.Get<WorldPosComponent>().Pos.Altitude, tolerance: 0.1);

// Test 2: No first-frame jitter
var entity = CreateEntity();
var firstPos = entity.Get<VehicleState>().Position;
SimulatePhysics(entity, frames: 1);
var secondPos = entity.Get<VehicleState>().Position;
Assert.Equal(firstPos, secondPos);  // No movement on first frame

// Test 3: Mission re-entrancy
var entity = CreateEntityWithMission();
ExecuteJumpCommand(entity, taskIndex: 2);
var startTime1 = entity.Get<Mission>().TaskStartTime;
ExecuteJumpCommand(entity, taskIndex: 2, forceRestart: false);
var startTime2 = entity.Get<Mission>().TaskStartTime;
Assert.Equal(startTime1, startTime2);  // Task not restarted
```

---

### IG-1.5: Implement ImGui Input Blocking

**File**: TASK-DETAILS-IG.md (to be added to Phase IG-1)

**Estimated**: 0.25 days

**Description**: Prevent map input when ImGui has focus

**Success Criteria**:
1. In `IgApplication.Update()`, check `ImGui.GetIO().WantCaptureMouse`
2. Skip `MapCanvas.Update()` when ImGui capturing
3. Document in code comments: "Prevents click-through to map when using UI"

**Testing**:
```csharp
// Manual Test: Click ImGui button, verify no MapClickEvent fired
ImGui.Begin("Test Panel");
if (ImGui.Button("Click Me"))
{
    _buttonClicked = true;
}
ImGui.End();

// In MapCanvas
if (!ImGui.GetIO().WantCaptureMouse)
{
    ProcessMapClick();  // Should NOT execute when button clicked
}

Assert.True(_buttonClicked);
Assert.False(_mapClickedThroughPanel);
```

---

### IG-3.6: Implement Tool Cleanup Logic

**File**: TASK-DETAILS-IG.md (to be added to Phase IG-3)

**Estimated**: 0.5 days

**Description**: Ensure all tools clean up state in `OnExit()`

**Success Criteria**:
1. `ToolManager.SwitchTool()` calls `currentTool.OnExit()` before switching
2. `MeasureTool.OnExit()` deletes temp measurement entities
3. `CreationTool.OnExit()` deletes ghost preview entities
4. `EditTool.OnExit()` deletes vertex handles
5. Use `[TempEntity]` tag for automatic cleanup query

**Testing**:
```csharp
// Test: Switch tool mid-measurement
var tool = new MeasureTool();
tool.OnEnter();
tool.OnClick(worldPos: new Vector2(100, 100));  // First click

// Switch tool before second click
_toolManager.SwitchTool(new SelectionTool());

// Verify temp entities cleaned up
var tempEntities = _world.Query().With<TempMeasurement>().Build();
Assert.Empty(tempEntities);
```

---

### IOS-P9.3: Implement State Reconciliation System

**File**: TASK-DETAILS-IOS.md (to be added to Phase IOS-P9)

**Estimated**: 0.5 days

**Description**: Sync UI state with IG on startup (late join handling)

**Success Criteria**:
1. `SynchronizeWithIg()` method reads `MapConfigStatus` on startup
2. Hydrates UI state (layers, tool, style) from IG's current config
3. Shows "Synchronizing..." UI during wait (max 5s timeout)
4. Blocks UI interaction until sync complete
5. Graceful fallback if IG not available (use defaults)

**Testing**:
```csharp
// Test: Late join synchronization
// 1. Start IG with custom config (Layers: [Units=true, Terrain=false])
// 2. Start IOS after 2 seconds
// 3. Verify IOS UI reflects IG's state
await IosSubsystem.SynchronizeWithIg(timeoutMs: 5000);
Assert.True(_uiState.VisibleLayers["Units"]);
Assert.False(_uiState.VisibleLayers["Terrain"]);
```

---

### RUNNER-R3.6: Implement Headless Camera Service

**File**: TASK-DETAILS-RUNNER.md (to be added to Phase R3)

**Estimated**: 0.5 days

**Description**: Create camera abstraction for headless IG testing

**Success Criteria**:
1. Define `ICameraService` interface (ScreenToWorld, WorldToScreen, GetViewBounds)
2. Implement `RaylibCameraService` (wraps real camera)
3. Implement `HeadlessCamera` (mathematical projection without GPU)
4. `IgSubsystem` injects appropriate service based on `Headless` flag
5. All tools use `ICameraService` instead of direct Raylib calls

**Testing**:
```csharp
// Test: Headless camera operations
var headlessCamera = new HeadlessCamera(1920, 1080);
var worldPos = headlessCamera.ScreenToWorld(new Vector2(960, 540));
Assert.Equal(new Vector2(960, 540), worldPos);  // Identity transform

var bounds = headlessCamera.GetViewBounds();
Assert.Equal(new Rectangle(0, 0, 1920, 1080), bounds);

// Test: IG runs in headless mode without crashes
var config = new IgConfiguration { Headless = true };
var ig = new IgSubsystem();
ig.Initialize(config);
Assert.NoThrow(() => ig.Update(0.016f));  // Should not crash
```

---

## 7. Task Summary

**Total New Tasks**: 5

| ID | Component | Phase | Effort | Description |
|----|-----------|-------|--------|-------------|
| **SIMHOST-S3.X** | SimHost | S3 | 0.5d | Terrain height + physics init |
| **IG-1.5** | IG | IG-1 | 0.25d | ImGui input blocking |
| **IG-3.6** | IG | IG-3 | 0.5d | Tool cleanup logic |
| **IOS-P9.3** | IOS | IOS-P9 | 0.5d | State reconciliation |
| **RUNNER-R3.6** | Runner | R3 | 0.5d | Headless camera service |

**Total Effort**: 2.25 days

**Impact on Schedule**:
- SimHost: +0.5d (now 18.5 days)
- IG: +0.75d (now 14.75 days)
- IOS: +0.5d (now 12.5 days)
- Runner: +0.5d (now 19.25 days)
- Runner R0 (new): +3.0d (now 22.25 days)
- **Overall Project**: +5.25 days (135 → 140+ tasks)

---

## 8. Verification Checklist

Use this checklist during implementation to ensure all mitigations are in place:

### SimHost
- [ ] `WorldPosBridgeSystem` reads existing altitude before overwriting
- [ ] `VehicleState` initialized with zero velocity
- [ ] `CarKinematicsSystem` guards against dt<=0
- [ ] `FirstFrameFlag` component implemented
- [ ] Mission jump command checks active task
- [ ] Unit tests for altitude preservation pass
- [ ] Unit tests for physics init pass

### IG
- [ ] `ImGui.GetIO().WantCaptureMouse` checked before map input
- [ ] `ToolManager.SwitchTool()` calls `OnExit()`
- [ ] All tools implement `OnExit()` cleanup
- [ ] `HeadlessCamera` service implemented
- [ ] `ICameraService` injected based on headless flag
- [ ] Manual test: click button, no map event fired
- [ ] Manual test: switch tool mid-interaction, no ghosts left

### IOS
- [ ] `SynchronizeWithIg()` implemented
- [ ] "Synchronizing..." UI shown during wait
- [ ] UI blocked until sync complete
- [ ] Graceful timeout handling (5s)
- [ ] `MapInteractionConfig` uses Dictionary for layers
- [ ] DER `SetDescriptor` checks schema version

### Runner
- [ ] Waiting room dependency graph validation
- [ ] Cycle detection algorithm implemented
- [ ] Test script timing uses priority queue
- [ ] Subsystem crash isolation (try-catch optional)
- [ ] Metrics collection in background thread
- [ ] `[ComponentId(byte)]` attribute applied to all component structs (R0.1)
- [ ] `GlobalComponentIds` catalog complete and collision-free (R0.1)
- [ ] `ComponentTypeRegistry` reads attribute instead of `_nextId++` (R0.1)
- [ ] `FdpConfig.EnforceExplicitComponentIds = true` in all production entry-points (R0.1)
- [ ] `ComponentLayoutHasher` produces stable hashes (R0.2)
- [ ] `SchemaManifest` saved in `.meta.json` by `AsyncRecorder` (R0.2)
- [ ] `SchemaValidator.Validate()` called in `PlaybackController` constructor (R0.2)

---

## 9. References

- [DESIGN-SHARED.md](./DESIGN-SHARED.md) - MapInteractionConfig structure
- [DESIGN-SIMHOST.md](./DESIGN-SIMHOST.md) - Section 6: Critical Edge Cases
- [DESIGN-IG.md](./DESIGN-IG.md) - Section 9: Critical Edge Cases
- [DESIGN-IOS.md](./DESIGN-IOS.md) - Section 7: Critical Edge Cases
- [DESIGN-RUNNER.md](./DESIGN-RUNNER.md) - Section 9: Critical Edge Cases
- [TASK-TRACKER.md](./TASK-TRACKER.md) - Overall progress tracking

---

**Document Status**: ✅ Complete — All identified gaps documented with mitigations  
**Last Updated**: 2026-03-05  
**Review Required**: Before starting implementation of any phase
