# BUG2 — Bug-Fix Design Document

**Source:** [design-talk.md](./design-talk.md)  
**Task Detail:** [BUG2-TASK-DETAIL.md](./BUG2-TASK-DETAIL.md)  
**Tracker:** [BUG2-TASK-TRACKER.md](./BUG2-TASK-TRACKER.md)

---

## Overview

This document covers a second batch of bug fixes and small features discovered during interactive
testing of the IOS / IG / SimHost federated simulation stack. The issues span eight concern areas:

1. **Network Correctness** — duplicate system registration causing double ACKs; missing DDS sender
   identity tracking; orphaned WorldPos descriptor after entity deletion
2. **Mission System** — missing `DoctrineFinished` / `UnderAttack` trigger cases in message-to-ECS
   translation; no trigger selection UI in the task editor; unreadable task action buttons; missing
   version-conflict resolution UI in the mission panel
3. **IOS UI clean-up** — legacy tool combo still present in Map Configuration panel; ORBAT tree
   subordinates not indented
4. **IG Interaction** — no per-frame immediate drag mode when SHIFT is held
5. **Layer Visibility Enforcement** — invisible-layer entities remain selectable and show selection
   rings; the entity render layer ignores per-entity layer masks
6. **Tool Cursors** — no visual feedback when Measure tool and EntityPickerTool are waiting for
   the first user click
7. **Entity Deletion** — inspector context menus lack a networked Delete action; IOS DELETE context
   menu action is never executed
8. **Road Graph** — SimHost never renders the road graph because the loaded blob is silently
   discarded and the file path is hardcoded
9. **Architecture** — dual `Health` / `HealthData` components is a documented hack (DEBT-033);
   the clean fix is to move `Health` into `FDP.Toolkit.Combat.Contracts`

---

## Phase 1 — Network Correctness

### 1.1 Fix Duplicate UpdateEntityDescriptorRequestSystem Registration

**Files:** `Hrot.SimHost/SimHostApp.cs`

`SimHostApp._kernelGroup.AddSystem` calls are made once for every system that should run each
tick. A copy-paste error causes `UpdateEntityDescriptorRequestSystem` to be added **twice**. This
creates two DDS reader instances that both subscribe to the same topic and both pass the
`HasAuthority` check when the SimHost owns a descriptor (e.g. `dtWorldPos`). The result is two
identical ACK samples on the network per incoming request.

**Fix:** Remove the duplicate `AddSystem` line so the system is registered exactly once.

---

### 1.2 Add EnableSenderTracking to All DDS Participant Initializations

**Files:**  
- `Hrot.SimHost/SimHostApp.cs`  
- `Hrot.IG/IgApplication.cs`  
- `Hrot.ClusterRunner/Services/IosSubsystem.cs`  
- `FDP/Examples/Fdp.Examples.NetworkDemo/NetworkDemoApp.cs`

Every DDS participant must call `participant.EnableSenderTracking(new SenderIdentityConfig { ... })`
**before** any `DdsWriter` instances are created. Without this, the CycloneDDS C# bindings emit
samples without an attached identity blob. Because the `EntityMaster` ingress translator extracts
the `OwnerId` from the sender identity metadata, all owner resolution silently fails when this
call is missing.

The configuration values come from already-available variables at each call site:

| Application | `AppDomainId` | `AppInstanceId` |
|---|---|---|
| SimHost | `domainId` local var | `localNodeId` local var |
| IG | `domainId` param | `_effectiveInstanceId` field |
| IOS | `config.DomainId` | `config.NodeId` |
| NetworkDemo | `0` | `instanceId` local var |

**Fix:** Insert the `EnableSenderTracking` call immediately after each `CreateParticipant` /
`new DdsParticipant(...)` call, and before the first writer construction at the same call site.

---

### 1.3 Fix WorldPos Descriptor Disposal Leak

**Files:** `Hrot.Map.Common/Replication/Egress/WorldPosEgressTranslator.cs`

`WorldPosEgressTranslator` inherits from `CycloneTranslator<WorldPos, WorldPos>`, which
provides a virtual `Dispose(long networkEntityId)` that disposes only the primary generic topic
instance. The translator also owns a secondary private `_drWriter` for the `WorldPos` topic.
Because `Dispose` is never overridden, `_drWriter` is never tombstoned when an entity is deleted —
the `WorldPos` sample remains alive on the DDS network indefinitely.

**Verification:** A review of the other egress translators confirms this pattern is unique to
`WorldPosEgressTranslator`. All others either implement their own full `Dispose` or document that
no disposal is necessary (`NavigationIntentEgressTranslator`, `NavigationStatusEgressTranslator`).

