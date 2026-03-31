# CGF-1 BATCH-30 — S0507: IOS Remote Cluster Control Panel + P3 Cleanup

## Objective

Implement **CGF1-S0507** (IOS Remote Cluster Control Panel) and close two P3 technical-debt items scheduled from BATCH-29.

**Definition of Done:**

- `Hrot.ExCon` exposes `TimePulseIngressHandler`, `TimeModeIngressHandler`, and time-state API on `IIosLogic`/`IosLogic`.
- `IosSubsystem` wires `ClusterUiCache` + `ClusterScenarioPanel` and renders a "Cluster Control" ImGui window from the IOS process.
- Dead `OrchestratorScenarioPanel` + its tests removed; dead `_drillTime` field in `OrchestratorSubsystem` removed.
- All existing tests still pass; new tests added as specified below.
- No build warnings in changed files.

---

## Context

### Current test baseline (all must remain green)

| Project | Tests |
|---------|-------|
| `Hrot.NED.Tests` | 47 |
| `Hrot.Orchestrator.Tests` | 60 |
| `Hrot.ClusterRunner.Tests` | 177 |
| `Hrot.ExCon.Tests` | 340 |
| **Total** | **624** |

### Key architecture facts

- `IIngressHandler` (in `Hrot.ExCon`) has a single `void Poll()` method; disposable handlers implement `IDisposable`.
- `DdsReader<T>.Take()` returns a loan; pattern: `using var l = _reader.Take(); foreach (var s in l) { if (!s.IsValid) continue; ... }`.
- `DdsWriter<T>` (no explicit topic string needed) is the live type; tests use `Mock<IDdsWriter<T>>`.
- `ClusterUiCache(DdsParticipant)` — already implemented in `Hrot.ClusterRunner/Services/ClusterUiCache.cs`.
- `ClusterScenarioPanel(DdsWriter<ClusterOpRequest>, ClusterUiCache, Action? requestPause = null)` — already implemented in `Hrot.ClusterRunner/Services/ClusterScenarioPanel.cs`.
- `IosLogic` does **not** yet have a `_sysOpWriter` field — it must be added.
- `ClusterOpType` values for time commands: `PauseTime = 9`, `ResumeTime = 10`, `StepTime = 14`, `SetTimeScale = 15`.
- `TimePulseDescriptor` field names: `.MasterWallTicks` (long), `.SimTimeSnapshot` (double — this is MasterSimTime), `.TimeScale` (float).
- `SwitchTimeModeWireDto` field name: `.TargetModeInt` (cast to `TimeMode` enum, **not** `.Mode`).
- `ClusterOpRequest` struct fields: `int OperationType`, `string PayloadJson`.
- `new DdsWriter<ClusterOpRequest>(_participant)` — no topic string needed.

---

## P3 Debt: Remove Dead Code

### Task P3-A — Delete `OrchestratorScenarioPanel.cs`

File: `D:\Work\IOS-IG-SimHost-FDP-2\Hrot.ClusterRunner\Services\OrchestratorScenarioPanel.cs`

This class is no longer instantiated anywhere (superseded by `ClusterScenarioPanel`). Delete the file.

### Task P3-B — Delete `OrchestratorScenarioPanelTests.cs`

File: `D:\Work\IOS-IG-SimHost-FDP-2\Hrot.ClusterRunner.Tests\OrchestratorScenarioPanelTests.cs`

This test file only covers the deleted `OrchestratorScenarioPanel`. Any tests that exercise general orchestrator behaviour (scenarios, drills, checkpoints) should be verified to already have equivalents in `ClusterScenarioPanelTests.cs`. Delete the file.

### Task P3-C — Remove dead `_drillTime` field from `OrchestratorSubsystem`

File: `D:\Work\IOS-IG-SimHost-FDP-2\Hrot.ClusterRunner\Services\OrchestratorSubsystem.cs`

The `_drillTime` field is computed inside `DrawUI()` but never used. Search for it and remove the line (typically something like `var _drillTime = ...` or a field declaration + assignment with no reads).

---

## S0507 Implementation

### Step 1 — Add Time Ingress Handlers

