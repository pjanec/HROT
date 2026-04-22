# BATCH-01: Create Fdp.Core (FDP Layer Foundation)

**Batch Number:** BATCH-01
**Tasks:** TASK-P1-001
**Phase:** Phase 1 — FDP Layer Consolidation
**Estimated Effort:** 8–12 hours
**Priority:** HIGH
**Dependencies:** None — this is the first batch

---

## Onboarding & Workflow

### Developer Instructions

This batch consolidates the three foundational FDP kernel assemblies (`Fdp.Kernel`,
`FDP.Interfaces`, `ModuleHost.Core`) into a single `Fdp.Core` assembly.  
This is a **pure project consolidation** — no source logic changes, no namespace renames.

### Required Reading (IN ORDER)

1. **Task Definition:** `.dev/modular-2/TASK-DETAIL.md#task-p1-001-create-fdpcore`
2. **Design Document:** `.dev/modular-2/DESIGN.md` — Section "FDP Layer (4 assemblies)" and Phase 1.1
3. **ONBOARDING:** `.dev/modular-2/ONBOARDING.md`

### Source Code Locations

- **`Fdp.Kernel`:** `FDP/Kernel/Fdp.Kernel/` (and `Fdp.Kernel.Tests/`)
- **`FDP.Interfaces`:** `FDP/Common/FDP.Interfaces/`
- **`ModuleHost.Core`:** `FDP/ModuleHost/ModuleHost.Core/` (and `ModuleHost.Core.Tests/`)
- **Target new project:** `FDP/Kernel/Fdp.Core/Fdp.Core.csproj`
- **FDP solution file:** `FDP/FDP.sln`
- **Top-level solution file:** `IOS-IG-SimHost.sln`

### Report Submission

When done, submit your report to: `.dev/modular-2/reports/BATCH-01-REPORT.md`

If you have questions, create: `.dev/modular-2/questions/BATCH-01-QUESTIONS.md`

---

## Context

The FDP layer currently has 20+ fragmented assemblies. Phase 1 consolidates them into 4.
This batch creates `Fdp.Core`, the foundation on which all other FDP and Hrot assemblies
rest. It absorbs `Fdp.Kernel` (the ECS engine), `FDP.Interfaces` (thin interface shims),
and `ModuleHost.Core` (orchestration host kernel).

**Why merge these three?**
- They are always deployed together — zero utility in keeping them separate.
- `ModuleHost.Core` has no public API surface outside `Fdp.Core`'s own consumers.
- `FDP.Interfaces` is a pure pass-through library; merging it eliminates an indirection.

---

## Batch Objectives

1. Create `FDP/Kernel/Fdp.Core/Fdp.Core.csproj` containing all source from the three projects.
2. Update every `<ProjectReference>` in the entire repository that targets the three old
   projects.
3. Delete the three old `.csproj` files and remove their entries from both solution files.
4. Ensure the solution builds and all existing tests pass.

---

## Tasks

### Task 1: Create Fdp.Core.csproj

**File:** `FDP/Kernel/Fdp.Core/Fdp.Core.csproj` (NEW)

Create the new project file. The new project must:
- Target `net8.0`
- Enable `ImplicitUsings`, `Nullable`, `AllowUnsafeBlocks`
- Set `LangVersion` to `12.0`
- Include the `FDP_PARANOID_MODE` conditional for Debug builds
- Carry all NuGet packages from all three merged projects:
  - `MessagePack` 3.1.4
  - `K4os.Compression.LZ4` 1.3.8
  - `NLog` 5.2.8
- Carry all `InternalsVisibleTo` entries from all three merged projects:
  - `Fdp.Tests` (from Fdp.Kernel)
  - `ModuleHost.Core` (from Fdp.Kernel — obsolete after merge, but add `Fdp.Core.Tests`
    as the canonical test target instead; keep `Fdp.Tests` for backwards compat with
    existing test project name)
  - Any other `InternalsVisibleTo` from `ModuleHost.Core` or `FDP.Interfaces`
- Suppress warnings already suppressed in any of the three merged projects

### Task 2: Move source files

Move (copy+delete) all `.cs` files from the three source directories into `Fdp.Core/`:

- `FDP/Kernel/Fdp.Kernel/*.cs` and subdirectories → `FDP/Kernel/Fdp.Core/` (maintain
  subfolder structure where it exists, e.g. `Collections/`, `FlightRecorder/`, etc.)
