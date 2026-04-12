# BATCH-12: Move ISubsystem Adapters to Plugin Assemblies

**Batch Number:** BATCH-12
**Tasks:** TASK-P4-004
**Phase:** Phase 4 — Subsystem Decoupling
**Estimated Effort:** 4-5 hours
**Priority:** HIGH
**Dependencies:** BATCH-11 complete

---

## Onboarding & Workflow

### Developer Instructions

This batch implements TASK-P4-004: move subsystem adapter files from
`Hrot.ClusterRunner/Services/` into their respective plugin assemblies so that
`Hrot.ClusterRunner.dll` no longer contains concrete subsystem types.

The critical constraint: each moved file must compile in its new location without
changes to logic. This means window files must co-locate with subsystems (no
new reverse dependencies allowed).

### Required Reading (in order)

1. **Task Definition:** `.dev/modular-2/TASK-DETAIL.md#task-p4-004`
2. **Previous report:** `.dev/modular-2/reports/BATCH-11-REPORT.md`
3. **ISubsystem interface:** search `Fdp.Engine` for `interface ISubsystem`
4. **Program.cs:** `Hrot.ClusterRunner/Program.cs` — understand subsystem instantiation
5. **ClusterRunner RunMode:** `Hrot.ClusterRunner/Configuration/` — mode enum

### Source Code Areas

- **Moving OUT of:** `Hrot.ClusterRunner/Services/`
- **Moving OUT of:** `Hrot.ClusterRunner/Windows/` (domain-specific window files)
- **Moving INTO:** `Hrot.SimHost/`, `Hrot.IG/`, `Hrot.CGF/`, `Hrot.ExCon/`, `Hrot.Orchestrator/`, `Hrot.Editor/`

### Report Submission

When done, submit your report to: `.dev/modular-2/reports/BATCH-12-REPORT.md`

---

## Context

`Hrot.ClusterRunner/Services/` currently contains concrete subsystem adapters for all
simulation node roles. These adapters act as glue code (they initialize the mode-specific
application; register windows; wire up UI infrastructure) but they belong logically in
the plugin assemblies they boot. The goal is that ClusterRunner becomes a pure composition
root: it loads plugin DLLs, discovers their `ISubsystem` implementations via reflection,
and delegates everything else.

**Key insight on windows:** Subsystem adapters create and register window objects in their
`Initialize()` methods. To avoid a reverse dependency (plugin → ClusterRunner), each
domain-specific window file must move with its subsystem. Shared FDP debug windows
(`FdpEntityInspectorWindow`, `FdpEventBrowserWindow`) belong in `Hrot.Presentation`.

---

## Objectives

1. Delete all concrete subsystem classes from `Hrot.ClusterRunner/Services/`
2. Each subsystem type lives in its plugin assembly and compiles there
3. `Program.cs` still works (update `using` directives only; no logic changes)
4. Headless unit tests exist for each moved subsystem
5. `CiSubsystem` moved to `Hrot.ClusterRunner/Scenarios/` (no external plugin needed)

---

## Tasks

---

### Phase 1: Pre-move helper type relocation

Before moving the subsystems themselves, relocate types they depend on.

#### Phase 1.1: Move CgfDebugVisualizerAdapter → Hrot.CGF

**File:** `Hrot.ClusterRunner/Services/CgfDebugVisualizerAdapter.cs`
**Target:** `Hrot.CGF/CgfDebugVisualizerAdapter.cs`

Change namespace from whatever it currently is to `Hrot.CGF`.
Verify `Hrot.CGF.csproj` already has all needed references (FDP.Toolkit.Behavior,
FDP.Toolkit.Vis2D.Abstractions, Fdp.Kernel). Add any missing ones.

#### Phase 1.2: Move EyesAndMuscleModule → Hrot.SimHost

**File:** `Hrot.ClusterRunner/Services/EyesAndMuscleModule.cs`
**Target:** `Hrot.SimHost/Modules/EyesAndMuscleModule.cs`

Change namespace to `Hrot.SimHost.Modules`.
Verify `Hrot.SimHost.csproj` has FDP.Toolkit.Navigation, FDP.Toolkit.Replication.Components,
Fdp.Kernel, Hrot.Common (NavigationIntent, NodeRole). Add missing refs.

#### Phase 1.3: Move ClusterScenarioPanel + ClusterUiCache → Hrot.Orchestrator

**Files:**
- `Hrot.ClusterRunner/Services/ClusterScenarioPanel.cs` → `Hrot.Orchestrator/Panels/ClusterScenarioPanel.cs`
- `Hrot.ClusterRunner/Services/ClusterUiCache.cs` → `Hrot.Orchestrator/Panels/ClusterUiCache.cs`

Change namespaces to `Hrot.Orchestrator.Panels`.

**Why:** Both files depend on `Hrot.Orchestrator.ClusterMaster` and orchestration types.
Moving them to Hrot.Orchestrator eliminates this reverse dependency.