Add two handler classes to `Hrot.ExCon/Services/DdsEventIngressHandlers.cs` (append to the existing file):

```csharp
/// <summary>
/// DDS ingress handler that forwards TimePulseDescriptor samples to IosLogic.
/// </summary>
public sealed class TimePulseIngressHandler : IIngressHandler, IDisposable
{
    private readonly DdsReader<TimePulseDescriptor>  _reader;
    private readonly Action<TimePulseDescriptor>     _onPulse;

    public TimePulseIngressHandler(DdsParticipant participant, Action<TimePulseDescriptor> onPulse)
    {
        _reader  = new DdsReader<TimePulseDescriptor>(participant);
        _onPulse = onPulse ?? throw new ArgumentNullException(nameof(onPulse));
    }

    public void Poll()
    {
        using var loan = _reader.Take();
        foreach (var s in loan)
        {
            if (!s.IsValid) continue;
            _onPulse(s.Data);
        }
    }

    public void Dispose() => _reader.Dispose();
}

/// <summary>
/// DDS ingress handler that forwards SwitchTimeModeWireDto samples to IosLogic.
/// </summary>
public sealed class TimeModeIngressHandler : IIngressHandler, IDisposable
{
    private readonly DdsReader<SwitchTimeModeWireDto>  _reader;
    private readonly Action<SwitchTimeModeWireDto>     _onMode;

    public TimeModeIngressHandler(DdsParticipant participant, Action<SwitchTimeModeWireDto> onMode)
    {
        _reader  = new DdsReader<SwitchTimeModeWireDto>(participant);
        _onMode  = onMode ?? throw new ArgumentNullException(nameof(onMode));
    }

    public void Poll()
    {
        using var loan = _reader.Take();
        foreach (var s in loan)
        {
            if (!s.IsValid) continue;
            _onMode(s.Data);
        }
    }

    public void Dispose() => _reader.Dispose();
}
```

You need the following additional `using` directives at the top of `DdsEventIngressHandlers.cs` if not already present:

```csharp
using Hrot.NED.Common;        // TimePulseDescriptor, SwitchTimeModeWireDto
```

(Check existing using directives; `Hrot.NED.Common` may already be imported.)

---

### Step 2 — Extend `IIosLogic` with Time API

File: `D:\Work\IOS-IG-SimHost-FDP-2\Hrot.ExCon\IIosLogic.cs`

Append a new region after the last existing member (before the closing `}`):

```csharp
    // ── Time state (observed from network) ───────────────────────────────────

    /// <summary>Current simulation time in seconds, received via TimePulseDescriptor.</summary>
    double MasterSimTime   { get; }

    /// <summary>Current wall-clock ticks (UTC), received via TimePulseDescriptor.</summary>
    long   MasterWallTicks { get; }

    /// <summary>Current time scale factor, received via TimePulseDescriptor.</summary>
    float  MasterTimeScale { get; }

    /// <summary>True when the simulation is paused (TimeMode = Paused).</summary>
    bool   IsPaused        { get; }

    // ── Time commands (dispatched to Orchestrator over DDS) ──────────────────

    /// <summary>Sends a PauseTime ClusterOpRequest to the Orchestrator.</summary>
    void RequestPause();

    /// <summary>Sends a ResumeTime ClusterOpRequest to the Orchestrator.</summary>
    void RequestResume();

    /// <summary>Sends a StepTime ClusterOpRequest to the Orchestrator.</summary>
    void RequestStep();

    /// <summary>Sends a SetTimeScale ClusterOpRequest with the given scale to the Orchestrator.</summary>
    void SetTimeScale(float scale);
```

---

### Step 3 — Implement Time API in `IosLogic`

File: `D:\Work\IOS-IG-SimHost-FDP-2\Hrot.ExCon\IosLogic.cs`

#### 3a. Add `_sysOpWriter` field (with other readonly fields):

```csharp
private readonly IDdsWriter<ClusterOpRequest>? _sysOpWriter;
```

#### 3b. Add time-state mutable fields (with other mutable state fields):

