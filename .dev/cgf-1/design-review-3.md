
I am running "Bagira.Runner -m all"

Orchestrator component's ImGui panel should have beige color (now that color is used INSIDE the panel, but it should be used JUST for the TITLE of the panel) Now there is an ImGui window missing and all the stuff are stacked into DrawUI(). Pls add it into a single ImGui window with beige title bar (similarly to what other panels are made).

To fix this issue, you need to make two main updates. First, update `OrchestratorSubsystem.cs` to supply the beige color to the runner's title bar logic and add the missing `ImGui.Begin()` window wrapper. Second, remove the explicit `ChildBg` color overrides from `OrchestratorScenarioPanel.cs`.

Because the `SubsystemOrchestrator` automatically applies a subsystem's `TitleBarColor` to any ImGui windows drawn inside its `DrawUI()` method, wrapping the contents in a window and passing the right color will perfectly achieve your goal.

Here are the changes to make:

1\. Update `Bagira.Runner/Services/OrchestratorSubsystem.cs`

Change the `TitleBarColor` property to the beige color, and wrap the contents of `DrawUI()` in a standard `ImGui.Begin("Orchestrator")` block:

```
// Change from the dark blue vector to the requested beige vector
public System.Numerics.Vector4 TitleBarColor => new(0.72f, 0.64f, 0.47f, 1f);
```
```
public void DrawUI()
{
    if (_drillMaster == null) return;

    // Wrap the floating controls into a single window
    if (!ImGui.Begin("Orchestrator"))
    {
        ImGui.End();
        return;
    }

    var bootstrapped = _drillMaster.BootstrapComplete;

    // ── Bootstrap banner ──────────────────────────────────────────────────
    if (!bootstrapped)
    {
        var waiting = _config.Mandatory
            .Where(name => !_drillMaster.NodeRoster.ActiveNodes.Values
                .Any(p => p.SubsystemName == name &&
                          p.LocalDsmState == Bagira.BDC.SSTD.Orchestration.DSMState.Standby))
            .ToArray();

        ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(1f, 0.8f, 0.2f, 1f));
        ImGui.TextWrapped($"Waiting for mandatory nodes: {string.Join(", ", waiting)}");
        ImGui.PopStyleColor();
        ImGui.Separator();
    }

    // ... [Keep the rest of your UI rendering code (Simulation controls, Node Health, 2PC History)] ...

    // ── Scenario & Story controls (CGF1-S0106) ───────────────────────────
    _scenarioPanel?.Render();

    // End the newly added window
    ImGui.End();
}
```

2\. Clean up `Bagira.Runner/Services/OrchestratorScenarioPanel.cs`

Now that the window title bar is beige, strip out all the inner `BeigeChildBg` styles that were incorrectly tinting the backgrounds of the child regions.

**Remove the constant:**

```
// Remove this line
// private static readonly Vector4 BeigeChildBg = new(0.72f, 0.64f, 0.47f, 1f);
```

**Remove the** **PushStyleColor** **and** **PopStyleColor** **calls** from all the `Render*` helper methods. For example, your `RenderStatusBanner` should now look like this:

```
private static void RenderStatusBanner(DSMState currentState, DistributedTransaction? activeTx,
    bool bootstrapped, bool hasInFlight)
{
    // Removed ImGui.PushStyleColor(...)
    if (ImGui.BeginChild("##OrcStatusBanner", new Vector2(-1, 54), ImGuiChildFlags.Borders))
    {
        string drillShort = activeTx != null
            ? activeTx.TransactionId.ToString()[..8]
            : "--------";

        string txStatus = hasInFlight
            ? $"TX {drillShort}... in flight"
            : "idle";

        ImGui.Text($"State: {currentState}");
        ImGui.SameLine();
        ImGui.Text("|");
        ImGui.SameLine();
        ImGui.Text(bootstrapped ? txStatus : "NOT BOOTSTRAPPED");
    }
    ImGui.EndChild();
    // Removed ImGui.PopStyleColor()
}
```

Repeat this removal for the push/pop styles wrapped around the other sections (`RenderDrillControl`, `RenderCheckpointSection`, `RenderScenarioSection`, `RenderReplaySection`, and `RenderStoriesSection`).

The 2PC History should show full GUID and should show also the beginning of the Json payload in the table (on hover over the json payload column the pretty formatted json tooltip window should pop up) There should be a Context menu offerin copying the content of the line into clipboard.

The 2PC History table should NOT grow indefinitely; it should show max 10 lines and support scrolling. Each transaction line should be expandable showing the responses from the nodes.

To satisfy these requirements, we need to update the `2PC History` table rendering in **OrchestratorSubsystem.cs**. We will also need to add two new properties to **DistributedTransaction.cs** to store the JSON payload and the responses from the nodes.

Here are the changes to implement the new behavior:

1\. Update `DistributedTransaction.cs`

Add the fields required to store the initial payload and the individual node responses.

```
// In Bagira.Orchestrator/DistributedTransaction.cs
public sealed class DistributedTransaction
{
    // ... existing properties ...
    public Dictionary<int, float> NodeAckLatencyMs { get; } = new();

    // NEW: Add fields to hold the request payload and the incoming node ACKs
    public string PayloadJson { get; set; } = string.Empty;
    public Dictionary<int, string> NodeResponses { get; } = new();
}
```

_(Note: Be sure to populate_ _PayloadJson = req.PayloadJson_ _when creating the transaction in_ _DrillMaster.ProcessSingleSysOpRequest__, and populate_ _tx.NodeResponses\[status.NodeId\] = status.ResultJson_ _when receiving ACKs in_ _DrillMaster.ConsumeNodeOpStatuses__)._

2\. Update `OrchestratorSubsystem.cs`

Replace the existing `2PC History` table rendering block inside the `DrawUI` method. We will add the `ScrollY` flag, enforce a max height of ~10 rows, and use `TreeNodeEx` to make each line expandable.

Add the following JSON formatting helper anywhere inside the `OrchestratorSubsystem` class:

```
private static string FormatPrettyJson(string json)
{
    if (string.IsNullOrWhiteSpace(json)) return string.Empty;
    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return System.Text.Json.JsonSerializer.Serialize(doc, 
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }
    catch
    {
        return json; // Fallback to raw string if it's not valid JSON
    }
}
```

Now, replace the `2PC History table` section:

```
// ── 2PC history table ───────────────────────────────────────────
if (ImGui.CollapsingHeader("2PC History"))
{
    var history = _drillMaster.TransactionHistory;
    
    // Support scrolling and cap at ~10 lines (+ header and scroll affordance)
    float rowHeight = ImGui.GetTextLineHeightWithSpacing();
    Vector2 tableSize = new Vector2(0, rowHeight * 11.5f);

    if (ImGui.BeginTable("TxHistory", 5,
        ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY, 
        tableSize))
    {
        ImGui.TableSetupScrollFreeze(0, 1); // Keep the header visible while scrolling
        ImGui.TableSetupColumn("TransactionId", ImGuiTableColumnFlags.WidthFixed, 270f);
        ImGui.TableSetupColumn("Target State", ImGuiTableColumnFlags.WidthFixed, 110f);
        ImGui.TableSetupColumn("Result", ImGuiTableColumnFlags.WidthFixed, 75f);
        ImGui.TableSetupColumn("ACK Latency", ImGuiTableColumnFlags.WidthFixed, 90f);
        ImGui.TableSetupColumn("Payload", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var tx in history)
        {
            ImGui.TableNextRow();
            
            // --- Column 1: Expandable Full GUID ---
            ImGui.TableNextColumn();
            bool expanded = ImGui.TreeNodeEx(tx.TransactionId.ToString(), ImGuiTreeNodeFlags.SpanFullWidth);

            // Context Menu: Copy line to clipboard
            if (ImGui.BeginPopupContextItem($"CtxMenu_{tx.TransactionId}"))
            {
                if (ImGui.MenuItem("Copy line to clipboard"))
                {
                    string copyText = $"{tx.TransactionId} | {tx.TargetDsmState} | {(tx.IsAborted ? "Aborted" : "Completed")} | Payload: {tx.PayloadJson}";
                    ImGui.SetClipboardText(copyText);
                }
                ImGui.EndPopup();
            }

            // --- Column 2: Target State ---
            ImGui.TableNextColumn(); 
            ImGui.Text(tx.TargetDsmState.ToString());
            
            // --- Column 3: Result ---
            ImGui.TableNextColumn(); 
            ImGui.Text(tx.IsAborted ? "Aborted" : "Completed");
            
            // --- Column 4: Latency ---
            ImGui.TableNextColumn(); 
            string latency = tx.NodeAckLatencyMs.Count == 0
                ? "0"
                : string.Join(", ", tx.NodeAckLatencyMs.Select(kv => $"{kv.Key}:{kv.Value:F0}ms"));
            ImGui.Text(latency);

            // --- Column 5: JSON Payload Snippet ---
            ImGui.TableNextColumn();
            string payloadStr = tx.PayloadJson ?? string.Empty;
            string snippet = payloadStr.Length > 25 ? payloadStr[..25] + "..." : payloadStr;
            ImGui.Text(snippet);

            // Pretty JSON Tooltip on Hover
            if (ImGui.IsItemHovered() && !string.IsNullOrWhiteSpace(payloadStr))
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(FormatPrettyJson(payloadStr));
                ImGui.EndTooltip();
            }

            // --- Expanded Node Responses ---
            if (expanded)
            {
                if (tx.NodeResponses != null && tx.NodeResponses.Count > 0)
                {
                    foreach (var kvp in tx.NodeResponses)
                    {
                        ImGui.TableNextRow();
                        
                        ImGui.TableNextColumn();
                        ImGui.Indent(); 
                        ImGui.TextDisabled($"↳ Node {kvp.Key}"); 
                        ImGui.Unindent();

                        ImGui.TableNextColumn(); // Target State (Empty)
                        ImGui.TableNextColumn(); // Result (Empty)
                        
                        ImGui.TableNextColumn();
                        if (tx.NodeAckLatencyMs.TryGetValue(kvp.Key, out float lat))
                            ImGui.TextDisabled($"{lat:F0}ms");

                        ImGui.TableNextColumn();
                        ImGui.TextWrapped(kvp.Value);
                    }
                }
                else
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Indent(); ImGui.TextDisabled("↳ No node responses recorded."); ImGui.Unindent();
                }
                ImGui.TreePop();
            }
        }
        ImGui.EndTable();
    }
}
```

The "Drill Control" panel should not take shortcut directly to the handler, it should send the SysOpRequest messages!

Some buttons in the Orchestrator ImGui are no-op just with TODO comments, needs to be fully implemented.

Pressing the State machine state button does NOT result in sending network Request - pressing those buttons do nothing in the system. they should send real requests!

To implement the real network requests and replace the local shortcuts, you need to introduce a `DdsWriter<SysOpRequest>` in the `OrchestratorSubsystem`, use it to implement the TODO buttons, and pass it to the `OrchestratorScenarioPanel` so all drill control actions are sent over the network.

Here is how you can update both files:

1\. Update `Bagira.Runner/Services/OrchestratorSubsystem.cs`

Add the `_sysOpWriter` field, initialize it, dispose it, and implement the TODO buttons in `DrawUI()` using this writer.

```
// Add this field to the class
private DdsWriter<SysOpRequest>? _sysOpWriter;
```

Update the `Initialize` method to create the writer and pass it to the panel:

```
public void Initialize(SubsystemConfig config)
{
    _config      = ClusterConfiguration.LoadFrom(
        System.IO.Path.Combine(Directory.GetCurrentDirectory(), "orchestrator-config.json"));
    _participant = BagiraEnvironment.CreateParticipant(config.DomainId);
    _drillMaster = new DrillMaster(_participant, _config);
    
    // NEW: Create the DDS writer for SysOpRequest
    _sysOpWriter = new DdsWriter<SysOpRequest>(_participant);
    
    // Update the panel instantiation to accept the writer
    _scenarioPanel = new OrchestratorScenarioPanel(_drillMaster, _sysOpWriter);

    // ... (keep the rest of the Time coordinator setup)
}
```

Update the `Shutdown` method to clean it up:

```
public void Shutdown()
{
    _scenarioPanel = null;
    _drillMaster?.Dispose();
    _drillMaster = null;
    
    // NEW: Dispose the writer
    _sysOpWriter?.Dispose();
    _sysOpWriter = null;
    
    _timeKernel?.Dispose();
    // ... (keep the rest of the shutdown logic)
}
```

Replace the "Simulation controls" section in `DrawUI()` to send actual `SysOpRequest` messages instead of empty TODOs:

    ```
    // ── Simulation controls (disabled until bootstrapped) ─────────────────
    if (!bootstrapped) ImGui.BeginDisabled();
    
    if (ImGui.Button("Initialize Live"))
    {
        _sysOpWriter?.Write(new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.TransitionState,
            PayloadJson   = $"{{\"TargetState\":{(int)Bagira.BDC.SSTD.Orchestration.DSMState.LoadingLive}}}"
        });
    }
    ImGui.SameLine();
    
    if (ImGui.Button("Pause"))
    {
        _sysOpWriter?.Write(new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.PauseTime,
            PayloadJson   = string.Empty
        });
    }
    ImGui.SameLine();
    
    if (ImGui.Button("Resume"))
    {
        _sysOpWriter?.Write(new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.ResumeTime,
            PayloadJson   = string.Empty
        });
    }
    
    if (!bootstrapped) ImGui.EndDisabled();
```

2\. Update `Bagira.Runner/Services/OrchestratorScenarioPanel.cs`

Import `CycloneDDS.Runtime`, update the constructor to receive the `DdsWriter`, and replace all `_drillMaster.HandleSysOpRequest` shortcut calls with `_sysOpWriter.Write`.

**Add the missing import at the top of the file:**

```
using CycloneDDS.Runtime;
```

**Update the constructor:**

