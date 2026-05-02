# BATCH-01: DRY Infrastructure + NedReplicationModule (Phases 1 & 2)

**Batch Number:** BATCH-01
**Tasks:** EAM-I001, EAM-I002, EAM-N001, EAM-N002
**Phase:** Phase 1 (DRY Initialization Infrastructure) + Phase 2 (NedReplicationModule)
**Estimated Effort:** 16–22 hours
**Priority:** CRITICAL — foundational; all later phases depend on this batch
**Dependencies:** None (first batch)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This is the first batch of the `eyes-and-muscle` workstream. You will build the foundational DRY
initialization infrastructure and the NedReplicationModule that all subsequent phases rely on.

The goal of this batch:
1. **EAM-I001 & EAM-I002** (Phase 1): Create `HrotNodeBuilder`, `HrotNodeContext`, and extract
   `EnsureIdAllocatorRouting` so any new Hrot node can bootstrap in a few lines instead of ~300.
2. **EAM-N001 & EAM-N002** (Phase 2): Create `NedReplicationModule` — a single `IEcsModule` that
   bundles NED translators with the ECS systems they are architecturally coupled to (DR/smoothing,
   ghost lifecycle).

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md` — How to work with batches
2. **Onboarding:** `.dev/eyes-and-muscle/ONBOARDING.md` — Project context overview
3. **Design Document:** `.dev/eyes-and-muscle/DESIGN.md` — Full architectural rationale (read
   §Phase 1 and §Phase 2 carefully; §Phase 3 and §Phase 4 are context only)
4. **Task Definitions:** `.dev/eyes-and-muscle/TASK-DETAIL.md` — Per-task specs for EAM-I001,
   EAM-I002, EAM-N001, EAM-N002

### Key Source Files to Understand Before Coding

| File | Why it matters |
|---|---|
| `Hrot.SimHost/SimHostApp.cs` `OnLoad()` | The monolithic init you're extracting INTO `HrotNodeBuilder` (study steps 1–8a) |
| `Hrot.SimHost/NodeBootstrapper.cs` `BuildOrchestration()` | The ClusterSlave + handler wiring you must INLINE (not call) in `HrotNodeBuilder` |
| `Hrot.SimHost/NodeBootstrapper.cs` `BuildTranslators()` | How translator packs are selected by role |
| `Hrot.SimHost/Network/KinematicTranslatorPack.cs` | `public static class` with `Create(...)` factory — Muscle-side translators |
| `Hrot.SimHost/Network/SharedTranslatorPack.cs` | `public static class` with `Create(...)` factory — entity lifecycle translators (all roles) |
| `Hrot.SimHost/Network/CognitiveTranslatorPack.cs` | `public static class` with `Create(...)` factory — Brain-side translators |
| `Hrot.Map.Common/Translators/EntityStatesIngressPack.cs` | `public class : IEcsModule` — IG-side ingress pack; constructor takes `PackRole.Ingress` |
| `Hrot.IG/Systems/DeadReckoningSyncSystem.cs` | `public class` in PostSimulation phase; needs `driveFromNetwork` flag |
| `FDP/Toolkits/FDP.Toolkit.Replication/Systems/SmartEgressSystem.cs` | `public class` — suppresses redundant egress |
| `FDP/Toolkits/FDP.Toolkit.Replication/Systems/DisposalMonitoringSystem.cs` | `public class` — cleans up NetworkEntityMap |
| `FDP/ModuleHost/ModuleHost.Network.Cyclone/Systems/CycloneNetworkCleanupSystem.cs` | `public class` — fires DDS Dispose on entity death |
| `FDP/ModuleHost/ModuleHost.Network.Cyclone/Modules/CycloneNetworkModule.cs` | Study `RegisterSystems` — it creates `CycloneNetworkIngressSystem` + `CycloneEgressSystem`; NedReplicationModule uses the same approach by calling `RegisterSystems` on inline sub-modules |
| `Hrot.ClusterRunner.Integration.Tests/HrotRunnerHarness.cs` | Existing harness pattern for headless integration tests |

### Report Submission

**When done, submit your report to:**
`.dev/eyes-and-muscle/reports/BATCH-01-REPORT.md`

**If you have questions, create:**
`.dev/eyes-and-muscle/questions/BATCH-01-QUESTIONS.md`

---

## Context

`SimHostApp.OnLoad` currently contains ~300 lines of bootstrap boilerplate that is substantially
duplicated across `IgApplication` and `CgfApplication`. Every future subsystem (EyesAndMuscle,
future Stride nodes) would copy this again.

This batch extracts the shared infrastructure into two building blocks:
- **`HrotNodeBuilder`** — produces a fully wired `HrotNodeContext` in one `.Build()` call
- **`NedReplicationModule`** — bundles NED translators with their tightly-coupled ECS systems

These are foundational primitives. Phase 3 (EyesAndMuscle subsystem) and Phase 4 (migration of
SimHostApp / IgApplication / CgfSubsystem) consume them directly. Any mistake here propagates
everywhere — correctness over speed.

---

## ⚠️ Critical Technical Findings

These are things you MUST know before coding:

1. **Translator factory methods are named `Create`, not `Build`.** The TASK-DETAIL spec uses `Build`
   for translator pack factories, but the actual methods in the codebase are `Create`:
   - `KinematicTranslatorPack.Create(...)` — already `public static`
   - `SharedTranslatorPack.Create(...)` — already `public static`
   - `CognitiveTranslatorPack.Create(...)` — already `public static`
   EAM-N002 confirms these are already public; no renaming needed. Accept `Create` as satisfying
   the "has a public factory" requirement — do NOT create redundant `Build` wrappers.

2. **`HrotNodeBuilder` must NOT call `NodeBootstrapper.BuildOrchestration`.** It must wire
   `ClusterSlave` and `NodeOpSlaveTranslator` INLINE, registering only these four generic handlers:
   - `ReferencePreviewHandler`
   - `ReferencePrefetchHandler`
   - `ReferenceArchiveHandler`
   - `ReferenceLiveLoadHandler`
   Domain-specific handlers (scenario/replay/episode/checkpoint) are registered by the subsystem
   AFTER `Build()` returns. See DESIGN.md §Phase 1 and NodeBootstrapper.BuildOrchestration source.

3. **`EntityStatesIngressPack` is an `IEcsModule` (not a static factory).** Constructor signature:
   ```csharp
   new EntityStatesIngressPack(PackRole.Ingress, participant, entityMap, eventBus, ghostCreationSystem, geoTransform)
   ```
   In NedReplicationModule for `ImageGenerator` role: create the instance and call
   `entityStatesIngressPack.RegisterSystems(registry)` from within `NedReplicationModule.RegisterSystems`.
   This is the "inline module" composition pattern (same as how CycloneNetworkModule composes
   CycloneNetworkIngressSystem internally).

4. **`HrotNodeContext` needs to expose `GhostCreationSystem`** as a public property (or via
   `BaseModules` cast) so that Phase 4 migration of SimHostApp can pass it to
   `ReplayLoadClusterOpHandler`. The simplest approach: add `GhostCreationSystem GhostCreationSystem`
   as an additional field in HrotNodeContext, or expose it via NedReplicationModule. Either is
   acceptable — choose the approach that makes Phase 4 feasible and document your decision.

5. **`EnsureIdAllocatorRouting` logic:** The method blocks up to 30s waiting for a DDS
   publication match from the orchestrator. In headless/test environments without a running
   orchestrator, this will time out and throw. The shared helper must preserve this behaviour
   exactly. The builder calls it after creating the `DdsIdAllocator`.

6. **`DeadReckoningSyncSystem` lacks `driveFromNetwork` parameter — you must add it.**
   The current class has no constructor and no `driveFromNetwork` property. TASK-DETAIL SC3 requires
   this flag to distinguish "smooth all entities" (pure IG) vs "smooth only ghost entities"
   (combined Muscle+IG). You must modify `DeadReckoningSyncSystem`:
   - Add `public bool DriveFromNetwork { get; }` property (set via constructor parameter).
   - In `Execute`: when `DriveFromNetwork == false`, add a query filter to skip entities where
     `NetworkIdentity.IsGhost == false` (locally-owned entities must not be overwritten).
   - `DeadReckoningSyncSystem()` (no args) → `DriveFromNetwork = true` (backward compat default).
   - `DeadReckoningSyncSystem(bool driveFromNetwork)` → explicit constructor.
   This change is in `Hrot.IG/Systems/DeadReckoningSyncSystem.cs`.
   All existing tests using `DeadReckoningSyncSystem` must still pass (default true ≡ old behavior).

---

## 🎯 Batch Objectives

1. Create `HrotNodeContext` (immutable record) and `HrotNodeBuilder` in
   `Hrot.ClusterRunner/Infrastructure/` — replacing ~300 lines of SimHostApp boilerplate
   with a fluent 3-line call.
2. Extract `EnsureIdAllocatorRouting` to a shared static helper (no logic change).
3. Create `NedReplicationModule` in `Hrot.ClusterRunner/Replication/` — bundling translators
   and their coupled ECS systems behind a single `IEcsModule` boundary.
4. Confirm translator packs are publicly accessible from `Hrot.ClusterRunner` (EAM-N002).
5. All existing tests remain green.

---

## ✅ Tasks

### Task 1 — `HrotNodeContext` record (EAM-I001, part A)

**File to create:** `Hrot.ClusterRunner/Infrastructure/HrotNodeContext.cs`

**Task Definition:** See [TASK-DETAIL.md — EAM-I001](../TASK-DETAIL.md#eam-i001--fdpkernelbuilder-and-hrotnodecontext)

**Key Requirements:**
- Positional record; all fields `init`-only.
- Fields as specified in TASK-DETAIL: `EntityRepository World`, `ModuleHostKernel Kernel`,
  `DdsParticipant Participant`, `FdpEventBus EventBus`, `NetworkEntityMap EntityMap`,
  `ClusterSlave ClusterSlave`, `NodeOpSlaveTranslator? SlaveTranslator`,
  `IReadOnlyList<IEcsModule> BaseModules`.
- Additionally expose `GhostCreationSystem? GhostCreationSystem { get; init; }` — needed for
  Phase 4 replay handler wiring. Nullable because headless/test contexts may omit it.
- No logic, no dependencies beyond the types it wraps.

**Namespace:** `Hrot.ClusterRunner.Infrastructure`

---

### Task 2 — `HrotNodeBuilder` (EAM-I001, part B)

**File to create:** `Hrot.ClusterRunner/Infrastructure/HrotNodeBuilder.cs`

**Task Definition:** See [TASK-DETAIL.md — EAM-I001](../TASK-DETAIL.md#eam-i001--fdpkernelbuilder-and-hrotnodecontext)

**Initialization sequence inside `Build()` — follow exactly:**

```
1.  EntityRepository construction
2.  EventAccumulator + ModuleHostKernel(world, eventAccumulator)
3.  FdpEventBus construction
4.  TimeControllerFactory.Create(eventBus, new TimeControllerConfig {
        Mode = TimeMode.Continuous,
        Role = TimeRole.Slave,
        LocalNodeId = config.NodeId,
        SyncConfig = TimeConfig.Default })
    kernel.SetTimeController(timeCtrl)
    eventBus.SwapBuffers()