```csharp
// ── Time state ────────────────────────────────────────────────────────────────
public double MasterSimTime   { get; private set; }
public long   MasterWallTicks { get; private set; }
public float  MasterTimeScale { get; private set; } = 1f;
public bool   IsPaused        { get; private set; }
```

#### 3c. Extend the constructor

Add optional parameter at the end of the `IosLogic` constructor:

```csharp
IDdsWriter<ClusterOpRequest>? sysOpWriter = null)
```

And assign it in the body:
```csharp
_sysOpWriter = sysOpWriter;
```

The full updated parameter list tail becomes:
```csharp
        IDdsWriter<MapCommandRequest>?      commandWriter   = null,
        int                                 targetMapId     = IosLogicConstants.DefaultTargetMapId,
        IEventQueue<MapCommandAck>?         mapCommandAckQueue = null,
        IDdsWriter<Hrot.NED.Messages.DeleteEntityRequest>? deleteEntityWriter = null,
        IDdsWriter<ClusterOpRequest>?           sysOpWriter     = null)
```

#### 3d. Add time-state update methods (internal callbacks from ingress handlers):

Add these methods to `IosLogic` (near the bottom, before `Dispose`):

```csharp
/// <summary>Called by TimePulseIngressHandler each frame to update observed time state.</summary>
internal void OnTimePulse(TimePulseDescriptor pulse)
{
    MasterSimTime   = pulse.SimTimeSnapshot;
    MasterWallTicks = pulse.MasterWallTicks;
    MasterTimeScale = pulse.TimeScale;
}

/// <summary>Called by TimeModeIngressHandler to update IsPaused state.</summary>
internal void OnTimeMode(SwitchTimeModeWireDto dto)
{
    IsPaused = (TimeMode)dto.TargetModeInt == TimeMode.Paused;
}
```

Note: `TimeMode` enum is defined in `Hrot.NED.Common` or similar. Check the existing imports in `ClusterUiCache.cs` for the correct namespace.

#### 3e. Implement time command methods in `IosLogic`:

```csharp
public void RequestPause()   => _sysOpWriter?.Write(new ClusterOpRequest { OperationType = (int)ClusterOpType.PauseTime,  PayloadJson = "{}" });
public void RequestResume()  => _sysOpWriter?.Write(new ClusterOpRequest { OperationType = (int)ClusterOpType.ResumeTime, PayloadJson = "{}" });
public void RequestStep()    => _sysOpWriter?.Write(new ClusterOpRequest { OperationType = (int)ClusterOpType.StepTime,   PayloadJson = "{}" });
public void SetTimeScale(float scale) => _sysOpWriter?.Write(new ClusterOpRequest { OperationType = (int)ClusterOpType.SetTimeScale, PayloadJson = $"{{\"scale\":{scale}}}" });
```

The `using` for `ClusterOpType` / `ClusterOpRequest` / `ClusterOpType` — check where they are imported in existing Runner code. They live in `Hrot.Common.Orchestration` or `Hrot.NED.Common`. Check `OrchestratorSubsystem.cs` for the exact `using` statement.

---

### Step 4 — Wire IosSubsystem

File: `D:\Work\IOS-IG-SimHost-FDP-2\Hrot.ClusterRunner\Services\IosSubsystem.cs`

#### 4a. Add new fields (with existing private fields at top of class):

```csharp
private DdsWriter<ClusterOpRequest>?   _sysOpWriter;
private ClusterUiCache?            _uiCache;
private ClusterScenarioPanel?      _clusterPanel;
```

#### 4b. Add new `using` directives at top of `IosSubsystem.cs` if not already present:

```csharp
using Hrot.NED.Common;            // ClusterOpRequest (check existing usings first)
```

(Most DDS types are already imported via `Hrot.NED.Descriptors`, `Hrot.NED.Messages`, etc. — only add what is missing.)

#### 4c. In `Initialize()`, after `_participant` is created and before `IosMock` is constructed:

```csharp
// ── Cluster control wiring (S0507) ─────────────────────────────────────────
_sysOpWriter  = new DdsWriter<ClusterOpRequest>(_participant);
_uiCache      = new ClusterUiCache(_participant);
_clusterPanel = new ClusterScenarioPanel(_sysOpWriter, _uiCache);
```

