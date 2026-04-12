# BATCH-03: Create Fdp.Presentation and Fdp.Network.Cyclone

**Batch Number:** BATCH-03
**Tasks:** TASK-P1-003, TASK-P1-004
**Phase:** Phase 1 — FDP Layer Consolidation
**Estimated Effort:** 8–12 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 (Fdp.Core), BATCH-02 (Fdp.Engine)

---

## Onboarding & Workflow

### Developer Instructions

This batch completes Phase 1 FDP consolidation by:
1. Creating `Fdp.Presentation` (absorbing `FDP.Toolkit.Vis2D`, `FDP.Toolkit.ImGui`,
   `FDP.Framework.Raylib` — the rendering layer)
2. Creating `Fdp.Network.Cyclone` (renaming `ModuleHost.Network.Cyclone` to its new
   canonical name)

These two tasks are independent and can be done in parallel (or sequentially).

### Required Reading (IN ORDER)

1. **Task Definitions:**
   - `.dev/modular-2/TASK-DETAIL.md#task-p1-003-create-fdppresentation`
   - `.dev/modular-2/TASK-DETAIL.md#task-p1-004-create-fdpnetworkcyclone`
2. **Design Document:** `.dev/modular-2/DESIGN.md` — Section "FDP Layer (4 assemblies)"

### Source Code Locations

- **Fdp.Presentation sources:** `FDP/Toolkits/FDP.Toolkit.Vis2D/`,
  `FDP/Toolkits/FDP.Toolkit.ImGui/`, `FDP/Framework/FDP.Framework.Raylib/`
- **Fdp.Network.Cyclone source:** `FDP/ModuleHost/ModuleHost.Network.Cyclone/`
- **Target new projects:**
  - `FDP/Framework/Fdp.Presentation/Fdp.Presentation.csproj`
  - `FDP/ModuleHost/Fdp.Network.Cyclone/Fdp.Network.Cyclone.csproj`
- **FDP solution file:** `FDP/FDP.sln`
- **Top-level solution file:** `IOS-IG-SimHost.sln`

### Report Submission

When done, submit your report to: `.dev/modular-2/reports/BATCH-03-REPORT.md`

---

## Context

After BATCH-02, the FDP assembly graph is:
- `Fdp.Core` — ECS kernel
- `Fdp.Engine` — all simulation toolkits + runner loop
- ~~remaining~~ → **this batch creates the last 2 FDP assemblies**

`Fdp.Presentation` wraps Raylib/ImGui for the application layer (used by editors and
headful runners). `Fdp.Network.Cyclone` wraps CycloneDDS for the network layer.

---

## Tasks

### TASK A: Create Fdp.Presentation

**File:** `FDP/Framework/Fdp.Presentation/Fdp.Presentation.csproj` (NEW)

#### A1 — Project file

The new csproj must:
- Target `net8.0`, enable `ImplicitUsings`, `Nullable`
- References: `Fdp.Core`, `Fdp.Engine`
- NuGet packages (union of all three merged projects):
  - `Raylib-cs` 7.0.2
  - `rlImGui-cs` 3.2.0 (note: in FDP.Framework.Raylib this is `rlImgui-cs` — check exact spelling)
  - `ImGui.NET` 1.91.0.1
- **ZERO** `ProjectReference` to `CycloneDDS.*`
- Embedded resource: `FDP/Data/Icons/famfamfam-silk.png` with logical name
  `FDP.Toolkit.ImGui.Icons.famfamfam-silk.png` (from FDP.Toolkit.ImGui)
- Consolidate `InternalsVisibleTo` from all three merged projects.
  Include: `Fdp.Presentation.Tests`

#### A2 — Move source files

Move all `.cs` files into `FDP/Framework/Fdp.Presentation/`:
- From `FDP.Toolkit.Vis2D/` → `Fdp.Presentation/Vis2D/`
- From `FDP.Toolkit.ImGui/` → `Fdp.Presentation/ImGui/`
- From `FDP.Framework.Raylib/` → `Fdp.Presentation/Raylib/`

Preserve all existing namespaces (`FDP.Toolkit.Vis2D`, `FDP.Toolkit.ImGui`,
`FDP.Framework.Raylib`). No namespace renames.

#### A3 — Consolidate test projects

Create `FDP/Framework/Fdp.Presentation.Tests/Fdp.Presentation.Tests.csproj` merging:
- `FDP.Toolkit.Vis2D.Tests`
- `FDP.Toolkit.ImGui.Tests`
- `FDP.Framework.Raylib.Tests` (if it has any tests worth keeping; per DESIGN.md it
  may be superseded/empty)