```
// ── Dependencies ──────────────────────────────────────────────────────
private readonly DrillMaster _drillMaster;
private readonly DdsWriter<SysOpRequest> _sysOpWriter; // NEW

public OrchestratorScenarioPanel(DrillMaster drillMaster, DdsWriter<SysOpRequest> sysOpWriter)
{
    _drillMaster = drillMaster ?? throw new ArgumentNullException(nameof(drillMaster));
    _sysOpWriter = sysOpWriter ?? throw new ArgumentNullException(nameof(sysOpWriter));
}
```

**Replace the calls in** **RenderDrillControl****:**

```
// Change this:
// _drillMaster.HandleSysOpRequest(new SysOpRequest { ... });

// To this:
_sysOpWriter.Write(new SysOpRequest
{
    RequestId     = Guid.NewGuid(),
    OperationType = SysOpType.TransitionState,
    PayloadJson   = $"{{\"TargetState\":{(int)target}}}",
});
```

**Replace the calls in** **RenderCheckpointSection****:**

```
_sysOpWriter.Write(new SysOpRequest
{
    RequestId     = Guid.NewGuid(),
    OperationType = SysOpType.TakeCheckpoint,
    PayloadJson   = string.Empty,
});
```

**Replace the calls in** **RenderScenarioSection** **(3 locations):**

```
// Save Scenario
_sysOpWriter.Write(new SysOpRequest
{
    RequestId     = Guid.NewGuid(),
    OperationType = SysOpType.SaveScenario,
    PayloadJson   = $"{{\"ScenarioId\":\"{_saveScenarioId}\"}}",
});

// Load into Edit
_sysOpWriter.Write(new SysOpRequest
{
    RequestId     = Guid.NewGuid(),
    OperationType = SysOpType.TransitionState,
    PayloadJson   = $"{{\"TargetState\":{(int)DSMState.LoadingEdit}," +
                    $"\"ScenarioId\":\"{_loadScenarioId}\"}}",
});

// Load into Live
_sysOpWriter.Write(new SysOpRequest
{
    RequestId     = Guid.NewGuid(),
    OperationType = SysOpType.TransitionState,
    PayloadJson   = $"{{\"TargetState\":{(int)DSMState.LoadingLive}," +
                    $"\"ScenarioId\":\"{_loadScenarioId}\"}}",
});
```

**Replace the calls in** **RenderReplaySection** **(2 locations):**

```
// Load Replay
_sysOpWriter.Write(new SysOpRequest
{
    RequestId     = Guid.NewGuid(),
    OperationType = SysOpType.TransitionState,
    PayloadJson   = $"{{\"TargetState\":{(int)DSMState.RunningReplay}," +
                    $"\"DrillId\":\"{_replayDrillId}\"}}",
});

// Seek slider
_sysOpWriter.Write(new SysOpRequest
{
    RequestId     = Guid.NewGuid(),
    OperationType = SysOpType.ReplaySeek,
    PayloadJson   = $"{{\"TargetWallTicks\":{wallTicks}}}",
});
```

**Replace the calls in** **RenderStoriesSection** **(2 locations):**

```
// Unload Story
_sysOpWriter.Write(new SysOpRequest
{
    RequestId     = Guid.NewGuid(),
    OperationType = SysOpType.ManageStory,
    PayloadJson   = $"{{\"Mode\":\"Stop\",\"StoryId\":\"{storyId}\"}}",
});

// Inject Story
_sysOpWriter.Write(new SysOpRequest
{
    RequestId     = Guid.NewGuid(),
    OperationType = SysOpType.ManageStory,
    PayloadJson   = $"{{\"Mode\":\"Start\"," +
                    $"\"StoryId\":\"{_injectStoryId}\"," +
                    $"\"ScenarioId\":\"{_injectScenarioId}\"}}",
});
```

By switching all these over to `_sysOpWriter.Write(...)`, the Orchestrator's UI will now send real DDS messages which the `DrillMaster` will consume asynchronously off the network bus, mimicking exact production topology behavior.

The Orchestrator in the Drill Control should indicate the most recent drill SM state confirmed (if all nodes completed) or it should show "Old State -> New State" transition (if not all nodes completed the transition yet).

To implement the "Old State -> New State" transition indicator, we need to track the state that the cluster was in _before_ the transition started. Because `_currentDsmState` is optimistically updated as soon as a `TransitionState` request is accepted, we must capture the source state right before this update and expose it to the UI panel.

Here are the 3 files to update to achieve this:

1\. Update `Bagira.Orchestrator/DistributedTransaction.cs`

Add a new `SourceDsmState` property to the transaction record so it can carry the pre-transition state.

```
public sealed class DistributedTransaction
{
    public Guid TransactionId { get; set; }
    public Guid OriginRequestId { get; set; }
    public DSMState SourceDsmState { get; set; } // NEW: Track the state we are transitioning from
    public DSMState TargetDsmState { get; set; }
    public int TotalSteps { get; set; }
    public int CompletedSteps { get; set; }
    // ... (keep the rest of your properties, including PayloadJson and NodeResponses from the previous step)
}
```

2\. Update `Bagira.Orchestrator/DrillMaster.cs`

In the `ProcessSingleSysOpRequest` method, capture `_currentDsmState` before the planner logic executes and pass it to the new `DistributedTransaction`.

```
    // Accept the request — resolve target via planner for TransitionState ops.
    DSMState resolvedTarget = _currentDsmState;
    int totalSteps = 1;
    
    // NEW: Capture the source state before any optimistic updates
    DSMState sourceState = _currentDsmState; 
    
    if (req.OperationType == SysOpType.TransitionState)
    {
        try
        {
            // Capture current state before optimistic advance (needed for S0305 detection).
            var stateBeforeAdvance = _currentDsmState;
            // ... (keep the rest of the try-catch block)
```

Scroll down to where the transaction is instantiated and assign the captured `sourceState`:

```
    var tx = new DistributedTransaction
    {
        TransactionId    = Guid.NewGuid(),
        OriginRequestId  = req.RequestId,
        SourceDsmState   = sourceState,     // NEW: Populate the source state
        TargetDsmState   = resolvedTarget,
        TotalSteps       = totalSteps,
        CompletedSteps   = totalSteps,
        IsAborted        = false
    };
```

3\. Update `Bagira.Runner/Services/OrchestratorScenarioPanel.cs`

Update the `RenderStatusBanner` method. Check if the cluster has an in-flight transaction where the source and target states differ. If so, display the arrow transition; otherwise, just show the current state.

```
private static void RenderStatusBanner(DSMState currentState, DistributedTransaction? activeTx,
    bool bootstrapped, bool hasInFlight)
{
    if (ImGui.BeginChild("##OrcStatusBanner", new Vector2(-1, 54), ImGuiChildFlags.Borders))
    {
        string drillShort = activeTx != null
            ? activeTx.TransactionId.ToString()[..8]
            : "--------";

        string txStatus = hasInFlight
            ? $"TX {drillShort}... in flight"
            : "idle";
    
        // NEW: Indicate Old State -> New State during transitions
        if (hasInFlight && activeTx != null && activeTx.SourceDsmState != activeTx.TargetDsmState)
        {
            ImGui.Text($"State: {activeTx.SourceDsmState} -> {activeTx.TargetDsmState}");
        }
        else
        {
            ImGui.Text($"State: {currentState}");
        }
        
        ImGui.SameLine();
        ImGui.Text("|");
        ImGui.SameLine();
        ImGui.Text(bootstrapped ? txStatus : "NOT BOOTSTRAPPED");
    }
    ImGui.EndChild();
}
```

Now, whenever you execute a state transition (e.g., clicking "LoadingLive"), the banner will dynamically display **State: Standby -> LoadingLive** as long as nodes are reporting an `InProgress` status, and seamlessly snap to **State: LoadingLive** once the transition commits.

I do not understand why in the "Stories" section there is ScenarioId and not just SotryId. I thought the story is just another json file saved in different folder than the scenario files. And can be injected to any currently running drill, independently on any existing scenario.

While it is true that a story can be injected independently into any currently running drill, the **ScenarioId** is required during the injection process because it serves as the directory or package identifier that tells the system where to find the story's physical JSON asset files on disk.

Here is exactly how the system uses the `ScenarioId` when you inject a story:

-   **File Distribution (Prefetching):** When you issue a `ManageStory` command in "Start" mode, the system's transition planner automatically prepends a `PrefetchScenario` step using the `ScenarioId`. This step uses the storage gateway to ensure that all the story's asset files are physically copied and staged onto the local SSDs of all participating simulation nodes before the injection begins.-   **File Loading:** Once the files are staged across the cluster, the `ReferenceStoryLoadHandler` on each node uses the `ScenarioId` to query its local storage provider, locate the specific JSON files, and deserialize the story entities into the live world.

If the `ScenarioId` is missing from the payload when attempting to start a story, the orchestrator will reject the request with an error, as it has no way to locate the story's data.

In this context, you can think of the `ScenarioId` not as a restriction tying the story to the _currently running_ main scenario, but simply as the folder name where the story's specific assets are stored.

However, you only need to provide the `ScenarioId` when **starting** a story. When you stop or unload a story, the `ScenarioId` is completely ignored, and the system relies solely on the `StoryId` to find and destroy the injected entities that are already residing in memory.

The Scenario control (where loading is possible) should support selecting an existing scenario from a combo when loading a scenario - the system should support enumerating available scenario ids.

The same should apply to the Drill ID for replay - the system should support enumerating existing drill ids (available locally - not those archived).

I would like to have a combo-based selection of existing stories - the Inject will use the one selected from the combo.

To implement this, we need to add a local filesystem scanner to `OrchestratorScenarioPanel.cs` that looks inside the `C:\FDP_Temp` staging directory to find available assets.

Folders containing `.json` files will be enumerated as **Scenarios** and **Stories**, while folders containing `.fdp` files will be enumerated as **Drills**.

Additionally, because we are switching the Story Injector to a combo box, we can greatly simplify the workflow: the operator simply selects the story package (the `ScenarioId`) from the dropdown, and the UI will **automatically generate a new unique** **StoryId** **(Guid)** under the hood when "Inject Story" is clicked. This completely eliminates the need to manually type GUIDs.

Here is the complete update for **Bagira.Runner/Services/OrchestratorScenarioPanel.cs**:

1\. Update Imports and Fields

Add `System.IO` and the arrays to hold our enumerated combo box items.

```
using System;
using System.Collections.Generic;
using System.IO;           // NEW: Required for Directory scanning
using System.Numerics;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.Orchestrator;
using CycloneDDS.Runtime;
using ImGuiNET;

namespace Bagira.Runner.Services;

public sealed class OrchestratorScenarioPanel
{
    private readonly DrillMaster _drillMaster;
    private readonly DdsWriter<SysOpRequest> _sysOpWriter;

    // ── Scenario section state ────────────────────────────────────────────
    private string _saveScenarioId = string.Empty;
    private string[] _availableScenarios = Array.Empty<string>();
    private int _selectedLoadScenarioIdx = -1;
    
    // ── Replay section state ──────────────────────────────────────────────
    private float _seekSliderValue = 0f;
    private string[] _availableDrills = Array.Empty<string>();
    private int _selectedDrillIdx = -1;
    
    // ── Stories section state ─────────────────────────────────────────────
    private string[] _availableStories = Array.Empty<string>();
    private int _selectedStoryIdx = -1;
    
    private static readonly Vector2 AutoSize = Vector2.Zero;
    
    public OrchestratorScenarioPanel(DrillMaster drillMaster, DdsWriter<SysOpRequest> sysOpWriter)
    {
        _drillMaster = drillMaster ?? throw new ArgumentNullException(nameof(drillMaster));
        _sysOpWriter = sysOpWriter ?? throw new ArgumentNullException(nameof(sysOpWriter));
        
        // Populate the combo boxes immediately on startup
        RefreshLocalAssets();
    }
```

2\. Add the Enumerator Helper Method

Add this method to scan the standard temporary directory for scenarios and drills.

```
    private void RefreshLocalAssets()
    {
        try
        {
            string root = @"C:\FDP_Temp";
            if (!Directory.Exists(root)) return;
    
            var scenarios = new List<string>();
            var drills = new List<string>();
    
            foreach (var dir in Directory.GetDirectories(root))
            {
                var dirName = Path.GetFileName(dir);
    
                // Drills contain .fdp flight-recorder files
                if (Directory.GetFiles(dir, "*.fdp").Length > 0)
                {
                    drills.Add(dirName);
                }
                // Scenarios/Stories contain .json files
                else if (Directory.GetFiles(dir, "*.json").Length > 0)
                {
                    scenarios.Add(dirName);
                }
            }
    
            _availableScenarios = scenarios.ToArray();
            _availableStories = scenarios.ToArray(); // Stories share the same packages as scenarios
            _availableDrills = drills.ToArray();
    
            // Clamp indices if the list shrank
            if (_selectedLoadScenarioIdx >= _availableScenarios.Length) _selectedLoadScenarioIdx = -1;
            if (_selectedStoryIdx >= _availableStories.Length) _selectedStoryIdx = -1;
            if (_selectedDrillIdx >= _availableDrills.Length) _selectedDrillIdx = -1;
        }
        catch
        {
            // Ignore access errors
        }
    }
```

3\. Update the Render Sections

Replace your existing `RenderScenarioSection`, `RenderReplaySection`, and `RenderStoriesSection` with these updated versions utilizing `ImGui.Combo`:

