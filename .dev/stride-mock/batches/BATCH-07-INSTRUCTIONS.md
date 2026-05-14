# BATCH-07 Instructions: SM-011 Integration Validation Gate

## Context

**Workstream:** `.dev/stride-mock`
**Batch number:** BATCH-07
**Task:** SM-011 — Full Integration Validation Gate

**Previous batches committed:** BATCH-01 through BATCH-06 (commit `d45ae4a`).

**Repository root:** `D:\Work\IOS-IG-SimHost-FDP-2\`

---

## Task Reference

**SM-011 location in TASK-DETAILS.md:** `.dev/stride-mock/TASK-DETAILS.md` — section `SM-011`
**Design reference:** `.dev/stride-mock/DESIGN.md` §12 "Success Conditions"
**Task tracker:** `.dev/stride-mock/TASK-TRACKER.md`

---

## Scope

SM-011 is the final integration validation gate for the stride-mock workstream. It consists of
two kinds of checks:
- **Static checks** — verifiable by code analysis and test runs (developer can do these)
- **Runtime checks** — require a live running cluster (out of scope for this batch; document status)

Your job is to perform ALL static checks, document their results, and update the task tracker.

---

## Static Checks to Perform

### Check 1 — DRY: StrideNodeBootstrapper isolation

Verify that `Hrot\Subsystems\Hrot.StrideMock\StrideNodeBootstrapper.cs` contains
**zero references** (in C# code — not in comments or string literals) to:
- `Raylib` or any `Raylib_cs` type
- `ImGui` or any ImGui type
- `IMapCameraProvider`

Use grep or code search. Only comment lines (starting with `//` or `///`) and string literals
in documentation attributes are acceptable. References in actual C# statements or imports
are violations.

**Expected result:** 0 violations.
**If violations found:** Do NOT fix them in this batch. Document them as defects.

### Check 2 — Both entry points use StrideNodeBootstrapper

Verify that BOTH of these entry points instantiate `StrideNodeBootstrapper`:
1. `Hrot\Subsystems\Hrot.StrideMock\StrideMockSubsystem.cs` — must contain
   `new StrideNodeBootstrapper(` or a factory/helper that creates one
2. `Hrot\Runner\Hrot.FakeStrideApp\FakeStrideApp.cs` — must contain
   `new StrideNodeBootstrapper(` or equivalent

Use grep to find `StrideNodeBootstrapper` in each file.

**Expected result:** Both files reference `StrideNodeBootstrapper`.

### Check 3 — SM-009 + SM-010: All SimHost and IG tests at baseline

Build and run the following test projects and document pass/fail counts:

```
Hrot\Subsystems\Hrot.SimHost.Tests\Hrot.SimHost.Tests.csproj
Hrot\Subsystems\Hrot.IG.Tests\Hrot.IG.Tests.csproj
Hrot\Subsystems\Hrot.StrideMock\Hrot.StrideMock.Tests\Hrot.StrideMock.Tests.csproj
```

**Expected baselines (pre-existing failures are acceptable):**
- `Hrot.SimHost.Tests`: ~566 pass / ~27 fail (pre-existing failures)
- `Hrot.IG.Tests`: ~319 pass / ~68 fail (pre-existing failures)
- `Hrot.StrideMock.Tests`: 41 pass / 0 fail

Any count BELOW the baseline (new failures) is a regression — document it.

### Check 4 — SM-002: SharedApplicationBootstrapper has all four concrete subclasses

Verify that `SharedApplicationBootstrapper` is subclassed by exactly these four concrete types:
1. `StrideNodeBootstrapper` (in `Hrot.StrideMock`)
2. `SimHostNodeBootstrapper` (in `Hrot.SimHost`)
3. `StrideNodeBootstrapper` — duplicate? No. Check: `SimHostNodeBootstrapper` vs `NodeBootstrapper`
4. `IgNodeBootstrapper` (in `Hrot.IG`)

