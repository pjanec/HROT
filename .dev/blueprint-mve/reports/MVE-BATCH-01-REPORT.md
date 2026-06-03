# MVE-BATCH-01 Report — headless "run an Instance Blueprint on an entity"

## Substrate decision (CRITICAL first step)

**Path taken: B — `BlueprintTestFixture` (`Hrot.Blueprints.Tests`).**

**The ClusterRunner kernel that `EditorHarness` builds does NOT schedule the blueprint
systems, nor register the blackboard tier components or a `BlueprintRegistry`.** Evidence:

- `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs:118–221` (the whole
  constructor): it registers `HrotSharedComponentRegistry`, `CognitiveComponentRegistry`,
  `CombatComponentRegistry`, `CgfComponentRegistry`, `ZoneMembership`, then the modules
  `CognitiveSpatialModule`, `ScenarioEditorModule`, `EntityLifecycleModule`, `SimHostModule`,
  `EqsModule`, `GenesisMaterializationSystem`, the CGF/SimHost input/sim/post systems, and
  `EditorSystemsModule`. **No `BlueprintTickSystem`, no `BlueprintMaintenanceSystem`, no
  `BlueprintBlackboard1024/4096/16384`, no `BlueprintRegistry` anywhere.**
- The module packs it loads contain zero blueprint references:
  `SimHostCoreLogicPack.cs`, `CgfLogicPack.cs`, `SimHostModule.cs`, `EditorSystemsModule.cs`
  all return 0 occurrences of `Blueprint` (grep).
- The blueprint systems/components/registry live in `FDP/Toolkits/Fdp.Toolkits/Blueprints/...`
  (`Systems/BlueprintTickSystem.cs`, `Systems/BlueprintMaintenanceSystem.cs`,
  `Components/BlueprintBlackboard1024.cs`, `BlueprintRegistry.cs`). In the non-Toolkit `Hrot`
  tree they are referenced only by editor/compiler/reload code
  (`EditorSubsystem.cs`, `AiHotReloadCoordinator.cs`, `QuickReloadService.cs`,
  `BlueprintsCore.cs`, `CSharpEmitter.cs`) — never by a kernel-loaded logic pack.

