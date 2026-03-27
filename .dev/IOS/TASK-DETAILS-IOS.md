# IOS Mock - Task Details

**Version:** 1.0  
**Date:** 2026-02-14  
**Project:** BDC-SST Map Mocks  
**Parent:** [DESIGN-IOS.md](./DESIGN-IOS.md)

## Overview

This document breaks down the implementation of the IOS Mock into specific, actionable tasks with time estimates, dependencies, and acceptance criteria.

**Total Estimated Duration:** 8 person-days (reduced from 11 - DER is in SHARED)

---


## Phase P5: Project Setup (0.5 days)

### P5.1: Create Bagira.IOS Project

**Description**: Create IOS Mock console application.

**Steps**:
1. Create project:
   ```bash
   dotnet new console -n Bagira.IOS -f net8.0
   ```
2. Add to solution `IOS-IG-SimHost.sln`.
3. Location: `Bagira.IOS/`

**Dependencies**: None

### P5.2: Add Dependencies

**Description**: Add references to Shared, Map, and FDP core projects.

**Steps**:
1. Add references:
   - `Bagira.DDS.DataModel`
   - `Bagira.Map.Common`
   - `Bagira.Map.Definitions`
   - `FDP.Toolkit.DER`
   - `FDP.Toolkit.Commands`
   - `CycloneDDS.NET`
   - `Raylib-cs`
   - `rlImGui`
   - `Newtonsoft.Json`

**Acceptance Criteria**:
- ✅ Project builds without errors.

---

## Phase P6: IOS Services - 2 Days

**Dependencies**: SHARED P3 (DER Toolkit), SHARED P4 (Commands)

### P6.1: Request Transaction Manager (0.5 days)

**Description**: Track request/response correlation for monitoring

**Files to Create:**
- `Bagira.IOS/Services/IRequestTransactionManager.cs`
- `Bagira.IOS/Services/RequestTransactionManager.cs`

**Implementation:**

```csharp
public interface IRequestTransactionManager
{
    void TrackRequest(Guid requestId, string description);
    void CompleteRequest(Guid requestId, bool success, string message = null);
    IEnumerable<PendingRequest> GetPendingRequests();
    void CheckTimeouts();
}

public class RequestTransactionManager : IRequestTransactionManager
{
    private readonly Dictionary<Guid, PendingRequest> _pending = new();
    private const double TimeoutMs = 5000;
    
    public void TrackRequest(Guid requestId, string description)
    {
        _pending[requestId] = new PendingRequest
        {
            RequestId = requestId,
            Description = description,
            SentTime = DateTime.Now
        };
    }
    
    public void CheckTimeouts()
    {
        var now = DateTime.Now;
        var timedOut = _pending.Values
            .Where(r => (now - r.SentTime).TotalMilliseconds > TimeoutMs)
            .ToList();
        
        foreach (var req in timedOut)
            CompleteRequest(req.RequestId, false, "Timeout");
    }
}
```

**Acceptance Criteria:**
- ✅ Tracks pending requests with timestamps
- ✅ CheckTimeouts marks stale requests as failed
- ✅ GetPendingRequests returns current list
- ✅ Unit tests pass

**Dependencies:** SHARED P3 (DER Toolkit)

---

### P6.2: Mission Editor Service (1 day)

**Description**: Implements optimistic locking for mission editing

**Files to Create:**
- `Bagira.IOS/Services/IMissionEditorService.cs`
- `Bagira.IOS/Services/MissionEditorService.cs`

**Implementation:**

