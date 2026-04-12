# BATCH-03 Report

## 1. What Was Done

### Task A: Created `Fdp.Presentation`

- Created `FDP/Framework/Fdp.Presentation/Fdp.Presentation.csproj` absorbing three
  existing projects: `FDP.Toolkit.Vis2D`, `FDP.Toolkit.ImGui`, `FDP.Framework.Raylib`.
- Source files placed in subdirectories matching origin:
  - `FDP.Toolkit.Vis2D/` -> `Fdp.Presentation/Vis2D/`
  - `FDP.Toolkit.ImGui/` -> `Fdp.Presentation/ImGui/`
  - `FDP.Framework.Raylib/` -> `Fdp.Presentation/Raylib/`
- All existing namespaces preserved (`FDP.Toolkit.Vis2D`, `FDP.Toolkit.ImGui`,
  `FDP.Framework.Raylib`). No namespace renames.
- NuGet packages: `Raylib-cs 7.0.2`, `rlImGui-cs 3.2.0`, `ImGui.NET 1.91.6.1`
  (upgraded from 1.91.0.1 required by `rlImGui-cs 3.2.0`).
- `InternalsVisibleTo` attributes consolidated from all three merged projects.
- `ZERO` `ProjectReference` to `CycloneDDS.*` (verified).
- Embedded resource `FDP.Toolkit.ImGui.Icons.famfamfam-silk.png` preserved from
  `FDP.Toolkit.ImGui`.
- Created `FDP/Framework/Fdp.Presentation.Tests/Fdp.Presentation.Tests.csproj`
  merging test files from `FDP.Toolkit.Vis2D.Tests`, `FDP.Toolkit.ImGui.Tests`,
  `FDP.Framework.Raylib.Tests` into subdirectories `Vis2D/`, `ImGui/`, `Raylib/`.

### Task B: Created `Fdp.Network.Cyclone`

- Created `FDP/ModuleHost/Fdp.Network.Cyclone/Fdp.Network.Cyclone.csproj` as the
  canonical rename of `ModuleHost.Network.Cyclone`.
- All source files copied from `ModuleHost.Network.Cyclone/` into the new directory.
- Namespace `ModuleHost.Network.Cyclone` preserved throughout all files.
- Created `FDP/ModuleHost/Fdp.Network.Cyclone.Tests/Fdp.Network.Cyclone.Tests.csproj`
  with the matching test files.
- `ZERO` `ProjectReference` to any `Hrot.*` project (verified).

### Architecture Change: `MapCameraView` struct

- `FDP.Toolkit.Vis2D` defined `IMapCameraProvider` (in `Fdp.Engine`) which created
  a circular dependency: `Fdp.Engine -> Fdp.Presentation -> Fdp.Engine`.
- Resolved by introducing `MapCameraView` as a plain value type in
  `FDP/Toolkits/Fdp.Engine/Vis2D/MapCameraView.cs` (namespace
  `FDP.Toolkit.Vis2D.Components`), eliminating the back-reference to `Fdp.Engine`
  from `Fdp.Presentation`.

### Solution files updated

- `FDP/FDP.sln`: removed three old projects
  (`FDP.Toolkit.Vis2D`, `FDP.Toolkit.ImGui`, `FDP.Framework.Raylib`) and their test
  projects; added `Fdp.Presentation`, `Fdp.Presentation.Tests`,
  `Fdp.Network.Cyclone`, `Fdp.Network.Cyclone.Tests`.
- `IOS-IG-SimHost.sln`: same additions and removals.

---

## 2. Issues Encountered and Resolved

### Issue 1: ImGui.NET version mismatch
`rlImGui-cs 3.2.0` requires `ImGui.NET >= 1.91.6.1` but the initial csproj pinned
`1.91.0.1`. Upgraded `Fdp.Presentation.csproj` to `1.91.6.1`.

### Issue 2: Invalid XML comment in `Hrot.SimHost.csproj`
`<!-- Network (CycloneDDS transitive -- no separate NuGet needed) -->` contains `--`
which is illegal in XML comments. Changed to `,`.

### Issue 3: ImGui namespace ambiguity in `RaylibInputProvider.cs`
The project now contains namespace `FDP.Toolkit.ImGui`, causing `ImGui.GetIO()` to
resolve to the namespace instead of the `ImGuiNET.ImGui` class. Fixed by using the
fully qualified call `ImGuiNET.ImGui.GetIO()`.

### Issue 4: Missing `using` in `SubsystemOrchestrator.cs`
`MapCameraView` (placed in `FDP.Toolkit.Vis2D.Components`) was not found without an
explicit using directive. Added `using FDP.Toolkit.Vis2D.Components;`.

### Issue 5: `Fdp.Examples.NetworkDemo` still referencing old assembly
`Fdp.Examples.NetworkDemo.csproj` had a `ProjectReference` pointing to the old
`ModuleHost.Network.Cyclone.csproj`. Updated to `Fdp.Network.Cyclone.csproj`.

### Issue 6: `NullCameraSubsystemMock` missing `IMapCameraProvider` interface
`Hrot.ClusterRunner.Tests/SubsystemOrchestratorTests.cs` defines a mock that
implements `ICameraSubsystem`. After `MapCameraView` was added to the interface,
the mock was missing `GetCameraView()` and `ApplyCameraView()`. Added both stubs.

