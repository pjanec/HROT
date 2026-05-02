[BUG\] Vehicle given MoveToLocation mission task does not start moving. Probably missing BehaviorFinished trigger case?

You are exactly right on your suspicion. The vehicle fails to start moving because the `BehaviorFinished` trigger case is missing, but the bug specifically resides in the parsing layer that translates incoming messages into ECS components, rather than the execution system itself.

Here is a detailed breakdown of the bug and its mechanics:

**1\. The Missing Trigger Cases** When a mission plan is received, it must be mapped from the DDS payload into internal `EcsMissionTrigger` enums. This is handled by a `ResolveTrigger` helper method in two places: **MissionControlRequestSystem** and **EntityMissionIngressTranslator**.

If you look at the `switch` statements in both methods, they only explicitly handle three cases:

-   `"TimerElapsed"`-   `"ReachedDestination"`-   `"HealthCritical"`

Both methods are entirely missing the cases for `"BehaviorFinished"` and `"UnderAttack"`.

**2\. The "Zero-Second" Fallback Trap** Because `"BehaviorFinished"` is not explicitly defined in the switch statement, the code falls through to the default catch-all case (`_`): `_ => (EcsMissionTrigger.TimerElapsed, 0f)`.

This converts your intended `BehaviorFinished` trigger into a **TimerElapsed** **trigger with a 0-second duration threshold**.

**3\. Why the Vehicle Never Moves** When the `MissionDirectorSystem` evaluates the active mission phase each tick, it handles the `TimerElapsed` condition by checking if `queue.PhaseElapsedSeconds >= phase.TriggerParam`.

Since the fallback mapped the parameter to `0f`, this condition evaluates to `true` instantly on the very first simulation tick. As a result, the `MissionDirectorSystem` immediately advances the queue to the next phase (or clears the behavior entirely if the queue is empty). **The vehicle never starts moving because the** **MoveToLocation** **task is skipped instantly before the locomotion pipeline can even process it.**

_Note: This bug is highly visible because the_ _MissionPanel_ _UI explicitly defaults any newly added task to use the_ _BehaviorFinished_ _trigger_.

**Architectural Insight for the Fix:** While you need to fix the `switch` statements in `ResolveTrigger` to correctly map `"BehaviorFinished"` to `EcsMissionTrigger.BehaviorFinished`, you should also note that **MoveToLocation** **tasks are designed to use the** **ReachedDestination** **trigger**, not `BehaviorFinished`.

The underlying behavior tree for `MoveToLocation` uses an action node (`Action_WriteMoveToChannel`) that continuously returns `NodeStatus.Running`. Because it always returns `Running`, the BTree root will never naturally reach a terminal state to fire a `BehaviorFinishedEvent`. Instead, programmatic move commands (like right-clicking the map) explicitly assign the `"ReachedDestination"` trigger, which allows the `MissionDirectorSystem` to read the `NavState.HasArrived` flag managed by the physical kinematics layer.

\[BUG\] still getting 2 identical acks for update entity descriptor request dtWorldPos (running each IOS, IG, SimHost standalone); only the descriptor owner should send one

The bug is caused by a copy-paste error in the **SimHostApp.cs** initialization sequence, which registers the `UpdateEntityDescriptorRequestSystem` twice.

Because the system is instantiated twice, the SimHost creates two separate `DdsReader` instances that both subscribe to the `UpdateEntityDescriptorRequest` topic. When a request arrives, here is exactly what happens:

-   Both `DdsReader` instances in the SimHost receive the same DDS request.-   Because the SimHost is the rightful owner of the `dtWorldPos` descriptor, **both** system instances successfully pass the `view.HasAuthority(entity, WorldPosOrdinal)` check.-   Both system instances process the exact same payload, update the `SimTransform` component, and call `WriteAck()`.

The IG node is correctly obeying the "only the descriptor owner should send one" rule—it drops the request early because it lacks authority. The two identical ACKs are entirely coming from the SimHost running the same logic twice.

**The Fix:** Open `Hrot.SimHost/SimHostApp.cs` and remove the duplicate `_kernelGroup.AddSystem` call.

```
_kernelGroup.AddSystem(new MissionAdapterSystem(behaviorRegistry, entityMap));

// Remove one of these duplicate registrations:
_kernelGroup.AddSystem(new UpdateEntityDescriptorRequestSystem(ddsParticipant, entityMap, wgs84));
_kernelGroup.AddSystem(new UpdateEntityAttributeRequestSystem(ddsParticipant, entityMap, wgs84, jsonAttributeCompiler));
_kernelGroup.AddSystem(new UpdateEntityDescriptorRequestSystem(ddsParticipant, entityMap, wgs84)); // <-- DELETE THIS LINE
```

the dds senderinfo is not available, maybe the dds participants are NOT initialized with proper nodeId? I mean the sender tracking init is very likely missing and should be added to whatever participant created, see the example below

## 6\. Sender Tracking (Identity)

Identify exactly which application instance sent a message. Essential for multi-process debugging.

### Sender Configuration

```
var config = new SenderIdentityConfig
{
    AppDomainId = 1,
    AppInstanceId = 100
};

// Enable tracking BEFORE creating writers
participant.EnableSenderTracking(config);

// Now, every writer created by this participant automatically broadcasts identity
using var writer = new DdsWriter<LogEvent>(participant, "Logs");
```

Ah, excellent catch! You are entirely correct.

Looking at the initialization sequences across the codebase, the call to `EnableSenderTracking` is completely missing immediately after the `DdsParticipant` instances are created.

Because the `EntityMaster` descriptor explicitly relies on this exact metadata (_"The owner id determined from sample metadata (sender id) provided by the dds csharp bindings"_), omitting this configuration step means the C# bindings will silently broadcast every sample without the required identity metadata.

To fix this globally, you need to apply the `SenderIdentityConfig` directly after the participant is instantiated, and **before** any `DdsWriter` instances are created, across your main application roots:

**1\. SimHost (****Hrot.SimHost/SimHostApp.cs****)** Right after creating the participant, you have the `domainId` and `localNodeId` ready to map to the tracking config:

```
var ddsParticipant = HrotEnvironment.CreateParticipant(domainId);

// Add sender tracking initialization:
ddsParticipant.EnableSenderTracking(new SenderIdentityConfig 
{ 
    AppDomainId = domainId, 
    AppInstanceId = localNodeId 
});

var tkbDb          = HrotEnvironment.CreateTkb();
// ... writers are created later in the module registrations
```

**2\. IG (****Hrot.IG/IgApplication.cs****)** Inside `InitializeNetwork`, using the `_effectiveInstanceId`:

```
participant = HrotEnvironment.CreateParticipant(domainId);

// Add sender tracking initialization:
participant.EnableSenderTracking(new SenderIdentityConfig 
{ 
    AppDomainId = domainId, 
    AppInstanceId = _effectiveInstanceId 
});

_commandGateway = new BdcCommandGateway(participant);
// ...
```

**3\. IOS (****Hrot.ClusterRunner/Services/IosSubsystem.cs****)** Inside the `Initialize` method:

```
_participant = HrotEnvironment.CreateParticipant(config.DomainId);

// Add sender tracking initialization:
_participant.EnableSenderTracking(new SenderIdentityConfig 
{ 
    AppDomainId = config.DomainId, 
    AppInstanceId = config.NodeId 
});
```

**4\. FDP Network Demos (****FDP/Examples/Fdp.Examples.NetworkDemo/NetworkDemoApp.cs****)** If you also use the test applications, the missing initialization is located in `InitializeAsync`:

```
participant = new DdsParticipant(domainId: 0);

// Add sender tracking initialization:
participant.EnableSenderTracking(new SenderIdentityConfig 
{ 
    AppDomainId = 0, 
    AppInstanceId = instanceId 
});
```

