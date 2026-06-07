# BATCH-01: Phase 0 Foundations — Assembly Policy, KinematicsMode, INavmeshProvider

**Batch Number:** BATCH-01
**Tasks:** NAV-P0-T1, NAV-P0-T2, NAV-P0-T3
**Phase:** Phase 0 — Foundations, contracts & corrective migration
**Estimated Effort:** 12-16 hours
**Priority:** HIGH (blockers for all later phases)
**Dependencies:** none

---

## Onboarding & Workflow

### Developer Instructions

This is the first batch for the Navigation Subsystem v2. It establishes the
foundation on which all subsequent phases are built: the assembly placement
policy, the corrected `KinematicsMode` enum, and the redefined `INavmeshProvider`
interface with its migrated EQS callers.

Do NOT stop after each task to ask if it is OK to continue. Implement each
task fully, write tests, fix all failures, and only then move to the next task.
Run `dotnet build IOS-IG-SimHost.sln` and the test suite after each task and
fix all errors and failures before proceeding. Do not submit the report until
all tests pass.

### Required Reading (in order)

1. **Workflow guide:** `.dev/.guides/DEV-GUIDE.md`
2. **Code standards:** `.dev/.guides/CODE-STANDARDS.md`
3. **Task definitions:** `.dev/navig-2/TASK-DETAILS.md` — Section "Phase 0" and the "Verified codebase facts" preamble (DSC-1 through DSC-6)
4. **Design overview:** `.dev/navig-2/Navigation_Design_v2_0.md` — §2 (topology), §7.1 (KinematicsMode), §8.1 and §8.4 (INavmeshProvider)
5. **DD-Fake-Nav:** `.dev/navig-2/DD-Fake-Nav.md` — §12 (ComponentId allocation) — needed for T1 placement context
6. **DD-Tests-Nav:** `.dev/navig-2/DD-Tests-Nav.md` — §2.1 (assembly placement) and §3 (layer-1 test locations)

### Source Code Locations

- **Primary nav source:** `FDP/Toolkits/Fdp.Toolkits/Navigation/` and `FDP/Toolkits/Fdp.Toolkits/CarKinem/Core/`
- **EQS source:** `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/`
- **Test project:** `FDP/Toolkits/Fdp.Toolkits.Tests/` (navigation tests in `Navigation/`, EQS tests in `Eqs/`)
- **Integration EQS tests:** `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/`
- **GlobalComponentIds:** `FDP/Engine/Fdp.Core/GlobalComponentIds.cs`
- **NavigationContractsComponentIds:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationContractsComponentIds.cs`

### Build & Test Commands

```powershell
# Build the entire solution
dotnet build IOS-IG-SimHost.sln

# Run all tests (quick smoke check)
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj -v quiet

# Run EQS integration tests
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj --filter "Eqs" -v quiet
```

### Report Submission

**When done, submit:** `.dev/navig-2/reports/BATCH-01-REPORT.md`

**If you have questions:** `.dev/navig-2/questions/BATCH-01-QUESTIONS.md`

---

## Context

TASK-DETAILS.md §"Verified codebase facts" documents six discrepancies (DSC-1 through DSC-6)
between the design documents and the actual codebase. This batch corrects the three that are
foundational blockers:

- **DSC-5** (T1): Design says new code goes in `Hrot.Navigation.*` assemblies — reality is it
  must go in the existing `Fdp.Toolkits` assembly to avoid circular dependencies.
- **DSC-2** (T2): Design proposes `Crowd=4` which collides with existing `Direct=4` in `KinematicsMode`.
- **DSC-1** (T3): Design says "amend `INavmeshProvider` in place" but the actual interface has
  entirely different method names and signatures — it requires a full redefinition.

---

## Tasks

### Task 1: Assembly Placement Policy (NAV-P0-T1)

**Task Definition:** [TASK-DETAILS.md#nav-p0-t1](./../TASK-DETAILS.md#nav-p0-t1)

This is a verification and documentation task. The policy is: **all new navigation production
code goes in the existing `Fdp.Toolkits` assembly** under these namespaces:

- `Fdp.Toolkit.Navigation` — provider interfaces, new components, new systems
- `Fdp.Toolkit.Navigation.Fake` — fake providers + test map (DD-Fake-Nav)
- `Fdp.Toolkit.Navigation.EngineBacked` — engine-backed providers + module (DD-EngineBacked-Nav)

UI/editor code (ImGui windows, gizmos) stays in the existing editor assemblies
(`Hrot.Editor.AiShared` / `Hrot.Editor`) since `Fdp.Toolkits` must remain UI-free.

Tests go in the existing `FDP/Toolkits/Fdp.Toolkits.Tests/` project.

**Deliverable:** No new `.csproj` files. Verify `dotnet build IOS-IG-SimHost.sln` still passes
and document the namespace plan as a `// NAV-P0-T1` comment block at the top of any new namespace
umbrella file you create (e.g., a `NavigationContracts.cs` if you introduce a namespace organizer).