### Issue 7: ImGui test hang (`UnmanagedComponent_FirstFrame_NoFlash`)
`ComponentReflectorTests.UnmanagedComponent_FirstFrame_NoFlash` contained a dead-code
line that called `ImGuiApi.GetStyleColorName(ImGuiCol.COUNT)`. `ImGuiCol.COUNT` is an
out-of-range index — passing it to the native ImGui function triggers `IM_ASSERT(0)`
(compiled as `__debugbreak()`) in ImGui.NET 1.91.6.1, hanging the test host.
The variable `stackBefore` was assigned but never read (comment: "proxy — we use
cache-state instead"). Removed the line entirely.

### Issue 8: `ImGuiTestFixture` thread safety
When VS Code test explorer and a terminal `dotnet test` run share the same
`ServiceHub.Host.dotnet.x64` process, two xunit adapters execute simultaneously.
Added a `static SemaphoreSlim(1, 1)` to `ImGuiTestFixture` to serialize all ImGui
context creation and destruction across threads, preventing native state corruption.

---

## 3. Weak Points Spotted

1. **Old source directories still present**: `FDP/Toolkits/FDP.Toolkit.Vis2D/`,
   `FDP/Toolkits/FDP.Toolkit.ImGui/`, `FDP/Framework/FDP.Framework.Raylib/`, and
   `FDP/ModuleHost/ModuleHost.Network.Cyclone/` still exist as directories with
   their original `.csproj` files. They were left in place (no destructive deletions
   per operational safety). A cleanup pass that removes them from the repository is
   tracked in DEBT-TRACKER.md.

2. **ImGui INI file path**: The headless `ImGuiTestFixture` does not disable
   `io.IniFilename`, so ImGui may silently write/read `imgui.ini` in the working
   directory during tests. This is benign but creates noise in the repository root.

---

## 4. Design Decisions Made Beyond Spec

1. **`MapCameraView` placed in `Fdp.Engine`** (namespace `FDP.Toolkit.Vis2D.Components`)
   rather than `Fdp.Presentation`. The spec did not prescribe how to break the
   circular dependency; placing the value type in `Fdp.Engine` (which `Fdp.Presentation`
   already references) was the minimal change that resolved it without introducing
   a new assembly.

2. **ImGui.NET upgraded to 1.91.6.1**: The spec said `1.91.0.1` but `rlImGui-cs 3.2.0`
   has a strict minimum on `1.91.6.1`. The upgrade was mandatory for the build to
   succeed.

3. **`xunit.runner.json` added at project root** with `parallelizeAssembly: false` and
   `parallelizeTestCollections: false` to guarantee sequential execution of the
   "ImGui Sequential" test collection. Required because the merged test assembly
   contains headless ImGui, Vis2D, and Raylib tests that must not run concurrently
   within a single process.

---

## 5. Test Results

### Fdp.Network.Cyclone.Tests

```
dotnet test FDP/ModuleHost/Fdp.Network.Cyclone.Tests/Fdp.Network.Cyclone.Tests.csproj
```

**Passed! - Failed: 0, Passed: 42, Skipped: 0, Total: 42**

### Fdp.Presentation.Tests

```
dotnet test FDP/Framework/Fdp.Presentation.Tests/Fdp.Presentation.Tests.csproj
```

**Passed! - Failed: 0, Passed: 181, Skipped: 0, Total: 181**

Breakdown:
- Vis2D tests: 27 (from `FDP.Toolkit.Vis2D.Tests`)
- ImGui tests: 152 (from `FDP.Toolkit.ImGui.Tests`)
- Raylib tests: 2 (from `FDP.Framework.Raylib.Tests`)

---

## 6. Build Result

```
dotnet build IOS-IG-SimHost.sln
```

**Build succeeded. 0 Error(s).**

---

## 7. Files Changed List

### New project and solution entries
- `FDP/Framework/Fdp.Presentation/Fdp.Presentation.csproj`
- `FDP/Framework/Fdp.Presentation.Tests/Fdp.Presentation.Tests.csproj`
- `FDP/ModuleHost/Fdp.Network.Cyclone/Fdp.Network.Cyclone.csproj`
- `FDP/ModuleHost/Fdp.Network.Cyclone.Tests/Fdp.Network.Cyclone.Tests.csproj`

### New source file (architecture change)
- `FDP/Toolkits/Fdp.Engine/Vis2D/MapCameraView.cs`

### Configuration files
- `FDP/Framework/Fdp.Presentation.Tests/xunit.runner.json` (new)

### `.csproj` files modified
- `FDP/Framework/Fdp.Presentation/Fdp.Presentation.csproj`
  (ImGui.NET version upgraded to 1.91.6.1)
- `FDP/Examples/Fdp.Examples.NetworkDemo/Fdp.Examples.NetworkDemo.csproj`
  (reference updated from `ModuleHost.Network.Cyclone` to `Fdp.Network.Cyclone`)
- `Hrot.SimHost/Hrot.SimHost.csproj` (fixed invalid XML comment)

### Solution files modified
- `FDP/FDP.sln`
- `IOS-IG-SimHost.sln`

### Source files modified
- `FDP/Toolkits/Fdp.Engine/Runner/SubsystemOrchestrator.cs`
  (added `using FDP.Toolkit.Vis2D.Components;`)
- `FDP/Framework/Fdp.Presentation/Vis2D/Defaults/RaylibInputProvider.cs`
  (qualified `ImGuiNET.ImGui.GetIO()` to resolve namespace ambiguity)
- `FDP/Framework/Fdp.Presentation.Tests/ImGui/ImGuiTestFixture.cs`
  (added `SemaphoreSlim` for thread-safe ImGui context management)
- `FDP/Framework/Fdp.Presentation.Tests/ImGui/ComponentReflectorTests.cs`
  (removed `GetStyleColorName(ImGuiCol.COUNT)` dead-code line that caused native hang)
- `Hrot.ClusterRunner.Tests/SubsystemOrchestratorTests.cs`
  (added `GetCameraView()` and `ApplyCameraView()` stubs to `NullCameraSubsystemMock`)