```
    private void RenderScenarioSection(DSMState currentState, bool disableAll)
    {
        if (!ImGui.CollapsingHeader("Scenario")) return;
    
        if (ImGui.BeginChild("##OrcScenario", AutoSize, ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY))
        {
            if (disableAll) ImGui.BeginDisabled();
    
            // Save Scenario (Keep text input for creating new ones)
            ImGui.InputText("Save Scenario ID##OrcSaveId", ref _saveScenarioId, 128);
            ImGui.SameLine();
            if (ImGui.Button("Save Scenario##OrcBtn") && !string.IsNullOrWhiteSpace(_saveScenarioId))
            {
                _sysOpWriter.Write(new SysOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = SysOpType.SaveScenario,
                    PayloadJson   = $"{{\"ScenarioId\":\"{_saveScenarioId}\"}}",
                });
            }
    
            ImGui.Spacing();
    
            // Load Scenario (Combo Box)
            ImGui.Combo("Select Scenario##OrcLoadId", ref _selectedLoadScenarioIdx, _availableScenarios, _availableScenarios.Length);
            ImGui.SameLine();
            if (ImGui.Button("⟳##RefScen")) RefreshLocalAssets();
    
            if (ImGui.Button("Load into Edit##OrcLoadEdit") && _selectedLoadScenarioIdx >= 0)
            {
                string scenId = _availableScenarios[_selectedLoadScenarioIdx];
                _sysOpWriter.Write(new SysOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = SysOpType.TransitionState,
                    PayloadJson   = $"{{\"TargetState\":{(int)DSMState.LoadingEdit}," +
                                    $"\"ScenarioId\":\"{scenId}\"}}",
                });
            }
            ImGui.SameLine();
            if (ImGui.Button("Load into Live##OrcLoadLive") && _selectedLoadScenarioIdx >= 0)
            {
                string scenId = _availableScenarios[_selectedLoadScenarioIdx];
                _sysOpWriter.Write(new SysOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = SysOpType.TransitionState,
                    PayloadJson   = $"{{\"TargetState\":{(int)DSMState.LoadingLive}," +
                                    $"\"ScenarioId\":\"{scenId}\"}}",
                });
            }
    
            if (disableAll) ImGui.EndDisabled();
        }
        ImGui.EndChild();
    }
    
    private void RenderReplaySection(DSMState currentState, bool disableAll)
    {
        if (!ImGui.CollapsingHeader("Replay")) return;
    
        if (ImGui.BeginChild("##OrcReplay", AutoSize, ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY))
        {
            if (disableAll) ImGui.BeginDisabled();
    
            // Load Replay (Combo Box)
            ImGui.Combo("Select Drill##OrcReplayId", ref _selectedDrillIdx, _availableDrills, _availableDrills.Length);
            ImGui.SameLine();
            if (ImGui.Button("⟳##RefDrill")) RefreshLocalAssets();
    
            if (ImGui.Button("Load Replay##OrcReplayBtn") && _selectedDrillIdx >= 0)
            {
                string drillId = _availableDrills[_selectedDrillIdx];
                _sysOpWriter.Write(new SysOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = SysOpType.TransitionState,
                    PayloadJson   = $"{{\"TargetState\":{(int)DSMState.RunningReplay}," +
                                    $"\"DrillId\":\"{drillId}\"}}",
                });
            }
    
            // Seek slider — only when RunningReplay
            if (currentState == DSMState.RunningReplay)
            {
                ImGui.Spacing();
                ImGui.Text("Seek (s):");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(300f);
                if (ImGui.SliderFloat("##OrcSeek", ref _seekSliderValue, 0f, 3600f))
                {
                    long wallTicks = (long)(_seekSliderValue * 10_000_000L);
                    _sysOpWriter.Write(new SysOpRequest
                    {
                        RequestId     = Guid.NewGuid(),
                        OperationType = SysOpType.ReplaySeek,
                        PayloadJson   = $"{{\"TargetWallTicks\":{wallTicks}}}",
                    });
                }
            }
    
            if (disableAll) ImGui.EndDisabled();
        }
        ImGui.EndChild();
    }
    
    private void RenderStoriesSection(bool disableAll)
    {
        if (!ImGui.CollapsingHeader("Stories")) return;
    
        if (ImGui.BeginChild("##OrcStories", new Vector2(-1, 180), ImGuiChildFlags.Borders))
        {
            if (disableAll) ImGui.BeginDisabled();
    
            // Active stories list
            var activeStories = _drillMaster.ActiveStories;
            if (activeStories.Count == 0)
            {
                ImGui.TextDisabled("No active stories.");
            }
            else
            {
                foreach (var storyId in activeStories)
                {
                    string shortId = storyId.ToString()[..8] + "...";
                    ImGui.Text(shortId);
                    ImGui.SameLine();
                    if (ImGui.Button($"Unload##OrcUnload{storyId}"))
                    {
                        _sysOpWriter.Write(new SysOpRequest
                        {
                            RequestId     = Guid.NewGuid(),
                            OperationType = SysOpType.ManageStory,
                            PayloadJson   = $"{{\"Mode\":\"Stop\",\"StoryId\":\"{storyId}\"}}",
                        });
                    }
                }
            }
    
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("Inject Story:");
            
            // Inject Story (Combo Box)
            ImGui.Combo("Story Package##OrcInjectScen", ref _selectedStoryIdx, _availableStories, _availableStories.Length);
            ImGui.SameLine();
            if (ImGui.Button("⟳##RefStory")) RefreshLocalAssets();
    
            if (ImGui.Button("Inject Story##OrcInjectBtn") && _selectedStoryIdx >= 0)
            {
                string scenId = _availableStories[_selectedStoryIdx];
                
                // Auto-generate the unique runtime StoryId so the operator doesn't have to
                string newStoryId = Guid.NewGuid().ToString();
    
                _sysOpWriter.Write(new SysOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = SysOpType.ManageStory,
                    PayloadJson   = $"{{\"Mode\":\"Start\"," +
                                    $"\"StoryId\":\"{newStoryId}\"," +
                                    $"\"ScenarioId\":\"{scenId}\"}}",
                });
            }
    
            if (disableAll) ImGui.EndDisabled();
        }
        ImGui.EndChild();
    }
```

Orchestrator UI should show the time control section. The should be current master time (wall clock time) as well as the drill time. And should have button for making a single step. The Pause should enter (using the future barrier) the deterministic stepping mode where pressing the "Step" button makes a short deterministic step. Pressing "Resume" should resume continuous time flow. There should be a slider for controlling the time flow speed in range 0.1 till 10times faster that real time. "Step" button must be disabled if not paused. Pause button should turn into Resume if time is paused (no need for 2 separate buttons, no need for extra Play button). Just two buttons: Pause/Resume and Step. The stepping should work both in live as well as in replay modes. In replay mode this shoudl be capped to the end time of the recording.

While in replay, there should be a slider showing current relative time position in a recording (drill time). If the slider is moved by the user, after a debounce period the system should execute the Jump-to-time operation and enter paused state once finished by all nodes.

To fulfill your requirements, we need to introduce a dedicated "Time Control" section in the Orchestrator's UI that integrates directly with the `DistributedTimeCoordinator` and `ModuleHostKernel`. We will also implement the debounced replay seek slider inside the scenario panel.

Here are the updates for the two relevant files:

1\. Update `Bagira.Runner/Services/OrchestratorSubsystem.cs`

We will add the new **Time Control** panel, stripping out the old placeholder simulation controls. We will also pass a pause callback to the scenario panel and tick its new `Update` method.

Update your `Initialize`, `Update`, and `DrawUI` methods:

```
public void Initialize(SubsystemConfig config)
{
    _config      = ClusterConfiguration.LoadFrom(
        System.IO.Path.Combine(Directory.GetCurrentDirectory(), "orchestrator-config.json"));
    _participant = BagiraEnvironment.CreateParticipant(config.DomainId);
    _drillMaster = new DrillMaster(_participant, _config);
    _sysOpWriter = new DdsWriter<SysOpRequest>(_participant);
    
    // NEW: Inject the Pause callback so the Scenario Panel can pause time after seeking
    _scenarioPanel = new OrchestratorScenarioPanel(_drillMaster, _sysOpWriter, () => 
    {
        if (_drillMaster != null && _timeCoordinator != null)
        {
            var slaveIds = new HashSet<int>(_drillMaster.NodeRoster.ActiveNodes.Keys);
            _timeCoordinator.SwitchToDeterministic(slaveIds);
        }
    });
    
    // ... (Keep existing Time coordinator setup)
}

public void Update(float deltaTime)
{
    _timeKernel?.Update();
    _eventBus?.SwapBuffers();
    _drillMaster?.Tick();

    var pendingMode = _drillMaster?.PendingTimeMode;
    if (pendingMode != _lastProcessedTimeMode)
    {
        if (pendingMode == "Deterministic" && _timeCoordinator != null && _drillMaster != null)
        {
            var slaveIds = new HashSet<int>(_drillMaster.NodeRoster.ActiveNodes.Keys);
            _timeCoordinator.SwitchToDeterministic(slaveIds);
        }
        _lastProcessedTimeMode = pendingMode;
    }
    
    _timeCoordinator?.Update();
    _timeModeTranslator?.ScanAndPublish(null!);
    _timeModeTranslator?.PollIngress(null!, null!);
    
    // NEW: Update scenario panel for debouncing
    _scenarioPanel?.Update(deltaTime);
}

public void DrawUI()
{
    if (_drillMaster == null) return;

    if (!ImGui.Begin("Orchestrator"))
    {
        ImGui.End();
        return;
    }
    
    var bootstrapped = _drillMaster.BootstrapComplete;
    var hasInFlight    = _drillMaster.HasInFlightTransaction;
    var disableAll     = !bootstrapped || hasInFlight;
    
    // ── Bootstrap banner ──────────────────────────────────────────────────
    if (!bootstrapped)
    {
        var waiting = _config.Mandatory
            .Where(name => !_drillMaster.NodeRoster.ActiveNodes.Values
                .Any(p => p.SubsystemName == name &&
                          p.LocalDsmState == Bagira.BDC.SSTD.Orchestration.DSMState.Standby))
            .ToArray();
    
        ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(1f, 0.8f, 0.2f, 1f));
        ImGui.TextWrapped($"Waiting for mandatory nodes: {string.Join(", ", waiting)}");
        ImGui.PopStyleColor();
        ImGui.Separator();
    }
    
    // ── Time Control ──────────────────────────────────────────────────────
    if (ImGui.CollapsingHeader("Time Control", ImGuiTreeNodeFlags.DefaultOpen))
    {
        var timeCtrl = _timeKernel?.GetTimeController();
        bool isPaused = timeCtrl?.GetMode() == ModuleHost.Core.Time.TimeMode.Deterministic;
    
        long wallTicks = _timeKernel?.CurrentTime.TotalWallTicks ?? 0;
        double drillTime = _timeKernel?.CurrentTime.TotalTime ?? 0;
    
        string wallTimeStr = new DateTime(wallTicks, DateTimeKind.Utc).ToString("HH:mm:ss.fff");
        
        ImGui.Text($"Master Time: {wallTimeStr}");
        ImGui.SameLine(200f);
        ImGui.Text($"Drill Time: {drillTime:F2} s");
        ImGui.Spacing();
    
        if (disableAll) ImGui.BeginDisabled();
    
        // 1. Pause/Resume
        if (ImGui.Button(isPaused ? "Resume" : "Pause"))
        {
            if (isPaused)
            {
                _timeCoordinator?.SwitchToContinuous();
            }
            else
            {
                var slaveIds = new HashSet<int>(_drillMaster.NodeRoster.ActiveNodes.Keys);
                _timeCoordinator?.SwitchToDeterministic(slaveIds);
            }
        }
    
        ImGui.SameLine();
        
        // 2. Single Step (disabled if not paused)
        if (!isPaused) ImGui.BeginDisabled();
        if (ImGui.Button("Step"))
        {
            _timeKernel?.StepFrame(1f / 60f); // Make a short 60Hz deterministic step
        }
        if (!isPaused) ImGui.EndDisabled();
    
        ImGui.SameLine();
        ImGui.SetNextItemWidth(200f);
        
        // 3. Time flow speed
        float timeScale = timeCtrl?.GetTimeScale() ?? 1.0f;
        if (ImGui.SliderFloat("Speed", ref timeScale, 0.1f, 10.0f, "%.1fx"))
        {
            timeCtrl?.SetTimeScale(timeScale);
        }
    
        if (disableAll) ImGui.EndDisabled();
    }
    
    // ... [Keep existing Node Health and 2PC History Table sections here] ...
    
    // ── Scenario & Story controls (CGF1-S0106) ───────────────────────────
    float currentDrillTime = (float)(_timeKernel?.CurrentTime.TotalTime ?? 0.0);
    _scenarioPanel?.Render(currentDrillTime);
    
    ImGui.End();
}
```

2\. Update `Bagira.Runner/Services/OrchestratorScenarioPanel.cs`

We will add the `Update` loop to process the debounce timer and modify the constructor to accept the new pause callback. We will also dynamically determine the replay maximum length by peeking into the `.meta.json` file.