5.  HrotEnvironment.CreateParticipant(config.DomainId) + EnableSenderTracking
6.  new NetworkEntityMap()
7.  new DdsIdAllocator(participant, subsystemName + "Allocator")
    EnsureIdAllocatorRouting(participant, idAllocator)    ← shared helper method (EAM-I002)
8.  ClusterSlave and NodeOpSlaveTranslator wiring INLINE:
    a.  var clusterSlave = new ClusterSlave(config.NodeId, subsystemName, eventBus)
    b.  NodeOpSlaveTranslator? slaveTranslator = null
        if (participant != null && eventBus != null)
        {
            slaveTranslator = new NodeOpSlaveTranslator(
                commandReader:   new DdsReader<NodeOpCommand>(participant),
                statusWriter:    new DdsWriter<NodeOpStatus>(participant),
                heartbeatWriter: new DdsWriter<NodeHeartbeat>(participant),
                bus:             eventBus,
                nodeId:          config.NodeId);
        }
    c.  var storageProvider = new LocalDiskStorageProvider(config.LocalTempRoot ?? @"C:\FDP_Temp")
    d.  clusterSlave.RegisterHandler(new ReferencePreviewHandler(world))
    e.  clusterSlave.RegisterHandler(new ReferencePrefetchHandler(storageProvider))
    f.  clusterSlave.RegisterHandler(new ReferenceArchiveHandler(storageProvider.Root, config.NodeId))
    g.  clusterSlave.RegisterHandler(new ReferenceLiveLoadHandler(null, null, storageProvider.Root))
