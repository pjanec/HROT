# RUNNER-BATCH-02: Runner Core Infrastructure (Phase R1)

**Batch Number:** RUNNER-BATCH-02  
**Tasks:** R1.1, R1.2, R1.3, R1.4, R1.5, R1.6  
**Phase:** R1 - Runner Core  
**Estimated Effort:** 30-36 hours (5-6 days)  
**Priority:** High  
**Dependencies:** RUNNER-BATCH-01 complete ?

> **?? PREREQUISITE:** Phase R0 (ECS Component ID Safety) must be complete and merged. Component IDs are now deterministic. Set `FdpConfig.EnforceExplicitComponentIds = true` in all production `Program.cs` files.

---

## ?? Onboarding & Workflow

### Developer Instructions

Welcome to Runner Core implementation! With Phase R0 complete, we can now safely merge three independent binaries (SimHost.exe, IG.exe, IOS.exe) into a single `Runner.exe` process.

**The Goal:** Build the Runner application shell that can launch subsystems in different modes:
- **Aggregated Mode (`--mode all`):** All three subsystems in one process
- **Separate Mode (`--mode simhost`, `--mode ig`, `--mode ios`):** Each subsystem runs standalone
- **Headless Mode (`--headless`):** No UI, runs automated test scripts for CI/CD

