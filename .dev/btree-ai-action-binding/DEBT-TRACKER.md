# DEBT-TRACKER — BTree AI Action/Condition Parameter Binding

> Known debts, deferrals, prerequisites, and watch-items for this workstream. Tasks: [TASK-TRACKER.md](./TASK-TRACKER.md). Design: `docs/blueprints/BTree_AiActionParameterBinding_Detailed_Design.md` ("AIB-DD").
> Status: `[ ]` open · `[x]` resolved.

## Prerequisites (folded into tasks — tracked here for visibility)
- [ ] **DEBT-AIB-001 — `bool` `[MarshalAs(I1)]` latent bug.** `BlackboardDtoEmitter` emits bare `bool`; bin-packer assumes 1 B but `Marshal.OffsetOf` defaults 4 B → silent offset drift / broken replay schemas. **Owned by task S1-0** (must precede S1-2). AIB-DD §3.2.
- [ ] **DEBT-AIB-002 — bin-packer C# sequential-layout fidelity.** Editor bin-packer must exactly replicate C# `Sequential` layout (natural alignment capped at 8; padding before `fixed`/`[InlineArray]` fields), else advisory offsets diverge from compiled `Marshal.OffsetOf`. S1-2 tests cover `{int,Vector3,bool}`; **add a `fixed`/`[InlineArray]` padding case** before relying on heavy/array variables. AIB-DD §2.

## Found during implementation
- [ ] **DEBT-AIB-008 (P2) — S1-0 runtime-layout test is non-discriminating.** `Emit_BoolField_CarriesMarshalAsI1` uses `{int,bool,int}`; alignment padding makes `OffsetOf(C)==8`/`SizeOf==12` hold for both 1- and 4-byte bool, so the Roslyn/Marshal check doesn't prove the fix (the source-level `[MarshalAs]` assertions in the same test *do* guard the regression). Add a discriminating layout (e.g. `{bool;byte}` ⇒ SizeOf 2 vs 8). Low risk. Found BATCH-01.
- [ ] **DEBT-AIB-009 (P2) — S1-1 not wired into live render path.** `BlackboardAuthoringWindow.cs:375` calls `BuildViewModel` without `actionSchemaExporter`/`boundActionFqns`, so `HardcodedDtoFields` is always empty in the live editor and nothing ImGui-renders it yet. VM contract (S1-1 tests) met. **Close at S1-5/S1-G** (source bound FQNs + exporter; render read-only). Found BATCH-01.

## Deferred / out of scope (not in S1/S2 task list)
- [ ] **DEBT-AIB-003 — authored heavy-DTO (>100 B) struct generation.** No generator emits a heavy DTO struct from authored variables today (verification "Claim 6 = NOT-FOUND"). Heavy params stay **hand-written** via `[SharedAiHeavyAction]` + `BehaviorDefinition.HeavyDtoType` for now. A demo needing >100 B uses a hand-written heavy DTO. Future: authored-heavy generation. AIB-DD §2, §4.4.
- [ ] **DEBT-AIB-004 — shared blackboard.** Per-instance working state is isolated; shared mutable state is a separate design pass: first iteration = single-behavior single-entity scratch in a `BlueprintBlackboard*` tier; squad-scope = read the **virtual squad-leader entity's** blackboard (existing hill-attack/`Hrot.SquadCoordination` concept). Multi-entity synchronous shared mutation is out of scope (determinism). AIB-DD §4.4; SLICE2-DESIGN §7.
- [ ] **DEBT-AIB-005 — blueprint-authored AiPrimitive demo.** First demos (S1-G/S2-G) use **hardcoded** action/condition DTOs reflected read-only in the panel (S1-1). A fully **blueprint-authored** AiPrimitive equivalent (author the action + its Params/WorkingState/return as a blueprint, bind in a BTree) is a follow-up demo — same memory model, no rework. SLICE1-DESIGN §5.

## Watch-items / verify-at-kickoff
- [ ] **DEBT-AIB-006 — Slice 2 phase-wiring re-verification.** The Slice-2 fixes assume specific ECS phase ordering (`Input` vs `Simulation` vs `BeforeSync`/`BlueprintMaintenanceSystem`) and `BehaviorIngressSystem`/`AiHotReloadCoordinator` hooks. Referenced systems verified to exist; **re-confirm exact phase wiring + `BlueprintBlackboardPartitions` API surface at Slice 2 kickoff** before implementing S2-2/S2-3. AIB-DD §4.3.
- [ ] **DEBT-AIB-007 (NOT ours, pre-existing) — `MigrationEquivalenceTests` byte-stability.** 2 cases (`BTree_SampleScout_…CarriesLayout`, `Hsm_SampleGuard_…CarriesLayout`) in `Hrot.AiEditor.Generators.Tests` assert JSON byte-stability of committed assets; they fail independently of this workstream. **Do not chase; do not let them mask new regressions** (count them out when reading that suite).

## Resolved
- [x] **VE-DEBT-002 path** — "no typed field to bind a DTO-param condition." The binding mechanism is designed (baked-offset projection of an authored variable's DTO); **closes when S1-G passes** (real condition bound to an authored variable, compiling + running). Update the original VE-DEBT-002 entry once S1-G is green.
- [x] PREREQ-A (JSON BTrees execute real bound actions; bridge injects populated registry; live `CgfSubsystem` no longer discards JSON behavior defs) — done `8eb45e0c`.
- [x] `FolderIcons_ResolveAndAreDistinct` test relaxed (`8ee40a33`) — folder/folder_open intentionally share a cell.
