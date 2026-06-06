# Blueprint Integration Finalization -- Task Tracker

One line per batch. Check the box when the batch is implemented, verified green, and committed.
Full descriptions, scope, and key files are in [TASK-DETAIL.md](./TASK-DETAIL.md).
Per-batch instructions live in `batches/`, reports in `reports/`.

Status legend: `[ ]` open / `[~]` in progress / `[x]` done (verified + committed). Do not delete rows.

---

## Phase 0 -- Hardening (P1 + observability)

- [x] **BATCH-01** -- DEBT-MVE-003 P1: multi-blueprint quick-reload safety (registry merge-commit + per-asset ALC) -> commit `2d06f741` -> [details](./TASK-DETAIL.md#batch-01----debt-mve-003-multi-blueprint-quick-reload-safety)
- [x] **BATCH-04** -- DEBT-MVE-002: emit `StateFields` in Instance codegen (durable observe by field name) -> committed -> [details](./TASK-DETAIL.md#batch-04----debt-mve-002-emit-statefields-in-codegen)

## Phase 1 -- Authoring: node value pins

- [x] **BATCH-02** -- Task 3: node value pins for all node kinds (ReadRankedResult / CallCustomEvent / CallPeerBlueprint return) -> committed -> [details](./TASK-DETAIL.md#batch-02----task-3-node-value-pins-for-all-node-kinds)

## Phase 2 -- Tasks 1+2: in-blueprint function-graph calls (Option B, full feature)

- [x] **BATCH-03A** -- Compiler core: `FunctionCallNode.TargetGraphId` + `IrOp_GraphCall` + Entry-input binding + emit-as-method + BP1650 latent-forbidden + e2e test -> committed -> [details](./TASK-DETAIL.md#batch-03a----compiler-core-in-blueprint-function-graph-calls)
- [x] **BATCH-03B** -- Compiler: validation hardening (BP1651 target, BP1652 arg-count, BP1653 arg-type, BP1654 recursion/cycle) + negative tests -> committed -> [details](./TASK-DETAIL.md#batch-03b----compiler-validation-hardening)
- [x] **BATCH-03C** -- Editor projection: Entry/Return value pins from `Graph.Inputs/Outputs` + FunctionCall mirrors in-blueprint target-graph signature -> committed -> [details](./TASK-DETAIL.md#batch-03c----editor-projection-entryreturn-value-pins--functioncall-mirrors-graph-signature)
- [x] **BATCH-03C2** -- Editor: `CallPeerBlueprintNode` arg pins via extended `BlueprintSignature` (per-function Inputs/Outputs) + sibling-signature lookup threaded to projection AND live-wired in `EditorSubsystem` (BATCH-02 deferral) -> committed -> [details](./TASK-DETAIL.md#batch-03c2----callpeerblueprint-arg-pins-via-extended-blueprintsignature)
- [x] **BATCH-03D1** -- Editor UI: FunctionCall Details drawer (CLR type/method fields + in-blueprint Function-graph picker + IsPure) -> committed -> [details](./TASK-DETAIL.md#batch-03d----editor-ui-functioncall-picker--graph-signature-editing-panel)
- [x] **BATCH-03D2** -- Editor UI: graph-signature editing panel for `Graph.Inputs/Outputs` (`GraphSignatureEditModel` + `GraphSignatureWindow`, bespoke rows panel, graph-picker combo) -> committed -> [details](./TASK-DETAIL.md#batch-03d----editor-ui-functioncall-picker--graph-signature-editing-panel)

## Phase 3 -- Demonstrable authoring

- [x] **BATCH-05** -- `BlueprintMath` library (38 pure fns) + `CountingDemo.bp.json` (increments `Count` via `BlueprintMath.AddInt`) + compile-and-run proof (Count 0→5) -> committed -> [details](./TASK-DETAIL.md#batch-05----task-6-canvas-authorable-counting-demo)
- [x] **BATCH-05B** -- Math node-palette entries: 50 `BlueprintMath` presets in the node picker (Math / Math/Int / Math/Compare / Math/Bool / Math/Vector), pins auto-projected by reflection -> committed -> [details](./TASK-DETAIL.md#batch-05----task-6-canvas-authorable-counting-demo)
- [x] **MINOR FOLLOW-UP (DEBT-MVE-004) — RESOLVED by BP-2 Stage0_Rehydrate** (compiler now rehydrates pins from registry+authored-state on the pins-empty load path; open-saved-blueprint→compile works).
- [~] (orig) -- canvas round-trip pins: RESOLVED for authoring — the compiler reads `node.Pins` directly (no hydration pass), and the editor DOES populate `node.Pins` in-memory when a node is added (`BlueprintCommandSink.cs:225`); save clears→restores pins (`SaveActiveBlueprintCommand.cs:84/95`) so disk stays projection-only (`"Pins": []`) while in-memory keeps pins for compile. So canvas-authored data-flow graphs compile. Remaining nuance to VERIFY: does **loading** a saved `"Pins": []` file re-hydrate `node.Pins` before a compile (vs only on node-add)? If not, "open saved blueprint → compile" needs a load-time hydration pass. Pre-existing; not introduced by BATCH-05. `CountingDemo.bp.json` ships with explicit pins so it loads+compiles regardless.

## Phase 4 -- Canvas polish backlog (Task 4, lower priority)

- [x] **BATCH-06** -- ChannelCommand param enrichment (DEBT-BCP-006) — real ParamsTypeFqn for MoveTo/FollowRoute/AimAndFire/OpenDoor; EjectPassengers has no params -> [details](./TASK-DETAIL.md#batch-06----channelcommand-param-enrichment-debt-bcp-006)
- [x] **BATCH-07** -- Inline mini-editors for node value pins (real PinDefaultValueEditorRegistry; persisted via Node.PinDefaults; visual pending user verify) -> [details](./TASK-DETAIL.md#batch-07----inline-mini-editors)
- [~] **BATCH-08** -- Fonts: engine multi-size atlas — DEFERRED (lead): engine-level font RENDERING (no NodeEdit wire-up path); genuinely needs visual/engine iteration in the running editor, not headless-doable with confidence. -> [details](./TASK-DETAIL.md#batch-08----fonts-multi-size-atlas)
- [~] **BATCH-09** -- Comments / reroutes / containers — READY but DEFERRED pending a visual checkpoint: NodeEdit infra exists (ICommentModel/IContainerNodeModel, renderers, demo Fakes S06/S26/S27/S35) so it's wire-able, BUT it's a LARGE multi-feature batch that adds NEW persisted asset model (comment boxes/containers/reroutes — must stay JsonIgnore-when-empty for byte-stability) and is deeply visual (unverifiable headlessly). Recommend visually smoke-testing BATCH-06/07 first, then do this with quick visual course-correction. -> [details](./TASK-DETAIL.md#batch-09----comments--reroutes--containers)

## Phase 4.6 -- Build-break + live-editor fixes (session 2026-06-06)

- [x] **NODESTATUS** -- AiPrimitive/function-graph emit `global::Fbt.NodeStatus` (fix CS0234 in the game assembly + the inverted Success/Failure ordinal cast) -> commit `908b8a2f` -> [details](./TASK-DETAIL.md#nodestatus----emit-fbtnodestatus)
- [x] **UX1** -- reload-on-edit gate (no Roslyn recompile on node move/edit; opt-in default-false) + ChannelCommand pin-collapse fix (ApplyPinIds passes channel catalog) + selection->Details bridge (was never wired in prod) + delete stub `GraphEditorWindow` -> commit `3a53c235`; **needs running-editor re-test** -> [details](./TASK-DETAIL.md#ux1----live-editor-usability)
- [x] **FIXEDSTRING** -- `Fdp.Core.FixedString32/64` as blueprint string pin types (StaticTypeRegistry + BlueprintTypeSystem + host StringPinEditor registration + ParseValue + demo) -> commit `2bc9ae11` -> [details](./TASK-DETAIL.md#fixedstring----fdpcorefixedstring3264-pin-types)

## Phase 5 -- Unified behavior-action nodes + enums
**Design (CONVERGED, architect-reviewed):** [ENUM-DESIGN.md](./ENUM-DESIGN.md) §RESOLVED + [ACTION-NODE-DESIGN.md](./ACTION-NODE-DESIGN.md) §ROUND-2 RESOLVED. Decisions D-A (handle=channel), D-B (one action=one node, action baked at create, pins immutable). Roadmap B1-B6 mapped to batches below.

### Phase 5A -- Autonomous foundation (headless-verifiable; NO visual review needed; intended as one large push)
**STATUS: COMPLETE (2026-06-06). All 6 committed; cumulative gate green — solution build 0 errors;
Blueprints 1539 passed / 4 pre-existing / 0 new; Hrot.Editor.AiShared.Tests 832/832. Commits:
AN2 `176b329c`, AN1 `81227f70`, AN6 `9f8690f7`, AN3 `9d932a73`, AN4 `addeeb9b`, AN5 `cee67887`.**
- [x] **AN1** -- Stage-3 default-literal materialization [B4; architect gotcha]: implement `Stage3_Normalize.MaterializeDefaultPinLiterals` (currently a no-op stub) so unconnected In-data pin defaults reach generated C# -- enum -> `(global::FQN)N`, FixedString32/64 -> `new global::Fdp.Core.FixedStringNN("...")`, primitives -> literal. Pure compiler; golden + compile tests. -> [details](./TASK-DETAIL.md#an1----stage-3-default-literal-materialization)
- [x] **AN2** -- StaticTypeRegistry enum-FQN acceptance [B3-compiler]: accept an enum-typed `BlueprintTypeRef` (FullName=enum FQN, IsUnmanaged=true, SizeBytes=underlying, editor-stamped) as a valid unmanaged type so enum pins/params/vars resolve + pack. (Resolve the reflection-less "how is size known" detail: editor stamps it into the persisted TypeRef.) Headless tests. -> [details](./TASK-DETAIL.md#an2----statictyperegistry-enum-fqn-acceptance)
- [x] **AN3** -- Unified behavior-action catalog [B2-core]: facade `IBehaviorActionCatalog` over `IChannelCommandCatalog` + `IActionSchemaExporter` enumerating all actions `{ FQN/id, Category/Channel, ParamsTypeFqn, validHosts, source }` (channel commands + hardcoded `[BTreeAction]`/`[HsmAction]`/`[SharedAiAction]` + blueprint `AiPrimitive`s). Headless tests. -> [details](./TASK-DETAIL.md#an3----unified-behavior-action-catalog)
- [x] **AN4** -- Per-action palette generation [B2]: emit one palette entry per catalog action (preset channel/actionId or action FQN) over the single `ChannelCommandNode` kind; placing one bakes the action id. Headless test (entries per action; placement bakes props + projects pins). -> [details](./TASK-DETAIL.md#an4----per-action-palette-generation)
- [x] **AN5** -- Immutable action selection [B1]: action fixed at create; `ChannelCommandNodeDrawer` renders ChannelType/ActionId as read-only labels (no Combo). No JSON migration. Headless logic test. -> [details](./TASK-DETAIL.md#an5----immutable-action-selection)
- [x] **AN6** -- Blueprint enum data pins [B3-editor]: `IEnumValueProvider` (reflect project enums) + register `EnumPinEditor` in `BlueprintDocumentFactory` + `BlueprintPinModel.ParseValue` enum case (persist int) + `BlueprintTypeSystem` enum color/name. Headless tests (provider members; registry returns EnumPinEditor; ParseValue round-trips). -> [details](./TASK-DETAIL.md#an6----blueprint-enum-data-pins)

### Phase 5B -- Visual review gate (running editor)
- [ ] **REVIEW-V1** -- user smoke: per-action palette lists actions; dropping one creates an immutable node with baked param pins + read-only action labels; enum-typed pins show a combo; setting an enum default + compile produces `(global::FQN)N` and runs. Fix findings as focused follow-ups.

## Phase 5C -- Generalize to non-channel behavior actions (ROUND-3 design; ACTION-NODE-DESIGN.md §ROUND-3)
The generalized "behavior-action invocation" node (ChannelCommandNode repurposed) dispatches ALL actions, not
just channel commands. AN4/AN5 delivered the channel SUBSET. FunctionCall is NOT used for behavior actions.
- [x] **ENUM-SAMPLE** -- a sample behavior action with an enum-typed parameter so the enum pin editor (AN6) is
      live-testable (combo render + persist + compile). -> [details](./TASK-DETAIL.md#enum-sample----enum-param-action-for-live-testing)
- [ ] **AN7** -- Editor: generalize node + palette to non-channel actions. The node carries an action FQN
      (alongside channel ChannelType/ActionId); palette emits one entry per `ActionSchemaExporter` action (named
      by FQN, via the AN3 unified catalog); `NodePinSchema` projects pins from the non-channel action's
      `ParamsTypeFqn`; drawer shows the action identity read-only (AN5 pattern). -> [details](./TASK-DETAIL.md#an7----generalize-node--palette-to-non-channel-actions)
- [ ] **AN8** -- Compiler (LARGE): lower a non-channel behavior-action invocation in a Blueprint. **UNBLOCKED
      (ROUND-4): model = INLINE-LATENT** — direct synchronous call `(self, ctx, paramsDTO) -> NodeStatus`;
      `Running` → inline `BlueprintLatentCursor` suspend + resume at the SAME node next tick (reuse the
      WaitForChannel latent path); Success/Failure route exec. AiPrimitive working state inline over
      `Blackboard1024` (StructureHash@0, state@8). **Slice-1: one stateful AiPrimitive per entity** (enforce/doc;
      Slice-2 partition allocator is future). No handle, no Wait node. -> [details](./TASK-DETAIL.md#an8----compiler-lowering-for-non-channel-behavior-action-invocation)

## Phase 6 -- BTree/HSM StructEdit inspector + param binding (Blackboard Slice 1.5)
- [x] **SE1** -- Wire `InspectorWindow` -> StructEdit render loop -> commit `2bd9ba67`. Facet fields render live (enum→combo, bool→checkbox, number/string) + composition-root wiring; pickers plain-text (completed in SE2).
- [x] **SE2** -- Per-asset facet picker dropdowns (BTree BehaviorHash/BlackboardField; HSM action/guard/state/event) via re-register on ActiveChanged -> commit `98992bda`.
- [~] **REVIEW-V2** -- SKIPPED per user (overnight run); folded into the morning review (see MORNING-HANDOFF.md): confirm facet rows render+edit, enum combos, picker dropdowns.
- [ ] **BB1+** -- BTree/HSM per-param binding [B6]: project the action DTO's fields -> per-field static literal OR `[BlackboardFieldPicker]` blackboard-var binding; sub-tree sync (Approach A/B). **DEFERRED from the overnight run (STOP-LINE): needs a NEW persisted binding schema + doesn't fit the static-facet model = a design decision to make WITH the user, not blind-built.** Next major task. -> [details](./TASK-DETAIL.md#bb1----btreehsm-per-param-binding)

---

## Pre-existing test failures (NOT regressions -- do not chase)
After BATCH-04 the green baseline for `Hrot.Blueprints.Tests` is **7 failures**, all pre-existing DEBT-006:
AiPrimitiveEmitGolden (×2 cases), LibraryEmitGolden, LibraryMath snapshot, MoveToAndFire snapshot,
ConditionSummary, AllocationFree. Plus the flaky sub-150ns WhenNode perf test under load (DEBT-014; passes
in isolation). Every batch must keep the failing set a SUBSET of these (0 new) unless it intentionally
re-baselines a golden (BATCH-04 resolved the 3 Instance goldens).

**Update (2026-06-06, session-verified):** NODESTATUS re-baselined the AiPrimitive goldens (MoveToAndFire/
HasVisibleTarget) for the `Fbt.NodeStatus` change. A clean run then shows **2 real** failures: `ConditionSummary`
"ScoreCrossed" + `AllocationFree` "AllocatesZeroBytes". The `Library`/`LibraryMath` demo snapshots can still
appear failing due to a **bin-copy / CRLF-vs-LF test-infra quirk** — `TestData.ResolveSnapshotsDir` resolves to
the `bin/Debug/net8.0/Snapshots` COPY, so a stale/partial build or line-ending diff makes them flap even when the
SOURCE golden is byte-correct (`git -c core.autocrlf=false diff` empty). To regenerate a SOURCE golden you must
`rm -rf bin/.../Snapshots` first (else regen writes to bin). Do NOT chase these as regressions; verify final runs
WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS` (regen mode writes snapshots → masks mismatches).

## Done-definition for this thread
Multi-blueprint editor use is safe (BATCH-01); compiled blueprints observable by field name (BATCH-04);
every node kind exposes its value pins (BATCH-02 + BATCH-03C); FunctionCall is configurable and can call a
CLR method OR an in-blueprint function graph (BATCH-03A/B/C/D); graphs have editable signatures with
Entry/Return value pins (BATCH-03C/D); and a hand-authored `.bp.json` visibly counts up (BATCH-05).
Canvas polish (BATCH-06..09) is best-effort.
