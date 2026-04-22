# CGF-1-BATCH-27: Time Control UI + Asset Combo Selection

**Batch Number:** BATCH-27  
**Tasks:** P3-Debt (ReplaySeek fan-out test), CGF1-S0503, CGF1-S0504  
**Phase:** Phase 5 — Operational UI / CQRS Architecture  
**Estimated Effort:** 8–12 hours  
**Priority:** HIGH  
**Dependencies:** CGF-1-BATCH-26 (S0501 + S0502 complete)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch finishes the Orchestrator ImGui control panel with interactive time controls
and filesystem-backed combo-box selection for scenarios, drills, and stories.  It also
closes a P3 test gap carried over from BATCH-26.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.github/skills/developer/SKILL.md`
2. **Task Definitions:** `.dev/cgf-1/CGF-1-TASK-DETAIL.md` — §CGF1-S0503, §CGF1-S0504
3. **Design Document:** `.dev/cgf-1/CGF-1-ADDENDUM-3.md` — §4 Time Control Section, §5 Asset Combo Selection
4. **Previous Review:** `.dev/cgf-1/reviews/CGF-1-BATCH-26-REVIEW.md` — context, approved state, P3 debt note
5. **Debt Tracker:** `.dev/DEBT-TRACKER.md` — row tagged CGF-1-BATCH-26 (ReplaySeek test gap)

### Source Code Locations

| Area | Path |
|---|---|
| DDS schema (ClusterOpType) | `Hrot.NED/Orchestration/OrchestrationMessages.cs` |
| ClusterMaster | `Hrot.Orchestrator/ClusterMaster.cs` |
| OrchestratorSubsystem | `Hrot.ClusterRunner/Services/OrchestratorSubsystem.cs` |
| OrchestratorScenarioPanel | `Hrot.ClusterRunner/Services/OrchestratorScenarioPanel.cs` |
| Orchestrator unit tests | `Hrot.Orchestrator.Tests/` |
| Runner unit tests | `Hrot.ClusterRunner.Tests/` |

### Test Commands

```powershell
dotnet test Hrot.Orchestrator.Tests/Hrot.Orchestrator.Tests.csproj -c Debug --logger "console;verbosity=quiet"
dotnet test Hrot.ClusterRunner.Tests/Hrot.ClusterRunner.Tests.csproj -c Debug --logger "console;verbosity=quiet"
dotnet test Hrot.NED.Tests/Hrot.NED.Tests.csproj -c Debug --logger "console;verbosity=quiet"
```

**Baseline (must still pass after your changes):**
- `Hrot.Orchestrator.Tests` → 46 passing
- `Hrot.ClusterRunner.Tests` → 148 passing

### Report Submission

**When done, submit your report to:**  
`.dev/cgf-1/reports/CGF-1-BATCH-27-REPORT.md`

**If you have questions, create:**  
`.dev/cgf-1/questions/CGF-1-BATCH-27-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement → Write tests → **ALL tests pass** ✅  
3. **Task 3:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

**Why:** Ensures each component is solid before building on top of it. Prevents cascading failures.

---

## Context

BATCH-26 completed S0501 (ImGui overhaul) and S0502 (real DDS fan-out).  The Orchestrator
panel now shows a beige window wrapper, 2PC history, status banner, and all buttons publish
live `ClusterOpRequest` messages on the network.

Two gaps remain in Phase 5:
- **S0503** — The "Time Control" sub-panel and `TimeControlRequested` event are not
  yet implemented, so Pause/Resume/Step/Speed have no handler path through
  `DistributedTimeCoordinator`.
- **S0504** — Scenario, drill, and story IDs are still free-text `InputText` fields;
  they must become `Combo` boxes populated from a local filesystem scan.

Plus a P3 debt carried from BATCH-26: the `ReplaySeek` fan-out code path has no
dedicated test covering `NodeReplaySeek` fan-out.

---

## ✅ Tasks

---

### Task 0 (P3 Debt): `ReplaySeekStep_FansOutNodeReplaySeek`