By inserting this configuration at these exact points, the CycloneDDS C# bindings will successfully attach the sender ID to all outgoing samples, and the `EntityMaster` ingress translators will finally be able to extract the `OwnerId` natively without relying on payload workarounds!

\[BUG\] map configuration IOS panel still contains tool selection combo although the tools are no more started via map configuration json. combo should be removed

You are completely correct. The Map Configuration panel still retains the legacy `Tools` array and its associated ImGui combo box, even though map tools are now correctly launched via explicit commands (such as `CMD_PLACE_ENTITY` and `CMD_START_AUTHORING`) driven by the specific tool panels.

To fix this bug and remove the obsolete combo box, you need to clean up **Hrot.ExCon/Panels/ConfigPanel.cs**.

Here are the specific removals and updates you need to make:

**1\. Remove the Tool State Fields** Delete the static array, the backing field, and the property that track the selected tool:

```
// DELETE THESE LINES:
public static readonly string[] Tools = { "Navigation", "Selection", "Placement", "Measure" };
private int _selectedTool = 0;

public int SelectedTool
{
    get => _selectedTool;
    set => _selectedTool = Math.Clamp(value, 0, Tools.Length - 1);
}
```

**2\. Update** **BuildPatch()** Remove the `interaction` block from the JSON serialisation so it no longer attempts to send the legacy active tool patch:

```
public string BuildPatch()
{
    return JsonConvert.SerializeObject(new
    {
        // REMOVED the 'interaction = new { activeTool = Tools[_selectedTool] }' block
        view = new
        {
            iconScale = _iconScale,
            layers = new
            {
                satellite         = _satelliteLayer,
                units_ground      = _groundUnits,
                units_air         = _airUnits,
                vehicles          = _vehicles,
                tactical_graphics = _tacticalGraphics,
                road_graphs       = _roadGraphs,
                grid              = _grid
            }
        }
    });
}
```

**3\. Update** **Draw()** Remove the ImGui combo box from the UI rendering logic:

```
public void Draw(IIosLogic logic)
{
    if (ImGui.GetCurrentContext() == IntPtr.Zero) return;
    IosPanelColors.Push();
    ImGui.Begin("Map Configuration");
    IosPanelColors.Pop();

    // REMOVED: ImGui.Combo("Tool", ref _selectedTool, Tools, Tools.Length);

    ImGui.Checkbox("Satellite Layer",    ref _satelliteLayer);
    ImGui.Checkbox("Ground Units",       ref _groundUnits);
    ImGui.Checkbox("Air Units",          ref _airUnits);
    ImGui.Checkbox("Vehicles",           ref _vehicles);
    ImGui.Checkbox("Tactical Graphics",  ref _tacticalGraphics);
    ImGui.Checkbox("Road Graphs",        ref _roadGraphs);
    ImGui.Checkbox("Grid",               ref _grid);

    float scale = _iconScale;
    if (ImGui.SliderFloat("Icon Scale", ref scale, PanelConstants.IconScaleMin, PanelConstants.IconScaleMax))
        IconScale = scale;

    if (ImGui.Button("SEND CONFIG PATCH"))
        HandleSendConfigPatch(logic);

    ImGui.End();
}
```

By removing these sections, the `ConfigPanel` will focus strictly on layer visibility and icon scaling, which correctly aligns with the modernized tool architecture.

\[BUG\] on IG no idea how to enable immediate drag mode - no UI for that. I would like to activate the immediate mode when SHIFT is pressed during dragging;

To enable immediate continuous dragging when the SHIFT key is pressed, you need to modify the event handler that processes drag movements in the IG application shell.

Currently, the framework already fully supports continuous drag network updates (throttled to 10Hz), but it is exclusively gated behind the `_userConfig.ContinuousDragUpdates` property. As you correctly noted, this property was never exposed to the `IgDebugPanel` UI.

You can easily bypass the UI limitation and map this directly to the SHIFT key by modifying the **Hrot.IG/IgApplication.cs** file.

Locate the `interactionTool.OnEntityMoved` subscription inside the `InitializeNetwork` method and update it to check for the SHIFT key state via Raylib:

```
interactionTool.OnEntityMoved += (entity, worldPos) =>
{
    _lastDragWorldPos = worldPos;
    
    // Detect if Left or Right SHIFT is held during the drag
    bool isShiftHeld = Raylib.IsKeyDown(KeyboardKey.LeftShift) || 
                       Raylib.IsKeyDown(KeyboardKey.RightShift);

    // Trigger continuous updates if either the config is true OR shift is held
    if (_userConfig.ContinuousDragUpdates || isShiftHeld)
    {
        _continuousDragTimer += _frameDt;
        if (_continuousDragTimer >= ContinuousDragIntervalSec)
        {
            SendWorldPosUpdate(entity, worldPos);
            _continuousDragTimer = 0f;
        }
    }
};
```

**Why this works:**

-   `OnEntityMoved` is fired by the `EntityDragTool` every frame while the mouse is dragged.-   By adding `isShiftHeld` to the evaluation condition, the system will immediately start accumulating the `_continuousDragTimer`.-   Every 0.1 seconds (`ContinuousDragIntervalSec`), it will fire `SendWorldPosUpdate(entity, worldPos)`, which sends a `TransientLocal` DDS update to the SimHost so the backend immediately reflects the dragged position.-   When the operator finally drops the entity, `OnEntityDragEnded` will still fire to ensure the final resting position is perfectly synchronized and the timer is reset.

I do not want to throttle it to 10Hz, i want the update to be sent every frame is the drag position changed. This is for testing purposes only, not for production, no worries about network performance

To remove the 10Hz throttle and send an update every single frame the drag position changes, you can completely rip out the `_continuousDragTimer` logic from the `OnEntityMoved` handler in **Hrot.IG/IgApplication.cs**.

Update the `interactionTool.OnEntityMoved` subscription inside the `InitializeNetwork` method to look like this:

```
interactionTool.OnEntityMoved += (entity, worldPos) =>
{
    // Detect if Left or Right SHIFT is held during the drag
    bool isShiftHeld = Raylib.IsKeyDown(KeyboardKey.LeftShift) || 
                       Raylib.IsKeyDown(KeyboardKey.RightShift);

    // Trigger continuous updates if either the config is true OR shift is held
    if (_userConfig.ContinuousDragUpdates || isShiftHeld)
    {
        // Only send the DDS update if the position actually changed this frame
        if (_lastDragWorldPos != worldPos)
        {
            SendWorldPosUpdate(entity, worldPos);
        }
    }
    
    _lastDragWorldPos = worldPos;
};
```

**Clean-up (Optional but recommended):** Since you are no longer throttling the network updates, you can safely delete the now-obsolete timer variables from the top of the `IgApplication` class:

```
// You can delete these lines:
private float _continuousDragTimer;
private float _frameDt;
private const float ContinuousDragIntervalSec = 0.1f;
```

**Why this works perfectly for your test:** The `EntityDragTool` fires `OnEntityMoved` every frame the mouse is held down and moving. By comparing `worldPos` directly against `_lastDragWorldPos` before calling `SendWorldPosUpdate`, you guarantee that a new `UpdateEntityDescriptorRequest` is blasted over the DDS network exactly once per frame, but _only_ if the entity's coordinates actually changed, completely eliminating the 0.1s delay.

\[BUG\] Selection & Mission editor does not show any trigger selection UI - there should be a combo for trigger selection and textbox for trigger parameters, with button for insering some valid default trigger-type-specific parameter json so that the user can easily change it without the need to remember all the trigger properties. BehaviorFinished should remain the default trigger for new tasks.

To fix this and expose the trigger selection UI to the operator, you need to update the **Hrot.ExCon/Panels/MissionPanel.cs** file.