**Success conditions:** See TASK-DETAILS.md#nav-p0-t1.

---

### Task 2: KinematicsMode Extension (NAV-P0-T2)

**Task Definition:** [TASK-DETAILS.md#nav-p0-t2](./../TASK-DETAILS.md#nav-p0-t2)

**File:** `FDP/Toolkits/Fdp.Toolkits/CarKinem/Core/NavigationEnums.cs` (UPDATE)

**Current enum values:**
```
None=0, RoadGraph=1, CustomTrajectory=2, Formation=3, Direct=4
```

**Required:** Add `Crowd=5`, `Naval=6`, `Flying=7` (the design erroneously proposed Crowd=4,
which collides with Direct=4 — use next free values per DSC-2).

Also update all `switch` statements and comparisons that reference `KinematicsMode` to handle
the new values (defaulting safely for now). Files to check:
- `FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/NavigationIntentBridgeSystem.cs`
- `FDP/Toolkits/Fdp.Toolkits/CarKinem/Core/CarKinematicsSystem.cs` (search the project)
- `FDP/Toolkits/Fdp.Toolkits/CarKinem/Core/LinearKinematicsSystem.cs` (search the project)
- `FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/NavigationExecutionSystem.cs` (if it references KinematicsMode)

Add an inline comment on each new member documenting the design's corrected mapping:
`// Design's DirectPoint == existing Direct=4. Crowd/Naval/Flying start at 5.`

**Tests Required (in `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/`):**

Add to a new file `NavigationEnumsTests.cs` (or extend `NavigationContractsTests.cs`):
- `KinematicsMode_ExistingValues_Unchanged` — asserts `None=0`, `RoadGraph=1`, `CustomTrajectory=2`,
  `Formation=3`, `Direct=4`.
- `KinematicsMode_NewValues_NotColliding` — asserts `Crowd=5`, `Naval=6`, `Flying=7` and that all
  enum values are distinct.

**Success conditions:** See TASK-DETAILS.md#nav-p0-t2.

---

### Task 3: Redefine INavmeshProvider + Migrate EQS Callers (NAV-P0-T3)

**Task Definition:** [TASK-DETAILS.md#nav-p0-t3](./../TASK-DETAILS.md#nav-p0-t3)

**Design reference:** Navigation_Design_v2_0.md §8.1 (interface definition), §8.4 (EQS integration)

This is the largest task in the batch. The current `INavmeshProvider`
(`FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/INavmeshProvider.cs`) has three methods:
`IsReachable`, `TryGetPathDistance`, `GetRandomPointsInRadius`.

Replace the entire interface with the design's layer-aware surface. The new interface lives in
`Fdp.Toolkit.Navigation` namespace (not `Fdp.Toolkit.Spatial.Eqs`) since it is now a core
navigation contract. The file can be relocated to
`FDP/Toolkits/Fdp.Toolkits/Navigation/INavmeshProvider.cs`.

#### New INavmeshProvider contract (§8.1)

```csharp
namespace Fdp.Toolkit.Navigation
{
    [ComponentId(GlobalComponentIds.INavmeshProvider)]
    public interface INavmeshProvider
    {
        // Returns true if the 3D position projects onto walkable navmesh within
        // tolerance, on any layer included in layerMask.
        bool IsWalkable(Vector3 position, uint layerMask = 0xFFFFFFFF);

        // Projects position onto the nearest walkable navmesh polygon.
        // Returns true and writes the snapped position; false if no polygon found.
        bool ProjectToNavmesh(Vector3 position, out Vector3 snapped,
                              uint layerMask = 0xFFFFFFFF);

        // Samples up to results.Length random walkable points within radius of center.
        // Returns the number of points written.
        int SampleNavmeshPoints(Vector3 center, float radius,
                                Span<Vector3> results, uint layerMask = 0xFFFFFFFF);

        // Returns true if a path exists between from and to on the given layers.
        bool PathExists(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF);

        // Returns the navmesh path cost (arc-length in metres) or float.MaxValue
        // if unreachable.
        float PathCost(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF);

        // Returns a monotonically-increasing version counter.
        // Callers can cache paths and invalidate them when this changes.
        uint QueryVersion();

        // Convenience: plan a sequence of NavWaypoints from from to to.
        // Returns the number of waypoints written (0 = unreachable).
        int PlanPath(Vector3 from, Vector3 to, Span<NavWaypoint> waypoints,
                     uint layerMask = 0xFFFFFFFF);
    }
}
```

Note: `NavWaypoint` is introduced in NAV-P0-T5 (next batch). For this task, declare a minimal
forward stub if needed so the interface compiles:

```csharp
// Temporary stub — full definition in NAV-P0-T5
public readonly struct NavWaypoint
{
    public System.Numerics.Vector3 Position { get; init; }
}
```

Place it in `FDP/Toolkits/Fdp.Toolkits/Navigation/NavWaypoint.cs` clearly commented
`// Stub — will be replaced by NAV-P0-T5`.

#### Migrate StubNavmeshProvider

`FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/StubNavmeshProvider.cs` — reimplement against the
new interface. Keep it in the same file and namespace for now. Euclidean defaults:
- `IsWalkable` → always `true`
- `ProjectToNavmesh` → snapped = position, return `true`
- `SampleNavmeshPoints` → generate a small grid as before (use `Vector3`)
- `PathExists` → always `true`
- `PathCost` → `Vector3.Distance(from, to)` (ignoring Y for flat-earth consistency)
- `QueryVersion` → always returns `1`
- `PlanPath` → returns `[from_waypoint, to_waypoint]` (2 waypoints) if span ≥ 2, else 0

#### Migrate EQS callers

Map old methods to new:
- `IsReachable(from2D, to2D)` → `PathExists(new Vector3(from2D.X, 0, from2D.Y), new Vector3(to2D.X, 0, to2D.Y))`
- `TryGetPathDistance(from2D, to2D, out dist)` → `dist = PathCost(...); return dist != float.MaxValue`
- `GetRandomPointsInRadius(center2D, radius, Span<Vector2>)` → `SampleNavmeshPoints(new Vector3(center2D.X, 0, center2D.Y), radius, tempSpan3D)` then extract XZ into Vector2

Files to update:
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/NavmeshReachableTest.cs`
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/PathCostScoreTest.cs`
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/NavmeshSamplesGenerator.cs`

Also update `using` directives in these files to reference `Fdp.Toolkit.Navigation`.

#### Update the per-entity layerMask default

Per §8.3 (NAV-P0-T5 adds `NavAgentProfile.PreferredLayerMask`), the EQS tests for now supply
`layerMask: 0xFFFFFFFF` (all layers) as a safe default. Leave a `// TODO NAV-P0-T5: use
NavAgentProfile.PreferredLayerMask from ctx.Self` comment at each call site.

**Tests Required:**

The following existing tests must continue to pass after migration:
- `FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/NavmeshProviderTests.cs` — update to call new interface
- `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsRoundTripTests.cs`
- `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/PathCostInversionTests.cs`
- `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/AccurateLosPhaseTests.cs`

Update any failing tests to use the new interface. Write two new unit tests in
`FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/NavmeshProviderTests.cs` (or a new
`NewINavmeshProviderTests.cs`):
- `StubNavmeshProvider_PathCost_ReturnsEuclideanDistance` — verify PathCost(A, B) ~= distance(A, B)
- `StubNavmeshProvider_PlanPath_ReturnsTwoWaypoints` — verify PlanPath writes start+end waypoints

**Success conditions:** See TASK-DETAILS.md#nav-p0-t3.

---

## Mandatory Workflow

Complete tasks **in sequence**: T1 → T2 → T3. After each task:
1. Run `dotnet build IOS-IG-SimHost.sln` — fix all errors.
2. Run the relevant test project — fix all failures.
3. Only then start the next task.

Do NOT stop to ask permission before running tests or fixing failures. Fix
all root causes yourself and proceed.

---

## Quality Standards

**Code:**
- All new public types need XML doc comments.
- No warnings introduced (treat warnings-as-errors per the existing project config).
- Match existing code style (see `CODE-STANDARDS.md`).

**Tests:**
- Tests must verify actual behavior, not just compilation.
- Enum value assertions must use `Assert.Equal((int)KinematicsMode.Direct, 4)` style.
- No `Assert.NotNull(new SomeClass())` style tests.
- EQS migration tests must exercise the full EQS pipeline with the new stub, not just
  call the interface methods directly.

---

## Success Criteria

- [ ] NAV-P0-T1: No new production `.csproj` created; namespace plan documented; build passes.
- [ ] NAV-P0-T2: `Crowd=5`, `Naval=6`, `Flying=7` added; no value collisions; existing tests green; two enum value tests added.
- [ ] NAV-P0-T3: New `INavmeshProvider` compiles; all EQS callers migrated; named EQS tests pass.
- [ ] `dotnet build IOS-IG-SimHost.sln` exits 0.
- [ ] `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/` exits 0.
- [ ] EQS-relevant integration tests pass.
- [ ] Report submitted to `.dev/navig-2/reports/BATCH-01-REPORT.md`.

---

## Common Pitfalls

- Do NOT create `Hrot.Navigation.*` assemblies — all production code in `Fdp.Toolkits`.
- The old `INavmeshProvider.IsReachable` is a bool; the new `PathExists` is also a bool — easy
  to confuse with `PathCost`. Map carefully.
- `StubNavmeshProvider` must compile and pass; do not break the existing EQS path.
- `NavWaypoint` stub must be placed in `Fdp.Toolkit.Navigation` namespace, not `Spatial.Eqs`.
- After renaming/moving the interface, check ALL files in the solution that import
  `Fdp.Toolkit.Spatial.Eqs` and reference `INavmeshProvider` — use `grep` or IDE search.

---

## Reference Materials

- **Task definitions:** `.dev/navig-2/TASK-DETAILS.md` — NAV-P0-T1, NAV-P0-T2, NAV-P0-T3
- **Design §8.1:** `.dev/navig-2/Navigation_Design_v2_0.md`
- **DD-Fake-Nav §12:** `.dev/navig-2/DD-Fake-Nav.md`
- **GlobalComponentIds:** `FDP/Engine/Fdp.Core/GlobalComponentIds.cs`
- **NavContractsComponentIds:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationContractsComponentIds.cs`
- **Existing INavmeshProvider:** `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/INavmeshProvider.cs`
- **KinematicsMode:** `FDP/Toolkits/Fdp.Toolkits/CarKinem/Core/NavigationEnums.cs`