**Fix:** Override `Dispose(long networkEntityId)` in `WorldPosEgressTranslator`:
1. Call `base.Dispose(networkEntityId)` to tombstone the primary `WorldPos` sample.
2. Call `_drWriter.DisposeInstance(new WorldPos { EntityId = (int)networkEntityId })` to
   tombstone the secondary `WorldPos` sample.

---

## Phase 2 — Mission System

### 2.1 Fix Missing ResolveTrigger Cases

**Files:**  
- `Hrot.SimHost/Systems/MissionControlRequestSystem.cs`  
- `Hrot.Map.Common/Translators/EntityMissionIngressTranslator.cs`

Both files contain a `ResolveTrigger` helper with a `switch` statement that only handles three of
the five documented trigger types:

| Trigger string | Expected mapping | Actual mapping |
|---|---|---|
| `"TimerElapsed"` | `EcsMissionTrigger.TimerElapsed` | ✓ handled |
| `"ReachedDestination"` | `EcsMissionTrigger.ReachedDestination` | ✓ handled |
| `"HealthCritical"` | `EcsMissionTrigger.HealthCritical` | ✓ handled |
| `"DoctrineFinished"` | `EcsMissionTrigger.DoctrineFinished` | ✗ falls to default |
| `"UnderAttack"` | `EcsMissionTrigger.UnderAttack` | ✗ falls to default |

The catch-all default returns `(EcsMissionTrigger.TimerElapsed, 0f)`. A `DoctrineFinished` trigger
therefore becomes a `TimerElapsed` trigger with a 0-second threshold, which fires immediately on
the first simulation tick. The `MissionDirectorSystem` advances the queue past the `MoveToLocation`
task before the locomotion pipeline can process it — the vehicle never moves.

This bug is highly visible because the `MissionPanel` UI defaults all newly created tasks to
`DoctrineFinished`.

**Fix:** Add `"DoctrineFinished"` and `"UnderAttack"` cases to the switch in both `ResolveTrigger`
methods.

---

### 2.2 Add Trigger Selection UI to MissionPanel

**Files:** `Hrot.ExCon/Panels/MissionPanel.cs`

The `MissionPanel` task-rendering loop displays the task type and behavior parameters but
completely skips the trigger definition. Operators can only see and edit trigger data indirectly.
Because the default trigger for new tasks is `DoctrineFinished` (which the backend currently
mis-handles — see §2.1), operators have no way to change it to `ReachedDestination` without
editing raw JSON externally.

#### Changes

1. **Add static trigger-type catalogue and default-params helper**

   ```csharp
   private static readonly string[] _triggerTypes = {
       "DoctrineFinished", "TimerElapsed", "ReachedDestination", "HealthCritical", "UnderAttack"
   };

   private static string GetDefaultTriggerParams(string triggerType) => triggerType switch
   {
       "TimerElapsed"   => "10.0",
       "HealthCritical" => "0.25",
       _                => ""
   };
   ```

2. **Add mutation handlers** `HandleEditTriggerType`, `HandleEditTriggerParams`, `HandleAddTrigger`
   — each modifies the draft task list safely via `TryGetDraftTasks`.

3. **Render the trigger UI** In the per-task `for` loop, after the `BehaviorParams` block:
   - If the task already has triggers: combo (`##TrigType{i}`) + input text (`##TrigParams{i}`) +
     "Default" button.
   - If no triggers: `+ Add Trigger` button (defaults to `DoctrineFinished`).

---

### 2.3 Fix Unreadable Mission Task Action Buttons

**Files:** `Hrot.ExCon/Panels/MissionPanel.cs`

The per-task Up/Down/Delete buttons use Unicode arrow and cross characters (`↑`, `↓`, `✕`). The
ImGui font atlas loaded by `rlImGui.Setup(darkTheme: true)` only includes the basic (ASCII)
character range, so these code-points render as empty boxes.

**Fix:** Replace the button labels with plain ASCII equivalents while preserving the `##{i}` ImGui
invisible-ID suffixes to maintain unique widget IDs:

- `$"↑##{i}"` → `$"Up##{i}"`
- `$"↓##{i}"` → `$"Down##{i}"`
- `$"✕##{i}"` → `$"Delete##{i}"`

---

### 2.4 Add Inline Version-Conflict Resolution to MissionPanel

**Files:** `Hrot.ExCon/Panels/MissionPanel.cs`