Add these two ingress handlers to the `ingressHandlers` list (before the list is passed to `IosLogic`):

```csharp
new TimePulseIngressHandler(_participant, pulse => logic.OnTimePulse(pulse)),
new TimeModeIngressHandler(_participant,  mode  => logic.OnTimeMode(mode)),
```

**Important:** The handlers reference `logic` which is constructed before them. The `ingressHandlers` list is built up iteratively in the existing code — add these two at the end of the list construction, after the existing handlers, and **before** `logic` is passed to `IosMock`.

Wait — `logic` is constructed from the list and then handlers are added. Look at the actual initialization order in the existing code:

```
var ingressHandlers = new List<IIngressHandler> { ... MapClickIngressHandler, ... };
// [more handlers added via .Add()]
var logic = new IosLogic(..., ingressHandlers: ingressHandlers, ...);
_mock = new IosMock(logic, ...);
```

Since `TimePulseIngressHandler` and `TimeModeIngressHandler` need to call `logic.OnTimePulse()` and `logic.OnTimeMode()`, they **must** be constructed after `logic`. Add them after the `IosLogic` construction and before `IosMock` construction:

```csharp
var logic = new IosLogic(
    ...,
    ingressHandlers: ingressHandlers,
    ...,
    sysOpWriter: _sysOpWriter);

// S0507: Time ingress — must be constructed after `logic` to capture the callback
var timePulseHandler = new TimePulseIngressHandler(_participant, pulse => logic.OnTimePulse(pulse));
var timeModeHandler  = new TimeModeIngressHandler (_participant, mode  => logic.OnTimeMode(mode));
ingressHandlers.Add(timePulseHandler);
ingressHandlers.Add(timeModeHandler);
_ingressDisposables.Add(timePulseHandler);
_ingressDisposables.Add(timeModeHandler);
```

Wait — the existing pattern sets `_ingressDisposables` from a separate pass over `ingressHandlers`. Look at actual code carefully. The existing pattern in IosSubsystem is:

```csharp
_ingressDisposables = new List<IDisposable>(ingressHandlers.Count);
// ... loop over IDisposable items from ingressHandlers
```

You need to ensure the time handlers are in `ingressHandlers` before `_ingressDisposables` is built (or add separately). The safest approach: construct the time handlers after `logic`, add them to `ingressHandlers`, then proceed to the `_ingressDisposables` initialization. Only do this if `_ingressDisposables` building comes AFTER `logic` construction — inspect the actual existing code to confirm.

Actually, looking at the existing logic more carefully — each `IDisposable` handler is added to `_ingressDisposables` individually (e.g., `_ingressDisposables.Add(handler)`). If you cannot add before the `_ingressDisposables` pass, add the time handlers to `_ingressDisposables` separately after construction.

The implementation pattern must track what `_ingressDisposables` is built from. If the existing code does:
```csharp
_ingressDisposables = ingressHandlers.OfType<IDisposable>().ToList();
```
then simply add time handlers to `ingressHandlers` before that line. If it tracks them individually, add them manually.

Read the actual `IosSubsystem.Initialize` carefully around `_ingressDisposables` and apply accordingly.

Also wire the `sysOpWriter` into the `IosLogic` constructor call:
```csharp
var logic = new IosLogic(
    ...,
    sysOpWriter: _sysOpWriter);
```

#### 4d. In `DrawUI()`, after the `if (!_headless)` check, add the Cluster Control window:

Current `DrawUI()`:
```csharp
public void DrawUI()
{
    if (!_headless)
        _mock?.DrawUI();
}
```

Updated:
```csharp
public void DrawUI()
{
    if (_headless) return;

    _mock?.DrawUI();

    if (ImGui.Begin("Cluster Control"))
    {
        bool disableAll = _uiCache == null || !_uiCache.IsBootstrapped || _uiCache.HasInFlightTransaction;
        _clusterPanel?.Render(_uiCache!, disableAll);
    }
    ImGui.End();
}
```