```csharp
public interface IMissionEditorService
{
    (MissionPlan Plan, long Version) GetMissionSnapshot(long entityId);
    Task<MissionCommitResult> CommitMissionAsync(long entityId, MissionPlan newPlan, long baseVersion);
    void SendControlCommand(long entityId, eMissionCommandType type, Guid taskId);
}

public class MissionEditorService : IMissionEditorService
{
    private readonly IDerRepo _repo;
    private readonly DdsWriter<MissionControlRequest> _requestWriter;
    private readonly Dictionary<Guid, TaskCompletionSource<MissionCommitResult>> _pendingCommits = new();
    
    public async Task<MissionCommitResult> CommitMissionAsync(
        long entityId, MissionPlan newPlan, long baseVersion)
    {
        var requestId = Guid.NewGuid();
        var tcs = new TaskCompletionSource<MissionCommitResult>();
        _pendingCommits[requestId] = tcs;
        
        // Send request
        _requestWriter.Write(new MissionControlRequest
        {
            RequestId = requestId,
            TargetEntityId = entityId,
            BaseVersion = baseVersion,
            Payload = new MissionCommandPayload
            {
                Type = eMissionCommandType.CMD_REPLACE_MISSION,
                FullMissionData = newPlan
            }
        });
        
        // Wait with timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            return await tcs.Task.WaitAsync(cts.Token);
        }
        catch (TimeoutException)
        {
            return new MissionCommitResult { Success = false, ErrorMessage = "Timeout" };
        }
    }
    
    public void OnAckReceived(MissionControlAck ack)
    {
        if (_pendingCommits.Remove(ack.RequestId, out var tcs))
        {
            tcs.SetResult(new MissionCommitResult
            {
                Success = ack.ErrorCode == 0,
                ErrorMessage = ack.ErrorMessage,
                NewVersion = ack.NewVersion
            });
        }
    }
}
```

**Acceptance Criteria:**
- ✅ GetMissionSnapshot reads current plan + version
- ✅ CommitMissionAsync sends request with base version
- ✅ OnAckReceived completes pending tasks
- ✅ Timeout handling works correctly
- ✅ Unit tests pass (with mock DDS)

**Dependencies:** P6 (DER), P7.1

---

### P7.3: Context Menu Logic (0.5 days)

**Description**: Strategy-based context menu generation

**Files to Create:**
- `Bagira.IOS/Logic/IContextMenuLogic.cs`
- `Bagira.IOS/Logic/ContextMenuLogic.cs`

**Implementation:**

```csharp
public interface IContextMenuLogic
{
    void OnSelectionChanged(SelectionChangedEvent evt);
    void OnActionInvoked(ContextActionInvoked evt);
    void SetStrategy(MenuStrategy strategy);
}

public enum MenuStrategy
{
    Standard,
    Admin,
    DamageControl,
    Logistics
}

public class ContextMenuLogic : IContextMenuLogic
{
    private readonly IDerRepo _repo;
    private readonly DdsWriter<ContextActionsUpdate> _menuWriter;
    private MenuStrategy _currentStrategy = MenuStrategy.Standard;
    
    public void OnSelectionChanged(SelectionChangedEvent evt)
    {
        var menuItems = BuildMenu(evt.SelectedEntityIds);
        string menuJson = JsonConvert.SerializeObject(new { items = menuItems });
        
        _menuWriter.Write(new ContextActionsUpdate
        {
            MapGroupId = evt.MapGroupId,
            ForSelection = evt.SelectedEntityIds,
            MenuDefinitionJson = menuJson
        });
    }
    
    private List<ContextMenuItem> BuildMenu(List<long> entityIds)
    {
        var items = new List<ContextMenuItem>();
        
        switch (_currentStrategy)
        {
            case MenuStrategy.Standard:
                items.Add(new ContextMenuItem { ActionId = "center", Label = "Center on Entity" });
                items.Add(new ContextMenuItem { ActionId = "properties", Label = "Properties..." });
                break;
            case MenuStrategy.Admin:
                items.Add(new ContextMenuItem { ActionId = "delete", Label = "DELETE", Style = "destructive" });
                items.Add(new ContextMenuItem { ActionId = "teleport", Label = "Teleport..." });
                break;
            // ... other strategies
        }
        
        return items;
    }
}
```

**Acceptance Criteria:**
- ✅ Menu changes based on strategy
- ✅ Menu pushed automatically on selection change
- ✅ OnActionInvoked handles IG responses
- ✅ Unit tests pass

**Dependencies:** P6 (DER)

---

## Phase P8: IOS UI Panels - 4 Days

### P8.1: Configuration Panel (0.5 days)

**Description**: Control IG configuration via JSON patches

**Files to Create:**
- `Bagira.IOS/Panels/ConfigPanel.cs`

**UI Layout:**