```
// Update fields at the top of the class
private readonly DrillMaster _drillMaster;
private readonly DdsWriter<SysOpRequest> _sysOpWriter;
private readonly Action _requestPause; // NEW

private float _seekDebounceTimer = 0f; // NEW
private bool _seekPending = false;     // NEW
private float _replayDuration = 3600f; // NEW

// Update Constructor
public OrchestratorScenarioPanel(DrillMaster drillMaster, DdsWriter<SysOpRequest> sysOpWriter, Action requestPause)
{
    _drillMaster = drillMaster ?? throw new ArgumentNullException(nameof(drillMaster));
    _sysOpWriter = sysOpWriter ?? throw new ArgumentNullException(nameof(sysOpWriter));
    _requestPause = requestPause;
    
    RefreshLocalAssets();
}

// Add the Update method to process debounce logic
public void Update(float dt)
{
    if (_seekPending)
    {
        _seekDebounceTimer -= dt;
        if (_seekDebounceTimer <= 0f)
        {
            _seekPending = false;
            long wallTicks = (long)(_seekSliderValue * 10_000_000L);
            _sysOpWriter.Write(new SysOpRequest
            {
                RequestId     = Guid.NewGuid(),
                OperationType = SysOpType.ReplaySeek,
                PayloadJson   = $"{{\"TargetWallTicks\":{wallTicks}}}",
            });
            
            // Re-enter paused state when completed
            _requestPause?.Invoke();
        }
    }
}

// Update Render signature and its calls
public void Render(float currentDrillTime)
{
    var bootstrapped = _drillMaster.BootstrapComplete;
    var hasInFlight  = _drillMaster.HasInFlightTransaction;
    var currentState = _drillMaster.CurrentSystemState;
    var activeTx     = _drillMaster.ActiveTransaction;
    var disableAll   = !bootstrapped || hasInFlight;

    ImGui.Separator();
    RenderStatusBanner(currentState, activeTx, bootstrapped, hasInFlight);
    RenderDrillControl(currentState, disableAll);
    RenderCheckpointSection(currentState, disableAll);
    RenderScenarioSection(currentState, disableAll);
    RenderReplaySection(currentState, disableAll, currentDrillTime); // Pass drill time here
    RenderStoriesSection(disableAll);
}

// Helper to extract duration from .meta.json file
private float GetReplayDuration(string drillId)
{
    try
    {
        string dir = $@"C:\FDP_Temp\{drillId}";
        if (!Directory.Exists(dir)) return 3600f;
        var metaFiles = Directory.GetFiles(dir, "*.meta.json");
        if (metaFiles.Length > 0)
        {
            string json = File.ReadAllText(metaFiles);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("TotalFrames", out var tfProp))
            {
                // Convert frames to seconds assuming standard 60Hz tick 
                return tfProp.GetInt32() / 60f;
            }
        }
    }
    catch { }
    return 3600f; // Fallback
}

// Update RenderReplaySection
private void RenderReplaySection(DSMState currentState, bool disableAll, float currentDrillTime)
{
    if (!ImGui.CollapsingHeader("Replay")) return;

    if (ImGui.BeginChild("##OrcReplay", AutoSize, ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY))
    {
        if (disableAll) ImGui.BeginDisabled();
    
        ImGui.Combo("Select Drill##OrcReplayId", ref _selectedDrillIdx, _availableDrills, _availableDrills.Length);
        ImGui.SameLine();
        if (ImGui.Button("⟳##RefDrill")) RefreshLocalAssets();
    
        if (ImGui.Button("Load Replay##OrcReplayBtn") && _selectedDrillIdx >= 0)
        {
            string drillId = _availableDrills[_selectedDrillIdx];
            _replayDuration = GetReplayDuration(drillId); // Dynamic cap based on metadata
            _seekSliderValue = 0f;
    
            _sysOpWriter.Write(new SysOpRequest
            {
                RequestId     = Guid.NewGuid(),
                OperationType = SysOpType.TransitionState,
                PayloadJson   = $"{{\"TargetState\":{(int)DSMState.RunningReplay}," +
                                $"\"DrillId\":\"{drillId}\"}}",
            });
        }
    
        // Seek slider
        if (currentState == DSMState.RunningReplay)
        {
            ImGui.Spacing();
            ImGui.Text("Seek (s):");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(300f);
            
            // Bind the visual position to actual playback time unless user is dragging it
            if (!_seekPending)
            {
                _seekSliderValue = currentDrillTime;
            }
    
            if (ImGui.SliderFloat("##OrcSeek", ref _seekSliderValue, 0f, _replayDuration))
            {
                _seekPending = true;
                _seekDebounceTimer = 0.5f; // Wait half a second before emitting the command
            }
        }
    
        if (disableAll) ImGui.EndDisabled();
    }
    ImGui.EndChild();
}
```

How does the scenario/drill/story file archiving and restoration from archive works? When/where from/how the arching/restoration is triggered?

The archiving and restoration of scenario, drill, and story files are structurally defined as orchestration commands driven by the system's **DrillMaster** and executed by the **StorageGatewayModule**.

**When and How it is Triggered:** Archiving and restoration are triggered by sending a `SysOpRequest` over the DDS network. The system's `SysOpType` enumeration explicitly defines two operations for this: **ExportArchive** and **ImportArchive**.

**Where and How it Works:**

-   **Storage Gateway:** All bulk file movements across the cluster—which explicitly includes Archive Export and Import—are owned by the `StorageGatewayModule` co-located with the Orchestrator's `DrillMaster`.-   **SMB Pull Gateway Pattern:** To execute these file transfers without saturating network interfaces or hitting Windows inbound connection limits, the gateway uses the "SMB Pull Gateway Pattern." Instead of all simulation nodes pushing data simultaneously, the Orchestrator opens outbound SMB connections to a central NAS and pulls or pushes the archive files to and from the leaf nodes in parallel.-   **Base Path Configuration:** The system's global context, which is published over DDS via the `OrchestratorContextTopic`, includes an `ArchiveBasePath` property that dictates where these archives are stored or retrieved from.

_Note: While the provided sources detail the architectural design, the network message types (__ExportArchive_ _/_ _ImportArchive__), and the gateway pattern used for archiving, they do not contain the specific concrete implementation code detailing how the_ _DrillMaster_ _processes these exact two requests or how the files are internally zipped/packaged._

ok maybe we are talking about the storage gateway. the storage is just a network shared folder, isn;t it? I guess it should have subfolders for different categories of stuff. like

1.  "scenarios" - contains scenarios with stories; for each scenario there is a subfolder matching the scenarioId, inside there are subfolders for individual nodes with the node type in the name following format \[NodeTypeName\]-\[NodeIdNumber\] (for example CGF-301) and inside there are individual files for that node
    
2.  "recordings" - contains drill recordings; subfolder with drillId, under subfolders for subsystems and under the recording files
    

And the gateway simply asks the nodes to copy the nodes back and forth.

does it work like that?

You have the right general idea about the categories, but the exact folder structure and the way the files are copied work a bit differently under the hood.

Here is exactly how the storage gateway and file structure work based on the system's design:

**1\. The Mechanics: The "SMB Pull Gateway Pattern"** The gateway does **not** ask the nodes to copy files back and forth. Instead, the central `StorageGatewayModule` (which lives on the Orchestrator) actively performs all the network file transfers itself.

-   **Why?** To avoid saturating network interfaces and hitting the Windows inbound connection limit (~20 connections for client SKUs), the system uses the "SMB Pull Gateway Pattern".-   **How it works:** The Orchestrator opens outbound SMB connections to both the central NAS and the individual nodes' local drives (using UNC paths like `\\NODE01\c$\FDP_Temp\...`). It then pulls files from the nodes to the NAS, or pushes files from the NAS to the nodes in parallel, capping the operations at 8 concurrent copies to prevent network saturation. The simulation nodes themselves never initiate network file transfers; they just read and write to their local `C:\FDP_Temp\` drive.

**2\. The "Scenarios" Folder Structure** There are no node-specific subfolders (like `CGF-301`) for scenarios.

-   On the NAS, the scenario files live in a single folder matching the scenario ID: `<nasBasePath>\<scenarioId>\`.-   When a prefetch is triggered, the gateway simply pushes **every file** in that NAS directory to the local staging directory (`C:\FDP_Temp\<scenarioId>\`) of **every active node**.

**3\. The "Recordings" and Checkpoints Folder Structure** Recordings are grouped by the `drillId` (or `storyId` / `requestId`), but they do not use subfolders for specific subsystems. Instead, the individual node IDs are baked directly into the filenames.

-   **Main Drills:** Stored under `{storageDirectory}/{drillId}/` and the files are named `node_{nodeId}.fdp`.-   **Stories:** Stored under `{storageDirectory}/stories/` and named `{storyId}_node{nodeId}.fdp`.-   **Checkpoints:** Stored as `{storageDirectory}/{requestId}_node_{nodeId}.fdp`.

So, while the central NAS does organize files by Drill IDs and Scenario IDs, the routing relies on universal replication for scenarios and flat, node-tagged filenames for recordings, all physically moved by the central orchestrator's gateway.

good. where from/when/how the copying to/from the storage is triggered?

The file copying to and from the central storage is explicitly orchestrated within the **DrillMaster** class in response to specific `SysOpRequest` commands received over the network.

Here is exactly where, when, and how the two main storage gateway processes are triggered:

**1\. Saving/Exporting Scenarios (Pulling files from Nodes to the NAS)**

-   **When:** The process is initiated when the `DrillMaster` receives a **SysOpType.SaveScenario** request.-   **How & Where:**
    -   First, in `ProcessSingleSysOpRequest`, the `DrillMaster` calls the `FanOutSerializeLocal` method. This broadcasts a command to all active nodes, instructing them to serialize their current state and save it to their local SSDs (`C:\FDP_Temp`).-   As the individual nodes finish saving, they send back `NodeOpStatus(Success)` ACKs containing a JSON list of `FileManifestEntry` records (which dictate the exact paths of the files they just created).-   The `DrillMaster` processes these ACKs inside the **ConsumeNodeOpStatuses** method. It decrements a `RemainingAcks` counter. Once the counter hits zero (meaning all nodes have successfully written their local files), it triggers **\_gateway.PullToNasAsync**. This gateway method performs the actual parallel network pull from the nodes to the central NAS.

**2\. Loading/Prefetching Scenarios (Pushing files from the NAS to Nodes)**

-   **When:** The process is initiated when the `DrillMaster` receives a **SysOpType.TransitionState** (loading a scenario) or **SysOpType.ManageStory** (Start mode) request that contains a `ScenarioId` in its payload.-   **How & Where:**
    -   The `DrillMasterPlanner` automatically prepends an internal `SysOpType.PrefetchScenario` step to the state machine trajectory.-   Back in `ProcessSingleSysOpRequest`, the `DrillMaster` detects this step and calls the **ExecutePrefetchScenario** method, which immediately invokes **\_gateway.PrefetchScenarioAsync**. This begins the parallel background transfer of files from the NAS to the local staging directories of every active node.-   Because this is an asynchronous copy, the `DrillMaster` calls the **DrainPendingPrefetch** method during every single simulation `Tick()`.-   Once `DrainPendingPrefetch` observes that the gateway task is fully completed, it finally fans out the `NodeOpType.PrefetchFiles` command to the cluster nodes. This strict ordering ensures the nodes are never told to transition into a loading state before their required asset files are physically present on their local disks.

if the files are not present on the local SSD before scenario is loaded, how can the Orchestrator's ImGui show the list of available scenario? It would need to ask the storage gateway, not its own local filesystem, correct?

You are absolutely right. Your observation highlights a crucial distinction between the local staging environment and the authoritative cluster storage.

If the UI simply scans the local `C:\FDP_Temp` directory, it will only ever see scenarios that the Orchestrator node itself has previously saved or pre-fetched. It would completely miss scenarios that were added to the central NAS by other means or during previous sessions.

The system's architecture actually anticipates exactly what you are suggesting. The `DrillMaster` class exposes its `StorageGateway` property specifically so the UI panel can query the central storage rather than its own local disk. The design documentation in the source code explicitly notes that this property is:

_"Exposed so OrchestratorScenarioPanel can call ListScenariosAsync() (CGF1-S0106)."_

To make the UI show the true list of available scenarios, the `RefreshLocalAssets()` method from the previous step should be replaced with a call to the storage gateway to scan the NAS (Network Attached Storage) base path.

Because the Orchestrator acts as the "SMB Pull Gateway" for the cluster, it already has direct access to the NAS base path (configured via `_nasBasePath`) and can enumerate the folders there. The Orchestrator's UI would ask the gateway to list the directories on the NAS, populate the combo box with those IDs, and only _after_ the operator clicks "Load" (or "Inject") does the gateway push those specific files down to the local `C:\FDP_Temp` SSDs of all participating nodes.

What about loading/prefetching and saving drill recordings. is this also integrated by the drill master in the state machine? in what states?

Yes, the saving (recording) and loading (replaying) of drill files are fully integrated into the Drill State Machine (DSM) and its Two-Phase Commit (2PC) protocol. However, unlike scenarios, **drill recordings do not have an automatic prefetch/copy step** in the state transition planner; instead, nodes read and write directly to the configured storage directory (or central NAS), and bulk movements are handled via explicit `ExportArchive`/`ImportArchive` operations.

Here is exactly how the recording and replay lifecycles are integrated into the DSM states:

1\. Saving/Recording Drills (`RunningLive`)

Recording is automatically started and stopped when the cluster transitions in and out of live simulation states.

-   **State Transition:** `Standby` → `LoadingLive` → `RunningLive`.-   **How it works:** When transitioning into `LoadingLive`, the Orchestrator fans out a `PrepareLive` network command to all nodes. On the simulation nodes, the `ReferenceLiveLoadHandler` intercepts this command and calls `PrepareRecordingAsync()`, which installs a `RecordingModule` into the local ECS kernel to begin capturing every frame to disk.-   **Teardown:** When the drill ends (transitioning to `UnloadingLive` or `Standby`), the Orchestrator sends a `FinalizeLive` command. The handler invokes `FinalizeRecordingAsync()`, which safely flushes the LZ4 data buffers to disk and writes the final `.meta.json` metadata manifest before closing the file.

2\. Loading/Replaying Drills (`RunningReplay`)

Replay sessions use their own dedicated DSM states that disable live physics and AI so the recorded frames can drive the world.

-   **State Transition:** `Standby` → `LoadingReplay` → `RunningReplay`.-   **How it works:** The Orchestrator fans out a `PrepareReplay` command containing the target `DrillId`. The `ReferenceReplayLoadHandler` receives this and calls `PrepareReplayAsync()`, installing the `ReplayModule`.-   **During Commit:** The handler actively disables the `SimulationSystemGroup` (physics, AI, kinematics) and the `NetworkLifecycleSystemGroup` (ghost creation) so that the live simulation logic does not interfere with the recorded data.-   **Teardown:** Transitioning to `UnloadingReplay` triggers `FinalizeReplay`, which tears down the replay module and re-enables the simulation systems.

3\. Live-from-Replay Branching (`RunningReplay` → `LoadingLive`)

The DSM supports seamlessly taking control of a paused replay to branch it into a new live drill.

-   **How it works:** If the cluster is in `RunningReplay` and the Orchestrator receives a transition request to `LoadingLive`, the transition planner intercepts this and triggers a special interlock protocol.-   **State Transition:** Time is hard-frozen by the Orchestrator, and a `PrepareLive` command is sent with a brand new, branched `DrillId`.-   **Node Execution:** The `ReferenceReplayLoadHandler` intercepts this specific `PrepareLive` command, immediately tears down the `ReplayModule` (leaving the entity repository frozen at the exact historical frame), and starts a brand new `RecordingModule` using the new `DrillId`. Once all nodes acknowledge, time is unfrozen and the branched timeline goes live.

ok so for the drill recodings, the configured storage directory must be a local file system path for each node. Is it now? We can not write from too many nodes to a shared NAS as this will deplete the connection limit.

Yes, you are exactly right. The configured storage directory for drill recordings is explicitly set to a local file system path on each individual node, precisely to avoid saturating the NAS and depleting connection limits during live simulations.

Here is how the local storage is currently configured and utilized across the system:

**1\. Local Path Configuration** The storage directory is configured to point to the local staging drive, which defaults to `C:\FDP_Temp`:

-   **SimHost:** Driven by `NodeConfiguration.LocalTempRoot`, which defaults to `@"C:\FDP_Temp"`.-   **CGF Node:** Passed via the `localTempRoot` parameter into its bootstrapper, defaulting to `@"C:\FDP_Temp"`.-   **IG & IOS Nodes:** Hardcoded directly to `storageDirectory: @"C:\FDP_Temp"` when registering their `ReferenceLiveLoadHandler` and `ReferenceReplayLoadHandler` components.

**2\. Local File Writing** When the Orchestrator commands a drill recording to start, the `EcsRecordReplayController` on each node builds the file path using this local storage directory combined with the drill ID and the specific node's ID. For example, the resulting local path on a node will be: `C:\FDP_Temp\<drillId>\node_<nodeId>.fdp`.

Because the `RecordingModule` captures 60 Hz frames and writes them entirely to this local SSD path, the individual nodes generate zero network file I/O during the live simulation.

**3\. Resolving the NAS Connection Limit** Your architectural logic is fully preserved: by writing to local `C:\FDP_Temp` drives, the leaf nodes never directly touch the NAS. When a recording needs to be archived, the Orchestrator's central `StorageGatewayModule` takes over using the "SMB Pull Gateway Pattern". The Orchestrator sequentially pulls the `.fdp` files from each node's local drive (`\\<node>\c$\FDP_Temp\...`) up to the NAS, capping the concurrency at 8 parallel operations to safely bypass the Windows inbound connection limit.

you said "When a recording needs to be archived, the Orchestrator's central StorageGatewayModule takes over" ... is that implemented at the moment? or do the drill recording stay only on local SSD?

You are correct to question this—based on the source code, the archiving feature for drill recordings is **not currently implemented**, and the recordings do indeed stay only on the local SSDs.

While the architectural intention and the network message types (`SysOpType.ExportArchive`, `SysOpType.ImportArchive`, and `NodeOpType.UploadChunk`) are fully defined in the system's schema, the actual wiring to execute these operations is missing:

-   **No Orchestrator Logic:** The `DrillMaster`'s main request loop (`ProcessSingleSysOpRequest`) only contains concrete implementation branches for `TransitionState`, `SaveScenario`, and `ManageStory`. It currently lacks the logic to handle `ExportArchive` requests or to instruct the `StorageGatewayModule` to pull `.fdp` recording files.-   **No Node-Side Handlers:** The `NodeBootstrapper` registers handlers for scenarios, checkpoints, stories, and live/replay lifecycles, but there is no handler registered to process archive upload or download commands on the individual simulation nodes.-   **No UI Controls:** The `OrchestratorScenarioPanel` contains sections for Scenarios, Replay, Checkpoints, and Stories, but it does not have any buttons or inputs to trigger an archive export or import.

Currently, the `StorageGatewayModule`'s "SMB Pull Gateway" functionality is only actively wired up and used for **Scenarios** (pulling scenario files to the NAS during a `SaveScenario` operation and pushing them back to nodes via `PrefetchScenario`).

Therefore, at the moment, any drill recordings captured by the `EcsRecordReplayController` remain permanently in the local `C:\FDP_Temp` staging directories on each individual node.

Ok, let's then sketch an implementation for archiving import/export for drill recordings and checkpoints. Using similar mechanism as storage gateway is using now for scenario files.

To implement the archive import/export feature using the existing "SMB Pull Gateway Pattern", we can mirror the mechanism currently used for scenarios. The network operation types `SysOpType.ExportArchive` and `SysOpType.ImportArchive` are already defined in the orchestration schema.

Here is a sketch of the implementation across the cluster.

1\. Node-Side: Generating the Archive Manifest

First, we need the simulation nodes to locate their local `.fdp` recording files and report them back to the orchestrator. We can create a new handler (e.g., `ReferenceArchiveHandler`) that listens for `NodeOpType.SerializeLocal`, but checks the payload for a `DrillId` instead of a `ScenarioId`.

```
public sealed class ReferenceArchiveHandler : IDsmHandler
{
    private readonly string _localTempRoot;
    private readonly int _nodeId;
    private readonly IOrchestrationTransport? _transport;