Note: `ImGui.End()` must be called unconditionally even when `ImGui.Begin()` returns false.

#### 4e. In `Shutdown()`, before `_mock?.Dispose()`:

```csharp
_clusterPanel?.Dispose();
_clusterPanel = null;
_uiCache?.Dispose();
_uiCache = null;
_sysOpWriter?.Dispose();
_sysOpWriter = null;
```

---

### Step 5 — Tests

#### 5a. New file: `Hrot.ExCon.Tests/IosLogicTimeTests.cs`

Test the time-state API and command dispatch:

```csharp
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.NED.Common;
using Hrot.ExCon.Logic;
using Hrot.ExCon.Panels;
using Hrot.ExCon.Services;
using Hrot.Map.Common.Dds;
using FDP.Toolkit.DER;
using Moq;
using Xunit;

namespace Hrot.ExCon.Tests;

public class IosLogicTimeTests
{
    private static IosLogic MakeLogic(Mock<IDdsWriter<ClusterOpRequest>>? sysOpWriterMock = null)
    {
        return new IosLogic(
            repo:                   new DerRepo(),
            missionEditorService:   Mock.Of<IMissionEditorService>(),
            contextMenuLogic:       Mock.Of<IContextMenuLogic>(),
            transactionManager:     new RequestTransactionManager(),
            configWriter:           Mock.Of<IDdsWriter<MapInteractionConfig>>(),
            createEntityWriter:     Mock.Of<IDdsWriter<CreateEntityRequest>>(),
            clickQueue:             new ConcurrentEventQueue<MapClickEvent>(),
            selectionQueue:         new ConcurrentEventQueue<SelectionChangedEvent>(),
            interactionPanel:       new InteractionPanel(),
            createEntityAckQueue:   new ConcurrentEventQueue<CreateUpdateDeleteEntityAck>(),
            sysOpWriter:            sysOpWriterMock?.Object);
    }

    [Fact]
    public void OnTimePulse_UpdatesTimeProperties()
    {
        var logic = MakeLogic();
        var pulse = new TimePulseDescriptor
        {
            SimTimeSnapshot = 42.5,
            MasterWallTicks = 12345L,
            TimeScale       = 2.0f
        };

        logic.OnTimePulse(pulse);

        Assert.Equal(42.5,   logic.MasterSimTime,   precision: 5);
        Assert.Equal(12345L, logic.MasterWallTicks);
        Assert.Equal(2.0f,   logic.MasterTimeScale);
    }

    [Fact]
    public void OnTimeMode_Paused_SetsIsPausedTrue()
    {
        var logic = MakeLogic();
        var dto = new SwitchTimeModeWireDto { TargetModeInt = (int)TimeMode.Paused };

        logic.OnTimeMode(dto);

        Assert.True(logic.IsPaused);
    }

    [Fact]
    public void OnTimeMode_Running_SetsIsPausedFalse()
    {
        var logic = MakeLogic();
        // First pause it
        logic.OnTimeMode(new SwitchTimeModeWireDto { TargetModeInt = (int)TimeMode.Paused });
        // Then resume
        logic.OnTimeMode(new SwitchTimeModeWireDto { TargetModeInt = (int)TimeMode.Running });

        Assert.False(logic.IsPaused);
    }

    [Fact]
    public void RequestPause_WritesClusterOpRequest()
    {
        var writerMock = new Mock<IDdsWriter<ClusterOpRequest>>();
        var logic = MakeLogic(writerMock);

        logic.RequestPause();

        writerMock.Verify(w => w.Write(It.Is<ClusterOpRequest>(r =>
            r.OperationType == (int)ClusterOpType.PauseTime)), Times.Once);
    }

    [Fact]
    public void RequestResume_WritesClusterOpRequest()
    {
        var writerMock = new Mock<IDdsWriter<ClusterOpRequest>>();
        var logic = MakeLogic(writerMock);

        logic.RequestResume();

        writerMock.Verify(w => w.Write(It.Is<ClusterOpRequest>(r =>
            r.OperationType == (int)ClusterOpType.ResumeTime)), Times.Once);
    }

    [Fact]
    public void RequestStep_WritesClusterOpRequest()
    {
        var writerMock = new Mock<IDdsWriter<ClusterOpRequest>>();
        var logic = MakeLogic(writerMock);

        logic.RequestStep();

        writerMock.Verify(w => w.Write(It.Is<ClusterOpRequest>(r =>
            r.OperationType == (int)ClusterOpType.StepTime)), Times.Once);
    }

    [Fact]
    public void SetTimeScale_WritesClusterOpRequestWithPayload()
    {
        var writerMock = new Mock<IDdsWriter<ClusterOpRequest>>();
        var logic = MakeLogic(writerMock);

        logic.SetTimeScale(0.5f);

        writerMock.Verify(w => w.Write(It.Is<ClusterOpRequest>(r =>
            r.OperationType == (int)ClusterOpType.SetTimeScale &&
            r.PayloadJson.Contains("0.5"))), Times.Once);
    }

    [Fact]
    public void TimeCommands_WithNoWriter_DoNotThrow()
    {
        var logic = MakeLogic(sysOpWriterMock: null);

        // Should silently no-op
        logic.RequestPause();
        logic.RequestResume();
        logic.RequestStep();
        logic.SetTimeScale(1.0f);
    }
}
```