```
┌─ MAP CONFIGURATION ──────────────┐
│                                   │
│ Tool: [Navigation        ▼]       │
│ ☑ Satellite Layer                 │
│ ☑ Tactical Graphics                │
│ ☐ Air Units                        │
│ ☐ Grid                             │
│                                    │
│ Icon Scale: [1.0            ]     │
│ Selection Color: [Green     ▼]    │
│                                    │
│ [SEND CONFIG PATCH]               │
└───────────────────────────────────┘
```

**Implementation:**

```csharp
public class ConfigPanel
{
    private string[] _tools = { "Navigation", "Selection", "Placement", "Measure" };
    private int _selectedTool = 0;
    private bool _satelliteLayer = true;
    private bool _tacticalGraphics = true;
    private bool _airUnits = false;
    private bool _grid = false;
    
    public void Draw(IosLogic logic)
    {
        ImGui.Begin("Map Configuration");
        
        ImGui.Combo("Tool", ref _selectedTool, _tools, _tools.Length);
        ImGui.Checkbox("Satellite Layer", ref _satelliteLayer);
        ImGui.Checkbox("Tactical Graphics", ref _tacticalGraphics);
        ImGui.Checkbox("Air Units", ref _airUnits);
        ImGui.Checkbox("Grid", ref _grid);
        
        ImGui.SliderFloat("Icon Scale", ref _iconScale, 0.5f, 2.0f);
        
        if (ImGui.Button("SEND CONFIG PATCH"))
        {
            string patch = BuildPatch();
            logic.SendConfigPatch(patch);
        }
        
        ImGui.End();
    }
    
    private string BuildPatch()
    {
        return JsonConvert.SerializeObject(new
        {
            interaction = new { activeTool = _tools[_selectedTool] },
            view = new
            {
                layers = new
                {
                    satellite = _satelliteLayer,
                    tactical_graphics = _tacticalGraphics,
                    air = _airUnits,
                    grid = _grid
                }
            }
        });
    }
}
```

**Acceptance Criteria:**
- ✅ UI renders correctly
- ✅ Config patch sent when button clicked
- ✅ Patch structure validated

**Dependencies:** P7 (Services)

---

### P8.2: ORBAT Hierarchy Panel (1 day)

**Description**: Tree view of command structure

**Files to Create:**
- `Bagira.IOS/Panels/OrbatPanel.cs`

**UI Layout:**

```
┌─ ORBAT TREE ─────────┐
│ [Filter...      ]    │
│ ▼ TaskForce 1        │
│   ▼ Platoon 1 (HQ)   │
│     • Tank#1         │
│     • Tank#2         │
│     • Tank#3         │
│   ▶ Platoon 2        │
│   • Supply Truck     │
│                      │
│ [New Unit...]        │
└──────────────────────┘
```

**Implementation:**

```csharp
public class OrbatPanel
{
    private HashSet<long> _expandedNodes = new();
    private string _filterText = "";
    
    public void Draw(IosLogic logic)
    {
        ImGui.Begin("ORBAT Tree");
        
        ImGui.InputText("Filter", ref _filterText, 256);
        
        // Build hierarchy
        var roots = FindRootEntities(logic.Repo);
        foreach (var root in roots)
            DrawEntityNode(root, logic);
        
        if (ImGui.Button("New Unit..."))
            logic.OpenSpawner();
        
        ImGui.End();
    }
    
    private void DrawEntityNode(IDerEntity entity, IosLogic logic)
    {
        var info = entity.GetDescriptor<EntityInfo>()?.Data;
        if (info == null) return;
        
        // Check if has children
        var children = FindChildren(entity, logic.Repo);
        bool hasChildren = children.Any();
        
        // Draw tree node
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow;
        if (!hasChildren) flags |= ImGuiTreeNodeFlags.Leaf;
        
        bool isOpen = ImGui.TreeNodeEx($"{info.Name} ({entity.EntityId})", flags);
        
        // Click handling
        if (ImGui.IsItemClicked())
            logic.SelectEntity(entity.EntityId);
        
        // Recurse
        if (isOpen)
        {
            foreach (var child in children)
                DrawEntityNode(child, logic);
            ImGui.TreePop();
        }
    }
    
    private IEnumerable<IDerEntity> FindRootEntities(IDerRepo repo)
    {
        return repo.GetAllEntities().Where(e =>
        {
            var info = e.GetDescriptor<EntityInfo>()?.Data;
            return info != null && info.CommanderId == 0;
        });
    }
    
    private IEnumerable<IDerEntity> FindChildren(IDerEntity parent, IDerRepo repo)
    {
        return repo.GetAllEntities().Where(e =>
        {
            var info = e.GetDescriptor<EntityInfo>()?.Data;
            return info != null && info.CommanderId == parent.EntityId;
        });
    }
}
```

