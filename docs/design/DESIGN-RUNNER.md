# Aggregated Mock Runner Application Design

**Version:** 1.0  
**Date:** 2026-02-14  
**Status:** Ready for Implementation

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
│                        Bagira.Runner.exe                             │
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
Bagira.Runner/
├── Program.cs                      # Entry point, CLI parsing
├── RunnerConfiguration.cs          # Parsed config model
├── SubsystemOrchestrator.cs        # Lifecycle manager
├── WaitingRoomCoordinator.cs       # Startup synchronization
└── HeadlessTestExecutor.cs         # Automated test runner

Bagira.SimHost/
├── SimHostSubsystem.cs             # Entry point for embedding
├── SimHostConfiguration.cs         # Config model
├── Systems/                         # All ECS systems
└── ... (existing SimHost design)

Bagira.IG/
├── IgSubsystem.cs                  # Entry point for embedding
├── IgConfiguration.cs              # Config model
├── Systems/                         # All ECS systems
└── ... (existing IG design)

Bagira.IOS/
├── IosSubsystem.cs                 # Entry point for embedding
├── IosConfiguration.cs             # Config model
├── Services/                        # DER, Commands
└── ... (existing IOS design)
```

### 2.3 Subsystem Interface

Each subsystem implements a common lifecycle interface:

```csharp
public interface ISubsystem : IDisposable
{
    string Name { get; }
    SubsystemStatus Status { get; }
    
    // Initialization
    void Initialize(SubsystemConfiguration config);
    void ConnectToDomain(int domainId);
    
    // Lifecycle
    void Start();
    void Stop();
    void Restart();
    
    // Update loop (for non-FDP subsystems)
    void Update(float deltaTime);
    
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
Bagira.Runner.exe --mode all --domain 0
```

**Behavior:**
- Starts all three subsystems in a single process
- Shared Raylib window with dockable ImGui panels
- All communication via DDS loopback
- Single unified log file

**Window Layout:**
```
┌────────────────────────────────────────────────────────────────────┐
│  Bagira Mock Runner - All Subsystems                          [x]  │
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
Bagira.Runner.exe --mode simhost --domain 0 --node-id 1

# Terminal 2
Bagira.Runner.exe --mode ig --domain 0 --node-id 2 --wait-for simhost

# Terminal 3
Bagira.Runner.exe --mode ios --domain 0 --node-id 3 --wait-for simhost,ig
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
Bagira.Runner.exe --mode simhost,ig --domain 0

# IOS + IG only (external SimHost)
Bagira.Runner.exe --mode ios,ig --domain 0 --external simhost

# Just SimHost (headless server)
Bagira.Runner.exe --mode simhost --domain 0 --headless
```

---

### 3.4 Mode D: Headless Auto-Testing

**Use Case**: CI/CD, automated validation, performance testing

**Command:**
```bash
Bagira.Runner.exe --mode all --domain 0 --headless --script tests/latency_test.json
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
Bagira.Runner.exe [OPTIONS]

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
public struct SubsystemStatusAnnounce
{
    [DdsKey]
    public int NodeId;
    
