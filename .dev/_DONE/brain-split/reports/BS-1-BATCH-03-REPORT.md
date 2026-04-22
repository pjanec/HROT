# BS-1-BATCH-03 Report

**Batch:** BS-1-BATCH-03  
**Developer:** GitHub Copilot  
**Date:** 2026-03-26  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| TD-4 | ✅ | Removed `FdpLog.Debug` calls from `WeaponFireRequestIngressTranslatorTests` skip-paths |
| TD-5 | ✅ | Added `FireProcessing_BulletExistsWhenNotificationIsConsumed_OrderingProxy` test |
| BS1-T008 | ✅ | `WeaponFireNotificationEgressTranslator` created + 3 tests |
| BS1-T009 | ✅ | `WeaponFireIngressTranslator` (IG) created + `IgWeaponFireEvent` + 3 tests |
| BS1-T010 | ✅ | `HitResolutionSystem` updated to emit `DetonationNotification` + 3 tests |

---

## 🧪 Testing Results

**FDP.Toolkit.Combat.Tests:** 39 / 39 ✅  
**FDP.Toolkit.Physics.Tests:** 24 / 24 ✅  
**Hrot.SimHost.Tests:** 343 / 343 ✅  
**Hrot.IG.Tests:** 429 / 429 ✅  
**Full solution:** All project-specific suites pass. Two pre-existing flaky tests in `Hrot.SimHost.Integration.Tests` and `Fdp.Tests` occasionally fail when run in parallel with the full suite (DDS participant timing issues); both pass deterministically when run in isolation.

**Key Test Scenarios Verified:**
- [x] `WeaponFireNotification` → `WeaponFire` DDS write (1 and N events)  
- [x] No DDS write when bus is empty
- [x] `WeaponFire` DDS → `IgWeaponFireEvent` with matching payload
- [x] Unknown entity IDs still publish `IgWeaponFireEvent` (no throw)
- [x] Bullet hit emits both `HitEvent` and `DetonationNotification` with correct position and IDs
- [x] LOS-check ray produces no `DetonationNotification`
- [x] Unknown entities produce zeroed IDs in `DetonationNotification` (no throw)
- [x] TD-5 ordering proxy: bullet entity with matching Shooter exists when notification consumed
- [x] All existing HitResolution tests (3 original) pass unchanged

---

## 📝 Developer Insights

**Q1: Issues encountered + how resolved**

**Circular dependency for `DetonationNotification`:** The biggest challenge was that `HitResolutionSystem` lives in `FDP.Toolkit.Physics`, which cannot reference `FDP.Toolkit.Combat` (Combat already references Physics). `DetonationNotification` was originally in `FDP.Toolkit.Combat.Events`.

Resolution: Moved `DetonationNotification` to `FDP.Toolkit.Combat.Contracts` — the thin contracts assembly already referenced by both Physics and Combat (the same pattern used for `HitEvent` via DEBT-031). The move is zero-impact for consumers: `CombatComponentTests.cs` already imports both `Contracts` and `Events`; the struct is resolved unambiguously from the Contracts namespace.

**Shooter network ID without `BallisticProjectile`:** `HitResolutionSystem` cannot read `BallisticProjectile.Shooter` without a circular dep. Resolution: `BallisticsSystem` already sets `RaycastRequest.IgnoreEntity = proj.Shooter`, so `batch.Requests[i].IgnoreEntity` carries the shooter entity handle. The `NetworkEntityMap.TryGetNetworkId` call on this field gets the shooter network ID without any Combat assembly dependency.

**TD-5 bus model limitation:** True intra-frame ordering (notification published _after_ `World.CreateEntity()`) is not observable from outside the synchronous `OnUpdate` call — both bullet entity and notification exist by the time the test asserts. The test `FireProcessing_BulletExistsWhenNotificationIsConsumed_OrderingProxy` verifies the strongest behavioral proxy available: when the notification is visible on the bus, a live `BallisticProjectile` entity with the matching `Shooter` is present in the world. This catches deletion of bullet creation, wrong shooter IDs, and phantom notifications from previous frames.

**Q2: Weak points noticed**

- `request.IgnoreEntity` is used as a proxy for "shooter entity" in the new code. This assumption holds because `BallisticsSystem` always sets `IgnoreEntity = proj.Shooter`. If future code ever creates bullet rays with a different `IgnoreEntity` policy, the `ShooterEntityId` in `DetonationNotification` would be wrong. Consider documenting this convention explicitly in `RaycastRequest.IgnoreEntity`'s XML doc.
- The `NetworkEntityMap` optional-injection pattern (`HitResolutionSystem(NetworkEntityMap?)`) means testing the path without a map requires nothing, but the positive path must be explicitly opted into. This is intentional for backward compatibility with legacy tests that don't have a Replication dependency.

**Q3: Design decisions beyond the spec**

- **Zeroed IDs instead of skipping:** The spec requires that `DetonationNotification` uses entity IDs "convertible via EntityMap". When an entity isn't in the map (edge case: entity destroyed before raycast resolves), `TryGetNetworkId` returns false and the ID stays 0. Rather than skipping the notification entirely (which would lose the position data useful for explosion effects), the notification is still published with zeroed IDs. The IG/consumer can decide how to handle zero IDs. This is documented in the test `BulletHit_WithUnknownEntities_StillPublishesDetonationWithZeroIds`.
- **`FDP.Toolkit.Replication` added to Physics csproj:** Checked for circular deps before adding — Replication does not reference Physics. This is the minimum footprint for `NetworkEntityMap`.

**Q4: Edge cases discovered**

- `batch.Requests[i]` and `batch.Hits[i]` are parallel arrays. If future code ever processes hits in a different order from requests (batch reordering), this assumption would silently produce wrong hit positions. The parallel-array contract is documented in `RaycastBatchData` but is worth a unit test in the physics solver.
- When `hit.T == 0`, the hit position equals `request.Start` (bullet origin). When `hit.T == 1`, it equals `request.End`. Both are degenerate but valid.

**Q5: Performance / allocation concerns**

- No allocations introduced. `DetonationNotification` is an unmanaged struct published via `World.Bus.Publish` (zero alloc).
- `NetworkEntityMap.TryGetNetworkId` does a dictionary lookup (O(1) amortized). Called at most once per bullet hit per frame — hot path impact is negligible.
- `request.Start + hit.T * (request.End - request.Start)` is 3 scalar multiplies and 6 adds using `System.Numerics.Vector3` SIMD — negligible.

---

## ⚠️ Outstanding Issues / Next Steps

- Pre-existing flaky DDS tests in `Hrot.SimHost.Integration.Tests` and `Fdp.Tests` are timing-dependent and unrelated to this batch.
- `MunitionDetonationEgressTranslator` (BS1-T011) is intentionally out of scope for this batch; `DetonationNotification` events are now emitted and ready for it in the next batch.
- The `WeaponFireIngressTranslator` in IG currently does not resolve entity handles (`Entity` from `NetworkEntityMap`) into `IgWeaponFireEvent` — it only carries raw network IDs. Visual layer consumers must look up entities themselves. This is by design per spec; if the IG muzzle-flash system needs local handles, they can be added to `IgWeaponFireEvent` in a follow-up batch.
