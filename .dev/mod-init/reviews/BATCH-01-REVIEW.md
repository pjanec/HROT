# BATCH-01 Review

**Batch:** BATCH-01  
**Tasks Reviewed:** MODINIT-S100, MODINIT-S107, MODINIT-S101, MODINIT-S102, MODINIT-S103, MODINIT-S104, MODINIT-S106  
**Reviewer:** Dev Lead  
**Date:** 2026-04-07  
**Decision:** ✅ APPROVED

---

## Verification Summary

### Build
- `dotnet build IOS-IG-SimHost.sln` → **0 errors**. Confirmed independently.

### Boundary Audit
All layer boundary queries confirmed clean (zero output):
- `Hrot.Common.csproj` — no reference to `Hrot.(SimHost|IG)` ✅
- `Hrot.Map.Common.csproj` — no reference to `Hrot.(SimHost|IG)` ✅
- `Hrot.Network.csproj` — no reference to `Hrot.(SimHost|IG)` ✅
- `Hrot.Common.csproj`, `Hrot.Map.Common.csproj` — no reference to `Hrot.Network` ✅

### File Audit
All 12 new/moved files verified to exist in their correct target paths. All 8 source files verified to be deleted. Namespace declarations verified correct:
- `Hrot.Common.Systems` (DeadReckoningSyncSystem) ✅
- `Hrot.Map.Common.Translators` (SharedTranslatorPack, KinematicTranslatorPack) ✅
- `Hrot.Network.Translators` (CognitiveTranslatorPack) ✅
- `Hrot.Map.Common.Replication.Ingress/Egress` (4 navigation translators) ✅

### Test Results
| Project | Passed | Pre-existing Failures | New Failures |
|---|---|---|---|
| `Hrot.ClusterRunner.Tests` | 140 | 0 | 0 |
| `Hrot.Map.Common.Tests` | 116 | 0 | 0 |
| `Hrot.SimHost.Tests` | 444 | 5 | 0 |
| `Hrot.IG.Tests` | 412 | 7 | 0 |

**Zero new failures introduced.** Pre-existing failures confirmed unchanged across the batch (developer verified via `git stash`).

### Test Quality
Tests assert on **behavior and types**, not just compilation:
- `DeadReckoningSyncSystemTests` — asserts on `NetworkTransform.LastPosition` values and `SimTransform.Position` range (correct interpolation checking). However, the SC3/SC4 test scenarios were adapted from the task spec: the developer correctly identified that the parameterless constructor uses `EntityLifecycle.Active` (not `.All`), and adjusted two Active-lifecycle entities for a more honest assertion.
- `SharedTranslatorPack` tests — assert `translator is EntityMasterEgressTranslator` (type-check), count is 3. Solid.
- `KinematicTranslatorPack` tests — pre-existing test updated to new namespace; asserts type containment.

---

## Scope Check

| Task | Scope Compliant? | Notes |
|---|---|---|
| MODINIT-S100 | ✅ | No code files added — directories and project file only |
| MODINIT-S107 | ✅ | 4 files moved, no logic changes |
| MODINIT-S101 | ✅ | File moved, namespace updated, no logic changes; 2 new tests |
| MODINIT-S102 | ✅ | File moved, namespace updated; 4 new tests |
| MODINIT-S103 | ✅ | File moved, namespace updated; existing tests updated |
| MODINIT-S104 | ✅ | File moved, namespace updated; existing tests updated |
| MODINIT-S106 | ✅ | Boundary validation only — no code changes |

No scope creep detected. No behavioral changes to any moved file.

---

## Design Alignment

Implementation is strictly in line with the design in `.dev/mod-init/DESIGN.md`:
- `Hrot.Network` references `Hrot.Common`, `Hrot.Map.Common`, `FDP.Toolkit.Behavior` only ✅
- `Hrot.Common` does NOT reference `Hrot.Network` ✅
- `Hrot.Map.Common` does NOT reference `Hrot.Common` (existing) or `Hrot.Network` ✅
- Navigation translators correctly placed in `Hrot.Map.Common.Replication.{Ingress,Egress}` ✅
- `CognitiveTranslatorPack` correctly placed in `Hrot.Network.Translators` (requires `FDP.Toolkit.Behavior`) ✅