The underlying `MissionPlan` data structures and ingress translators already fully support these triggers (`TimerElapsed`, `ReachedDestination`, `HealthCritical`, `UnderAttack`, `BehaviorFinished`), but the `MissionPanel` was simply skipping them during the ImGui rendering loop.

Here are the specific additions to make to `MissionPanel.cs`:

1\. Add the Trigger Types and Default Parameters Helper

At the top of the `MissionPanel` class, add an array containing the valid trigger types, and a helper to return sensible default values (even though the backend schema says "JSON", the `EntityMissionIngressTranslator` currently parses these parameters as floats):

```
private static readonly string[] _triggerTypes = { 
    "BehaviorFinished", 
    "TimerElapsed", 
    "ReachedDestination", 
    "HealthCritical", 
    "UnderAttack" 
};

private static string GetDefaultTriggerParams(string triggerType) => triggerType switch
{
    "TimerElapsed"   => "10.0",   // Default to 10 seconds
    "HealthCritical" => "0.25",   // Default to 25% health threshold
    _                => ""        // Others do not strictly require parameters
};
```

2\. Add the State Handlers

Add the following methods into the "Draft editing handlers" region of the `MissionPanel` class to handle mutations safely against the draft plan:

```
public void HandleEditTriggerType(int taskIndex, int triggerIndex, string newType)
{
    if (!TryGetDraftTasks(out var tasks)) return;
    if (taskIndex < 0 || taskIndex >= tasks.Count) return;

    var task = tasks[taskIndex];
    if (task.Triggers != null && triggerIndex >= 0 && triggerIndex < task.Triggers.Count)
    {
        var trigger = task.Triggers[triggerIndex];
        trigger.Type = newType;
        trigger.Params = GetDefaultTriggerParams(newType); // Auto-apply valid default when changed
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
        Type = type, 
        Params = GetDefaultTriggerParams(type) 
    });
    tasks[taskIndex] = task;
}
```

3\. Update the UI Rendering Loop

Inside the `Draw(IIosLogic logic)` method, locate the `for (int i = 0; i < planToShow.Tasks.Count; i++)` loop. Right after the `BehaviorParams` text editor block (and before the `↑ / ↓ / ✕` move/delete buttons), insert the new Trigger UI logic:

            ```
            // ... existing BehaviorParams editor ...
            }
        }
    
        // ─── Trigger UI ───────────────────────────────────────────────────
        if (task.Triggers != null && task.Triggers.Count > 0)
        {
            var trigger = task.Triggers;
            string triggerType = trigger.Type ?? "BehaviorFinished";
            string triggerParams = trigger.Params ?? string.Empty;
    
            ImGui.Text("Trigger:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(150f);
            if (ImGui.BeginCombo($"##TrigType{i}", triggerType))
            {
                for (int t = 0; t < _triggerTypes.Length; t++)
                {
                    bool isSelected = triggerType == _triggerTypes[t];
                    if (ImGui.Selectable(_triggerTypes[t], isSelected))
                        HandleEditTriggerType(i, 0, _triggerTypes[t]);
                    
                    if (isSelected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
    
            ImGui.SameLine();
            ImGui.SetNextItemWidth(120f);
            if (ImGui.InputText($"##TrigParams{i}", ref triggerParams, 1024))
            {
                HandleEditTriggerParams(i, 0, triggerParams);
            }
    
            ImGui.SameLine();
            if (ImGui.Button($"Default##TrigDef{i}"))
            {
                HandleEditTriggerParams(i, 0, GetDefaultTriggerParams(triggerType));
            }
        }
        else
        {
            if (ImGui.Button($"+ Add Trigger##{i}"))
                HandleAddTrigger(i, "BehaviorFinished");
        }
        
        // ... existing task Move/Delete buttons ...
        if (ImGui.SmallButton($"↑##{i}"))
            HandleMoveTask(i, i - 1);
```

**Note:** `HandleAddTask` (around line 351) already defaults new tasks to use `new MissionTrigger { Type = "BehaviorFinished" }`, so `BehaviorFinished` will inherently remain the default trigger for all newly appended tasks!

\[BUG\] Buttons for Mission Task Up/Down/Delete operations should contain normal text, now unreadable symbol only.

You are completely right. The bug occurs because the ImGui font atlas being used doesn't support the specific Unicode characters (`↑`, `↓`, and `✕`) hardcoded for those buttons, causing them to render as unreadable missing-glyph boxes.

To fix this, you need to update the task rendering loop in **Hrot.ExCon/Panels/MissionPanel.cs** to use standard text while preserving the `##{i}` invisible ImGui ID tags so that each button remains uniquely identifiable to the UI framework.

Locate the end of the `for` loop inside the `Draw` method and replace the symbol buttons with standard text like "Up", "Down", and "Delete":

```
// Replace these lines:
if (ImGui.SmallButton($"↑##{i}"))
    HandleMoveTask(i, i - 1);
ImGui.SameLine();
if (ImGui.SmallButton($"↓##{i}"))
    HandleMoveTask(i, i + 1);
ImGui.SameLine();
if (ImGui.SmallButton($"✕##{i}"))
    HandleDeleteTask(i);

// With this standard text version:
if (ImGui.SmallButton($"Up##{i}"))
    HandleMoveTask(i, i - 1);
ImGui.SameLine();
if (ImGui.SmallButton($"Down##{i}"))
    HandleMoveTask(i, i + 1);
ImGui.SameLine();
if (ImGui.SmallButton($"Delete##{i}"))
    HandleDeleteTask(i);
```

Making this change will immediately replace the broken symbols with readable text buttons while maintaining the exact same reordering and deletion functionality for your mission tasks.

btw is there a way for the imgui on raylib to support these unicode charatcers?

The provided sources do not contain information on how to configure ImGui to support additional Unicode characters.

Based on the sources, the applications currently initialize ImGui using basic setup methods like **rlImGui.Setup(darkTheme: true)** or **rlImGui.Setup(true)**, and there is no code present that interacts with the ImGui font atlas to load custom fonts or specify extended character ranges.

Please note that this information is not from your sources and you may want to independently verify that information: Generally, to support Unicode characters like `↑`, `↓`, and `✕` in `rlImGui-cs` and ImGui.NET, you need to load a custom TrueType font (`.ttf`) that contains those specific glyphs. Before your main render loop, you would access **ImGui.GetIO().Fonts.AddFontFromFileTTF()** and pass in a custom glyph range array that includes the specific Unicode hex values for those symbols, followed by rebuilding the font texture atlas for Raylib.

\[BUG\] When map layer with ground vehicles is turned off, now invisible entities still can be selected . Also currently selected but now invisible entity remains selected, still showing selection indicator on the 2d.

The issue occurs because layer visibility filtering was being bypassed in several decoupled systems:

-   **Box Selection** directly loops over the raw ECS query and adds entities without verifying if their `MapDisplayComponent.LayerMask` is currently enabled.-   **SelectionRenderSystem** unconditionally draws selection rings for any entity with `SelectionState.IsSelected = true`, ignoring the canvas's `VisibleLayersMask`.-   **EntityRenderLayer** in `IgApplication.cs` was hardcoded to `layerBitIndex: 0` (Ground Units), meaning it was only performing strict single-bit mask checks. It needs to be converted to a "catch-all" layer (`-1`) that cross-references each entity's individual mask against the global canvas layer mask dynamically.

Here is the comprehensive fix across the four affected files:

1\. Fix `BoxSelectionTool.cs`

The tool needs to capture the `MapCanvas` to know which layers are active, and skip invisible entities during the bounding box evaluation.

**File:** `FDP/Toolkits/FDP.Toolkit.Vis2D/Tools/BoxSelectionTool.cs`