- `FDP/Common/FDP.Interfaces/Abstractions/*.cs` → `FDP/Kernel/Fdp.Core/Abstractions/`
  (if files don't already exist there from the Fdp.Kernel copy)
- `FDP/ModuleHost/ModuleHost.Core/*.cs` and subdirectories → `FDP/Kernel/Fdp.Core/`
  (maintain subfolder structure)

**Important:** If there are duplicate filenames or conflicting content between the three
projects, resolve by keeping the most complete version and ensuring no two files define
the same type in the same namespace.

Note: `FDP/Kernel/Fdp.Kernel/`, `FDP/Common/FDP.Interfaces/`, and
`FDP/ModuleHost/ModuleHost.Core/` source directories should be **deleted** after the
move (leave only the old empty `.csproj` shells temporarily for the solution update step).

### Task 3: Update all project references

Search the entire repository for `<ProjectReference` entries that point to any of:
- `Fdp.Kernel.csproj`
- `FDP.Interfaces.csproj`
- `ModuleHost.Core.csproj`

Replace all such references with a single reference to `Fdp.Core.csproj` (relative path
calculation depends on the referencing project location).

**Affected projects include at minimum:**
- `FDP/Kernel/Fdp.Kernel.Tests/Fdp.Tests.csproj` → update ref to Fdp.Core
- `FDP/ModuleHost/ModuleHost.Core.Tests/ModuleHost.Core.Tests.csproj` → update ref to Fdp.Core
- All Toolkit projects (`FDP.Toolkit.*`) that reference Fdp.Kernel or FDP.Interfaces
- `FDP/Framework/FDP.Framework.Runner/FDP.Framework.Runner.csproj`
- Any Hrot.* project that references Fdp.Kernel, FDP.Interfaces, or ModuleHost.Core
- Top-level solution referencing projects

Run `grep -r "Fdp.Kernel\|FDP.Interfaces\|ModuleHost.Core" --include="*.csproj" .` from
the workspace root to get the full list.

### Task 4: Update both solution files

**`FDP/FDP.sln`:**
- Remove solution entries for `Fdp.Kernel`, `FDP.Interfaces`, `ModuleHost.Core`
  (and their test counterparts `Fdp.Tests`, `ModuleHost.Core.Tests` if they
  become merged — see Task 5 note)
- Add a new solution entry for `Fdp.Core`

**`IOS-IG-SimHost.sln`:**
- Remove solution entries for the same three projects (if they appear here)
- Add a new solution entry for `Fdp.Core`

### Task 5: Decide on test project consolidation

The instructions say "when merging assemblies into a new one, do the same with the test
assemblies."

- Merge `Fdp.Kernel.Tests` (csproj: `Fdp.Tests`) and `ModuleHost.Core.Tests` into a
  single `FDP/Kernel/Fdp.Core.Tests/Fdp.Core.Tests.csproj`.
- Move all `.cs` test files into the new test project folder.
- Update both solution files to remove old test projects and add `Fdp.Core.Tests`.
- `FDP.Interfaces` has no separate test project — nothing to merge there.

The new test project must reference `Fdp.Core` (not the individual old projects).

### Task 6: Delete old project files

After updating all references and solution files, delete:
- `FDP/Kernel/Fdp.Kernel/Fdp.Kernel.csproj`
- `FDP/Common/FDP.Interfaces/FDP.Interfaces.csproj`
- `FDP/ModuleHost/ModuleHost.Core/ModuleHost.Core.csproj`
- `FDP/Kernel/Fdp.Kernel.Tests/Fdp.Tests.csproj`
- `FDP/ModuleHost/ModuleHost.Core.Tests/ModuleHost.Core.Tests.csproj`

---

## Mandatory Workflow: Test-Driven Task Progression

**Before starting any task:**
1. Run `dotnet build FDP/FDP.sln` to confirm the current baseline builds cleanly.
2. Run `dotnet test FDP/FDP.sln --filter "FullyQualifiedName~Fdp|FullyQualifiedName~ModuleHost"` to confirm existing tests pass.

**After completing each task step:**
1. Run `dotnet build FDP/FDP.sln` — fix all errors before proceeding.
2. Run `dotnet build IOS-IG-SimHost.sln` — fix cross-solution errors.
3. Never leave the build broken between steps.

**Final verification:**
```
dotnet build IOS-IG-SimHost.sln
dotnet test FDP/FDP.sln
dotnet test IOS-IG-SimHost.sln
```
All must pass with zero errors before submitting the report.

---

## Testing Requirements

- All tests that existed before this batch must still pass after it.
- Specifically: `Fdp.Core.Tests` must run all tests previously in `Fdp.Tests` AND all
  tests previously in `ModuleHost.Core.Tests`.
- No test logic may be modified — only project structure changes.

---

## Report Requirements

Submit `.dev/modular-2/reports/BATCH-01-REPORT.md` covering:

1. **What was done:** Brief summary of file moves and project reference updates.
2. **Issues encountered:** Any namespace conflicts, duplicate file names, unexpected
   dependencies found.
3. **Weak points spotted:** Any code quality issues noticed (but not fixed) during the
   work. These will become tech debt entries.
4. **Design decisions made beyond spec:** Any deviation from these instructions and why.
5. **Test results:** Output of final `dotnet test` run (pass/fail counts).
6. **Files changed list:** List of all `.csproj` and solution files modified or deleted.