**Adapt the `MakeLogic` factory pattern** to match the existing `IosLogicTests` factory in the same project — use the same mocking approach, e.g.:

```csharp
// Check IosLogicTests.cs for the exact factory helper pattern and replicate it.
// Key: pass sysOpWriter as the new optional parameter.
```

#### 5b. New test in `Hrot.ClusterRunner.Tests/ClusterScenarioPanelTests.cs` (append)

Or create a new file `Hrot.ClusterRunner.Tests/IosSubsystemClusterTests.cs`:

```csharp
[Fact]
public void IosSubsystem_HasNoDirectClusterMasterReference()
{
    // Static analysis guard: IosSubsystem must not import Hrot.Orchestrator namespace.
    var source = File.ReadAllText(
        Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "..",
            "Hrot.ClusterRunner", "Services", "IosSubsystem.cs"));

    Assert.DoesNotContain("Hrot.Orchestrator", source);
    Assert.DoesNotContain("ClusterMaster",         source);
}
```

Alternatively, put this as a comment-test or a simple Fact in existing test infrastructure.

---

## Build & Test Commands

Run these to validate (in order):

```powershell
# 1. Build the whole solution
dotnet build "D:\Work\IOS-IG-SimHost-FDP-2\IOS-IG-SimHost.sln" -c Debug

# 2. Run IOS unit tests (baseline: 340; target: ~347+)
dotnet test "D:\Work\IOS-IG-SimHost-FDP-2\Hrot.ExCon.Tests\Hrot.ExCon.Tests.csproj" -c Debug --no-build --verbosity quiet

# 3. Run Runner unit tests (baseline: 177; may change due to P3 cleanup)
dotnet test "D:\Work\IOS-IG-SimHost-FDP-2\Hrot.ClusterRunner.Tests\Hrot.ClusterRunner.Tests.csproj" -c Debug --no-build --verbosity quiet

# 4. Run full suite
dotnet test "D:\Work\IOS-IG-SimHost-FDP-2\IOS-IG-SimHost.sln" -c Debug --no-build --verbosity quiet
```

---

## Acceptance Criteria

1. `Hrot.ExCon/Services/DdsEventIngressHandlers.cs` contains `TimePulseIngressHandler` and `TimeModeIngressHandler`.
2. `IIosLogic` declares: `MasterSimTime`, `MasterWallTicks`, `MasterTimeScale`, `IsPaused`, `RequestPause()`, `RequestResume()`, `RequestStep()`, `SetTimeScale(float)`.
3. `IosLogic` implements all new interface members; `_sysOpWriter` is injected via optional constructor parameter.
4. `IosSubsystem` has fields `_sysOpWriter`, `_uiCache`, `_clusterPanel`; all three initialized in `Initialize()` and disposed in `Shutdown()`.
5. `IosSubsystem.DrawUI()` renders an ImGui "Cluster Control" window via `_clusterPanel.Render(...)`.
6. `grep -r "Hrot.Orchestrator" Hrot.ClusterRunner/Services/IosSubsystem.cs` returns **no matches**.
7. `OrchestratorScenarioPanel.cs` and `OrchestratorScenarioPanelTests.cs` are **deleted**.
8. Dead `_drillTime` field removed from `OrchestratorSubsystem.cs`.
9. All tests pass; new IosLogicTimeTests (≥7 tests) pass; total test count ≥ 631.