```
// 1. Add the _canvas field
private MapCanvas? _canvas;

// 2. Capture the canvas when the tool is entered
public void OnEnter(MapCanvas canvas)
{
    _canvas = canvas;
    _isActive = true;
}

public void OnExit()
{
    _canvas = null;
    _isActive = false;
}

// 3. Update FinishSelection to evaluate the layer mask
private void FinishSelection()
{
    var selected = new List<Entity>();
    var min = Vector2.Min(_startPos, _currentPos);
    var max = Vector2.Max(_startPos, _currentPos);
    
    // Fallback to all layers visible if canvas is somehow missing
    uint activeMask = _canvas?.ActiveLayerMask ?? 0xFFFFFFFF;
    
    foreach (var entity in _query)
    {
        // NEW: Enforce layer visibility during box selection
        if (_view.HasComponent<FDP.Toolkit.Vis2D.Components.MapDisplayComponent>(entity))
        {
            uint entityMask = _view.GetComponentRO<FDP.Toolkit.Vis2D.Components.MapDisplayComponent>(entity).LayerMask;
            if ((entityMask & activeMask) == 0) 
                continue; // Entity's layer is turned off, skip selection
        }
    
        var pos = _adapter.GetPosition(_view, entity);
        if (!pos.HasValue) continue;
    
        if (pos.Value.X >= min.X && pos.Value.X <= max.X &&
            pos.Value.Y >= min.Y && pos.Value.Y <= max.Y)
        {
            selected.Add(entity);
        }
    }
    
    _onSelectionComplete?.Invoke(selected);
}
```

2\. Fix `SelectionRenderSystem.cs`

Prevent the 2D selection indicators from rendering if the entity's map layer is turned off.

**File:** `Hrot.IG/Systems/SelectionRenderSystem.cs`

```
public void Draw(RenderContext ctx)
{
    foreach (var entity in _query)
    {
        if (!_view.HasComponent<SelectionState>(entity)) continue;
        ref readonly var sel = ref _view.GetComponentRO<SelectionState>(entity);
        if (!sel.IsSelected) continue;

        if (!_view.HasComponent<SimTransform>(entity)) continue;
    
        // NEW: Check if the entity's layer is currently visible on the canvas
        if (_view.HasComponent<FDP.Toolkit.Vis2D.Components.MapDisplayComponent>(entity))
        {
            uint entityMask = _view.GetComponentRO<FDP.Toolkit.Vis2D.Components.MapDisplayComponent>(entity).LayerMask;
            if ((entityMask & ctx.VisibleLayersMask) == 0) 
                continue; // Skip rendering selection ring for hidden entity
        }
    
        ref readonly var transform = ref _view.GetComponentRO<SimTransform>(entity);
        var pos = new Vector2(transform.Position.X, transform.Position.Y);
        
        // ... (existing ring drawing logic remains untouched)
```

3\. Update `EntityRenderLayer.cs`

Provide it access to the `MapCanvas` so it can granularly filter hit-testing and rendering when configured as a monolithic catch-all layer (`LayerBitIndex = -1`).

**File:** `FDP/Toolkits/FDP.Toolkit.Vis2D/Layers/EntityRenderLayer.cs`

```
// 1. Add Canvas Property
public MapCanvas? Canvas { get; set; }

// 2. Rewrite Draw() to use granular masking
public void Draw(RenderContext ctx)
{
    if (LayerBitIndex >= 0)
    {
        uint maskBit = 1u << LayerBitIndex;
        if ((ctx.VisibleLayersMask & maskBit) == 0) return;
    }

    foreach (var entity in _query)
    {
        uint entityMask = 1; 
        if (_view.HasComponent<MapDisplayComponent>(entity))
            entityMask = _view.GetComponentRO<MapDisplayComponent>(entity).LayerMask;
    
        // NEW: Filter against the global mask for granular visibility
        if ((entityMask & ctx.VisibleLayersMask) == 0)
            continue;
    
        if (LayerBitIndex >= 0)
        {
            uint maskBit = 1u << LayerBitIndex;
            if ((entityMask & maskBit) == 0) continue;
        }
    
        var pos = _adapter.GetPosition(_view, entity);
        if (!pos.HasValue) continue;
        // ... (existing render execution)
```

_(You will need to make the exact same masking condition update inside the_ _PickEntity_ _and_ _HandleInput_ _methods of_ _EntityRenderLayer__, replacing_ _ctx.VisibleLayersMask_ _with_ _Canvas?.ActiveLayerMask ?? 0xFFFFFFFF__)_

4\. Update `IgApplication.cs`

Configure the `EntityRenderLayer` to act as a monolithic catch-all layer (by setting `layerBitIndex: -1`) and bind the new `Canvas` property.

**File:** `Hrot.IG/IgApplication.cs` Inside the `InitializeEcs` method:

```
// Replace these lines:
var layer     = new EntityRenderLayer(
    "Entities", layerBitIndex: 0,
    _world, query, adapter, selection);

// With:
var layer     = new EntityRenderLayer(
    "Entities", layerBitIndex: -1,
    _world, query, adapter, selection) { Canvas = _canvas };
```

\[BUG\] I still can not see any road graph rendered on simhost, even if standalone ig app started from proper Hrot.runner project folder - maybe the roadmap file not found or failed to load or something \[BUG\] This might also prevent the road picking when mission task FollowRoute is selected in IOS mission task editor, preventing to use this kind of task. The road picker should show specific cursor/indicator.

You have uncovered two separate bugs that interact to create this confusing behavior.

The first issue is a direct coding oversight that prevents the static road network from rendering on the SimHost. The second issue is a missing visual indicator in your map tool. However, there is a **crucial architectural disconnect** between these two systems that you need to be aware of: the static road graph (from JSON) and the `FollowRoute` mission task actually use two completely different navigation modes under the hood.

Here is the comprehensive breakdown and the fixes for both.

1\. Fix the SimHost Road Graph Rendering

The road graph fails to render because the loaded `RoadNetworkBlob` is accidentally discarded before it reaches the visualization layer, and the file loading logic relies on a hardcoded relative path that breaks depending on the working directory (like when running from `Hrot.ClusterRunner`).

**Fix** **Hrot.SimHost/Modules/SimulationLogicModule.cs** The module hardcodes the public property to return `default`, effectively passing an empty/uninitialized struct to the rendering layer.

```
// Change this line:
public RoadNetworkBlob RoadNetwork => default;

// To an auto-property:
public RoadNetworkBlob RoadNetwork { get; }
```

Then, inside the `SimulationLogicModule` constructor, properly assign it:

```
public SimulationLogicModule(
    BehaviorRegistry         behaviorRegistry,
    NetworkEntityMap          entityMap,
    VehicleAPI?               vehicleAPI               = null,
    RoadNetworkBlob           roadNetwork              = default,
    /* ... */)
{
    RoadNetwork = roadNetwork; // Add this line
    // ...
```

**Fix** **Hrot.SimHost/SimHostApp.cs** Remove the hardcoded `"Assets/sample_road.json"` path and the silent `catch` block. Use the actual `RoadNetworkBlobPath` from the `nodeConfig` so it respects the Runner's configuration setup:

```
// ── 7. Road network ───────────────────────────────────────────────
var roadNetwork = new RoadNetworkBlob();
if (!string.IsNullOrWhiteSpace(nodeConfig.RoadNetworkBlobPath))
{
    try 
    { 
        roadNetwork = RoadNetworkLoader.LoadFromJson(nodeConfig.RoadNetworkBlobPath); 
    }
    catch (Exception ex) 
    { 
        FDP.Kernel.Logging.FdpLog<SimHostApp>.Warn($"[SimHost] Failed to load road network: {ex.Message}"); 
    }
}
```

2\. Fix the Missing Road Picker Cursor/Indicator