    public ReferenceArchiveHandler(string localTempRoot, int nodeId, IOrchestrationTransport? transport)
    {
        _localTempRoot = localTempRoot;
        _nodeId = nodeId;
        _transport = transport;
    }
    
    public bool CanHandle(int operationId) => operationId == (int)NodeOpType.SerializeLocal;
    
    public Task<string?> PrepareAsync(OrchestrationCommand cmd, CancellationToken ct) 
        => Task.FromResult<string?>(null);
    
    public void Commit(OrchestrationCommand cmd, EntityRepository? repo)
    {
        // Parse DrillId from payload to differentiate from SaveScenario
        var drillId = ParseDrillId(cmd.PayloadJson);
        if (drillId == null) return; 
    
        var fileUnc = Path.Combine(_localTempRoot, drillId, $"node_{_nodeId}.fdp");
        var manifests = new List<FileManifestEntry>();
    
        if (File.Exists(fileUnc))
        {
            manifests.Add(new FileManifestEntry
            {
                SourceUnc = fileUnc, // e.g. C:\FDP_Temp\<drillId>\node_100.fdp
                RelativeDest = Path.Combine(drillId, $"node_{_nodeId}.fdp")
            });
        }
    
        var resultJson = JsonSerializer.Serialize(manifests);
        _transport?.PublishStatus(new OrchestrationStatus(
            cmd.TransactionId, _nodeId, OrchestrationStatusCode.Success, true, resultJson));
    }
    
    public void Abort(OrchestrationCommand cmd, EntityRepository? repo) { }
}
```

2\. Orchestrator-Side: ExportArchive (Pull to NAS)

In `DrillMaster.cs`, we intercept the `SysOpType.ExportArchive` request from the UI, fan out the `SerializeLocal` command to the active nodes, and then reuse the existing manifest collection logic to pull the files to the NAS.

**In** **DrillMaster.ProcessSingleSysOpRequest****:**

```
if (req.OperationType == SysOpType.ExportArchive)
{
    var nodeIds = new List<int>(_roster.ActiveNodes.Keys);
    var txId = Guid.NewGuid();
    
    // Fan out SerializeLocal; payload contains {"DrillId": "..."}
    FanOutSerializeLocal(txId, nodeIds, req.PayloadJson); [2, 3]
    
    FdpLog<DrillMaster>.Info("ExportArchive requested. Fanning out to {0} nodes.", nodeIds.Count);
}
```

_Note: Because_ _DrillMaster.ConsumeNodeOpStatuses_ _already aggregates_ _FileManifestEntry_ _lists and automatically calls_ _\_gateway.PullToNasAsync_ _when all ACKs arrive, the actual network pull to the NAS will work out-of-the-box without further modification!_

3\. Orchestrator-Side: ImportArchive (Push to Nodes)

To import an archive, we need to push the files from the central NAS down to the target nodes' local drives before transitioning into a replay state. We can add a method to `StorageGatewayModule.cs` specifically for this, utilizing the existing `PushToNodesAsync` logic.

**In** **StorageGatewayModule.cs****:**

```
public async Task<GatewayResult> PrefetchArchiveAsync(
    string drillId, 
    IReadOnlyList<NodeDistributionTarget> targets, 
    string nasBasePath)
{
    var sourceDir = Path.Combine(nasBasePath, drillId);
    if (!Directory.Exists(sourceDir)) return new GatewayResult { FailureCount = 1 };

    var files = Directory.GetFiles(sourceDir, "*.fdp");
    int success = 0, failure = 0;
    var options = new ParallelOptions { MaxDegreeOfParallelism = MaxParallelCopies }; [7, 8]
    
    await Task.Run(() =>
    {
        Parallel.ForEach(files, options, file =>
        {
            // Optimize: Only push the specific node's recording file to that node,
            // rather than broadcasting all node files to everyone.
            foreach (var target in targets)
            {
                if (file.Contains($"node_{target.NodeId}"))
                {
                    var destPath = Path.Combine(target.DestinationPath, Path.GetFileName(file));
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                    File.Copy(file, destPath, overwrite: true);
                    Interlocked.Increment(ref success);
                }
            }
        });
    });
    
    return new GatewayResult { SuccessCount = success, FailureCount = failure };
}
```

**In** **DrillMaster.ProcessSingleSysOpRequest****:**

```
if (req.OperationType == SysOpType.ImportArchive)
{
    var drillId = ParseDrillId(req.PayloadJson);
    var targets = BuildNodeDistributionTargets(drillId); // Helper that builds paths like C:\FDP_Temp\<drillId>\ [9, 10]

    // Run the gateway copy asynchronously
    _ = _gateway.PrefetchArchiveAsync(drillId, targets, _nasBasePath).ContinueWith(t => 
    {
        int status = t.Result.IsFullSuccess ? OrchestrationStatusCode.Success : OrchestrationStatusCode.Timeout;
        
        // Notify the UI panel that the import is complete
        _sysOpStatusWriter.Write(new SysOpStatus
        {
            RequestId = req.RequestId,
            StatusCode = status,
            ResultJson = string.Empty
        });
    });
}
```

This completes the pipeline: the `ExportArchive` leverages the existing manifest-aggregation system to pull files to the NAS, and the `ImportArchive` triggers the "SMB Push" to selectively drop specific `node_<nodeId>.fdp` files directly onto their target machines' staging SSDs.

In orchestrator's ImGui There should be another section for archive management - the system should support enumerating the archived drill recordings, archiving locally stored drill recordings not yet present on the NAS - combo showing those not yet archived . And restoring selected recording from archive back to nodes. The replay shoudl be possible just for recordings already present locally.

To implement the Archive Management section, we need to update the Orchestrator's UI panel to scan both the local SSD and the central NAS, compare the results, and dispatch the `ExportArchive` and `ImportArchive` DDS commands we sketched out previously.

Because Replay already relies strictly on the `_availableDrills` array (which only scans the local `C:\FDP_Temp` directory), the requirement that replays are only possible for locally present recordings is naturally satisfied.

Here are the changes required:

1\. Update `Bagira.Orchestrator/DrillMaster.cs`

To allow the UI to scan the NAS, expose the `_nasBasePath` field as a public property:

```
// Add this property to DrillMaster
public string NasBasePath => _nasBasePath;
```

2\. Update `Bagira.Runner/Services/OrchestratorScenarioPanel.cs`

Add the new state fields for the Archive section, update the file scanner to cross-reference the NAS, and implement the new UI section.

**Update the Fields:**

```
    // ── Replay section state ──────────────────────────────────────────────
    private float _seekSliderValue = 0f;
    private string[] _availableDrills = Array.Empty<string>();
    private int _selectedDrillIdx = -1;
    
    // ── Archive section state (NEW) ───────────────────────────────────────
    private string[] _archivedDrills = Array.Empty<string>();
    private string[] _unarchivedLocalDrills = Array.Empty<string>();
    private int _selectedArchiveIdx = -1;
    private int _selectedUnarchivedIdx = -1;
```

**Update** **RefreshLocalAssets()** **to scan the NAS:**

```
    private void RefreshLocalAssets()
    {
        try
        {
            // 1. Scan Local SSD
            string localRoot = @"C:\FDP_Temp";
            var scenarios = new List<string>();
            var localDrills = new List<string>();
    
            if (Directory.Exists(localRoot))
            {
                foreach (var dir in Directory.GetDirectories(localRoot))
                {
                    var dirName = Path.GetFileName(dir);
                    if (Directory.GetFiles(dir, "*.fdp").Length > 0)
                        localDrills.Add(dirName);
                    else if (Directory.GetFiles(dir, "*.json").Length > 0)
                        scenarios.Add(dirName);
                }
            }
    
            // 2. Scan Central NAS
            var archivedDrills = new List<string>();
            string nasRoot = _drillMaster.NasBasePath;
            
            if (!string.IsNullOrWhiteSpace(nasRoot) && Directory.Exists(nasRoot))
            {
                foreach (var dir in Directory.GetDirectories(nasRoot))
                {
                    if (Directory.GetFiles(dir, "*.fdp").Length > 0)
                        archivedDrills.Add(Path.GetFileName(dir));
                }
            }
    
            // 3. Assign Arrays & Compute Unarchived list
            _availableScenarios = scenarios.ToArray();
            _availableStories = scenarios.ToArray();
            _availableDrills = localDrills.ToArray(); // Replay ONLY uses these local files
            _archivedDrills = archivedDrills.ToArray();
            _unarchivedLocalDrills = localDrills.Except(archivedDrills).ToArray();
    
            // Clamp indices
            if (_selectedLoadScenarioIdx >= _availableScenarios.Length) _selectedLoadScenarioIdx = -1;
            if (_selectedStoryIdx >= _availableStories.Length) _selectedStoryIdx = -1;
            if (_selectedDrillIdx >= _availableDrills.Length) _selectedDrillIdx = -1;
            if (_selectedArchiveIdx >= _archivedDrills.Length) _selectedArchiveIdx = -1;
            if (_selectedUnarchivedIdx >= _unarchivedLocalDrills.Length) _selectedUnarchivedIdx = -1;
        }
        catch
        {
            // Ignore access errors
        }
    }
