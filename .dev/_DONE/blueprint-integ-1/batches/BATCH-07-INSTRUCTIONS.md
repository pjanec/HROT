# BATCH-07: Blackboard binding + Save→emit→hot-reload loop (completes Phase 2)
**Tasks:** AIE-025, AIE-026   **Phase:** 2   **Est:** ~13h
**Dependencies:** BATCH-04 (composition), BATCH-05/06 (host binding, command sinks, inspector).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — working contract.
2. `.dev/blueprint-integ-1/DESIGN.md` §4.5, §5.4; `.dev/blueprint-integ-1/TASK-DETAIL.md` AIE-025, AIE-026 — success conditions.
3. `.dev/blueprint-integ-1/reviews/BATCH-06-REVIEW.md` — current state.

Use **codebase-memory MCP** first (project `D-Work-IOS-IG-SimHost-FDP-2`); not `search_code`. Headless tests must not call ImGui without a context.

## Task 1: Blackboard Authoring bound to active asset (AIE-025) — composition wiring + `BlackboardAuthoringWindow`
The `BlackboardAuthoringWindow` is already registered per BTree/HSM perspective (BATCH-03/04 `PerspectiveWorkspaceRegistrar`). Remaining work: bind it to the **active asset's** blackboard schema (retarget when the perspective's active asset changes, via the selection store / `AiDocumentManager.ActiveChanged`), reading the asset's `BlackboardVariables` (the editor assets implement `IBlackboardManagedAsset`). Tolerate **no aggregator** (Phase 5 supplies strategies) — show explicit variables only, no throw.
**Tests required:** `BlackboardWindow_BindsActiveAssetSchema` (active BTree/HSM asset → window view-model lists its blackboard variables); `BlackboardWindow_NoAggregator_ShowsExplicitVarsOnly_NoThrow`; `BlackboardWindow_RetargetsOnActiveAssetChange`. Keep existing `BlackboardAuthoringWindowTests` green.

## Task 2: Save → emit → hot-reload loop (AIE-026) — files: new `Hrot/Editor/Hrot.Editor.AiShared/Emit/RegenerationScheduler.cs` + command-sink wiring + composition
Implement the deterministic save loop for BTree/HSM (DESIGN §4.5):
- **`RegenerationScheduler`** (AiShared/Emit): debounces rapid edits (e.g. drag → many `MoveNodes`) into a single save (use an injectable clock/dispatch so it's unit-testable without real timers — do NOT use wall-clock `Task.Delay` in a way tests can't control; inject a scheduler/`Action` flush or a test-driven `Tick`).
- **Emit on dirty:** when a command sink marks the asset dirty, schedule a regen; on flush, run the kind's fluent emitter (`BTreeFluentEmitter`/`HsmFluentEmitter`, existing) to produce C#, then **atomic write** (byte-compare → skip if identical → temp-file + `File.Move`). Verify the emitters' actual API and the existing `FluentCSharpEmitterBase`/atomic-write helper; reuse, don't duplicate.
- **Reconciliation on reload:** on `_aiCoordinator.OnReloadCompleted`, the contributors re-project; ensure the open document's model reconciles (positions/comments by `VisualId`/`StableId`) and the canvas refreshes. Mirror existing projector reconciliation; if a helper exists use it.
- **Blueprint:** Blueprint dirty → route to the existing `QuickReloadService` (do not rebuild that pipeline). Light wiring only — full Blueprint editing is Phase 4; here just ensure a dirty Blueprint can trigger Quick Reload through the shared path if already open.
**Tests required:** `RegenerationScheduler_DebouncesBurst_IntoSingleSave` (N rapid schedules → 1 flush, deterministic via injected tick); `Save_BTree_EmitsDeterministicCSharp` (emit a known asset → expected C#; re-emit unchanged model → **byte-identical**, atomic write is a no-op); `Save_Hsm_EmitsDeterministicCSharp`; `Reload_ReconcilesModel_ByStableId` (positions/comments preserved across a simulated reload). Prefer real emitter output assertions over string-contains; if asserting generated code, assert structural/byte-identity, not substrings.

## Success Criteria
- [ ] AIE-025, AIE-026 per success conditions; **Phase 2 / M-Authoring complete** (open→edit→inspect→save→reload works for BTree/HSM).
- [ ] Green (full, no crashes): `Hrot.Editor.AiShared.Tests`, `Hrot.BTree.Editor.Tests`, `Hrot.Hsm.Editor.Tests`, `EditorSubsystemBoot` filter. `Hrot.Blueprints.Tests` no new failures beyond DEBT-006's 10.
- [ ] No warnings; docs; no leftover TODO/debug.
- [ ] Report at `.dev/blueprint-integ-1/reports/BATCH-07-REPORT.md`.

## Execution rules
- Tasks in sequence; run the named suites yourself; fix root causes; never fake a pass or assert NotNull/contains-only.
- Verify the fluent emitters' real API + the atomic-write/`FluentCSharpEmitterBase` helpers before use; reuse existing reconciliation. If a real API contradicts the batch, follow the code and note it.
- The `RegenerationScheduler` must be unit-testable deterministically (no uncontrolled real timers in tests).

## Report Requirements
In `reports/BATCH-07-REPORT.md`: the scheduler's debounce/test-control design; the emitter API used + how byte-identical determinism is asserted; the reconciliation approach; the Blueprint Quick-Reload hookup; actual test counts (all named suites); confirm `EditorSubsystemBoot` stays 10/10; suggested commit message. No comprehension questions.