**File:** `Hrot.Orchestrator.Tests/ClusterMasterFanOutTests.cs` (UPDATE)  
**Debt ref:** DEBT-TRACKER.md row — CGF-1-BATCH-26 review

**Description:** Add a single `[Fact]` named `ReplaySeekStep_FansOutNodeReplaySeek` that
verifies the `OperationStep(ReplaySeek)` branch in `ClusterMaster.ProcessSingleClusterOpRequest`
fans out a `NodeOpType.NodeReplaySeek` command to all active nodes.

**Pattern to follow:** Study the existing fan-out facts in `ClusterMasterFanOutTests.cs`.
The test should:
1. Register one node with a Standby heartbeat (call `RegisterNode`).
2. Ensure `_bootstrapLatch` is set (use `BootstrapForTests` or the existing `NoMandatoryConfig`
   pattern so `BootstrapComplete == true`).
3. First transition the cluster to `RunningReplay` (required for a `ReplaySeek`
   `OperationStep` to appear in the trajectory — use a `ClusterOpType.TransitionState` request
   targeting `ClusterState.RunningReplay`).
4. After that, write a `ClusterOpRequest { OperationType = ClusterOpType.ReplaySeek,
   PayloadJson = "{\"TargetWallTicks\":1000}" }` and call `drill.Tick()`.
5. Assert a `NodeOpCommand` with `Operation == NodeOpType.NodeReplaySeek` was received
   by the test DDS reader within 1 s.

**Important:** `ReplaySeek` is a standalone `ClusterOpType` that maps directly to a
`NodeReplaySeek` fan-out — it does not go through the `TransitionState` call stack.
Look at `ClusterMaster.ProcessSingleClusterOpRequest` to find the handler for
`ClusterOpType.ReplaySeek` and write a test that exercises it.

**Tests required:**
- `ReplaySeekStep_FansOutNodeReplaySeek` — 1 new fact in `ClusterMasterFanOutTests`

---

### Task 1 (S0503-A): Extend `ClusterOpType` enum

