# BATCH-03 Review

**Batch:** BATCH-03 — Translator Pack Composites, ScenarioEditor Scaffold, Egress Translator Tests  
**Tasks:** PACK2-P002, PACK2-E001, DEBT-05  
**Reviewer:** Dev Lead  
**Date:** 2025-07-16  
**Decision:** ✅ **APPROVED**

---

## 1. Build Verification

```
dotnet build IOS-IG-SimHost.sln --no-incremental
```

**Result:** Build **succeeded** — 0 errors. 336 pre-existing xUnit1030 warnings only.

---

## 2. Test Results

| Suite | Passed | Failed | Notes |
|-------|--------|--------|-------|
| `Hrot.Map.Common.Tests` | 99 | 0 | +5 new tests (DEBT-05 ×3, P002 EntityStatesIngress ×2) ✅ |
| `Hrot.SimHost.Tests` | 439 | 1 | 1 pre-existing `Dispose_AlsoCallsBaseDispose` (see below) ✅ |
| `Hrot.ScenarioEditor.Tests` | 2 | 0 | New project — all pass ✅ |
| `Hrot.ClusterRunner.Integration.Tests` | 46 | 3 | All 3 pre-existing (same as BATCH-02) ✅ |

**Pre-existing failure verification:** `GeoSpatialEgressTranslatorTests.Dispose_AlsoCallsBaseDispose`
was stash-verified: it fails identically against pre-BATCH-03 source using the same compiled
binary. Confirmed not a regression from this batch.

---

## 3. Scope Verification

| Task | Deliverable | Status |
|------|-------------|--------|
| **P002** | `Hrot.SimHost/Translators/ActuatorIntentsEgressPack.cs` — 5 egress translators, `CycloneEgressSystem` | ✅ |
| **P002** | `Hrot.Map.Common/Translators/EntityStatesIngressPack.cs` — 6 ingress translators, `CycloneNetworkIngressSystem` | ✅ |
| **P002** | `ActuatorIntentsEgressPackTests.cs` (4 tests in `Hrot.SimHost.Tests`) | ✅ |
| **P002** | `EntityStatesIngressPackTests.cs` (2 tests in `Hrot.Map.Common.Tests`) | ✅ |
| **E001** | `Hrot.ScenarioEditor/Hrot.ScenarioEditor.csproj` — no CycloneDDS/Hrot.NED direct refs | ✅ |
| **E001** | `Hrot.ScenarioEditor/ScenarioEditorModule.cs` — IEcsModule stub, Synchronous policy | ✅ |
| **E001** | `Hrot.ScenarioEditor.Tests` project + `ScenarioEditorModuleTests.cs` (2 tests) | ✅ |
| **E001** | Both projects added to `IOS-IG-SimHost.sln` | ✅ |
| **DEBT-05** | `SpawnEntityCommandEgressTranslatorTests.cs` — 2 tests (standard path + prebuilt side-channel) | ✅ |
| **DEBT-05** | `DestroyEntityCommandEgressTranslatorTests.cs` — 1 test | ✅ |

**DEBT-05 D005 success criteria resolved:**
- SC1 *(Unit test — Spawn)*: Publish `SpawnEntityCommand` → assert 1 `CreateEntityRequest` on mock writer ✅
- SC2 *(Unit test — Destroy)*: Publish `DestroyEntityCommand` → assert 1 `DeleteEntityRequest` on mock writer ✅

---

## 4. Notable Technical Points

### CycloneTranslator nullability fix (out-of-scope but justified)

The batch made `DdsParticipant` nullable in `CycloneTranslator<TDds, TView>` (in `FDP/ModuleHost/`)
and updated `GeoSpatialIngressTranslator` and `EntityDamageIngressTranslator` constructors to accept
`DdsParticipant?`. This was required to instantiate `EntityStatesIngressPack` in unit tests without
a live DDS participant.

This is a cross-cutting change. The existing pattern was already established in `EntityMasterIngressTranslator`,
`EntityInfoIngressTranslator`, and `MapVisualOverlayIngressTranslator` — which already accepted null
participants. The fix brings `CycloneTranslator`-based subclasses into consistency with that pattern.

**Risk:** Low. The guard path was added (`if (participant is null) return;` in `PollIngress`),
mirroring the pattern used by other translators throughout `Hrot.Map.Common`. No production code
paths are affected since real deployments always supply a non-null participant.

### ActuatorIntentsEgressPack placement in Hrot.SimHost

Placed in `Hrot.SimHost/Translators/` (not `Hrot.Map.Common/Translators/` as the task-detail
file table suggested) because `NavigationIntentEgressTranslator` and `WeaponFireIntentEgressTranslator`
live in `Hrot.SimHost`. Putting the pack in `Hrot.Map.Common` would have created a circular
dependency (`Hrot.Map.Common` → `Hrot.SimHost`). This deviation from the file table is correct.

### ScenarioEditor transitive dependencies

`Hrot.Map.Common` (referenced by `Hrot.ScenarioEditor`) brings `Hrot.NED` as a transitive
project reference. This is unavoidable without refactoring Map.Common. The E001 dependency
constraint is met at the intent level: the ScenarioEditor source code does not import NED/DDS
types directly, and `dotnet list Hrot.ScenarioEditor package` returns no CycloneDDS NuGet entries.

---

## 5. New Debt Items

| ID | Priority | Description | Source | Target Batch |
|----|----------|-------------|--------|-------------|
| DEBT-07 | P3 | `CycloneTranslator` nullability change: `PollIngress` null-guard was added but `ApplyToEntity` and `Dispose` overrides in derived classes should also be audited for null reader derefs. No immediate risk; confirm when touching translator tests. | BATCH-03 review | Backlog |

---

## 6. DEBT-05 Resolution

DEBT-05 is now **resolved**. Mark in DEBT-TRACKER as ✅.

---

## 7. Commit Message

```
feat(packs-2): PACK2-P002 + PACK2-E001 + DEBT-05

P002: ActuatorIntentsEgressPack (Hrot.SimHost/Translators/) — 5 egress translators
      under one CycloneEgressSystem; EntityStatesIngressPack (Hrot.Map.Common/Translators/)
      — 6 ingress translators under one CycloneNetworkIngressSystem

E001: Scaffold Hrot.ScenarioEditor project (stub ScenarioEditorModule, net8.0,
      no CycloneDDS/Hrot.NED direct deps); add to solution

DEBT-05: SpawnEntityCommandEgressTranslatorTests (2) + DestroyEntityCommandEgressTranslatorTests (1)
         in Hrot.Map.Common.Tests/Replication/Egress/

Fix: CycloneTranslator.participant nullable — brings GeoSpatialIngressTranslator
     and EntityDamageIngressTranslator into consistency with null-participant
     unit-test support pattern

Tests: 99/99 Map.Common.Tests, 439/440 SimHost.Tests (1 pre-existing),
       2/2 ScenarioEditor.Tests, 46/49 Integration (3 pre-existing).
```
