# Aggregated Mock Runner Application Design

**Version:** 1.2  
**Date:** 2026-02-14  
**Last Updated:** 2026-03-05  
**Status:** Ready for Implementation — Architect-Reviewed

> **Architecture Review (2026-02-26):** Several sections have been corrected to align with the current Hrot codebase. Key changes: `ISubsystem` now has split `DrawWorld()`/`DrawUI()` phases; `SubsystemOrchestrator` owns the Raylib render loop; obsolete FDP kernel references (`FdpWorld`, `CarKinemModule`) are replaced with `EntityRepository`/`ModuleHostKernel`; `DerRepo` constructor signature corrected; `ICameraService` removed (not needed); DDS QoS attributes corrected to use `[DdsQos(...)]`.

> **Design Talk (2026-03-05):** A new blocking prerequisite was identified. Merging three binaries into one process exposes non-deterministic ECS component ID assignment in `ComponentTypeRegistry`. This breaks the Flight Recorder and causes silent memory corruption in a combined binary. **Phase R0** must be completed before any Runner work begins. See [Section 11](#11-ecs-component-id-safety-phase-r0-pre-requisite) for the full design.

**Parent Document**: [Overall Design](./DESIGN-OVERALL.md)

## Table of Contents

1. [Overview](#1-overview)
2. [Architecture](#2-architecture)
3. [Deployment Modes](#3-deployment-modes)
4. [Command-Line Interface](#4-command-line-interface)
5. [Waiting Room & Synchronization](#5-waiting-room--synchronization)
6. [Headless Auto-Testing](#6-headless-auto-testing)
7. [Component Embeddability](#7-component-embeddability)
8. [Implementation Details](#8-implementation-details)
9. [Testing Scenarios](#9-testing-scenarios)
10. [Implementation Plan](#10-implementation-plan)
11. [ECS Component ID Safety (Phase R0 Pre-Requisite)](#11-ecs-component-id-safety-phase-r0-pre-requisite)

---

## 1. Overview

### 1.1 Purpose

The **Aggregated Mock Runner** is a unified application shell that can instantiate and run:
- **SimHost Mock** (simulation server)
- **IG Mock** (2D map visualization)
- **IOS Mock** (command & control dashboard)

**Critical Requirement**: The same codebase can be deployed in multiple configurations:
1. **Single Aggregated App**: All three subsystems in one process
2. **Separate Applications**: Three independent executables
3. **Headless Mode**: No UI, automated script execution
4. **Custom Combinations**: Any subset (e.g., SimHost+IG only)

### 1.2 Motivation

**Testing Flexibility:**
- **Latency Testing**: Run as separate processes with network loopback to measure real DDS overhead
- **Integration Testing**: Run as single process for deterministic testing
- **Performance Testing**: Headless mode for CI/CD pipelines
- **Development**: Single app for debugging all subsystems together

**Production Readiness:**
- Proves individual subsystems are truly independent (DDS-only coupling)
- Validates that IG/SimHost can be deployed without IOS
- Demonstrates "waiting room" pattern for distributed startup

### 1.3 Design Principles

1. **Subsystem Isolation**: Each subsystem (IOS, IG, SimHost) is a self-contained library
2. **Main as Glue**: `Program.cs` is thin orchestration layer, not logic
3. **Configuration Over Code**: Command-line args drive behavior
4. **Graceful Degradation**: Missing subsystems don't crash others
5. **Observability**: Unified logging and status reporting

---

## 2. Architecture

### 2.1 Component Structure

```
┌─────────────────────────────────────────────────────────────────────┐
│                        Hrot.ClusterRunner.exe                             │
│                                                                       │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │                    Main Orchestrator                         │   │
│  │  - Parse command-line arguments                              │   │
│  │  - Initialize DDS domain                                     │   │
│  │  - Start subsystems based on mode                            │   │
│  │  - Manage lifecycle (start/stop/restart)                     │   │
│  │  - Waiting room coordination                                 │   │
│  └─────────────────────────────────────────────────────────────┘   │
│                                                                       │
│  ┌─────────────┐    ┌──────────────┐    ┌───────────────────┐      │
│  │             │    │              │    │                   │      │
│  │  SimHost    │    │     IG       │    │       IOS         │      │
│  │  Library    │    │   Library    │    │     Library       │      │
│  │             │    │              │    │                   │      │
│  │ • Physics   │    │ • Rendering  │    │ • DER Access      │      │
│  │ • Network   │    │ • Tools      │    │ • Commands        │      │
│  │ • Missions  │    │ • ImGui UI   │    │ • ImGui UI        │      │
│  │ • ImGui UI  │    │ • Network    │    │ • DDS Direct      │      │
│  │             │    │              │    │                   │      │
│  └─────────────┘    └──────────────┘    └───────────────────┘      │
│         △                   △                     △                  │
│         │                   │                     │                  │
│         └─────────────────  DDS Network  ─────────┘                  │
│                                                                       │
└─────────────────────────────────────────────────────────────────────┘
```

### 2.2 Project Structure

```
Hrot.ClusterRunner/
├── Program.cs                      # Entry point, CLI parsing
├── RunnerConfiguration.cs          # Parsed config model
├── SubsystemOrchestrator.cs        # Lifecycle manager
├── WaitingRoomCoordinator.cs       # Startup synchronization
└── HeadlessTestExecutor.cs         # Automated test runner

Hrot.SimHost/
├── SimHostSubsystem.cs             # Entry point for embedding
├── SimHostConfiguration.cs         # Config model
├── Systems/                         # All ECS systems
└── ... (existing SimHost design)

Hrot.IG/
├── IgSubsystem.cs                  # Entry point for embedding
├── IgConfiguration.cs              # Config model
├── Systems/                         # All ECS systems
└── ... (existing IG design)

Hrot.ExCon/
├── IosSubsystem.cs                 # Entry point for embedding
├── IosConfiguration.cs             # Config model
├── Services/                        # DER, Commands
└── ... (existing IOS design)
```

### 2.3 Subsystem Interface

Each subsystem implements a common lifecycle interface.

> **Architect Note (2026-02-26):** The interface now defines three distinct per-frame phases — `Update`, `DrawWorld`, and `DrawUI` — that are driven exclusively by the `SubsystemOrchestrator`. This eliminates Raylib/ImGui context conflicts that would occur if subsystems each owned their own draw calls.

```csharp
public interface ISubsystem : IDisposable
{
    string Name { get; }
    SubsystemStatus Status { get; }
    
    // Initialization
    void Initialize(object config);  // object to allow subsystem-specific config
    void ConnectToDomain(int domainId);
    
    // Lifecycle
    void Start();
    void Stop();
    
    // Per-frame lifecycle phases — all driven by SubsystemOrchestrator
    void Update(float deltaTime);   // Physics / network logic (no rendering)
    void DrawWorld();               // 2D/3D Raylib rendering (NO ImGui calls)
    void DrawUI();                  // ImGui panel rendering only
    
    // Waiting room
    Task WaitForReady();
    void AnnounceReady();
    
    // Headless support
    bool IsHeadless { get; }
    void SetHeadless(bool enabled);
    
    // Events
    event Action<SubsystemStatus> OnStatusChanged;
    event Action<string> OnError;
}

public enum SubsystemStatus
{
    Uninitialized,
    Initializing,
    WaitingForPeers,
    Ready,
    Running,
    Paused,
    Stopped,
    Error
}
```

---

## 3. Deployment Modes

### 3.1 Mode A: Single Aggregated Application

**Use Case**: Development, debugging, demos

**Command:**
```bash
Hrot.ClusterRunner.exe --mode all --domain 0
```

**Behavior:**
- Starts all three subsystems in a single process
- Shared Raylib window with dockable ImGui panels
- All communication via DDS loopback
- Single unified log file

**Window Layout:**
```
┌────────────────────────────────────────────────────────────────────┐
│  Hrot Mock Runner - All Subsystems                          [x]  │
├────────────────────────────────────────────────────────────────────┤
│  ┌──────────────────┐  ┌──────────────────┐  ┌─────────────────┐ │
│  │  SimHost Panel   │  │    IG Map View   │  │   IOS Panel     │ │
│  │  • Time Control  │  │                  │  │  • ORBAT Tree   │ │
│  │  • Spawner       │  │  [Map Canvas]    │  │  • Mission Edit │ │
│  │  • Status        │  │                  │  │  • Config       │ │
│  └──────────────────┘  └──────────────────┘  └─────────────────┘ │
└────────────────────────────────────────────────────────────────────┘
```

**Pros:**
- Easy to debug (single debugger)
- No network configuration needed
- Deterministic execution

**Cons:**
- Can't measure real network latency
- High memory usage

---

### 3.2 Mode B: Separate Applications

**Use Case**: Latency testing, distributed deployment simulation

**Commands:**
```bash
# Terminal 1
Hrot.ClusterRunner.exe --mode simhost --domain 0 --node-id 1

# Terminal 2
Hrot.ClusterRunner.exe --mode ig --domain 0 --node-id 2 --wait-for simhost

# Terminal 3
Hrot.ClusterRunner.exe --mode ios --domain 0 --node-id 3 --wait-for simhost,ig
```

**Behavior:**
- Three independent processes
- Real DDS network stack (UDP multicast)
- Each process has its own window/console
- Waiting room ensures proper startup order

**Network Discovery:**
- Each process announces itself via `SubsystemStatusAnnounce` topic
- Late joiners wait for required peers before starting
- Timeout after 30 seconds with error message

**Pros:**
- Realistic network conditions
- Can run on separate machines
- Measures real DDS overhead
- Process isolation (crash containment)

**Cons:**
- More complex setup
- Requires network configuration
- Harder to debug

---

### 3.3 Mode C: Custom Combination

**Use Case**: Specific testing scenarios

**Examples:**
```bash
# SimHost + IG only (no IOS)
Hrot.ClusterRunner.exe --mode simhost,ig --domain 0

# IOS + IG only (external SimHost)
Hrot.ClusterRunner.exe --mode ios,ig --domain 0 --external simhost

# Just SimHost (headless server)
Hrot.ClusterRunner.exe --mode simhost --domain 0 --headless
```

---

### 3.4 Mode D: Headless Auto-Testing

**Use Case**: CI/CD, automated validation, performance testing

**Command:**
```bash
Hrot.ClusterRunner.exe --mode all --domain 0 --headless --script tests/latency_test.json
```

**Behavior:**
- No UI (ImGui disabled)
- Scripted commands loaded from JSON
- Runs test sequence
- Outputs structured results (JSON/XML)
- Exits with code 0 (pass) or 1 (fail)

**Test Script Format:**
```json
{
  "test_name": "Entity Creation Latency",
  "duration": 60.0,
  "steps": [
    {
      "time": 0.0,
      "action": "simhost.spawn_entity",
      "args": { "type": 100, "position": [50.0, 14.0, 200.0] }
    },
    {
      "time": 0.1,
      "action": "ios.await_entity",
      "args": { "timeout": 5.0 },
      "assert": { "latency_ms": { "max": 100 } }
    },
    {
      "time": 1.0,
      "action": "ig.measure_fps",
      "assert": { "fps": { "min": 30 } }
    }
  ]
}
```

**Output:**
```json
{
  "test_name": "Entity Creation Latency",
  "status": "PASS",
  "duration": 60.123,
  "results": {
    "entity_creation_latency_ms": 45.2,
    "fps_avg": 58.7,
    "network_packets_sent": 524,
    "network_packets_lost": 0
  }
}
```

---

## 4. Command-Line Interface

### 4.1 Core Arguments

```
Hrot.ClusterRunner.exe [OPTIONS]

CORE OPTIONS:
  --mode <mode>               Subsystems to run (all|simhost|ig|ios|<combo>)
  --domain <id>               DDS domain ID (default: 0)
  --node-id <id>              Network node identifier (default: auto)
  --headless                  Run without UI (for testing)
  --config <path>             Load configuration from JSON file

WAITING ROOM:
  --wait-for <subsystems>     Comma-separated list of subsystems to wait for
  --wait-timeout <seconds>    Max wait time for peers (default: 30)
  --no-wait                   Skip waiting room, start immediately

SIMHOST OPTIONS:
  --simhost-time-scale <val>  Initial time scale (default: 1.0)
  --simhost-auto-spawn        Automatically spawn test entities
  --simhost-headless          SimHost runs without UI panel

IG OPTIONS:
  --ig-window-size <w>x<h>    Window dimensions (default: 1920x1080)
  --ig-fullscreen             Start in fullscreen mode
  --ig-vsync <on|off>         Enable VSync (default: on)
  --ig-headless               IG runs without rendering (events only)

IOS OPTIONS:
  --ios-auto-config           Automatically push initial config to IG
  --ios-headless              IOS runs without UI panel

TESTING:
  --script <path>             Run automated test script (requires --headless)
  --log-level <level>         Logging verbosity (debug|info|warn|error)
  --log-file <path>           Log output file (default: stdout)
  --performance-log           Enable performance profiling
  --exit-after <seconds>      Auto-exit after duration (for benchmarks)
```

### 4.2 Configuration File Format

```json
{
  "mode": "all",
  "domain_id": 0,
  "waiting_room": {
    "enabled": true,
    "timeout_seconds": 30,
    "required_peers": ["simhost", "ig"]
  },
  "simhost": {
    "headless": false,
    "time_scale": 1.0,
    "auto_spawn": true,
    "spawn_config": {
      "entity_type": 100,
      "count": 10,
      "formation": "line"
    }
  },
  "ig": {
    "window_width": 1920,
    "window_height": 1080,
    "fullscreen": false,
    "vsync": true,
    "map_origin": { "lat": 50.0755, "lon": 14.4378, "alt": 200.0 }
  },
  "ios": {
    "auto_config": true,
    "initial_config": {
      "active_tool": "selection",
      "layers": {"ground": true, "air": true, "graphics": true}
    }
  },
  "logging": {
    "level": "info",
    "file": "runner.log",
    "console": true
  }
}
```

---

## 5. Waiting Room & Synchronization

### 5.1 Problem Statement

When running as separate processes, subsystems may start in arbitrary order. Issues:
- IG starts before SimHost → No ID allocator available → Entity creation fails
- IOS pushes config before IG ready → Config lost
- SimHost starts spawning entities before IG connected → IG misses initial state

### 5.2 Solution: Waiting Room Pattern

**Concept**: Each subsystem announces its status via DDS. Others wait until required peers are ready.

**DDS Topic:**
```csharp
[DdsTopic("SubsystemStatusAnnounce")]
[DdsQos(
    Reliability = DdsReliability.Reliable,
    Durability = DdsDurability.TransientLocal,
    HistoryKind = DdsHistoryKind.KeepLast,
    HistoryDepth = 1)]
public partial struct SubsystemStatusAnnounce
{
    [DdsKey] public int NodeId;
    
    public string SubsystemName;  // "simhost", "ig", "ios"
    public byte Status;           // SubsystemStatus enum cast to byte
    public long TimestampMs;
    public string Version;
}

public enum SubsystemStatus : byte
{
    Initializing = 0,
    Ready = 1,
    Running = 2,
    Stopped = 3,
    Error = 4
}
```

**Protocol:**

1. **Startup Phase:**
   ```
   SimHost: Initializing → Ready → Running
   IG:      Initializing → WaitingForPeers → Ready → Running
   IOS:     Initializing → WaitingForPeers → Ready → Running
   ```

2. **Dependency Graph:**
   ```
   SimHost (no dependencies)
     ↓
   IG (waits for SimHost.Ready)
     ↓
   IOS (waits for SimHost.Ready + IG.Ready)
   ```

3. **Waiting Logic:**
   ```csharp
   public async Task WaitForPeers(List<string> requiredSubsystems, int timeoutSeconds)
   {
       var reader = new DdsReader<SubsystemStatusAnnounce>(_participant, "SubsystemStatusAnnounce");
       var stopwatch = Stopwatch.StartNew();
       var readyPeers = new HashSet<string>();
       
       while (readyPeers.Count < requiredSubsystems.Count)
       {
           if (stopwatch.Elapsed.TotalSeconds > timeoutSeconds)
               throw new TimeoutException($"Waiting room timeout. Missing: {string.Join(", ", requiredSubsystems.Except(readyPeers))}");
           
           using var samples = reader.Take();
           foreach (var sample in samples)
           {
               if (sample.Data.Status == SubsystemStatus.Ready && requiredSubsystems.Contains(sample.Data.SubsystemName))
                   readyPeers.Add(sample.Data.SubsystemName);
           }
           
           await Task.Delay(100);
       }
   }
   ```

4. **Heartbeat:**
   - Each subsystem republishes status every 1 second (liveliness)
   - If peer disappears (no heartbeat for 5 seconds), others log warning but continue

---

## 6. Headless Auto-Testing

### 6.1 Architecture

**Headless Mode Changes:**
- **IG**: Raylib window not created, `MapCanvas.Render()` skipped, but ECS still running
- **SimHost**: ImGui panels disabled, but ECS and DDS active
- **IOS**: ImGui panels disabled, but DER and DDS active

**Test Script Execution:**
```
┌─────────────────────────────────────────────────────────────────┐
│                   HeadlessTestExecutor                          │
│                                                                 │
│  1. Load test script JSON                                       │
│  2. Initialize subsystems (headless)                            │
│  3. Wait for subsystems ready                                   │
│  4. Execute test steps sequentially                             │
│  5. Collect metrics and assertions                              │
│  6. Generate test report                                        │
│  7. Shutdown subsystems                                         │
│  8. Exit with status code                                       │
└─────────────────────────────────────────────────────────────────┘
```

### 6.2 Test Actions

**SimHost Actions:**
- `simhost.spawn_entity` - Create entity via EntityFactory
- `simhost.set_time_scale` - Adjust simulation speed
- `simhost.pause` / `simhost.resume` - Control time
- `simhost.start_recording` - Enable flight recorder

**IG Actions:**
- `ig.create_local_overlay` - Draw scribble
- `ig.simulate_click` - Inject MapClickEvent
- `ig.simulate_drag` - Inject DragEvent sequence
- `ig.measure_fps` - Capture framerate

**IOS Actions:**
- `ios.send_config` - Push MapInteractionConfig
- `ios.create_entity_request` - Request entity creation
- `ios.await_entity` - Wait for entity to appear in DER
- `ios.measure_latency` - Measure request→ack roundtrip

**Assertions:**
```json
{
  "action": "ios.await_entity",
  "assert": {
    "latency_ms": { "max": 100, "min": 10 },
    "entity_count": { "equals": 1 },
    "entity_type": { "equals": 100 }
  }
}
```

### 6.3 Performance Metrics

**Collected Automatically:**
- Entity creation latency (request sent → entity appears in DER)
- Config update propagation time (IOS → IG receives)
- Frame rate (IG rendering FPS, even in headless calculates "would-be" FPS)
- Network packet count (DDS stats)
- Memory usage (process stats)
- CPU usage (process stats)

**Output Format:**
```json
{
  "test_name": "Stress Test",
  "status": "PASS",
  "duration_seconds": 120.5,
  "metrics": {
    "entity_creation_latency_ms": {"min": 12, "max": 85, "avg": 34.2, "p95": 67},
    "config_propagation_ms": {"min": 5, "max": 45, "avg": 18.7, "p95": 38},
    "ig_fps": {"min": 55, "max": 62, "avg": 59.1},
    "network_packets_sent": 12450,
    "network_packets_lost": 0,
    "memory_mb": {"min": 245, "max": 312, "avg": 278},
    "cpu_percent": {"min": 12, "max": 45, "avg": 28}
  },
  "assertions": {
    "total": 15,
    "passed": 15,
    "failed": 0
  }
}
```

---

## 7. Component Embeddability

### 7.1 Design Goal

Each subsystem (SimHost, IG, IOS) should be usable as:
1. **Standalone Executable** - Own `Program.cs`
2. **Embedded Library** - Called from Runner
3. **Test Fixture** - Instantiated in unit tests

### 7.2 Refactoring Required

**Current State**: Individual designs assume standalone `Program.cs`

**Required Changes**: Extract logic into reusable classes

#### 7.2.1 SimHost Embeddability

> **Architect Note (2026-02-26):** The older snippets referenced `FdpWorld`, `CarKinemModule`, and `MissionExecutionModule` which are all obsolete. The current SimHost uses `EntityRepository`, `EventAccumulator`, `ModuleHostKernel`, and `TkbDatabase`. The embeddable version below reflects the actual `Hrot.SimHost/Program.cs` architecture.

**Embeddable SimHostSubsystem:**
```csharp
// Hrot.SimHost/SimHostSubsystem.cs
public class SimHostSubsystem : SubsystemBase
{
    private EntityRepository? _world;
    private EventAccumulator? _eventAccumulator;
    private ModuleHostKernel? _kernel;
    private SimHostConfiguration _config;
    
    public override void Initialize(object config)
    {
        _config = (SimHostConfiguration)config;
        _world = new EntityRepository();
        _eventAccumulator = new EventAccumulator();
        _kernel = new ModuleHostKernel(_world, _eventAccumulator);
        
        // Register TKB catalog (matches Hrot.SimHost/Program.cs logic)
        var tkbDb = new TkbDatabase();
        BdcTkbCatalog.RegisterAll(tkbDb);
        _world.SetSingletonManaged<ITkbDatabase>(tkbDb);
        
        // Register GeographicModule, ELM, SimulationLogicModule, etc.
        // (same modules as standalone Program.cs)
        
        Status = SubsystemStatus.Ready;
    }
    
    public override void ConnectToDomain(int domainId)
    {
        // Wire DDS readers/writers to kernel translators
    }
    
    public override void Update(float deltaTime)
    {
        _kernel?.Tick(deltaTime);
    }
    
    // SimHost has no world or UI rendering
    public override void DrawWorld() { }
    public override void DrawUI() { }
}

// Hrot.SimHost/Program.cs (standalone thin shell — unchanged)
// SimHost continues to run its own loop when deployed standalone.
```

#### 7.2.2 IG Embeddability

**Key Challenge**: IG has Raylib which requires main thread rendering.

**Solution**: The `SubsystemOrchestrator` owns the Raylib window. IG provides `DrawWorld()` and `DrawUI()` that are called by the orchestrator at the right point in the render loop. IG never calls `Raylib.BeginDrawing()` or `rlImGui.Begin()` itself.

> **Architect Note (2026-02-26):** The old design had IG's `Update()` calling `Raylib.BeginDrawing()` and `rlImGui.Begin()` internally. This crashes when IOS or SimHost panels also need to draw ImGui in the same frame. The orchestrator now drives all phases.

```csharp
// Hrot.IG/IgSubsystem.cs
public class IgSubsystem : SubsystemBase
{
    private IgApplication? _app;
    private MapCanvas? _canvas;
    private bool _headless;
    
    public override void Initialize(object config)
    {
        var igConfig = (IgConfiguration)config;
        _headless = igConfig.Headless;
        
        if (!_headless)
        {
            Raylib.InitWindow(igConfig.WindowWidth, igConfig.WindowHeight, "IG Mock");
            rlImGui.Setup();
            // IG creates its canvas and input provider but does NOT start a draw loop
        }
        else
        {
            // Inject HeadlessInputProvider — no window opened
        }
    }
    
    public override void Update(float deltaTime)
    {
        // ECS tick, input polling, tool logic — no rendering here
        _app?.Update(deltaTime);
    }
    
    public override void DrawWorld()
    {
        // Called by orchestrator between BeginDrawing/EndDrawing, before rlImGui.Begin
        if (!_headless)
            _canvas?.Render();
    }
    
    public override void DrawUI()
    {
        // Called by orchestrator between rlImGui.Begin/End
        if (!_headless)
            _app?.DrawPanels();
    }
}
```

#### 7.2.3 IOS Embeddability

**Key Challenge**: IOS has ImGui panels that must render inside the orchestrator's ImGui frame.

**Solution**: `IosSubsystem` exposes `DrawUI()` so the orchestrator can call it between `rlImGui.Begin()` and `rlImGui.End()`. IOS does not own any window context.

> **Architect Note (2026-02-26):** `DerRepo` takes no network arguments — it is a pure storage class. Network wiring happens in `ConnectToDomain()`. The old snippet with `new DerRepo(config.DomainId, config.NodeId)` was incorrect.

```csharp
// Hrot.ExCon/IosSubsystem.cs
public class IosSubsystem : SubsystemBase
{
    private DerRepo? _repo;
    private IosLogic? _iosLogic;
    private IosConfiguration _config;
    
    // Panel instances (same as standalone IosLogic panels)
    private ConfigPanel? _configPanel;
    private OrbatPanel? _orbatPanel;
    
    public override void Initialize(object config)
    {
        _config = (IosConfiguration)config;
        _repo = new DerRepo();  // No network args — pure storage
        // IosLogic writers/queues are wired in ConnectToDomain
        
        _configPanel = new ConfigPanel();
        _orbatPanel = new OrbatPanel();
        Status = SubsystemStatus.Ready;
    }
    
    public override void ConnectToDomain(int domainId)
    {
        // Wire DDS readers/writers and instantiate IosLogic
        _iosLogic = new IosLogic(_repo, /* DDS writers */ );
    }
    
    public override void Update(float deltaTime)
    {
        // Poll DDS / update DER state — no rendering here
        _iosLogic?.Tick(deltaTime);
    }
    
    // IOS has no 2D/3D world rendering
    public override void DrawWorld() { }
    
    public override void DrawUI()
    {
        if (!_config.Headless && _iosLogic != null)
        {
            _configPanel?.Draw(_iosLogic);
            _orbatPanel?.Draw(_iosLogic);
            // ... other panels
        }
    }
}
```

---

## 8. Implementation Details

### 8.1 Runner Implementation

```csharp
// Hrot.ClusterRunner/Program.cs
class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            var config = RunnerConfiguration.Parse(args);
            var orchestrator = new SubsystemOrchestrator(config);
            
            if (config.Headless && config.TestScript != null)
            {
                var executor = new HeadlessTestExecutor(orchestrator, config.TestScript);
                return await executor.RunAsync();
            }
            else
            {
                await orchestrator.StartAsync();
                await orchestrator.WaitForShutdownAsync();
                return 0;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            return 1;
        }
    }
}

// Hrot.ClusterRunner/SubsystemOrchestrator.cs
public class SubsystemOrchestrator
{
    private readonly RunnerConfiguration _config;
    private readonly List<ISubsystem> _subsystems = new();
    private readonly WaitingRoomCoordinator _waitingRoom;
    
    public async Task StartAsync()
    {
        // 1. Initialize subsystems
        if (_config.EnableSimHost)
        {
            var simHost = new SimHostSubsystem();
            simHost.Initialize(_config.SimHost);
            _subsystems.Add(simHost);
        }
        
        if (_config.EnableIg)
        {
            var ig = new IgSubsystem();
            ig.Initialize(_config.Ig);
            _subsystems.Add(ig);
        }
        
        if (_config.EnableIos)
        {
            var ios = new IosSubsystem();
            ios.Initialize(_config.Ios);
            _subsystems.Add(ios);
        }
        
        // 2. Connect to DDS domain
        foreach (var subsystem in _subsystems)
        {
            subsystem.ConnectToDomain(_config.DomainId);
        }
        
        // 3. Waiting room
        if (_config.WaitingRoom.Enabled)
        {
            await _waitingRoom.WaitForAllAsync(_subsystems, _config.WaitingRoom.Timeout);
        }
        
        // 4. Start subsystems
        foreach (var subsystem in _subsystems)
        {
            subsystem.Start();
        }
        
        // 5. Main loop (if IG is present, it owns the loop)
        if (_subsystems.Any(s => s is IgSubsystem))
        {
            RunWithIgMainLoop();
        }
        else
        {
            RunHeadlessLoop();
        }
    }
    
    // IMPORTANT: The orchestrator owns the Raylib window AND all render phases.
    // Subsystems MUST NOT call BeginDrawing/EndDrawing or rlImGui.Begin/End themselves.
    private void RunWithIgMainLoop()
    {
        while (!Raylib.WindowShouldClose())
        {
            float dt = Raylib.GetFrameTime();
            
            // Phase 1: Logic update (physics, network, ECS ticks)
            foreach (var subsystem in _subsystems)
                subsystem.Update(dt);
            
            // Phase 2: World rendering (Raylib draw calls only, NO ImGui)
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.DarkGray);
            foreach (var subsystem in _subsystems)
                subsystem.DrawWorld();
            
            // Phase 3: UI rendering (ImGui panels only)
            rlImGui.Begin();
            foreach (var subsystem in _subsystems)
                subsystem.DrawUI();
            rlImGui.End();
            
            Raylib.EndDrawing();
        }
    }
    
    private void RunHeadlessLoop()
    {
        var stopwatch = Stopwatch.StartNew();
        var lastTime = 0.0;
        
        while (true)
        {
            var currentTime = stopwatch.Elapsed.TotalSeconds;
            var dt = (float)(currentTime - lastTime);
            lastTime = currentTime;
            
            foreach (var subsystem in _subsystems)
            {
                subsystem.Update(dt);
            }
            
            Thread.Sleep(16); // ~60Hz
        }
    }
}
```

### 8.2 Subsystem Configuration Models

```csharp
// Hrot.ClusterRunner/RunnerConfiguration.cs
public class RunnerConfiguration
{
    public RunMode Mode { get; set; }
    public int DomainId { get; set; } = 0;
    public int NodeId { get; set; } = -1; // Auto-assign
    public bool Headless { get; set; }
    public string? TestScript { get; set; }
    
    public bool EnableSimHost => Mode.HasFlag(RunMode.SimHost);
    public bool EnableIg => Mode.HasFlag(RunMode.IG);
    public bool EnableIos => Mode.HasFlag(RunMode.IOS);
    
    public WaitingRoomConfig WaitingRoom { get; set; } = new();
    public SimHostConfiguration SimHost { get; set; } = new();
    public IgConfiguration Ig { get; set; } = new();
    public IosConfiguration Ios { get; set; } = new();
    public LoggingConfiguration Logging { get; set; } = new();
}

[Flags]
public enum RunMode
{
    None = 0,
    SimHost = 1 << 0,
    IG = 1 << 1,
    IOS = 1 << 2,
    All = SimHost | IG | IOS
}

public class WaitingRoomConfig
{
    public bool Enabled { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 30;
    public List<string> RequiredPeers { get; set; } = new();
}

public class SimHostConfiguration
{
    public bool Headless { get; set; }
    public float TimeScale { get; set; } = 1.0f;
    public bool AutoSpawn { get; set; }
    public int AutoSpawnCount { get; set; } = 10;
}

public class IgConfiguration
{
    public bool Headless { get; set; }
    public int WindowWidth { get; set; } = 1920;
    public int WindowHeight { get; set; } = 1080;
    public bool Fullscreen { get; set; }
    public bool VSync { get; set; } = true;
    public GeoPoint MapOrigin { get; set; }
}

public class IosConfiguration
{
    public bool Headless { get; set; }
    public bool AutoPushConfig { get; set; }
    public bool StandaloneWindow { get; set; } = true;
}
```

---

## 9. Testing Scenarios

### 9.1 Latency Measurement Test

**Objective**: Measure entity creation latency in distributed mode

**Setup**:
```bash
# Terminal 1
Hrot.ClusterRunner.exe --mode simhost --domain 0

# Terminal 2
Hrot.ClusterRunner.exe --mode ig,ios --domain 0 --script latency_test.json
```

**Test Script**:
```json
{
  "test_name": "Entity Creation Latency",
  "duration": 60.0,
  "steps": [
    {"time": 0.0, "action": "ios.create_entity_request", "args": {"type": 100}},
    {"time": 0.0, "action": "ios.start_stopwatch"},
    {"time": 0.0, "action": "ios.await_entity", "assert": {"latency_ms": {"max": 100}}},
    {"time": 1.0, "action": "repeat", "count": 100}
  ]
}
```

### 9.2 Stress Test

**Objective**: Verify system stability under load

**Setup**:
```bash
Hrot.ClusterRunner.exe --mode all --domain 0 --headless --script stress_test.json
```

**Test Script**:
```json
{
  "test_name": "Stress Test",
  "duration": 300.0,
  "steps": [
    {"time": 0.0, "action": "simhost.spawn_entity", "args": {"type": 100}, "repeat": 100},
    {"time": 10.0, "action": "ig.simulate_click", "args": {"x": 500, "y": 500}, "repeat": 1000, "interval": 0.01},
    {"time": 60.0, "action": "ios.send_config", "args": {"active_tool": "selection"}, "repeat": 10, "interval": 1.0},
    {"time": 300.0, "action": "assert_all", "assert": {"ig_fps": {"min": 30}, "memory_mb": {"max": 1000}}}
  ]
}
```

### 9.3 Waiting Room Test

**Objective**: Verify waiting room prevents race conditions

**Setup**:
```bash
# Start in wrong order (IG before SimHost)
Hrot.ClusterRunner.exe --mode ig --domain 0 --wait-for simhost &
sleep 5
Hrot.ClusterRunner.exe --mode simhost --domain 0
```

**Expected**: IG waits 5 seconds, then proceeds when SimHost appears

---

## 10. Implementation Plan

### Phase R0: ECS Component ID Safety (0/2 tasks) ⚠️ MUST COMPLETE FIRST

| ID | Task | Estimated |
|----|------|-----------|
| **R0.1** | Make component IDs deterministic (`[ComponentId]` + `GlobalComponentIds`) | 1.5d |
| **R0.2** | Implement Flight Recorder schema manifest + validator | 1.5d |

**Total**: 3.0 days  
**Note**: This phase operates entirely in `Fdp.Kernel` and associated toolkit libraries. It has no dependency on any other Runner phase, but **all Runner phases depend on it**. Do not merge three binaries into one process until R0 is complete.

---

### Phase R1: Runner Core (0/6 tasks)

| ID | Task | Estimated |
|----|------|-----------|
| **R1.1** | Create Hrot.ClusterRunner project | 0.25d |
| **R1.2** | Implement RunnerConfiguration with CLI parsing | 0.5d |
| **R1.3** | Implement SubsystemOrchestrator | 1.0d |
| **R1.4** | Implement ISubsystem interface | 0.25d |
| **R1.5** | Implement SubsystemStatusAnnounce DDS topic | 0.5d |
| **R1.6** | Implement WaitingRoomCoordinator | 1.0d |

**Total**: 3.5 days

---

### Phase R2: Subsystem Refactoring (0/9 tasks)

| ID | Task | Estimated |
|----|------|-----------|
| **R2.1** | Refactor SimHost to SimHostSubsystem library | 1.0d |
| **R2.2** | Create SimHost standalone Program.cs | 0.25d |
| **R2.3** | Test SimHost embeddability | 0.5d |
| **R2.4** | Refactor IG to IgSubsystem library | 1.5d |
| **R2.5** | Create IG standalone Program.cs | 0.25d |
| **R2.6** | Test IG embeddability | 0.5d |
| **R2.7** | Refactor IOS to IosSubsystem library | 1.0d |
| **R2.8** | Create IOS standalone Program.cs | 0.25d |
| **R2.9** | Test IOS embeddability | 0.5d |

**Total**: 5.75 days

---

### Phase R3: Headless Testing Infrastructure (0/5 tasks)

| ID | Task | Estimated |
|----|------|-----------|
| **R3.1** | Implement HeadlessTestExecutor | 1.5d |
| **R3.2** | Implement test script JSON parser | 0.5d |
| **R3.3** | Implement test action handlers | 2.0d |
| **R3.4** | Implement metrics collection | 1.0d |
| **R3.5** | Implement test report generator | 0.5d |

**Total**: 5.5 days

---

### Phase R4: Integration Testing (0/6 tasks)

| ID | Task | Estimated |
|----|------|-----------|
| **R4.1** | Test single aggregated mode | 0.5d |
| **R4.2** | Test separate applications mode | 1.0d |
| **R4.3** | Test waiting room with various orders | 1.0d |
| **R4.4** | Test headless latency test | 0.5d |
| **R4.5** | Test headless stress test | 0.5d |
| **R4.6** | Document deployment modes | 0.5d |

**Total**: 4.0 days

---

### Total Effort: 18.75 days (approximately 4 weeks)

**Dependencies**:
- **Phase R0** has no dependencies (can start immediately, operates on `Fdp.Kernel`)
- Phase R1 requires R0 complete
- Phase R2 requires Shared components P1-P6 complete
- Phase R3 requires R1, R2 complete
- Phase R4 requires all phases complete

**Critical Path**: R0 → R1 → R2 → R3 → R4

---

## 11. ECS Component ID Safety (Phase R0 Pre-Requisite)

> **Design Talk (2026-03-05):** Identified by architect as a blocking prerequisite before starting the Runner project. Two distinct but related problems must be resolved in `Fdp.Kernel` before three independent binaries can safely share one process.

### 11.1 The Problem: Non-Deterministic Component IDs

`ComponentTypeRegistry` in `Fdp.Kernel` assigns integer IDs to component structs using a static counter (`_nextId++`). The ID assigned to a given struct depends entirely on which static constructor executes first — i.e., the order in which `ComponentType<T>.Id` is first accessed during process startup.

**In a standalone `SimHost.exe`**, `SimTransform` might be assigned ID 0.  
**In an aggregated `Runner.exe`** that also loads IG and IOS assemblies in the same process, `SimTransform` might be assigned ID 27 — because dozens of IG and IOS components whose static constructors were never present in the standalone binary now run first.

**Why this is catastrophic for the Flight Recorder:**  
The Flight Recorder writes raw integer component type IDs directly into record frames. When a recording is played back in a binary where the IDs differ, `PlaybackController` injects component bytes into the wrong memory tables. The result is silent memory corruption, incorrect simulation replay state, or process crashes with no meaningful diagnostic.

**Constraints:**  
- `BitMask256`: Entity component membership is tracked in `EntityHeader.ComponentMask` as a 256-bit SIMD bitmask. IDs must be bytes 0–255. GUIDs are not viable.  
- At most 256 component types can exist across the entire combined binary.

---

### 11.2 Solution: Explicit `[ComponentId]` Attribute

Mirror the existing `[EventId(int)]` pattern (`EventIdAttribute.cs`) to introduce `[ComponentId(byte)]`:

```csharp
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class ComponentIdAttribute : Attribute
{
    public byte Id { get; }
    public ComponentIdAttribute(byte id) => Id = id;
}
```

`ComponentTypeRegistry.GetOrRegisterManaged<T>()` (and its unmanaged counterpart) reads this attribute instead of incrementing `_nextId`. Behaviour:

- **Attribute present:** Use the declared `byte` as the permanent ID.
- **Attribute absent + `FdpConfig.EnforceExplicitComponentIds = true`:** Throw `InvalidOperationException` at first access (fail-fast startup).
- **Attribute absent + enforcement off (default):** Fall back to `_nextId++` (legacy behaviour for tests).
- **ID collision detected:** Always throw, regardless of enforcement flag.

Add `bool EnforceExplicitComponentIds { get; set; }` to `FdpConfig`. Default: `false` during transition; set to `true` in all production entry-points (`Program.cs` of every application).

---

### 11.3 Solution: `GlobalComponentIds` Central Catalog

A single `public static class GlobalComponentIds` in `Fdp.Kernel` owns all ID constants using block allocation. This prevents cross-team collisions and makes the entire ID space auditable in one file.

| Block | Range | Owner |
|-------|-------|-------|
| Fdp.Kernel core | 0–19 | FDP kernel team |
| SimHost simulation | 20–49 | SimHost team |
| FDP.Toolkit.Replication | 50–79 | Networking team |
| FDP.Toolkit.Vis2D | 80–109 | Vis2D team |
| Hrot.IG | 110–139 | IG team |
| Hrot.SimHost app | 140–169 | SimHost app team |
| Hrot.ExCon / shared UI | 170–199 | IOS team |
| Reserved | 200–255 | Future use |

**Initial allocation (all known components at time of writing):**

```csharp
public static class GlobalComponentIds
{
    // Fdp.Kernel (0–19)
    public const byte SimTransform        = 0;
    public const byte SimVelocity         = 1;
    public const byte HealthData          = 2;
    public const byte GlobalTime          = 3;
    public const byte IsActiveTag         = 4;
    public const byte LifecycleDescriptor = 5;
    public const byte HierarchyNode       = 6;
    public const byte PartDescriptor      = 7;

    // FDP.Toolkit.Replication (50–79)
    public const byte NetworkIdentity     = 50;
    public const byte NetworkAuthority    = 51;
    public const byte NetworkPosition     = 52;
    public const byte NetworkVelocity     = 53;
    public const byte NetworkSpawnRequest = 54;
    public const byte PartMetadata        = 55;

    // FDP.Toolkit.Vis2D (80–109)
    public const byte MapDisplayComponent = 80;
    public const byte VisHierarchyNode    = 81;
    public const byte AggregateState      = 82;
    public const byte AggregateRoot       = 83;

    // Hrot.IG (110–139)
    public const byte ResolvedStyle       = 110;
    public const byte CullingState        = 111;
    public const byte SelectionState      = 112;
    public const byte VisualEffectState   = 113;
    public const byte TracerTarget        = 114;
}
```

Each component struct then carries the `[ComponentId]` attribute referencing these constants:

```csharp
[ComponentId(GlobalComponentIds.SimTransform)]
public struct SimTransform { ... }
```

---

### 11.4 The Second Problem: Silent Flight Recorder Schema Drift

Even after IDs are stabilised, a component struct's memory layout can silently diverge across versions: a field is added, a field is reordered, an alignment attribute changes. When `PlaybackController` reads a recording, it writes raw bytes at field offsets directly into component tables. A changed layout causes wrong values to appear in the wrong fields — typically presenting as bizarre entity behaviour rather than a crash.

**Solution:** Save a `SchemaManifest` inside every `.meta.json` sidecar at record time and validate it at playback time before any memory is touched.

**`ComponentSchemaInfo`** (serialised in `.meta.json`):

| Field | Type | Description |
|-------|------|-------------|
| `Name` | `string` | Full CLR type name, e.g. `Fdp.Kernel.SimTransform` |
| `Size` | `int` | `Marshal.SizeOf<T>()` at record time |
| `LayoutHash` | `ulong` | FNV-1a 64-bit hash of field names, type names, and `Marshal.OffsetOf` per field |
| `IsManaged` | `bool` | `true` for managed component types |

**`ComponentLayoutHasher.ComputeHash(Type)`** — deterministic, never uses `GetHashCode()`. Iterates all public + private instance fields in declaration order. For each field: hashes `{fieldName}|{fieldTypeName}|{offsetInBytes}`. Catches field reorders even when `sizeof` is unchanged.

**`SchemaValidator.Validate(RecordingMetadata meta, ComponentTypeRegistry registry)`** — called inside `PlaybackController` constructor, after deserialising `.meta.json` and before opening the binary frame stream. Failure produces a detailed `InvalidOperationException`:

> _"Component SimTransform layout has changed: recorded hash 0xABCD1234 vs current 0xEFAB5678 (recorded size=12, current size=16)"_

Old recordings without a `SchemaManifest` bypass validation (warning logged, playback allowed). This preserves compatibility with recordings made before this feature.

**`RecordingMetadata` extension:**

```csharp
public Dictionary<int, ComponentSchemaInfo>? SchemaManifest { get; set; }
```

**`AsyncRecorder.Dispose()` extension:** Immediately before calling `MetadataSerializer.Serialize(...)`, iterate `ComponentTypeRegistry.GetRecordableTypeIds()`, compute `ComponentSchemaInfo` for each, and populate `RecordingMetadata.SchemaManifest`.

---

### 11.5 Total Effort Revision

| Original Total | R0 Addition | Revised Total |
|----------------|-------------|---------------|
| 18.75 days | +3.0 days | **21.75 days** |

---

## Appendix A: Deployment Diagram

```
┌───────────────────────────────────────────────────────────────────┐
│                     Deployment Scenarios                           │
└───────────────────────────────────────────────────────────────────┘

SCENARIO 1: Single Process (Development)
┌─────────────────────────────────────────────┐
│  Hrot.ClusterRunner.exe --mode all               │
│  ┌───────────┬───────────┬──────────────┐  │
│  │ SimHost   │    IG     │     IOS      │  │
│  │ Thread    │  (Main)   │   Thread     │  │
│  └─────┬─────┴─────┬─────┴──────┬───────┘  │
│        │           │            │           │
│        └───────────┴────────────┘           │
│              DDS Loopback                   │
└─────────────────────────────────────────────┘

SCENARIO 2: Separate Processes (Testing)
┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐
│  Process 1       │  │  Process 2       │  │  Process 3       │
│  Runner.exe      │  │  Runner.exe      │  │  Runner.exe      │
│  --mode simhost  │  │  --mode ig       │  │  --mode ios      │
│  ┌────────────┐  │  │  ┌────────────┐  │  │  ┌────────────┐  │
│  │  SimHost   │  │  │  │     IG     │  │  │  │    IOS     │  │
│  └─────┬──────┘  │  │  └─────┬──────┘  │  │  └─────┬──────┘  │
└────────┼─────────┘  └────────┼─────────┘  └────────┼─────────┘
         │                     │                      │
         └─────────────────────┴──────────────────────┘
                      DDS UDP Multicast

SCENARIO 3: Headless CI/CD
┌─────────────────────────────────────────────┐
│  Hrot.ClusterRunner.exe --mode all --headless    │
│  --script latency_test.json                 │
│  ┌───────────┬───────────┬──────────────┐  │
│  │ SimHost   │    IG     │     IOS      │  │
│  │ (No UI)   │  (No Gfx) │   (No UI)    │  │
│  └─────┬─────┴─────┬─────┴──────┬───────┘  │
│        │           │            │           │
│        └───────────┴────────────┘           │
│              DDS Loopback                   │
│                                              │
│  Output: test_results.json                  │
└─────────────────────────────────────────────┘
```

---

## Appendix B: Example CLI Usage

```bash
# Development: All in one, with UI
Hrot.ClusterRunner.exe --mode all --domain 0

# Distributed latency test
Hrot.ClusterRunner.exe --mode simhost --domain 0 --node-id 1 &
Hrot.ClusterRunner.exe --mode ig --domain 0 --node-id 2 --wait-for simhost &
Hrot.ClusterRunner.exe --mode ios --domain 0 --node-id 3 --wait-for simhost,ig

# Headless CI/CD
Hrot.ClusterRunner.exe --mode all --domain 0 --headless --script tests/smoke_test.json --exit-after 60

# Custom combination: SimHost + IG only (for IG development)
Hrot.ClusterRunner.exe --mode simhost,ig --domain 0

# Load from config file
Hrot.ClusterRunner.exe --config configs/dev_env.json

# Performance profiling
Hrot.ClusterRunner.exe --mode all --domain 0 --headless --performance-log --exit-after 120
```

---

## 9. Critical Edge Cases & Mitigations

### 9.1 Waiting Room Deadlock Prevention

**Issue:** If misconfigured, subsystems might wait circularly:
- SimHost waits for IG
- IG waits for SimHost
- Result: Deadlock, no subsystem starts

**Solution:**
- Orchestrator validates dependency graph before starting
- Enforce strict hierarchy:
  - **SimHost**: Waits for NO ONE (server, starts first)
  - **IG**: Waits for SimHost
  - **IOS**: Waits for IG (or both SimHost + IG)
- Reject configurations with circular dependencies

**Code Pattern:**
```csharp
public void ValidateWaitingRoomConfig()
{
    // Build dependency graph
    var deps = new Dictionary<string, List<string>>();
    foreach (var subsystem in _subsystems)
    {
        deps[subsystem.Name] = subsystem.GetWaitForList();
    }
    
    // Check for cycles using DFS
    if (HasCycle(deps))
    {
        throw new InvalidOperationException(
            "Circular waiting room dependency detected. " +
            "SimHost should not wait for anyone. " +
            "IG waits for SimHost. IOS waits for IG."
        );
    }
}

private bool HasCycle(Dictionary<string, List<string>> graph)
{
    var visited = new HashSet<string>();
    var stack = new HashSet<string>();
    
    foreach (var node in graph.Keys)
    {
        if (HasCycleDFS(node, graph, visited, stack))
            return true;
    }
    return false;
}
```

**Recommended Default Configuration:**
```bash
# Correct: No cycles
Hrot.ClusterRunner.exe --mode all --wait-for "" --domain 0
# SimHost doesn't wait
# IG automatically waits for "simhost"
# IOS automatically waits for "ig,simhost"
```

### 9.2 Headless Rendering Abstraction

> **Architect Note (2026-02-26):** The previous design proposed an `ICameraService` / `HeadlessCamera` abstraction. This is over-engineered. `MapCamera` only manipulates the `Camera2D` struct (pure math), which is perfectly safe in headless mode — it never calls any GPU function. An `ICameraService` is therefore not needed.

**Actual Headless Solution (simplified):**
- Inject a `HeadlessInputProvider` (implements `IInputProvider`, returns zeros) so tools never call `Raylib.GetMousePosition()` directly.
- In headless mode, the orchestrator simply **skips** the `DrawWorld()` and `DrawUI()` calls — no Raylib window is ever opened.
- `MapCamera` math can still run safely during `Update()` even without a window.

**HeadlessInputProvider (the only addition needed):**
```csharp
public class HeadlessInputProvider : IInputProvider
{
    public Vector2 GetMousePosition() => Vector2.Zero;
    public bool IsMouseButtonPressed(MouseButton button) => false;
    public bool IsKeyPressed(KeyboardKey key) => false;
    public bool IsKeyDown(KeyboardKey key) => false;
}
```

**Orchestrator headless loop (no DrawWorld/DrawUI calls):**
```csharp
private void RunHeadlessLoop()
{
    var stopwatch = Stopwatch.StartNew();
    var lastTime = 0.0;
    const float targetDt = 1.0f / 60.0f;
    
    while (!_shutdownToken.IsCancellationRequested)
    {
        var currentTime = stopwatch.Elapsed.TotalSeconds;
        var dt = (float)(currentTime - lastTime);
        lastTime = currentTime;
        
        // Logic only — no rendering in headless
        foreach (var subsystem in _subsystems)
            subsystem.Update(dt);
        
        var sleepTime = (int)((targetDt - dt) * 1000);
        if (sleepTime > 0)
            Thread.Sleep(sleepTime);
    }
}
```

~~`ICameraService` and `HeadlessCamera` are removed from the design.~~

### 9.3 Test Script Timing Precision

**Issue:** Test scripts specify actions at specific times (e.g., `"time": 5.0`). If the headless loop lags, actions might miss their window.

**Solution:**
- Use priority queue for pending actions sorted by time
- Each frame, dequeue and execute ALL actions where `action.time <= currentTime`
- Don't rely on exact frame timing, use cumulative time

**Code Pattern:**
```csharp
private async Task ExecuteTestScript()
{
    var actionQueue = new PriorityQueue<TestStep, double>();
    foreach (var step in _script.Steps)
    {
        actionQueue.Enqueue(step, step.Time);
    }
    
    var stopwatch = Stopwatch.StartNew();
    
    while (actionQueue.Count > 0 || stopwatch.Elapsed.TotalSeconds < _script.Duration)
    {
        var currentTime = stopwatch.Elapsed.TotalSeconds;
        
        // Execute all actions due at or before current time
        while (actionQueue.Count > 0 && actionQueue.Peek().Time <= currentTime)
        {
            var step = actionQueue.Dequeue();
            await ExecuteStep(step);
        }
        
        await Task.Delay(10);  // 100Hz check rate
    }
}
```

### 9.4 Subsystem Crash Isolation

**Issue:** If SimHost crashes (e.g., null reference), should the entire runner process crash, or should IG/IOS continue?

**Solution for Mock:** Let it crash (fail-fast)
**Solution for Production:** Wrap subsystem update in try-catch, mark subsystem as Error state, continue others

**Code Pattern (Production-Ready):**
```csharp
foreach (var subsystem in _subsystems)
{
    try
    {
        subsystem.Update(dt);
    }
    catch (Exception ex)
    {
        _logger.LogError($"Subsystem {subsystem.Name} crashed: {ex.Message}");
        subsystem.Status = SubsystemStatus.Error;
        
        if (_config.StopOnSubsystemError)
        {
            throw;  // Fail-fast mode
        }
        // Otherwise continue updating healthy subsystems
    }
}
```

### 9.5 Metrics Collection Overhead

**Issue:** Collecting metrics every frame (FPS, CPU, memory) might slow down the test itself.

**Solution:**
- Collect system metrics at lower rate (10Hz) in background thread
- Only collect per-action metrics synchronously
- Use lock-free data structures (`ConcurrentQueue`) for metric recording

**Code Pattern:**
```csharp
public class TestMetricsCollector
{
    private readonly ConcurrentQueue<(string metric, double value, DateTime time)> _samples = new();
    private readonly Timer _systemMetricsTimer;
    
    public TestMetricsCollector()
    {
        // Collect system metrics every 100ms in background
        _systemMetricsTimer = new Timer(_ => CollectSystemMetrics(), null, 0, 100);
    }
    
    public void RecordMetric(string name, double value)
    {
        // Lock-free, safe from any thread
        _samples.Enqueue((name, value, DateTime.UtcNow));
    }
    
    private void CollectSystemMetrics()
    {
        RecordMetric("cpu_percent", GetCpuUsage());
        RecordMetric("memory_mb", GetMemoryUsageMB());
    }
}
```

---

## Appendix A: Configuration File Schema

**Purpose**: Enable waiting room and distributed health monitoring

> **Architect Note (2026-02-26):** FDP uses a **single `[DdsQos]` attribute** to specify all QoS policies. The older pseudo-attributes `[DdsReliability(...)]` and `[DdsDurability(...)]` do not exist in FDP and will not compile. Use the pattern below.

**DDS Configuration**:
```csharp
[DdsTopic("SubsystemStatusAnnounce")]
[DdsQos(
    Reliability = DdsReliability.Reliable,
    Durability = DdsDurability.TransientLocal,    // Late joiners see last status
    HistoryKind = DdsHistoryKind.KeepLast,
    HistoryDepth = 1)]
public partial struct SubsystemStatusAnnounce
{
    [DdsKey] public int NodeId;
    
    public string SubsystemName;  // "simhost", "ig", "ios"
    public byte Status;           // SubsystemStatus enum cast to byte
    public long TimestampMs;      // Unix epoch milliseconds
    public string Version;        // "1.0.0"
    public string HostName;       // "DEV-MACHINE-01"
    public uint ProcessId;        // OS process ID
}
```

**Publishing Pattern**:
```csharp
// Announce on startup
writer.Write(new SubsystemStatusAnnounce {
    NodeId = _nodeId,
    SubsystemName = "simhost",
    Status = (byte)SubsystemStatus.Ready,
    TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
    Version = "1.0.0",
    HostName = Environment.MachineName,
    ProcessId = (uint)Environment.ProcessId
});

// Heartbeat every 1 second
_heartbeatTimer = new Timer(_ => PublishStatus(), null, 0, 1000);
```