**Acceptance Criteria:**
- ✅ Tree view renders correctly
- ✅ Expand/collapse works
- ✅ Hierarchy based on CommanderId
- ✅ Click selects entity
- ✅ Filter works

**Dependencies:** P6 (DER)

---

### P8.3: Mission Panel (1 day)

**Description**: Display and edit mission plans

**Files to Create:**
- `Bagira.IOS/Panels/MissionPanel.cs`

**UI Layout:**

```
┌─ SELECTION & MISSION ─────────┐
│ Selected: Tank#1               │
│ ID: 5000002                    │
│ Owner: SimHost(1)              │
│                                │
│ Mission:                       │
│ ▶ 1. Move to WP_A              │
│ ⏸ 2. Wait 30s                  │
│ ⏹ 3. Patrol Area               │
│                                │
│ [JUMP][ABORT]                  │
│ [UPLOAD NEW MISSION]           │
└────────────────────────────────┘
```

**Implementation:**

```csharp
public class MissionPanel
{
    private long _selectedEntityId = 0;
    private bool _editMode = false;
    
    public void Draw(IosLogic logic)
    {
        ImGui.Begin("Selection & Mission");
        
        if (_selectedEntityId == 0)
        {
            ImGui.Text("No selection");
            ImGui.End();
            return;
        }
        
        var entity = logic.Repo.GetEntity(_selectedEntityId);
        if (entity == null)
        {
            ImGui.Text("Entity not found");
            ImGui.End();
            return;
        }
        
        var info = entity.GetDescriptor<EntityInfo>()?.Data;
        var mission = entity.GetDescriptor<EntityMission>()?.Data;
        
        ImGui.Text($"Selected: {info?.Name}");
        ImGui.Text($"ID: {_selectedEntityId}");
        
        if (mission != null && mission.Plan != null)
        {
            ImGui.Text("Mission:");
            for (int i = 0; i < mission.Plan.Tasks.Count; i++)
            {
                var task = mission.Plan.Tasks[i];
                string icon = GetTaskIcon(task, mission.CurrentTaskIndex == i);
                ImGui.Text($"{icon} {i + 1}. {task.Type}");
            }
            
            if (ImGui.Button("JUMP"))
                logic.MissionEditorService.SendControlCommand(_selectedEntityId, eMissionCommandType.CMD_JUMP_TO_TASK, Guid.Empty);
            ImGui.SameLine();
            if (ImGui.Button("ABORT"))
                logic.MissionEditorService.SendControlCommand(_selectedEntityId, eMissionCommandType.CMD_ABORT_MISSION, Guid.Empty);
            
            if (ImGui.Button("UPLOAD NEW MISSION"))
                OpenMissionEditor(entity, mission, logic);
        }
        
        ImGui.End();
    }
    
    private string GetTaskIcon(MissionTask task, bool isActive)
    {
        if (isActive) return "▶";
        if (task.Completed) return "✓";
        return "⏹";
    }
}
```

**Acceptance Criteria:**
- ✅ Displays selected entity info
- ✅ Shows mission task list
- ✅ Highlights active task
- ✅ Jump/Abort buttons work
- ✅ Mission editor opens

**Dependencies:** P7.2 (Mission Service)

---

### P8.4: Interaction Panel (Event Log) (0.5 days)

**Description**: Displays network event log for debugging

**Files to Create:**
- `Bagira.IOS/Panels/InteractionPanel.cs`

**UI Layout:**

```
┌─ DATA MONITOR & LOGS ──────────────────────────┐
│ ┌─Time──┬─Topic─────────────┬─Details─────────┐│
│ │ 14:01 │ RX MapClickEvent  │ Pos:45.12,12.33 ││
│ │ 14:01 │ TX CreateEntityReq│ Type:T-72       ││
│ │ 14:02 │ RX CreateEntityAck│ ✓Success (120ms)││
│ │ 14:03 │ RX EntityMaster   │ ID:5000005      ││
│ └───────┴───────────────────┴─────────────────┘│
└────────────────────────────────────────────────┘
```