---

## Files to Create / Modify / Delete

| Action | File |
|--------|------|
| Modify | `Hrot.ExCon/Services/DdsEventIngressHandlers.cs` — append two new ingress handler classes |
| Modify | `Hrot.ExCon/IIosLogic.cs` — add time state + command interface members |
| Modify | `Hrot.ExCon/IosLogic.cs` — implement time state, commands, OnTimePulse/OnTimeMode, sysOpWriter ctor param |
| Modify | `Hrot.ClusterRunner/Services/IosSubsystem.cs` — wire ClusterUiCache, ClusterScenarioPanel, time handlers, DrawUI window |
| Modify | `Hrot.ClusterRunner/Services/OrchestratorSubsystem.cs` — remove dead `_drillTime` field |
| **Create** | `Hrot.ExCon.Tests/IosLogicTimeTests.cs` — new time API tests |
| **Delete** | `Hrot.ClusterRunner/Services/OrchestratorScenarioPanel.cs` |
| **Delete** | `Hrot.ClusterRunner.Tests/OrchestratorScenarioPanelTests.cs` |

---

## Notes and Gotchas

1. **`TimeMode` enum namespace**: The `TimeMode` enum is used in `ClusterUiCache.cs`. Check its `using` directives for the exact namespace — likely `Hrot.NED.Common` or a nested namespace within it.

2. **`ClusterOpRequest`/`ClusterOpType` namespaces**: These are used in `OrchestratorSubsystem.cs`. Check its `using` directives for the correct namespace — likely `Hrot.Common.Orchestration` or `Hrot.NED.Common`.

3. **IosMock interface**: `IosMock` wraps `IIosLogic`. If `IosMock` holds a concrete `IosLogic` reference internally (not `IIosLogic`), it already exposes the new methods without changes. If `IosMock` delegates through `IIosLogic`, ensure `IosMock` forwards all new interface members.

4. **IosLogic `OnTimePulse`/`OnTimeMode` visibility**: These are `internal` callbacks. Since `IosSubsystem` is in `Hrot.ClusterRunner` and `IosLogic` is in `Hrot.ExCon`, these methods need to be `public` (not `internal`) so `IosSubsystem` can reference them. Unless there's an `InternalsVisibleTo` attribute — check `Hrot.ExCon.csproj` for any such assembly attribute before deciding. If in doubt, make them `public`.

5. **`TimePulseDescriptor.SimTimeSnapshot` vs. other field names**: Verified in BATCH-29 analysis. `SimTimeSnapshot` is the simulation time field, `MasterWallTicks` is the wall-clock field, `TimeScale` is the time scale. Double-check field names in `Hrot.NED` `TimePulseDescriptor` definition before implementing.

6. **`_ingressDisposables` construction order**: Inspect the exact existing code in `IosSubsystem.Initialize()`:
   - If `_ingressDisposables` is built with `new List<IDisposable>(ingressHandlers.OfType<IDisposable>())` style — add time handlers to `ingressHandlers` BEFORE that line.
   - If each disposable handler is explicitly `.Add()`-ed to `_ingressDisposables` in a loop — add them after `logic` construction via explicit `.Add()`.

7. **`ClusterScenarioPanel.Dispose()`**: Check if `ClusterScenarioPanel` implements `IDisposable`. If yes, call `_clusterPanel?.Dispose()` in `Shutdown()`. If not, skip that call.

8. **`ClusterUiCache.IsBootstrapped` and `HasInFlightTransaction`**: These are already in `ClusterUiCache` from BATCH-29. The `disableAll` logic in `DrawUI()` follows the same pattern as `OrchestratorSubsystem.DrawUI()`.
