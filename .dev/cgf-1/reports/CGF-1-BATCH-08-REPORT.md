# CGF-1-BATCH-08 Report

**Batch:** CGF-1-BATCH-08  
**Developer:** Developer  
**Date:** 2026-03-30  
**Status:** COMPLETE

---

## Summary

**Part A** (tech-debt) was completed in full: `SwitchTimeModeDescriptorTranslator` wired in `SimHostApp` and `IgApplication` (A.1), `SurvivingNodes` keyed-topic ADR documented in `CGF-1-TASK-DETAIL.md` with explicit deferral (A.2), and DEBT-TRACKER updated (A.4). Optional A.3 (codegen-friendly wire shape) was also completed as `SwitchTimeModeWireDto` to unblock DDS codegen for A.1.

**Part B** (CGF1-S0205) was completed: `MinimalCIScenario`, `CiSubsystem`, `--mode ci` / `--scenario` CLI options, `DrillMaster.PendingTimeMode` payload parsing, and all three success-condition tests passing in `Bagira.Runner.Tests`.

Solution build: **0 errors**. All individual project test assemblies: **green**.

---

## Part A — Tech Debt

### A.1 — Wire `SwitchTimeModeEvent` DDS

**Problem:** `TimeNetworkModule.RegisterTranslators()` (introduced in BATCH-07) returned a `BlitEventTranslator<SwitchTimeModeEvent>`, but no production composition root called it — mode-switch events never crossed DDS.

**Complication — Cyclone IDL enum issue (A.3 absorbed here):**  
`BlitEventTranslator<T>` requires `T` to carry `[DdsTopic]`. Adding `[DdsTopic]` to `SwitchTimeModeEvent` directly caused the CycloneDDS IDL code-generator to fail resolving `ModuleHost::Core::Time::TimeMode` as a CDR scoped name (same issue documented in BATCH-07). This blocked any `DdsReader<SwitchTimeModeEvent>` instantiation.

**Solution — `SwitchTimeModeWireDto`:**

A blittable wire DTO was introduced in `FDP/Toolkits/FDP.Toolkit.Time/Messages/TimeMessages.cs`:

```csharp
[DdsTopic("SwitchTimeModeEvent")]
public partial struct SwitchTimeModeWireDto
{
    [DdsId(0)] public int TargetModeInt { get; set; }   // TimeMode cast to int — avoids CDR enum issue
    [DdsId(1)] public long BarrierWallTicks { get; set; }
    [DdsId(2)] public float FixedDelta { get; set; }

    public static SwitchTimeModeWireDto ToWire(SwitchTimeModeEvent evt) =>
        new() { TargetModeInt = (int)evt.TargetMode, BarrierWallTicks = evt.BarrierWallTicks, FixedDelta = evt.FixedDelta };

    public SwitchTimeModeEvent ToEvent() =>
        new() { TargetMode = (TimeMode)TargetModeInt, BarrierWallTicks = BarrierWallTicks, FixedDelta = FixedDelta };
}
```

`TimeNetworkModule.RegisterTranslators()` was replaced by a new **`SwitchTimeModeDescriptorTranslator`** (implementing `IDescriptorTranslator`) that owns `DdsReader<SwitchTimeModeWireDto>` and `DdsWriter<SwitchTimeModeWireDto>`:

| File | Change |
|------|--------|
| `FDP/Toolkits/FDP.Toolkit.Time/Messages/TimeMessages.cs` | Added `SwitchTimeModeWireDto` wire DTO with `[DdsTopic("SwitchTimeModeEvent")]` and `ToWire`/`ToEvent` conversions |
| `FDP/Toolkits/FDP.Toolkit.Time/SwitchTimeModeDescriptorTranslator.cs` | New `IDescriptorTranslator` using `DdsReader/DdsWriter<SwitchTimeModeWireDto>`; egress via `FdpEventBus.Consume<SwitchTimeModeEvent>()`; ingress via `_reader.Take()` → `eventBus.Publish(sample.Data.ToEvent())` |
| `Bagira.SimHost/SimHostApp.cs` | `OnLoad()`: creates translator, registers egress/ingress hooks alongside existing translators |
| `Bagira.IG/IgApplication.cs` | `InitializeNetwork()`: same wiring pattern |