The Optimistic Concurrency Control (OCC) mechanism in `MissionControlRequestSystem` detects version
conflicts and sets `HasConflictAlert` / `ConflictMessage` on the panel, but the panel never renders
any corresponding UI. The operator has no way to know their commit was rejected, and no way to
discard their stale draft or force-overwrite the remote state.

Modals are not acceptable because they block the entire IOS application. The conflict indication
must be **inline** within the Mission Control panel.

#### Changes

1. **Add `HandleForceCommit` method** — identical to `HandleCommit` except it passes `baseVersion = 0`
   to `CommitMissionAsync`, which bypasses the OCC check in `MissionControlRequestSystem` (the
   `request.BaseVersion > 0` guard is intentional and documented).

2. **Replace bottom button bar with conditional rendering:**
   - When `HasConflictAlert` is true: show an inline red warning text, a **"Discard Draft (Reload)"**
     button (calls `ClearDraft()` + `DismissConflict()`), and a **"Force Commit (Overwrite)"** button
     (calls `HandleForceCommit`).
   - Otherwise: show the standard **Commit**, **Discard Draft** (visible only when `_draftPlan.HasValue`),
     **JUMP**, and **ABORT** controls.

---

## Phase 3 — IOS UI Clean-up

### 3.1 Remove Legacy Tool Combo from ConfigPanel

**Files:** `Hrot.ExCon/Panels/ConfigPanel.cs`

Map tools are now launched via explicit commands (`CMD_PLACE_ENTITY`, `CMD_START_AUTHORING`). The
`ConfigPanel` still carries dead-weight from the era when the active tool was set via the map
configuration JSON: a `Tools` static array, a `_selectedTool` backing field, a `SelectedTool`
property, an `interaction` JSON block in `BuildPatch()`, and an ImGui combo in `Draw()`.

**Fix:** Delete all of the above. After the removal, `BuildPatch()` should only include the `view`
block (icon scale + layer visibility flags).

---

### 3.2 Fix ORBAT Tree Indentation

**Files:** `Hrot.ExCon/Panels/OrbatPanel.cs`

`GetVisibleNodes` correctly computes a pre-flattened depth-first list where each `OrbatNode` carries
a `Depth` property. The rendering loop calls `ImGui.TreeNodeEx` + immediately `ImGui.TreePop()` for
every item. `TreePop()` reverses ImGui's internal indentation before the next item in the flat list
is rendered, so all items appear at the same root indent level regardless of their actual depth.

**Fix:** In the rendering loop, before drawing each node:
1. Calculate `float indentSpacing = node.Depth * ImGui.GetStyle().IndentSpacing`.
2. If `indentSpacing > 0`, call `ImGui.Indent(indentSpacing)`.
3. Draw the node exactly as before (`TreeNodeEx`, click handler, `TreePop`).
4. Call `ImGui.Unindent(indentSpacing)` to restore the cursor for the next item.

Using `ImGui.GetStyle().IndentSpacing` respects any global UI scaling applied to the application.

---

## Phase 4 — IG Interaction

### 4.1 Add Shift-Key Immediate Drag Mode

**Files:** `Hrot.IG/IgApplication.cs`

The `EntityDragTool` fires `OnEntityMoved` every frame the mouse is held and moving. The original
throttle implementation (`_continuousDragTimer`, `ContinuousDragIntervalSec = 0.1f`) sends at most
10 updates per second. A per-frame mode is needed for testing purposes where every position change
is immediately broadcast over DDS.

The existing `_userConfig.ContinuousDragUpdates` toggle retains its 10 Hz server-throttled
behavior for production use. Adding SHIFT-key detection as a separate unconditional path avoids
changing existing behavior.

**Fix:** Rewrite the `OnEntityMoved` lambda body:
1. Detect SHIFT via `Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift)`.
2. If `_userConfig.ContinuousDragUpdates`: apply the existing throttle+timer logic (keep unchanged).
3. If SHIFT is held (and ContinuousDragUpdates is false): call `SendWorldPosUpdate(entity, worldPos)`
   directly whenever `_lastDragWorldPos != worldPos` — no timer involved.
4. Update `_lastDragWorldPos = worldPos` unconditionally at the end.
5. Remove `_continuousDragTimer`, `_frameDt`, and `ContinuousDragIntervalSec` from the class — they
   are no longer needed once the throttle path is removed.

---

## Phase 5 — Layer Visibility Enforcement

### 5.1 Enforce Layer Visibility in Selection and Rendering