9.  Collect infrastructure IEcsModule instances (EntityLifecycleModule, GeographicModule if created)
    into IReadOnlyList<IEcsModule> baseModules
10. Return new HrotNodeContext(
        World: world, Kernel: kernel, Participant: participant, EventBus: eventBus,
        EntityMap: entityMap, ClusterSlave: clusterSlave, SlaveTranslator: slaveTranslator,
        BaseModules: baseModules)
```

**API:**
```csharp
_context = new HrotNodeBuilder(config)
    .WithRole("EyesAndMuscle", NodeRole.MuscleGround | NodeRole.ImageGenerator)
    .Build();
```

`config` type: reuse `SubsystemConfig` (already in `FDP.Framework.Runner`) or define a small
`HrotNodeConfig` struct containing `DomainId`, `NodeId`, `LocalTempRoot`. **Check what
`SubsystemConfig` exposes first** — it may already have the fields you need, avoiding a new type.

**Constraints:**
- Single-use builder: second `Build()` call throws `InvalidOperationException`.
- `HrotNodeBuilder` must NOT reference `Hrot.SimHost` internal domain types.
- Must NOT call `NodeBootstrapper.BuildOrchestration` (confirmed by code review).
- Optionally: a private `FdpKernelBuilder` nested class or region that isolates steps 1-4
  (generic engine, no DDS) from steps 5-9 (Hrot/DDS-specific). This separation of concerns is
  required to be VISIBLE in the code, but whether it's a nested class or clearly separated
  regions/methods is your call.

**Tests Required (in Hrot.ClusterRunner.Tests or Integration.Tests):**

*SC1 — Builder produces valid context (headless):*
- Call `new HrotNodeBuilder(config).WithRole("Test", NodeRole.MuscleGround).Build()`
  with a headless / no-DDS config.
- Assert: `HrotNodeContext` non-null; `World`, `Kernel`, `EventBus`, `EntityMap`, `ClusterSlave`
  all non-null; `BaseModules` non-null and non-empty.

*SC2 — Kernel has a time controller:*
- Same setup as SC1.
- Assert: `context.Kernel` has an active `SlaveSyncController` (check via kernel state or no-throw
  on a method that requires time controller).

*SC3 — Double-build throws:*
- Create builder, call `Build()` once (succeeds), call `Build()` again.
- Assert: `InvalidOperationException`.

*SC4 — NodeBootstrapper.BuildOrchestration is NOT called (code review):*
- `HrotNodeBuilder.cs` must not contain a call to `BuildOrchestration`.

---

### Task 3 — `EnsureIdAllocatorRouting` shared helper (EAM-I002)

**Task Definition:** See [TASK-DETAIL.md — EAM-I002](../TASK-DETAIL.md#eam-i002--ensureidallocatorroutinghelper)

**Approach:** Two options:
- **Option A (recommended):** Inline as a `private static` method in `HrotNodeBuilder.cs` in a
  clearly-marked `// ── ID Allocator routing ──` region. Called from `Build()` step 7.