**Implementation:**

```csharp
public class InteractionPanel
{
    private List<LogEntry> _log = new();
    
    public void AddLog(string direction, string topic, string details)
    {
        _log.Add(new LogEntry
        {
            Time = DateTime.Now,
            Direction = direction,
            Topic = topic,
            Details = details
        });
        
        // Keep last 100 entries
        if (_log.Count > 100)
            _log.RemoveAt(0);
    }
    
    public void Draw(IosLogic logic)
    {
        ImGui.Begin("Data Monitor");
        
        ImGui.BeginTable("log", 3);
        ImGui.TableSetupColumn("Time");
        ImGui.TableSetupColumn("Topic");
        ImGui.TableSetupColumn("Details");
        ImGui.TableHeadersRow();
        
        foreach (var entry in _log)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.Text(entry.Time.ToString("HH:mm:ss"));
            ImGui.TableNextColumn(); ImGui.Text($"{entry.Direction} {entry.Topic}");
            ImGui.TableNextColumn(); ImGui.Text(entry.Details);
        }
        
        ImGui.EndTable();
        ImGui.End();
    }
}
```

**Acceptance Criteria:**
- ✅ Logs all network events
- ✅ Shows RX/TX direction
- ✅ Scrollable list
- ✅ Limited to 100 entries

**Dependencies:** P6 (DER)

---

### P8.5: Spawner Panel (1 day)

**Description**: Entity type browser and placement tool

**Files to Create:**
- `Bagira.IOS/Panels/SpawnerPanel.cs`

**UI Layout:**

```
┌─ ENTITY SPAWNER ──────────────────┐
│ [Units][Graphics][Measures]       │
│ Search: [t-72           ]    (×)  │
│                                   │
│ ┌───────────────────────────────┐ │
│ │ [🚗] T-72B3 Main Battle Tank  │ │
│ │      Type:100 | Russia        │ │
│ └───────────────────────────────┘ │
│ ┌───────────────────────────────┐ │
│ │ [🚗] T-72M1 (Export)          │ │
│ │      Type:105 | Generic       │ │
│ └───────────────────────────────┘ │
│                                   │
│ Affiliation: ⦿Friend ○Hostile     │
│ Mode: [Shared (SimHost)▼]         │
│ [ACTIVATE PLACEMENT TOOL]         │
└───────────────────────────────────┘
```

**Implementation:**

```csharp
public class SpawnerPanel
{
    private TkbService _tkb;
    private string _searchFilter = "";
    private int _selectedType = 0;
    private eAffiliation _affiliation = eAffiliation.FRIEND;
    
    public void Draw(IosLogic logic)
    {
        ImGui.Begin("Entity Spawner");
        
        ImGui.InputText("Search", ref _searchFilter, 256);
        
        // List TKB types
        var types = _tkb.GetAll()
            .Where(t => string.IsNullOrEmpty(_searchFilter) || 
                        t.Name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();
        
        foreach (var type in types)
        {
            if (ImGui.Selectable($"{type.Name} (Type:{type.TkbId})"))
                _selectedType = type.TkbId;
        }
        
        ImGui.Separator();
        ImGui.RadioButton("Friend", ref _affiliation, (int)eAffiliation.FRIEND);
        ImGui.SameLine();
        ImGui.RadioButton("Hostile", ref _affiliation, (int)eAffiliation.HOSTILE);
        
        if (ImGui.Button("ACTIVATE PLACEMENT TOOL"))
        {
            logic.StartPlacementMode(_selectedType, _affiliation);
        }
        
        ImGui.End();
    }
}
```

**Acceptance Criteria:**
- ✅ Lists all TKB types
- ✅ Search filter works
- ✅ Affiliation selector works
- ✅ Placement tool activated

**Dependencies:** P6 (DER), TKB Service

---

## Phase P9: IOS Application Shell - 2 Days

### P9.1: IOS Main Logic (1 day)

**Description**: Core application state and command handlers

**Files to Create:**
- `Bagira.IOS/IosLogic.cs`

**Implementation:**

