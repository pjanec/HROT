# TASK-TRACKER — BTree AI Action/Condition Parameter Binding

**Reference:** [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions & success conditions (test specs — do **not** invent acceptance criteria).
**Design of record:** `docs/blueprints/BTree_AiActionParameterBinding_Detailed_Design.md` ("AIB-DD"); working drafts [SLICE1-DESIGN.md](./SLICE1-DESIGN.md), [SLICE2-DESIGN.md](./SLICE2-DESIGN.md). **Debt:** [DEBT-TRACKER.md](./DEBT-TRACKER.md).

Status: `[ ]` not done / `[x]` done. Both slices architect-approved (2026-06-15). Build gate per task: solution builds 0 errors; 0 net-new test failures in touched projects; byte-identity gate stays green; `dotnet build-server shutdown` before any codegen verification.

---

## Slice 1 — Stateless multi-action binding (architect-greenlit)

**Goal:** authored JSON BTrees bind multiple stateless actions/conditions, each with its own param DTO at its own bin-packed offset; surfaced + editable in the blackboard UI; runnable demo.

- [x] **S1-0** `bool` `[MarshalAs(UnmanagedType.I1)]` fix in `BlackboardDtoEmitter` (prerequisite) [details](./TASK-DETAIL.md#s1-0--bool-marshalas-fix) — *done BATCH-01 (P2: DEBT-AIB-008)*
- [x] **S1-1** Variables panel read-only reflection of hardcoded Category-1 DTOs (via `ActionSchemaExporter`) [details](./TASK-DETAIL.md#s1-1--category-1-dto-reflection-in-variables-panel) — *done BATCH-01 (VM-level; live wiring DEBT-AIB-009 → S1-5/S1-G)*
- [x] **S1-2** Per-asset blackboard struct + topology-over-struct codegen [details](./TASK-DETAIL.md#s1-2--per-asset-struct--topology-over-struct) — *done BATCH-02 (build-time `BTreeBlackboardPackHelper`; DEBT-AIB-011)*
- [x] **S1-3** Per-asset baked-offset registrar + adapter-calls-`TickCore` [details](./TASK-DETAIL.md#s1-3--baked-offset-registrar--adapter) — *done BATCH-02*
- [x] **S1-2b** Struct-DTO size resolution in the build-time packer (via Roslyn `Compilation`) [details](./TASK-DETAIL.md#s1-2b--struct-dto-size-resolution) — *done BATCH-03 (managed sizing + alias accept; generated struct confirmed nominal; inspector multi-DTO read → DEBT-AIB-012 @ S1-G)*
- [x] **S1-4** Validator: unblock `ThreeParamReusable` (type-matched binding) [details](./TASK-DETAIL.md#s1-4--validator-unblock-threeparamreusable) — *done BATCH-02*
- [x] **S1-5** Node-inspector field-picker + "promote to new variable" [details](./TASK-DETAIL.md#s1-5--field-picker--promote-to-variable) — *already satisfied by DEC-05 tests (verified BATCH-04): `GetItems_ReturnsOnlyCompatibleVars_ForKnownFqn` (type-filter), `Promote_CreatesVar_AndFacetApply_SetsExpressionTargetField_BTree` + `Promote_CreatesAutoVar_..._IsAutoManaged`; 18/0*
- [ ] **S1-G** **DEMO GATE** — multi-action / distinct-DTO / decorator / aliasing + proof tests [details](./TASK-DETAIL.md#s1-g--slice-1-demo-gate) — *deps S1-2…S1-5*

## Slice 2 — Multiple stateful primitives per entity (after S1-G)

**Goal:** lift the "one stateful AiPrimitive per entity" constraint via Option β (partitioned working state), with the three architect-mandated fixes; runnable multi-stateful demo.

- [ ] **S2-1** WorkingState → `BlueprintBlackboard*` (Option β) + FNV-1a per-node slot key + adapter [details](./TASK-DETAIL.md#s2-1--option-beta-working-state--slot-key) — *deps S1-3*
- [ ] **S2-2** Synchronous `Input`-phase tier provisioning (Fix 1) [details](./TASK-DETAIL.md#s2-2--synchronous-input-phase-provisioning) — *deps S2-1*
- [ ] **S2-3** Hot-reload ghost-slot fix — re-publish `AssignBehaviorEvent` (Fix 2) [details](./TASK-DETAIL.md#s2-3--hot-reload-ghost-slot-fix) — *deps S2-1, S2-2*
- [ ] **S2-4** Cross-region validator: forbid concurrent stateful Subtree (Fix 3) [details](./TASK-DETAIL.md#s2-4--cross-region-validator-stateful-subtree) — *independent (editor validator)*
- [ ] **S2-G** **DEMO GATE** — multiple stateful primitives + mixed stateless + proof tests [details](./TASK-DETAIL.md#s2-g--slice-2-demo-gate) — *deps S2-1…S2-4*

---

### Implementation order
S1-0 → S1-1 (parallel) → S1-2 → S1-3 → S1-4 → **S1-2b** → S1-5 → **S1-G** → S2-1 → S2-2 → S2-3 → S2-4 → **S2-G**.
(S1-2b inserted 2026-06-15 by user decision — struct-DTO sizing; must precede S1-G. See [[project-btree-struct-dto-sizing]].)

Notes:
- **S1-4 must not ship without S1-2+S1-3** — unblocking `ThreeParamReusable` before the per-asset struct/registrar exist turns clean `BTREE0002` skips into hard build breaks (AIB-DD §3.3).
- **S1-0 is a real latent bug** (`BlackboardDtoEmitter` emits bare `bool`; bin-packer assumes 1 B, `Marshal.OffsetOf` defaults 4 B) — fix it first or S1-2's offsets silently drift (AIB-DD §3.2).
- **S1-1 and S1-2/3 are independent paths** — S1-1 is editor-only (no codegen); can run in parallel with the codegen chain.
- **Slice 2 starts only after S1-G passes.** All three Slice-2 fixes (S2-2/3/4) are mandatory per architect (AIB-DD §4.3); none is optional.
- Out of plan: authored heavy-DTO struct gen, shared blackboard — see [DEBT-TRACKER.md](./DEBT-TRACKER.md).