**Files:**  
- `FDP/Toolkits/FDP.Toolkit.Vis2D/Tools/BoxSelectionTool.cs`  
- `Hrot.IG/Systems/SelectionRenderSystem.cs`  
- `FDP/Toolkits/FDP.Toolkit.Vis2D/Layers/EntityRenderLayer.cs`  
- `Hrot.IG/IgApplication.cs`

When the operator turns off a map layer (e.g. Ground Units) three distinct subsystems fail to
respect the layer mask:

**a. BoxSelectionTool** iterates the raw ECS query without checking the entity's
`MapDisplayComponent.LayerMask` against the canvas's `ActiveLayerMask`. Entities on hidden layers
can be selected by drawing a box.

**b. SelectionRenderSystem** draws selection rings unconditionally for any entity where
`SelectionState.IsSelected == true`. Rings remain visible for entities on disabled layers.

**c. EntityRenderLayer** in `IgApplication.cs` is configured with `layerBitIndex: 0` (Ground
Units). This hardcodes a single-bit visibility check that prevents it from acting as a
general-purpose pass-through layer that cross-references each entity's individual mask against the
global canvas layer mask.

#### Fixes

**BoxSelectionTool:**
- Add private `_canvas` field.
- Capture `canvas` in `OnEnter(MapCanvas canvas)`, clear it in `OnExit()`.
- In `FinishSelection`, before adding an entity to the result: read
  `MapDisplayComponent.LayerMask`; skip if `(entityMask & activeMask) == 0`.

**SelectionRenderSystem:**
- In the `Draw` loop, after checking `sel.IsSelected`: read `MapDisplayComponent.LayerMask` if
  present; skip drawing the ring if `(entityMask & ctx.VisibleLayersMask) == 0`.

**EntityRenderLayer:**
- Add `public MapCanvas? Canvas { get; set; }` property.
- Rewrite `Draw`, `PickEntity`, and `HandleInput` to support `LayerBitIndex == -1` as a
  "catch-all" mode: when `-1`, skip the single-bit early-out and instead filter per entity using
  `(entityMask & ctx.VisibleLayersMask) == 0`.

**IgApplication.cs:**
- Change `layerBitIndex: 0` to `layerBitIndex: -1` and set `Canvas = _canvas` on the layer
  instance.

---

## Phase 6 — Tool Cursors

### 6.1 Add Crosshair Cursor to MeasureTool

**Files:** `Hrot.IG/Tools/MeasureTool.cs`

`MeasureTool.Draw` returns immediately when `!_startPoint.HasValue`. The operator has no visual
cue that the tool is active and waiting for a click. `HandleHover` already tracks `_currentPoint`,
so drawing a crosshair at that position is trivial.

**Fix:** In the `!_startPoint.HasValue` early-return block, draw a scalable crosshair using
`MeasureToolConstants.LineColor` and `MeasureToolConstants.LineThickness`, divided by `ctx.Zoom`
to keep the cursor screen-space-constant as the operator zooms.

---

### 6.2 Add Crosshair Cursor to EntityPickerTool

**Files:** `FDP/Toolkits/FDP.Toolkit.Vis2D/Tools/EntityPickerTool.cs`

When a `FollowRoute` task is assigned and the operator clicks "Pick Route", the IG activates
`EntityPickerTool`. The tool does not implement `Draw`, so the cursor gives no feedback that a
pick operation is in progress. The cursor should be orange/amber by default and turn red when the
cursor hovers over a valid pickable target.

**Fix:** Implement `public void Draw(RenderContext ctx)` using the same crosshair-and-circle
geometry as the entity picker crosshair documented in `MapCommandRequest`. The color switches to
`Color(255, 0, 0, 255)` when `_hoveredEntity.HasValue`, otherwise `Color(255, 161, 0, 255)`.

---

## Phase 7 — Entity Deletion

### 7.1 Add Delete to Inspector Context Menus

**Files:**  
- `Hrot.SimHost/SimHostVisualization.cs`  
- `Hrot.IG/IgApplication.cs`

Both the SimHost and IG inspector panels render a context menu via
`LambdaEntityContextMenuHandler`. Neither currently exposes a Delete action. The deletion must go
through the Entity Lifecycle Module (ELM): publish a `DestroyEntityCommand` so the
`NetworkSpawningSystem` performs the authoritative teardown and broadcasts a DDS `EntityMaster`
DISPOSE to all peers, rather than locally calling `DestroyEntity()` directly.

**Fix:** In each handler's lambda, add a separator and a `"Delete entity"` item that:
1. Checks `repo.IsAlive(entity)` (SimHost) / `_world.IsAlive(entity)` (IG).
2. If the entity has `NetworkIdentity`, publishes `DestroyEntityCommand { NetworkId, Reason }`.
3. Otherwise calls local `DestroyEntity(entity)` (local-only entities have no ELM).
4. Clears the selection state if the deleted entity was selected.