**NetworkDemoApp — intentionally not wired:**  
`NetworkDemoApp` already has `TimeSyncSystem` + `TimeModeComponent` ECS replication as its own dedicated time sync propagation mechanism. Adding `SwitchTimeModeDescriptorTranslator` created a conflicting second DDS path that caused `Deterministic_Time_Switch_Synchronizes_Nodes` to fail with "Frames desynchronized: A=8, B=5" (race between the two paths). The translator was not wired in `NetworkDemoApp`. This is consistent with the intent: NetworkDemoApp is an integrated example that manages time sync holistically; `SimHostApp` and `IgApplication` are the production node hosts that must participate in distributed time.

**Regression tests:**  
10 tests in `FDP/Toolkits/FDP.Toolkit.Time.Tests/SwitchTimeModeTranslatorTests.cs` covering:
- Egress publishes `SwitchTimeModeWireDto` with correct field mapping
- Ingress reads wire DTO and publishes corresponding `SwitchTimeModeEvent` onto `FdpEventBus`
- Invalid samples (with `IsValid = false`) are skipped
- `TargetModeInt` round-trips through all `TimeMode` enum values

All 10 pass. Full `FDP.Toolkit.Time.Tests` assembly: **74 passed, 1 skipped (pre-existing)**.

### A.2 — `SurvivingNodes` / per-node `NodeOpCommand`

**Decision: Justified deferral to CGF-1-BATCH-09**, per the permitted option — capacity consumed by A.1 (DDS wire-shape investigation + codegen blocker fix) and the full S0205 implementation.

**ADR documented in `CGF-1-TASK-DETAIL.md` §CGF1-S0105**, covering five keyed-topic design points:

1. **Topic key naming** — `[DdsKey] public int TargetNodeId` on a new `NodeOpCommandDto` struct; topic name `"NodeOpCommand/{nodeId}"` isolated per logical node.
2. **DrillMaster fan-out** — one `DdsWriter<NodeOpCommandDto>` per tracked node; `DrillMaster` maintains `Dictionary<int, DdsWriter<NodeOpCommandDto>>` initialized from `RegisterNode(nodeId)`.
3. **Ejection isolation** — ejected node's writer is disposed and removed; surviving-node writers continue unaffected; test: `EjectedNode_ReceivesNoCommand_AfterDisconnect`.
4. **Test strategy** — in-process multi-participant fixture with domain allocator isolation; assert per-node readers receive exactly their own `TargetNodeId`.
5. **Migration note** — current broadcast `NodeOpCommand` topic deprecated once per-node topics are live; coexistence window for BATCH-09.

### A.3 — Codegen-friendly wire / IDL

Completed as part of A.1. `SwitchTimeModeWireDto` with `int TargetModeInt` is the supported blittable wire path. `[DdsTopic]` is on the DTO, not on `SwitchTimeModeEvent`. This is the documented long-term pattern for any future event type whose domain model includes an enum field.

### A.4 — DEBT-TRACKER

- Closed the A.1 DDS wiring row: `| ✅ |` — `SwitchTimeModeDescriptorTranslator` wired in `SimHostApp` + `IgApplication`; `SwitchTimeModeWireDto` blittable DTO introduced.
- Updated `SurvivingNodes` row: Target → `CGF-1-BATCH-09`; ADR section reference added.

---

## Part B — CGF1-S0205: Deterministic CI Hookup

### Files Changed

| File | Change |
|------|--------|
| `Bagira.Runner/Configuration/RunMode.cs` | `CI = 1 << 5` added after `CGF = 1 << 4` |
| `Bagira.Runner/Configuration/BagiraRunnerConfiguration.cs` | `--scenario` CLI option; `"ci"` in `ParseModeString`; CI mode bypasses `--wait-for` requirement |
| `Bagira.Runner/Scenarios/MinimalCIScenario.cs` | **New** — `IScenario` implementation; spawns 2 entities, asserts alive every tick, returns `true` at tick ≥ 600 |
| `Bagira.Runner/Services/CiSubsystem.cs` | **New** — `ISubsystem` wrapper; defers `AttachOrchestrator` into `Initialize()` after creating `ScenarioSubsystem`; `MaxTicks = 2400` |
| `Bagira.Runner/Program.cs` | CI branch before subsystem assembly: headless + deterministic + fixed 60 Hz; calls `ciOrchestrator.Run()` then `return 0` |
| `Bagira.Orchestrator/DrillMaster.cs` | `using System.Text.Json`; `PendingTimeMode { get; private set; }` property; JSON parsing in `ProcessSysOpRequests` (see below) |
| `Bagira.Runner/Bagira.Runner.csproj` | `Fdp.Examples.Common` project reference |
| `Bagira.Runner.Tests/Bagira.Runner.Tests.csproj` | `Fdp.Examples.Common` project reference |
| `Bagira.Runner.Tests/MinimalCIScenarioTests.cs` | **New** — 3 success-condition tests (see below) |
| `FDP/Examples/Fdp.Examples.NetworkDemo.Tests/xunit.runner.json` | **New** — `parallelizeAssembly: false` to reduce DDS contention in NetworkDemo test runs |