This batch builds the **orchestration layer** that manages subsystem lifecycle, window ownership, ImGui context sharing, and distributed startup synchronization.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream\README.md` — How to work with batches
2. **Design Document:** `docs\design\DESIGN-RUNNER.md` — Sections 5 (Runner Configuration), 6 (Subsystem Orchestrator), 7 (Waiting Room)
3. **Task Details:** `docs\design\TASK-DETAILS-RUNNER.md` — Phase R1 tasks
4. **Task Tracker:** `docs\design\TASK-TRACKER.md` — RUNNER Phase R1 tasks
5. **Code Standards:** `.dev-workstream\guides\CODE-STANDARDS.md` — §0 (Test Quality), §1 (No Magic Numbers)
6. **Previous Batch Review:** `.dev-workstream\reviews\RUNNER-BATCH-01-REVIEW.md` — Quality standards

### Architect Context
- **Architecture Review (2026-02-26):** `ISubsystem` has `DrawWorld()` + `DrawUI()` render phases. Orchestrator owns Raylib window + render loop.
- **Design Correction:** `SubsystemStatusAnnounce` uses single `[DdsQos(...)]` attribute (not separate `[DdsReliability]`/`[DdsDurability]`).
- **Design Correction:** No `ICameraService` for headless mode — use `HeadlessInputProvider` only.

### Source Code Location
- **Primary Work Area:** `Hrot.ClusterRunner\` (new project)
- **Secondary Areas:** `Hrot.NED\Runner\` (DDS topics)
- **Reference Implementations:**
  - `Hrot.IG\IgApplication.cs` (Raylib + ImGui setup)
  - `Hrot.ExCon\Program.cs` (CommandLineParser usage)
  - `Fdp.Examples.NetworkDemo\` (DDS participant setup)

### Report Submission
**When done, submit your report to:**  
`.dev-workstream\reports\RUNNER-BATCH-02-REPORT.md`

**If you have questions, create:**  
`.dev-workstream\questions\RUNNER-BATCH-02-QUESTIONS.md`

---

## Context

Phase R1 builds the Runner application shell — the "aggregator" that can host SimHost, IG, and IOS in a single process or launch them as separate communicating processes.

**Critical Design Points:**
1. **Window Ownership:** Orchestrator owns Raylib window in aggregated mode. Subsystems must NOT call `InitWindow()`.
2. **ImGui Context:** Orchestrator owns `rlImGui` context in aggregated mode. Subsystems share it.
3. **Waiting Room:** Distributed startup synchronization via DDS `SubsystemStatusAnnounce` topic (transient-local QoS).

**Related Tasks:**
- [R1.1](../../docs/design/TASK-DETAILS-RUNNER.md#r11-create-hrotrunner-project) - Create Hrot.ClusterRunner Project
- [R1.2](../../docs/design/TASK-DETAILS-RUNNER.md#r12-implement-runnerconfiguration-with-cli-parsing) - Implement RunnerConfiguration with CLI Parsing
- [R1.3](../../docs/design/TASK-DETAILS-RUNNER.md#r13-implement-subsystemorchestrator) - Implement SubsystemOrchestrator
- [R1.4](../../docs/design/TASK-DETAILS-RUNNER.md#r14-implement-isubsystem-interface) - Implement ISubsystem Interface
- [R1.5](../../docs/design/TASK-DETAILS-RUNNER.md#r15-implement-subsystemstatusannounce-dds-topic) - Implement SubsystemStatusAnnounce DDS Topic
- [R1.6](../../docs/design/TASK-DETAILS-RUNNER.md#r16-implement-waitingroomcoordinator) - Implement WaitingRoomCoordinator

---

## ?? Batch Objectives

**Primary Goal:** Build Runner application shell that orchestrates subsystem lifecycle, manages window/ImGui ownership, and synchronizes distributed startup.

**Success Criteria:**
- CLI argument parsing supports all modes (`all`, `simhost`, `ig`, `ios`, `simhost,ig`)
- Orchestrator manages subsystem init/update/render/shutdown lifecycle
- Aggregated mode shares single Raylib window + ImGui context across subsystems
- Headless mode skips all render calls
- Waiting room synchronizes startup across separate processes via DDS
- Zero regressions in existing tests
- New tests cover CLI parsing, orchestrator lifecycle, waiting room timeout/discovery

---

## ? Tasks

### Task 1: Create Hrot.ClusterRunner Project (R1.1)

**Task Definition:** See [TASK-DETAILS-RUNNER.md R1.1](../../docs/design/TASK-DETAILS-RUNNER.md#r11-create-hrotrunner-project)

**Estimated:** 2 hours

#### Subtask 1.1: Create Console Application

**Steps:**
1. `dotnet new console -n Hrot.ClusterRunner -f net8.0`
2. `dotnet sln IOS-IG-SimHost.sln add Hrot.ClusterRunner/Hrot.ClusterRunner.csproj`
3. Create folder structure:
   ```
   Hrot.ClusterRunner/
     Program.cs
     Configuration/
     Services/
     Abstractions/
     Models/
   ```

#### Subtask 1.2: Add Project References

**Project References:**
- `Hrot.NED`

**NuGet Packages:**
- `CommandLineParser` (version 2.9.1 or later)
- `Microsoft.Extensions.Logging`
- `Newtonsoft.Json`

**Acceptance Criteria:**
- ? Project compiles
- ? Added to solution
- ? Folder structure created

---

### Task 2: Implement RunnerConfiguration + CLI Parsing (R1.2)

**Task Definition:** See [TASK-DETAILS-RUNNER.md R1.2](../../docs/design/TASK-DETAILS-RUNNER.md#r12-implement-runnerconfiguration-with-cli-parsing)

**Estimated:** 4 hours

#### Subtask 2.1: Create RunMode Enum

**File:** `Configuration/RunMode.cs` (NEW FILE)

**Code:**
```csharp
namespace Hrot.ClusterRunner.Configuration
{
    [Flags]
    public enum RunMode
    {
        None = 0,
        SimHost = 1 << 0,  // 1
        IG = 1 << 1,       // 2
        IOS = 1 << 2,      // 4
        All = SimHost | IG | IOS
    }
}
```

#### Subtask 2.2: Create RunnerConfiguration Class

**File:** `Configuration/RunnerConfiguration.cs` (NEW FILE)

**Requirements:**
- CLI options via `CommandLineParser` attributes
- `Validate()` method throws on invalid combinations
- `MergeFromJsonFile(string path)` merges JSON config over CLI defaults

**Code Pattern:**
```csharp
using CommandLine;
using Newtonsoft.Json;

namespace Hrot.ClusterRunner.Configuration
{
    public class RunnerConfiguration
    {
        [Option('m', "mode", Required = true, HelpText = "all|simhost|ig|ios|simhost,ig")]
        public string ModeString { get; set; } = string.Empty;
        
        [Option('d', "domain", Default = 0, HelpText = "DDS domain ID")]
        public int DomainId { get; set; }
        
