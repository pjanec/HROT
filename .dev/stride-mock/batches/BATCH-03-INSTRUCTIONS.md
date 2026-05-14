# BATCH-03: StrideMockSubsystem + ClusterRunner Integration

**Batch Number:** BATCH-03
**Tasks:** SM-006 (StrideMockSubsystem), SM-007 (ClusterRunner Wiring)
**Phase:** Phase 4
**Estimated Effort:** 10-14 hours
**Priority:** HIGH
**Dependencies:** BATCH-02 must be complete (StrideNodeBootstrapper + SyncFdpToStrideScript exist)

---

## Mandatory Workflow — Complete Without Stopping

**YOU MUST FINISH BOTH TASKS COMPLETELY BEFORE WRITING THE REPORT.**
Do not stop to ask permission. Do not stop after SM-006 to check if it is correct.
Fix all build errors and test failures yourself before submitting. No partial work.

**Mandatory task-progression (Test-Driven):**

1. **SM-006:** Implement `StrideMockSubsystem` -> Write tests -> **ALL tests pass** ✅
2. **SM-007:** Modify ClusterRunner wiring -> Write tests -> **ALL tests pass** ✅

Do NOT move to the next task until the current one compiles and all its tests pass.

---

## Onboarding & Workflow

### Required Reading (IN ORDER)
1. `.dev/stride-mock/DESIGN.md` — §7 (StrideMockSubsystem), §9 (ClusterRunner Integration)
2. `.dev/stride-mock/TASK-DETAILS.md` — SM-006, SM-007 sections (all success conditions)
3. `.dev/stride-mock/reviews/BATCH-02-REVIEW.md` — context on what was built last batch
4. `Hrot\Subsystems\Hrot.SimHost\SimHostSubsystem.cs` — reference pattern for the adapter

