# BTree / HSM Visual Editing — Task Tracker

One line per task. Check the box when implemented, **verified green**, reviewed, and committed.
Full specs in [TASK-DETAIL.md](./TASK-DETAIL.md); deferred issues in [DEBT-TRACKER.md](./DEBT-TRACKER.md).
Design of record: [docs/blueprints/BTree_HSM_Editor_State_And_Forward_Plan.md](../../docs/blueprints/BTree_HSM_Editor_State_And_Forward_Plan.md)
(feature/UX detail: the `BTree_Editor_NodeEditor_Host_Design.md` / `HSM_Editor_NodeEditor_Host_Design.md` host docs).

Status: `[ ]` open · `[~]` in progress · `[x]` done (verified + committed) · `[!]` blocked.
**Phases are sequential** — finish (build + tests green + review) before starting the next.

---

## Working agreement — implementing agent (Zoo)

> Zoo is a capable executor for **small, single-objective** tasks but is **not autonomous-trustworthy**.
> These rules are MANDATORY and must be restated in every batch instruction.

1. **One task per batch.** Do not combine tasks. Do not touch code outside the task's named files/scope. Do not re-litigate or edit files committed by other tasks/workstreams.
2. **No cheating to make the build/tests pass.** NEVER: exclude/delete a user asset from compilation, comment out or `<Compile Remove>` a file, suppress a diagnostic/warning, `#pragma warning disable`, weaken an assertion, or stub a feature to dodge a hard error. If blocked, **stop and write the blocker in the report** — do not paper over it.
3. **Finish without asking.** Run the build and the named test project(s), diagnose root causes, fix, and repeat **until `Failed: 0`** — then write the report. No "is it OK to run tests?" permission-asking. No "complete" while anything is red.
4. **Headless only.** Zoo verifies via build + unit tests (headless). Tasks below mark a **[VISUAL GATE]** where pixel-level appearance must be confirmed by the lead in the running editor — Zoo is NOT responsible for that, but MUST make the underlying logic headless-testable (assert model values, strings, enums — not screenshots).
5. **Tests verify behavior, not strings.** No `Assert.Contains`-on-generated-text as the only check; assert actual values/enums/offsets. A broken implementation must fail the test.
6. **Litter-free.** No debug `File.WriteAllText`, no scratch files, no leftover `Console.WriteLine`. Leave the tree clean.
7. **Report = truth.** The report must match the diffs. The lead reviews diffs + assertions, not the prose.