        [Option("headless", Default = false, HelpText = "Run without UI")]
        public bool Headless { get; set; }
        
        [Option("no-wait", Default = false, HelpText = "Skip waiting room sync")]
        public bool NoWait { get; set; }
        
        [Option("wait-for", HelpText = "simhost,ig,ios (comma-separated)")]
        public string WaitForString { get; set; } = string.Empty;
        
        [Option('c', "config", HelpText = "JSON config file path")]
        public string ConfigFile { get; set; } = string.Empty;
        
        // Parsed values
        public RunMode ParsedMode { get; set; }
        public HashSet<string> WaitForPeers { get; set; } = new();
        
        public void Validate()
        {
            // Parse ModeString ? ParsedMode
            ParsedMode = ParseModeString(ModeString);
            if (ParsedMode == RunMode.None)
                throw new InvalidOperationException($"Invalid mode: '{ModeString}'. Use: all, simhost, ig, ios, or comma-separated.");
            
            // Parse WaitForString ? WaitForPeers
            if (!string.IsNullOrWhiteSpace(WaitForString))
            {
                WaitForPeers = new HashSet<string>(
                    WaitForString.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                 .Select(s => s.Trim().ToLowerInvariant()));
            }
            
            // Validation rules
            if (!NoWait && WaitForPeers.Count == 0 && ParsedMode != RunMode.All)
                throw new InvalidOperationException("--wait-for required when launching separate subsystems without --no-wait.");
        }
        
        public void MergeFromJsonFile(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Config file not found: {path}");
            
            var json = File.ReadAllText(path);
            var overrides = JsonConvert.DeserializeObject<RunnerConfiguration>(json);
            
            // Merge non-default values
            if (!string.IsNullOrEmpty(overrides.ModeString))
                ModeString = overrides.ModeString;
            if (overrides.DomainId != 0)
                DomainId = overrides.DomainId;
            // ... merge other properties ...
        }
        
        private static RunMode ParseModeString(string str)
        {
            var lower = str.ToLowerInvariant();
            if (lower == "all") return RunMode.All;
            if (lower == "simhost") return RunMode.SimHost;
            if (lower == "ig") return RunMode.IG;
            if (lower == "ios") return RunMode.IOS;
            
            // Parse comma-separated
            RunMode result = RunMode.None;
            foreach (var part in lower.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                switch (part.Trim())
                {
                    case "simhost": result |= RunMode.SimHost; break;
                    case "ig": result |= RunMode.IG; break;
                    case "ios": result |= RunMode.IOS; break;
                    default: return RunMode.None; // Invalid
                }
            }
            return result;
        }
    }
}
```

**Design Reference:** [DESIGN-RUNNER.md Section 5](../../docs/design/DESIGN-RUNNER.md#5-runner-configuration)

#### Subtask 2.3: Write Unit Tests

**File:** `Hrot.ClusterRunner.Tests/RunnerConfigurationTests.cs` (NEW FILE)

**Requirements:** Minimum 12 tests covering:
- Mode parsing: "all", "simhost,ig", invalid values
- Flags: `--headless`, `--no-wait`
- Wait-for parsing: "simhost,ig,ios"
- JSON merge overwrites CLI defaults
- Validation errors: wait-for without peers, invalid mode

**Test Pattern:**
```csharp
[Fact]
public void ParseMode_All_ReturnsAllFlags()
{
    var config = new RunnerConfiguration { ModeString = "all" };
    config.Validate();
    Assert.Equal(RunMode.All, config.ParsedMode);
}

[Fact]
public void ParseMode_ComboSimHostIg_ReturnsCorrectFlags()
{
    var config = new RunnerConfiguration { ModeString = "simhost,ig" };
    config.Validate();
    Assert.Equal(RunMode.SimHost | RunMode.IG, config.ParsedMode);
}