### Source Code Locations
- **New file:** `Hrot\Subsystems\Hrot.StrideMock\StrideMockSubsystem.cs`
- **Core bootstrapper (already built):** `Hrot\Subsystems\Hrot.StrideMock\StrideNodeBootstrapper.cs`
- **Sync script (already built):** `Hrot\Subsystems\Hrot.StrideMock\SyncFdpToStrideScript.cs`
- **Reference pattern:** `Hrot\Subsystems\Hrot.SimHost\SimHostSubsystem.cs`
- **Configuration to modify:** `Hrot\Runner\Hrot.ClusterRunner\Configuration\HrotRunnerConfiguration.cs`
- **Program to modify:** `Hrot\Runner\Hrot.ClusterRunner\Program.cs`
- **Test project for SM-006:** `Hrot\Subsystems\Hrot.StrideMock\Hrot.StrideMock.Tests\` (add new file)
- **Test project for SM-007:** `Hrot\Runner\Hrot.ClusterRunner.Tests\` (add new file, or add to `Configuration\RunModeTests.cs`)
- **Existing tests for SM-007 pattern:** `Hrot\Runner\Hrot.ClusterRunner.Tests\Configuration\RunModeTests.cs`

### Build Commands
```
dotnet build Hrot\Subsystems\Hrot.StrideMock\Hrot.StrideMock.csproj
dotnet test Hrot\Subsystems\Hrot.StrideMock\Hrot.StrideMock.Tests\Hrot.StrideMock.Tests.csproj
dotnet build Hrot\Runner\Hrot.ClusterRunner\Hrot.ClusterRunner.csproj
dotnet test Hrot\Runner\Hrot.ClusterRunner.Tests\Hrot.ClusterRunner.Tests.csproj
```

### Report Submission
**When done, submit your report to:**
`.dev/stride-mock/reports/BATCH-03-REPORT.md`

**If you have questions, create:**
`.dev/stride-mock/questions/BATCH-03-QUESTIONS.md`

---

## Context

BATCH-02 built the core engine-agnostic library (`StrideNodeBootstrapper`, `SyncFdpToStrideScript`,
visual effects). BATCH-03 wraps that library in two thin adapters:

1. **StrideMockSubsystem** — the `ISubsystem` + `IMapCameraProvider` adapter that plugs into
   `SubsystemOrchestrator`. Pattern identical to `SimHostSubsystem` (thin adapter over core app).
2. **ClusterRunner wiring** — adds `"stridemock"` to the CLI whitelist and assigns NodeId offset 700.

**Design Key Principle:** `StrideMockSubsystem` must be a thin adapter. All simulation logic stays in
`StrideNodeBootstrapper` and `SyncFdpToStrideScript`. The subsystem only owns lifecycle delegation
and the rendering calls.

---

## Task 1: SM-006 — Implement StrideMockSubsystem

**Task Definition:** See [TASK-DETAILS.md](../TASK-DETAILS.md#sm-006--implement-stridemocksubsystem)
**Design Reference:** [DESIGN.md §7](../DESIGN.md#7-stridemocksubsystem-hrotstrideMock)

### File: `Hrot\Subsystems\Hrot.StrideMock\StrideMockSubsystem.cs` (NEW)

Implement `StrideMockSubsystem` as a `sealed class` implementing `ISubsystem` and `IMapCameraProvider`.
Pattern is identical to `SimHostSubsystem` — a thin adapter that delegates everything to the core.

**Required using references** (check SimHostSubsystem.cs for the exact namespace pattern):
```csharp
using Fdp.Toolkit.Runner;
using Hrot.Common;
using Hrot.Core.Network;
using Hrot.Map.Common;
// ... plus any rendering imports for Raylib/ImGui
```

**Constructor (required by TryCreateSubsystem reflection):**
```csharp
public StrideMockSubsystem(INetworkFactory networkFactory)
```

The subsystem must store the factory and pass it to `StrideNodeBootstrapper.BootstrapNode()` during
`Initialize(SubsystemConfig config)`.

**Identity properties:**
```csharp
public string Name => "StrideMock";
public System.Numerics.Vector4 TitleBarColor => new(0.8f, 0.4f, 0.1f, 1f);  // orange
```

**Initialize(SubsystemConfig config):**
Read DESIGN.md §7.3 carefully — TKB population order is critical:
1. Build `HrotNodeConfig` from `config` (DomainId, NodeId, Headless, LocalTempRoot isolated path)
2. Create `StrideNodeBootstrapper` with no modules (null — Stage 1 uses defaults internally or just no modules)
3. Call `_core.BootstrapNode(nodeConfig, StrideNodeBootstrapper.Role, _networkFactory)`
4. **After** BootstrapNode: extract `_core.Context.TkbDb` and call:
   ```csharp
   DemoTkbSetup.RegisterAll(tkb);
   Fdp.Examples.Scenarios.UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates(tkb);
   ```
5. Create `_script = new SyncFdpToStrideScript(_core)` and call `_script.Start()`

**Find `DemoTkbSetup`** — grep the codebase for it. It is likely in
`Hrot\Subsystems\Hrot.SimHost\` or `FDP\Examples\`. Check `SimHostSubsystem.cs` or `SimHostApp.cs`
for the exact reference pattern and namespace.

**Find `UrbanCombatNewScenario`** — grep for it. It is in `FDP\Examples\` or `Hrot\` examples.

**Update(float deltaTime):**
```csharp
public void Update(float deltaTime)
{
    if (IsActiveMapOwner())
        _core.Camera.HandleInput(_inputProvider);
    _core.Camera.Update(deltaTime);
    _script.Update(deltaTime);
    _core.Tick(deltaTime);
}
```

`IsActiveMapOwner()` — check `SubsystemConfig` or the `ISubsystem` interface for how SimHostSubsystem
checks tab ownership. Look at `SimHostSubsystem.Update()` for the exact pattern.

**DrawWorld():** See DESIGN.md §7.6. Follow the same pattern as `SimHostApp.OnDrawWorld()`:
1. `_core.Camera.BeginMode()`
2. Draw consumer buffer via `DebugPrimitiveRenderer2D` (or equivalent)
3. Draw fake entities as circles (radius 5, red)
4. Draw effects: orange expanding circles for Explosion, yellow lines for Tracers
5. `_core.Camera.EndMode()`

**DrawUI():** Show splash overlay when `_script.CurrentStateMessage` is non-empty.

**Shutdown():** Call `_core.Dispose()`.

**IMapCameraProvider:**
```csharp
public MapCameraView? GetCameraView() => _core.Camera.GetCameraView();
public void ApplyCameraView(MapCameraView view) => _core.Camera.ApplyCameraView(view);
```

### Tests: `Hrot\Subsystems\Hrot.StrideMock\Hrot.StrideMock.Tests\StrideMockSubsystemTests.cs` (NEW)

Write tests covering all SC_SM006_x success conditions. Use `OfflineNetworkFactory` for headless
testing (same pattern as `StrideNodeBootstrapperTests.cs`).

Use `SubsystemConfig` with `Headless = true`. Check SimHostSubsystem tests at
`Hrot\Runner\Hrot.ClusterRunner.Tests\SimHostSubsystemTests.cs` for the exact headless config pattern.

**Required tests:**

- **SC_SM006_1:** `Name == "StrideMock"`
  ```csharp
  Assert.Equal("StrideMock", new StrideMockSubsystem(new OfflineNetworkFactory()).Name);
  ```

- **SC_SM006_2:** `TitleBarColor` is orange (0.8f, 0.4f, 0.1f, 1f)
  ```csharp
  Assert.Equal(new Vector4(0.8f, 0.4f, 0.1f, 1f), subsystem.TitleBarColor);
  ```

- **SC_SM006_3:** Constructor with null factory must throw `ArgumentNullException`
  ```csharp
  Assert.Throws<ArgumentNullException>(() => new StrideMockSubsystem(null!));
  ```

- **SC_SM006_3 (Initialize):** `Initialize(config)` calls `BootstrapNode` and does not throw.
  Create a headless config, call `Initialize`, verify no exception.

- **SC_SM006_4:** `GetCameraView()` returns non-null after `Initialize`

- **SC_SM006_5:** `ApplyCameraView(view)` changes camera Target and Zoom:
  ```csharp
  var view = new MapCameraView { Target = new Vector2(100f, 200f), Zoom = 2.5f };
  subsystem.ApplyCameraView(view);
  Assert.Equal(100f, subsystem.GetCameraView()!.Value.Target.X, precision: 1);
  Assert.Equal(2.5f, subsystem.GetCameraView()!.Value.Zoom, precision: 2);
  ```

- **SC_SM006_6:** After Initialize, `Update(0.016f)` does not throw (headless, no Raylib input)

- **SC_SM006_8:** When `_script.CurrentStateMessage` is non-empty (loading state), `DrawUI()` does
  not throw. (Visual verification only — just assert no exception in headless mode)

- **SC_SM006_9:** `Shutdown()` calls `Dispose()` without throwing:
  ```csharp
  subsystem.Initialize(config);
  subsystem.Shutdown(); // must not throw
  ```

**Notes on testing DrawWorld/DrawUI:** These methods call Raylib rendering APIs. In headless tests
(no window), wrap them in try/catch or check if there is a headless mode that skips rendering.
Look at how `SimHostSubsystemTests.cs` handles this — some tests skip rendering calls entirely.

---

## Task 2: SM-007 — Wire StrideMockSubsystem into ClusterRunner

**Task Definition:** See [TASK-DETAILS.md](../TASK-DETAILS.md#sm-007--wire-stridemocksubsystem-into-clusterrunner)
**Design Reference:** [DESIGN.md §9](../DESIGN.md#9-clusterrunner-integration)

### File 1: `Hrot\Runner\Hrot.ClusterRunner\Configuration\HrotRunnerConfiguration.cs` (MODIFY)

In the `Validate()` method, locate the `validNames` HashSet:
```csharp
var validNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "simhost", "ig", "excon", "orchestrator", "cgf", "ci", "editor" };
```
Add `"stridemock"`:
```csharp
var validNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "simhost", "ig", "excon", "orchestrator", "cgf", "ci", "editor", "stridemock" };
```

**Also check the `WaitForPeers` validation block** — ensure "stridemock" is NOT added to the
`validPeers` set (StrideMock does not participate in the waiting-room peer protocol; it uses
`--no-wait` mode like the orchestrator).

**Also check the `isAll` / `isOrchestratorOnly` logic** — the "all" expansion must NOT include
stridemock (it is not part of the default cluster). No changes needed there.

### File 2: `Hrot\Runner\Hrot.ClusterRunner\Program.cs` (MODIFY)

In `ResolveAppNodeId()`, add the STRIDEMOCK case before the default:
```csharp
"STRIDEMOCK" => 700,
_            => 600,
```

**Also add "StrideMock" to the `perspectiveMap`** in the main run loop if it needs a camera
perspective entry. Check the existing map — add `["StrideMock"] = "StrideMock"` only if
`StrideMockSubsystem` implements `IMapCameraProvider` (it does).

### File 3: `Hrot\Runner\Hrot.ClusterRunner\Hrot.ClusterRunner.csproj` (VERIFY)

Check that `Hrot.StrideMock` is already listed as a `<ProjectReference>` (it was added in
SM-001 / BATCH-01). If missing, add it:
```xml
<ProjectReference Include="..\..\..\Subsystems\Hrot.StrideMock\Hrot.StrideMock.csproj" />
```

### Tests: Add to `Hrot\Runner\Hrot.ClusterRunner.Tests\Configuration\RunModeTests.cs` (MODIFY)

Add the following tests covering SC_SM007_1 through SC_SM007_4:

```csharp
[Fact]
public void Validate_StrideMockMode_DoesNotThrow()
{
    // SC_SM007_1
    var cfg = new HrotRunnerConfiguration { ModeString = "stridemock", NoWait = true };
    cfg.Validate(); // must not throw
    Assert.Contains("stridemock", cfg.RequestedSubsystems);
}

