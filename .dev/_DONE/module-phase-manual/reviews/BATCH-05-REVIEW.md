# BATCH-05 Review

**Batch:** BATCH-05  
**Status:** APPROVED WITH NOTES  
**Reviewed by:** Dev Lead  
**Date:** 2025-07-15

---

## Verification Results

| Check | Result |
|-------|--------|
| `dotnet build IOS-IG-SimHost.sln` | Build succeeded. 0 Error(s), 0 Warning(s) |
| Full test sweep | 10 pre-existing integration failures, baseline unchanged |

---

## Task Review

### MPM-P5-T01: Create BehaviorCategory + BehaviorContractAttribute - APPROVED

Both types created correctly in `Hrot.Core/MapDefinitions/Behavior/`. `AllMilitary = 14` (2|4|8) correct. Attribute `Inherited=false, AllowMultiple=false` correct.

Note: `BehaviorIds.cs` (internal constants) was created as a companion file because `CgfBehaviorIds.cs` lives in `Hrot.CGF` and `Hrot.Core` cannot reference it without a circular dependency. This is a pragmatic solution but creates duplication. BATCH-06 should assess whether to consolidate or keep them separate.

### MPM-P5-T02: Decorate DTOs + Create Marker DTOs - APPROVED WITH NOTE

Key finding: the 3 parameter DTOs with JSON properties (`FireAtTargetParamsJsonDto`, `MoveToLocationParamsJsonDto`, `FollowRouteParamsJsonDto`) live in `Fdp.Toolkits`, not `Hrot.Core`, and cannot be decorated in-place. The developer correctly identified the circular dependency problem and created new Hrot.Core versions with full property definitions.

This is a reasonable approach for now. BATCH-06 must decide: keep dual DTOs (Fdp.Toolkits originals for legacy callers + Hrot.Core versions for behavior auto-registration), or consolidate by moving the originals or creating a shared layer.

All 9 DTOs present with correct `[BehaviorContract]` and `const string BehaviorId`.

### MPM-P5-T03: Create BehaviorSchemaDiscovery - APPROVED

Hosted in `Hrot.Presentation` - correct choice (already references both `Hrot.Core` and `Fdp.Toolkits`, and `BehaviorUiRegistry` is defined there). No new project references created.

`Register<T>` signatures verified - both `BehaviorUiRegistry` and `ScenarioBehaviorRemapper` have `Register<TDto>(string behaviorId)`.

---

## Notes for BATCH-06

1. **BehaviorIds duplication:** `Hrot.Core/BehaviorIds.cs` and `Hrot.CGF/CgfBehaviorIds.cs` both define the same integer constants. BATCH-06 should add the 3 missing constants (ConvoyEscort, InfantryCombat, Ambush) to `CgfBehaviorIds.cs` and then use those in `BehaviorIds.cs` (or remove `BehaviorIds.cs` if `Hrot.Core` can avoid needing these values).
2. **Legacy Fdp.Toolkits DTOs:** `FireAtTargetParamsJsonDto`, `MoveToLocationParamsJsonDto`, `FollowRouteParamsJsonDto` in Fdp.Toolkits remain unchanged. After `BehaviorUiSetup.cs` switches to `BehaviorSchemaDiscovery.AutoRegister`, these may become unused - check in BATCH-06.
3. **ConvoyEscort, InfantryCombat, Ambush** are not yet in either setup file (only in `BehaviorCatalog.cs`). `BehaviorSchemaDiscovery.AutoRegister` will include them automatically via the new Hrot.Core marker DTOs.

---

## Debt Tracker Update

No new debt items. The BehaviorIds duplication is an expected intermediate state that BATCH-06 will resolve.