[Fact]
public void Validate_ThrowsOnInvalidMode()
{
    var config = new RunnerConfiguration { ModeString = "invalid" };
    var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
    Assert.Contains("Invalid mode", ex.Message, StringComparison.OrdinalIgnoreCase);
}
```

**Acceptance Criteria:**
- ? All 12 CLI tests pass
- ? Exception messages descriptive

---

### Task 3: Implement SubsystemOrchestrator (R1.3)

**Task Definition:** See [TASK-DETAILS-RUNNER.md R1.3](../../docs/design/TASK-DETAILS-RUNNER.md#r13-implement-subsystemorchestrator)

**Estimated:** 12 hours

#### Subtask 3.1: Create SubsystemConfig Model

**File:** `Models/SubsystemConfig.cs` (NEW FILE)

```csharp
namespace Hrot.ClusterRunner.Models
{
    public class SubsystemConfig
    {
        public int DomainId { get; set; }
        public bool Headless { get; set; }
        public bool OwnWindow { get; set; }
        public string SubsystemName { get; set; } = string.Empty;
    }
}
```

#### Subtask 3.2: Implement SubsystemOrchestrator

**File:** `Services/SubsystemOrchestrator.cs` (NEW FILE)

**Requirements:**
- Manages subsystem lifecycle (Initialize ? Update loop ? Shutdown)
- Owns Raylib window + ImGui context in aggregated mode
- Calls `DrawWorld()` + `DrawUI()` on all subsystems during Render
- Skips Render entirely when `_headless = true`

**Code Pattern:**
```csharp
using Raylib_cs;
using rlImgui_cs;
using Hrot.ClusterRunner.Abstractions;
using Hrot.ClusterRunner.Models;

namespace Hrot.ClusterRunner.Services
{
    public class SubsystemOrchestrator
    {
        private readonly List<ISubsystem> _subsystems = new();
        private readonly bool _headless;
        private readonly int _windowWidth;
        private readonly int _windowHeight;
        private bool _running;
        
        public SubsystemOrchestrator(RunnerConfiguration config)
        {
            _headless = config.Headless;
            _windowWidth = 1600;
            _windowHeight = 900;
            
            // Create subsystems based on config.ParsedMode
            if (config.ParsedMode.HasFlag(RunMode.SimHost))
                _subsystems.Add(new SimHostSubsystem()); // Stub for now
            if (config.ParsedMode.HasFlag(RunMode.IG))
                _subsystems.Add(new IgSubsystem()); // Stub for now
            if (config.ParsedMode.HasFlag(RunMode.IOS))
                _subsystems.Add(new IosSubsystem()); // Stub for now
        }
        
        public void Initialize()
        {
            // Init Raylib window if NOT headless
            if (!_headless)
            {
                Raylib.InitWindow(_windowWidth, _windowHeight, "Hrot Runner");
                Raylib.SetTargetFPS(60);
                rlImGui.Setup(true);
            }
            
            // Init subsystems
            foreach (var subsystem in _subsystems)
            {
                var config = new SubsystemConfig
                {
                    Headless = _headless,
                    OwnWindow = false, // Orchestrator owns window
                    SubsystemName = subsystem.Name
                };
                subsystem.Initialize(config);
            }
        }
        
        public void Run()
        {
            _running = true;
            while (_running && (_headless || !Raylib.WindowShouldClose()))
            {
                float dt = Raylib.GetFrameTime();
                Update(dt);
                
                if (!_headless)
                    Render();
            }
        }
        
        private void Update(float dt)
        {
            foreach (var subsystem in _subsystems)
                subsystem.Update(dt);
        }
        
        private void Render()
        {
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);
            
            // World rendering
            foreach (var subsystem in _subsystems)
                subsystem.DrawWorld();
            
            // UI rendering
            rlImGui.Begin();
            foreach (var subsystem in _subsystems)
                subsystem.DrawUI();
            rlImGui.End();
            
            Raylib.EndDrawing();
        }
        
        public void Shutdown()
        {
            // Shutdown subsystems in reverse order
            for (int i = _subsystems.Count - 1; i >= 0; i--)
                _subsystems[i].Shutdown();
            
            if (!_headless)
            {
                rlImGui.Shutdown();
                Raylib.CloseWindow();
            }
        }
        
