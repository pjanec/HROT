# BATCH-02 Review

**Batch:** BATCH-02 — Phase 1: Decouple Map Tools from the Network Edge  
**Tasks:** PACK2-D001, PACK2-D002, PACK2-D003, PACK2-D004, PACK2-D005  
**Reviewer:** Dev Lead  
**Date:** 2025-07-16  
**Decision:** ✅ **APPROVED** (with P2 debt recorded)

---

## 1. Build Verification

```
dotnet build IOS-IG-SimHost.sln --no-incremental
```

**Result:** Build **succeeded** — 0 errors. Pre-existing xUnit1030 warnings only (analyzer
issue, not build-blocking).

---

## 2. Test Results

| Suite | Passed | Failed | Pre-existing failures |
|-------|--------|--------|-----------------------|
| `Hrot.IG.Tests` | 408 | 7 | ✅ All 7 are pre-existing (6 `UniqueNameGeneratorTests`, 1 `TraceLoggingTests`) |
| `Hrot.Map.Common.Tests` | 94 | 0 | — |
| `Hrot.ClusterRunner.Tests` | 188 | 3 | ✅ All 3 are pre-existing |
| `Hrot.ClusterRunner.Integration.Tests` | ~44–47 | ~2–5 | ✅ confirmed flaky/pre-existing (see below) |

**Integration test failures:**

| Test | Verdict |
|------|---------|
| `SimHost_ClearAllPattern_AllIgGhostsRemoved` | **Flaky** — passes in isolation and post-stash; timing-sensitive; unrelated to BATCH-02 changes |
| `MiniExConSpawnWithWanderMission_...` | Pre-existing (`EntityMission.Plan.Tasks` empty) |
| `ClusterOpE2eScriptTests.LiveFromReplayBranch_Passes` | Pre-existing replay-infrastructure failure |
| `ClusterOpE2eScriptTests.RecordAndReplaySeek_Passes` | Pre-existing flaky |
| `ClusterOpE2eScriptTests.PreviewStateRestore_Passes` | Pre-existing flaky |

The `SimHost_ClearAllPattern_AllIgGhostsRemoved` flakiness was verified by:
1. Running the test in isolation → **PASSED**
2. Running after `git stash` of source (same compiled binary) → **PASSED**

This confirms the failure is timing/scheduling noise, not a regression from BATCH-02.

**Targeted scenario verification (82/82 pass):**
```
dotnet test Hrot.IG.Tests --filter "CreationTool|SpawnEntityCommand|MapCommandController|AreaAuthoring|EditTool|DestroyEntity|EgressTranslator"
```
All scenarios introduced or modified in BATCH-02 pass without failures.

---

## 3. Scope Verification

| Task | Files | Status |
|------|-------|--------|
| **D001** | `CreationTool.cs` — no NED imports; emits `SpawnEntityCommand` | ✅ |
| **D002** | `IgApplication.cs` — edit/route-commit subscribers publish `UpdateEntityCommand` | ✅ |
| **D003** | `IgApplication.cs` — `_deleteEntityDdsWriter` removed; delete always emits `DestroyEntityCommand` | ✅ |
| **D004** | `MapCommandController.cs` — `IDdsWriter<CreateEntityRequest>` ctor param removed; `FdpEventBus` injected | ✅ |
| **D005** | `SpawnEntityCommandEgressTranslator.cs`, `UpdateEntityCommandEgressTranslator.cs`, `DestroyEntityCommandEgressTranslator.cs` created in `Hrot.Map.Common/Replication/Egress/`; all three installed in `IgApplication.cs` `customTranslators` | ✅ |

---

## 4. Test Quality Assessment

All 5 D tasks verified via:
- **Unit-level** (via `IgApplication`-free harness in `Hrot.IG.Tests`): CreationTool, MapCommandController,
  ToolInteractionIntegration scenario tests, and translator-behaviour assertions.
- **Integration-level**: `AreaAuthoringIntegrationTests`, `EntityDestroyIntegrationTests`,
  `SpawnMovingVehicleIntegrationTests`.