Use grep for `: SharedApplicationBootstrapper` to find all subclasses.

**Expected result:** Exactly `StrideNodeBootstrapper`, `SimHostNodeBootstrapper`, `IgNodeBootstrapper`
(and the test stub in `SharedApplicationBootstrapperTests.cs`).

### Check 5 — SharedApplicationBootstrapper phase ordering invariant

Verify in `Hrot\Engine\Hrot.Common\Infrastructure\SharedApplicationBootstrapper.cs` that:
- `context.Kernel.Initialize()` is the LAST call in `BootstrapNode()`
- `RegisterApplicationSystems(context)` (Phase 6d) is called BEFORE `context.Kernel.Initialize()`
- Phase 6a+ NedReplication registration happens BEFORE Phase 6b `RegisterNetworkTranslators`

Use `read_file` to confirm the phase ordering in the `BootstrapNode()` method body.

**Expected result:** Phase ordering as designed.

---

## Runtime Checks (document as deferred)

The following SM-011 checklist items require a live running cluster. You CANNOT verify them
statically. Document each as "DEFERRED — requires live cluster" in your report:

1. `SC_SM006_7 / SC_SM007_7`: `[StrideMock]` tab visible; camera sync on tab switch works
2. `SC_SM007_6`: Standalone mode boots cleanly
3. `SC_SM008_1-SC_SM008_7`: FakeStrideApp visual + lifecycle verification
4. Replay safety: load a recording, seek backward — no ghost entities
5. Recording: `OperatingLive` session produces `node_700.fdp` in the staging directory
6. 2PC: `SerializeLocal` and `PrefetchFiles` commands ACKed correctly
7. Diagnostics: `CollectDiagnostics` produces a valid dump from node 700
8. Time: Orchestrator Pause command halts all nodes on same tick

---

## Deliverables

### 1. BATCH-07-REPORT.md

Create `.dev/stride-mock/reports/BATCH-07-REPORT.md` with:
- Status (COMPLETE or PARTIAL)
- Results of all 5 static checks (PASS/FAIL/DEVIATION for each)
- Test suite results table (baseline vs actual)
- List of deferred runtime items

### 2. Updated TASK-TRACKER.md

In `.dev/stride-mock/TASK-TRACKER.md`, mark SM-011 as `[x]` complete since all
implementation work and statically-verifiable items are done.

The SM-011 completion note should mention that runtime cluster tests (items 1-8 above)
are deferred and require a live cluster environment.

---

## Build Commands

```
# Build key projects (avoid --no-incremental on the full solution due to CycloneDDS codegen)
dotnet build "Hrot\Subsystems\Hrot.StrideMock\Hrot.StrideMock.Tests\Hrot.StrideMock.Tests.csproj"
dotnet build "Hrot\Subsystems\Hrot.SimHost.Tests\Hrot.SimHost.Tests.csproj"
dotnet build "Hrot\Subsystems\Hrot.IG.Tests\Hrot.IG.Tests.csproj"

# Run tests (after build)
dotnet test "Hrot\Subsystems\Hrot.StrideMock\Hrot.StrideMock.Tests\Hrot.StrideMock.Tests.csproj" --no-build -q
dotnet test "Hrot\Subsystems\Hrot.SimHost.Tests\Hrot.SimHost.Tests.csproj" --no-build -q
dotnet test "Hrot\Subsystems\Hrot.IG.Tests\Hrot.IG.Tests.csproj" --no-build -q
```

Note: The full solution build (`IOS-IG-SimHost.sln`) may show CycloneDDS code-gen errors
on clean builds — this is a pre-existing infrastructure issue unrelated to this workstream.
Use individual project builds instead.

---

## AGENTS.md Invariants (mandatory)

- Do NOT use Unicode characters in comments or string literals
- Preserve existing comments exactly
- Minimize textual diffs
- Solution must compile before finishing
