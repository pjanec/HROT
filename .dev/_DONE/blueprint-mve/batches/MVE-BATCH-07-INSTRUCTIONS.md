# MVE-BATCH-07: hot-reload — recompile a RUNNING blueprint; new code goes live, state reconciled

Closes the user's full lifecycle: load/author → compile → run/debug → save → **hot-reload**. The machinery already
exists (MVE-05 wired `QuickReloadService` to the "Compile / Reload Blueprint" toolbar button; the kernel re-resolves
the definition every tick). This batch is mostly **PROOF + reconciliation semantics + a confirmed-debt writeup** — NOT
new runtime. NO codegen / golden change.

## Onboarding
1. `.dev/.guides/DEV-GUIDE_claude.md`; `.dev/blueprint-mve/DESIGN.md`; the MVE-05 and MVE-06 reports/reviews.
2. The lead has already traced the mechanism (cite these, confirm against the code yourself):
   - `BlueprintTickSystem.Execute` re-resolves `_registry.TryGetById(slot.BlueprintId, out def)` **every tick per slot** (`Systems/BlueprintTickSystem.cs:85,133,181`) — so a mid-run registry swap is picked up next tick (no per-entity caching).
   - Reconciliation (`BlueprintTickSystem.cs:87-99`): if `slot.StructureHash == def.StructureHash` → **state preserved**, new `Tick` runs; if different → `ResetSlot`+`InitDefault` → **hard reset** (logged `OnHardReset`).
   - `QuickReloadService.TriggerAsync` (`Reload/QuickReloadService.cs`): compiles the in-memory asset → ALC → registrars → `_session?.RegisterDebugMap(result.DebugMap)` (line ~160, re-registered every reload) → `_coordinator.ApplyQuickReload` → `BlueprintRegistry.CommitStaging` (atomic swap).
   - The existing MVE-05 "Compile / Reload Blueprint" toolbar button in `EditorSubsystem` IS the hot-reload trigger (first call = compile-on-demand; subsequent calls while running = hot-reload). Confirm; no new button needed.
   - Reuse the MVE-05 test harness: `BlueprintTestFixture`, `BlueprintRunHarness`, `BlueprintCompileOnDemandMveTests` (`Hrot.Blueprints.Tests/Runtime/`), and `BlueprintAssetBuilder` (`Tests/Builders/`).
Use codebase-memory MCP; not search_code. GizmoMap.Contracts 0.2.2; no Hrot.IG/DDS.

## VERIFY FIRST (cite file:line)
- **`StructureHash` independence:** confirm `StructureHash` is derived from the **state-field layout** (variables: names/types/offsets), NOT from the Tick graph/logic — i.e. changing only a Tick literal/body (same variables) keeps `StructureHash` constant → state preserved on reload. (Look at where `StructureHash` is computed — `FieldLayout`/the IR. If a pure Tick-body change DOES alter `StructureHash`, say so and switch the "behavior-only" test to whatever IS layout-stable; document the finding.)
- **`BlueprintAssetBuilder` increment expressiveness:** can it author a Tick that increments an int variable by a literal (e.g. `GetVariable Count → Add k → SetVariable Count`)? (The MVE-05 test comment at `BlueprintCompileOnDemandMveTests.cs:124-127` implies the compiler generates this from a real graph.) Determine the exact builder API. This decides the observable below.

## Task 1 — headless hot-reload proof (through the REAL kernel + REAL QuickReloadService)
New `BlueprintHotReloadMveTests` (Blueprints.Tests/Runtime or ClusterRunner.Integration.Tests — wherever the real kernel + QuickReloadService host cleanly, mirroring MVE-05/06). Single blueprint, single entity, NO re-attach across reload:
- **1a — behavior change + state preserved (same StructureHash):** Build an in-memory Instance asset v1 (var `Count:int`, Tick increments Count). `TriggerAsync(v1)` → attach to a self-created entity → `PumpFrames(N)` → assert `Count == <v1 delta>*N`. Then modify the **in-memory asset** to v2 with a DIFFERENT per-tick delta but the SAME variables (same `StructureHash`) — preferred: v1 increments by 1, v2 by 2; fallback if the builder can't vary the literal: v1 = empty Tick (`Entry().Return()`, Count stays 0), v2 = increment Tick. `TriggerAsync(v2)` (the hot-reload) → WITHOUT re-attaching, `PumpFrames(M)` on the SAME entity → assert Count **continued from its pre-reload value** (NOT reset) and advanced at the **v2 rate** (proves new code is live AND state preserved). Assert real numbers.
- **1b — structural change → hard-reset reconciliation:** From a running entity with `Count` advanced, reload a v3 that changes the **state layout** (e.g. adds a second variable → different `StructureHash`). Pump 1+ frames → assert the slot was hard-reset (`Count` back to `InitDefault`/0) — proving `BlueprintTickSystem`'s `StructureHash`-mismatch reconciliation. (If wiring an `IReloadLogSink` to observe `OnHardReset` is cheap, assert it fired; otherwise assert the observable reset value.)
- **1c — observe survives reload:** after 1a's v2 reload, `session.CaptureLiveState(entity, assetId)` (MVE-06) returns `FieldValues["Count"]` equal to the live post-reload value — proving the DebugMap re-registered on reload keeps the inspector correct.