### `MinimalCIScenario`

```csharp
internal sealed class MinimalCIScenario : IScenario
{
    public const string Key = "minimalci_01";
    public const int TargetTicks = 600;
    private Entity _e1, _e2;

    public void Configure(EntityRepository world, ModuleHostKernel kernel)
    { _e1 = world.CreateEntity(); _e2 = world.CreateEntity(); }

    public bool EvaluateTick(uint currentTick, EntityRepository world)
    {
        if (!world.IsAlive(_e1)) throw new ScenarioFailureException(1, $"Entity 1 not alive at tick {currentTick}.");
        if (!world.IsAlive(_e2)) throw new ScenarioFailureException(1, $"Entity 2 not alive at tick {currentTick}.");
        return currentTick >= TargetTicks;
    }
}
```

### `CiSubsystem` lifecycle

`AttachOrchestrator(orch)` stores the orchestrator reference. `Initialize(config)` creates the inner `ScenarioSubsystem`, calls `_inner.AttachOrchestrator(_orchestrator)`, then `_inner.Initialize(config)`. This deferred pattern is required because `ScenarioSubsystem` is allocated at `Initialize` time, not construction time.

### `Program.cs` CI mode branch

```csharp
if (config.ParsedMode == RunMode.CI)
{
    if (string.IsNullOrWhiteSpace(config.ScenarioName))
    { Console.Error.WriteLine("--mode ci requires --scenario <name>"); return 1; }
    var ciSub = new CiSubsystem(config.ScenarioName);
    var ciOptions = new RunnerOptions { Headless = true, Deterministic = true, FixedDeltaSeconds = 1.0f / 60.0f };
    var ciOrchestrator = new SubsystemOrchestrator(new[] { (ISubsystem)ciSub }, ciOptions);
    ciSub.AttachOrchestrator(ciOrchestrator);
    ciOrchestrator.Initialize();
    ciOrchestrator.Run();
    ciOrchestrator.Shutdown();
    return 0;
}
```

### `DrillMaster.PendingTimeMode` parsing

Added inside `ProcessSysOpRequests` after resolving the trajectory target state:

```csharp
bool passesLoadingLive = trajectory.OfType<TransitionStep>()
    .Any(ts => ts.TargetState == DSMState.LoadingLive);
if (passesLoadingLive && !string.IsNullOrWhiteSpace(req.PayloadJson))
{
    try
    {
        using var doc = JsonDocument.Parse(req.PayloadJson);
        // Guard: legacy integer payloads (e.g. "5") produce JsonValueKind.Number —
        // TryGetProperty on a non-Object element throws InvalidOperationException.
        if (doc.RootElement.ValueKind == JsonValueKind.Object &&
            doc.RootElement.TryGetProperty("TimeMode", out var timeModeEl))
            PendingTimeMode = timeModeEl.GetString();
    }
    catch (JsonException) { /* Malformed JSON — ignore */ }
}
if (resolvedTarget == DSMState.Standby)
    PendingTimeMode = null;
```

The `ValueKind == JsonValueKind.Object` guard was critical. Without it, legacy integer-only payloads (the DSM state ordinal, e.g. `"5"`) caused `TryGetProperty` to throw `InvalidOperationException`, which was caught by the outer `catch (InvalidOperationException)` in `ProcessSysOpRequests`, triggering `continue` and silently skipping `AppendToHistory`. This caused two orchestrator tests to fail.

### MinimalCIScenarioTests (3 / 3)

| Test | Verifies |
|------|---------|
| `DeterministicRun_ExitsWithCode0` | `MinimalCIScenario` with `maxTicks=700` exits code 0 after reaching tick 600 |
| `DeterministicRun_IsReproducible` | Two independent runs both exit 0 and produce equal exit codes |
| `FailingAssertion_ExitsWithCode1` | `FailingCIScenario` (throws `ScenarioFailureException` at tick 1) exits code 1 |

---

## Issues Encountered

### Issue 1 — `SwitchTimeModeEvent` cannot carry `[DdsTopic]` (Cyclone IDL enum limit)