```csharp
public class IosLogic : IDisposable
{
    public IDerRepo Repo { get; }
    public IMissionEditorService MissionEditorService { get; }
    public IContextMenuLogic ContextMenuLogic { get; }
    public IRequestTransactionManager TransactionManager { get; }
    
    private readonly DdsWriter<MapInteractionConfig> _configWriter;
    private readonly DdsWriter<CreateEntityRequest> _createEntityWriter;
    private readonly DdsWriter<MapCommandRequest> _mapCommandWriter;
    private readonly DdsReader<MapClickEvent> _clickReader;
    private readonly DdsReader<SelectionChangedEvent> _selectionReader;
    
    private Guid _activeContextId;
    private int _placementType;
    
    public IosLogic(int domainId, int nodeId)
    {
        Repo = new DerRepo(domainId, nodeId);
        
        // Register topics
        Repo.RegisterTopic<EntityInfo>();
        Repo.RegisterTopic<EntityMission>();
        Repo.RegisterTopic<GeoSpatial>();
        // ... etc
        
        // Create services
        MissionEditorService = new MissionEditorService(Repo, ...);
        ContextMenuLogic = new ContextMenuLogic(Repo, ...);
        TransactionManager = new RequestTransactionManager();
    }
    
    public void Update()
    {
        // 1. Network ingress
        Repo.Poll();
        
        // 2. Process events
        ProcessClickEvents();
        ProcessSelectionEvents();
        
        // 3. Check timeouts
        TransactionManager.CheckTimeouts();
        
        // 4. Network egress
        Repo.Flush();
    }
    
    public void SendConfigPatch(string jsonPatch)
    {
        var entity = FindConfigEntity();
        var desc = entity.GetOrCreateDescriptor<MapInteractionConfig>();
        desc.ApplyJsonPatch(jsonPatch);
        desc.Write();
    }
    
    public void StartPlacementMode(int tkbType, eAffiliation affiliation)
    {
        _activeContextId = Guid.NewGuid();
        _placementType = tkbType;
        
        SendConfigPatch($@"{{
            ""activeContextId"": ""{_activeContextId}"",
            ""interaction"": {{
                ""activeTool"": ""PLACEMENT"",
                ""toolConfig"": {{ ""entityType"": {tkbType} }}
            }}
        }}");
    }
    
    private void ProcessClickEvents()
    {
        using var samples = _clickReader.Take();
        foreach (var sample in samples)
        {
            if (!sample.IsValid) continue;
            
            // Validate context ID
            if (sample.Data.ContextId != _activeContextId)
                continue; // Stale click
            
            // Send create request
            var requestId = Guid.NewGuid();
            TransactionManager.TrackRequest(requestId, $"Create entity type {_placementType}");
            
            _createEntityWriter.Write(new CreateEntityRequest
            {
                RequestId = requestId,
                TkbType = _placementType,
                Position = sample.Data.Position,
                Owner = 1 // SimHost
            });
        }
    }
}
```

**Acceptance Criteria:**
- ✅ DER initialized correctly
- ✅ All services created
- ✅ Update loop calls Poll/Flush
- ✅ Click events processed
- ✅ Config patches sent

**Dependencies:** P6-P8

---

### P9.2: IOS Program & CLI (1 day)

**Description**: Main entry point, CLI parsing, ImGui setup

**Files to Create:**
- `Bagira.IOS/Program.cs`
- `Bagira.IOS/IosMock.cs`

**Implementation:**

