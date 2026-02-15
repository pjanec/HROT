# Task Details: Aggregated Runner Application

**Version:** 1.0  
**Date:** 2026-02-14  
**Parent Document**: [DESIGN-RUNNER.md](./DESIGN-RUNNER.md)

## Table of Contents

1. [Phase R1: Runner Core](#phase-r1-runner-core)
2. [Phase R2: Subsystem Refactoring](#phase-r2-subsystem-refactoring)
3. [Phase R3: Headless Testing Infrastructure](#phase-r3-headless-testing-infrastructure)
4. [Phase R4: Integration Testing](#phase-r4-integration-testing)

---

# Phase R1: Runner Core

## R1.1: Create Bagira.Runner Project

**Estimated**: 0.25 days  
**Dependencies**: None

### Description
Create the main runner application project that will orchestrate all subsystems.

### Success Criteria

**SC-1**: Project Structure Created
- Create `Bagira.Runner` console application project (.NET 8)
- Add to solution file
- Project compiles successfully

**SC-2**: Dependencies Added
- Reference `Bagira.DDS.DataModel` project
- Add `CommandLineParser` NuGet package (for CLI argument parsing)
- Add `Microsoft.Extensions.Logging` NuGet package
- Add `Newtonsoft.Json` NuGet package

**SC-3**: Folder Structure Created
```
Bagira.Runner/
├── Program.cs
├── Models/
│   └── (empty, for next task)
├── Services/
│   └── (empty, for next task)
└── Configuration/
    └── (empty, for next task)
```

### Testing

Build the empty project:
```bash
cd Bagira.Runner
dotnet build
```

Expected: Successful build with exit code 0

---

## R1.2: Implement RunnerConfiguration with CLI Parsing

**Estimated**: 0.5 days  
**Dependencies**: R1.1

### Description
Implement configuration model and command-line argument parsing using CommandLineParser library.

### Success Criteria

**SC-1**: Configuration Model Class Created

Create `Configuration/RunnerConfiguration.cs` with properties:
```csharp
public class RunnerConfiguration
{
    [Option('m', "mode", Required = true, HelpText = "Subsystems to run (all|simhost|ig|ios|<combo>)")]
    public string ModeString { get; set; }
    
    [Option('d', "domain", Default = 0, HelpText = "DDS domain ID")]
    public int DomainId { get; set; }
    
    [Option('n', "node-id", Default = -1, HelpText = "Network node identifier (auto-assign if -1)")]
    public int NodeId { get; set; }
    
    [Option("headless", Default = false, HelpText = "Run without UI")]
    public bool Headless { get; set; }
    
    [Option('c', "config", HelpText = "Load configuration from JSON file")]
    public string? ConfigFile { get; set; }
    
    [Option("wait-for", Separator = ',', HelpText = "Comma-separated list of subsystems to wait for")]
    public IEnumerable<string>? WaitFor { get; set; }
    
    [Option("wait-timeout", Default = 30, HelpText = "Max wait time for peers (seconds)")]
    public int WaitTimeout { get; set; }
    
    [Option("no-wait", Default = false, HelpText = "Skip waiting room")]
    public bool NoWait { get; set; }
    
    [Option("script", HelpText = "Run automated test script (requires --headless)")]
    public string? TestScript { get; set; }
    
    [Option("log-level", Default = "info", HelpText = "Logging verbosity")]
    public string LogLevel { get; set; }
    
    [Option("log-file", HelpText = "Log output file")]
    public string? LogFile { get; set; }
    
    // Computed properties
    public RunMode Mode { get; private set; }
    public bool EnableSimHost => Mode.HasFlag(RunMode.SimHost);
    public bool EnableIg => Mode.HasFlag(RunMode.IG);
    public bool EnableIos => Mode.HasFlag(RunMode.IOS);
    
    // Validation and parsing
    public bool Validate(out List<string> errors);
    public void ParseModeString();
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
```

**SC-2**: Configuration Parsing Implemented

Implement parsing logic:
- Parse `ModeString` into `RunMode` enum (support "all", "simhost", "ig", "ios", "simhost,ig", etc.)
- Validate that mode string is valid
- Auto-assign `NodeId` if -1 (use random or timestamp-based)
- Validate `TestScript` requires `Headless` mode
- Validate `WaitFor` subsystem names

**SC-3**: JSON Configuration Loading

Implement `LoadFromJson(string path)` method:
- Read JSON file
- Deserialize to configuration
- Merge with command-line arguments (CLI overrides JSON)
- Validate merged configuration

**SC-4**: Unit Tests

Create `Bagira.Runner.Tests` project and implement tests:
- `Test_ParseMode_All`: Verify "all" → `RunMode.All`
- `Test_ParseMode_Combo`: Verify "simhost,ig" → `RunMode.SimHost | RunMode.IG`
- `Test_ParseMode_Invalid`: Verify invalid mode string returns error
- `Test_Validation_HeadlessScript`: Verify script requires headless
- `Test_LoadFromJson`: Verify JSON loading and merging

### Testing

Run unit tests:
```bash
cd Bagira.Runner.Tests
dotnet test
```

Expected: All 5+ tests pass

---

## R1.3: Implement SubsystemOrchestrator

**Estimated**: 1.0 days  
**Dependencies**: R1.2, R1.4

### Description
Implement the orchestrator that manages subsystem lifecycle and main loop.

### Success Criteria

**SC-1**: Orchestrator Class Created

Create `Services/SubsystemOrchestrator.cs` with methods:
```csharp
public class SubsystemOrchestrator : IDisposable
{
    private readonly RunnerConfiguration _config;
    private readonly List<ISubsystem> _subsystems;
    private readonly ILogger _logger;
    private CancellationTokenSource _shutdownToken;
    
    public SubsystemOrchestrator(RunnerConfiguration config, ILogger logger);
    
    public async Task InitializeAsync();
    public async Task StartAsync();
    public Task WaitForShutdownAsync();
    public void RequestShutdown();
    public void Dispose();
    
    private void InstantiateSubsystems();
    private void ConnectToDdsDomain();
    private void RunMainLoop();
    private void RunHeadlessLoop();
}
```

**SC-2**: Subsystem Instantiation Logic

Implement `InstantiateSubsystems()`:
- Based on `config.Mode`, create instances of:
  - `SimHostSubsystem` if `EnableSimHost`
  - `IgSubsystem` if `EnableIg`
  - `IosSubsystem` if `EnableIos`
- Call `Initialize()` on each
- Store in `_subsystems` list
- Handle missing dependencies gracefully (log warning if subsystem can't load)

**SC-3**: DDS Domain Connection

Implement `ConnectToDdsDomain()`:
- For each subsystem, call `ConnectToDomain(config.DomainId)`
- Log connection attempts
- Catch and log errors but continue (some subsystems may not need DDS immediately)

**SC-4**: Main Loop Logic

Implement dual-mode main loop:

**Option A: IG Present (IG owns main thread)**
```csharp
private void RunMainLoop()
{
    var ig = _subsystems.OfType<IgSubsystem>().FirstOrDefault();
    if (ig != null)
    {
        while (!Raylib.WindowShouldClose() && !_shutdownToken.IsCancellationRequested)
        {
            float dt = Raylib.GetFrameTime();
            foreach (var subsystem in _subsystems)
                subsystem.Update(dt);
        }
    }
    else
    {
        RunHeadlessLoop();
    }
}
```

**Option B: Headless (Fixed timestep loop)**
```csharp
private void RunHeadlessLoop()
{
    var stopwatch = Stopwatch.StartNew();
    var lastTime = 0.0;
    const float targetDt = 1.0f / 60.0f; // 60Hz
    
    while (!_shutdownToken.IsCancellationRequested)
    {
        var currentTime = stopwatch.Elapsed.TotalSeconds;
        var dt = (float)(currentTime - lastTime);
        lastTime = currentTime;
        
        foreach (var subsystem in _subsystems)
            subsystem.Update(dt);
        
        // Sleep to maintain ~60Hz
        var sleepTime = (int)((targetDt - dt) * 1000);
        if (sleepTime > 0)
            Thread.Sleep(sleepTime);
    }
}
```

**SC-5**: Graceful Shutdown

Implement shutdown logic:
- Stop all subsystems (call `Stop()`)
- Dispose all subsystems (call `Dispose()`)
- Log shutdown messages
- Handle exceptions during shutdown (log but continue)

**SC-6**: Unit Tests

Create integration test:
- `Test_Orchestrator_Lifecycle`: Verify init → start → stop sequence
- `Test_Orchestrator_MissingSubsystem`: Verify graceful handling of missing subsystem
- `Test_Orchestrator_Shutdown`: Verify clean shutdown

### Testing

Create mock subsystem and run lifecycle test:
```csharp
var mockSubsystem = new MockSubsystem();
var config = new RunnerConfiguration { Mode = RunMode.SimHost };
var orchestrator = new SubsystemOrchestrator(config, logger);
await orchestrator.InitializeAsync();
await orchestrator.StartAsync();
// ... verify subsystem started
orchestrator.RequestShutdown();
await orchestrator.WaitForShutdownAsync();
// ... verify subsystem stopped
```

---

## R1.4: Implement ISubsystem Interface

**Estimated**: 0.25 days  
**Dependencies**: R1.1

### Description
Define the common interface that all subsystems must implement for embedding.

### Success Criteria

**SC-1**: Interface Defined

Create `Models/ISubsystem.cs`:
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
    
    // Update loop
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
    Uninitialized = 0,
    Initializing = 1,
    WaitingForPeers = 2,
    Ready = 3,
    Running = 4,
    Paused = 5,
    Stopped = 6,
    Error = 7
}
```

**SC-2**: Base Implementation Helper

Create `Models/SubsystemBase.cs` with common functionality:
```csharp
public abstract class SubsystemBase : ISubsystem
{
    protected ILogger Logger { get; }
    protected SubsystemStatus _status;
    
    public string Name { get; protected set; }
    public SubsystemStatus Status 
    { 
        get => _status;
        protected set
        {
            if (_status != value)
            {
                _status = value;
                OnStatusChanged?.Invoke(value);
            }
        }
    }
    
    public event Action<SubsystemStatus>? OnStatusChanged;
    public event Action<string>? OnError;
    
    protected SubsystemBase(string name, ILogger logger)
    {
        Name = name;
        Logger = logger;
        _status = SubsystemStatus.Uninitialized;
    }
    
    protected void RaiseError(string message)
    {
        Logger.LogError(message);
        Status = SubsystemStatus.Error;
        OnError?.Invoke(message);
    }
    
    // Abstract methods to be implemented by derived classes
    public abstract void Initialize(object config);
    public abstract void ConnectToDomain(int domainId);
    public abstract void Start();
    public abstract void Stop();
    public abstract void Update(float deltaTime);
    public abstract Task WaitForReady();
    public abstract void AnnounceReady();
    public abstract bool IsHeadless { get; }
    public abstract void SetHeadless(bool enabled);
    public abstract void Dispose();
}
```

**SC-3**: Mock Implementation for Testing

Create `Tests/Mocks/MockSubsystem.cs`:
```csharp
public class MockSubsystem : SubsystemBase
{
    public bool WasInitialized { get; private set; }
    public bool WasStarted { get; private set; }
    public bool WasStopped { get; private set; }
    public int UpdateCount { get; private set; }
    
    public MockSubsystem() : base("Mock", NullLogger.Instance) { }
    
    public override void Initialize(object config)
    {
        WasInitialized = true;
        Status = SubsystemStatus.Initializing;
    }
    
    public override void Start()
    {
        WasStarted = true;
        Status = SubsystemStatus.Running;
    }
    
    public override void Stop()
    {
        WasStopped = true;
        Status = SubsystemStatus.Stopped;
    }
    
    public override void Update(float deltaTime)
    {
        UpdateCount++;
    }
    
    // ... other methods
}
```

**SC-4**: Unit Tests

- `Test_SubsystemStatus_Transitions`: Verify status transitions emit events
- `Test_SubsystemBase_ErrorHandling`: Verify RaiseError() sets status and emits event
- `Test_MockSubsystem_Lifecycle`: Verify mock subsystem lifecycle

### Testing

```csharp
var mock = new MockSubsystem();
var statusChanges = new List<SubsystemStatus>();

mock.OnStatusChanged += status => statusChanges.Add(status);

mock.Initialize(null);
mock.Start();
mock.Update(0.016f);
mock.Stop();

Assert.True(mock.WasInitialized);
Assert.True(mock.WasStarted);
Assert.True(mock.WasStopped);
Assert.Equal(1, mock.UpdateCount);
Assert.Contains(SubsystemStatus.Running, statusChanges);
```

---

## R1.5: Implement SubsystemStatusAnnounce DDS Topic

**Estimated**: 0.5 days  
**Dependencies**: R1.1

### Description
Define and implement the DDS topic for subsystem status announcements (waiting room protocol).

### Success Criteria

**SC-1**: Data Model Added to Bagira.DDS.DataModel

Add to `Bagira.DDS.DataModel/SimDescriptors.cs`:
```csharp
[DdsTopic("SubsystemStatusAnnounce")]
public class SubsystemStatusAnnounce
{
    [DdsKey]
    public int NodeId { get; set; }
    
    public string SubsystemName { get; set; } = string.Empty;  // "simhost", "ig", "ios"
    public byte Status { get; set; }  // SubsystemStatus enum cast to byte
    public long TimestampMs { get; set; }  // Unix epoch milliseconds
    public string Version { get; set; } = "1.0.0";
    public string HostName { get; set; } = string.Empty;
    public uint ProcessId { get; set; }
}
```

**SC-2**: Publisher Service Created

Create `Services/SubsystemStatusPublisher.cs`:
```csharp
public class SubsystemStatusPublisher : IDisposable
{
    private readonly DdsParticipant _participant;
    private readonly DdsWriter<SubsystemStatusAnnounce> _writer;
    private readonly Timer _heartbeatTimer;
    private readonly SubsystemStatusAnnounce _currentStatus;
    
    public SubsystemStatusPublisher(DdsParticipant participant, int nodeId, string subsystemName)
    {
        _participant = participant;
        _writer = new DdsWriter<SubsystemStatusAnnounce>(participant, "SubsystemStatusAnnounce");
        
        _currentStatus = new SubsystemStatusAnnounce
        {
            NodeId = nodeId,
            SubsystemName = subsystemName,
            Version = "1.0.0",
            HostName = Environment.MachineName,
            ProcessId = (uint)Environment.ProcessId
        };
        
        // Publish heartbeat every 1 second
        _heartbeatTimer = new Timer(_ => PublishStatus(), null, 0, 1000);
    }
    
    public void UpdateStatus(SubsystemStatus status)
    {
        _currentStatus.Status = (byte)status;
        PublishStatus();
    }
    
    private void PublishStatus()
    {
        _currentStatus.TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _writer.Write(_currentStatus);
    }
    
    public void Dispose()
    {
        _heartbeatTimer?.Dispose();
        _writer?.Dispose();
    }
}
```

**SC-3**: Unit Tests

Create pub/sub test:
- `Test_StatusAnnounce_PublishSubscribe`: Verify topic can be written and read
- `Test_StatusAnnounce_Heartbeat`: Verify heartbeat timer publishes every 1s
- `Test_StatusAnnounce_KeyResolver`: Verify DDS discovers multiple nodes by NodeId

### Testing

```csharp
using var participant1 = new DdsParticipant(0);
using var participant2 = new DdsParticipant(0);

using var publisher = new SubsystemStatusPublisher(participant1, 1, "simhost");
using var reader = new DdsReader<SubsystemStatusAnnounce>(participant2, "SubsystemStatusAnnounce");

publisher.UpdateStatus(SubsystemStatus.Ready);

Thread.Sleep(100); // Allow DDS propagation

using var samples = reader.Take();
Assert.Single(samples);
Assert.Equal("simhost", samples.First().Data.SubsystemName);
Assert.Equal((byte)SubsystemStatus.Ready, samples.First().Data.Status);
```

---

## R1.6: Implement WaitingRoomCoordinator

**Estimated**: 1.0 days  
**Dependencies**: R1.5

### Description
Implement the waiting room logic that synchronizes subsystem startup.

### Success Criteria

**SC-1**: Coordinator Class Created

Create `Services/WaitingRoomCoordinator.cs`:
```csharp
public class WaitingRoomCoordinator : IDisposable
{
    private readonly DdsParticipant _participant;
    private readonly DdsReader<SubsystemStatusAnnounce> _reader;
    private readonly ILogger _logger;
    
    public WaitingRoomCoordinator(DdsParticipant participant, ILogger logger)
    {
        _participant = participant;
        _reader = new DdsReader<SubsystemStatusAnnounce>(participant, "SubsystemStatusAnnounce");
        _logger = logger;
    }
    
    public async Task WaitForPeersAsync(List<string> requiredSubsystems, int timeoutSeconds)
    {
        var readyPeers = new HashSet<string>();
        var stopwatch = Stopwatch.StartNew();
        
        _logger.LogInformation($"Waiting for peers: {string.Join(", ", requiredSubsystems)}");
        
        while (readyPeers.Count < requiredSubsystems.Count)
        {
            if (stopwatch.Elapsed.TotalSeconds > timeoutSeconds)
            {
                var missing = requiredSubsystems.Except(readyPeers);
                throw new TimeoutException($"Waiting room timeout. Missing subsystems: {string.Join(", ", missing)}");
            }
            
            using var samples = _reader.Take();
            foreach (var sample in samples)
            {
                if (!sample.IsValid) continue;
                
                var status = (SubsystemStatus)sample.Data.Status;
                if (status == SubsystemStatus.Ready || status == SubsystemStatus.Running)
                {
                    if (requiredSubsystems.Contains(sample.Data.SubsystemName))
                    {
                        if (readyPeers.Add(sample.Data.SubsystemName))
                        {
                            _logger.LogInformation($"Peer ready: {sample.Data.SubsystemName} (Node {sample.Data.NodeId})");
                        }
                    }
                }
            }
            
            await Task.Delay(100); // Poll every 100ms
        }
        
        _logger.LogInformation("All peers ready!");
    }
    
    public void Dispose()
    {
        _reader?.Dispose();
    }
}
```

**SC-2**: Timeout Handling

Implement robust timeout:
- Log progress updates every 5 seconds ("Still waiting for X, Y...")
- On timeout, provide clear error message listing missing subsystems
- Support cancellation token for early abort

**SC-3**: Integration with Subsystems

Update `ISubsystem` interface usage:
- Each subsystem announces itself via `SubsystemStatusPublisher`
- Orchestrator calls `WaitForPeersAsync()` before starting subsystems
- Subsystems can query coordinator for peer status

**SC-4**: Unit Tests

Create integration tests:
- `Test_WaitingRoom_AllPeersReady`: Verify completes when all peers announce
- `Test_WaitingRoom_Timeout`: Verify throws TimeoutException if peer missing
- `Test_WaitingRoom_OutOfOrder`: Verify works if peers announce in random order
- `Test_WaitingRoom_EarlyStart`: Verify handles peer already ready before waiting starts

### Testing

```csharp
// Scenario: IG waits for SimHost
using var participant1 = new DdsParticipant(0);
using var participant2 = new DdsParticipant(0);

using var simhostPublisher = new SubsystemStatusPublisher(participant1, 1, "simhost");
using var coordinator = new WaitingRoomCoordinator(participant2, logger);

// Start wait in background
var waitTask = coordinator.WaitForPeersAsync(new List<string> { "simhost" }, timeoutSeconds: 10);

// Simulate slight delay before SimHost announces
await Task.Delay(500);
simhostPublisher.UpdateStatus(SubsystemStatus.Ready);

// Wait should complete
await waitTask; // Should not throw
```

---

# Phase R2: Subsystem Refactoring

## R2.1: Refactor SimHost to SimHostSubsystem Library

**Estimated**: 1.0 days  
**Dependencies**: R1.4, SIMHOST tasks S1-S4 complete

### Description
Refactor existing SimHost code to implement `ISubsystem` interface for embeddability.

### Success Criteria

**SC-1**: Create SimHostSubsystem Class

Create `Bagira.SimHost/SimHostSubsystem.cs`:
```csharp
public class SimHostSubsystem : SubsystemBase
{
    private FdpWorld? _world;
    private DdsParticipant? _participant;
    private SimHostConfiguration _config;
    private Task? _updateLoopTask;
    private CancellationTokenSource? _cancelToken;
    private SubsystemStatusPublisher? _statusPublisher;
    
    public SimHostSubsystem() : base("simhost", LoggerFactory.Create(b => b.AddConsole()).CreateLogger("SimHost"))
    {
    }
    
    public override void Initialize(object config)
    {
        Status = SubsystemStatus.Initializing;
        _config = (SimHostConfiguration)config;
        
        // Create FDP World
        _world = new FdpWorld();
        
        // Add modules (but DON'T connect to DDS yet)
        _world.AddModule<CarKinemModule>();
        _world.AddModule<MissionExecutionModule>();
        _world.AddModule<EntityLifecycleModule>();
        
        if (!_config.Headless)
        {
            // Initialize ImGui standalone window (if needed)
            // Or prepare to share context with IG
        }
        
        Status = SubsystemStatus.Ready;
        Logger.LogInformation("SimHost initialized");
    }
    
    public override void ConnectToDomain(int domainId)
    {
        _participant = new DdsParticipant(domainId);
        
        // Add network module
        var networkModule = new CycloneNetworkModule(_participant);
        _world.AddModule(networkModule);
        
        // Start ID allocator server
        var idAllocator = new DdsIdAllocator(_participant, "IdAllocatorService");
        idAllocator.Start();
        
        // Announce presence
        _statusPublisher = new SubsystemStatusPublisher(_participant, _config.NodeId, "simhost");
        _statusPublisher.UpdateStatus(SubsystemStatus.Ready);
        
        Logger.LogInformation($"SimHost connected to DDS domain {domainId}");
    }
    
    public override void Start()
    {
        Status = SubsystemStatus.Running;
        _cancelToken = new CancellationTokenSource();
        
        // Start update loop in background thread
        _updateLoopTask = Task.Run(UpdateLoop, _cancelToken.Token);
        
        Logger.LogInformation("SimHost started");
    }
    
    private void UpdateLoop()
    {
        var stopwatch = Stopwatch.StartNew();
        var lastTime = 0.0;
        
        while (!_cancelToken.IsCancellationRequested)
        {
            var currentTime = stopwatch.Elapsed.TotalSeconds;
            var dt = (float)(currentTime - lastTime);
            lastTime = currentTime;
            
            _world?.Update(dt);
            
            Thread.Sleep(16); // ~60Hz
        }
    }
    
    public override void Update(float deltaTime)
    {
        // If called externally, update world
        // (only used if NOT running in background thread)
        if (_updateLoopTask == null)
        {
            _world?.Update(deltaTime);
        }
        
        // Update ImGui panels if not headless
        if (!_config.Headless)
        {
            DrawImGuiPanels();
        }
    }
    
    private void DrawImGuiPanels()
    {
        // Draw SimHost control panel (time control, spawner, etc.)
        ImGui.Begin("SimHost Control");
        // ... existing ImGui code from DESIGN-SIMHOST
        ImGui.End();
    }
    
    // ... implement other ISubsystem methods
}
```

**SC-2**: Extract Configuration Model

Create `Bagira.SimHost/SimHostConfiguration.cs`:
```csharp
public class SimHostConfiguration
{
    public int NodeId { get; set; } = 1;
    public bool Headless { get; set; }
    public float TimeScale { get; set; } = 1.0f;
    public bool AutoSpawn { get; set; }
    public int AutoSpawnCount { get; set; } = 10;
    public long AutoSpawnType { get; set; } = 100;  // Default TKB type
}
```

**SC-3**: Move Existing Code

Refactor existing SimHost systems and modules:
- Keep all ECS systems as-is (no changes needed)
- Keep all modules as-is

- Move `Program.cs` logic into `SimHostSubsystem.Initialize()`
- Separate DDS connection logic into `ConnectToDomain()`
- Extract ImGui panel code into `DrawImGuiPanels()`

**SC-4**: Maintain Backwards Compatibility

Ensure existing functionality works:
- Entity spawning still works
- Mission execution still works
- Time control still works
- Recording/replay still works

**SC-5**: Unit Tests

- `Test_SimHost_Initialize`: Verify Initialize() creates FDP world
- `Test_SimHost_ConnectDomain`: Verify DDS connection works
- `Test_SimHost_Start`: Verify Start() begins update loop
- `Test_SimHost_Headless`: Verify headless mode skips ImGui

### Testing

```csharp
var config = new SimHostConfiguration { Headless = true };
var simHost = new SimHostSubsystem();

simHost.Initialize(config);
Assert.Equal(SubsystemStatus.Ready, simHost.Status);

simHost.ConnectToDomain(0);
simHost.Start();

// Wait for a few updates
await Task.Delay(200);

simHost.Stop();
simHost.Dispose();
```

---

## R2.2: Create SimHost Standalone Program.cs

**Estimated**: 0.25 days  
**Dependencies**: R2.1

### Description
Create standalone executable that uses SimHostSubsystem library.

### Success Criteria

**SC-1**: Create Bagira.SimHost.Standalone Project

Create console application project that references `Bagira.SimHost` and `Bagira.Runner`

**SC-2**: Implement Thin Program.cs

Create minimal `Program.cs`:
```csharp
class Program
{
    static async Task<int> Main(string[] args)
    {
        var parser = new Parser(with => with.HelpWriter = Console.Out);
        var result = parser.ParseArguments<SimHostCli>(args);
        
        return await result.MapResult(
            async (SimHostCli opts) => await RunSimHost(opts),
            errors => Task.FromResult(1)
        );
    }
    
    static async Task<int> RunSimHost(SimHostCli opts)
    {
        var config = new SimHostConfiguration
        {
            NodeId = opts.NodeId,
            Headless = opts.Headless,
            TimeScale = opts.TimeScale,
            AutoSpawn = opts.AutoSpawn
        };
        
        var simHost = new SimHostSubsystem();
        
        try
        {
            simHost.Initialize(config);
            simHost.ConnectToDomain(opts.DomainId);
            simHost.Start();
            
            Console.WriteLine("SimHost running. Press Ctrl+C to exit.");
            
            var exitEvent = new ManualResetEvent(false);
            Console.CancelKeyPress += (s, e) => {
                e.Cancel = true;
                exitEvent.Set();
            };
            
            exitEvent.WaitOne();
            
            simHost.Stop();
            simHost.Dispose();
            
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}

class SimHostCli
{
    [Option('d', "domain", Default = 0)]
    public int DomainId { get; set; }
    
    [Option('n', "node-id", Default = 1)]
    public int NodeId { get; set; }
    
    [Option("headless", Default = false)]
    public bool Headless { get; set; }
    
    [Option("time-scale", Default = 1.0f)]
    public float TimeScale { get; set; }
    
    [Option("auto-spawn", Default = false)]
    public bool AutoSpawn { get; set; }
}
```

**SC-3**: Test Standalone Execution

Build and run:
```bash
cd Bagira.SimHost.Standalone
dotnet run -- --domain 0 --headless
```

Expected: SimHost starts, announces via DDS, runs until Ctrl+C

---

## R2.3: Test SimHost Embeddability

**Estimated**: 0.5 days  
**Dependencies**: R2.1, R2.2

### Description
Verify SimHost can be used both standalone and embedded in Runner.

### Success Criteria

**SC-1**: Standalone Mode Test

Run SimHost as standalone executable:
```bash
Bagira.SimHost.Standalone.exe --domain 0
```

Verify:
- SimHost starts
- Announces via DDS (check with DDS spy tool)
- Can spawn entities
- Can control time
- Exits cleanly on Ctrl+C

**SC-2**: Embedded Mode Test

Run SimHost via Runner:
```bash
Bagira.Runner.exe --mode simhost --domain 0
```

Verify same functionality as standalone

**SC-3**: Integration Test

Create integration test:
```csharp
[Test]
public async Task Test_SimHost_RunsInBothModes()
{
    // Test 1: Standalone
    using var standaloneProcess = Process.Start("Bagira.SimHost.Standalone.exe", "--domain 99 --headless");
    await Task.Delay(2000);
    Assert.False(standaloneProcess.HasExited);
    standaloneProcess.Kill();
    
    // Test 2: Embedded
    var config = new RunnerConfiguration { Mode = RunMode.SimHost, DomainId = 99, Headless = true };
    var orchestrator = new SubsystemOrchestrator(config, logger);
    await orchestrator.InitializeAsync();
    await orchestrator.StartAsync();
    
    // Give it time to run
    await Task.Delay(2000);
    
    orchestrator.RequestShutdown();
    await orchestrator.WaitForShutdownAsync();
    
    // Success if no exceptions
}
```

---

## R2.4-R2.9: IG and IOS Subsystem Refactoring

**[Similar structure to SimHost refactoring above]**

Key differences:

**R2.4: IG Refactoring**
- IG must handle Raylib window lifecycle
- IG `Update()` includes rendering loop
- IG can share ImGui context with IOS or use separate window

**R2.7: IOS Refactoring**
- IOS uses DER instead of FDP ECS
- IOS initialization is lighter (just DDS + DER)
- IOS can run in standalone ImGui window or share IG's context

---

# Phase R3: Headless Testing Infrastructure

## R3.1: Implement HeadlessTestExecutor

**Estimated**: 1.5 days  
**Dependencies**: R2.x complete

### Description
Implement automated test script executor for headless CI/CD mode.

### Success Criteria

**SC-1**: Test Executor Class Created

Create `Services/HeadlessTestExecutor.cs`:
```csharp
public class HeadlessTestExecutor
{
    private readonly SubsystemOrchestrator _orchestrator;
    private readonly TestScript _script;
    private readonly ILogger _logger;
    private readonly Dictionary<string, ITestActionHandler> _actionHandlers;
    private readonly TestMetricsCollector _metrics;
    
    public HeadlessTestExecutor(SubsystemOrchestrator orchestrator, string scriptPath, ILogger logger)
    {
        _orchestrator = orchestrator;
        _script = LoadScript(scriptPath);
        _logger = logger;
        _metrics = new TestMetricsCollector();
        
        RegisterActionHandlers();
    }
    
    public async Task<int> RunAsync()
    {
        try
        {
            _logger.LogInformation($"Starting test: {_script.TestName}");
            
            // Initialize subsystems
            await _orchestrator.InitializeAsync();
            await _orchestrator.StartAsync();
            
            // Execute test steps
            var stopwatch = Stopwatch.StartNew();
            foreach (var step in _script.Steps.OrderBy(s => s.Time))
            {
                await WaitUntilTime(stopwatch, step.Time);
                await ExecuteStep(step);
            }
            
            // Wait for duration
            while (stopwatch.Elapsed.TotalSeconds < _script.Duration)
            {
                await Task.Delay(100);
            }
            
            // Shutdown
            _orchestrator.RequestShutdown();
            await _orchestrator.WaitForShutdownAsync();
            
            // Generate report
            var report = GenerateReport();
            SaveReport(report);
            
            return report.Status == "PASS" ? 0 : 1;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Test failed: {ex.Message}");
            return 1;
        }
    }
    
    private async Task ExecuteStep(TestStep step)
    {
        _logger.LogInformation($"[{step.Time:F2}s] Executing: {step.Action}");
        
        var handler = _actionHandlers[step.Action];
        var result = await handler.ExecuteAsync(step.Args);
        
        // Check assertions
        if (step.Assert != null)
        {
            ValidateAssertions(step.Assert, result);
        }
    }
}
```

**SC-2**: Test Action Handler Interface

Create `Models/ITestActionHandler.cs`:
```csharp
public interface ITestActionHandler
{
    string ActionName { get; }
    Task<object?> ExecuteAsync(Dictionary<string, object> args);
}
```

**SC-3**: Register Built-in Handlers

Implement handlers for common actions:
- `WaitActionHandler` - Simple delay
- `AssertAllActionHandler` - Validate metrics
- Subsystem-specific handlers added in R3.3

**SC-4**: Metrics Collection

Create `Services/TestMetricsCollector.cs`:
```csharp
public class TestMetricsCollector
{
    private readonly ConcurrentDictionary<string, List<double>> _samples = new();
    
    public void RecordMetric(string name, double value)
    {
        _samples.GetOrAdd(name, _ => new List<double>()).Add(value);
    }
    
    public MetricSummary GetSummary(string name)
    {
        var values = _samples[name];
        return new MetricSummary
        {
            Min = values.Min(),
            Max = values.Max(),
            Avg = values.Average(),
            P95 = CalculatePercentile(values, 0.95)
        };
    }
}
```

### Testing

Create test script file `test_basic.json`:
```json
{
  "test_name": "Basic Test",
  "duration": 5.0,
  "steps": [
    {"time": 0.0, "action": "wait", "args": {"seconds": 1.0}},
    {"time": 1.0, "action": "assert_all", "assert": {"duration": {"min": 1.0}}}
  ]
}
```

Run:
```bash
Bagira.Runner.exe --mode all --domain 0 --headless --script test_basic.json
```

Expected: Exit code 0, report generated

---

## R3.2: Implement Test Script JSON Parser

**Estimated**: 0.5 days  
**Dependencies**: R3.1

### Description
Implement JSON parser for test script files.

### Success Criteria

**SC-1**: Test Script Model

Create `Models/TestScript.cs`:
```csharp
public class TestScript
{
    public string TestName { get; set; } = string.Empty;
    public double Duration { get; set; }
    public List<TestStep> Steps { get; set; } = new();
}

public class TestStep
{
    public double Time { get; set; }
    public string Action { get; set; } = string.Empty;
    public Dictionary<string, object> Args { get; set; } = new();
    public Dictionary<string, AssertionRule>? Assert { get; set; }
    public int Repeat { get; set; } = 1;
    public double Interval { get; set; }
}

public class AssertionRule
{
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Equals { get; set; }
}
```

**SC-2**: Parser Implementation

Implement `LoadScript(string path)`:
```csharp
private TestScript LoadScript(string path)
{
    var json = File.ReadAllText(path);
    var script = JsonConvert.DeserializeObject<TestScript>(json);
    
    // Validate
    if (script.Duration <= 0)
        throw new InvalidOperationException("Duration must be > 0");
    
    if (!script.Steps.Any())
        throw new InvalidOperationException("Script must have at least one step");
    
    // Expand repeat steps
    script.Steps = ExpandRepeats(script.Steps);
    
    return script;
}

private List<TestStep> ExpandRepeats(List<TestStep> steps)
{
    var expanded = new List<TestStep>();
    
    foreach (var step in steps)
    {
        for (int i = 0; i < step.Repeat; i++)
        {
            var clone = JsonConvert.DeserializeObject<TestStep>(JsonConvert.SerializeObject(step))!;
            clone.Time = step.Time + (i * step.Interval);
            expanded.Add(clone);
        }
    }
    
    return expanded;
}
```

**SC-3**: Unit Tests

- `Test_ParseScript_Valid`: Verify valid script parses
- `Test_ParseScript_InvalidDuration`: Verify error on invalid duration
- `Test_ParseScript_RepeatExpansion`: Verify repeat=3, interval=1.0 creates 3 steps at t=0, t=1, t=2

---

## R3.3: Implement Test Action Handlers

**Estimated**: 2.0 days  
**Dependencies**: R3.1, R2.x

### Description
Implement action handlers for SimHost, IG, and IOS testing actions.

### Success Criteria

**SC-1**: SimHost Action Handlers

Implement in `Services/Handlers/SimHostActionHandlers.cs`:
```csharp
public class SpawnEntityHandler : ITestActionHandler
{
    private readonly SimHostSubsystem _simHost;
    
    public string ActionName => "simhost.spawn_entity";
    
    public async Task<object?> ExecuteAsync(Dictionary<string, object> args)
    {
        var type = (long)args["type"];
        var position = (double[])args["position"];  // [lat, lon, alt]
        
        // Access SimHost internal EntityFactorySystem
        var entity = _simHost.CreateEntityViaFactory(type, new GeoPosition 
        {
            Latitude = position[0],
            Longitude = position[1],
            Altitude = position[2]
        });
        
        return new { EntityId = entity.Id };
    }
}

// Similar for:
// - SetTimeScaleHandler
// - PauseHandler
// - ResumeHandler
// - StartRecordingHandler
```

**SC-2**: IG Action Handlers

Implement:
- `IgCreateLocalOverlayHandler` - Create scribble
- `IgSimulateClickHandler` - Inject MapClickEvent
- `IgSimulateDragHandler` - Inject DragEvent sequence
- `IgMeasureFpsHandler` - Capture framerate

**SC-3**: IOS Action Handlers

Implement:
- `IosSendConfigHandler` - Push MapInteractionConfig
- `IosCreateEntityRequestHandler` - Request entity creation
- `IosAwaitEntityHandler` - Wait for entity in DER (with latency measurement)
- `IosMeasureLatencyHandler` - Measure request→ack roundtrip

**SC-4**: Assertion Validation

Implement `ValidateAssertions()`:
```csharp
private void ValidateAssertions(Dictionary<string, AssertionRule> assertions, object? result)
{
    foreach (var (metricName, rule) in assertions)
    {
        var value = GetMetricValue(result, metricName);
        
        if (rule.Min.HasValue && value < rule.Min.Value)
            throw new AssertionFailedException($"{metricName} = {value}, expected >= {rule.Min}");
        
        if (rule.Max.HasValue && value > rule Max.Value)
            throw new AssertionFailedException($"{metricName} = {value}, expected <= {rule.Max}");
        
        if (rule.Equals.HasValue && Math.Abs(value - rule.Equals.Value) > 0.001)
            throw new AssertionFailedException($"{metricName} = {value}, expected == {rule.Equals}");
    }
}
```

**SC-5**: Integration Test

Create comprehensive test script:
```json
{
  "test_name": "Full Integration Test",
  "duration": 30.0,
  "steps": [
    {"time": 0.0, "action": "simhost.spawn_entity", "args": {"type": 100, "position": [50.0, 14.0, 200.0]}},
    {"time": 0.1, "action": "ios.await_entity", "assert": {"latency_ms": {"max": 100}}},
    {"time": 1.0, "action": "ig.simulate_click", "args": {"x": 500, "y": 500}},
    {"time": 5.0, "action": "ig.measure_fps", "assert": {"fps": {"min": 30}}},
    {"time": 10.0, "action": "simhost.pause"},
    {"time": 15.0, "action": "simhost.resume"}
  ]
}
```

---

## R3.4: Implement Metrics Collection

**Estimated**: 1.0 days  
**Dependencies**: R3.3

### Description
Implement automatic performance metrics collection during test execution.

### Success Criteria

**SC-1**: Metrics Collector Enhanced

Extend `TestMetricsCollector` with automatic collection:
```csharp
public class TestMetricsCollector
{
    private readonly Dictionary<string, PerformanceCounter> _systemCounters = new();
    private readonly Timer _collectionTimer;
    
    public void StartCollection()
    {
        // Start collecting system metrics every 100ms
        _collectionTimer = new Timer(_ => CollectSystemMetrics(), null, 0, 100);
    }
    
    private void CollectSystemMetrics()
    {
        // CPU usage
        var cpuPercent = GetCpuUsage();
        RecordMetric("cpu_percent", cpuPercent);
        
        // Memory usage
        var memoryMb = Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0);
        RecordMetric("memory_mb", memoryMb);
        
        // Network stats (via DDS)
        var (sent, lost) = GetDdsStats();
        RecordMetric("network_packets_sent", sent);
        RecordMetric("network_packets_lost", lost);
    }
}
```

**SC-2**: IG FPS Measurement

Add to IG subsystem:
```csharp
public class IgSubsystem
{
    private readonly CircularBuffer<double> _frameTimesMs = new(60);
    
    public override void Update(float deltaTime)
    {
        _frameTimesMs.Add(deltaTime * 1000.0);
        
        // Calculate FPS
        var avgFrameTime = _frameTimesMs.Items.Average();
        var fps = 1000.0 / avgFrameTime;
        
        // Make available for metrics
        CurrentFps = fps;
    }
    
    public double CurrentFps { get; private set; }
}
```

**SC-3**: Latency Measurement

Implement latency tracking in action handlers:
```csharp
public async Task<object?> ExecuteAsync(Dictionary<string, object> args)
{
    var stopwatch = Stopwatch.StartNew();
    
    // Send request
    await _gateway.CreateEntityAsync(...);
    
    // Wait for response
    await _derRepo.WaitForEntity(...);
    
    stopwatch.Stop();
    
    return new { LatencyMs = stopwatch.Elapsed.TotalMilliseconds };
}
```

---

## R3.5: Implement Test Report Generator

**Estimated**: 0.5 days  
**Dependencies**: R3.4

### Description
Generate structured test report in JSON format.

### Success Criteria

**SC-1**: Report Model

Create `Models/TestReport.cs`:
```csharp
public class TestReport
{
    public string TestName { get; set; } = string.Empty;
    public string Status { get; set; } = "PASS";  // "PASS" or "FAIL"
    public double DurationSeconds { get; set; }
    public Dictionary<string, MetricSummary> Metrics { get; set; } = new();
    public AssertionResults Assertions { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public class MetricSummary
{
    public double Min { get; set; }
    public double Max { get; set; }
    public double Avg { get; set; }
    public double P95 { get; set; }
}

public class AssertionResults
{
    public int Total { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
}
```

**SC-2**: Report Generation

Implement `GenerateReport()`:
```csharp
private TestReport GenerateReport()
{
    var report = new TestReport
    {
        TestName = _script.TestName,
        Status = _metrics.GetFailedAssertions().Any() ? "FAIL" : "PASS",
        DurationSeconds = _stopwatch.Elapsed.TotalSeconds
    };
    
    // Add all collected metrics
    foreach (var metricName in _metrics.GetAllMetricNames())
    {
        report.Metrics[metricName] = _metrics.GetSummary(metricName);
    }
    
    // Add assertion results
    report.Assertions.Total = _metrics.GetTotalAssertions();
    report.Assertions.Passed = _metrics.GetPassedAssertions();
    report.Assertions.Failed = _metrics.GetFailedAssertions().Count;
    
    return report;
}

private void SaveReport(TestReport report)
{
    var json = JsonConvert.SerializeObject(report, Formatting.Indented);
    var filename = $"test_report_{DateTime.Now:yyyyMMdd_HHmmss}.json";
    File.WriteAllText(filename, json);
    Console.WriteLine($"Report saved to: {filename}");
}
```

**SC-3**: Console Output

Also output summary to console:
```
=== TEST RESULTS ===
Test: Entity Creation Latency
Status: PASS
Duration: 60.12s

Metrics:
  entity_creation_latency_ms: min=12, max=85, avg=34.2, p95=67
  config_propagation_ms: min=5, max=45, avg=18.7, p95=38
  ig_fps: min=55, max=62, avg=59.1
  memory_mb: min=245, max=312, avg=278
  
Assertions: 15/15 passed

Report saved to: test_report_20260214_143052.json
```

---

# Phase R4: Integration Testing

## R4.1-R4.6: Integration Testing Tasks

**[Detailed testing procedures for each deployment mode]**

Key tests:
- Single aggregated mode
- Separate applications mode
- Waiting room synchronization
- Headless latency test
- Headless stress test
- Documentation validation

---

## Success Metrics

### Code Coverage
- Runner core: >90%
- Subsystem refactoring: >80%
- Headless infrastructure: >85%

### Performance Targets
- Entity creation latency: <100ms (p95)
- Config propagation: <50ms (p95)
- IG FPS: >30 (min) in headless mode
- Test script execution: <1% overhead

### Documentation
- All deployment modes documented
- CLI reference complete
- Test script format specification
- Integration guide with examples

---

## Appendix: Test Script Examples

### Example 1: Latency Test
```json
{
  "test_name": "Entity Creation Latency",
  "duration": 60.0,
  "steps": [
    {
      "time": 0.0,
      "action": "ios.create_entity_request",
      "args": {"type": 100, "position": [50.0, 14.0, 200.0]},
      "repeat": 100,
      "interval": 0.5
    },
    {
      "time": 0.0,
      "action": "ios.await_entity",
      "assert": {"latency_ms": {"max": 100}},
      "repeat": 100,
      "interval": 0.5
    }
  ]
}
```

### Example 2: Stress Test
```json
{
  "test_name": "System Stress Test",
  "duration": 300.0,
  "steps": [
    {
      "time": 0.0,
      "action": "simhost.spawn_entity",
      "args": {"type": 100, "position": [50.0, 14.0, 200.0]},
      "repeat": 100,
      "interval": 0.1
    },
    {
      "time": 30.0,
      "action": "ig.measure_fps",
      "assert": {"fps": {"min": 30}}
    },
    {
      "time": 60.0,
      "action": "assert_all",
      "assert": {
        "memory_mb": {"max": 1000},
        "cpu_percent": {"max": 80}
      }
    }
  ]
}
```