        public void Stop() => _running = false;
    }
}
```

**Design Reference:** [DESIGN-RUNNER.md Section 6](../../docs/design/DESIGN-RUNNER.md#6-subsystem-orchestrator)

#### Subtask 3.3: Write Unit Tests

**File:** `Hrot.ClusterRunner.Tests/SubsystemOrchestratorTests.cs` (NEW FILE)

**Requirements:** Minimum 6 tests covering:
- Initialization order (subsystems init before first Update)
- Update loop calls all subsystems
- Shutdown reverse order
- Headless mode skips Render
- Aggregated mode creates window (visual test or stub check)

**Test Pattern:**
```csharp
[Fact]
public void Orchestrator_CallsInitializeOnAllSubsystems()
{
    var mockSubsystem = new MockSubsystem();
    // ... inject mock, run Initialize(), assert init called
}

[Fact]
public void Orchestrator_HeadlessMode_SkipsRender()
{
    var config = new RunnerConfiguration { ModeString = "all", Headless = true };
    var orchestrator = new SubsystemOrchestrator(config);
    // Assert Raylib.InitWindow never called
}
```

**Acceptance Criteria:**
- ? 6+ tests pass
- ? No Raylib calls when headless = true

---

### Task 4: Implement ISubsystem Interface (R1.4)

**Task Definition:** See [TASK-DETAILS-RUNNER.md R1.4](../../docs/design/TASK-DETAILS-RUNNER.md#r14-implement-isubsystem-interface)

**Estimated:** 4 hours

#### Subtask 4.1: Create ISubsystem Interface

**File:** `Abstractions/ISubsystem.cs` (NEW FILE)

```csharp
using Hrot.ClusterRunner.Models;

namespace Hrot.ClusterRunner.Abstractions
{
    /// <summary>
    /// Interface for Runner subsystems (SimHost, IG, IOS).
    /// Orchestrator calls Initialize/Update/DrawWorld/DrawUI/Shutdown in strict order.
    /// </summary>
    public interface ISubsystem
    {
        string Name { get; }
        
        void Initialize(SubsystemConfig config);
        void Update(float deltaTime);
        void DrawWorld();
        void DrawUI();
        void Shutdown();
    }
}
```

**Design Reference:** [DESIGN-RUNNER.md Section 6.2](../../docs/design/DESIGN-RUNNER.md#62-isubsystem-interface)

#### Subtask 4.2: Create Mock Subsystem for Tests

**File:** `Hrot.ClusterRunner.Tests/Mocks/MockSubsystem.cs` (NEW FILE)

```csharp
using Hrot.ClusterRunner.Abstractions;
using Hrot.ClusterRunner.Models;

namespace Hrot.ClusterRunner.Tests.Mocks
{
    public class MockSubsystem : ISubsystem
    {
        public string Name => "MockSubsystem";
        public bool InitializeCalled { get; private set; }
        public bool UpdateCalled { get; private set; }
        public bool DrawWorldCalled { get; private set; }
        public bool DrawUICalled { get; private set; }
        public bool ShutdownCalled { get; private set; }
        
        public void Initialize(SubsystemConfig config) => InitializeCalled = true;
        public void Update(float deltaTime) => UpdateCalled = true;
        public void DrawWorld() => DrawWorldCalled = true;
        public void DrawUI() => DrawUICalled = true;
        public void Shutdown() => ShutdownCalled = true;
    }
}
```

**Acceptance Criteria:**
- ? Interface compiles
- ? Mock subsystem usable in orchestrator tests

---

### Task 5: Implement SubsystemStatusAnnounce DDS Topic (R1.5)

**Task Definition:** See [TASK-DETAILS-RUNNER.md R1.5](../../docs/design/TASK-DETAILS-RUNNER.md#r15-implement-subsystemstatusannounce-dds-topic)

**Estimated:** 4 hours

#### Subtask 5.1: Create SubsystemStatusAnnounce Struct

**File:** `Hrot.NED/Runner/SubsystemStatusAnnounce.cs` (NEW FILE)

```csharp
using CycloneDDS.Schema;

namespace Hrot.NED.Runner
{
    [DdsTopic("SubsystemStatusAnnounce")]
    [DdsIdlFile("runner-msgs")]
    [DdsQos(
        Reliability = DdsReliability.Reliable,
        Durability = DdsDurability.TransientLocal,
        HistoryKind = DdsHistoryKind.KeepLast,
        HistoryDepth = 1)]
    public partial struct SubsystemStatusAnnounce
    {
        [DdsKey]
        public int NodeId;
        
