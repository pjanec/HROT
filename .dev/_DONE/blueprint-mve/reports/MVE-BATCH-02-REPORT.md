# MVE-BATCH-02 Report — wire the Blueprint runtime into the editor kernel + headless real-kernel run test

## Implementation Summary

### Task 1 — wire the blueprint runtime into the editor kernel (`EditorSubsystem`)
The blueprint runtime now runs inside the editor's **real** `ModuleHostKernel` (no sandbox world).
Wiring goes through a single shared helper (see Task 2) called from `EditorSubsystem`'s composition
root, immediately before the simulation group is built:

- File: `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (~line 667).
- `var bpTick = BlueprintRuntimeWiring.WireBlueprintRuntime(_kernel, _world!, _blueprintRegistry);`
  - Registers `BlueprintBlackboard1024/4096/16384` on `_world` (before `_kernel.Initialize()` at line 1080).
  - Registers `BlueprintMaintenanceSystem` (BeforeSync) as a **global** system on the kernel.
  - Returns the Simulation-phase `BlueprintTickSystem(_blueprintRegistry)`.
- `bpTick` is **appended** to the simulation systems array fed to `TogglableSimulationGroup`:
  `cgfLogicPackInst.SimulationSystems.Concat(simHostCorePack.SimulationSystems).Append(bpTick).ToArray()`
  (the `EditorSimulationModule` at ~line 2276 wraps this group; the group is registered as a module
  because the kernel forbids Simulation-phase systems as global systems).

**Registry field (verified, cited):** `private BlueprintRegistry _blueprintRegistry = new();` at
`EditorSubsystem.cs:253`. It is the instance the editor compiles into: it is passed to the
`AiHotReloadCoordinator` constructor at `EditorSubsystem.cs:525` (the coordinator stages/commits
compiled blueprints into it) and to the `BlueprintDebugSession` (~`EditorSubsystem.cs:777`). Ticking
`BlueprintTickSystem` against this same instance means editor-registered/hot-reloaded blueprints run
live in the editor sim.

### Task 2 — headless real-kernel run test
- Shared helper `BlueprintRuntimeWiring.WireBlueprintRuntime(kernel, world, registry)` extracted to
  `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Runtime/BlueprintRuntimeWiring.cs` —
  **one source of truth** used by BOTH `EditorSubsystem` and `EditorHarness`.
- `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs` now calls the same helper
  before `Kernel.Initialize()`, exposes the shared registry via a new
  `public BlueprintRegistry BlueprintRegistry { get; }` property, and splices `bpTick` into the CGF
  simulation-systems list passed to its `EditorSimulationModule`.
- New test: `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/BlueprintKernelRunTests.cs`. Each test
  creates its **own** entity in the harness's live world, registers the demo blueprint into the
  kernel's registry, attaches via the production `BlueprintAttachService`, `PumpFrames(N)` (which
  steps time + calls `Kernel.Update()`), and asserts the blackboard `Count == N` read directly from
  the slot. This proves REAL execution through the genuine kernel schedule, not "no throw".

### Task 3 — observable demo blueprint
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Runtime/CounterDemoBlueprint.cs` — a small,
  **production-side**, code-defined Instance blueprint (`CounterDemo`) whose `Tick` increments a
  `Count:int` working-state field each frame. State layout is `{ BlueprintLatentCursor Cursor; int Count; }`
  so `CountOffset == sizeof(BlueprintLatentCursor)`. `AssetGuid` is fixed so
  `BlueprintIdHash.Compute(asset.AssetId) == BlueprintId`. `Register(registry)` commits it via the
  staging protocol. This is the same asset the MVE-03 button will run.

### Task 4 — production attach helper (`BlueprintAttachService`)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Runtime/BlueprintAttachService.cs`.
- `AttachToEntity(EntityRepository world, BlueprintRegistry registry, BlueprintAsset asset, Entity entity)`
  performs exactly the `BlueprintTestFixture.AttachBlueprint` sequence:
  `BlueprintIdHash.Compute` → `registry.TryGetById` → require `Kind == Instance` →
  `ChooseTier(def.StateSize)` → ensure `BlueprintBlackboard*` component → `Initialize` (idempotent on
  header magic) → `TryAttach` → `InitDefault` on the fresh payload.
- **Idempotent:** if a slot for the blueprint already exists on the entity (any tier) it returns
  `AlreadyAttached` without re-attaching. **Run-mode-agnostic:** it only mutates the entity's
  components; it never requires the sim to be running/previewing.