```csharp
// Program.cs
class Program
{
    static void Main(string[] args)
    {
        // Parse args
        int domainId = 0;
        int nodeId = 10;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--domain") domainId = int.Parse(args[i + 1]);
            if (args[i] == "--node") nodeId = int.Parse(args[i + 1]);
        }
        
        // Initialize Raylib (for ImGui context)
        Raylib.InitWindow(1280, 720, "IOS Mock");
        Raylib.SetTargetFPS(60);
        rlImGui.Setup(true);
        
        // Create mock
        var mock = new IosMock(domainId, nodeId);
        
        // Main loop
        while (!Raylib.WindowShouldClose())
        {
            float dt = Raylib.GetFrameTime();
            mock.Update(dt);
            
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.DARKGRAY);
            
            rlImGui.Begin();
            mock.DrawUI();
            rlImGui.End();
            
            Raylib.EndDrawing();
        }
        
        rlImGui.Shutdown();
        Raylib.CloseWindow();
    }
}

// IosMock.cs
public class IosMock : IMockSubsystem
{
    private IosLogic _logic;
    private ConfigPanel _configPanel;
    private OrbatPanel _orbatPanel;
    private MissionPanel _missionPanel;
    private InteractionPanel _interactionPanel;
    private SpawnerPanel _spawnerPanel;
    
    public IosMock(int domainId, int nodeId)
    {
        _logic = new IosLogic(domainId, nodeId);
        _configPanel = new ConfigPanel();
        _orbatPanel = new OrbatPanel();
        _missionPanel = new MissionPanel();
        _interactionPanel = new InteractionPanel();
        _spawnerPanel = new SpawnerPanel();
    }
    
    public void Update(float dt)
    {
        _logic.Update();
    }
    
    public void DrawUI()
    {
        // Menu bar
        if (ImGui.BeginMainMenuBar())
        {
            ImGui.Text($"IOS Mock (Node {_logic.Repo.LocalNodeId})");
            if (ImGui.Button("EXIT")) Environment.Exit(0);
            ImGui.EndMainMenuBar();
        }
        
        // Dockspace
        ImGui.DockSpaceOverViewport(ImGui.GetMainViewport());
        
        // Panels
        _configPanel.Draw(_logic);
        _orbatPanel.Draw(_logic);
        _missionPanel.Draw(_logic);
        _interactionPanel.Draw(_logic);
        _spawnerPanel.Draw(_logic);
    }
}
```

**Acceptance Criteria:**
- ✅ CLI arguments parsed correctly
- ✅ ImGui initialized
- ✅ All panels displayed
- ✅ Update loop runs at 60 FPS
- ✅ Window closes gracefully

**Dependencies:** P9.1

---

## Testing & Integration

### Integration Tests (Parallel with implementation)

**Test Scenarios:**

1. **IOS Standalone**:
   - Launch IOS alone
   - Verify panels render
   - Verify no crashes without network

2. **IOS + IG**:
   - Launch both mocks
   - Send config from IOS
   - Verify IG updates tool
   - Click on IG map
   - Verify IOS receives event

3. **IOS + SimHost**:
   - Launch both mocks
   - Click "Spawn Tank" in IOS
   - Verify SimHost receives request
   - Verify SimHost sends ack
   - Verify entity appears in ORBAT

4. **Full Stack (IOS + IG + SimHost)**:
   - Launch all three mocks
   - Complete workflow:
     1. Spawn platoon from IOS
     2. View on IG map
     3. Select entity on IG
     4. Edit mission in IOS
     5. Monitor execution in all views

5. **Conflict Detection**:
   - Launch 2 IOS instances
   - Both edit same mission
   - First commit succeeds
   - Second commit fails with version conflict

**Test Files:**
- `Bagira.IOS.Tests/IntegrationTests.cs`
- `Bagira.IOS.Tests/WorkflowTests.cs`

---

## Summary Timeline

| Phase | Description | Duration | Dependencies |
|-------|-------------|----------|--------------|
| P6.1 | DER Core | 1 day | P2, P4 |
| P6.2 | DER Descriptors | 1 day | P6.1 |
| P6.3 | JSON Patch | 0.5 days | P6.2 |
| P6.4 | DER Tests | 0.5 days | P6.1-P6.3 |
| **P6 Total** | **DER Toolkit** | **3 days** | |
| P7.1 | Transaction Manager | 0.5 days | P6 |
| P7.2 | Mission Editor | 1 day | P6, P7.1 |
| P7.3 | Context Menu | 0.5 days | P6 |
| **P7 Total** | **IOS Services** | **2 days** | |
| P8.1 | Config Panel | 0.5 days | P7 |
| P8.2 | ORBAT Panel | 1 day | P6 |
| P8.3 | Mission Panel | 1 day | P7.2 |
| P8.4 | Interaction Panel | 0.5 days | P6 |
| P8.5 | Spawner Panel | 1 day | P6, TKB |
| **P8 Total** | **IOS UI Panels** | **4 days** | |
| P9.1 | IOS Logic | 1 day | P6-P8 |
| P9.2 | Program & CLI | 1 day | P9.1 |
| **P9 Total** | **IOS Application** | **2 days** | |
| **TOTAL** | | **11 days** | |

---

**END OF TASK DETAILS**
