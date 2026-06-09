# TASK-TRACKER — Blueprint ↔ Entity Assignment & Scenario Persistence

**Reference:** [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions & success conditions.
**Design of record:** [BLUEPRINT-SCENARIO-DESIGN.md](./BLUEPRINT-SCENARIO-DESIGN.md). **Debt:** [DEBT-TRACKER.md](./DEBT-TRACKER.md).

Status: `[ ]` not done / `[x]` done. Each task links to its TASK-DETAIL section. Build gate per task: solution
builds 0 errors; 0 net-new test failures in touched projects.

---

## Phase 1 — Core foundation (`Fdp.Toolkits.Blueprints`)

**Goal:** stop persisting volatile blackboard bytes, and establish one run-mode-agnostic attach/detach seam in core.

- [ ] **BSA-101** Mark `BlueprintBlackboard{1024,4096,16384}` `[DataPolicy(NoSave)]` [details](./TASK-DETAIL.md#bsa-101-mark-blackboard-components-nosave) — *land with BSA-202*
- [x] **BSA-102** Unified attach/detach seam in core, keyed by `BlueprintId`; editor service → forwarder [details](./TASK-DETAIL.md#bsa-102-unified-attachdetach-seam-in-core-keyed-by-blueprintid)

## Phase 2 — Static scenario assignment (CGF genesis)

**Goal:** persist *intent* (which blueprints, optional overrides) declaratively; materialize at genesis through the core seam.

- [ ] **BSA-201** `BlueprintAssignmentDto` + `InitialBlueprintsIntent` (`[Transient]`) + `RegisterManagedComponent` [details](./TASK-DETAIL.md#bsa-201-blueprintassignmentdto-initialblueprintsintent-registration)
- [ ] **BSA-202** `BlueprintStateTranslator` — populate `def.AssetId` (emit) + Extract/Inject + `GetOutputDomKeys` (new key + legacy black-hole) [details](./TASK-DETAIL.md#bsa-202-blueprintstatetranslator-extract--inject--dom-keys--legacy-black-hole)
- [ ] **BSA-203** `BlueprintMaterializationSystem` — tier pre-provision + ceiling guard + ECB intent removal [details](./TASK-DETAIL.md#bsa-203-blueprintmaterializationsystem-tier-pre-provision--ceiling-guard--ecb-removal)

## Phase 3 — Dynamic / mid-runtime assignment

**Goal:** add/remove/replace Instance blueprints at runtime via events, reachable from a blueprint action node.

- [ ] **BSA-301** `Attach`/`Remove`/`ReplaceInstanceBlueprintEvent` (unmanaged) + `Input`-phase system (removes-before-adds) [details](./TASK-DETAIL.md#bsa-301-runtime-mutation-events--consuming-system)
- [ ] **BSA-302** `[SharedAiAction]` `BlueprintLifecycleLibrary` node(s) publishing the events via `world.Bus` [details](./TASK-DETAIL.md#bsa-302-sharedaiaction-lifecycle-nodes)

## Phase 4 — Editor UI

**Goal:** monitor live blueprint state (read-only) and author entity↔blueprint assignments (transactional panel).

- [ ] **BSA-204** Entity Inspector per-tier summary renderers (read-only; replace byte-dump) — *independent* [details](./TASK-DETAIL.md#bsa-204-entity-inspector-per-tier-summary-renderers-read-only-monitoring)
- [ ] **BSA-205** "Entity Blueprints" authoring panel (reality/intent diff; paused=sync+tier-upgrade, running=events) — *deps BSA-102 + BSA-301* [details](./TASK-DETAIL.md#bsa-205-entity-blueprints-authoring-panel-assign--remove-staged-commit)

## Phase 5 — Integration gate

**Goal:** prove the whole pipeline end-to-end.

- [ ] **BSA-401** Scenario round-trip + dynamic swap + resilience + backward-compat (GATE) [details](./TASK-DETAIL.md#bsa-401-end-to-end-scenario-round-trip--dynamic-swap-gate)
- [ ] **BSA-402** Demo scenario fixture (assign via panel → save → committed test scenario) [details](./TASK-DETAIL.md#bsa-402-demo-scenario-fixture)

---

### Implementation order
BSA-102 → (BSA-101 + BSA-202 together) → BSA-201 → BSA-203 → BSA-301 → BSA-302 → BSA-204 → BSA-205 → BSA-401 → BSA-402.

Notes:
- **BSA-101 must not ship without BSA-202's legacy black-hole**, or scenario load throws on old files.
- **BSA-202 includes the `def.AssetId` emit fix** (Design §11) — Extract's reverse-lookup is empty without it.
- **BSA-205 needs BSA-301** (live-mode commit events + removes-before-adds) and **BSA-102** (paused-mode seam + `CopyToLargerTier`/old-tier removal). **BSA-204** is independent (can be done any time after core).