**File:** `Hrot.NED/Orchestration/OrchestrationMessages.cs` (UPDATE)  
**Task Definition:** [CGF1-S0503](../CGF-1-TASK-DETAIL.md#cgf1-s0503--time-control-section--remote-time-commands), item 1

**Description:** Add three new values after `PrefetchScenario = 12`:

```csharp
CancelOperation = 13,
StepTime        = 14,
SetTimeScale    = 15,
```

> **Wire-value note:** `NodeOpType.NodeReplaySeek = 13` is on a separate enum; the
> overlap is intentional and defined in the IDL. `CancelOperation = 13` sits on
> `ClusterOpType` — no conflict.

**Tests required:**
- Add two `[Fact]` items to `Hrot.NED.Tests/OrchestrationSchemaTests.cs`:
  - `ClusterOpType_StepTime_Is14` — `Assert.Equal(14, (int)ClusterOpType.StepTime)`
  - `ClusterOpType_SetTimeScale_Is15` — `Assert.Equal(15, (int)ClusterOpType.SetTimeScale)`

---

### Task 2 (S0503-B): `TimeControlRequested` event in `ClusterMaster`

**File:** `Hrot.Orchestrator/ClusterMaster.cs` (UPDATE)  
**Task Definition:** [CGF1-S0503](../CGF-1-TASK-DETAIL.md#cgf1-s0503--time-control-section--remote-time-commands), item 2  
**Design ref:** [§4.2](../CGF-1-ADDENDUM-3.md#42-clustmastertimecontrolrequested-event)

**Description:** Add the event and intercept in `ProcessSingleClusterOpRequest`:

```csharp
/// <summary>
/// Raised for time-control operations (Pause/Resume/Step/SetTimeScale) that do
/// not require 2PC across simulation nodes.  <see cref="OrchestratorSubsystem"/>
/// subscribes to route these to <see cref="DistributedTimeCoordinator"/>.
/// </summary>
public event Action<ClusterOpType, string>? TimeControlRequested;
```

At the **very start** of `ProcessSingleClusterOpRequest`, before the main dispatch switch:

```csharp
if (req.OperationType is ClusterOpType.PauseTime or ClusterOpType.ResumeTime
                      or ClusterOpType.StepTime  or ClusterOpType.SetTimeScale)
{
    TimeControlRequested?.Invoke(req.OperationType, req.PayloadJson ?? string.Empty);
    return;
}
```

**Tests required:**  
Add two `[Fact]` items to `Hrot.Orchestrator.Tests/` (new file
`ClusterMasterTimeControlTests.cs` or appended to an appropriate existing test file):

- `TimeControlRequested_FiresOnPauseTime` — call `HandleClusterOpRequest` with
  `OperationType = ClusterOpType.PauseTime`; assert the event was raised exactly once
  with `ClusterOpType.PauseTime`.
- `TimeControlRequested_BypassesTransactionHistory` — same call; assert
  `_drillMaster.TransactionHistory` is empty (no 2PC transaction was created).

---

### Task 3 (S0503-C): Time Control UI section in `OrchestratorSubsystem`

**File:** `Hrot.ClusterRunner/Services/OrchestratorSubsystem.cs` (UPDATE)  
**Task Definition:** [CGF1-S0503](../CGF-1-TASK-DETAIL.md#cgf1-s0503--time-control-section--remote-time-commands), items 3–4, 7  
**Design ref:** [§4.2–§4.3](../CGF-1-ADDENDUM-3.md#42-clustmastertimecontrolrequested-event)

**Description (5 sub-changes):**

**3a. Add `_isPaused` tracking field:**

```csharp
private bool _isPaused;   // S0503: toggled by TimeControlRequested handler
```

**3b. In `Initialize`, subscribe to `TimeControlRequested` after `_drillMaster` is created:**

```csharp
_drillMaster.TimeControlRequested += (op, payload) =>
{
    switch (op)
    {
        case ClusterOpType.PauseTime:
            var ids = new HashSet<int>(_drillMaster.NodeRoster.ActiveNodes.Keys);
            _timeCoordinator?.SwitchToDeterministic(ids);
            _isPaused = true;
            break;
        case ClusterOpType.ResumeTime:
            _timeCoordinator?.SwitchToContinuous();
            _isPaused = false;
            break;
        case ClusterOpType.StepTime:
            _timeKernel?.StepFrame(1f / 60f);
            break;
        case ClusterOpType.SetTimeScale:
            if (float.TryParse(payload,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out float s))
                _timeKernel?.GetTimeController()?.SetTimeScale(s);
            break;
    }
};
```

> `ModuleHostKernel.StepFrame(float dt)` advances the kernel by exactly one fixed
> delta without consuming real-wall-clock time.  If the method does not exist, add it
> to `ModuleHostKernel` (or call `_timeKernel.Update()` as a fallback and document
> the deviation).

**3c. In `Update`, pass `deltaTime` and `isPaused`/drillTime to the panel:**

```csharp
float drillTime = (float)(_timeKernel?.GetTimeController()?.CurrentTime.TotalSeconds ?? 0.0);
_scenarioPanel?.Update(deltaTime);
```

Update `_scenarioPanel.Render()` call (in `DrawUI`) to pass the new parameters:

```csharp
_scenarioPanel?.Render(_isPaused, drillTime);
```

**3d. Remove the inline Pause / Resume buttons** from the `"Simulation controls"` block in
`DrawUI()`.  Those buttons will live in the new "Time Control" section below.

**3e. Add "Time Control" `CollapsingHeader` section** in `DrawUI()`, after the
`"Node Health"` section and before `"2PC History"`:

```csharp
if (ImGui.CollapsingHeader("Time Control", ImGuiTreeNodeFlags.DefaultOpen))
{
    long wallTicks = DateTimeOffset.UtcNow.Ticks;
    string wallTimeStr = new DateTime(wallTicks, DateTimeKind.Utc).ToString("HH:mm:ss.fff");
    ImGui.Text($"Wall Time: {wallTimeStr}");

    if (!bootstrapped) ImGui.BeginDisabled();

    float timeScale = 1.0f;  // future: read from _uiCache when S0506 lands
    if (ImGui.Button(_isPaused ? "Resume##OrcResume" : "Pause##OrcPause") && _sysOpWriter != null)
        _sysOpWriter.Write(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = _isPaused ? ClusterOpType.ResumeTime : ClusterOpType.PauseTime,
            PayloadJson   = string.Empty,
        });

    ImGui.SameLine();
    if (!_isPaused) ImGui.BeginDisabled();
    if (ImGui.Button("Step##OrcStep") && _sysOpWriter != null)
        _sysOpWriter.Write(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.StepTime,
            PayloadJson   = string.Empty,
        });
    if (!_isPaused) ImGui.EndDisabled();

    ImGui.SameLine();
    ImGui.SetNextItemWidth(150f);
    if (ImGui.SliderFloat("Speed##OrcSpeed", ref timeScale, 0.1f, 10.0f, "%.1fx") && _sysOpWriter != null)
        _sysOpWriter.Write(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.SetTimeScale,
            PayloadJson   = timeScale.ToString("F2",
                System.Globalization.CultureInfo.InvariantCulture),
        });

    if (!bootstrapped) ImGui.EndDisabled();
}
```

**Tests required:**  
Add to `Hrot.ClusterRunner.Tests/OrchestratorSubsystemTests.cs`:

- `PauseButton_WhenNotPaused_DispatchesPauseTime` — render one frame; assert button
  labelled "Pause" exists (check ImGui state).  Programmatically click it; assert a
  `ClusterOpRequest` with `PauseTime` was written.  (Use the test-writer pattern already
  in use in `OrchestratorSubsystemTests`.)
- `StepButton_DisabledWhenNotPaused` — render one frame with `_isPaused == false`;
  assert `BeginDisabled` wraps the Step button (observe that clicking does not
  produce a request).
- `TimeControlRequested_PauseTime_SetsIsPaused` — after `Initialize`, call
  `TestHook_ClusterMaster.HandleClusterOpRequest(PauseTime)`; assert `_isPaused == true`
  via a `internal bool IsPausedForTest => _isPaused` hook.

> **Note on `_timeKernel.GetTimeController().SetTimeScale`:** Check whether this
> API exists in `ModuleHostKernel`.  If not, document the deviation and skip the
> call — the test only needs to verify the `ClusterOpRequest` payload, not the kernel
> side-effect.

---

### Task 4 (S0503-D): Replay seek debounce in `OrchestratorScenarioPanel`

**File:** `Hrot.ClusterRunner/Services/OrchestratorScenarioPanel.cs` (UPDATE)  
**Task Definition:** [CGF1-S0503](../CGF-1-TASK-DETAIL.md#cgf1-s0503--time-control-section--remote-time-commands), items 5–7  
**Design ref:** [§4.3](../CGF-1-ADDENDUM-3.md#43-replay-seek-debounce)

**4a. New fields:**

```csharp
// ── Seek debounce (S0503) ─────────────────────────────────────────────
private float _seekDebounceTimer = 0f;
private bool  _seekPending       = false;
private float _replayDuration    = 3600f;
```

**4b. Add `Update(float dt)` method:**

```csharp
/// <summary>
/// Advances the seek debounce timer.  Call once per frame from
/// <see cref="OrchestratorSubsystem.Update"/>.
/// </summary>
public void Update(float dt)
{
    if (!_seekPending) return;
    _seekDebounceTimer -= dt;
    if (_seekDebounceTimer > 0f) return;

    _seekPending = false;
    long wallTicks = (long)(_seekSliderValue * 10_000_000L);
    _sysOpWriter.Write(new ClusterOpRequest
    {
        RequestId     = Guid.NewGuid(),
        OperationType = ClusterOpType.ReplaySeek,
        PayloadJson   = $"{{\"TargetWallTicks\":{wallTicks}}}",
    });
}
```

**4c. Add `GetReplayDuration(string drillId)` static helper** (reads `*.meta.json` TotalFrames):

```csharp
/// <summary>
/// Reads the replay duration in seconds from the drill's meta.json.
/// Returns 3600 if the file is absent or malformed.
/// </summary>
internal static float GetReplayDuration(string metaJsonContent)
{
    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(metaJsonContent);
        if (doc.RootElement.TryGetProperty("TotalFrames", out var el))
            return el.GetInt32() / 60f;
    }
    catch { }
    return 3600f;
}
```

**4d. Update `Render()` signature** to accept `isPaused` and `drillTime`:

```csharp
public void Render(bool isPaused = false, float drillTime = 0f)
```

Pass both through to `RenderReplaySection`.

**4e. Update `RenderReplaySection`** to accept `(ClusterState, bool disableAll, bool isPaused, float currentDrillTime)`:

- When **not** `_seekPending`: `_seekSliderValue = currentDrillTime;`  (passive track)
- When slider is dragged: `_seekPending = true; _seekDebounceTimer = 0.5f;`  
  Remove the old immediate-write on slider drag.
- On "Load Replay" click, attempt to load `_replayDuration` from the meta.json file
  for the selected drill.  See §5 of the addendum for path convention.  If the file
  can't be read, `_replayDuration` remains at the 3600 fallback.

**Tests required:**  
Add to `Hrot.ClusterRunner.Tests/OrchestratorScenarioPanelTests.cs`:

- `GetReplayDuration_TotalFrames3600_Returns60s` — `Assert.Equal(60f, OrchestratorScenarioPanel.GetReplayDuration("{\"TotalFrames\":3600}"))`
- `GetReplayDuration_MalformedJson_ReturnsFallback` — assert returns `3600f`
- `SeekDebounce_DoesNotWriteWithin400ms` — call `Update(0.1f)` × 4 immediately after
  arming `_seekPending`; assert no `ClusterOpRequest` written.
- `SeekDebounce_WritesAfter500ms` — arm `_seekPending`; call `Update(0.5f)`; assert
  exactly 1 `ClusterOpRequest` with `OperationType == ClusterOpType.ReplaySeek`.

---

### Task 5 (S0504): Asset Combo Selection

**File:** `Hrot.ClusterRunner/Services/OrchestratorScenarioPanel.cs` (UPDATE)  
**Task Definition:** [CGF1-S0504](../CGF-1-TASK-DETAIL.md#cgf1-s0504--asset-combo-selection-local-filesystem-scan)  
**Design ref:** [§5](../CGF-1-ADDENDUM-3.md#5-asset-combo-selection-local-scan)

**5a. Replace text-input fields with combo state:**

Remove fields:
```csharp
private string _loadScenarioId  = string.Empty;
private string _replayExerciseId   = string.Empty;
private string _injectScenarioId = string.Empty;
private string _injectStoryId    = string.Empty;
```

Add:
```csharp
// ── Asset combo state (S0504) ─────────────────────────────────────────
private string[] _availableScenarios     = Array.Empty<string>();
private string[] _availableStories       = Array.Empty<string>();
private string[] _availableDrills        = Array.Empty<string>();
private int      _selectedLoadScenarioIdx = -1;
private int      _selectedExerciseIdx        = -1;
private int      _selectedStoryIdx        = -1;
```

**5b. Add `RefreshLocalAssets()` and call it from the constructor:**

Add `using System.IO;` at the top.

```csharp
/// <summary>
/// Scans <c>C:\FDP_Temp</c> for asset folders.
/// Subdirectories containing <c>*.fdp</c> files are drills;
/// subdirectories containing <c>*.json</c> files are scenario/story packages.
/// Protected for unit-test override via wrapper.
/// </summary>
internal void RefreshLocalAssets(string? root = null)
{
    root ??= @"C:\FDP_Temp";
    var scenarios = new List<string>();
    var drills    = new List<string>();

    if (Directory.Exists(root))
    {
        foreach (var dir in Directory.GetDirectories(root))
        {
            var name = Path.GetFileName(dir)!;
            if (Directory.GetFiles(dir, "*.fdp").Length > 0)
                drills.Add(name);
            else if (Directory.GetFiles(dir, "*.json").Length > 0)
                scenarios.Add(name);
        }
    }

    _availableScenarios = scenarios.ToArray();
    _availableStories   = scenarios.ToArray();   // stories share scenario packages
    _availableDrills    = drills.ToArray();

    if (_selectedLoadScenarioIdx >= _availableScenarios.Length) _selectedLoadScenarioIdx = -1;
    if (_selectedStoryIdx        >= _availableStories.Length)   _selectedStoryIdx        = -1;
    if (_selectedExerciseIdx        >= _availableDrills.Length)    _selectedExerciseIdx        = -1;
}
```

Call `RefreshLocalAssets()` at the end of the constructor.

**5c. Update `RenderScenarioSection`:**

Replace:
```csharp
ImGui.InputText("Load Scenario ID##OrcLoadId", ref _loadScenarioId, 128);
ImGui.SameLine();
```

With:
```csharp
ImGui.Combo("Select Scenario##OrcLoadId", ref _selectedLoadScenarioIdx,
    _availableScenarios, _availableScenarios.Length);
ImGui.SameLine();
if (ImGui.Button("⟳##RefScen")) RefreshLocalAssets();
ImGui.SameLine();
```

In the two load buttons, replace `_loadScenarioId` usage with:
```csharp
if (_selectedLoadScenarioIdx >= 0)
{
    string scenId = _availableScenarios[_selectedLoadScenarioIdx];
    // ... use scenId in PayloadJson ...
}
```

Guard both "Load into Edit" and "Load into Live" buttons with `_selectedLoadScenarioIdx >= 0`
check.  The Save section's `_saveScenarioId` text input is **unchanged**.

**5d. Update `RenderReplaySection`:**

Replace the `_replayExerciseId` `InputText` with:
```csharp
ImGui.Combo("Select Drill##OrcReplayId", ref _selectedExerciseIdx,
    _availableDrills, _availableDrills.Length);
ImGui.SameLine();
if (ImGui.Button("⟳##RefDrill")) RefreshLocalAssets();
```

Guard the "Load Replay" button with `_selectedExerciseIdx >= 0` and use
`_availableDrills[_selectedExerciseIdx]` as `drillId`.

**5e. Update `RenderStoriesSection`:**

Remove `_injectScenarioId` and `_injectStoryId` `InputText` widgets.  
Add:
```csharp
ImGui.Combo("Story Package##OrcInjectScen", ref _selectedStoryIdx,
    _availableStories, _availableStories.Length);
ImGui.SameLine();
if (ImGui.Button("⟳##RefStory")) RefreshLocalAssets();
```

On "Inject Story" click, use:
```csharp
if (_selectedStoryIdx >= 0)
{
    string scenId    = _availableStories[_selectedStoryIdx];
    string newStoryId = Guid.NewGuid().ToString();
    _sysOpWriter.Write(new ClusterOpRequest
    {
        RequestId     = Guid.NewGuid(),
        OperationType = ClusterOpType.ManageEpisode,
        PayloadJson   = $"{{\"Mode\":\"Start\"," +
                        $"\"StoryId\":\"{newStoryId}\"," +
                        $"\"ScenarioId\":\"{scenId}\"}}",
    });
}
```

**Tests required:**  
Add to `Hrot.ClusterRunner.Tests/OrchestratorScenarioPanelTests.cs`:

- `RefreshLocalAssets_PopulatesFromTempDirectory` — create a temp directory with one
  subdirectory containing `entities.json` (scenario) and one subdirectory containing
  `node_1.fdp` (drill); call `panel.RefreshLocalAssets(tmpRoot)`; assert
  `_availableScenarios.Length == 1` and `_availableDrills.Length == 1`.  
  Access the private arrays via `internal` exposure (add
  `[assembly: InternalsVisibleTo("Hrot.ClusterRunner.Tests")]` if not already present, or
  use reflection).
- `RefreshLocalAssets_ClampsStaleSelectionIndex` — set `_selectedExerciseIdx = 5`;
  call `RefreshLocalAssets` with empty root; assert `_selectedExerciseIdx == -1`.
- `InjectStory_AutoGeneratesStoryId` — trigger two successive "Inject Story" presses
  (via the writer spy); assert the two `ManageEpisode` `PayloadJson` values contain
  different `StoryId` GUIDs.
- `LoadScenario_WithNoSelection_DisabledGuard` — with `_selectedLoadScenarioIdx = -1`,
  simulate a "Load into Live" press; assert no `ClusterOpRequest` was written.

---

## 🧪 Testing Requirements

| Test project | Baseline | Minimum after batch |
|---|---|---|
| `Hrot.NED.Tests` | passes | +2 |
| `Hrot.Orchestrator.Tests` | 46 | ≥ 49 (+1 fan-out debt, +2 TimeControl) |
| `Hrot.ClusterRunner.Tests` | 148 | ≥ 159 (+3 subsystem + 4 debounce/duration + 4 panel combo) |

**All existing tests must still pass.**

### Test Quality Standards

- **Tests must verify behavior**, not just property assignment.  
  ❌ `Assert.NotNull(panel)` — useless.  
  ✅ `Assert.Equal(ClusterOpType.PauseTime, capturedOp)` — verifies dispatch behavior.
- **Writer-spy pattern:** inject a `DdsWriter<ClusterOpRequest>` backed by a real DDS
  participant and poll the matching `DdsReader` in the test process to intercept writes.  
  This is the established pattern in `ClusterMasterFanOutTests` and
  `OrchestratorScenarioPanelTests`.
- **No mock frameworks.** Use real DDS participants on domain ≥ 20 (reserved test range).
- **Headless ImGui pattern** for rendering-dependent tests follows the pattern in
  `OrchestratorScenarioPanelTests` (create context, begin/end frame, verify state).

---

## ⚠️ Quality Standards

**❗ CRITICAL — do not regress existing tests.**  Before submitting, run the full test
suite with `dotnet test --no-build` and confirm all 194 tests that were green after
BATCH-26 still pass.

**❗ `InternalsVisibleTo`:** `Hrot.ClusterRunner` must expose internals to
`Hrot.ClusterRunner.Tests`.  Check `Directory.Build.props` or existing assembly attributes
before adding a new `[assembly: InternalsVisibleTo(...)]`.

**❗ `StepFrame`:** If `ModuleHostKernel` does not have a `StepFrame(float)` method,
do **not** add one without documenting the deviation in your report.  Use
`_timeKernel?.Update()` as the fallback and note it as a delta.

**❗ Time zone safety:** Always use `System.Globalization.CultureInfo.InvariantCulture`
when parsing/formatting `SetTimeScale` payloads (avoids comma-decimal locale bugs).

---

## 📊 Report Requirements

**Submit to:** `.dev/cgf-1/reports/CGF-1-BATCH-27-REPORT.md`

Your report MUST address the following questions:

1. **Implementation Summary:** What did you build? For each task, one paragraph.

2. **Developer Insights:**
   - What issues did you encounter during implementation?
   - What weak points did you spot in the codebase while working on this batch?
   - What design decisions did you make beyond the spec?

3. **Deviations:** List any changes from the instructions with a clear rationale.
   - Did `ModuleHostKernel.StepFrame` exist? If not, what did you do?
   - Did any API shape differ from the code examples? How did you adapt?

4. **Test Results:** Paste the final test run output for all three test projects.

5. **Challenges:** What was most difficult? How did you solve it?

6. **Known Issues / P2–P3 Observations:** Anything deferred or spotted for future work.