[Fact]
public void Validate_OrchestratorCgfStrideMock_DoesNotThrow()
{
    // SC_SM007_2
    var cfg = new HrotRunnerConfiguration { ModeString = "orchestrator,cgf,stridemock", NoWait = true };
    cfg.Validate(); // must not throw
    Assert.Contains("stridemock", cfg.RequestedSubsystems);
    Assert.Contains("orchestrator", cfg.RequestedSubsystems);
    Assert.Contains("cgf", cfg.RequestedSubsystems);
}

[Fact]
public void Validate_ExistingModes_StillParseWithoutError()
{
    // SC_SM007_3 — no regression
    foreach (var mode in new[] { "simhost", "ig", "excon", "orchestrator", "cgf" })
    {
        var cfg = new HrotRunnerConfiguration { ModeString = mode, NoWait = true };
        cfg.Validate(); // must not throw
    }
}
```

For SC_SM007_4 (`ResolveAppNodeId` returns 700), this method is `private static` in `Program.cs`.
Test it via `SC_SM007_5` indirectly, or use reflection if needed. Alternatively, accept that this
is verified by SC_SM007_6/SC_SM007_7 integration tests. Add a comment in the test file noting this.

For SC_SM007_5 (`ScanForSubsystems` contains `StrideMockSubsystem`), this is a runtime reflection
test. Add it to `Hrot\Runner\Hrot.ClusterRunner.Tests\ISubsystemTests.cs` or create a new file:

```csharp
[Fact]
public void ScanForSubsystems_ContainsStrideMockSubsystem()
{
    // SC_SM007_5: After the assembly is referenced, reflection discovers StrideMockSubsystem
    // Verify the type exists and is assignable to ISubsystem
    var type = typeof(Hrot.StrideMock.StrideMockSubsystem);
    Assert.True(typeof(Fdp.Toolkit.Runner.ISubsystem).IsAssignableFrom(type));
    Assert.False(type.IsAbstract);
}
```

---

## Quality Standards

**❗ TEST QUALITY EXPECTATIONS**
- **NOT ACCEPTABLE:** Tests that only verify "does not throw" for complex behaviors
- **REQUIRED:** Tests that verify actual property values, state transitions, and delegate calls
- SC_SM006_5 (camera sync) must verify actual coordinate values, not just "no exception"
- SC_SM007_1-3 must verify `RequestedSubsystems` contains the expected values (not just no-throw)

**❗ DO NOT** add `StrideMock` to the "all"/"demo" expansion — it is not part of the default cluster.

**❗ DO NOT** modify `SimHostSubsystem` or any other existing subsystem — this batch is additive only.

---

## Success Criteria

This batch is DONE when:
- [ ] `StrideMockSubsystem` compiles and all SC_SM006_x tests pass
- [ ] `HrotRunnerConfiguration.Validate()` accepts "stridemock" and "orchestrator,cgf,stridemock"
- [ ] `ResolveAppNodeId("StrideMock", 0)` returns 700
- [ ] `ScanForSubsystems()` type-check test passes
- [ ] All existing `Hrot.ClusterRunner.Tests` still pass (no regression)
- [ ] All existing `Hrot.StrideMock.Tests` still pass (no regression)
- [ ] Report submitted

---

## Report Requirements

`.dev/stride-mock/reports/BATCH-03-REPORT.md`

**Required sections:**
1. Summary of files created/modified
2. Test results table for SM-006 (SC_SM006_x)
3. Test results table for SM-007 (SC_SM007_x)
4. Total test counts (new + existing all passing)
5. Issues encountered and how resolved
6. Design decisions made beyond the spec
7. Any edge cases discovered
8. Suggested commit message

---

## Reference Materials
- **Task Defs:** [TASK-DETAILS.md](../TASK-DETAILS.md) — SM-006, SM-007
- **Design:** [DESIGN.md](../DESIGN.md) — §7, §9
- **Reference pattern:** `Hrot\Subsystems\Hrot.SimHost\SimHostSubsystem.cs`
- **Config tests pattern:** `Hrot\Runner\Hrot.ClusterRunner.Tests\Configuration\RunModeTests.cs`