- **Option B:** Create `Hrot.ClusterRunner/Infrastructure/DdsIdAllocatorHelper.cs` as a
  `public static class` with a `public static void EnsureRouting(DdsParticipant, DdsIdAllocator)`.

The DESIGN.md allows either. Option A keeps it private to the builder (single-use helper). Option B
enables future reuse from subsystems. **Choose Option B** (shared helper) since SimHostApp also
calls it and we'll replace that call in Phase 4 (EAM-M001).

**Files:**
- `Hrot.ClusterRunner/Infrastructure/DdsIdAllocatorHelper.cs` — NEW FILE, public static helper
- `Hrot.SimHost/SimHostApp.cs` — update `EnsureIdAllocatorRouting` call to use the shared helper

**Logic to move (unchanged):** The while-loop that polls `_idAllocator.HasPublicationMatch` up to
30s, logging a warning at 5s, and throwing `InvalidOperationException` on timeout.

**Constraint:** Do NOT change the logic — only move it.

**Tests Required:**

*SC1 — SimHost tests still pass:*
- `dotnet test Hrot.SimHost.Tests --no-build` must report 0 failures.
- Existing integration tests that exercise DDS must pass.

*SC2 — No duplicate code (code review):*
- `SimHostApp.EnsureIdAllocatorRouting` private method must be DELETED.
- `SimHostApp.OnLoad` calls the shared helper instead.