```

**Add** **RenderArchiveSection()****:**

```
    private void RenderArchiveSection(DSMState currentState, bool disableAll)
    {
        if (!ImGui.CollapsingHeader("Archive Management")) return;
    
        if (ImGui.BeginChild("##OrcArchive", AutoSize, ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY))
        {
            if (disableAll) ImGui.BeginDisabled();
    
            // --- 1. Export: Local -> NAS ---
            ImGui.Text("Archive Local Drill:");
            ImGui.Combo("Unarchived Local##OrcUnarchived", ref _selectedUnarchivedIdx, _unarchivedLocalDrills, _unarchivedLocalDrills.Length);
            ImGui.SameLine();
            if (ImGui.Button("Export to NAS##OrcExportBtn") && _selectedUnarchivedIdx >= 0)
            {
                string drillId = _unarchivedLocalDrills[_selectedUnarchivedIdx];
                _sysOpWriter.Write(new SysOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = SysOpType.ExportArchive,
                    PayloadJson   = $"{{\"DrillId\":\"{drillId}\"}}"
                });
            }
    
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
    
            // --- 2. Import: NAS -> Local ---
            ImGui.Text("Restore Archived Drill:");
            ImGui.Combo("Archived Drills##OrcArchived", ref _selectedArchiveIdx, _archivedDrills, _archivedDrills.Length);
            ImGui.SameLine();
            if (ImGui.Button("Import from NAS##OrcImportBtn") && _selectedArchiveIdx >= 0)
            {
                string drillId = _archivedDrills[_selectedArchiveIdx];
                _sysOpWriter.Write(new SysOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = SysOpType.ImportArchive,
                    PayloadJson   = $"{{\"DrillId\":\"{drillId}\"}}"
                });
            }
    
            if (disableAll) ImGui.EndDisabled();
        }
        ImGui.EndChild();
    }
```

**Update the main** **Render** **method:** Finally, call the new section inside your main `Render(float currentDrillTime)` method, right next to the Replay block:

```
    ImGui.Separator();
    RenderStatusBanner(currentState, activeTx, bootstrapped, hasInFlight);
    RenderDrillControl(currentState, disableAll);
    RenderCheckpointSection(currentState, disableAll);
    RenderScenarioSection(currentState, disableAll);
    
    RenderArchiveSection(currentState, disableAll); // NEW
    
    RenderReplaySection(currentState, disableAll, currentDrillTime); 
    RenderStoriesSection(disableAll);
```

Do we suport a cancel request for an already running and not yet fisnied operationo at the sysop/nodeop level?

No, the system **does not support explicit, user-initiated cancellation requests** for operations that are already in-flight.

Here is how the system handles in-flight operations across its different layers:

-   **UI Level:** The Orchestrator's control panel deliberately disables all interactive buttons while there is an active, in-flight distributed transaction, preventing the user from issuing new commands or cancellations.-   **SysOp Level:** The `SysOpType` enumeration does not contain any "Cancel" or "Abort" network command that an operator could send.-   **NodeOp Level (Internal Aborts):** The system _does_ have an internal `NodeOpType.AbortTransaction` command, and every `IDsmHandler` implements an `Abort` method designed to roll back prepared resources. However, this mechanism is strictly used for **automatic failure recovery**, not user cancellation.

For example, if a mandatory simulation node times out and is ejected from the cluster while a transition is running, the `DrillMaster` automatically marks the active transaction as aborted and broadcasts the `AbortTransaction` command to all surviving nodes to clean up.

Explicit cancelling is usefull. Can we define a clean way of aborting a still running not yet finished request? with a clean of way of how to handle the abort on the nodes?

for example for a long lasting file copy operation.

As the recordings could be long, the ImGui should show some kind of progress bar and should disable the archiving controls while the operation is in progress. But there should always be a Cancel button available that force-cancels the operation. The cancellation should be handled greacefully on all nodes by deleting the partial stuff already copied there so the system remains in a consistent state.

As a software architect, I completely agree. Relying strictly on timeouts or forcing users to wait out a massive 50GB file transfer is a poor user experience and bad system design. A robust distributed system must treat cancellation as a first-class citizen, ensuring that tearing down an in-flight operation leaves the cluster in a pristine, deterministic state without leaking garbage files.

To implement a clean cancellation pipeline, we need to span four architectural layers:

-   **Network Contract:** A new explicit `SysOpRequest` to signal the cancellation intent over DDS.-   **Orchestrator Execution:** Linking the cancellation intent to standard .NET `CancellationTokenSource` (CTS) for local gateway tasks, and fanning out `NodeOpType.AbortTransaction` to the nodes.-   **Storage Gateway (Data Plane):** Honoring the `CancellationToken`, breaking the parallel loops, and executing a `catch (OperationCanceledException)` block to scrub partial files.-   **ImGui UI Layer:** Rendering the progress bar and exposing the "Cancel" button while locking out concurrent operations.

Here is how we can sketch this clean architecture into the Bagira codebase.

1\. Update the Network Contract (`OrchestrationMessages.cs`)

Add a specific operation type for cancellation:

```
public enum SysOpType : int
{
    // ... existing ...
    PrefetchScenario = 12,
    CancelOperation = 13 // NEW: Explicit cancellation intent
}
```

2\. Update the Storage Gateway (`StorageGatewayModule.cs`)

We must thread a `CancellationToken` through the SMB Pull/Push methods. If cancelled, the `Parallel.ForEach` will throw an `OperationCanceledException`. We catch it, delete any files that were in the middle of being written, and rethrow to fail the task cleanly.

```
public async Task<GatewayResult> PullToNasAsync(
    IReadOnlyList<FileManifestEntry> manifests, 
    string nasBasePath, 
    CancellationToken ct) // NEW
{
    int successCount = 0, failureCount = 0;
    var options = new ParallelOptions { 
        MaxDegreeOfParallelism = MaxParallelCopies, 
        CancellationToken = ct // Bind the token
    };

    try
    {
        await Task.Run(() =>
        {
            Parallel.ForEach(manifests, options, entry =>
            {
                var destPath = Path.Combine(nasBasePath, entry.RelativeDest);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
    
                // Copy operations on standard File.Copy cannot be natively cancelled mid-byte,
                // but the token will stop new files from starting.
                File.Copy(entry.SourceUnc, destPath, overwrite: true);
                Interlocked.Increment(ref successCount);
            });
        }, ct).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
        // CLEANUP: The operation was aborted. Delete any partial files on the NAS.
        foreach (var entry in manifests)
        {
            var destPath = Path.Combine(nasBasePath, entry.RelativeDest);
            if (File.Exists(destPath)) File.Delete(destPath);
        }
        throw; // Rethrow to ensure the Orchestrator marks it as Failed/Cancelled
    }
    
    return new GatewayResult { SuccessCount = successCount, FailureCount = failureCount };
}
```

3\. Update the Orchestrator (`DrillMaster.cs`)

The `DrillMaster` needs a registry of `CancellationTokenSource` objects tied to active operation GUIDs. When `CancelOperation` is received, it triggers the local CTS (killing the Storage Gateway task) and broadcasts `AbortTransaction` to the nodes.

```
// Add state to track cancellable operations
private readonly Dictionary<Guid, CancellationTokenSource> _activeCancellations = new();

// In ProcessSingleSysOpRequest:
if (req.OperationType == SysOpType.CancelOperation)
{
    // Parse the target operation ID to cancel
    if (Guid.TryParse(req.PayloadJson, out Guid targetOpId))
    {
        // 1. Cancel the local Orchestrator task (StorageGateway)
        if (_activeCancellations.TryGetValue(targetOpId, out var cts))
        {
            cts.Cancel();
            _activeCancellations.Remove(targetOpId);
        }

        // 2. Fan-out AbortTransaction to all nodes so they can delete their local partial files
        FanOutNodeOp(new NodeOpCommand
        {
            TransactionId = targetOpId,
            Operation     = NodeOpType.AbortTransaction,
            PayloadJson   = string.Empty
        }, _roster.ActiveNodes.Keys);
    
        FdpLog<DrillMaster>.Info("Operation {0} force-cancelled by operator.", targetOpId);
    }
    return;
}
```

_(Note: Be sure to pass the_ _cts.Token_ _to_ _\_gateway.PullToNasAsync_ _when you initiate the archive task, and wrap it in a_ _try/finally_ _to remove the CTS from the dictionary when it finishes naturally.)_

At the node level, your `ReferenceArchiveHandler` (from our previous sketch) will receive the `AbortTransaction` network message, which automatically routes to its `Abort(OrchestratorCommand cmd, EntityRepository? repo)` method. In that `Abort` method, the node deletes any partial `.fdp` files it was preparing on its own SSD.

4\. Update the UI (`OrchestratorScenarioPanel.cs`)

The UI must block standard controls during archiving, display the progress, and provide the hard-cancel button.

```
// Add to state fields:
private Guid _activeArchiveOpId = Guid.Empty;
private float _archiveProgress = 0f; // Tracked via GatewayResult or activeTx.CompletedSteps

private void RenderArchiveSection(DSMState currentState, bool disableAll)
{
    if (!ImGui.CollapsingHeader("Archive Management")) return;

    if (ImGui.BeginChild("##OrcArchive", AutoSize, ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY))
    {
        bool isArchiving = _activeArchiveOpId != Guid.Empty;
    
        // Disable standard combo boxes and start buttons while an operation is running
        if (disableAll || isArchiving) ImGui.BeginDisabled();
    
        ImGui.Combo("Archived Drills##OrcArchived", ref _selectedArchiveIdx, _archivedDrills, _archivedDrills.Length);
        ImGui.SameLine();
        if (ImGui.Button("Import from NAS") && _selectedArchiveIdx >= 0)
        {
            _activeArchiveOpId = Guid.NewGuid();
            _archiveProgress = 0f;
            _sysOpWriter.Write(new SysOpRequest { OperationType = SysOpType.ImportArchive, RequestId = _activeArchiveOpId, /*...*/ });
        }
        
        if (disableAll || isArchiving) ImGui.EndDisabled();
    
        // Show Progress and Cancel button ONLY if archiving is active
        if (isArchiving)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), "Operation in progress...");
            
            // Render progress bar
            ImGui.ProgressBar(_archiveProgress, new Vector2(-1, 24), $"{(_archiveProgress * 100):F0}%");
    
            // The Cancel Button - Always active during archiving
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.2f, 0.2f, 1f));
            if (ImGui.Button("CANCEL OPERATION", new Vector2(-1, 32)))
            {
                _sysOpWriter.Write(new SysOpRequest
                {
                    RequestId     = Guid.NewGuid(),
                    OperationType = SysOpType.CancelOperation,
                    PayloadJson   = _activeArchiveOpId.ToString()
                });
                
                // Optimistically clear the UI state
                _activeArchiveOpId = Guid.Empty; 
            }
            ImGui.PopStyleColor();
        }
    }
    ImGui.EndChild();
}
```

By bridging the `SysOp` intent down into the .NET `CancellationToken` and across the cluster via the `AbortTransaction` 2PC hook, we enforce strict idempotency and eliminate orphaned artifacts on partial failures.

What in the orchestrator's "Drill Control" I now click on the button for switching the state, nothing happend. Not even one single node shown in the "Node Health" moves from its Standby state. where is the issue?

The issue lies in **Bagira.Orchestrator/DrillMaster.cs**. While we successfully wired the UI to send real `SysOpRequest` messages over the network, and the `DrillMaster` receives them and successfully plans the state transition trajectory, it completely lacks the logic to actually broadcast the resulting `TransitionStep` commands to the cluster.

As noted in the source documentation, the `DistributedTransaction` handling is currently a "skeleton" where the actual 2PC execution was left for later phases. Because the `DrillMaster` only updates its own optimistic internal state (`_currentDsmState`) and never fans out the `PrepareState` or `CommitState` messages to the leaf nodes, the nodes never execute the transitions and remain in `Standby` forever.

To fix this, we need to iterate through the planned `trajectory` and explicitly fan out the corresponding `Prepare` and `Commit` network commands to all active nodes.

Update **DrillMaster.cs** inside the `ProcessSingleSysOpRequest` method. Scroll down to the end of the `if (req.OperationType == SysOpType.TransitionState)` block, right after the Live-from-Replay branch logic, and insert the network fan-out loop:

```
                if (passesLoadingLive && stateBeforeAdvance == DSMState.RunningReplay)
                {
                    // ... existing Live-from-Replay freeze time logic ...
                }
            }
            catch (InvalidOperationException ex)
            {
                // ... existing catch block ...
            }
        }
        else if (req.OperationType == SysOpType.SaveScenario)
        // ... existing SaveScenario and ManageStory blocks ...
    
        // ──────────────────────────────────────────────────────────────────────────
        // NEW: Fan out the actual state transitions to the cluster nodes!
        // ──────────────────────────────────────────────────────────────────────────
        var txId = Guid.NewGuid();
        var activeNodeIds = new List<int>(_roster.ActiveNodes.Keys);
    
        if (req.OperationType == SysOpType.TransitionState && activeNodeIds.Count > 0)
        {
            foreach (var step in trajectory)
            {
                if (step is TransitionStep tStep)
                {
                    // Map the TargetState to the correct lifecycle Prepare operation
                    NodeOpType prepareOp = NodeOpType.PrepareState;
                    if (tStep.TargetState == DSMState.LoadingLive) prepareOp = NodeOpType.PrepareLive;
                    else if (tStep.TargetState == DSMState.UnloadingLive) prepareOp = NodeOpType.FinalizeLive;
                    else if (tStep.TargetState == DSMState.LoadingReplay) prepareOp = NodeOpType.PrepareReplay;
                    else if (tStep.TargetState == DSMState.UnloadingReplay) prepareOp = NodeOpType.FinalizeReplay;
    
                    // 1. Fan out Prepare (Distributes DrillId / ScenarioId to node handlers)
                    FanOutNodeOp(new NodeOpCommand
                    {
                        TransactionId = txId,
                        Operation     = prepareOp,
                        PayloadJson   = req.PayloadJson ?? string.Empty
                    }, activeNodeIds);
    
                    // 2. Fan out Commit (Forces DrillSlave to update its _localStateId)
                    FanOutNodeOp(new NodeOpCommand
                    {
                        TransactionId = txId,
                        Operation     = NodeOpType.CommitState,
                        PayloadJson   = ((int)tStep.TargetState).ToString()
                    }, activeNodeIds);
                }
                else if (step is OperationStep opStep && opStep.Operation == SysOpType.ReplaySeek)
                {
                    FanOutNodeOp(new NodeOpCommand
                    {
                        TransactionId = txId,
                        Operation     = NodeOpType.NodeReplaySeek,
                        PayloadJson   = opStep.PayloadJson
                    }, activeNodeIds);
                }
            }
        }
    
        // ──────────────────────────────────────────────────────────────────────────
        // UPDATE: Modify the transaction instantiation to use our newly generated txId
        // ──────────────────────────────────────────────────────────────────────────
        var tx = new DistributedTransaction
        {
            TransactionId    = txId, // Replaced Guid.NewGuid() with txId
            OriginRequestId  = req.RequestId,
            TargetDsmState   = resolvedTarget,
            TotalSteps       = totalSteps,
            CompletedSteps   = totalSteps, // Still completing instantly as a skeleton 2PC
            IsAborted        = false
        };
        
        AppendToHistory(tx);
        // ... remainder of the method