        public string SubsystemName;
        public int DomainId;
        public bool Ready;
        public long Timestamp;
    }
}
```

**Design Reference:** [DESIGN-RUNNER.md Section 7.2](../../docs/design/DESIGN-RUNNER.md#72-subsystemstatusannounce-topic)

#### Subtask 5.2: Write DDS Pub/Sub Test

**File:** `Hrot.NED.Tests/SubsystemStatusAnnounceTests.cs` (NEW FILE)

**Requirements:**
- Test DDS pub/sub round-trip
- Test TransientLocal durability (late joiner sees previous announcement)

**Test Pattern:**
```csharp
[Fact]
public async Task SubsystemStatusAnnounce_PubSub_RoundTrip()
{
    using var participant = new DdsParticipant(0);
    var writer = new DdsWriter<SubsystemStatusAnnounce>(participant, "SubsystemStatusAnnounce");
    var reader = new DdsReader<SubsystemStatusAnnounce>(participant, "SubsystemStatusAnnounce");
    
    var sample = new SubsystemStatusAnnounce
    {
        NodeId = 100,
        SubsystemName = "SimHost",
        DomainId = 0,
        Ready = true,
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    };
    
    writer.Write(sample);
    await Task.Delay(100);
    
    using var samples = reader.Take();
    Assert.Single(samples);
    Assert.Equal(100, samples[0].Data.NodeId);
    Assert.Equal("SimHost", samples[0].Data.SubsystemName);
    Assert.True(samples[0].Data.Ready);
}
```

**Acceptance Criteria:**
- ? DDS pub/sub test passes
- ? TransientLocal QoS verified

---

### Task 6: Implement WaitingRoomCoordinator (R1.6)

**Task Definition:** See [TASK-DETAILS-RUNNER.md R1.6](../../docs/design/TASK-DETAILS-RUNNER.md#r16-implement-waitingroomcoordinator)

**Estimated:** 12 hours

#### Subtask 6.1: Create SubsystemPeerInfo Model

**File:** `Models/SubsystemPeerInfo.cs` (NEW FILE)

```csharp
namespace Hrot.ClusterRunner.Models
{
    public class SubsystemPeerInfo
    {
        public int NodeId { get; set; }
        public string SubsystemName { get; set; } = string.Empty;
        public int DomainId { get; set; }
        public bool Ready { get; set; }
        public long LastSeenTimestamp { get; set; }
    }
}
```

#### Subtask 6.2: Implement WaitingRoomCoordinator

**File:** `Services/WaitingRoomCoordinator.cs` (NEW FILE)

**Requirements:**
- Publishes `SubsystemStatusAnnounce` with `Ready = false` ? poll DDS ? set `Ready = true` when peers discovered ? blocks until all peers ready or timeout
- Timeout: 30 seconds (configurable constant `WAITING_ROOM_TIMEOUT_MS`)

**Code Pattern:**
```csharp
using CycloneDDS.Runtime;
using Hrot.NED.Runner;
using Hrot.ClusterRunner.Models;

namespace Hrot.ClusterRunner.Services
{
    public class WaitingRoomCoordinator
    {
        private const int WAITING_ROOM_TIMEOUT_MS = 30_000;
        
        private readonly DdsParticipant _participant;
        private readonly DdsWriter<SubsystemStatusAnnounce> _writer;
        private readonly DdsReader<SubsystemStatusAnnounce> _reader;
        private readonly int _localNodeId;
        private readonly string _subsystemName;
        private readonly HashSet<string> _requiredPeers;
        
        public WaitingRoomCoordinator(
            DdsParticipant participant,
            int localNodeId,
            string subsystemName,
            HashSet<string> requiredPeers)
        {
            _participant = participant;
            _localNodeId = localNodeId;
            _subsystemName = subsystemName;
            _requiredPeers = requiredPeers;
            
            _writer = new DdsWriter<SubsystemStatusAnnounce>(participant, "SubsystemStatusAnnounce");
            _reader = new DdsReader<SubsystemStatusAnnounce>(participant, "SubsystemStatusAnnounce");
        }
        