Place test files in subdirectories: `Vis2D/`, `ImGui/`, `Raylib/` to avoid conflicts.

**Note from DESIGN.md:** `FDP.Framework.Raylib.Tests` is listed as deleted/superseded.
If it contains no meaningful tests, just delete the project; do not merge empty tests.

#### A4 — Update project references

Search entire repo for references to:
- `FDP.Toolkit.Vis2D.csproj`
- `FDP.Toolkit.ImGui.csproj`
- `FDP.Framework.Raylib.csproj`

Replace all with a single reference to `Fdp.Presentation.csproj`.

Affected projects include at minimum:
- `Hrot.Editor`, `Hrot.IG`, `Hrot.ClusterRunner` (they reference these for rendering)
- Any example projects that use Vis2D or ImGui

#### A5 — Update both solution files

Remove old project entries for the three absorbed projects and their tests.
Add `Fdp.Presentation` and `Fdp.Presentation.Tests`.

---

### TASK B: Create Fdp.Network.Cyclone

**File:** `FDP/ModuleHost/Fdp.Network.Cyclone/Fdp.Network.Cyclone.csproj` (NEW)

#### B1 — Project file

The new csproj must:
- Target `net8.0`, enable `Nullable`, `AllowUnsafeBlocks`
- References: `Fdp.Core`, `Fdp.Engine`
- Project references to CycloneDDS: `CycloneDDS.Runtime`, `CycloneDDS.Schema`,
  `CycloneDDS.Core` (same as current `ModuleHost.Network.Cyclone`)
- `NLog` 5.2.8 package reference
- Import: `CycloneDDS.CodeGen/CycloneDDS.targets`
- **ZERO** `ProjectReference` to any `Hrot.*` assembly
- InternalsVisibleTo: `Fdp.Network.Cyclone.Tests`, `ModuleHost.Network.Cyclone.Tests`
  (backward compat for the renamed test project)

#### B2 — Move source files

Move all `.cs` and `.idl` files from `FDP/ModuleHost/ModuleHost.Network.Cyclone/`
to `FDP/ModuleHost/Fdp.Network.Cyclone/`.

Preserve namespace `ModuleHost.Network.Cyclone` for all existing types.

#### B3 — Consolidate test projects

Create `FDP/ModuleHost/Fdp.Network.Cyclone.Tests/Fdp.Network.Cyclone.Tests.csproj`
absorbing `ModuleHost.Network.Cyclone.Tests`.
Move all test files. The new test project references `Fdp.Network.Cyclone`.

#### B4 — Update project references

Search entire repo for references to `ModuleHost.Network.Cyclone.csproj`.
Replace all with a reference to `Fdp.Network.Cyclone.csproj`.

Affected projects include: `Hrot.NED`, `Hrot.Network`, `Hrot.ClusterRunner` (and their
test counterparts).

#### B5 — Update both solution files

Remove `ModuleHost.Network.Cyclone` and `ModuleHost.Network.Cyclone.Tests`.
Add `Fdp.Network.Cyclone` and `Fdp.Network.Cyclone.Tests`.

---

## Mandatory Workflow: Test-Driven Task Progression

**Before starting:**
```
dotnet build IOS-IG-SimHost.sln
```

**After each task section (A and B):**
```
dotnet build IOS-IG-SimHost.sln
```

**Final verification:**
```
dotnet build IOS-IG-SimHost.sln
dotnet test FDP/Framework/Fdp.Presentation.Tests/Fdp.Presentation.Tests.csproj
dotnet test FDP/ModuleHost/Fdp.Network.Cyclone.Tests/Fdp.Network.Cyclone.Tests.csproj
dotnet test IOS-IG-SimHost.sln
```

---

## Testing Requirements

- All existing Vis2D, ImGui, and Framework.Raylib tests must pass in `Fdp.Presentation.Tests`.
- All existing `ModuleHost.Network.Cyclone.Tests` tests must pass in `Fdp.Network.Cyclone.Tests`.
- Build output must NOT contain any of the old individual DLLs as project outputs.
- `Fdp.Network.Cyclone.csproj` has ZERO project references to any `Hrot.*` project.

---

## Report Requirements

Submit `.dev/modular-2/reports/BATCH-03-REPORT.md` covering:

1. **What was done:** Summary of merges, file moves.
2. **Issues encountered:** Any dependency conflicts or embedded resource issues.
3. **Weak points spotted:** Code quality observations.
4. **Design decisions beyond spec:** Any deviations.
5. **Test results:** Output of final test runs.
6. **Files changed list:** All modified `.csproj` and solution files.