- Returns a `BlueprintAttachResult { Status, Tier, Message }` with a clear classified outcome
  (`Attached` / `AlreadyAttached` / `NotRegistered` / `NotInstanceKind` / `NoSlotAvailable`).
- Unit-tested headlessly in
  `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/BlueprintAttachServiceTests.cs`.

## Design Decisions
- **Shared helper returns `bpTick` instead of registering it.** The two hosts wrap Simulation-phase
  systems differently (editor: a single `TogglableSimulationGroup` array; harness: two enumerables
  into its own `EditorSimulationModule`). The kernel forbids registering Simulation systems globally,
  so the helper does the two host-uniform steps (tier components + BeforeSync maintenance global) and
  hands back the tick system for each host to splice into its own sim list. This keeps a single source
  of truth without forcing both hosts into one module shape.
- **Demo blueprint is production-side, not test-only.** Put `CounterDemoBlueprint` in
  `Hrot.Blueprints.Editor` (not reuse the test project's `FakeInstanceBp`) so both the integration
  test AND the future toolbar button reach the same asset without a cross-test-project dependency, and
  so the observable is the spec-mandated `Count:int` (FakeInstanceBp uses `TickCount`).
- **All three tiers registered.** Component tables reserve virtual address space lazily per 64 KB chunk
  (`NativeChunkTable<T>`, `NativeMemoryAllocator.Reserve` = `VirtualAlloc(MEM_RESERVE)`, 0 physical RAM).
  With `FdpConfig.MAX_ENTITIES = 524288` the 16 KB tier reserves ~8.6 GB of *address space* (no RAM),
  under the paranoid-mode cap (`int.MaxValue * 8`). So registering BB16384 is cheap and matches the
  tiers the tick/maintenance systems query. (The fixture's "16 GB / skip BB16384" comment is based on
  MAX_ENTITIES = 1 000 000 and does not apply here.)

## Deviations
- **`[UpdateBefore]` does not auto-order `bpTick` *inside* the `TogglableSimulationGroup`.**
  WHAT: The instructions state that appending `bpTick` lets its `[UpdateBefore(...Dispatcher)]`
  attributes auto-order it before the dispatchers. Verified against
  `FDP/Engine/Fdp.ModuleHost/Scheduling/TogglableSimulationGroup.cs:66` — the group executes its inner
  systems in **array order** and does NOT re-sort by `[UpdateBefore]` (attribute ordering is the
  top-level scheduler's job, but here the systems are nested inside one group system). So appending
  places `bpTick` **after** the dispatchers within the group.
  WHY: I still followed the "append" directive as written.
  BENEFIT/RISK: For the MVE observable (`Count` increment in the blueprint's own slot) ordering is
  irrelevant — the tick runs every frame regardless of position, which the tests prove (`Count == N`).
  RISK: if a future blueprint writes channel commands (Locomotion/Weapon/Interaction) that the
  dispatchers must consume the *same* frame, the current append-last position would defer them one
  frame. If that becomes a requirement, the dispatchers + `bpTick` should be promoted to individually
  scheduler-registered Simulation systems (so `[UpdateBefore]` is honored) rather than bundled in a
  single group. Documented here as the precise follow-up.

## Test Results
- `BlueprintKernelRunTests` (new, real kernel) + `EditorSubsystemBoot`: **15 passed, 0 failed.**
  - `InstanceBlueprint_TicksInRealKernel_CounterAdvancesByFrameCount` (N = 1, 3, 10): `Count == N`.
  - `InstanceBlueprint_TwoEntities_AdvanceIndependentlyInRealKernel`: A = 5, B = 2 (per-entity slot isolation).
  - `AttachToEntity_IsIdempotent_DoesNotDoubleCountInRealKernel`: double-attach then pump 4 → `Count == 4` (one slot).
  - `EditorSubsystemBoot`: 10/10 in isolation — the editor's real composition boots clean with the wiring.
- `BlueprintAttachServiceTests` (new, headless): **6 passed, 0 failed.**
  - Fresh attach → `Attached`, tier B1024, `Count == 0` after `InitDefault`.
  - Double attach → `AlreadyAttached`, exactly 1 slot.
  - Unregistered asset → `NotRegistered` (no tier component added).
  - Library-kind blueprint → `NotInstanceKind` (no tier component added).
  - Attach-then-tick on the fixture (N = 1, 5) → `Count == N` (the service's slot is the one the real tick ticks).
- `Hrot.Editor.AiShared.Tests`: **761 passed, 0 failed.**
- `Hrot.Blueprints.Tests` (full): **1131 passed, 8 skipped, 11 failed.** All 11 failures are
  pre-existing and unrelated to this batch (DEBT-006 golden-source/snapshot + flaky perf/allocation):
  `InstanceEmitGoldenTests` (InstanceCounter/DoorActor/HealthRegen), `LibraryEmitGoldenTests`,
  `AiPrimitiveEmitGoldenTests` (MoveToAndFire/HasVisibleTarget), `ConditionSummaryAttachmentTests`,
  `LibraryMathDemoTests`, `MoveToAndFireDemoTests`, `AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes`,
  `WhenNodePerfTests` (sub-80ns/200ns). None touch the blueprint runtime wiring, attach service, or demo
  blueprint. Perf tests pass when run isolated (8/9); only the strict 0-byte allocation test stays red
  in isolation (a known timing-sensitive DEBT test, not mine).
- `Hrot.ClusterRunner.Integration.Tests` (full suite): the run **aborts** because an unrelated
  DDS-based class (`ClusterOpE2eScriptTests`) crashes the test host on a background thread
  (`CycloneDDS.Runtime.DdsException: dds_take failed: -3` via `DdsIdAllocatorServer.ProcessRequests` /
  `HostedIdAllocatorServer.RunLoop`). This is pre-existing DDS-teardown flakiness — I touched no
  DDS/network files (`git status`: only `EditorHarness.cs` modified + `BlueprintKernelRunTests.cs`
  added). With the DDS class excluded, all 39 editor/preview/zone/blueprint tests pass.
- Full solution build: **0 errors, 18 warnings**, all pre-existing in unrelated test projects
  (`Fdp.Core.Tests`, `Hrot.Utility.Editor.Tests`, `Hrot.Diagnostics.Breakpoints.Tests` — xUnit2013 /
  CS0618 obsolete-API). Touched projects (`Hrot.Blueprints.Editor`, `Hrot.Editor`,
  `Hrot.ClusterRunner.Integration.Tests`) build with **0 warnings** under `TreatWarningsAsErrors`.

## Developer Insights
- The editor's `EditorSimulationModule` (`TogglableSimulationGroup`) and the harness's
  `EditorSimulationModule` are two different classes with different ctors; the helper deliberately does
  not try to unify them (see Deviations). Promoting the dispatchers + tick system to scheduler-level
  Simulation registration would let `[UpdateBefore]` work and is the cleanest long-term fix if blueprint
  channel-writes ever need same-frame dispatch.
- `EntityRepository.RegisterComponent<T>` is idempotent (`GetTable<T>(true)` returns the existing
  table), so `RegisterTierComponents` is safe to call even if a host already registered a tier.
- `BlueprintTickSystem.Execute` casts the view to `EntityRepository` and calls `view.GetCommandBuffer()`
  unconditionally; in the kernel's Direct/Synchronous path the view is the live world and the per-thread
  command buffer is returned, so this is safe even though the demo Tick does not use the ECB.
- Idempotency edge case handled: a freshly `AddComponent`-ed tier blackboard is zeroed (no header
  magic). `BlueprintAttachService.HasInitializedSlot` checks the header magic before calling
  `TryGetSlotOffset`, so the idempotency probe never scans uninitialized memory.

## Known Issues
- The full `Hrot.ClusterRunner.Integration.Tests` run cannot complete in one shot due to the unrelated
  CycloneDDS background-thread crash in `ClusterOpE2eScriptTests`. The blueprint tests and
  `EditorSubsystemBoot` are headless and pass cleanly when the DDS class is not in the run.
- `[UpdateBefore]` ordering of `bpTick` vs the dispatchers is not honored inside the group (see Deviations).
  Harmless for the MVE observable; a noted follow-up if same-frame channel dispatch is needed.

## Next steps
- **MVE-03 (toolbar button):** add a "Run Opened Blueprint on a Test Entity" command in the editor.
  It should read `EditorSelectionStore.SelectedEntity` (or spawn a test entity if none), then call
  `BlueprintAttachService.AttachToEntity(_world, _blueprintRegistry, openedAsset, entity)` — attach-only,
  run-mode-agnostic (works paused or running); the in-kernel `BlueprintTickSystem` (already wired by this
  batch) ticks it on the next frame the sim group runs. No new runtime needed.
- **MVE-04 (Save):** implement editor Save via `BlueprintJsonServices.Serialize` → disk, with a headless
  load → mutate → save → reload-identical test. Independent of this batch's runtime wiring.

## Suggested Commit Message
feat(blueprints): wire Instance-Blueprint runtime into the editor ModuleHostKernel + headless real-kernel run test (MVE-BATCH-02)
