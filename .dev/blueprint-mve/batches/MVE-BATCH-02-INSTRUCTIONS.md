# MVE-BATCH-02: wire the Blueprint runtime into the editor kernel + headless real-kernel run test
Foundation of the vertical slice. Make Instance Blueprints actually tick inside the editor's real `ModuleHostKernel` (the same composition the running app uses), proven by a headless integration test that creates its own entity and runs a demo blueprint through that kernel. (Toolbar button = MVE-BATCH-03; Save = MVE-BATCH-04.)

## Locked design decisions (from the user)
- Target **`EditorSubsystem`** only (no `editor_stride` — doesn't exist in this branch).
- **Observable = increment a blackboard variable** (`Count:int` working-state). Simplest proof the blueprint ran; do NOT move the entity / touch `SimTransform`.
- The headless test **creates its own entity** (nothing to select headlessly).
- Reuse the existing `_blueprintRegistry` the editor already maintains; same instance the runtime ticks.

## Onboarding
1. `.dev/.guides/DEV-GUIDE_claude.md`; `.dev/blueprint-mve/DESIGN.md` (architectural rule: run in the REAL system, NO sandbox/separate world).
2. **Architect's verified wiring** (matches the code): `EditorSubsystem.cs` composition — `simHostCorePack` (649), `cgfLogicPackInst` (659), the sim group built at **667–669** (`cgfLogicPackInst.SimulationSystems.Concat(simHostCorePack.SimulationSystems).ToArray()`) wrapped in `EditorSimulationModule` (registered **693**, class ~2276); `RegisterGlobalSystem(...)` used for non-Simulation systems (679/690–691). `_blueprintRegistry`/`BlueprintDebugSession` exist (~183/776).
3. Reuse: `BlueprintTickSystem`(Simulation, `[UpdateBefore]` dispatchers), `BlueprintMaintenanceSystem`(BeforeSync), `BlueprintBlackboard1024/4096/16384`, `BlueprintRegistry`, `BlueprintBlackboardPartitions`, `BlueprintIdHash`, and `BlueprintTestFixture.AttachBlueprint`/`ChooseTier` (the attach sequence to adapt) — under `FDP/Toolkits/Fdp.Toolkits/Blueprints/*` and `Hrot.Blueprints.Tests`.
Use codebase-memory MCP; not search_code. GizmoMap.Contracts 0.2.2; no Hrot.IG/DDS.

## Task 1 — wire the blueprint runtime into the editor kernel (`EditorSubsystem`)
In `EditorSubsystem`'s composition root (~640–730):
- Register tier components on `_world` before kernel init: `RegisterComponent<BlueprintBlackboard1024/4096/16384>()`. (16384 ≈ 16KB/entity — register all three per the architect; if the world pre-allocates per-archetype and that's prohibitive, register 1024/4096 and document.)
- `var bpTick = new BlueprintTickSystem(_blueprintRegistry); var bpMaint = new BlueprintMaintenanceSystem();`
- **Simulation:** append `bpTick` to the sim systems array at ~669 → `…SimulationSystems.Concat(…).Append(bpTick).ToArray()`. Its `[UpdateBefore]` attrs auto-order it before the dispatchers — do NOT hand-order.
- **BeforeSync:** `_kernel.RegisterGlobalSystem(bpMaint);` with the other global registrations.
- Confirm the exact `BlueprintRegistry` field name and that it's the instance the editor compiles into (so the live sim sees editor-registered blueprints). Cite it.
- Keep `EditorSubsystemBoot` + the integration suite green.

## Task 2 — headless real-kernel run test
Run through the REAL `ModuleHostKernel`, not the fixture. Use `Hrot.ClusterRunner.Integration.Tests/EditorHarness` (mirrors the SimHost/CGF composition). Since EditorHarness builds its own kernel, **mirror the Task-1 wiring into EditorHarness — preferably by extracting a tiny shared `WireBlueprintRuntime(kernel, world, registry)` helper used by BOTH `EditorSubsystem` and `EditorHarness`** (one source of truth). Then:
- New `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/BlueprintKernelRunTests.cs`: register the demo blueprint into the kernel's registry, **create its own entity**, attach it via the production `BlueprintAttachService` (Task 4), enable the sim group, `PumpFrames(N)`, assert the blackboard `Count` advanced by exactly N (read the slot).
- Assert REAL execution (== N), not "no throw". World-singleton variant optional.

## Task 3 — observable demo blueprint
A small **Instance** blueprint whose generated tick increments a `Count:int` working-state each frame (InstanceCounter-style). Reuse `InstanceCounter`/`FakeInstanceBp` if simplest, else a clearly-named `CounterDemo`. This is also the asset the MVE-03 button will run.

## Task 4 — production attach helper (the seam the button reuses)
Extract the attach sequence into a **production-side** helper the MVE-03 toolbar button calls without test deps — e.g. `BlueprintAttachService.AttachToEntity(EntityRepository world, BlueprintRegistry registry, BlueprintAsset asset, Entity entity)` in `Hrot.Blueprints.Editor`. It performs exactly the fixture sequence (`BlueprintIdHash.Compute` → `TryGetById` → require `Kind==Instance` → `ChooseTier(def.StateSize)` → ensure `BlueprintBlackboard*` component → `Initialize` if fresh → `TryAttach` → `InitDefault`), is idempotent if already attached, and returns a clear success/failure. **Run-mode-agnostic: it only sets up the entity's components; it does not require the sim to be running/previewing.** The headless test uses this same helper (test + button share one path). Unit-test it headlessly.

## Success Criteria
- [ ] Blueprint runtime ticks inside the editor `ModuleHostKernel` (systems + tier components + shared registry wired in `EditorSubsystem`; mirrored into EditorHarness via the shared helper).
- [ ] `BlueprintKernelRunTests` runs the demo blueprint through the REAL kernel on a self-created entity; `Count` advances by the pumped frame count.
- [ ] `BlueprintAttachService` (production, run-mode-agnostic, idempotent) extracted + unit-tested; shared by test + (later) button.
- [ ] Build 0 errors; touched projects no new warnings. GizmoMap.Contracts 0.2.2.
- [ ] Green: new tests; `Hrot.ClusterRunner.Integration.Tests` incl. `EditorSubsystemBoot` (no regressions); `Hrot.Blueprints.Tests` (DEBT-006 unchanged); `Hrot.Editor.AiShared.Tests`.
- [ ] Report at `.dev/blueprint-mve/reports/MVE-BATCH-02-REPORT.md`.

## Execution rules
- Verify the system/registry/composition APIs + the exact `_blueprintRegistry` field against the code FIRST (cite file:line). Reuse the existing systems/registry/partitions + the fixture's attach sequence — do NOT reimplement the runtime, hand-roll a tick, or hand-order systems. Assert real observable state. Never fake a pass.
- One source of truth for the kernel wiring (shared helper) if clean; else mirror + note. NO separate/sandbox world.

## Report
Document: the editor-kernel wiring (lines, registry field, ordering); how the headless test runs through the real kernel (shared helper / EditorHarness); the demo blueprint + observable; the `BlueprintAttachService` API; test counts; build status; boot/integration unaffected; precise next steps for MVE-03 (toolbar button on `EditorSelectionStore.SelectedEntity`, attach-only via `BlueprintAttachService`, run-mode-agnostic) and MVE-04 (Save). Suggested commit message. No comprehension questions.