When you select `FollowRoute` and click "Pick Route" in the IOS, it activates the `EntityPickerTool` on the IG. The reason you don't see an indicator is because `EntityPickerTool` does not implement the `Draw` method to render the crosshair documented in `MapCommandRequest`.

To fix this, open your **EntityPickerTool.cs** (inside `FDP.Toolkit.Vis2D/Tools`) and add the same crosshair rendering logic used by the location picker, but styled to turn red when hovering over a valid target:

```
public void Draw(RenderContext ctx)
{
    var pos = _currentMousePos;
    float size = 10f / ctx.Zoom;
    float gap = 3f / ctx.Zoom;
    float thick = 2f / ctx.Zoom;

    // Red if hovering a pickable entity, otherwise orange
    Color color = _hoveredEntity.HasValue ? new Color(255, 0, 0, 255) : new Color(255, 161, 0, 255);
    
    Raylib.DrawLineEx(new Vector2(pos.X - size, pos.Y), new Vector2(pos.X - gap, pos.Y), thick, color);
    Raylib.DrawLineEx(new Vector2(pos.X + gap, pos.Y),  new Vector2(pos.X + size, pos.Y), thick, color);
    Raylib.DrawLineEx(new Vector2(pos.X, pos.Y - size), new Vector2(pos.X, pos.Y - gap), thick, color);
    Raylib.DrawLineEx(new Vector2(pos.X, pos.Y + gap),  new Vector2(pos.X, pos.Y + size), thick, color);
    Raylib.DrawCircleLinesV(pos, gap, color);
}
```

⚠️ Bonus Insight: The "FollowRoute" JSON Schema Mismatch

Even after you fix the cursor and pick a route, your vehicle still won't move. You are experiencing a major architectural mismatch between the IOS and the SimHost regarding this specific task.

-   **The Layer Disconnect:** The `FollowRoute` filter specifically searches for `road_graphs`, which the IG maps to `TacGraphic_Route` ECS entities (drawn by the area authoring tool). It **cannot** pick the static JSON road graph lines, as those are not ECS entities.-   **The JSON Mismatch:** Look at `Hrot.ExCon/Panels/MissionPanel.cs`. When you pick an entity, it generates the parameters: `{"routeEntityId": 123}` However, look at `Hrot.SimHost/Brains/SimHostNodes.cs`. The backend expects: `{"TrajectoryId": 123, "Loop": false}`

Because the property names do not match, the SimHost deserializes the target as `TrajectoryId = 0` and silently fails. To make `FollowRoute` work, you will need to update `FollowRouteParams` in `SimHostNodes.cs` to accept `RouteEntityId`, and then update `Action_WriteFollowRouteChannel` to look up the `EditablePolyline` component of that entity and register it dynamically with the `TrajectoryPoolManager`.

\[BUG\] Measure tool should show specific cursor/inidcator when waiting for first click - now not clear that the tool is active.

You are completely right. The issue is that the `MeasureTool.Draw` method currently evaluates `if (!_startPoint.HasValue)` and instantly returns, meaning absolutely nothing is drawn on the screen while the tool waits for the operator's first click.

Because `HandleHover` is already successfully tracking the mouse coordinates into `_currentPoint`, we can easily fix this by drawing a crosshair cursor specifically when `_startPoint` is null.

To fix this, open **Hrot.IG/Tools/MeasureTool.cs** and update the `Draw` method to include a crosshair indicator before the early return:

```
public void Draw(RenderContext ctx)
{
    if (!_startPoint.HasValue)
    {
        // NEW: Draw a crosshair indicator to show the tool is active
        float zoom  = ctx.Zoom > 0 ? ctx.Zoom : 1f;
        float size  = 14f / zoom;
        float gap   = 5f / zoom;
        float thick = MeasureToolConstants.LineThickness / zoom;
        Color color = MeasureToolConstants.LineColor;
        var pos     = _currentPoint;

        // Draw horizontal arms
        Raylib.DrawLineEx(new Vector2(pos.X - size, pos.Y), new Vector2(pos.X - gap, pos.Y), thick, color);
        Raylib.DrawLineEx(new Vector2(pos.X + gap, pos.Y),  new Vector2(pos.X + size, pos.Y), thick, color);
        // Draw vertical arms
        Raylib.DrawLineEx(new Vector2(pos.X, pos.Y - size), new Vector2(pos.X, pos.Y - gap), thick, color);
        Raylib.DrawLineEx(new Vector2(pos.X, pos.Y + gap),  new Vector2(pos.X, pos.Y + size), thick, color);
        // Draw centre circle
        Raylib.DrawCircleLinesV(pos, gap, color);
    
        return;
    }
    
    var start = _startPoint.Value;
    var end   = _currentPoint;
    
    Raylib.DrawLineEx(start, end, MeasureToolConstants.LineThickness, MeasureToolConstants.LineColor);
    
    float  distance = Vector2.Distance(start, end);
    string label    = $"{distance:F1} m";
    var    midpoint = (start + end) * 0.5f;
    
    Raylib.DrawText(
        label,
        (int)midpoint.X,
        (int)(midpoint.Y + MeasureToolConstants.LabelOffsetY),
        MeasureToolConstants.LabelFontSize,
        Color.White);
}
```

**How this fix works:**

-   Instead of doing nothing, the tool will now draw a scalable `MeasureToolConstants.LineColor` (cyan) crosshair that follows the mouse.-   By dividing the sizes by `ctx.Zoom`, the crosshair will remain a consistent size on the screen regardless of how far the operator has zoomed in or out.-   Once the operator left-clicks and `_startPoint` is populated, it bypasses the crosshair block and draws the measurement line and distance label exactly as it did before.

\[BUG\] ENtity inspector UI should support in its context menu a field for deleting the entity (using proper networked way using ELM, no shortcuts)

To implement a proper networked deletion from the Entity Inspector's context menu, you need to append a new action to the `LambdaEntityContextMenuHandler` that publishes a `DestroyEntityCommand` to the event bus. This ensures the request is correctly routed through the `NetworkSpawningSystem`, which triggers the Entity Lifecycle Module (ELM) teardown and broadcasts a DDS `EntityMaster` DISPOSE to all peers.

You will need to update the context menu registrations in both the **SimHost** and **IG** applications.

1\. Update SimHost Entity Inspector

Open **Hrot.SimHost/SimHostVisualization.cs** and add the "Delete entity" option to the `_fdpEntityInspector` handler inside the constructor:

```
_fdpEntityInspector.RegisterContextMenuHandler(new LambdaEntityContextMenuHandler((entity, builder) =>
{
    builder.AddItem("Center on entity", () => CenterCameraOnEntity(entity));
    builder.AddItem("Select entity", () =>
    {
        _selection!.Set(entity);
        _fdpInspectorState.SelectedEntity = entity;
    });

    // NEW: Proper networked deletion
    builder.AddSeparator();
    builder.AddItem("Delete entity", () =>
    {
        if (repo.IsAlive(entity))
        {
            if (repo.HasComponent<NetworkIdentity>(entity))
            {
                // Network-replicated entity — route through NetworkSpawningSystem
                ref readonly var netId = ref repo.GetComponentRO<NetworkIdentity>(entity);
                repo.Bus.PublishManaged(new DestroyEntityCommand
                {
                    NetworkId = netId.Value,
                    Reason    = "inspector-deleted"
                });
            }
            else
            {
                // Local-only entity
                repo.DestroyEntity(entity);
            }
            
            // Clear selection if we just deleted the selected entity
            if (_selection!.Contains(entity))
            {
                _selection.Remove(entity);
                if (_fdpInspectorState.SelectedEntity == entity)
                    _fdpInspectorState.SelectedEntity = null;
            }
        }
    });
}));
```

2\. Update IG Entity Inspector

Open **Hrot.IG/IgApplication.cs** and add the exact same networked deletion logic to the `_fdpEntityInspector` handler inside the `DrawUI` method (or where `_fdpContextMenusWired` is initialized):