Verify `Hrot.Orchestrator.csproj` has all required references (FDP.Toolkit.Orchestration,
FDP.Toolkit.Time, ImGui, clusterstate types). Add missing ones.

#### Phase 1.4: Move shared FDP debug windows → Hrot.Presentation

**File to examine:** `Hrot.ClusterRunner/Windows/FdpPanelWindows.cs`

Check if `FdpEntityInspectorWindow` and `FdpEventBrowserWindow` are already in
`Hrot.Presentation`. If not, move them there:
- **Target:** `Hrot.Presentation/Windows/FdpPanelWindows.cs`
- Namespace: `Hrot.Presentation` or `Hrot.Presentation.Windows`

Verify `Hrot.Presentation.csproj` has the required FDP dependencies. Add missing refs.

---

### Phase 2: Move CiSubsystem within ClusterRunner

`CiSubsystem` is a runner-specific scenario bootstrap (not a plugin). It should live in
`Hrot.ClusterRunner/Scenarios/` so the reflection scanner can discover it.

**File:** `Hrot.ClusterRunner/Services/CiSubsystem.cs`
**Target:** `Hrot.ClusterRunner/Scenarios/CiSubsystem.cs`

Change namespace to `Hrot.ClusterRunner.Scenarios`.
Ensure it still implements `ISubsystem` with `Name => "ci"`.
Remove any hardcoded `if (mode == "ci")` branch from `Program.cs` if one exists
(the reflection scanner should find it naturally).

---

### Phase 3: Move EyesAndMuscleSubsystem with its module reference updated

Now that EyesAndMuscleModule is in Hrot.SimHost (Phase 1.2):

**File:** `Hrot.ClusterRunner/Services/EyesAndMuscleSubsystem.cs`
(this file stays in ClusterRunner/Services/ for now — it's a runner-internal wiring
subsystem, not a plugin assembly subsystem)

**Action:** Update the `using` in EyesAndMuscleSubsystem.cs to reference
`Hrot.SimHost.Modules.EyesAndMuscleModule` (new namespace).
This file stays in `Hrot.ClusterRunner/Services/` as runner infrastructure.

---

### Phase 4: Move SimHostSubsystem → Hrot.SimHost

**Files to move:**
1. `Hrot.ClusterRunner/Services/SimHostSubsystem.cs` → `Hrot.SimHost/SimHostSubsystem.cs`
2. `Hrot.ClusterRunner/Windows/SimHostWindows.cs` → `Hrot.SimHost/Windows/SimHostWindows.cs`

**Namespace changes:**
- SimHostSubsystem: from `Hrot.ClusterRunner.Services` → `Hrot.SimHost`
- SimHostWindows.cs: change namespace to `Hrot.SimHost.Windows`

**Dependency check:** After moving, check what `SimHostSubsystem.cs` references:
- `SimHostControlsWindow` → moves to `Hrot.SimHost.Windows` (same file move above) ✓
- `FdpEntityInspectorWindow` / `FdpEventBrowserWindow` → now in `Hrot.Presentation` (Phase 1.4) ✓
- Any remaining ClusterRunner types? If yes, note them as blockers.

**Update `Hrot.SimHost.csproj`:**
- Add reference to `Hrot.Presentation` if not already present (for shared FDP windows)
- Add reference to `Fdp.Engine` for `ISubsystem` if not already present

**Update `Hrot.ClusterRunner/Program.cs` (or wherever OrchestratorSubsystem is instantiated):**
- Add `using Hrot.SimHost;` (no logic change, just namespace fix)

---

### Phase 5: Move IgSubsystem → Hrot.IG

**Files to move:**
1. `Hrot.ClusterRunner/Services/IgSubsystem.cs` → `Hrot.IG/IgSubsystem.cs`
2. `Hrot.ClusterRunner/Windows/IgWindows.cs` → `Hrot.IG/Windows/IgWindows.cs`

**Namespace changes:**
- IgSubsystem: from `Hrot.ClusterRunner.Services` → `Hrot.IG`
- IgWindows: → `Hrot.IG.Windows`

**Dependency check:** Look for any reference to ClusterRunner-only types.

**Update `Hrot.IG.csproj`:**
- Ensure `Hrot.Presentation` reference present (for shared FDP windows if used)
- Add `Fdp.Engine` for `ISubsystem`

---

### Phase 6: Move CgfSubsystem → Hrot.CGF

**Prerequisites:** Phase 1.1 complete (CgfDebugVisualizerAdapter in Hrot.CGF).

**File to move:** `Hrot.ClusterRunner/Services/CgfSubsystem.cs` → `Hrot.CGF/CgfSubsystem.cs`

**Namespace change:** `Hrot.ClusterRunner.Services` → `Hrot.CGF`

Check for any ClusterRunner-specific window types used in CgfSubsystem.
If `Hrot.ClusterRunner/Windows/` had a Cgf-specific window file, move it to `Hrot.CGF/Windows/`.

**Update `Hrot.CGF.csproj`:** Add `Fdp.Engine` reference if not present.

---

### Phase 7: Move ExConSubsystem → Hrot.ExCon