---

### Task 4 — `NedReplicationModule` core (EAM-N001)

**File to create:** `Hrot.ClusterRunner/Replication/NedReplicationModule.cs`

**Task Definition:** See [TASK-DETAIL.md — EAM-N001](../TASK-DETAIL.md#eam-n001--nedreplicationmodule-core)

**Constructor parameters:**
```csharp
public NedReplicationModule(
    DdsParticipant participant,
    NodeRole role,
    NetworkEntityMap entityMap,
    IGeographicTransform geoTransform,
    FdpEventBus eventBus,
    int localNodeId,
    int domainId)
```

**Module properties:**
```csharp
public string Name => "NedReplication";
public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();
public void Tick(ISimulationView view, float dt) { }  // no-op; all work via registered systems
```

**Implementation notes:**

In the **constructor**:
1. Validate role — if none of `MuscleGround`, `ImageGenerator`, `Brain` flags are set, throw
   `ArgumentException`.
2. Create `GhostCreationSystem ghostCreationSystem = new GhostCreationSystem(entityMap)`.
3. Store `ghostCreationSystem` as a public property `public GhostCreationSystem GhostCreationSystem
   { get; }` — for Phase 4 replay handler wiring (see Critical Technical Finding #4).
4. Build translator lists by role:
   - ALL roles: `SharedTranslatorPack.Create(participant, entityMap, localNodeId, eventBus, ghostCreationSystem)` → `_sharedTranslators`
   - `MuscleGround` / `AllInOne`: `KinematicTranslatorPack.Create(participant, entityMap, geoTransform)` → `_kinematicTranslators`
   - `Brain` / `AllInOne`: `CognitiveTranslatorPack.Create(participant, entityMap, geoTransform, null, ghostCreationSystem)` → `_cognitiveTranslators`
   - `ImageGenerator` (handled via `EntityStatesIngressPack` module in RegisterSystems, not raw translators)
5. Determine `driveFromNetwork` flag:
   - `true` if role is purely `ImageGenerator` (no local physics)
   - `false` if combined role includes `MuscleGround | ImageGenerator`

**`RegisterSystems(ISystemRegistry registry)` implementation:**

```csharp
public void RegisterSystems(ISystemRegistry registry)
{
    // ── Ghost lifecycle systems (all roles) ──────────────────────────────
    registry.RegisterSystem(ghostCreationSystem);
    registry.RegisterSystem(new NetworkLifecycleSystemGroup(ghostCreationSystem));

    // ── Translator routing systems ────────────────────────────────────────
    // Collect all egress translators for cleanup system
    var allTranslators = new List<IDescriptorTranslator>(_sharedTranslators);

    if (roleHasMuscle)
        allTranslators.AddRange(_kinematicTranslators);
    if (roleHasBrain)
        allTranslators.AddRange(_cognitiveTranslators);
    // Note: IG role translators are inside EntityStatesIngressPack (IEcsModule)

    // Route ingress ticks: shared + kinematic/cognitive
    registry.RegisterSystem(new CycloneNetworkIngressSystem(allTranslators.ToArray()));
    // Route egress ticks
    registry.RegisterSystem(new CycloneEgressSystem(allTranslators.ToArray()));

    // For ImageGenerator: inline EntityStatesIngressPack.RegisterSystems
    if (roleHasIG)
    {
        var igPack = new EntityStatesIngressPack(
            PackRole.Ingress, participant, entityMap, eventBus,
            ghostCreationSystem, geoTransform);
        igPack.RegisterSystems(registry);  // inlines CycloneNetworkIngressSystem for IG translators
        // DR sync — processes ghost entities
        registry.RegisterSystem(new DeadReckoningSyncSystem(driveFromNetwork));
    }

    // ── Role-specific systems ─────────────────────────────────────────────
    if (roleHasMuscle || roleHasBrain)
        registry.RegisterSystem(new SmartEgressSystem(...));

    // ── Cleanup systems (all roles) ────────────────────────────────────────
    registry.RegisterSystem(new CycloneNetworkCleanupSystem(allTranslators));
    registry.RegisterSystem(new DisposalMonitoringSystem(entityMap));
}
```

**⚠️ Important notes on translator constructor signatures:**
- `SharedTranslatorPack.Create(...)` — study the actual signature in the file (it may differ
  slightly from TASK-DETAIL). Match what the code actually accepts.
- `KinematicTranslatorPack.Create(...)` — same: study the actual method signature.
- `CognitiveTranslatorPack.Create(...)` — takes `behaviorRegistry?` which can be `null` for
  NedReplicationModule (moved to subsystem's responsibility).
- `CycloneNetworkIngressSystem` and `CycloneEgressSystem` — these classes are defined inside
  `CycloneNetworkModule.cs` (local to the Cyclone assembly). Check their namespace and whether
  they are `public` or `internal`. If `internal`, you may need to either:
  a. Replicate the polling logic inline, OR
  b. Use the `EntityStatesIngressPack.RegisterSystems` delegation pattern for ALL roles.
  Study what PACK3 used (CycloneNetworkModule.RegisterSystems delegates pattern) for inspiration.

**Constructor comment requirement:**
```csharp
// TODO: move to shared if NedReplicationModule is extracted from Hrot.ClusterRunner
// DeadReckoningSyncSystem is currently in Hrot.IG/Systems/ — accessible here because
// Hrot.ClusterRunner references Hrot.IG. If NedReplicationModule is later moved to a
// shared project, DeadReckoningSyncSystem would need to move with it.
```

**Tests Required (in an appropriate test project):**

For the role-based system registration tests (SC1–SC6 from TASK-DETAIL), use a test kernel or
system registry mock that can enumerate registered system types. Study existing registration tests
(e.g., in `Hrot.ClusterRunner.Tests/` or similar) for the harness pattern.

*SC1 — MuscleGround role registers correct systems:*
- Construct module with `NodeRole.MuscleGround`.
- Assert: `GhostCreationSystem` registered, `SmartEgressSystem` registered,
  `CycloneNetworkCleanupSystem` registered, `DisposalMonitoringSystem` registered.
- Assert: `DeadReckoningSyncSystem` NOT registered.

*SC2 — ImageGenerator role registers correct systems:*
- Construct with `NodeRole.ImageGenerator`.
- Assert: `GhostCreationSystem` registered, `DeadReckoningSyncSystem` registered.
- Assert: `SmartEgressSystem` NOT registered.

*SC3 — Combined role (MuscleGround | ImageGenerator):*
- Assert: `GhostCreationSystem`, `SmartEgressSystem`, `DeadReckoningSyncSystem` all registered.
- Assert: `DeadReckoningSyncSystem` was created with `driveFromNetwork: false`.

*SC4 — Invalid role throws:*
- Construct with `NodeRole.Perception` (or any role with no `MuscleGround`/`ImageGenerator`/`Brain`).
- Assert: `ArgumentException` thrown.

*SC5 — Brain role registers correct systems:*
- Construct with `NodeRole.Brain`.
- Assert: `GhostCreationSystem` registered, `SmartEgressSystem` registered.
- Assert: `DeadReckoningSyncSystem` NOT registered.

---

### Task 5 — Translator pack accessibility verification (EAM-N002)

**Task Definition:** See [TASK-DETAIL.md — EAM-N002](../TASK-DETAIL.md#eam-n002--shared-translator-pack-accessibility)

**Checklist (verify each, make minimum-visibility changes only):**
- [ ] `KinematicTranslatorPack` — `public static class`, `public static ... Create(...)` → ✓ already public
- [ ] `SharedTranslatorPack` — `public static class`, `public static ... Create(...)` → ✓ already public
- [ ] `CognitiveTranslatorPack` — `public static class`, `public static ... Create(...)` → ✓ already public
- [ ] `EntityStatesIngressPack` — `public class`, constructor is `public` → ✓ already public
- [ ] `DeadReckoningSyncSystem` — `public class` → ✓ already public

If any are NOT public, change visibility to `public` (minimum change only — no refactor of internals).

**SC1 — NedReplicationModule.cs compiles cleanly:**
- No `// HACK: internal access` workarounds anywhere.

**SC2 — No behavioral changes:**
- All existing tests pass after any visibility changes.

---

## 🧪 Testing Requirements

### Minimum Test Coverage

| Task | Test Type | Minimum Assertions |
|---|---|---|
| I001-SC1 | Unit (headless) | HrotNodeContext non-null; World, Kernel, EventBus, EntityMap, ClusterSlave non-null |
| I001-SC2 | Unit | Kernel has active time controller (no exception) |
| I001-SC3 | Unit | Second Build() throws InvalidOperationException |
| I002-SC1 | Regression | dotnet test Hrot.SimHost.Tests passes |
| N001-SC1 | Unit | GhostCreationSystem + SmartEgressSystem registered for MuscleGround |
| N001-SC2 | Unit | DeadReckoningSyncSystem registered for ImageGenerator |
| N001-SC3 | Unit | driveFromNetwork=false for combined MuscleGround\|ImageGenerator |
| N001-SC4 | Unit | ArgumentException for invalid role (e.g. Perception) |
| N001-SC5 | Unit | GhostCreationSystem + SmartEgressSystem registered for Brain |
| N002-SC1 | Build | dotnet build IOS-IG-SimHost.sln passes cleanly |

### Test-Driven Task Progression

**MANDATORY WORKFLOW — Test-Driven Task Progression:**

> For each task, before writing production code:
> 1. Read the existing tests to understand what currently passes.
> 2. Write the failing test(s) first (unit or integration as specified).
> 3. Implement the production code to make the tests pass.
> 4. Run the full relevant test suite to confirm no regressions.
> 5. Only then mark the task done in your report.
>
> **Never consider a task complete until all its tests pass AND existing tests remain green.**

### Test commands to run before submitting report

```powershell
# Build first
dotnet build d:\Work\IOS-IG-SimHost-FDP-2\IOS-IG-SimHost.sln --no-restore

# Run new unit tests
dotnet test Hrot.ClusterRunner.Tests --no-build --logger "console;verbosity=normal"

# Run integration tests (critical regression check)
dotnet test Hrot.ClusterRunner.Integration.Tests --no-build --logger "console;verbosity=quiet"

# SimHost tests (EAM-I002 regression)
dotnet test Hrot.SimHost.Tests --no-build --logger "console;verbosity=quiet"
```

---

## 📊 Report Requirements

Submit to: `.dev/eyes-and-muscle/reports/BATCH-01-REPORT.md`

```markdown
# BATCH-01 Report — DRY Infrastructure + NedReplicationModule

## Implementation Summary
[Per-task: what was done, key decisions made]

## Files Created / Modified
[List each file with its path and what changed]

## Tests Added
[List new test methods with file paths and what they assert]

## Test Results
[Paste or describe test run output — include pass/fail counts]

## Developer Insights
1. **Issues Encountered:** What problems did you hit?
2. **Weak Points Spotted:** Fragile or unclear areas noticed in the codebase?
3. **Design Decisions Beyond the Spec:** Any decisions not explicitly stated?

## Deviations from Spec
[List with justification — e.g. "Used Create instead of Build because..."]
```

---

## ⚠️ Important Notes

1. **Phase 3 and Phase 4 are OUT OF SCOPE for this batch** — only EAM-I001, I002, N001, N002.
2. **Do NOT modify SimHostApp.OnLoad** beyond replacing the `EnsureIdAllocatorRouting` call
   (EAM-I002). The full replacement of OnLoad is Phase 4 (BATCH-03).
3. **Do NOT change existing translator pack logic** — only access modifiers if needed (EAM-N002).
4. **Translator factory methods use `Create` not `Build`** — do not rename them.
5. **`NedReplicationModule` constructor validates role** — throw `ArgumentException` for unsupported
   role flags (Perception, NavigationSolver, or any combination without Muscle/IG/Brain).
6. **`HrotNodeBuilder` must not import `Hrot.SimHost`** — it belongs to `Hrot.ClusterRunner` and
   must only use types accessible from that project without domain-specific libraries.
7. **Study `CycloneNetworkModule.RegisterSystems`** carefully before implementing `NedReplicationModule.RegisterSystems` — the two follow the same "inline sub-module" pattern.
