# Persistence Unification (BTree/HSM to JSON) — Task Tracker

**Reference:** [`TASK-DETAIL.md`](./TASK-DETAIL.md) for full descriptions + success conditions · **Design:** [`BTree_HSM_JSON_Persistence_Detailed_Design.md`](./BTree_HSM_JSON_Persistence_Detailed_Design.md) · **Debt:** [`DEBT-TRACKER.md`](./DEBT-TRACKER.md)

> One line per task; check the box when its success conditions (in TASK-DETAIL) are met and verified. Phases are roughly sequential; **Phase 1 is the keystone** and gates everything. Phases 7–9 don't gate the persistence core. **No development has started.**

---

## Phase 1: JSON substrate and emit core  *(keystone; zero behavior change)*
**Goal:** JSON fully serializable/round-trippable; emit logic relocated to a `netstandard2.0` core. Nothing decommitted yet.

- [x] **PU-101** Emit-core extraction (`netstandard2.0`) — BATCH-02 (byte-identical gate green for all editor-owned fixtures) [details](./TASK-DETAIL.md#pu-101-emit-core-extraction)
- [x] **PU-102** Persisted DTO + mapping (BTree) — BATCH-01 [details](./TASK-DETAIL.md#pu-102-persisted-dto-and-mapping-for-btree)
- [x] **PU-103** Persisted DTO + mapping (HSM) — BATCH-01 [details](./TASK-DETAIL.md#pu-103-persisted-dto-and-mapping-for-hsm)
- [x] **PU-104** JSON services + header-lazy discovery — BATCH-01 [details](./TASK-DETAIL.md#pu-104-json-services-and-discovery)
- [x] **PU-105** Round-trip + determinism tests — RT byte-stability (BATCH-01) + emit-core byte-identical gate & `SaveBTree/HsmEmitTests` re-base (BATCH-02) [details](./TASK-DETAIL.md#pu-105-round-trip-and-determinism-tests)

> **Phase 1 COMPLETE** (PU-101..105). Round-trippable JSON + relocated netstandard2.0 emit core; zero behavior change.

## Phase 2: Build-time generation  *(JSON → C#)*
**Goal:** MSBuild generates runtime C# from JSON; editor-owned `.cs` becomes a non-committed artifact.

- [x] **PU-201** IncrementalGenerator: topology + thunk (BTree) — BATCH-03 [details](./TASK-DETAIL.md#pu-201-incrementalgenerator-topology-and-thunk-for-btree)
- [x] **PU-202** IncrementalGenerator: topology + thunk (HSM) — BATCH-03 [details](./TASK-DETAIL.md#pu-202-incrementalgenerator-topology-and-thunk-for-hsm)
- [x] **PU-203** `[BlueprintRegistrar]` self-registration bridge — BATCH-04 (JSON-owned BTree/HSM discovered→registered→tickable) [details](./TASK-DETAIL.md#pu-203-blueprintregistrar-self-registration-bridge)
- [x] **PU-204** `Hrot.AI.Behaviors.csproj` wiring (AdditionalFiles + analyzer ref; dormant until migration) — BATCH-04 [details](./TASK-DETAIL.md#pu-204-hrotaibehaviors-csproj-wiring)

> **Phase 2 COMPLETE** (PU-201..205). ⚠ **PU-D06 escalated:** migration-equivalence criterion (byte-identical `.cs` → blob/behavioral equivalence) needs user/architect sign-off before PU-401.
- [~] **PU-205** Migration-equivalence test harness — BATCH-03 (faithful-routing equivalence; DIRECT committed-`.cs` compare deferred to PU-401, see DEBT PU-D04) [details](./TASK-DETAIL.md#pu-205-migration-equivalence-test-harness)

## Phase 3: Editor load path + reconciliation stitching
**Goal:** editor opens editor-owned assets from JSON (no compile needed to reopen); hand-authored stays reflection; debug overlay survives reload.

- [x] **PU-301** JSON load path (dual-load) — BATCH-05 (reopen-when-C#-broken proven) [details](./TASK-DETAIL.md#pu-301-json-load-path-dual-load)
- [x] **PU-302** Post-reload stitching (VisualId/StableId → KernelBlobIndex), Kind-guarded — BATCH-05 [details](./TASK-DETAIL.md#pu-302-post-reload-stitching)
- [x] **PU-303** Load-path tests — BATCH-05 [details](./TASK-DETAIL.md#pu-303-load-path-tests)

> **Phase 3 COMPLETE** (PU-301..303). Editor-owned BTree/HSM load from JSON + reopen even when C# won't compile; stitch is Kind-guarded (Blueprint path untouched). Mechanism dormant in the live editor until migration (PU-401, blocked on PU-D06).

## Phase 4: Migration of existing assets
**Goal:** existing editor-owned `.cs` → `.json`; equivalence proven; old `.cs` decommitted.

- [ ] **PU-401** Migration pass (`.cs` → `.json`) [details](./TASK-DETAIL.md#pu-401-migration-pass-cs-to-json)
- [ ] **PU-402** Decommit generated `.cs` [details](./TASK-DETAIL.md#pu-402-decommit-generated-cs)

## Phase 5: Path-at-creation + fixed roots
**Goal:** every asset gets a real path at creation; no `.cs`/`.json` base-name collisions.

- [~] **PU-501** Fixed roots + path-at-creation — **DEFERRED to PU-401** (debt PU-D12: SourceFilePath→.json would be clobbered by the unchanged .cs flushAction; BTree/HSM have no creation flow yet) [details](./TASK-DETAIL.md#pu-501-fixed-roots-and-path-at-creation)
- [x] **PU-502** Base-name collision guard — BATCH-07 (`AssetBaseNameCollisionGuard` + Save-All wiring; both directions) [details](./TASK-DETAIL.md#pu-502-base-name-collision-guard)

> **Phase 5 partial:** PU-502 ✅. PU-501 deferred to the migration batch (PU-401, blocked on PU-D06) — see DEBT PU-D12.

## Phase 6: Unified Save / Save-All
**Goal:** all dirty open docs flush to JSON on demand + on close; no debounce data loss.

- [x] **PU-601** `RegenerationScheduler.FlushNow()` — BATCH-06 [details](./TASK-DETAIL.md#pu-601-regenerationscheduler-flushnow)
- [x] **PU-602** Save-All command (all dirty docs, by kind) — BATCH-06 [details](./TASK-DETAIL.md#pu-602-save-all-command)
- [x] **PU-603** Save-All wiring (`Ctrl+Shift+S`) + flush-on-close — BATCH-06 [details](./TASK-DETAIL.md#pu-603-save-all-wiring-and-flush-on-close)

## Phase 7: Unified tree asset browser  *(does not gate the persistence core)*
**Goal:** one folder-tree browser across all three kinds.

- [ ] **PU-701** Folder-tree asset browser (NodeEdit widgets) [details](./TASK-DETAIL.md#pu-701-folder-tree-asset-browser)

## Phase 8: Rename / refactor across json + cs
**Goal:** FQN rename rewrites references across both JSON and `.cs`.

- [ ] **PU-801** JSON-aware refactor writer [details](./TASK-DETAIL.md#pu-801-json-aware-refactor-writer)

## Phase 9: In-process quick reload  *(meets ≤100 ms target; not needed to avoid regression)*
**Goal:** edit-to-live latency ≤100 ms for BTree/HSM.

- [ ] **PU-901** In-process Roslyn quick reload (emit core + masquerade) [details](./TASK-DETAIL.md#pu-901-in-process-roslyn-quick-reload)

## Phase 10: Blackboard DD revision handoff  *(pre-Slice-1.5)*
**Goal:** the Blackboard Authoring DD is aligned to the JSON substrate before Slice 1.5. *(The revision itself is done by the lead in the design session; this task is the downstream consistency check.)*

- [ ] **PU-1001** Verify revised Blackboard DD consistency [details](./TASK-DETAIL.md#pu-1001-verify-revised-blackboard-dd-consistency)

---

**Totals:** 5 + 5 + 3 + 2 + 2 + 3 + 1 + 1 + 1 + 1 = **24 tasks**, 10 phases.

**Sequencing notes:**
- **Phase 1 → 2 → 3 → 4** is the critical path that delivers the core promise (*save always works; assets always reopenable, even when C# won't compile* — see PU-301 acceptance).
- Phases **5, 6** depend on Phase 1–3 plumbing but are otherwise independent.
- Phases **7 (browser), 8 (rename), 9 (quick reload)** are independent of each other and don't gate the persistence core; sequence by preference.
- **PU-1001** is last (post-implementation check of the lead-revised Blackboard DD).