## Task 2 — confirm + clarify the editor trigger (light touch)
Confirm the existing "Compile / Reload Blueprint" toolbar action drives `QuickReloadService.TriggerAsync` for the active asset and thus hot-reloads a running blueprint. If a one-line status/label clarification helps (e.g. the status text noting "recompiled & hot-reloaded — running instances pick up new code next tick"), make it; keep callbacks ImGui-free/headless-friendly. Do NOT add a separate button or new runtime. If nothing needs changing, say so in the report (proof + docs are the deliverable).

## Task 3 — upgrade DEBT-MVE-003 with the confirmed mechanism (REQUIRED)
The lead confirmed (cite in the debt entry): `BlueprintRegistry.CommitStaging` **fully replaces** the snapshot (`BlueprintRegistry.cs:117-138`); `CSharpEmitter.EmitRegistrarClass(asset)` emits a registrar for **exactly one asset per compile** (`CSharpEmitter.cs:103`); `QuickReloadService` invokes only that one assembly's registrar into staging (`QuickReloadService.cs:120-157`); and `AiHotReloadCoordinator` tracks a **single `_currentAlc`** it unloads on the next reload (`AiHotReloadCoordinator.cs:188-190`). **Consequence:** with >1 editor-compiled blueprint, a quick-reload of one blueprint (a) WIPES all sibling definitions from the registry (full-replace with a single-entry staging buffer) AND (b) leaves siblings' `Tick`/`InitDefault` delegates dangling in ALCs that get unloaded → crash on next tick. This is invisible to the single-blueprint MVE tests. Update `DEBT-MVE-003` to **P1 / production blocker for multi-blueprint editor use**, with the confirmed root cause and a fix sketch (e.g. seed staging with all live defs except the recompiled id before committing → carry-forward; PLUS per-asset ALC tracking — or a merge-commit registry mode + multi-ALC retention — since carry-forward alone still dangles other ALCs). Do NOT implement the fix here (architectural; out of MVE scope) — document it loudly.

## Success Criteria
- [ ] Hot-reload proof tests (1a behavior-change+state-preserved, 1b structural hard-reset, 1c observe-survives-reload) green through the real kernel + real `QuickReloadService`, single entity, no re-attach. Real value assertions.
- [ ] Editor trigger confirmed (Task 2) — documented; minimal/no change.
- [ ] DEBT-MVE-003 upgraded with the confirmed mechanism + fix sketch (Task 3).
- [ ] Build 0 errors; touched projects no new warnings. **Zero golden/snapshot/codegen change** (confirm the emit goldens are untouched). GizmoMap.Contracts 0.2.2.
- [ ] Green: new tests; `EditorSubsystemBoot` filter (still boots); `Hrot.Blueprints.Tests` (only the 10 pre-existing DEBT-006 failures, 0 new; flaky sub-80ns perf re-run isolated); `Hrot.Editor.AiShared.Tests`.
- [ ] Report at `.dev/blueprint-mve/reports/MVE-BATCH-07-REPORT.md`.

## Execution rules — YOU (the sonnet agent) run the full implement→build→test→fix loop yourself
Verify the two VERIFY-FIRST items against the code FIRST (cite). Reuse `QuickReloadService`/`BlueprintTickSystem`/the MVE harness — do NOT reimplement compile, the registry swap, or the tick loop. The behavior-change test MUST keep `StructureHash` constant across v1→v2 (else it's a hard-reset, not a behavior swap — different assertion). Gate any ImGui. Build + run the suites yourself; reach green; never fake a pass. Do NOT commit.

## Report
Document: the two verify-first findings (StructureHash independence; builder increment API) with citations; the chosen observable; the three hot-reload tests + exact counts/values; the editor-trigger confirmation (+ any one-line change); the DEBT-MVE-003 upgrade; explicit confirmation of zero golden/codegen change; build + suite results; and that this CLOSES the MVE lifecycle (with multi-blueprint robustness as the remaining tracked debt). Suggested commit message. No comprehension questions.
