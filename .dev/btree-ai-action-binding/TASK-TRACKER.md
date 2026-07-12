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
- [x] **S1-G** **DEMO GATE** — multi-action / distinct-DTO / decorator / aliasing + proof tests [details](./TASK-DETAIL.md#s1-g--slice-1-demo-gate) — *done BATCH-05: T10/T11 assets + 3 end-to-end proof tests green; live multi-DTO inspector wired (DEBT-012); **awaiting user manual visual check**. Defaults seeded (DEBT-013); hardcoded-DTO live reflection deferred (DEBT-009).* ✅ **SLICE 1 COMPLETE**

## Slice 2 — Multiple stateful primitives per entity (after S1-G)

**Goal:** lift the "one stateful AiPrimitive per entity" constraint via Option β (partitioned working state), with the three architect-mandated fixes; runnable multi-stateful demo.

- [x] **S2-1** WorkingState → `BlueprintBlackboard*` (Option β) + FNV-1a per-node slot key + adapter [details](./TASK-DETAIL.md#s2-1--option-beta-working-state--slot-key) — *done BATCH-06 (mechanism via stateful demo primitive, not full blueprint-TickCore composition → DEBT-AIB-025; emitted-thunk compile-gap → DEBT-AIB-026 @ S2-G)*
- [x] **S2-2** Synchronous `Input`-phase tier provisioning (Fix 1) [details](./TASK-DETAIL.md#s2-2--synchronous-input-phase-provisioning) — *done BATCH-06 (sync AddComponent+CopyToLargerTier+RemoveComponent in Input; slot preservation verified)*
- [x] **S2-3** Hot-reload ghost-slot fix — re-publish `AssignBehaviorEvent` (Fix 2) [details](./TASK-DETAIL.md#s2-3--hot-reload-ghost-slot-fix) — *done BATCH-08 (ghost-slot-safe re-provision ACTIVE; size-sensitive hash resolves DEBT-AIB-027; coordinator OnHardReloadCompleted event dormant until host wiring → DEBT-AIB-031; full-suite flakiness → DEBT-AIB-030)*
- [x] **S2-4** Cross-region validator: forbid concurrent stateful Subtree (Fix 3) [details](./TASK-DETAIL.md#s2-4--cross-region-validator-stateful-subtree) — *done BATCH-07 (HsmValidator ConcurrentStatefulSubtree hard-error; dormant until subtree-ref persistence + resolver wiring → DEBT-AIB-028/029)*
- [x] **S2-G** **DEMO GATE** — multiple stateful primitives + mixed stateless + proof tests [details](./TASK-DETAIL.md#s2-g--slice-2-demo-gate) — *done BATCH-09: T20 asset + 2 end-to-end proof tests (generate→compile→provision→tick); clean rebuild 0 errors (DEBT-AIB-026 closed — fixed 3 emitter gaps the compile-gate surfaced); byte-identity 129/0.* ✅ **SLICE 2 COMPLETE**

## Slice 3 — Scoped shared working state (§4.4 MVP = Behavior scope)

**Goal:** every blackboard variable carries a `role` (input/state) and, for state, a `scope` (Node/Behavior/Entity). A **local** variable = state@Node (the S2 case); a **shared** variable = state@Behavior. MVP ships **Behavior scope**: multiple nodes on one entity share **one** working-state slot. Concrete driver: replace Hill Attack's manual `GetComponentRW<Blackboard1024>() + Unsafe.As` with a `Behavior`-scoped `HillAttackMutableState` shared across `CalculateSegments`/`DispatchWave`/`IsWaveCompleted`. Design: AIB-DD §4.4 (resolved 2026-07-12). Storage reuses the S2 partitioned tier + `BlueprintBlackboardPartitions` unchanged — **only the slot key + provisioning granularity change**.

> **Key-formula RESOLVED (2026-07-12, code-grounded, architect-proxy) — AIB-DD §4.4:**
> - **`Behavior` key = `FNV-1a(assetId, variableId)`** (`variableId` = the binding's `ExpressionTargetField`). Drop `entityId` (the partitioned tier is a per-entity component fetched via `ctx.Self`, so the key space is already per-entity — `entityId` is redundant); add `variableId` (else two distinct Behavior-scoped vars in one asset collide onto one slot). `Entity` key = `FNV-1a(variableId)` (no `assetId`, survives behavior switch — post-MVP).
> - **Consequence: keys stay compile-time constants → NO runtime key computation.** Both `assetId` and `variableId` are known at emit time, so the emitted thunk keeps its baked `const` (Node scope unchanged); Behavior-scoped nodes just bake the *same* `(assetId, variableId)`-derived value and resolve to the same per-entity slot. **This collapses the originally-planned S3-3 "runtime key" work** into S3-2's key change + S3-4's shared provisioning.
> - Keys are ephemeral runtime ids (never persisted) → **no byte-identity impact**; `Node` keys unchanged → Slice-2 untouched.

- [x] **S3-1** Authoring: add `Role`+`Scope` to `BlackboardVariableDto`/`BlackboardVariableEntry` (default `input`/`Node` = back-compat); Variables-panel selectors; persist in asset blackboard block [details](./TASK-DETAIL.md#s3-1--authoring-role--scope) — *done BATCH-12 (`5eb8f1da`): enums in `BlackboardVariableEnums.cs`; DTO+HSM omit-when-default (byte-stable); Variables-panel Role combo + State-gated Scope combo; Persistence.Tests 136/0, AiShared.Tests 1110/0*
- [x] **S3-2** Scope-aware slot key: `ComputeStatefulSlotKey(assetId, scope, nodeVisualId, variableId)` — Node=`(asset,node)` unchanged; Behavior=`(asset,variableId)`; Entity=`(variableId)`. All compile-time. Pure fn + unit tests [details](./TASK-DETAIL.md#s3-2--scope-aware-slot-key) — *done BATCH-13 (`58c9aabd`): overload added; `SlotKey_Node_MatchesLegacy` confirms byte-identical Node path; slot-key suite 6/6*
- [ ] **S3-3** ~~Runtime key resolution~~ **Emit the scope-aware baked const**: the thunk keeps its compile-time `const __slotKey`; for Behavior bindings it's derived from `(assetId, variableId)` so co-bound nodes share the slot. (Reduced from "runtime key" — see key-formula resolution.) [details](./TASK-DETAIL.md#s3-3--scope-aware-baked-const) — *depends S3-2*
- [x] **S3-4** Shared-slot provisioning/dedup: provision **one** slot per distinct Behavior-scoped variable per entity (not per node); manifest (`StatefulWorkingSlots`/`StatefulSlotInfo`) represents shared slots; `ProvisionStatefulSlots` dedupes by scope key [details](./TASK-DETAIL.md#s3-4--shared-slot-provisioning) — *done BATCH-14 (`912fc55`): `ResolveStatefulSlotKey` helper + scope-aware manifest emit; existing slotsBySeen dedup collapses co-bound Behavior nodes to one slot. Byte-identity 136/0; 2 new provisioning tests green; T20 unchanged.*
- [ ] **S3-5** `ClearBehaviorEvent` detach fix (design change #2): capture previous behavior id + call `DetachStatefulSlots` on clear, not only on switch (today leaks on clear-without-successor) [details](./TASK-DETAIL.md#s3-5--clearbehaviorevent-detach) — *depends S3-4*
- [ ] **S3-6** Fix-3 guard extension (design change #3): flag two stateful nodes in concurrent HSM regions resolving to the same Behavior/Entity slot key (same scope+type under one asset); extend `HsmValidator.CheckConcurrentStatefulSubtrees` [details](./TASK-DETAIL.md#s3-6--fix3-scope-guard) — *depends S3-2*
- [ ] **S3-7** Monitoring (v1-mandatory): thread `Role`/`Scope` into `StatefulSlotInfo`; `StatefulWorkingStateProjection` groups/labels by scope; live read-only inspector shows the shared slot's current values [details](./TASK-DETAIL.md#s3-7--scope-aware-monitoring) — *depends S3-4*
- [ ] **S3-G** **DEMO GATE** — Hill Attack `HillAttackMutableState` as a `Behavior`-scoped shared variable; 3 nodes bind 4-param `(ref Params, ref HillAttackMutableState, …)`; end-to-end proof test (generate→compile→provision→tick) replacing the `Blackboard1024`+`Unsafe.As` hack; live inspector shows shared state [details](./TASK-DETAIL.md#s3-g--slice-3-demo-gate) — *gate; Hill Attack fully jsonized on shared state*

---

### Implementation order
S1-0 → S1-1 (parallel) → S1-2 → S1-3 → S1-4 → **S1-2b** → S1-5 → **S1-G** → S2-1 → S2-2 → S2-3 → S2-4 → **S2-G** → S3-1 (parallel, editor-only) ∥ (S3-2 → S3-3 → S3-4 → S3-5) → S3-6 (after S3-2) → S3-7 (after S3-4) → **S3-G**.
(S1-2b inserted 2026-06-15 by user decision — struct-DTO sizing; must precede S1-G. See [[project-btree-struct-dto-sizing]].)
(Slice 3 added 2026-07-12 — §4.4 Behavior-scope shared working state MVP; storage reuses S2's partitioned tier, only slot-key + provisioning-granularity change. **Key formula RESOLVED 2026-07-12 (architect-proxy): `Behavior`=`FNV(assetId, variableId)`, keys stay compile-time constants — S3-2/S3-3 unblocked.**)

Notes:
- **S1-4 must not ship without S1-2+S1-3** — unblocking `ThreeParamReusable` before the per-asset struct/registrar exist turns clean `BTREE0002` skips into hard build breaks (AIB-DD §3.3).
- **S1-0 is a real latent bug** (`BlackboardDtoEmitter` emits bare `bool`; bin-packer assumes 1 B, `Marshal.OffsetOf` defaults 4 B) — fix it first or S1-2's offsets silently drift (AIB-DD §3.2).
- **S1-1 and S1-2/3 are independent paths** — S1-1 is editor-only (no codegen); can run in parallel with the codegen chain.
- **Slice 2 starts only after S1-G passes.** All three Slice-2 fixes (S2-2/3/4) are mandatory per architect (AIB-DD §4.3); none is optional.
- Out of plan: authored heavy-DTO struct gen, shared blackboard — see [DEBT-TRACKER.md](./DEBT-TRACKER.md).