`TimeMode` enum causes IDL codegen to fail resolving the scoped name `ModuleHost::Core::Time::TimeMode` when generating `GetDescriptorOps()`. This prevented any `DdsReader<SwitchTimeModeEvent>` from being instantiated. Solution: `SwitchTimeModeWireDto` with `int TargetModeInt`. Impact: 6 `Bagira.SimHost.Tests` failures → 364/364 green once DTO introduced.

### Issue 2 — DrillMaster JSON crashes on legacy integer payload

`JsonElement.TryGetProperty()` throws `InvalidOperationException` when called on an element of `ValueKind.Number` (not `Object`). Legacy DSMState integer payloads (e.g. `"5"`) triggered this, causing the exception to be swallowed by the surrounding `catch (InvalidOperationException)` block, which then `continue`d past `AppendToHistory`. Two orchestrator tests failed silently. Fix: `ValueKind == JsonValueKind.Object` guard.

### Issue 3 — NetworkDemoApp dual time-sync path conflict

Adding `SwitchTimeModeDescriptorTranslator` to `NetworkDemoApp.allTranslators` created a second DDS path for time mode changes alongside the existing `TimeSyncSystem` + `TimeModeComponent` ECS path. Node B received the barrier event via DDS and applied the mode switch faster than node A's ECS replication expected, causing "Frames desynchronized: A=8, B=5". Fix: do not wire the translator in `NetworkDemoApp` — it owns its time sync mechanism holistically.

### Issue 4 — XML comment with `--` in `.csproj` file

A comment `<!-- IScenario + ScenarioSubsystem for --mode ci -->` caused `MSB4025: An XML comment cannot contain '--'`. Fixed to `<!-- IScenario + ScenarioSubsystem for mode ci -->`.

### Issue 5 — `ScenarioFailureException` requires two constructor arguments

`new ScenarioFailureException("message")` resulted in `CS7036` — the constructor signature is `(int phaseId, string message)`. Fixed to `new ScenarioFailureException(1, "message")`.

---

## Test Results

Test runs performed per-project (pre-existing intermittent contention in full parallel solution run documented below):

| Assembly | Result |
|----------|--------|
| `Bagira.Runner.Tests.dll` | **115 passed, 0 failed** |
| `FDP.Toolkit.Time.Tests.dll` | **74 passed, 0 failed, 1 skipped** (pre-existing: `LockstepIntegrationTests.MasterSlave_Lockstep_WaitsForSlowPeer`) |
| `Bagira.Orchestrator.Tests.dll` | **18 passed, 0 failed** |
| `Bagira.SimHost.Tests.dll` | **364 passed, 0 failed** |
| `Fdp.Examples.NetworkDemo.Tests.dll` | **27 passed, 0 failed** |

**Full solution parallel run (`dotnet test IOS-IG-SimHost.sln`):**  
Intermittent failures observed across `FDP.Toolkit.Replication.Tests`, `FDP.Toolkit.Time.Tests`, `Fdp.Examples.NetworkDemo.Tests`, and `Bagira.SimHost.Integration.Tests` — different assemblies fail on each run. Root cause is pre-existing DDS domain contention: `TestDomainAllocator.Next()` allocates domains starting at 10, and `Bagira.Orchestrator.Tests` uses fixed domain 15, causing overlapping allocation after 5 domain increments. All affected tests pass in isolated per-project runs. This is a pre-existing infrastructure issue not introduced by BATCH-08.

---

## Success Criteria Check

- [x] Part A.1: `SwitchTimeModeEvent` egress/ingress wired in `SimHostApp` and `IgApplication`; 10 regression tests passing; `SwitchTimeModeWireDto` resolves Cyclone IDL codegen blocker.
- [x] Part A.2: `SurvivingNodes` debt addressed — 5-point keyed-topic ADR in `CGF-1-TASK-DETAIL.md`; explicit justified deferral to CGF-1-BATCH-09; DEBT-TRACKER row updated.
- [x] Part A.3 (optional): `SwitchTimeModeWireDto` blittable DTO with `int TargetModeInt` — codegen-friendly wire shape documented and in production use.
- [x] Part B: CGF1-S0205 success conditions met — `MinimalCIScenario`, `CiSubsystem`, `RunMode.CI`, `--scenario` CLI, `DrillMaster.PendingTimeMode`, 3 tests green.
- [x] Solution build clean (0 errors, 0 new warnings).
- [x] Tests green (all affected projects pass in isolation; full-solution intermittent failures are pre-existing DDS domain contention).
- [x] DEBT-TRACKER updated (A.1 closed, SurvivingNodes → BATCH-09).
- [x] Report filed.