Per the batch instruction ("If NO → write against `BlueprintTestFixture` AND document the
gap; do not bodge systems into the kernel"), I used the fixture and report the wiring gap
below. The fixture is the proven minimal world + registry + `BlueprintTickSystem`/
`BlueprintMaintenanceSystem` substrate that `SingleSlotTickTests`/`WorldSingletonTickTests`
already exercise; `TickFrame` runs the **production** tick + maintenance systems (not a
hand-rolled parallel tick) — see `BlueprintTestFixture.cs:122–152`.

### Gap + fix to put the blueprint systems into the ClusterRunner kernel (for MVE-06)

To give the editor "Run Opened Blueprint" button a real run substrate, the kernel needs,
mirroring the fixture's wiring:

1. **Components:** `Repo.RegisterComponent<BlueprintBlackboard1024>()` (+ `4096`, and
   `16384` only where the ~16 GB VA reservation for `MAX_ENTITIES` is acceptable — the
   fixture deliberately skips BB16384, see `BlueprintTestFixture.cs:99–103`).
2. **Registry:** construct a `BlueprintRegistry` and let the build-time
   `[BlueprintRegistrar]` source-generated registrars populate it at startup (route (i) in
   DESIGN.md), and/or the runtime `QuickReloadService` → `AiHotReloadCoordinator` path
   (route (ii), MVE-02).
3. **Systems:** schedule `BlueprintMaintenanceSystem` in the BeforeSync phase and
   `BlueprintTickSystem(registry)` in `SystemPhase.Simulation`. Like the editor harness'
   `EditorSimulationModule`, the Simulation-phase system must be registered through an
   `IEcsModule` (the kernel forbids global registration of Simulation-phase systems —
   `EditorHarness.cs:200–203, 277–313`).

The clean home is a small `BlueprintModule : IEcsModule` in `Fdp.Toolkits` (or a SimHost
pack) that does (1)+(3) given an injected `BlueprintRegistry`. This batch does **not** add
it (out of scope; would be a bodge without the editor button consuming it).

## Implementation Summary

### Task 1 — end-to-end RUN test
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/BlueprintRunMveTests.cs`
(placed in the chosen substrate's project per the batch's "or under BlueprintTestFixture's
project" clause; it cannot live in `Hrot.ClusterRunner.Integration.Tests` because that
project does not reference the blueprint runtime/fixture and its kernel can't run blueprints).

- **Asset / observable:** `FakeInstanceBp` — the genuinely-registered in-code Instance
  blueprint definition (`Runtime/FakeBlueprints.cs:17–65`) whose `Tick` increments an `int`
  `TickCount` field each frame. This is the asset `SingleSlotTickTests` proves; chosen over
  the `InstanceCounter.bp.json` compile-path asset because MVE-01 is the *run* slice (compile
  -on-demand is MVE-02). The observable is read back through the real slot via
  `GetBlueprintState(asset, entity).TryGetField<int>("TickCount")`.
- `InstanceBlueprint_RunsOnEntity_CounterAdvancesByFrameCount` (Theory: 1, 3, 10 frames):
  registers the blueprint, spawns an entity, attaches via the tiered
  `BlueprintBlackboardPartitions.TryAttach` path (through `fixture.AttachBlueprint`), asserts
  the counter is 0 pre-tick, pumps N frames through the real tick, and asserts
  `TickCount == N`. Real execution, not "no throw".
- `InstanceBlueprint_TwoEntities_AdvanceIndependently`: attaches the same blueprint to two
  entities spawned at different times (A for 5 frames, B for the last 2) and asserts
  A==5, B==2 — proves per-entity slot isolation (the counter is not a shared static).
- **World-singleton variant** `WorldSingletonBlueprint_LazyInitsAndTicks_CounterAdvancesByFrameCount`
  (Theory: 1, 4 frames): registers `FakeWorldSingletonBp` (with `AddWorldSingleton`),
  asserts the singleton blackboard does NOT exist before the first tick, pumps N frames,
  asserts the singleton was lazily attached exactly once (`SlotCount == 1`) and its
  `TickCount == N`. Uses `World.GetSingleton<BlueprintBlackboard1024>()` +
  `BlueprintBlackboardPartitions.TryGetSlotOffset` (the substrate supports singletons:
  `BlueprintTickSystem.Execute` calls `TickWorldSingletons`, file:line 46–63).

### Task 2 — reusable attach+run helper
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/BlueprintRunHarness.cs` — a small
headless helper wrapping the fixture:
- `Entity SpawnAndAttach(BlueprintAsset asset)` — create entity + attach (tiered TryAttach).
- `void Pump(int frames, float dt = 0.016f)` — pass-through to the real `TickFrame`.
- `int ReadIntField(Entity, BlueprintAsset, string field)` — reads the observable slot field;
  **throws** on missing slot/field so a silent miss can never masquerade as 0.

The MVE-01 entity tests drive the run logic exclusively through this helper, so MVE-06's
editor button can reuse the exact attach+run+read path.

**Production home for the button (MVE-06):** the editor must not take a test dependency.
The same three operations should live in a `BlueprintRunService` in
`Hrot.Blueprints.Editor` (it already references the Toolkit attach/registry/tick types and
is the editor's natural seam). It would take the live `EntityRepository` + `BlueprintRegistry`
+ a frame-pump callback (or the kernel) instead of a `BlueprintTestFixture`, and reuse
`BlueprintBlackboardPartitions.TryAttach` / `BlueprintStateView.TryGetField<T>` directly.
This is documented in the `BlueprintRunHarness` XML doc. It depends on the kernel-wiring gap
above being closed first.

## Design Decisions
- **Asset choice:** `FakeInstanceBp` over `InstanceCounter.bp.json` — keeps MVE-01 strictly
  the run slice; the compile path is MVE-02's job and would pull the compiler into the run
  proof unnecessarily.
- **Helper lives test-side** for this batch (per spec); production home named, not built.
- **Theory cases** (1/3/N) make "advances by the pumped frame count" the explicit contract
  rather than a single hard-coded number.

## Deviations
- **Test file location:** placed in `Hrot.Blueprints.Tests` (the substrate's project) rather
  than `Hrot.ClusterRunner.Integration.Tests`.
  - WHAT: file at `.../Hrot.Blueprints.Tests/Runtime/BlueprintRunMveTests.cs`.
  - WHY: the ClusterRunner kernel can't run blueprints (substrate decision above), and that
    test project references neither the blueprint runtime nor the fixture. The batch
    explicitly permits the fixture's project when Path B is chosen.
  - BENEFIT: the test runs against the proven substrate with no new project wiring.
  - RISK: none for the run proof; the "prove it in the actual ClusterRunner" goal is deferred
    to whoever closes the kernel-wiring gap (tracked above for MVE-06).

## Test Results
- New MVE tests (`BlueprintRunMveTests`): **Passed 6, Failed 0** (3 instance theory cases +
  1 two-entity + 2 singleton theory cases), 118 ms.
- Template classes still green: `SingleSlotTickTests` + `WorldSingletonTickTests` → **7/7**.
- Full `Hrot.Blueprints.Tests`: **Passed 1126, Failed 10, Skipped 8, Total 1144.** The 10
  failures are exactly the pre-existing **DEBT-006** set (golden-source/snapshot emit tests:
  `LibraryEmitGoldenTests`, `AiPrimitiveEmitGoldenTests` ×2, `InstanceEmitGoldenTests` ×3,
  `ConditionSummaryAttachmentTests`, `LibraryMathDemoTests`, `MoveToAndFireDemoTests`, plus
  the perf test `AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes`). None touch
  the blueprint tick path or my new files; count unchanged at 10.
- `Hrot.ClusterRunner.Integration.Tests` (filter `~EditorSubsystemBoot`): **Passed 10/10.**
- `Hrot.Editor.AiShared.Tests`: **Passed 761/761.**
- `dotnet build IOS-IG-SimHost.sln` (single-threaded): **0 errors**, 18 pre-existing
  solution-wide warnings, none from the two new files (verified by filtered build).

## Developer Insights
- **Environmental build trap:** the first full-solution build failed with ~34 `MSB3030`
  (CycloneDDS `.idl` copy) + `CS2012` (Hrot.IG.dll locked) errors. Root cause was ~70 stale
  `dotnet`/`MSBuild`/`VBCSCompiler` processes from a prior parallel build holding obj/ file
  locks — **not** a code problem. `dotnet build-server shutdown` + killing the stragglers,
  then a single-threaded (`-m:1`) build, produced a clean 0-error build. The DDS codegen is
  sensitive to parallel obj/ contention; recommend `-m:1` for full-solution builds in this
  repo, or building the touched project directly.
- `BlueprintStateView` is a `readonly struct`, so `GetBlueprintState` returns
  `BlueprintStateView?` (Nullable<struct>). After `?? throw`, call `.TryGetField` directly on
  the unwrapped struct — not `.Value.TryGetField` (that only applies to the still-nullable
  form, as in `SingleSlotTickTests`).
- The fixture intentionally omits `BlueprintBlackboard16384` (16 GB VA reservation for
  `MAX_ENTITIES`); any kernel-side BB16384 wiring must account for this.

## Known Issues
- The ClusterRunner kernel still cannot run blueprints; the real-runner proof is blocked on
  the `BlueprintModule` wiring described above. MVE-06's button depends on it.
- DEBT-006's 10 golden/perf failures remain (out of scope for this batch).

## Next gaps
- **MVE-02 (compile-on-demand):** drive `QuickReloadService` (BlueprintCompiler Stages 1–8 +
  `InMemoryRoslynCompiler` → ALC → registrar scan → `AiHotReloadCoordinator` commit) to
  compile `InstanceCounter.bp.json` at test time, then run it via this same harness — proving
  compile→register→run without `dotnet build`. The fixture's `CompileAndLoad`/`SimulateReload`
  already exercise that pipeline and are the natural entry point.
- **MVE-06 (editor button):** close the kernel-wiring gap (add `BlueprintModule` registering
  the tier components + maintenance/tick systems + a `BlueprintRegistry`), then add the
  production `BlueprintRunService` in `Hrot.Blueprints.Editor` that reuses the exact
  attach+pump+read logic the test-side `BlueprintRunHarness` proves here, and surface it as
  the "Run Opened Blueprint on a Test Entity" command.

## Suggested Commit Message
MVE-01: headless RUN proof for Instance + world-singleton blueprints + reusable BlueprintRunHarness