**Prerequisites:** Phase 1.3 complete (ClusterScenarioPanel + ClusterUiCache in Hrot.Orchestrator).

**Files to move:**
1. `Hrot.ClusterRunner/Services/ExConSubsystem.cs` → `Hrot.ExCon/ExConSubsystem.cs`
2. `Hrot.ClusterRunner/Windows/ExConWindows.cs` → `Hrot.ExCon/Windows/ExConWindows.cs`

**Namespace changes:** → `Hrot.ExCon`, `Hrot.ExCon.Windows`

**Update `Hrot.ExCon.csproj`:**
- Add `Hrot.Orchestrator` reference (for ClusterScenarioPanel, ClusterUiCache)
- Add `Fdp.Engine` for `ISubsystem`

---

### Phase 8: Move OrchestratorSubsystem → Hrot.Orchestrator

**Prerequisites:** Phase 1.3 complete (ClusterScenarioPanel + ClusterUiCache in Hrot.Orchestrator).

**Files to move:**
1. `Hrot.ClusterRunner/Services/OrchestratorSubsystem.cs` → `Hrot.Orchestrator/OrchestratorSubsystem.cs`
2. `Hrot.ClusterRunner/Windows/OrchestratorWindow.cs` → `Hrot.Orchestrator/Windows/OrchestratorWindow.cs`
3. `Hrot.ClusterRunner/Windows/ClusterControlWindow.cs` → `Hrot.Orchestrator/Windows/ClusterControlWindow.cs`

**Namespace changes:** → `Hrot.Orchestrator`, `Hrot.Orchestrator.Windows`

**Update `Hrot.Orchestrator.csproj`:** Add `Fdp.Engine`; already has Hrot.Common, Orchestration.

---

### Phase 9: Move EditorSubsystem → Hrot.Editor

**Check first:** Does `Hrot.Editor/` exist as a project? If not, this phase is DEFERRED.
Read `Hrot.Editor/Hrot.Editor.csproj` if present.

**Files to move:**
1. `Hrot.ClusterRunner/Services/EditorSubsystem.cs` → `Hrot.Editor/EditorSubsystem.cs`
2. `Hrot.ClusterRunner/Windows/EditorWindows.cs` → `Hrot.Editor/Windows/EditorWindows.cs`

**Namespace changes:** → `Hrot.Editor`, `Hrot.Editor.Windows`

**Update `Hrot.Editor.csproj`:** Add `Fdp.Engine` reference if not present.

---

### Phase 10: Update Program.cs

Read `Hrot.ClusterRunner/Program.cs` fully. Update `using` directives to reflect new namespaces
for all moved types:

```csharp
using Hrot.SimHost;       // SimHostSubsystem
using Hrot.IG;            // IgSubsystem
using Hrot.CGF;           // CgfSubsystem
using Hrot.ExCon;         // ExConSubsystem
using Hrot.Orchestrator;  // OrchestratorSubsystem
using Hrot.Editor;        // EditorSubsystem
```

No changes to logic — only `using` statements and any fully-qualified type names.

---

### Phase 11: Add headless unit tests

For each moved subsystem, add a test asserting headless initialization does not throw:

**Add to the relevant test project** (e.g., Hrot.SimHost.Tests or a new shared tests file):

```csharp
[Fact]
public void SimHostSubsystem_InitializeHeadless_DoesNotThrow()
{
    var subsystem = new SimHostSubsystem();
    var config = new SubsystemConfig { Headless = true };
    var ex = Record.Exception(() => subsystem.Initialize(config));
    Assert.Null(ex); // in particular: no DllNotFoundException for native graphics
}
```

Add similar tests for: `IgSubsystem`, `ExConSubsystem`, `CgfSubsystem`,
`OrchestratorSubsystem`. Put them in the project where the subsystem now lives
or in the corresponding `.Tests` project.

---

## Build and Test Verification

```powershell
cd D:\Work\IOS-IG-SimHost-FDP-2

# Verify subsystem types no longer in ClusterRunner.dll
# (after building, check ClusterRunner.dll with reflection)
dotnet build IOS-IG-SimHost.sln -v quiet

# Run unit tests
dotnet test IOS-IG-SimHost.sln --filter "FullyQualifiedName!~Integration" -v quiet
```

**Success conditions:**
- Build: **0 errors**
- All unit tests pass
- Grep check: `Hrot.ClusterRunner/Services/` should contain only: `PerspectiveUpdateSubsystem.cs`, `EyesAndMuscleSubsystem.cs`, `EyesAndMuscleModule.cs` (if not moved), and `ClusterUiCache.cs` if blocked

---

## Report Requirements

Create `.dev/modular-2/reports/BATCH-12-REPORT.md` with:

1. **Phase summary table** — Done/Partial/Skipped for each phase with explanation
2. **Remaining files in `Hrot.ClusterRunner/Services/`** — list what's left
3. **Build result** — 0 errors confirmation
4. **Test results** — headless subsystem test results
5. **Deferred items** — any phases skipped with debt proposals