```
_fdpEntityInspector.RegisterContextMenuHandler(new LambdaEntityContextMenuHandler((entity, builder) =>
{
    builder.AddItem("Center on entity", () => CenterCameraOn(entity));
    builder.AddItem("Select entity",    () => SelectEntityOnMap(entity));

    if (_world.HasManagedComponent<EditablePolyline>(entity)
        && _entityMap.TryGetNetworkId(entity, out long editNetId))
    {
        builder.AddSeparator();
        builder.AddItem("Edit overlay", () => ActivateAreaEditingTool(editNetId));
    }
    
    // NEW: Proper networked deletion
    builder.AddSeparator();
    builder.AddItem("Delete entity", () =>
    {
        if (_world.IsAlive(entity))
        {
            if (_world.HasComponent<NetworkIdentity>(entity))
            {
                // Network-replicated entity — route through NetworkSpawningSystem
                ref readonly var netId = ref _world.GetComponentRO<NetworkIdentity>(entity);
                _world.Bus.PublishManaged(new DestroyEntityCommand
                {
                    NetworkId = netId.Value,
                    Reason    = "inspector-deleted"
                });
            }
            else
            {
                // Local-only entity
                _world.DestroyEntity(entity);
            }
            
            // Clear selection if we just deleted the selected entity
            if (_fdpInspectorState.SelectedEntity == entity)
                _fdpInspectorState.SelectedEntity = null;
        }
    });
}));
```

By publishing the `DestroyEntityCommand`, you successfully bypass any local-only "shortcuts" (like `DestroyEntity(entity)`) for networked objects, guaranteeing that the ELM tracks the teardown and the IG cleans up its ghosts correctly.

\[BUG\] WorldPos descriptor not disposed when entity deleted. Maybe this issue is there for other entity descriptors as well?

You are completely correct. The **WorldPos** descriptor is failing to be disposed due to an architectural oversight in its egress translator, creating a unique "sidecar" leak.

**The Root Cause for** **WorldPos** The `WorldPosEgressTranslator` inherits from the generic `CycloneTranslator<WorldPos, WorldPos>` base class. This generic base class provides a virtual `Dispose` method that automatically cleans up the primary generic topic (which in this case is `WorldPos`).

However, the `WorldPosEgressTranslator` internally instantiates a secondary writer (`_drWriter`) specifically to handle the `WorldPos` topic. Because the translator never overrides the base `Dispose` method, the `CycloneNetworkCleanupSystem` only triggers the base disposal logic. It successfully disposes the primary `WorldPos` topic, but completely forgets about the `_drWriter`, leaving the `WorldPos` instance orphaned on the DDS network when the entity is deleted.

**Are other descriptors affected?** Fortunately, this bug is isolated to `WorldPos` because it is the only descriptor "piggybacking" inside another translator. A review of the other egress translators shows they are safe:

-   **Correctly Disposed:** `EntityInfoEgressTranslator`, `MapVisualOverlayEgressTranslator`, `EntityMasterEgressTranslator`, and `EntityMissionEgressTranslator` all correctly implement their own explicit `Dispose` methods to clean up their respective topics.-   **Intentionally Ignored:** `NavigationIntentEgressTranslator` and `NavigationStatusEgressTranslator` have explicitly empty `Dispose` methods, with source comments confirming that `"No DDS dispose needed"` is intentional for those specific topics.

**The Fix** To resolve the leak, you need to override the `Dispose` method in **Hrot.Map.Common/Replication/Egress/WorldPosEgressTranslator.cs** so that it cleans up both the primary and secondary topics.

Add the following method to the `WorldPosEgressTranslator` class:

```
public override void Dispose(long networkEntityId)
{
    // 1. Dispose the primary WorldPos topic via the base generic class
    base.Dispose(networkEntityId);

    // 2. Dispose the secondary WorldPos topic
    _drWriter.DisposeInstance(new WorldPos 
    { 
        EntityId = (int)networkEntityId 
    });
}
```

This guarantees that when an entity is destroyed, the network cleanup system successfully tombstones both positional descriptors across all peers.

\[BUG\] ORBAT Tree in IOS does not indent the subordinates, they appear at the same level as their commander - not good UX

The bug is caused by how the **OrbatPanel.Draw** method renders the ImGui tree using a pre-flattened list of nodes.

Here is exactly what is happening:

-   `GetVisibleNodes` executes a depth-first search and correctly computes a flattened list of `OrbatNode`s, where each node is assigned a correct `Depth` property.-   However, inside the rendering loop, whenever `ImGui.TreeNodeEx` returns `true` (meaning the node is expanded), the code **immediately calls** **ImGui.TreePop()**.-   `TreePop()` instantly reverts the structural indentation that ImGui just applied. Because the list is completely flat, the next subordinate item in the loop is then drawn at the exact same root indentation level as its commander.

To fix this, you should keep the flat-loop architecture (which is highly performant) but **manually apply ImGui's indentation spacing based on the pre-calculated** **node.Depth** before drawing each item, and unindent it immediately after.

The Fix

Open **Hrot.ExCon/Panels/OrbatPanel.cs** and update the rendering loop inside the `Draw(IIosLogic logic)` method:

```
var nodes = GetVisibleNodes(logic.Repo);
foreach (var node in nodes)
{
    // NEW: Calculate and apply manual indentation based on the node's Depth
    float indentSpacing = node.Depth * ImGui.GetStyle().IndentSpacing;
    if (indentSpacing > 0) 
        ImGui.Indent(indentSpacing);

    var flags = node.HasChildren
        ? ImGuiTreeNodeFlags.OpenOnArrow
        : ImGuiTreeNodeFlags.Leaf;
    
    string label = $"{node.Name} ({node.EntityId})";
    bool open = ImGui.TreeNodeEx(label, flags);
    
    if (ImGui.IsItemClicked()) HandleEntityClick(node.EntityId, logic);
    
    if (open)
    {
        if (!_expandedNodes.Contains(node.EntityId)) ToggleExpanded(node.EntityId);
        ImGui.TreePop(); // Still pop immediately to prevent structural nesting affecting the flat loop
    }
    else if (_expandedNodes.Contains(node.EntityId))
    {
        ToggleExpanded(node.EntityId);
    }
    
    // NEW: Restore the cursor indentation for the next item in the flat loop
    if (indentSpacing > 0) 
        ImGui.Unindent(indentSpacing);
}
```

By multiplying the node's `Depth` by `ImGui.GetStyle().IndentSpacing`, the UI will perfectly indent subordinates hierarchically while gracefully respecting any global UI scaling or DPI settings applied to the application.

\[BUG\] IOS Mission/task editor does not seem to handle the version conflicts properly - no warning that information shown is already obsolete, no possibility to forget the user changes and update to latest state. How to achieve that? Could that waork when the mission executing mechanism updates the task status on the fly, wouldn't that somehow collide?

You have identified an excellent UX gap in the mission editor. The underlying Optimistic Concurrency Control (OCC) logic is fully implemented to detect conflicts, but the UI is missing the rendering code to display the warning and allow the operator to discard their obsolete draft.

Additionally, your intuition about runtime collisions is sharp, but the architecture actually protects you from this!

Here is how the version conflict handling works, why it won't collide with runtime execution, and the exact code you need to add to fix the UI.

1\. The Collision Question: Will runtime task status updates collide with the editor?

**No, they will not collide.** The mission executing mechanism updating the task status (e.g., advancing from `TASK_PLANNED` to `TASK_ACTIVE` or `TASK_DONE`) will not invalidate an operator's draft.

Here is why:

-   The CGF/SimHost uses a `MissionDirectorSystem` to evaluate triggers and advance the active phase dynamically. This runtime state is broadcasted back to the network via the `EntityMissionEgressTranslator`.-   However, the optimistic lock version (`BaseVersion`) is isolated. It is managed by the `MissionControlRequestSystem` and is **only incremented when a structural command is received** (such as `CMD_REPLACE_MISSION`, `CMD_JUMP_TO_TASK`, or `CMD_ABORT_ALL`).-   Therefore, the routine, on-the-fly progression of a vehicle following its route does not increment the lock version. Your draft only becomes obsolete if _another operator_ submits a structural edit to the mission plan while you are drafting yours.

2\. Fixing the UI (Missing Modal and Discard Button)

While the `MissionPanel` currently detects conflicts and sets the `HasConflictAlert` and `ConflictMessage` properties, it never actually draws anything on the screen to warn the user. Furthermore, the `SyncDraftFromSnapshot` method locks onto the draft and refuses to fetch the latest network state until the draft is explicitly cleared.

To fix this, open **Hrot.ExCon/Panels/MissionPanel.cs** and make two additions to the `Draw(IIosLogic logic)` method:

**A. Add the Conflict Modal** Right after calling `PollPickCompletion();` at the top of the `Draw` method, insert the ImGui modal rendering logic:

```
public void Draw(IIosLogic logic)
{
    // ... existing setup ...
    PollCommitCompletion();
    PollPickCompletion();

    // ─── NEW: Version Conflict Modal ──────────────────────────────────────────
    if (HasConflictAlert)
    {
        ImGui.OpenPopup("Version Conflict");
    }
    
    if (ImGui.BeginPopupModal("Version Conflict", ImGuiWindowFlags.AlwaysAutoResize))
    {
        ImGui.TextColored(new System.Numerics.Vector4(1f, 0.2f, 0.2f, 1f), "Mission plan modified by another operator!");
        ImGui.TextWrapped(ConflictMessage ?? "Your draft is based on an obsolete version of the mission.");
        
        ImGui.Separator();
        
        if (ImGui.Button("Discard My Changes & Reload"))
        {
            ClearDraft();
            DismissConflict();
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Keep Draft (Cannot Commit)"))
        {
            DismissConflict();
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }
    // ──────────────────────────────────────────────────────────────────────────
    
    if (_selectedEntityId == 0)
    // ...
```

**B. Add a permanent "Discard Draft" button** At the bottom of the `Draw` method, where the Commit, Jump, and Abort buttons are rendered, add a button that allows the user to manually abandon their draft at any time (which will invoke `ClearDraft()` and force `SyncDraftFromSnapshot` to pull the latest network state):

```
    // ... inside Draw() ...
    bool commitEnabled = CommitButtonEnabled;
    if (!commitEnabled) ImGui.BeginDisabled();
    if (ImGui.Button("Commit")) HandleCommit(logic);
    if (!commitEnabled) ImGui.EndDisabled();
    
    // ─── NEW: Discard Draft Button ──────────────────────────────────────────
    if (_draftPlan.HasValue)
    {
        ImGui.SameLine();
        if (ImGui.Button("Discard Draft"))
        {
            ClearDraft();
        }
    }
    // ──────────────────────────────────────────────────────────────────────────
    
    if (ImGui.Button("JUMP"))  HandleJump(logic);
    ImGui.SameLine();
    if (ImGui.Button("ABORT")) HandleAbort(logic);
    // ...
```

By adding these two blocks, operators will immediately get a red popup modal if their commit is rejected due to a version conflict, offering them a 1-click option to throw away their stale draft and sync with the latest authoritative mission plan.

no there sould me no modals, the IOS UI needs to keep working in other panels. Just the mission control panel should indicate the obsoleteness somehow and offer either forgetting the draft or forcing the draft as the new state to be used authoritatively.

You are absolutely right. Using a modal blocks the entire application flow, which goes against the non-blocking philosophy of the IOS panels. An inline indicator is much better UX.

Furthermore, your idea to "force the draft" is perfectly supported by the underlying architecture! In **MissionControlRequestSystem**, the optimistic locking check is explicitly bypassed if the request's `BaseVersion` is `0`: `if (request.BaseVersion > 0 && request.BaseVersion != currentVersion) { WriteAck(..., VersionConflict); return; }`

Here is how you can implement this inline, non-blocking UI in **Hrot.ExCon/Panels/MissionPanel.cs**:

1\. Add a `HandleForceCommit` method

Add this new method next to your existing `HandleCommit` method. It acts identically but deliberately passes `0` as the `baseVersion` to bypass the lock check, and automatically dismisses the conflict state:

```
public void HandleForceCommit(IIosLogic logic)
{
    ArgumentNullException.ThrowIfNull(logic);
    if (!CanCommit) return;

    var plan = _draftPlan!.Value;
    FdpLog<MissionPanel>.Info("[IOS] Force Commit triggered: entityId={0} taskCount={1}",
        _selectedEntityId, plan.Tasks?.Count ?? 0);
    
    // Passing 0 as the baseVersion bypasses the optimistic locking check entirely
    _pendingCommit = logic.MissionEditorService
        .CommitMissionAsync(_selectedEntityId, plan, 0);
    
    _commitInFlight = true;
    DismissConflict();
}
```

2\. Update the `Draw` method to show inline conflict UI

Locate the bottom of the `Draw(IIosLogic logic)` method where the commit, jump, and abort buttons are rendered. Replace the existing button rendering logic with an `if/else` block that checks for `HasConflictAlert`:

```
        // ... (inside Draw(), right after the '+ Add Task' button) ...
        if (ImGui.Button("+ Add Task"))
            HandleAddTask();
    
        ImGui.Separator();
    
        // ─── Conflict Resolution UI ───────────────────────────────────────
        if (HasConflictAlert)
        {
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.4f, 0.4f, 1f), "⚠ Conflict: Mission plan was modified by another operator!");
            
            if (ImGui.Button("Discard Draft (Reload)"))
            {
                ClearDraft();
                DismissConflict();
            }
            ImGui.SameLine();
            if (ImGui.Button("Force Commit (Overwrite)"))
            {
                HandleForceCommit(logic);
            }
        }
        else
        {
            // ─── Standard Controls ──────────────────────────────────────────
            bool commitEnabled = CommitButtonEnabled;
            if (!commitEnabled) ImGui.BeginDisabled();
            if (ImGui.Button("Commit")) HandleCommit(logic);
            if (!commitEnabled) ImGui.EndDisabled();
    
            // Optional: You can keep the 'Discard Draft' button here too if you want users 
            // to be able to abandon uncommitted changes at any time without a conflict
            if (_draftPlan.HasValue)
            {
                ImGui.SameLine();
                if (ImGui.Button("Discard Draft")) ClearDraft();
            }
    
            if (ImGui.Button("JUMP"))  HandleJump(logic);
            ImGui.SameLine();
            if (ImGui.Button("ABORT")) HandleAbort(logic);
        }
        
        ImGui.End();
    }
```

**How this flows:**

-   If the operator tries to commit and gets rejected by the server, the `MissionPanel` registers `HasConflictAlert`.-   The UI instantly swaps the standard "Commit" button for an inline red warning. The rest of the IOS is completely unblocked.-   If they click **"Discard Draft"**, it calls `ClearDraft()`, which forces `SyncDraftFromSnapshot` to pull the latest authoritative plan from the DER repository on the next frame.-   If they click **"Force Commit"**, it blasts the command back to the SimHost with `BaseVersion = 0`, bypassing the check and forcibly establishing their draft as the new authoritative state.

\[BUG\] There are 2 similar ECS components Health and HealthData - are they needed both?

Both components are absolutely necessary, and this is a deliberate architectural design rather than a bug!

The existence of both `Health` and `HealthData` solves a specific **circular assembly dependency** problem (tracked internally as **DEBT-033**).

Here is exactly why they both exist and how they differ:

-   **Health****:** This is the **actual hit-point pool** component defined in the Combat toolkit (`FDP.Toolkit.Combat`). It is actively mutated by the `DamageSystem` whenever an entity takes a hit.-   **HealthData****:** This is a **read-only mirror** component defined in the foundational `Fdp.Kernel`.

**The Circular Dependency Problem:** Systems in the Behavior toolkit (specifically the `MissionDirectorSystem`) need to read an entity's health to evaluate mission plan triggers like `HealthCritical`. However, the Combat toolkit _already_ references the Behavior toolkit. If the Behavior toolkit tried to reference the `Health` component directly from the Combat toolkit, it would create an uncompilable circular project dependency.

**The Solution:** To break this cycle, `HealthData` was placed in the core kernel where both toolkits can see it. When the `DamageSystem` (in Combat) applies damage and reduces an entity's `Health`, it immediately synchronizes the new values over to the `HealthData` mirror component. This allows the `MissionDirectorSystem` (in Behavior) to safely read the `HealthData.Fraction` to evaluate triggers without needing to reference the Combat assembly.

**Do you always need both?** No. The presence of `HealthData` on an entity is actually **optional**. You only need to add `HealthData` alongside `Health` at spawn time if you specifically want that entity's AI/behavior systems to be able to react to its health state.

i do nto understand. entity has just one health. So there should be just one health component. Not in Fdp.Kernel - health is nothing kernel-specific, it is toolkit specific stuff. So maybe one little intermediate assembly (ending with "Contract" might be added to remove the circular dependecy? Mirroring to another component seems like a hack. I need clean solution.

You are absolutely right, and your intuition about the architecture is spot on. It **is** a hack, and the codebase actually agrees with you—it is officially documented in the source code as technical debt (**DEBT-033**).

Furthermore, your proposed solution to use an intermediate `.Contracts` assembly is the exact architectural pattern this framework uses to solve these circular dependency issues!

Just recently, a very similar hack involving `HitEvent` was fixed (**DEBT-031**) by creating an assembly named **FDP.Toolkit.Combat.Contracts** specifically to restore kernel purity. The framework also uses this pattern for Navigation (`FDP.Toolkit.Navigation.Contracts`) to share state between Navigation and Physics without circular references.

Since `FDP.Toolkit.Combat.Contracts` already exists, you can execute your clean solution immediately and eliminate the `HealthData` hack entirely.

Here is the clean architectural fix:

**1\. Consolidate into the Contracts Assembly** Move the `Health` component definition out of `FDP.Toolkit.Combat.Components`, delete `HealthData` from `Fdp.Kernel`, and place a single, unified `Health` component into the **FDP.Toolkit.Combat.Contracts** assembly.

**2\. Update Project References** Make sure `FDP.Toolkit.Behavior` has a project reference to `FDP.Toolkit.Combat.Contracts`. (The Combat toolkit already references it).

**3\. Clean up the Systems** Now that there is only one `Health` component visible to both toolkits, you can rip out the mirroring hack from the Combat toolkit's `DamageSystem`. In `FDP.Toolkit.Combat/Systems/DamageSystem.cs`, you can delete this entire block:

```
// Sync HealthData mirror (Fdp.Kernel) so Behavior systems
// (e.g. MissionDirectorSystem) can react to health without
// a Combat → Behavior circular dependency (DEBT-033).
if (World.HasComponent<HealthData>(evt.HitEntity))
{
    var existingHd = World.GetComponent<HealthData>(evt.HitEntity);
    if (existingHd.Current != health.Current)
        World.SetComponent(evt.HitEntity,
            new HealthData { Current = health.Current, Max = health.Max });
}
```

**4\. Update the Behavior Toolkit** In `FDP.Toolkit.Behavior/Systems/MissionDirectorSystem.cs`, update the `HealthCritical` trigger evaluation to just read the unified `Health` component directly instead of `HealthData`.

By doing this, you successfully restore kernel purity (since Health has nothing to do with the ECS kernel) and remove the redundant mirroring, exactly as you suggested!

\[BUG\] The DELETE context menu item sent from IOS does nothing when clicked on IOS - entity not deletes. Should use proper netwroked ELM mechanism. no I do not need IOS to handle the deletion directly. IOS is just providing context menu extensions when map or entity is clicked. This externsion contains DELETE context meny item. IG is already receiving it and displaying it in its context menu. The menu item action is must be such that IG recognizes it as known action and can issue proper networke ELM based entity deletion mechanism.

Ah, I understand perfectly now! You want the IOS to continue acting purely as the menu _provider_, but have the IG natively intercept the `DELETE` item when it is clicked on the map, bypassing the network round-trip back to the IOS, and instead triggering the ELM teardown locally on the IG.

Since the IG automatically intercepts any action name starting with `"IG_"` and executes it natively (via `ExecuteLocalContextAction`), we simply need to map the IOS's known Delete ID (`10`) to an `"IG_"` action string during translation, and then handle that string inside the IG.

Here is exactly how to wire that up:

1\. Map the Action ID in the IG Ingress Translator

Open **Hrot.IG/Translators/ContextActionsUpdateTranslator.cs** and locate the `ParseActions` method. Update the `id` parsing logic to convert the IOS numeric ID `10` (which maps to `ContextMenuActions.Delete`) into a local `"IG_DeleteEntity"` action name:

```
if (idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt32(out int id))
{
    // Map well-known IOS numeric IDs to IG-local action names so
    // they are executed on the IG side rather than round-tripped to IOS.
    actionName = id switch
    {
        1  => "IG_CenterOnEntity",
        10 => "IG_DeleteEntity", // NEW: Intercept the IOS Delete action
        _  => id.ToString(CultureInfo.InvariantCulture)
    };
}
```

2\. Execute the ELM Teardown in IG

Open **Hrot.IG/IgApplication.cs** and locate the `ExecuteLocalContextAction` method. Because we prefixed the action with `"IG_"`, the `HandleContextMenuAction` router will automatically send it here.

Add the `IG_DeleteEntity` case to fire off the `DestroyEntityCommand`, natively invoking the Entity Lifecycle Module:

```
private void ExecuteLocalContextAction(Entity entity, string actionName)
{
    switch (actionName)
    {
        case "IG_Center":
        case "IG_CenterOnEntity":
            CenterCameraOn(entity);
            break;

        // NEW: Proper ELM networked deletion executed directly on the IG
        case "IG_DeleteEntity":
        {
            if (_world.IsAlive(entity))
            {
                if (_world.HasComponent<NetworkIdentity>(entity))
                {
                    // Network-replicated entity — route through NetworkSpawningSystem
                    ref readonly var netId = ref _world.GetComponentRO<NetworkIdentity>(entity);
                    _world.Bus.PublishManaged(new DestroyEntityCommand
                    {
                        NetworkId = netId.Value,
                        Reason    = "map-context-deleted"
                    });
                }
                else
                {
                    // Local-only entity
                    _world.DestroyEntity(entity);
                }
                
                // Clear selection if we just deleted the selected entity
                if (_fdpInspectorState.SelectedEntity == entity)
                    _fdpInspectorState.SelectedEntity = null;
            }
            break;
        }
    
        case "100": // EditOverlay — activate area-editing tool on the selected entity
        // ... (existing code continues)
```

**Why this is the perfect architectural fit:**

-   The IOS remains completely decoupled—it just provides the JSON menu definition (`id: 10`).-   When the operator right-clicks the entity on the map and clicks DELETE, the IG translates `10` to `"IG_DeleteEntity"`.-   The IG short-circuits the request locally and publishes `DestroyEntityCommand`.-   `NetworkSpawningSystem` intercepts it on the IG, initiates the ELM sequence, and gracefully cleans up the entity locally without waiting for or requiring the IOS to do any heavy lifting!