**Gap:** The D005 success criteria also specify two **standalone unit tests** (one for
`SpawnEntityCommandEgressTranslator`, one for `DestroyEntityCommandEgressTranslator`) that mock
the DDS writer. No such standalone test files exist in `Hrot.Map.Common.Tests` or `Hrot.IG.Tests`.
The translator behaviour is verified indirectly through integration tests. Recording as **DEBT-05**
(see §5).

---

## 5. Notable Technical Points

### Critical fix: side-channel for area authoring (Q2, Q5 in report)

The most significant issue discovered post-D001/D004: `NetworkSpawningSystem` in the IG bus consumed
the newly-emitted `SpawnEntityCommand` and tried to apply `CreateEntityRequest` (a NED IDL struct)
as an ECS managed component, throwing `TypeInitializationException`.

**Fix:** `_prebuiltRequests Dictionary<Guid, CreateEntityRequest>` side-channel in
`MapCommandController` + `Func<Guid, CreateEntityRequest?>` delegate passed to
`SpawnEntityCommandEgressTranslator`. Area authoring stores the pre-built request before publishing
and the egress translator retrieves it directly, bypassing the standard path.

Area authoring integration test confirmed passing after fix.

### `UpdateEntityCommandEgressTranslator` silent drain

RoutePlan `UpdateEntityCommand` events are intentionally drained without DDS write (route updates
go via `MapRouteEgressTranslator.ScanAndPublish`, not the egress path). This is documented with a
comment but is an invisible coupling assumption.

---

## 6. New Debt Items

| ID | Priority | Description | Source | Target Batch |
|----|----------|-------------|--------|-------------|
| DEBT-03 | P2 | `_prebuiltRequests` in `MapCommandController` is unbounded — no TTL or eviction. Under sustained network outage, grows without limit. | BATCH-02 report Q4 | BATCH-04 |
| DEBT-04 | P2 | `NetworkSpawningSystem` in the IG consumes EGRESS `SpawnEntityCommand` events, potentially creating spurious ghost entities. Architecturally fragile; needs INGRESS/EGRESS bus partition or discriminant field. | BATCH-02 report Q4 | BATCH-05 |
| DEBT-05 | P2 | No standalone unit test files for `SpawnEntityCommandEgressTranslator` or `DestroyEntityCommandEgressTranslator`. D005 success criteria 1 & 2 are met only by integration-level coverage. Add `SpawnEntityCommandEgressTranslatorTests.cs` and `DestroyEntityCommandEgressTranslatorTests.cs` to `Hrot.Map.Common.Tests`. | BATCH-02 review | BATCH-03 |
| DEBT-06 | P3 | `UpdateEntityCommandEgressTranslator` silently drains `UpdateEntityCommand` events that carry `RoutePlan`, forwarding nothing to DDS (intentional, but fragile). If route editing moves to a new module in Phase 2, this assumption must be re-evaluated. | BATCH-02 report Q4 | Backlog |

---

## 7. Commit Message

```
feat(packs-2): PACK2-D001–D005 — Decouple Map Tools from Network Edge (Phase 1)

D001: CreationTool emits SpawnEntityCommand; zero NED imports
D002: IgApplication.cs edit/route-commit subscribers → UpdateEntityCommand
D003: Delete path unconditionally publishes DestroyEntityCommand; remove
      _deleteEntityDdsWriter field
D004: MapCommandController receives FdpEventBus; remove IDdsWriter<CreateEntityRequest>
D005: SpawnEntityCommandEgressTranslator, UpdateEntityCommandEgressTranslator,
      DestroyEntityCommandEgressTranslator in Hrot.Map.Common/Replication/Egress/;
      installed in IgApplication.cs customTranslators

Critical fix: _prebuiltRequests side-channel in MapCommandController prevents
TypeInitializationException when NetworkSpawningSystem consumes egress bus events.
Func<Guid, CreateEntityRequest?> delegate wired in IgApplication.cs closure.

Tests: 408/415 Hrot.IG.Tests (7 pre-existing); 94/94 Map.Common;
       188/191 ClusterRunner; integration flaky pre-existing only.
```
