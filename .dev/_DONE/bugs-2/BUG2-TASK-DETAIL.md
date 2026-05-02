# BUG2 — Task Detail Document

**Design Reference:** [BUG2-DESIGN.md](./BUG2-DESIGN.md)  
**Tracker:** [BUG2-TASK-TRACKER.md](./BUG2-TASK-TRACKER.md)

Each task references the relevant design section for architecture context. This document focuses
on **what to change** and **how to verify** it.

---

## Phase 1 — Network Correctness

---

### BUG2-N001 Fix Duplicate UpdateEntityDescriptorRequestSystem Registration

**Design ref:** [§1.1](./BUG2-DESIGN.md#11-fix-duplicate-updateentitydescriptorrequestsystem-registration)

**Files to change:**  
- `Hrot.SimHost/SimHostApp.cs`

#### What to do

Locate the block in `SimHostApp` where `_kernelGroup.AddSystem` is called for
`UpdateEntityDescriptorRequestSystem`. The system is added twice in sequence. Delete the
**second** occurrence so it is registered exactly once.

The duplicate registration looks like:

```csharp
_kernelGroup.AddSystem(new UpdateEntityDescriptorRequestSystem(ddsParticipant, entityMap, wgs84));
_kernelGroup.AddSystem(new UpdateEntityAttributeRequestSystem(ddsParticipant, entityMap, wgs84, jsonAttributeCompiler));
_kernelGroup.AddSystem(new UpdateEntityDescriptorRequestSystem(ddsParticipant, entityMap, wgs84)); // DELETE THIS LINE
```

#### Success conditions

- Unit test `SimHostAppTests.RegisteredSystemTypes_ContainsNoDuplicates` (new): introspect the
  `_kernelGroup` system list and assert that `UpdateEntityDescriptorRequestSystem` appears
  exactly once.
- Integration test: when a single `UpdateEntityDescriptorRequest` arrives for a descriptor owned
  by the SimHost, exactly **one** ACK sample is observed on the network.

---

### BUG2-N002 Add EnableSenderTracking to All DDS Participant Initializations

**Design ref:** [§1.2](./BUG2-DESIGN.md#12-add-enablesendertracking-to-all-dds-participant-initializations)

**Files to change:**  
- `Hrot.SimHost/SimHostApp.cs`  
- `Hrot.IG/IgApplication.cs`  
- `Hrot.ClusterRunner/Services/IosSubsystem.cs`  
- `FDP/Examples/Fdp.Examples.NetworkDemo/NetworkDemoApp.cs`

#### What to do

In each file, find the call to `HrotEnvironment.CreateParticipant(...)` or
`new DdsParticipant(...)`. Immediately **after** that call and **before** any `DdsWriter` or
`DdsReader` construction, insert:

```csharp
participant.EnableSenderTracking(new SenderIdentityConfig
{
    AppDomainId  = <domainId>,
    AppInstanceId = <instanceId>
});
```

Use the values documented in the design (see §1.2 table).

#### Success conditions

- `EntityMasterIngressTranslatorTests.ProcessSample_WithSenderTracking_SetsOwnerId` (new test or
  update existing): create a fake `DdsSample<EntityMaster>` with a populated `SenderInfo` and
  assert that the ingress translator correctly extracts `OwnerId` from it without falling back to
  a default/zero value.
- No assertion-failure or warning log line containing "sender id" or "owner id" appears during
  an integration run where all three processes are started.

---

### BUG2-N003 Fix WorldPos Descriptor Disposal Leak

**Design ref:** [§1.3](./BUG2-DESIGN.md#13-fix-geospatialdr-descriptor-disposal-leak)

**Files to change:**  
- `Hrot.Map.Common/Replication/Egress/WorldPosEgressTranslator.cs`

#### What to do

Add the following override to `WorldPosEgressTranslator`:

```csharp
public override void Dispose(long networkEntityId)
{
    base.Dispose(networkEntityId);         // tombstones primary WorldPos topic
    _drWriter.DisposeInstance(new WorldPos { EntityId = (int)networkEntityId });
}
```

#### Success conditions

- `WorldPosEgressTranslatorTests.Dispose_CallsDisposeOnDrWriter` (new): mock `_drWriter`;
  call `Dispose(42L)`; verify the mock received `DisposeInstance` with `EntityId == 42`.
- `WorldPosEgressTranslatorTests.Dispose_AlsoCallsBaseDispose` (new): verify the base
  translator's `DisposeInstance` is also invoked.

---

## Phase 2 — Mission System

---

### BUG2-M001 Fix Missing ResolveTrigger Cases

**Design ref:** [§2.1](./BUG2-DESIGN.md#21-fix-missing-resolvetrigger-cases)

**Files to change:**  
- `Hrot.SimHost/Systems/MissionControlRequestSystem.cs`  
- `Hrot.Map.Common/Translators/EntityMissionIngressTranslator.cs`

#### What to do

In both files, locate the `ResolveTrigger` switch and add:

```csharp
"BehaviorFinished" => (EcsMissionTrigger.BehaviorFinished, 0f),
"UnderAttack"      => (EcsMissionTrigger.UnderAttack,      0f),
```

Ensure the default/catch-all case has a comment explaining that unknown trigger strings still
fall back to `TimerElapsed(0f)` as a safe observable failure mode.

#### Success conditions

- `EntityMissionIngressTranslatorTests.ResolveTrigger_BehaviorFinished_ReturnsCorrectEnum` (new):
  pass a payload containing `"BehaviorFinished"` and assert the translated `EcsMissionTrigger`
  equals `BehaviorFinished`.
- `EntityMissionIngressTranslatorTests.ResolveTrigger_UnderAttack_ReturnsCorrectEnum` (new).
- `MissionControlRequestSystemTests.ResolveTrigger_BehaviorFinished_ReturnsCorrectEnum` (new,
  mirrored for the SimHost version).
- `MissionControlRequestSystemTests.ResolveTrigger_UnderAttack_ReturnsCorrectEnum` (new).
- Existing tests for `TimerElapsed`, `ReachedDestination`, `HealthCritical` continue to pass.

---

### BUG2-M002 Add Trigger Selection UI to MissionPanel

**Design ref:** [§2.2](./BUG2-DESIGN.md#22-add-trigger-selection-ui-to-missionpanel)

**Files to change:**  
- `Hrot.ExCon/Panels/MissionPanel.cs`

#### What to do

1. At the top of the `MissionPanel` class, add:

   ```csharp
   private static readonly string[] _triggerTypes =
   {
       "BehaviorFinished", "TimerElapsed", "ReachedDestination", "HealthCritical", "UnderAttack"
   };

   private static string GetDefaultTriggerParams(string triggerType) => triggerType switch
   {
       "TimerElapsed"   => "10.0",
       "HealthCritical" => "0.25",
       _                => ""
   };
   ```

2. Add these methods (inside the "draft editing handlers" region or grouped with existing
   handler methods):

   ```csharp
   public void HandleEditTriggerType(int taskIndex, int triggerIndex, string newType)
   {
       if (!TryGetDraftTasks(out var tasks)) return;
       if (taskIndex < 0 || taskIndex >= tasks.Count) return;
       var task = tasks[taskIndex];
       if (task.Triggers != null && triggerIndex >= 0 && triggerIndex < task.Triggers.Count)
       {
           var trigger = task.Triggers[triggerIndex];
           trigger.Type   = newType;
           trigger.Params = GetDefaultTriggerParams(newType);
           task.Triggers[triggerIndex] = trigger;
           tasks[taskIndex] = task;
       }
   }

   public void HandleEditTriggerParams(int taskIndex, int triggerIndex, string newParams)
   {
       if (!TryGetDraftTasks(out var tasks)) return;
       if (taskIndex < 0 || taskIndex >= tasks.Count) return;
       var task = tasks[taskIndex];
       if (task.Triggers != null && triggerIndex >= 0 && triggerIndex < task.Triggers.Count)
       {
           var trigger = task.Triggers[triggerIndex];
           trigger.Params = newParams ?? string.Empty;
           task.Triggers[triggerIndex] = trigger;
           tasks[taskIndex] = task;
       }
   }

   public void HandleAddTrigger(int taskIndex, string type)
   {
       if (!TryGetDraftTasks(out var tasks)) return;
       if (taskIndex < 0 || taskIndex >= tasks.Count) return;
       var task = tasks[taskIndex];
       task.Triggers ??= new List<Hrot.NED.Descriptors.MissionTrigger>();
       task.Triggers.Add(new Hrot.NED.Descriptors.MissionTrigger
       {
           Type   = type,
           Params = GetDefaultTriggerParams(type)
       });
       tasks[taskIndex] = task;
   }
   ```

3. In the `for (int i = 0; i < planToShow.Tasks.Count; i++)` loop inside `Draw`, after the
   `BehaviorParams` block and before the Up/Down/Delete buttons, insert:

   ```csharp
   if (task.Triggers != null && task.Triggers.Count > 0)
   {
       var trigger      = task.Triggers[0];
       string trigType  = trigger.Type   ?? "BehaviorFinished";
       string trigParam = trigger.Params ?? string.Empty;

       ImGui.Text("Trigger:");
       ImGui.SameLine();
       ImGui.SetNextItemWidth(150f);
       if (ImGui.BeginCombo($"##TrigType{i}", trigType))
       {
           for (int t = 0; t < _triggerTypes.Length; t++)
           {
               bool isSel = trigType == _triggerTypes[t];
               if (ImGui.Selectable(_triggerTypes[t], isSel))
                   HandleEditTriggerType(i, 0, _triggerTypes[t]);
               if (isSel) ImGui.SetItemDefaultFocus();
           }
           ImGui.EndCombo();
       }
       ImGui.SameLine();
       ImGui.SetNextItemWidth(120f);
       if (ImGui.InputText($"##TrigParams{i}", ref trigParam, 1024))
           HandleEditTriggerParams(i, 0, trigParam);
       ImGui.SameLine();
       if (ImGui.Button($"Default##TrigDef{i}"))
           HandleEditTriggerParams(i, 0, GetDefaultTriggerParams(trigType));
   }
   else
   {
       if (ImGui.Button($"+ Add Trigger##{i}"))
           HandleAddTrigger(i, "BehaviorFinished");
   }
   ```

#### Success conditions

- `MissionPanelTests.HandleEditTriggerType_UpdatesTriggerInDraft` (new): create a draft with one
  task that has a `BehaviorFinished` trigger; call `HandleEditTriggerType(0, 0, "TimerElapsed")`;
  assert the trigger type is updated and params are set to `"10.0"`.
- `MissionPanelTests.HandleEditTriggerParams_UpdatesParamsInDraft` (new).
- `MissionPanelTests.HandleAddTrigger_AddsBehaviorFinishedTrigger` (new): call
  `HandleAddTrigger(0, "BehaviorFinished")` on a task with no triggers; assert one trigger is
  added with type `BehaviorFinished` and empty params.
- `GetDefaultTriggerParams_KnownTypes_ReturnExpectedDefaults` (new): parameterized test
  verifying each branch of the switch.

---

### BUG2-M003 Fix Unreadable Mission Task Action Buttons

**Design ref:** [§2.3](./BUG2-DESIGN.md#23-fix-unreadable-mission-task-action-buttons)

**Files to change:**  
- `Hrot.ExCon/Panels/MissionPanel.cs`

#### What to do

In the per-task `for` loop inside `Draw`, replace the three symbol buttons:

```csharp
// Before                           // After
$"↑##{i}"    →    $"Up##{i}"
$"↓##{i}"    →    $"Down##{i}"
$"✕##{i}"    →    $"Delete##{i}"
```

#### Success conditions

- Code review: confirm no non-ASCII characters remain in button label strings in
  `MissionPanel.cs`.
- Manual verification: run the IOS standalone; open a mission plan; confirm task Up, Down, and
  Delete buttons display readable labels.

---

### BUG2-M004 Add Inline Version-Conflict Resolution to MissionPanel

**Design ref:** [§2.4](./BUG2-DESIGN.md#24-add-inline-version-conflict-resolution-to-missionpanel)

**Files to change:**  
- `Hrot.ExCon/Panels/MissionPanel.cs`

#### What to do

1. Add `HandleForceCommit`:

   ```csharp
   public void HandleForceCommit(IIosLogic logic)
   {
       ArgumentNullException.ThrowIfNull(logic);
       if (!CanCommit) return;
       var plan = _draftPlan!.Value;
       FdpLog<MissionPanel>.Info("[IOS] Force Commit: entity={0} tasks={1}",
           _selectedEntityId, plan.Tasks?.Count ?? 0);
       _pendingCommit  = logic.MissionEditorService.CommitMissionAsync(_selectedEntityId, plan, 0);
       _commitInFlight = true;
       DismissConflict();
   }
   ```

2. Replace the bottom control block in `Draw` with the conditional rendering:

   ```csharp
   if (HasConflictAlert)
   {
       ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f),
           "⚠ Conflict: Mission plan was modified by another operator!");
       if (ImGui.Button("Discard Draft (Reload)"))
       {
           ClearDraft();
           DismissConflict();
       }
       ImGui.SameLine();
       if (ImGui.Button("Force Commit (Overwrite)"))
           HandleForceCommit(logic);
   }
   else
   {
       bool commitEnabled = CommitButtonEnabled;
       if (!commitEnabled) ImGui.BeginDisabled();
       if (ImGui.Button("Commit")) HandleCommit(logic);
       if (!commitEnabled) ImGui.EndDisabled();

       if (_draftPlan.HasValue)
       {
           ImGui.SameLine();
           if (ImGui.Button("Discard Draft")) ClearDraft();
       }
       if (ImGui.Button("JUMP"))  HandleJump(logic);
       ImGui.SameLine();
       if (ImGui.Button("ABORT")) HandleAbort(logic);
   }
   ```

#### Success conditions

- `MissionPanelTests.HandleForceCommit_SendsWithBaseVersionZero` (new): set up a mock
  `MissionEditorService`; call `HandleForceCommit`; assert `CommitMissionAsync` was called with
  `baseVersion == 0`.
- `MissionPanelTests.ConflictState_ShowsConflictButtonsNotCommit` (new): set `HasConflictAlert =
  true` via `SimulateConflict()`; verify that "Force Commit (Overwrite)" and
  "Discard Draft (Reload)" are shown.
- `MissionPanelTests.DiscardDraft_ClearsConflictAndDraft` (new): trigger conflict, click discard;
  assert both `HasConflictAlert` and `_draftPlan.HasValue` become false.

---

## Phase 3 — IOS UI Clean-up

---

### BUG2-U001 Remove Legacy Tool Combo from ConfigPanel

**Design ref:** [§3.1](./BUG2-DESIGN.md#31-remove-legacy-tool-combo-from-configpanel)

**Files to change:**  
- `Hrot.ExCon/Panels/ConfigPanel.cs`

#### What to do

Delete the following from `ConfigPanel.cs`:

1. `public static readonly string[] Tools = { "Navigation", "Selection", "Placement", "Measure" };`
2. `private int _selectedTool = 0;`
3. The `SelectedTool` property.
4. In `BuildPatch()`: the entire `interaction = new { activeTool = Tools[_selectedTool] }` line
   (or the surrounding object initialiser element if it is an anonymous type member).
5. In `Draw()`: `ImGui.Combo("Tool", ref _selectedTool, Tools, Tools.Length);` and any surrounding
   layout calls (e.g. `ImGui.SameLine()` that exists solely because of the combo).

After removal, `BuildPatch()` should produce a JSON object with only the `view` key.

#### Success conditions

- `ConfigPanelTests.BuildPatch_DoesNotContainInteractionKey` (new): call `BuildPatch()` and
  deserialize; assert the resulting JSON does not have an `"interaction"` top-level key.
- `ConfigPanelTests.NoToolsField` (new): confirm `ConfigPanel` no longer exposes a public
  `Tools` static array via reflection.
- Build passes with no reference to `_selectedTool` or `SelectedTool` in the file.

---

### BUG2-U002 Fix ORBAT Tree Indentation

**Design ref:** [§3.2](./BUG2-DESIGN.md#32-fix-orbat-tree-indentation)

**Files to change:**  
- `Hrot.ExCon/Panels/OrbatPanel.cs`

#### What to do

In the `Draw(IIosLogic logic)` method, locate the `foreach (var node in nodes)` loop. Wrap the
body with indent/unindent calls:

```csharp
foreach (var node in nodes)
{
    float indent = node.Depth * ImGui.GetStyle().IndentSpacing;
    if (indent > 0) ImGui.Indent(indent);

    var flags = node.HasChildren ? ImGuiTreeNodeFlags.OpenOnArrow : ImGuiTreeNodeFlags.Leaf;
    bool open = ImGui.TreeNodeEx($"{node.Name} ({node.EntityId})", flags);

    if (ImGui.IsItemClicked()) HandleEntityClick(node.EntityId, logic);

    if (open)
    {
        if (!_expandedNodes.Contains(node.EntityId)) ToggleExpanded(node.EntityId);
        ImGui.TreePop();
    }
    else if (_expandedNodes.Contains(node.EntityId))
    {
        ToggleExpanded(node.EntityId);
    }

    if (indent > 0) ImGui.Unindent(indent);
}
```

Do not change `GetVisibleNodes` — it already produces correct `Depth` values.

#### Success conditions

- `OrbatPanelTests.GetVisibleNodes_SubordinateHasGreaterDepth` (new or verify existing): assert
  that a child node's `Depth` is strictly greater than its parent's `Depth`.
- Manual verification: run the IOS standalone with a multi-level ORBAT; confirm child units are
  visually indented beneath their commanders.

---

## Phase 4 — IG Interaction

---

### BUG2-I001 Add Shift-Key Immediate Drag Mode

**Design ref:** [§4.1](./BUG2-DESIGN.md#41-add-shift-key-immediate-drag-mode)

**Files to change:**  
- `Hrot.IG/IgApplication.cs`

#### What to do

1. In `InitializeNetwork`, locate the `interactionTool.OnEntityMoved` lambda and replace the
   body:

   ```csharp
   interactionTool.OnEntityMoved += (entity, worldPos) =>
   {
       bool isShiftHeld = Raylib.IsKeyDown(KeyboardKey.LeftShift)
                       || Raylib.IsKeyDown(KeyboardKey.RightShift);

       if (_userConfig.ContinuousDragUpdates)
       {
           // Existing throttle path: keep unchanged
           _continuousDragTimer += _frameDt;
           if (_continuousDragTimer >= ContinuousDragIntervalSec)
           {
               SendWorldPosUpdate(entity, worldPos);
               _continuousDragTimer = 0f;
           }
       }
       else if (isShiftHeld && _lastDragWorldPos != worldPos)
       {
           SendWorldPosUpdate(entity, worldPos);
       }

       _lastDragWorldPos = worldPos;
   };
   ```

   > **Note:** The design talk originally specified removing the timer fields entirely, assuming
   > the `ContinuousDragUpdates` throttle path would also be removed. Retain the throttle path
   > and its fields to avoid breaking the existing production toggle. Only the SHIFT path is
   > new and timer-free.

#### Success conditions

- `ContinuousDragTests.OnEntityMoved_ShiftHeld_PositionChanged_SendsUpdate` (new): mock
  `SendWorldPosUpdate`; simulate `isShiftHeld = true`, distinct start/end positions; assert
  `SendWorldPosUpdate` is called once.
- `ContinuousDragTests.OnEntityMoved_ShiftHeld_SamePosition_DoesNotSend` (new): same position
  repeated; assert no call.
- `ContinuousDragTests.OnEntityMoved_ShiftNotHeld_ContinuousDragDisabled_DoesNotSend` (new).
- Existing `ContinuousDragTests` (throttle path) continue to pass.

---

## Phase 5 — Layer Visibility Enforcement

---

### BUG2-V001 Enforce Layer Visibility in Selection and Rendering

**Design ref:** [§5.1](./BUG2-DESIGN.md#51-enforce-layer-visibility-in-selection-and-rendering)

**Files to change:**  
- `FDP/Toolkits/FDP.Toolkit.Vis2D/Tools/BoxSelectionTool.cs`  
- `Hrot.IG/Systems/SelectionRenderSystem.cs`  
- `FDP/Toolkits/FDP.Toolkit.Vis2D/Layers/EntityRenderLayer.cs`  
- `Hrot.IG/IgApplication.cs`

#### What to do

**BoxSelectionTool.cs:**

1. Add `private MapCanvas? _canvas;`.
2. In `OnEnter(MapCanvas canvas)`: set `_canvas = canvas; _isActive = true;`.
3. In `OnExit()`: set `_canvas = null; _isActive = false;`.
4. In `FinishSelection()`, before adding an entity to `selected`:
   ```csharp
   uint activeMask = _canvas?.ActiveLayerMask ?? 0xFFFFFFFF;
   // inside entity loop:
   if (_view.HasComponent<MapDisplayComponent>(entity))
   {
       uint em = _view.GetComponentRO<MapDisplayComponent>(entity).LayerMask;
       if ((em & activeMask) == 0) continue;
   }
   ```

**SelectionRenderSystem.cs:**

In the `Draw` loop, after `if (!sel.IsSelected) continue;`:
```csharp
if (_view.HasComponent<MapDisplayComponent>(entity))
{
    uint em = _view.GetComponentRO<MapDisplayComponent>(entity).LayerMask;
    if ((em & ctx.VisibleLayersMask) == 0) continue;
}
```

**EntityRenderLayer.cs:**

1. Add `public MapCanvas? Canvas { get; set; }`.
2. In `Draw`, `PickEntity`, and `HandleInput` — replace the existing single-bit early return with:
   ```csharp
   if (LayerBitIndex >= 0)
   {
       uint maskBit = 1u << LayerBitIndex;
       if ((ctx.VisibleLayersMask & maskBit) == 0) return; // whole layer off
   }
   // inside entity loop:
   uint entityMask = _view.HasComponent<MapDisplayComponent>(entity)
       ? _view.GetComponentRO<MapDisplayComponent>(entity).LayerMask
       : 1u;
   if ((entityMask & ctx.VisibleLayersMask) == 0) continue;
   if (LayerBitIndex >= 0)
   {
       uint bit = 1u << LayerBitIndex;
       if ((entityMask & bit) == 0) continue;
   }
   ```

**IgApplication.cs:**

In `InitializeEcs`, change:
```csharp
// Before:
var layer = new EntityRenderLayer("Entities", layerBitIndex: 0, ...);
// After:
var layer = new EntityRenderLayer("Entities", layerBitIndex: -1, ...) { Canvas = _canvas };
```

#### Success conditions

- `BoxSelectionToolTests.FinishSelection_HiddenLayerEntities_NotIncluded` (new): set
  `ActiveLayerMask` to exclude entity layer; run selection; assert entity is absent from result.
- `BoxSelectionToolTests.FinishSelection_VisibleLayerEntities_Included` (new): include mask set;
  assert entity is present.
- `SelectionRenderSystemTests.Draw_HiddenLayerEntity_DoesNotRenderRing` (new): verify
  `Raylib.DrawCircle` / selection ring draw call is not made for entity with masked-out layer.
- `EntityRenderLayerTests.Draw_CatchAllMode_HiddenEntities_Skipped` (new): `layerBitIndex = -1`,
  entity with `LayerMask = 0x1`, canvas `VisibleLayersMask = 0x2`; assert entity is not rendered.

---

## Phase 6 — Tool Cursors

---

### BUG2-T001 Add Crosshair Cursor to MeasureTool

**Design ref:** [§6.1](./BUG2-DESIGN.md#61-add-crosshair-cursor-to-measuretool)

**Files to change:**  
- `Hrot.IG/Tools/MeasureTool.cs`

#### What to do

In `Draw(RenderContext ctx)`, replace the bare `return` in the `!_startPoint.HasValue` branch with
crosshair rendering:

```csharp
if (!_startPoint.HasValue)
{
    float zoom  = ctx.Zoom > 0 ? ctx.Zoom : 1f;
    float size  = 14f / zoom;
    float gap   = 5f  / zoom;
    float thick = MeasureToolConstants.LineThickness / zoom;
    Color color = MeasureToolConstants.LineColor;
    var pos     = _currentPoint;

    Raylib.DrawLineEx(new Vector2(pos.X - size, pos.Y), new Vector2(pos.X - gap,  pos.Y), thick, color);
    Raylib.DrawLineEx(new Vector2(pos.X + gap,  pos.Y), new Vector2(pos.X + size, pos.Y), thick, color);
    Raylib.DrawLineEx(new Vector2(pos.X, pos.Y - size), new Vector2(pos.X, pos.Y - gap),  thick, color);
    Raylib.DrawLineEx(new Vector2(pos.X, pos.Y + gap),  new Vector2(pos.X, pos.Y + size), thick, color);
    Raylib.DrawCircleLinesV(pos, gap, color);
    return;
}
// ... existing measurement-line rendering unchanged
```

#### Success conditions

- `MeasureToolTests.Draw_NoStartPoint_DoesNotThrow` (ensure existing or new): call `Draw` with
  no start point set; confirm no exception.
- `MeasureToolTests.Draw_NoStartPoint_DrawsCrosshair` (new): inject mock/spy Raylib draw calls;
  assert at least four line draws and one circle draw occur when start point is null.
- `MeasureToolTests.Draw_WithStartPoint_DrawsMeasurementLine` (existing): confirm unchanged.

---

### BUG2-T002 Add Crosshair Cursor to EntityPickerTool

**Design ref:** [§6.2](./BUG2-DESIGN.md#62-add-crosshair-cursor-to-entitypickertool)

**Files to change:**  
- `FDP/Toolkits/FDP.Toolkit.Vis2D/Tools/EntityPickerTool.cs`

#### What to do

Add the `Draw(RenderContext ctx)` method to render an orange crosshair that turns red when the
mouse is hovering over a valid pickable entity:

```csharp
public void Draw(RenderContext ctx)
{
    var pos   = _currentMousePos;
    float zoom  = ctx.Zoom > 0 ? ctx.Zoom : 1f;
    float size  = 10f / zoom;
    float gap   = 3f  / zoom;
    float thick = 2f  / zoom;
    Color color = _hoveredEntity.HasValue
        ? new Color(255, 0,   0,   255)  // red   = valid target hovered
        : new Color(255, 161, 0,   255); // amber = waiting for pick

    Raylib.DrawLineEx(new Vector2(pos.X - size, pos.Y), new Vector2(pos.X - gap,  pos.Y), thick, color);
    Raylib.DrawLineEx(new Vector2(pos.X + gap,  pos.Y), new Vector2(pos.X + size, pos.Y), thick, color);
    Raylib.DrawLineEx(new Vector2(pos.X, pos.Y - size), new Vector2(pos.X, pos.Y - gap),  thick, color);
    Raylib.DrawLineEx(new Vector2(pos.X, pos.Y + gap),  new Vector2(pos.X, pos.Y + size), thick, color);
    Raylib.DrawCircleLinesV(pos, gap, color);
}
```

#### Success conditions

- `EntityPickerToolTests.Draw_NoHoveredEntity_DrawsAmberCrosshair` (new): assert draw calls use
  `Color(255, 161, 0, 255)`.
- `EntityPickerToolTests.Draw_HoveredEntity_DrawsRedCrosshair` (new): set `_hoveredEntity`;
  assert draw calls use `Color(255, 0, 0, 255)`.

---

## Phase 7 — Entity Deletion

---

### BUG2-E001 Add Delete to Inspector Context Menus

**Design ref:** [§7.1](./BUG2-DESIGN.md#71-add-delete-to-inspector-context-menus)

**Files to change:**  
- `Hrot.SimHost/SimHostVisualization.cs`  
- `Hrot.IG/IgApplication.cs`

#### What to do

In both files, locate the `LambdaEntityContextMenuHandler` lambda passed to
`_fdpEntityInspector.RegisterContextMenuHandler(...)`. Add to the builder:

```csharp
builder.AddSeparator();
builder.AddItem("Delete entity", () =>
{
    if (world.IsAlive(entity))
    {
        if (world.HasComponent<NetworkIdentity>(entity))
        {
            ref readonly var netId = ref world.GetComponentRO<NetworkIdentity>(entity);
            world.Bus.PublishManaged(new DestroyEntityCommand
            {
                NetworkId = netId.Value,
                Reason    = "inspector-deleted"
            });
        }
        else
        {
            world.DestroyEntity(entity);
        }

        if (selection.Contains(entity))
        {
            selection.Remove(entity);
            inspectorState.SelectedEntity = null;
        }
    }
});
```

Use the appropriate local variable names for `world`, `selection`, and `inspectorState` at each
call site.

#### Success conditions

- `EntityInspectorContextMenuTests.DeleteNetworkedEntity_PublishesDestroyEntityCommand` (new):
  register handler; invoke "Delete entity" for an entity with `NetworkIdentity`; assert
  `DestroyEntityCommand` was published to the bus with the correct `NetworkId`.
- `EntityInspectorContextMenuTests.DeleteLocalEntity_CallsDestroyEntity` (new): entity without
  `NetworkIdentity`; assert `DestroyEntity` is called directly.
- `EntityInspectorContextMenuTests.DeleteSelectedEntity_ClearsSelection` (new): entity is
  selected; invoke delete; assert selection is cleared.

---

### BUG2-E002 Wire IOS DELETE Context Action to IG-Side ELM Deletion

**Design ref:** [§7.2](./BUG2-DESIGN.md#72-wire-ios-delete-context-action-to-ig-side-elm-deletion)

**Files to change:**  
- `Hrot.IG/Translators/ContextActionsUpdateTranslator.cs`  
- `Hrot.IG/IgApplication.cs`

#### What to do

**ContextActionsUpdateTranslator.cs** — in `ParseActions`, add to the numeric-ID switch:

```csharp
actionName = id switch
{
    1  => "IG_CenterOnEntity",
    10 => "IG_DeleteEntity",   // IOS ContextMenuActions.Delete
    _  => id.ToString(CultureInfo.InvariantCulture)
};
```

**IgApplication.cs** — in `ExecuteLocalContextAction`, add a new case:

```csharp
case "IG_DeleteEntity":
{
    if (_world.IsAlive(entity))
    {
        if (_world.HasComponent<NetworkIdentity>(entity))
        {
            ref readonly var netId = ref _world.GetComponentRO<NetworkIdentity>(entity);
            _world.Bus.PublishManaged(new DestroyEntityCommand
            {
                NetworkId = netId.Value,
                Reason    = "map-context-deleted"
            });
        }
        else
        {
            _world.DestroyEntity(entity);
        }
        if (_fdpInspectorState.SelectedEntity == entity)
            _fdpInspectorState.SelectedEntity = null;
    }
    break;
}
```

#### Success conditions

- `ContextActionsUpdateTranslatorTests.ParseActions_Id10_ReturnsIgDeleteEntity` (new): pass JSON
  with `"id": 10`; assert parsed action name equals `"IG_DeleteEntity"`.
- `IgApplicationTests.ExecuteLocalContextAction_IgDeleteEntity_PublishesDestroyCommand` (new):
  invoke handler; assert bus receives `DestroyEntityCommand`.

---

## Phase 8 — Road Graph

---

### BUG2-R001 Fix SimHost Road Graph Rendering

**Design ref:** [§8.1](./BUG2-DESIGN.md#81-fix-simhost-road-graph-rendering)

**Files to change:**  
- `Hrot.SimHost/Modules/SimulationLogicModule.cs`  
- `Hrot.SimHost/SimHostApp.cs`

#### What to do

**SimulationLogicModule.cs:**

1. Change the `RoadNetwork` property from:
   ```csharp
   public RoadNetworkBlob RoadNetwork => default;
   ```
   to an auto-property:
   ```csharp
   public RoadNetworkBlob RoadNetwork { get; }
   ```
2. In the constructor, assign it from the parameter:
   ```csharp
   public SimulationLogicModule(..., RoadNetworkBlob roadNetwork = default, ...)
   {
       RoadNetwork = roadNetwork;
       // ...
   }
   ```

**SimHostApp.cs:**

Replace the hardcoded path block with:
```csharp
var roadNetwork = new RoadNetworkBlob();
if (!string.IsNullOrWhiteSpace(nodeConfig.RoadNetworkBlobPath))
{
    try
    {
        roadNetwork = RoadNetworkLoader.LoadFromJson(nodeConfig.RoadNetworkBlobPath);
    }
    catch (Exception ex)
    {
        FdpLog<SimHostApp>.Warn($"[SimHost] Failed to load road network: {ex.Message}");
    }
}
```

#### Success conditions

- `SimulationLogicModuleTests.Constructor_WithRoadNetwork_SetsProperty` (new): pass a non-default
  `RoadNetworkBlob`; assert `module.RoadNetwork` equals the passed value.
- `SimulationLogicModuleTests.RoadNetwork_Default_ReturnsDefaultNotAlwaysDefault` (rename of
  existing test if present, or new): assert the property returns what was set, not always
  `default`.
- `SimHostAppTests.LoadRoadNetwork_ValidPath_AssignsNetworkToModule` (new): mock
  `RoadNetworkLoader`; provide a valid path; assert the module's `RoadNetwork` is populated.
- `SimHostAppTests.LoadRoadNetwork_InvalidPath_LogsWarnDoesNotThrow` (new): assert warning is
  logged and no exception propagates.

---

## Phase 9 — Architecture

---

### BUG2-A001 Consolidate Health into FDP.Toolkit.Combat.Contracts

**Design ref:** [§9.1](./BUG2-DESIGN.md#91-consolidate-health-into-fdptoolkitcombatcontracts)

**Files to change:**  
- `FDP/Toolkits/FDP.Toolkit.Combat.Contracts/` — add `Health.cs` (moved from Combat)  
- `FDP/Toolkits/FDP.Toolkit.Combat/Components/Health.cs` — delete (or redirect namespace only)  
- `Fdp.Kernel/Components/HealthData.cs` — delete  
- `FDP/Toolkits/FDP.Toolkit.Combat/Systems/DamageSystem.cs` — remove HealthData mirror sync  
- `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/MissionDirectorSystem.cs` — read `Health` directly  
- `FDP/Toolkits/FDP.Toolkit.Behavior/FDP.Toolkit.Behavior.csproj` — add project reference  
- All files importing `HealthData` — update namespace / using

#### What to do

1. Create `FDP/Toolkits/FDP.Toolkit.Combat.Contracts/Components/Health.cs` containing the unified
   `Health` component struct (identical fields to the existing `Health` in Combat).
2. Delete `FDP/Toolkits/FDP.Toolkit.Combat/Components/Health.cs` and replace all usages in the
   Combat toolkit with the Contracts namespace import.
3. Delete `Fdp.Kernel/Components/HealthData.cs`.
4. Add `<ProjectReference Include="..\FDP.Toolkit.Combat.Contracts\FDP.Toolkit.Combat.Contracts.csproj" />`
   to `FDP/Toolkits/FDP.Toolkit.Behavior/FDP.Toolkit.Behavior.csproj`.
5. In `DamageSystem.cs`, delete the `HealthData` sync block (DEBT-033 comment block).
6. In `MissionDirectorSystem.cs`, replace `HealthData.Fraction` references with `Health` component
   reads from the entity.
7. Fix all other `using`/`namespace` references across the solution that imported `HealthData`.

#### Success conditions

- `DamageSystemTests.ProcessHit_DoesNotSetHealthDataComponent` (new): inflict damage; assert the
  entity does NOT have a `HealthData` component (the type should not compile if removed — this
  test serves as a deletion guard).
- `MissionDirectorSystemTests.EvaluateTrigger_HealthCritical_ReadFromHealthComponent` (new):
  set up entity with only `Health` (no `HealthData`); assert trigger fires when
  `health.Current / health.Max < threshold`.
- Solution builds with zero compile errors and zero warnings related to `HealthData`.
- All previously passing tests continue to pass.