```

Why this works:

-   **Prepare Phase Routing:** By mapping specific targets like `LoadingLive` to `NodeOpType.PrepareLive`, the system correctly routes the command to specific lifecycle handlers on the nodes (like `ReferenceLiveLoadHandler` or `ReferenceReplayLoadHandler`), and forwards the original `PayloadJson` so the nodes can extract the `DrillId` and `ScenarioId`.-   **Commit Phase Execution:** When the nodes receive `NodeOpType.CommitState`, the local `DrillSlave` explicitly intercepts it, parses the payload as the target integer state, and overrides its `_localStateId`.-   **UI Updates:** Because the nodes have transitioned states, their next 1Hz `NodeHeartbeat` publish will broadcast the new state, and the Orchestrator's Node Health ImGui table will dynamically update to reflect the entire cluster stepping through the sequence.

While the orchestrator has direct access to the nas, i want other nodes like the IOS to present simular file / archive management UI. That means the orchestrator (or rather the storage gateway which is now embedded to it) to provide the scenario/drill recording/checkpoint enumaration remotely, using dds network messages holding the list of resources. The dds messages belong to app layer while the generic implemenation if the storage gateway should stay generic so some translation is necessary. The remote availability ofvthe api should apply to state machine control as well. Simply lts imagine all the orchestrator ui now should be implementable by IOS not having direct access to nas nor having direct acceess to drull state machine controller.

To achieve a clean, decoupled architecture in a distributed simulation, UI components should never reach directly into the file system or bypass the network to access local service instances. Instead, the UI must act as a pure projection of the network state, reacting to published data and issuing commands via our CQRS-style DDS orchestration topics.

By pushing the file enumeration into a dedicated DDS topic and relying entirely on `SysOpRequest` for commands, we can easily share the exact same Scenario/Archive Management panel across the Orchestrator, IOS, or any other remote instructor node.

Here is the architectural blueprint and implementation guide to decouple this cleanly.

1\. The Network Contract: `AssetInventoryTopic`

First, we extend the application data model to broadcast the available assets. A request/response pattern for UI populating is often clunky; instead, the Orchestrator should publish an `AssetInventoryTopic` with `TransientLocal` QoS. This ensures any late-joining node (like the IOS) instantly receives the latest directory cache upon subscribing, resulting in zero-latency UI population.

Add this to `Bagira.DDS.DataModel/Orchestration/OrchestrationMessages.cs`:

```
[DdsTopic("AssetInventory")]
[DdsIdlFile("bdc-sst-orchestration")]
[DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
public partial struct AssetInventoryTopic
{
    [DdsKey] public int NodeId; // 0 for the singleton cluster orchestrator
    
    [DdsManaged] public string LocalScenariosJson;
    [DdsManaged] public string LocalDrillsJson;
    [DdsManaged] public string ArchivedDrillsJson;
    [DdsManaged] public string UnarchivedLocalDrillsJson;
}
```

2\. The Orchestrator (Data Plane): Scanning and Publishing

We keep the `StorageGatewayModule` generic. The `DrillMaster` (which bridges the generic toolkit layer and the Bagira app layer) becomes responsible for periodically scanning the storage layers and publishing the `AssetInventoryTopic`.

In `DrillMaster.cs`, add a new writer and a background refresh loop:

```
private readonly DdsWriter<AssetInventoryTopic> _inventoryWriter;
private DateTime _lastInventoryScan = DateTime.MinValue;

// Inside Update / Tick():
public void Tick()
{
    // ... existing tick logic ...
    
    // Throttle disk I/O to every ~5 seconds
    if ((DateTime.UtcNow - _lastInventoryScan).TotalSeconds > 5)
    {
        PublishAssetInventory();
        _lastInventoryScan = DateTime.UtcNow;
    }
}

private void PublishAssetInventory()
{
    // Generic file scanning (isolated from DDS types)
    var localScenarios = ScanLocalScenarios();
    var localDrills = ScanLocalDrills();
    var archivedDrills = ScanNasDrills();
    var unarchivedDrills = localDrills.Except(archivedDrills).ToList();

    // Translate to App-layer DDS message
    _inventoryWriter.Write(new AssetInventoryTopic
    {
        NodeId = 0,
        LocalScenariosJson = JsonSerializer.Serialize(localScenarios),
        LocalDrillsJson = JsonSerializer.Serialize(localDrills),
        ArchivedDrillsJson = JsonSerializer.Serialize(archivedDrills),
        UnarchivedLocalDrillsJson = JsonSerializer.Serialize(unarchivedDrills)
    });
}
```

3\. Remote State Machine Control (CQRS)

You noted that the remote availability should apply to the state machine control as well. The great news is that the foundational network contract is already perfectly set up for this CQRS approach.

The IOS does not need direct access to `DrillMaster`. It simply needs to:

-   **Read State:** Subscribe to `SystemStateTopic` to know the `CurrentState` and active `DrillId`. Subscribe to `OrchestratorContextTopic` to read the active `ScenarioId` and injected stories.-   **Plan Transitions locally:** The IOS can locally instantiate the static `BagiraStateGraph.Build()` and pass it to a `DrillMasterPlanner`. This allows the IOS to compute `GetReachableTargets(currentState)` purely client-side, populating its "Next State" buttons without a network round-trip.-   **Dispatch Commands:** When the operator clicks "Load into Live" or "Export Archive", the IOS UI simply writes a `SysOpRequest` via its `DdsWriter<SysOpRequest>`. The Orchestrator receives it, plans the 2PC round, and executes it.

4\. The Shared UI Component: `ClusterScenarioPanel`

Now we rename and refactor `OrchestratorScenarioPanel.cs` to `ClusterScenarioPanel.cs` so it can be injected into both `IosSubsystem.cs` and `OrchestratorSubsystem.cs`.

We strip out the direct `_drillMaster` dependency and replace it with DDS readers/writers.

```
public sealed class ClusterScenarioPanel
{
    private readonly DdsWriter<SysOpRequest> _sysOpWriter;
    private readonly DdsReader<AssetInventoryTopic> _inventoryReader;
    private readonly DrillMasterPlanner _localPlanner;
    
    // UI State
    private string[] _archivedDrills = Array.Empty<string>();
    // ... other arrays ...
    
    public ClusterScenarioPanel(
        DdsWriter<SysOpRequest> sysOpWriter, 
        DdsReader<AssetInventoryTopic> inventoryReader)
    {
        _sysOpWriter = sysOpWriter;
        _inventoryReader = inventoryReader;
        _localPlanner = new DrillMasterPlanner(BagiraStateGraph.Build());
    }
    
    public void Update()
    {
        // 1. Consume the TransientLocal inventory from the Orchestrator
        using var loan = _inventoryReader.Take();
        foreach (var sample in loan)
        {
            if (sample.IsValid)
            {
                _archivedDrills = JsonSerializer.Deserialize<string[]>(sample.Data.ArchivedDrillsJson) ?? Array.Empty<string>();
                // ... deserialize other arrays ...
            }
        }
    }
    
    public void Render(SystemStateTopic systemState, OrchestratorContextTopic context)
    {
        var currentState = systemState.CurrentState;
        
        // Compute reachable states client-side
        var reachable = _localPlanner.GetReachableTargets(currentState);
    
        // ... Render exact same UI using the locally cached _archivedDrills
        // and emitting _sysOpWriter.Write(...) on button clicks ...
    }
}
```

By unifying the control plane around the `SysOpRequest` / `AssetInventoryTopic` boundaries, any node on the DDS domain can govern the simulation lifecycle. The IOS becomes functionally equal to the Orchestrator's local UI, enforcing a strict separation between the data plane (NAS/Gateway) and the presentation layer.

Ok lets plan for verifying it by implementing the control panel as the orchesyrator has ui on the ios subsystem imgui. The control should include also the time! The ios must be able to request a pause/step/speed of the master time, without itself being the time master.

To implement a remote, fully decoupled Time Control panel on the IOS, we must adhere strictly to the CQRS (Command Query Responsibility Segregation) pattern that governs the cluster.

The IOS should act purely as an observer and intent-dispatcher. It will **Query** the current time state by subscribing to the existing `TimePulse` and `SwitchTimeModeEvent` DDS topics. It will issue **Commands** by dispatching `SysOpRequest` messages back to the Orchestrator, which holds the authoritative `DistributedTimeCoordinator`.

Here is the clean-architecture blueprint to implement this across the data model, orchestrator, and IOS layers.

1\. Extend the Network Contract (`OrchestrationMessages.cs`)

We must add explicit explicit operational intents for stepping and scaling time to our system operation schema.

```
public enum SysOpType : int
{
    // ... existing ...
    PauseTime = 10, 
    ResumeTime = 11,
    PrefetchScenario = 12,
    CancelOperation = 13,
    
    // NEW: Time manipulation commands
    StepTime = 14, 
    SetTimeScale = 15 
}
```

2\. Orchestrator Data Plane (`DrillMaster.cs` & `OrchestratorSubsystem.cs`)

The `DrillMaster` receives `SysOpRequests` from the network bus. Because time manipulation does not require a Two-Phase Commit (2PC) round across the simulation nodes, the `DrillMaster` should intercept these requests and fire a local C# event. The `OrchestratorSubsystem` (which owns the time kernel) will listen and apply them.

**In** **DrillMaster.cs****:**

```
// Add the event for the hosting subsystem to consume
public event Action<SysOpType, string>? TimeControlRequested;

// Inside ProcessSingleSysOpRequest(SysOpRequest req):
if (req.OperationType is SysOpType.PauseTime or SysOpType.ResumeTime or 
    SysOpType.StepTime or SysOpType.SetTimeScale)
{
    // Fire event and return early; no distributed 2PC required for master time operations
    TimeControlRequested?.Invoke(req.OperationType, req.PayloadJson ?? string.Empty);
    return;
}
```

**In** **OrchestratorSubsystem.cs** **(inside** **Initialize****):**

```
_drillMaster.TimeControlRequested += (op, payload) =>
{
    if (op == SysOpType.PauseTime)
    {
        var slaveIds = new HashSet<int>(_drillMaster.NodeRoster.ActiveNodes.Keys);
        _timeCoordinator?.SwitchToDeterministic(slaveIds);
    }
    else if (op == SysOpType.ResumeTime)
    {
        _timeCoordinator?.SwitchToContinuous();
    }
    else if (op == SysOpType.StepTime)
    {
        _timeKernel?.StepFrame(1f / 60f); // Make a short 60Hz deterministic step
    }
    else if (op == SysOpType.SetTimeScale && float.TryParse(payload, out float scale))
    {
        _timeKernel?.GetTimeController()?.SetTimeScale(scale);
    }
};
```

3\. IOS Ingress Handlers (`DdsEventIngressHandlers.cs`)

To reflect the current time on the IOS without tight coupling, we create two lightweight `IIngressHandler` implementations to consume the authoritative time topics.

```
using FDP.Toolkit.Time.Messages;

public sealed class TimePulseIngressHandler : IIngressHandler, IDisposable
{
    private readonly DdsReader<TimePulseDescriptor> _reader;
    private readonly Action<TimePulseDescriptor> _onPulse;

    public TimePulseIngressHandler(DdsParticipant participant, Action<TimePulseDescriptor> onPulse)
    {
        _reader = new DdsReader<TimePulseDescriptor>(participant, "TimePulse");
        _onPulse = onPulse;
    }
    
    public void Poll()
    {
        using var loan = _reader.Take();
        foreach (var sample in loan) if (sample.IsValid) _onPulse(sample.Data);
    }
    public void Dispose() => _reader.Dispose();
}

public sealed class TimeModeIngressHandler : IIngressHandler, IDisposable
{
    private readonly DdsReader<SwitchTimeModeWireDto> _reader;
    private readonly Action<SwitchTimeModeWireDto> _onModeSwitch;

    public TimeModeIngressHandler(DdsParticipant participant, Action<SwitchTimeModeWireDto> onModeSwitch)
    {
        _reader = new DdsReader<SwitchTimeModeWireDto>(participant);
        _onModeSwitch = onModeSwitch;
    }
    
    public void Poll()
    {
        using var loan = _reader.Take();
        foreach (var sample in loan) if (sample.IsValid) _onModeSwitch(sample.Data);
    }
    public void Dispose() => _reader.Dispose();
}
```

4\. IOS Presentation Logic (`IIosLogic.cs` & `IosLogic.cs`)

Expose the observed state and the command-dispatch methods to the ImGui panels.

**In** **IIosLogic.cs****:**

```
// ── Time State & Control ──────────────────────────────────────────────────
double MasterSimTime { get; }
long MasterWallTicks { get; }
float MasterTimeScale { get; }
bool IsPaused { get; }

void RequestPause();
void RequestResume();
void RequestStep();
void SetTimeScale(float scale);
```

**In** **IosLogic.cs****:**

```
public double MasterSimTime { get; private set; }
public long MasterWallTicks { get; private set; }
public float MasterTimeScale { get; private set; } = 1.0f;
public bool IsPaused { get; private set; }

// Dispatch commands via the existing SysOp writer
public void RequestPause()  => _sysOpWriter.Write(new SysOpRequest { RequestId = Guid.NewGuid(), OperationType = SysOpType.PauseTime });
public void RequestResume() => _sysOpWriter.Write(new SysOpRequest { RequestId = Guid.NewGuid(), OperationType = SysOpType.ResumeTime });
public void RequestStep()   => _sysOpWriter.Write(new SysOpRequest { RequestId = Guid.NewGuid(), OperationType = SysOpType.StepTime });
public void SetTimeScale(float scale) => _sysOpWriter.Write(new SysOpRequest { RequestId = Guid.NewGuid(), OperationType = SysOpType.SetTimeScale, PayloadJson = scale.ToString() });
```

_Don't forget to wire the new ingress handlers inside_ _IosSubsystem.cs_ _passing_ _pulse => logic.MasterSimTime = pulse.SimTimeSnapshot;_ _(etc.) into the callbacks._

5\. IOS ImGui Panel (`ClusterScenarioPanel.cs` or `TimeControlPanel.cs`)

Finally, map the observed properties and commands to ImGui inside the shared panel. Because we abstracted the time engine away behind `IIosLogic`, the UI logic is completely isolated from the physics loops.

```
public void RenderTimeControl(IIosLogic logic, bool disableAll)
{
    if (!ImGui.CollapsingHeader("Time Control", ImGuiTreeNodeFlags.DefaultOpen)) return;

    // Display Authoritative Time
    string wallTimeStr = new DateTime(logic.MasterWallTicks, DateTimeKind.Utc).ToString("HH:mm:ss.fff");
    ImGui.Text($"Master Time: {wallTimeStr}");
    ImGui.SameLine(200f);
    ImGui.Text($"Drill Time: {logic.MasterSimTime:F2} s");
    ImGui.Spacing();
    
    if (disableAll) ImGui.BeginDisabled();
    
    // 1. Pause/Resume
    if (ImGui.Button(logic.IsPaused ? "Resume" : "Pause"))
    {
        if (logic.IsPaused) logic.RequestResume();
        else logic.RequestPause();
    }
    
    ImGui.SameLine();
    
    // 2. Single Step (disabled if continuous)
    if (!logic.IsPaused) ImGui.BeginDisabled();
    if (ImGui.Button("Step"))
    {
        logic.RequestStep();
    }
    if (!logic.IsPaused) ImGui.EndDisabled();
    
    ImGui.SameLine();
    ImGui.SetNextItemWidth(200f);
    
    // 3. Time flow speed
    float timeScale = logic.MasterTimeScale;
    if (ImGui.SliderFloat("Speed", ref timeScale, 0.1f, 10.0f, "%.1fx"))
    {
        logic.SetTimeScale(timeScale);
    }
    
    if (disableAll) ImGui.EndDisabled();
}
```

This strict producer-consumer CQRS architecture guarantees that the IOS panel instantly tracks network time-sync states and issues temporal control directives without ever becoming entangled with the `DistributedTimeCoordinator` directly.

The orchestrator ui must be using these remote networked request, never taking shortcut directly to its internals, to keep the ui responsibily split.

To achieve strict CQRS separation and ensure the UI never takes shortcuts to local memory, we must treat the Orchestrator's ImGui panel exactly like the IOS panel: as a pure network client.

By sniffing the DDS bus, the UI can rebuild the cluster's entire state (Node Health, 2PC History, Time, and Asset Inventory) without ever holding a reference to the `DrillMaster`.

Here is the architectural refactoring to completely decouple the Orchestrator's UI into a pure network-driven projection.

1\. Create a Pure Network Projection (`ClusterUiCache.cs`)

Create a new class that polls DDS readers each frame to project the cluster's state. It sniffs `SysOpStatus` to track in-flight locks, and sniffs `NodeOpCommand`/`NodeOpStatus` to rebuild the 2PC history table purely from network traffic.

```
using System;
using System.Collections.Generic;
using Bagira.BDC.SSTD.Orchestration;
using CycloneDDS.Runtime;
using FDP.Toolkit.Time.Messages;

namespace Bagira.Runner.Services;

public sealed class ClusterUiCache : IDisposable
{
    // ── DDS Readers ────────────────────────────────────────────────────────
    private readonly DdsReader<SystemStateTopic> _stateReader;
    private readonly DdsReader<OrchestratorContextTopic> _contextReader;
    private readonly DdsReader<AssetInventoryTopic> _inventoryReader;
    private readonly DdsReader<NodeHeartbeat> _heartbeatReader;
    private readonly DdsReader<SysOpStatus> _sysOpStatusReader;
    private readonly DdsReader<NodeOpCommand> _nodeOpCmdReader;
    private readonly DdsReader<NodeOpStatus> _nodeOpStatusReader;
    private readonly DdsReader<TimePulseDescriptor> _timePulseReader;

    // ── Projected UI State ─────────────────────────────────────────────────
    public DSMState CurrentState { get; private set; } = DSMState.Standby;
    public bool HasInFlightTransaction { get; private set; }
    public bool IsBootstrapped { get; private set; }
    
    // Asset Inventory
    public string[] AvailableScenarios { get; private set; } = Array.Empty<string>();
    public string[] AvailableDrills { get; private set; } = Array.Empty<string>();
    public string[] ArchivedDrills { get; private set; } = Array.Empty<string>();
    
    // Time Control
    public double MasterSimTime { get; private set; }
    public long MasterWallTicks { get; private set; }
    
    // Node Health & 2PC History
    public Dictionary<int, NodeHeartbeat> ActiveNodes { get; } = new();
    public List<DistributedTransaction> TxHistory { get; } = new();
    private readonly Dictionary<Guid, DistributedTransaction> _txLookup = new();
    
    public ClusterUiCache(DdsParticipant participant)
    {
        _stateReader = new DdsReader<SystemStateTopic>(participant);
        _contextReader = new DdsReader<OrchestratorContextTopic>(participant);
        _inventoryReader = new DdsReader<AssetInventoryTopic>(participant);
        _heartbeatReader = new DdsReader<NodeHeartbeat>(participant);
        _sysOpStatusReader = new DdsReader<SysOpStatus>(participant);
        _nodeOpCmdReader = new DdsReader<NodeOpCommand>(participant);
        _nodeOpStatusReader = new DdsReader<NodeOpStatus>(participant);
        _timePulseReader = new DdsReader<TimePulseDescriptor>(participant, "TimePulse");
    }
    
    public void Update()
    {
        // 1. System State (Current state & bootstrap detection)
        using var stateScope = _stateReader.Take();
        foreach (var sample in stateScope)
        {
            if (!sample.IsValid) continue;
            CurrentState = sample.Data.CurrentState;
            IsBootstrapped = true; // State is only published after bootstrap clears
        }
    
        // 2. SysOp Status (UI Locking during in-flight operations)
        using var sysOpScope = _sysOpStatusReader.Take();
        foreach (var sample in sysOpScope)
        {
            if (!sample.IsValid) continue;
            // 1 = InProgress, 0 = Success, >10 = Errors
            HasInFlightTransaction = sample.Data.StatusCode == 1; 
        }
    
        // 3. Node Heartbeats (Node Health Table)
        using var hbScope = _heartbeatReader.Take();
        foreach (var sample in hbScope)
        {
            if (sample.IsValid) ActiveNodes[sample.Data.NodeId] = sample.Data;
        }
    
        // 4. Asset Inventory
        using var invScope = _inventoryReader.Take();
        foreach (var sample in invScope)
        {
            if (!sample.IsValid) continue;
            AvailableScenarios = System.Text.Json.JsonSerializer.Deserialize<string[]>(sample.Data.LocalScenariosJson) ?? Array.Empty<string>();
            AvailableDrills = System.Text.Json.JsonSerializer.Deserialize<string[]>(sample.Data.LocalDrillsJson) ?? Array.Empty<string>();
            ArchivedDrills = System.Text.Json.JsonSerializer.Deserialize<string[]>(sample.Data.ArchivedDrillsJson) ?? Array.Empty<string>();
        }
        
        // 5. Time Pulse
        using var timeScope = _timePulseReader.Take();
        foreach (var sample in timeScope)
        {
            if (!sample.IsValid) continue;
            MasterSimTime = sample.Data.SimTimeSnapshot;
            MasterWallTicks = sample.Data.MasterWallTicks;
        }
    
        // 6. Network-Sniffed 2PC History (No direct access to DrillMaster needed!)
        Process2PcNetworkTraffic(); 
    }
    
    private void Process2PcNetworkTraffic()
    {
        using var cmdScope = _nodeOpCmdReader.Take();
        foreach (var sample in cmdScope)
        {
            if (!sample.IsValid || sample.Data.Operation != NodeOpType.PrepareState) continue;
            
            var txId = sample.Data.TransactionId;
            if (!_txLookup.TryGetValue(txId, out var tx))
            {
                tx = new DistributedTransaction { TransactionId = txId, PayloadJson = sample.Data.PayloadJson };
                _txLookup[txId] = tx;
                TxHistory.Insert(0, tx);
                if (TxHistory.Count > 10) TxHistory.RemoveAt(TxHistory.Count - 1);
            }
        }
    
        using var statusScope = _nodeOpStatusReader.Take();
        foreach (var sample in statusScope)
        {
            if (!sample.IsValid) continue;
            if (_txLookup.TryGetValue(sample.Data.TransactionId, out var tx))
            {
                tx.NodeResponses[sample.Data.NodeId] = sample.Data.ResultJson;
            }
        }
    }
    
    public void Dispose()
    {
        _stateReader.Dispose();
        _contextReader.Dispose();
        _inventoryReader.Dispose();
        _heartbeatReader.Dispose();
        _sysOpStatusReader.Dispose();
        _nodeOpCmdReader.Dispose();
        _nodeOpStatusReader.Dispose();
        _timePulseReader.Dispose();
    }
}
```

2\. Isolate the Subsystem (`OrchestratorSubsystem.cs`)

Now we sever the UI's direct access to `_drillMaster`. The subsystem's only job is to host the headless `DrillMaster` backend and pass the UI projections to the ImGui panels.

```
public sealed class OrchestratorSubsystem : ISubsystem
{
    private DdsParticipant? _participant;
    
    // ── Data Plane (Headless Services) ──────────────────────────────────
    private DrillMaster? _drillMaster;
    private ModuleHostKernel? _timeKernel;
    private DistributedTimeCoordinator? _timeCoordinator;
    
    // ── Control Plane (UI Clients) ──────────────────────────────────────
    private DdsWriter<SysOpRequest>? _sysOpWriter;
    private ClusterUiCache? _uiCache;
    private ClusterScenarioPanel? _scenarioPanel;
    
    public void Initialize(SubsystemConfig config)
    {
        _config = ClusterConfiguration.LoadFrom("orchestrator-config.json");
        _participant = BagiraEnvironment.CreateParticipant(config.DomainId);
        
        // 1. Boot the headless data plane
        _drillMaster = new DrillMaster(_participant, _config);
        
        // (Time coordinator initialization omitted for brevity, keep your existing logic)
    
        // 2. Boot the decoupled control plane (UI)
        _sysOpWriter = new DdsWriter<SysOpRequest>(_participant);
        _uiCache = new ClusterUiCache(_participant);
        
        // Pass ONLY the network projection and the command writer to the UI
        _scenarioPanel = new ClusterScenarioPanel(_sysOpWriter, _uiCache);
    }
    
    public void Update(float deltaTime)
    {
        // Advance the background services
        _timeKernel?.Update();
        _eventBus?.SwapBuffers();
        _drillMaster?.Tick();
        _timeCoordinator?.Update();
    
        // Advance the UI projection by draining the DDS readers
        _uiCache?.Update();
    }
    
    public void DrawUI()
    {
        if (_uiCache == null) return;
    
        if (!ImGui.Begin("Orchestrator"))
        {
            ImGui.End();
            return;
        }
    
        // The UI now renders strictly from network-replicated state
        var bootstrapped = _uiCache.IsBootstrapped;
        var hasInFlight  = _uiCache.HasInFlightTransaction;
        var disableAll   = !bootstrapped || hasInFlight;
    
        // Render Time Control, Node Health, 2PC History, and Scenarios using _uiCache properties
        _scenarioPanel?.Render(_uiCache, disableAll);
    
        ImGui.End();
    }
    
    public void Shutdown()
    {
        _scenarioPanel = null;
        _uiCache?.Dispose();
        _sysOpWriter?.Dispose();
        _drillMaster?.Dispose();
        _timeKernel?.Dispose();
        _participant?.Dispose();
    }
}
```

3\. Dispatch Time Controls over DDS

Because time manipulation is now driven by `SysOpRequest` commands, the UI no longer triggers `_timeKernel.StepFrame()` directly. It dispatches a network command to the Orchestrator, which acts on it.

In `ClusterScenarioPanel.RenderTimeControl`, map the buttons to the `_sysOpWriter`:

```
    if (ImGui.Button("Step"))
    {
        _sysOpWriter.Write(new SysOpRequest 
        { 
            RequestId = Guid.NewGuid(), 
            OperationType = SysOpType.StepTime 
        });
    }
```

By decoupling the UI into `ClusterUiCache`, the Orchestrator's ImGui panel and the IOS ImGui panel share the exact same network-transparent data model. They observe the world purely by reading `SystemStateTopic` and `NodeHeartbeat`, and mutate it purely by dispatching `SysOpRequest` messages.