---

### 7.2 Wire IOS DELETE Context Action to IG-Side ELM Deletion

**Files:**  
- `Hrot.IG/Translators/ContextActionsUpdateTranslator.cs`  
- `Hrot.IG/IgApplication.cs`

The IOS registers a DELETE item in its context menu extension using numeric action ID `10`
(`ContextMenuActions.Delete`). The IG's `ContextActionsUpdateTranslator.ParseActions` converts
numeric IDs to string action names; the only mapped case currently is `1 => "IG_CenterOnEntity"`.
Because `10` falls through to the integer-to-string default, the router in `IgApplication` treats
it as an unknown string and does nothing.

**Fix:**
1. In `ParseActions`, add `10 => "IG_DeleteEntity"` to the numeric-ID switch.
2. In `ExecuteLocalContextAction`, add a `"IG_DeleteEntity"` case that publishes
   `DestroyEntityCommand` (identical logic to §7.1 IG inspector handler).

The IOS continues to act as a pure **menu provider** without any execution responsibility.

---

## Phase 8 — Road Graph

### 8.1 Fix SimHost Road Graph Rendering

**Files:**  
- `Hrot.SimHost/Modules/SimulationLogicModule.cs`  
- `Hrot.SimHost/SimHostApp.cs`

Two independent bugs prevent the road graph from rendering on the SimHost:

**a. Blob silently discarded in SimulationLogicModule**  
`public RoadNetworkBlob RoadNetwork => default;` is a hardcoded property that always returns an
empty struct, discarding any `roadNetwork` value passed to the constructor. The visualization layer
therefore always receives an empty network.

**Fix:** Convert the property to a proper auto-property and assign it from the `roadNetwork`
constructor parameter:
```csharp
public RoadNetworkBlob RoadNetwork { get; }
// ...
public SimulationLogicModule(..., RoadNetworkBlob roadNetwork = default, ...)
{
    RoadNetwork = roadNetwork;
    // ...
}
```

**b. Hardcoded relative file path in SimHostApp**  
`SimHostApp` loads the road network from the hardcoded string `"Assets/sample_road.json"` which
resolves relative to the process working directory. When launched from the Hrot.ClusterRunner project
folder the file is not found and the silent `catch` swallows the error.

**Fix:** Use `nodeConfig.RoadNetworkBlobPath` (already present in the node configuration) instead
of the hardcoded string, and replace the silent `catch` with a warning-level log entry so failures
are visible without crashing the application.

---

## Phase 9 — Architecture

### 9.1 Consolidate Health into FDP.Toolkit.Combat.Contracts

**Files:**  
- `FDP/Toolkits/FDP.Toolkit.Combat.Contracts/` (target location for unified `Health`)  
- `FDP/Toolkits/FDP.Toolkit.Combat/Components/Health.cs` (move source)  
- `Fdp.Kernel/Components/HealthData.cs` (delete)  
- `FDP/Toolkits/FDP.Toolkit.Combat/Systems/DamageSystem.cs` (remove mirror sync)  
- `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/MissionDirectorSystem.cs` (update reference)  
- `FDP/Toolkits/FDP.Toolkit.Behavior/FDP.Toolkit.Behavior.csproj` (add project reference)

The current architecture uses two components to break a circular dependency (DEBT-033):
- `Health` in `FDP.Toolkit.Combat` — the live HP pool mutated by `DamageSystem`.
- `HealthData` in `Fdp.Kernel` — a read-only mirror written by `DamageSystem` so that
  `MissionDirectorSystem` (Behavior toolkit) can read health without a Combat → Behavior
  circular reference.

`FDP.Toolkit.Combat.Contracts` already exists (it was created for the `HitEvent` fix, DEBT-031).
Both the Behavior and Combat toolkits can reference it, eliminating the circular dependency.

**Fix:**
1. Move `Health` component definition into `FDP.Toolkit.Combat.Contracts`.
2. Delete `HealthData` from `Fdp.Kernel`.
3. Add a `<ProjectReference>` from `FDP.Toolkit.Behavior` to `FDP.Toolkit.Combat.Contracts`.
4. In `DamageSystem`, delete the entire `HealthData` mirror-sync block (documented as DEBT-033).
5. In `MissionDirectorSystem`, update the `HealthCritical` trigger evaluation to read `Health`
   directly instead of `HealthData`.
6. Resolve all remaining `using`/`namespace` references across the solution.
