# MVE-BATCH-01: headless "run an Instance Blueprint on an entity" in the ClusterRunner sim
First MVE slice — the RUN stage, proven headlessly + a reusable helper the editor button will call.

## Onboarding
1. `.dev/.guides/DEV-GUIDE_claude.md`; `.dev/blueprint-mve/DESIGN.md` (verified run pipeline + harness inventory).
2. Reuse: `Hrot.Blueprints.Tests/BlueprintTestFixture` (CompileAndLoad/CreateEntity/AttachBlueprint/TickFrame/GetBlueprintState) and `Hrot.ClusterRunner.Integration.Tests/EditorHarness` (ModuleHostKernel + PumpFrames). Study `SingleSlotTickTests` (Hrot.Blueprints.Tests) as the attach→tick→observe template.
Use codebase-memory MCP; not search_code. GizmoMap.Contracts 0.2.2; no Hrot.IG/DDS.

## CRITICAL first step — verify the run substrate
Determine whether the **ClusterRunner kernel** (the one `EditorHarness` builds) actually schedules `BlueprintTickSystem` + `BlueprintMaintenanceSystem` and registers the `BlueprintBlackboard1024/4096/16384` components + a `BlueprintRegistry`. Search the SimHost/CGF/Toolkit module packs the kernel loads.
- **If YES:** write the MVE test against `EditorHarness` (the real "fully-blown sim engine") — this is preferred (proves it in the actual runner).
- **If NO** (blueprint systems live only in `BlueprintTestFixture`'s hand-rolled world): write the MVE test against `BlueprintTestFixture` for now AND document precisely what module/registration would add the blueprint systems to the ClusterRunner kernel (so MVE-06's editor button has a real run substrate). Do NOT bodge blueprint systems into the kernel in this batch beyond what's clean/correct — report the gap.
State which path you took and why, with file:line.

## Task 1 — the end-to-end RUN test
New test file `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/BlueprintRunMveTests.cs` (or under BlueprintTestFixture's project if that's the chosen substrate — but prefer the ClusterRunner integration test project per the user's ask):
- Register/compile a simple **Instance** blueprint whose tick produces an observable state change (use **InstanceCounter** test asset, or the existing `FakeInstanceBp`/an equivalent compiled asset — pick the one that's genuinely registered in the chosen substrate). 
- Create an entity, attach the blueprint (the tiered `BlueprintBlackboardPartitions.TryAttach` path; reuse the fixture's `AttachBlueprint` or the production attach API), pump **N** frames through the real tick (EditorHarness.PumpFrames, or fixture.TickFrame), and **assert the observable state change** (e.g. `Count`/`TickCount` == N) by reading the blackboard slot (`GetBlueprintState(...).TryGetField<int>(...)`).
- Add a **world-singleton** variant if the chosen substrate supports it (no entity; `GetSingleton<TBB>`; assert it lazy-inits + ticks).
**Assertions must prove real execution** — the counter advances by the number of pumped frames, not merely "no throw".

## Task 2 — reusable run helper (for the editor button later)
Extract a small, reusable helper — e.g. `BlueprintRunHarness` (in the test project, or a thin shared helper if cleanly placeable) — exposing something like:
`Entity SpawnAndAttach(BlueprintAsset asset)` and `int ReadIntField(Entity e, BlueprintAsset asset, string field)` (+ a tick/pump pass-through), so MVE-06's editor "Run Opened Blueprint" button can reuse the exact same attach+run logic. Keep it minimal and headless-friendly. If a production-side helper is warranted (so the editor can call it without test deps), note where it should live (e.g. a small service in Hrot.Blueprints.Editor or a Toolkit helper) — but for THIS batch the test-side helper + a clear note is enough.

## Success Criteria
- [ ] A headless integration test runs an Instance Blueprint on an entity in the (real ClusterRunner kernel if available, else BlueprintTestFixture) sim and asserts the counter advances by the pumped frame count.
- [ ] World-singleton variant (if substrate supports it).
- [ ] Reusable attach+run helper extracted (or its production home identified).
- [ ] Clear statement of whether the ClusterRunner kernel schedules the blueprint systems, with the gap + fix if not.
- [ ] Build 0 errors; touched projects no new warnings. GizmoMap.Contracts 0.2.2.
- [ ] Green: the new test(s); plus no regressions in `Hrot.Blueprints.Tests` (DEBT-006 unchanged), `Hrot.ClusterRunner.Integration.Tests` (EditorSubsystemBoot filter), `Hrot.Editor.AiShared.Tests`.
- [ ] Report at `.dev/blueprint-mve/reports/MVE-BATCH-01-REPORT.md`.

## Execution rules
- Verify the substrate question FIRST (cite file:line). Reuse existing fixtures/harness; don't reimplement the runtime or hand-roll a parallel tick. Assert real state change. Never fake a pass.
- Keep it minimal — this is the run slice; compile-on-demand/save/hot-reload/debug/button are later MVE batches.

## Report
Document: substrate decision (ClusterRunner kernel vs fixture) + evidence the blueprint systems are/aren't in the kernel; the asset used + observable; the attach+run helper + its intended production home; actual test counts; build status; suggested commit message; and the precise next-gap for MVE-02 (compile-on-demand) and MVE-06 (button). No comprehension questions.