        public void WaitForPeers()
        {
            // Announce self (not ready)
            PublishStatus(ready: false);
            
            var discovered = new HashSet<string>();
            var stopwatch = Stopwatch.StartNew();
            
            while (discovered.Count < _requiredPeers.Count)
            {
                if (stopwatch.ElapsedMilliseconds > WAITING_ROOM_TIMEOUT_MS)
                    throw new TimeoutException($"Waiting room timeout after {WAITING_ROOM_TIMEOUT_MS}ms. Expected peers: {string.Join(", ", _requiredPeers)}. Discovered: {string.Join(", ", discovered)}");
                
                // Poll DDS
                using var samples = _reader.Take();
                foreach (var sample in samples)
                {
                    if (!sample.IsValid || sample.Info.InstanceState != DdsInstanceState.Alive)
                        continue;
                    
                    var peer = sample.Data;
                    if (peer.NodeId == _localNodeId)
                        continue; // Ignore self
                    
                    if (_requiredPeers.Contains(peer.SubsystemName.ToLowerInvariant()))
                        discovered.Add(peer.SubsystemName.ToLowerInvariant());
                }
                
                Thread.Sleep(100);
            }
            
            // All peers discovered — announce ready
            PublishStatus(ready: true);
        }
        
        private void PublishStatus(bool ready)
        {
            var status = new SubsystemStatusAnnounce
            {
                NodeId = _localNodeId,
                SubsystemName = _subsystemName,
                DomainId = _participant.DomainId,
                Ready = ready,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            _writer.Write(status);
        }
    }
}
```

**Design Reference:** [DESIGN-RUNNER.md Section 7](../../docs/design/DESIGN-RUNNER.md#7-waiting-room-protocol)

#### Subtask 6.3: Write Unit Tests

**File:** `Hrot.ClusterRunner.Tests/WaitingRoomCoordinatorTests.cs` (NEW FILE)

**Requirements:** Minimum 6 tests covering:
- Peer discovery (all peers announce ? WaitForPeers returns)
- Timeout (no peers ? throws TimeoutException after 30s)
- Self-ignore (own announcements ignored)
- TransientLocal late joiner (peer announced before WaitForPeers called)

**Test Pattern:**
```csharp
[Fact]
public void WaitForPeers_AllPeersPresent_ReturnsSuccessfully()
{
    // Start 3 participants, all announce, verify WaitForPeers returns
}

[Fact]
public void WaitForPeers_Timeout_ThrowsTimeoutException()
{
    var config = new RunnerConfiguration
    {
        ModeString = "simhost",
        WaitForString = "ig,ios",
        NoWait = false
    };
    config.Validate();
    
    var coordinator = new WaitingRoomCoordinator(..., config.WaitForPeers);
    
    var ex = Assert.Throws<TimeoutException>(() => coordinator.WaitForPeers());
    Assert.Contains("timeout", ex.Message, StringComparison.OrdinalIgnoreCase);
}
```

**Acceptance Criteria:**
- ? 6+ tests pass
- ? Timeout test completes in <5s (use reduced timeout constant for tests)

---

## ?? Testing Requirements

**Minimum Test Count:** 40+ tests total
- R1.2 CLI parsing: 12+ tests
- R1.3 Orchestrator: 6+ tests
- R1.4 ISubsystem: 2+ tests (interface + mock)
- R1.5 DDS topic: 2+ tests (pub/sub + late joiner)
- R1.6 Waiting room: 6+ tests

**Test Categories:**
1. **CLI Parsing:** Mode strings, flags, JSON merge, validation errors
2. **Orchestrator Lifecycle:** Init order, update loop, shutdown reverse order
3. **Headless Mode:** No Raylib/ImGui calls
4. **Waiting Room:** Peer discovery, timeout, self-ignore

**Quality Standards:**
- Tests verify **behavior** (peers discovered, timeout throws), not just "can I call this method"
- Exception tests check message content (case-insensitive `Assert.Contains`)
- No LINQ or `new` in orchestrator update loop

---

## ?? Report Requirements

When submitting your report, answer these questions:

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did you spot any weak points in the existing codebase (DDS API, Raylib integration)? What would you improve?

**Q3:** How many CLI arguments/modes/configurations are supported? (Count exact numbers)

**Q4:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q5:** Did you discover any edge cases not mentioned in the spec (e.g., multiple Runner instances on same domain)?

**Q6:** Are there any performance concerns with the waiting room polling loop? (e.g., 100ms sleep vs DDS async notifications)

---

## ?? Success Criteria

This batch is DONE when:
- [ ] R1.1–R1.6 Complete: Project created, CLI parsing, orchestrator, ISubsystem, DDS topic, waiting room
- [ ] All new unit tests pass (40+ tests)
- [ ] Integration tests pass: R1-IT-001 (headless aggregated), R1-IT-002 (timeout), R1-IT-003 (3-process discovery)
- [ ] All existing tests pass (zero regressions)
- [ ] Report submitted with answers to Q1–Q6

---

## ?? Quality Standards

**? CODE QUALITY EXPECTATIONS**
- Follow CODE-STANDARDS.md §1 (No Magic Numbers): `WAITING_ROOM_TIMEOUT_MS = 30_000` (not literal)
- All public APIs have XML doc comments
- Exception messages are descriptive and actionable
- No LINQ in update loops

**? TEST QUALITY EXPECTATIONS**
- **NOT ACCEPTABLE:** Tests that only verify "can I create this object"
- **REQUIRED:** Tests verify peers discovered, timeout throws with message, shutdown order correct
- **REQUIRED:** Tests verify exception messages contain expected keywords (case-insensitive)

**? REPORT QUALITY EXPECTATIONS**
- **REQUIRED:** Document exact counts (CLI arguments, modes, waiting room states)
- **REQUIRED:** Document design decisions (e.g., why 100ms polling interval in waiting room)
- **REQUIRED:** Share insights on orchestrator architecture and potential improvements

---

## ?? Common Pitfalls to Avoid

1. **Subsystems calling `InitWindow()`:** Orchestrator owns window. Subsystems must check `config.OwnWindow` flag.

2. **ImGui context sharing:** IG and IOS both use ImGui. Orchestrator calls `rlImGui.Setup()` once. Subsystems must NOT call it.

3. **Waiting room self-discovery:** Filter out own NodeId when reading `SubsystemStatusAnnounce`.

4. **Headless mode Raylib calls:** Guard all `Raylib.*` and `rlImGui.*` calls with `if (!_headless)`.

5. **Timeout in tests:** Use reduced timeout constant (e.g., 1000ms) in waiting room tests to avoid 30s test runs.

6. **DDS domain conflicts:** Multiple Runner instances on same domain will see each other's announcements. Enforce unique domain IDs via `--domain` flag.

---

## ?? Reference Materials

- **Design:** `docs\design\DESIGN-RUNNER.md` — Sections 5, 6, 7
- **Task Details:** `docs\design\TASK-DETAILS-RUNNER.md` — Phase R1
- **Task Tracker:** `docs\design\TASK-TRACKER.md` — RUNNER Phase R1
- **Code Standards:** `.dev-workstream\guides\CODE-STANDARDS.md` — §0 (Test Quality), §1 (No Magic Numbers)
- **Reference Implementation:** `Hrot.IG\IgApplication.cs` (Raylib + ImGui setup)
- **Reference Implementation:** `Hrot.ExCon\Program.cs` (CommandLineParser usage)
- **Reference Implementation:** `Fdp.Examples.NetworkDemo\` (DDS participant setup)

---

## ?? Workflow Reminder

1. **Read all required documents** (in order listed in Onboarding)
2. **Implement R1.1 first** (project must exist before other tasks)
3. **Write tests as you go** (don't defer all tests to the end)
4. **Run ALL tests** after each subtask (verify no regressions)
5. **Submit complete report** when all R1.1–R1.6 are done

---

**Questions?** Create `.dev-workstream\questions\RUNNER-BATCH-02-QUESTIONS.md`

Good luck! This is core infrastructure work — orchestrator is the foundation for all future Runner features. ??