---

## Issues Found

### P3 — DDS Domain ID Hardcoding in Test Fixtures
The developer noted that `TranslatorPackTests.cs` hardcodes domain IDs (`209u`, `210u`, etc.) for isolation. With more packs potentially tested in future batches, collision risk in parallel test runs increases. Low urgency.

### P3 — Pre-existing `UniqueNameGeneratorTests` failures (6 tests)
Related to `UnsafeShim.ManagedAccessor<T>` — a framework-level reflection/generic instantiation bug. Unrelated to this workstream but silently degrades IG naming coverage.

### P3 — Pre-existing `TraceLoggingTests.IngressAndRender_EmitsTraceLines` failure
Timing/threading issue in DDS integration test infrastructure. Pre-existing.

---

## Developer Insights Extracted

**Key findings from developer report:**

1. **`IgApplication.cs` was a hidden caller** of `DeadReckoningSyncSystem` not highlighted in the batch instructions. The developer found it independently and patched it correctly.

2. **`Hrot.SimHost/NodeBootstrapper.cs`** was also a hidden caller of the translator packs — not covered by the simple namespace grep. Developer patched it correctly.

3. **Stage 2 risk identified:** `NedReplicationModule.cs` still imports `using Hrot.SimHost.Network;` for `BrainPerceptionTranslatorPack`, `SimPerceptionTranslatorPack`, `SimPathfindingTranslatorPack`, `BrainPathfindingTranslatorPack` — these remain in `Hrot.SimHost/Network/` (OUT OF SCOPE for Stage 1). Stage 2 must address these or move these packs first. This is a P2 item.

4. **SC3 test precision**: Parameterless `DeadReckoningSyncSystem()` uses `EntityLifecycle.Active` (not `.All`). The dev's test with two Active-lifecycle entities is more truthful than a ghost+local scenario would have been.

---

## Debt Tracker Updates

| Action | Item |
|---|---|
| ADD | DEBT-002 (P2): `NedReplicationModule.cs` still imports `Hrot.SimHost.Network` for 4 brain/perception/pathfinding packs not in scope of Stage 1. Must be resolved in BATCH-02 before module relocation can compile cleanly. |
| ADD | DEBT-003 (P3): Test domain ID hardcoding in `TranslatorPackTests.cs` — collision risk in parallel test runs |
| Keep 🔴 | DEBT-001 (P2): `_nedReplicationModule` field + `// TODO (P2 debt)` in `SimHostApp.cs` — targeted at Stage 3 |

---

## Suggested Git Commit Message

```
feat(mod-init): Stage 1 — push down architecturally coupled systems (MODINIT-S100–S107)

- Create Hrot.Network assembly (net8.0); wire into SimHost, IG, CGF, ClusterRunner
- Move DeadReckoningSyncSystem: Hrot.IG.Systems → Hrot.Common.Systems
- Move SharedTranslatorPack, KinematicTranslatorPack: Hrot.SimHost.Network → Hrot.Map.Common.Translators
- Move CognitiveTranslatorPack: Hrot.SimHost.Network → Hrot.Network.Translators
- Move 4 navigation translators (NavigationIntent*/NavigationStatus*): Hrot.SimHost.Network → Hrot.Map.Common.Replication.{Ingress,Egress}
- Update all callers to use new namespaces
- Add unit tests for DeadReckoningSyncSystem lifecycle behavior
- Add integration tests for SharedTranslatorPack factory output
- Validate layer boundaries: Hrot.Common and Hrot.Map.Common have no upward references

Zero behavioral changes. Build: 0 errors. Pre-existing test failures unchanged.
```