**Verification baseline (run WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`):** see [TASK-DETAIL.md §Verification](./TASK-DETAIL.md#verification--baseline). Pre-existing failures are listed there and must stay a *subset* (0 new).

**Folder convention:** batch instructions → `batches/BATCH-XX-INSTRUCTIONS.md`; agent report → `reports/BATCH-XX-REPORT.md`; questions → `questions/BATCH-XX-QUESTIONS.md`; lead review → `reviews/BATCH-XX-REVIEW.md`.

---

## Phase A — BTree to usable (do first; unparks `REVIEW-BB1(BTree)`)

- [x] **TASK-BT-01** Live action/condition palette (wire `IActionSchemaExporter` → `BTreeNodeCatalog`) → [details](./TASK-DETAIL.md#task-bt-01--live-actioncondition-palette) *(BATCH-01, verified+committed; visual confirm deferred to REVIEW-BT)*
- [x] **TASK-BT-02** Node colors by kind (composite/leaf/decorator) **[VISUAL GATE]** → [details](./TASK-DETAIL.md#task-bt-02--node-colors-by-kind) *(BATCH-02, verified+committed; pixel confirm → REVIEW-BT)*
- [x] **TASK-BT-03** Pill glyph + param label (↺×3 / ⏲2s) **[VISUAL GATE]** → [details](./TASK-DETAIL.md#task-bt-03--pill-glyph--param-label) *(BATCH-03, verified+committed; pixel confirm → REVIEW-BT)*
- [x] **TASK-BT-04** Validators → Diagnostics window (register `BTreeAssetValidator`) → [details](./TASK-DETAIL.md#task-bt-04--validators--diagnostics-window) *(BATCH-04, verified+committed)*
- [x] **TASK-BT-05** Validation inline on canvas (node `State`/tooltip; inspector banner deferred per D-04) **[VISUAL GATE]** → [details](./TASK-DETAIL.md#task-bt-05--validation-inline-on-canvas) *(BATCH-05, verified+committed; pixel confirm → REVIEW-BT)*
- [x] **TASK-BT-06** Showcase `.btree.json` + Starter recipe → [details](./TASK-DETAIL.md#task-bt-06--showcase-btree--starter-recipe) *(BATCH-06+06B(rej)+06C, verified+committed; OBSERVES/real-condition deferred → VE-DEBT-002)*
- [ ] **TASK-BT-07** *(optional — DEFERRED)* In-process quick reload for BTree (PU-09) → [details](./TASK-DETAIL.md#task-bt-07-optional--in-process-quick-reload) *(large/risky; lead-handled or post-REVIEW-BT)*
- [ ] **REVIEW-BT** *(USER visual smoke — pending)* — run the editor, open CombatShowcase: confirm category colors (BT-02), pill glyph+param (BT-03), inline validation outline/⚠ (BT-05), specific actions in palette (BT-01); note OBSERVES/real-condition gap (VE-DEBT-002) + deferred inspector banner (BT-05b). Then signal blueprint-finalize to run **REVIEW-BB1(BTree)**.

## REVIEW-BT findings (2026-06-12 user visual smoke) — follow-ups

Confirmed working: pills `R x3`/`C 2s` (BT-03), Macro=violet + Function=blue (BT-02), red validation frame on invalid tree (BT-05). Two CombatShowcase shown (1 rich/JSON, 1 single-node/assembly).
- [x] **TASK-BT-08** Wire Add-Node picker (`BTreePickerSources` → `"nodes.all"`) — root cause: picker source never registered (Blueprint has it). *(BATCH-08, verified+committed; picker-opens-visually → REVIEW-BT-2)*
- [x] **TASK-BT-09** Fix duplicate CombatShowcase — `[BTreeDefinition]` now carries `AssetId` (mirrors HSM); assembly contributor uses it → dedupes vs JSON. *(BATCH-09, verified+committed; lead reverted a showcase scope-creep + verified the 2 Generators.Tests fails are pre-existing on HEAD; one-entry confirmed → REVIEW-BT-2)*
- [x] **TASK-BT-10** **BTree vertical pin orientation** — `PinOrientation` on `GraphKindDescriptor` (default Horizontal); CanvasLayout output-top/input-bottom for Vertical; BTree opts in. Blueprint/HSM unchanged. *(BATCH-10, verified+committed; pixel/wire look → REVIEW-BT-2)*
  - [x] **BT-10b** *(lead, follow-up to user smoke)* Vertical **wire tangents** — `HitTester.WireTangents` + WireRenderer + pending-wire now leave/enter along Y for Vertical graphs (was sprouting sideways like Blueprint). Hit-test matches render. +3 tangent tests. *(lead-implemented+verified, NodeEditor.UI.Tests 62/0)*
- [x] **TASK-BT-11** *(minor)* FlowControl composite color (gray → orange) — one literal in shared `EngineEditorTheme`; +3 value-asserting tests. *(BATCH-11, verified+committed; orange pixel → REVIEW-BT-2)*
- [x] **TASK-BT-12** *(CRITICAL)* Fault-tolerant codegen — emit core throws on emitted-unbound leaf; generator skips asset + `BTREE0002` Warning (not Error); csproj exempts BTREE0002 from TWAE. *(BATCH-12, verified+committed via generator/emit tests + clean build; live full-build-with-invalid-asset proof blocked by an unrelated MSBuild sandbox crash → confirm in REVIEW-BT-2)*
- [x] **TASK-BT-13** Palette offers only **bindable** actions/conditions (DtoType matches blackboard). *(BATCH-13, verified+committed)*
- [x] **TASK-BT-17** *(CRITICAL guarantee)* Generator symbol-check — `BTreeMethodCompatibilityValidator` (Compilation-based) validates each bound method vs `NodeLogicDelegate<TBB,TCtx>`; incompatible/unresolved → skip+BTREE0002. Sound (no false-pass). Build can never break from any binding. *(BATCH-17, sonnet-implemented + lead-verified, committed)*
- [x] **TASK-BT-14** *(CRITICAL)* Emit cycle guard — path-visited DFS pre-pass throws on cycle (caught → BTREE0002); no more uncatchable StackOverflow. *(BATCH-14, verified+committed)*
- [x] **TASK-BT-15** *(CRITICAL)* Single-parent + no-cycle on wire — `ApplyAddLink` detaches child from old parent, rejects self-parent/cycles. Fixes "disappearing links" + stops cycle creation. *(BATCH-15, verified+committed)*
- [x] **TASK-BT-16** Break-link for projected links — `ApplyRemoveLinks` resolves via the graph model; deletes JSON-loaded + session links. *(BATCH-16, verified+committed)*
- [x] **TASK-BT-18** *(lead, REVIEW-BT-2 findings)* (1) Manual-connect parent/child was inverted when the drag started on a bottom Input pin — `ApplyAddLink` now resolves child/parent by pin **direction**, not From/To order (rejects same-direction). (2) Drag-from-pin→canvas→pick created the node but didn't auto-wire — BTree's derived pin IDs never matched the canvas's pre-generated ones; `BTreeEditorNode` now adopts supplied `PinIds` (session override) so the auto-wire link resolves. +5 tests. *(lead-implemented+verified, BTree.Editor.Tests 511/0)*
- [ ] **REVIEW-BT-2** re-run visual smoke after BT-08..18 (incl. add-node→wire→build-survives, break-link, re-parent, vertical pins+wires, color, **drag-to-create auto-wire**, **connect-direction both ways**).

## Phase B — HSM to usable (after Phase A pattern is proven)

- [x] **TASK-HS-01** Command sink: **create state** (`ApplyAddNode`, incl. promote-to-composite) → [details](./TASK-DETAIL.md#task-hs-01--command-sink-create-state) *(BATCH-HS-01, verified+committed; adds HsmAsset Register/Unregister State+Transition API; promotion automatic via Kind/reparent; Hsm.Editor.Tests 390/0)*
- [x] **TASK-HS-02** Command sink: **delete state** (`ApplyRemoveNodes` full cascade) → [details](./TASK-DETAIL.md#task-hs-02--command-sink-delete-state) *(BATCH-HS-02, verified+committed; full subtree cascade + incident transition cleanup + BB1 auto-var removal; Hsm.Editor.Tests 402/0)*
- [x] **TASK-HS-03** Command sink: **draw transition** (`ApplyAddLink` → new `TransitionNode`) → [details](./TASK-DETAIL.md#task-hs-03--command-sink-draw-transition) *(BATCH-HS-03, verified+committed; HsmAsset pin→state resolvers + validator-gated ApplyAddLink; projects to Links; Hsm.Editor.Tests 408/0)*
- [x] **TASK-HS-04** Command sink: **delete transition** + container collapse (`ApplyRemoveLinks` create-path, `ApplySetContainerCollapsed`) → [details](./TASK-DETAIL.md#task-hs-04--command-sink-delete-transition--collapse) *(BATCH-HS-04, verified+committed; ApplyRemoveLinks→shared helper fixes dangling-map bug; collapse impl; Hsm.Editor.Tests 417/0)*
- [x] **TASK-HS-05** Initial-state arrows (finish `HsmInitialArrowRenderer` TODO) **[VISUAL GATE]** → [details](./TASK-DETAIL.md#task-hs-05--initial-state-arrows) *(BATCH-HS-05, verified+committed headless; CollectInitialMarkers + ComputeMarkerGeometry + draw; pixels → REVIEW-HS; Hsm.Editor.Tests 423/0)*
- [x] **TASK-HS-06** Validation surfacing: Diagnostics + node state + feed `HsmRegionConflictsRenderer` **[VISUAL GATE]** → [details](./TASK-DETAIL.md#task-hs-06--validation-surfacing) *(BATCH-HS-06, verified+committed headless; validator registered + StateNode diag-state + decoupled renderer feed; Hrot.Editor builds clean; pixels → REVIEW-HS; Hsm.Editor.Tests 432/0)*
- [x] **TASK-HS-07** Showcase `.hsm.json` + Starter recipe → [details](./TASK-DETAIL.md#task-hs-07--showcase-hsm--starter-recipe) *(BATCH-HS-07, verified+committed; rich showcase + Starter; guards null per VE-DEBT-004; **fixed latent HSM-codegen build-break** HsmBridgeEmitCore lambda→fn-ptr; layout → REVIEW-HS)*
  - [x] **HS-07-FIX (boot crash)** showcase crashed the editor at boot — generated builder did `GoTo("Scanning")` before Scanning declared (emitter emits nested-child transitions inline; forward-ref → `FindState` throws). Reordered GuardComposite children topologically (all backward refs) + added runtime `HsmShowcase.Compile()` execution-guard test. Verified Hrot.AI.Behaviors builds + **Compile() executes** 0-throw; Hsm.Editor.Tests 458/0. Underlying emitter limitation → **VE-DEBT-006** (P1, nested-child two-pass).
- [x] **TASK-HS-08** Appearance/loop hardening + verify Events-table/Globals-strip perspective wiring → [details](./TASK-DETAIL.md#task-hs-08--appearance--loop-hardening) *(BATCH-HS-08, verified+committed; create→edit→save→reopen round-trip proven via real HsmAssetMapper+HsmJsonServices, topology+layout+collapse preserved; Hsm.Editor.Tests 456/0. **Events/Globals window wiring deferred → VE-DEBT-005**; rendering → REVIEW-HS)*
- [!] **DEBT-BF-04** HSM-state 4-slot param binding — **architect design call** (NOT a Zoo task; blocks `REVIEW-BB1(HSM)`) → see [DEBT-TRACKER.md](./DEBT-TRACKER.md)
- [ ] **REVIEW-HS** *(lead/user visual smoke)* — full HSM authoring pass; then (after DEBT-BF-04) signal `REVIEW-BB1(HSM)`.

---

## Progress

Phase A: **functional ✅** (BT-01..06 + REVIEW-BT follow-ups BT-08..18; BT-07 optional-deferred). REVIEW-BT-2 wiring confirmed by user.
Phase B: **8/8 headless ✅** (HS-01..08, Hsm.Editor.Tests 456/0; Hrot.AI.Behaviors builds clean with the showcase). Also fixed a latent HSM-codegen build-break (HsmBridgeEmitCore lambda→fn-ptr). **Remaining for Phase B:** REVIEW-HS (user/lead visual gate — initial arrows, region-conflict overlay, container/history rendering), VE-DEBT-005 (Events/Globals window doc-retarget wiring), DEBT-BF-04 (HSM 4-slot state param-binding — architect design call, blocks REVIEW-BB1(HSM)).
Deferred debt: BT-05b inspector banner, VE-DEBT-002 (BTree OBSERVES/real-condition), VE-DEBT-004 (HSM real-guard binding), VE-DEBT-005 (HSM events/globals windows).

## Done-definition for this thread

A content author can create, open, build, bind real actions, see validation, save, and run **BTree** (Phase A) and **HSM** (Phase B) assets entirely from the visual editor — making `REVIEW-BB1` runnable. Runtime debugging (overlays, breakpoints, stepping) is **out of scope** (Phase 2, separate thread).