    public string SubsystemName;  // "simhost", "ig", "ios"
    public SubsystemStatus Status;
    public long Timestamp;
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

**Before (Standalone):**
```csharp
// Bagira.SimHost/Program.cs
class Program
{
    static void Main(string[] args)
    {
        var world = new FdpWorld();
        world.AddModule<CarKinemModule>();
        // ... setup ...
        while (!exit)
        {
            world.Update(dt);
        }
    }
}
```

**After (Embeddable):**
```csharp
// Bagira.SimHost/SimHostSubsystem.cs
public class SimHostSubsystem : ISubsystem
{
    private FdpWorld _world;
    private SimHostConfiguration _config;
    private bool _isRunning;
    
    public void Initialize(SimHostConfiguration config)
    {
        _config = config;
        _world = new FdpWorld();
        _world.AddModule<CarKinemModule>();
        _world.AddModule<MissionExecutionModule>();
        // ...
    }
    
    public void ConnectToDomain(int domainId)
    {
        var cyclone = new CycloneNetworkModule(domainId);
        _world.AddModule(cyclone);
    }
    
    public void Start()
    {
        _isRunning = true;
        Task.Run(RunLoop);
    }
    
    private void RunLoop()
    {
        while (_isRunning)
        {
            _world.Update(Time.DeltaTime);
        }
    }
}

// Bagira.SimHost.Standalone/Program.cs (thin shell)
class Program
{
    static void Main(string[] args)
    {
        var config = ParseArgs(args);
        var simHost = new SimHostSubsystem();
        simHost.Initialize(config);
        simHost.ConnectToDomain(config.DomainId);
        simHost.Start();
        
        Console.WriteLine("SimHost running. Press any key to exit.");
        Console.ReadKey();
        simHost.Stop();
    }
}
```

#### 7.2.2 IG Embeddability

**Key Challenge**: IG has Raylib which requires main thread

**Solution**: IG subsystem owns the main loop when embedded

```csharp
// Bagira.IG/IgSubsystem.cs
public class IgSubsystem : ISubsystem
{
    private FdpWorld _world;
    private MapCanvas _canvas;
    private bool _headless;
    
    public void Initialize(IgConfiguration config)
    {
        _headless = config.Headless;
        _world = new FdpWorld();
        
        if (!_headless)
        {
            Raylib.InitWindow(config.WindowWidth, config.WindowHeight, "IG Mock");
            _canvas = new MapCanvas(new RaylibInputProvider());
        }
    }
    
    public void Update(float deltaTime)
    {
        _world.Update(deltaTime);
        
        if (!_headless && !Raylib.WindowShouldClose())
        {
            _canvas.Update(deltaTime);
            
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.DARKGRAY);
            _canvas.Render(new RenderContext());
            Raylib.EndDrawing();
        }
    }
}
```

#### 7.2.3 IOS Embeddability

**Key Challenge**: IOS has ImGui which requires window context

**Solution**: IOS can use standalone ImGui or share IG's context

```csharp
// Bagira.IOS/IosSubsystem.cs
public class IosSubsystem : ISubsystem
{
    private DerRepo _repo;
    private BdcCommandGateway _commands;
    private bool _headless;
    private nint _imGuiContext; // Shared or standalone
    
    public void Initialize(IosConfiguration config)
    {
        _headless = config.Headless;
        _repo = new DerRepo(config.DomainId, config.NodeId);
        _commands = new BdcCommandGateway(_repo);
        
        if (!_headless)
        {
            // Option A: Standalone window
            if (config.StandaloneWindow)
            {
                // Initialize SDL+ImGui
            }
            // Option B: Share context
            else if (config.SharedImGuiContext != nint.Zero)
            {
                _imGuiContext = config.SharedImGuiContext;
                ImGui.SetCurrentContext(_imGuiContext);
            }
        }
    }
    
    public void Update(float deltaTime)
    {
        _repo.Poll();
        
        if (!_headless)
        {
            ImGui.Begin("IOS Control Panel");
            DrawOrbatTree();
            DrawMissionEditor();
            // ...
            ImGui.End();
        }
    }
}
```

---

## 8. Implementation Details

### 8.1 Runner Implementation

```csharp
// Bagira.Runner/Program.cs
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

// Bagira.Runner/SubsystemOrchestrator.cs
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
    
    private void RunWithIgMainLoop()
    {
        var ig = _subsystems.OfType<IgSubsystem>().First();
        
        while (!Raylib.WindowShouldClose())
        {
            float dt = Raylib.GetFrameTime();
            
            foreach (var subsystem in _subsystems)
            {
                subsystem.Update(dt);
            }
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
// Bagira.Runner/RunnerConfiguration.cs
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
    public GeoPosition MapOrigin { get; set; }
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
Bagira.Runner.exe --mode simhost --domain 0

# Terminal 2
Bagira.Runner.exe --mode ig,ios --domain 0 --script latency_test.json
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
Bagira.Runner.exe --mode all --domain 0 --headless --script stress_test.json
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
Bagira.Runner.exe --mode ig --domain 0 --wait-for simhost &
sleep 5
Bagira.Runner.exe --mode simhost --domain 0
```

**Expected**: IG waits 5 seconds, then proceeds when SimHost appears

---

## 10. Implementation Plan

### Phase R1: Runner Core (0/6 tasks)

| ID | Task | Estimated |
|----|------|-----------|
| **R1.1** | Create Bagira.Runner project | 0.25d |
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
- Phase R1 has no dependencies (can start immediately)
- Phase R2 requires Shared components P1-P6 complete
- Phase R3 requires R1, R2 complete
- Phase R4 requires all phases complete

**Critical Path**: R1 → R2 → R3 → R4

---

## Appendix A: Deployment Diagram

```
┌───────────────────────────────────────────────────────────────────┐
│                     Deployment Scenarios                           │
└───────────────────────────────────────────────────────────────────┘

SCENARIO 1: Single Process (Development)
┌─────────────────────────────────────────────┐
│  Bagira.Runner.exe --mode all               │
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
│  Bagira.Runner.exe --mode all --headless    │
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
Bagira.Runner.exe --mode all --domain 0

# Distributed latency test
Bagira.Runner.exe --mode simhost --domain 0 --node-id 1 &
Bagira.Runner.exe --mode ig --domain 0 --node-id 2 --wait-for simhost &
Bagira.Runner.exe --mode ios --domain 0 --node-id 3 --wait-for simhost,ig

# Headless CI/CD
Bagira.Runner.exe --mode all --domain 0 --headless --script tests/smoke_test.json --exit-after 60

# Custom combination: SimHost + IG only (for IG development)
Bagira.Runner.exe --mode simhost,ig --domain 0

# Load from config file
Bagira.Runner.exe --config configs/dev_env.json

# Performance profiling
Bagira.Runner.exe --mode all --domain 0 --headless --performance-log --exit-after 120
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
Bagira.Runner.exe --mode all --wait-for "" --domain 0
# SimHost doesn't wait
# IG automatically waits for "simhost"
# IOS automatically waits for "ig,simhost"
```

### 9.2 Headless Rendering Abstraction

**Issue:** IG's headless mode skips Raylib window creation, but code might call `Raylib.GetMousePosition()` or `Camera.ScreenToWorld()`, causing crashes.

**Solution:**
- Abstract input and camera behind interfaces:
  - `IInputProvider` (mouse, keyboard)
  - `ICameraService` (screen↔world transforms)
- In headless mode, inject mock implementations:
  - `HeadlessInputProvider` (returns zeros)
  - `HeadlessCamera` (mathematical projection without GPU)

**Code Pattern:**
```csharp
// In IgSubsystem.Initialize()
if (_config.Headless)
{
    _inputProvider = new HeadlessInputProvider();
    _cameraService = new HeadlessCamera(1920, 1080);
    _canvas = new MapCanvas(_world, _cameraService);  // Pass camera abstraction
}
else
{
    _inputProvider = new RaylibInputProvider();
    _cameraService = new RaylibCameraService(_canvas.Camera);
}

// Tools use abstraction, not direct Raylib calls
public void Update()
{
    var mousePos = _inputProvider.GetMousePosition();  // Works in both modes
    var worldPos = _cameraService.ScreenToWorld(mousePos);
    // ...
}
```

**HeadlessInputProvider:**
```csharp
public class HeadlessInputProvider : IInputProvider
{
    public Vector2 GetMousePosition() => Vector2.Zero;
    public bool IsMouseButtonPressed(MouseButton button) => false;
    public bool IsKeyPressed(KeyboardKey key) => false;
}
```

**HeadlessCamera:**
```csharp
public class HeadlessCamera : ICameraService
{
    private readonly Rectangle _virtualView;
    
    public HeadlessCamera(float width, float height)
    {
        _virtualView = new Rectangle(0, 0, width, height);
    }
    
    public Vector2 ScreenToWorld(Vector2 screenPos) => screenPos;  // Identity transform
    public Vector2 WorldToScreen(Vector2 worldPos) => worldPos;
    public Rectangle GetViewBounds() => _virtualView;
}
```

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

**DDS Configuration**:
```csharp
[DdsTopic("SubsystemStatusAnnounce")]
[DdsReliability(DdsReliabilityKind.Reliable)]
[DdsDurability(DdsDurabilityKind.TransientLocal)] // Late joiners see last status
public struct SubsystemStatusAnnounce
{
    [DdsKey]
    public int NodeId;
    
    public string SubsystemName;  // "simhost", "ig", "ios"
    public byte Status;           // SubsystemStatus enum
    public long TimestampMs;      // Unix epoch
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
