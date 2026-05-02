# BATCH-06 Review

**Batch:** BATCH-06  
**Status:** APPROVED  
**Reviewed by:** Dev Lead  
**Date:** 2025-07-15

---

## Verification Results

| Check | Result |
|-------|--------|
| `dotnet build IOS-IG-SimHost.sln` | Build succeeded. 0 Error(s), 0 Warning(s) |
| Fdp.Core.Tests (718 + Hrot tests) | All non-integration tests pass |
| Integration tests | 10 pre-existing failures unchanged |

---

## Task Review

### MPM-P5-T04: Replace BehaviorUiSetup + CgfBehaviorSetup behavior-ID strings - APPROVED

`BehaviorUiSetup.CreateRegistry()` now uses `BehaviorSchemaDiscovery.AutoRegister`. Option A (throwaway remapper) is clean and correct. Unused import removed.

`CgfBehaviorSetup.RegisterAll()` string literals replaced with DTO `BehaviorId` constants. The transitive Hrot.Core accessibility via Hrot.Common is verified by the successful build. `BehaviorDefinition.Name` fields left as-is per minimize-diff rule (they are display names, not registry keys). `CreateBehaviorRemapper()` now uses `BehaviorSchemaDiscovery.AutoRegister` registering all 9 DTOs (registering extra harmless ones is a no-op at remap time).

### MPM-P5-T05: Rebuild BehaviorCatalog Using Reflection - APPROVED

Military/insurgent lists correctly built via `BuildMap()` from `[BehaviorContract]` assembly scan. Civilian list (`WanderCivil`, `PanicFlee`) and default fallback preserved as hardcoded.

Note: The dynamically-built lists now include `Idle` and `WanderMilitary` in the MilitaryApc list (they carry `AllMilitary` and `MilitaryApc` flags respectively), which were absent from the original hardcoded list. This is correct - the DTO category annotations are the source of truth, and the original list was incomplete. The test assertion only checks "contains", not exact equality.

### MPM-P5-T06: Update CgfNodes.cs TreeName Strings - APPROVED

All 5 TreeName literals replaced with DTO `BehaviorId` constants. The `const string` → `static readonly string` promotion is correct and necessary for interpolated strings. Naming conflicts with private inner classes correctly handled via fully-qualified names. Runtime JSON values identical.

### MPM-P5-T07: Create BehaviorTestHelper + Update Test Files - APPROVED

`BehaviorTestHelper.cs` created correctly. Test files in `Hrot.Presentation.Tests` updated (project already references Hrot.Core). `Hrot.SimHost.Tests` and `Hrot.Network.NED.Tests` left unchanged (no Hrot.Core reference - correct decision per instructions). The NED test strings are network-protocol test data representing wire format values, not behavior domain objects.

---

## Findings

No deviations from spec. The scope of "magically appearing" extra doctines in MilitaryApc via reflection is expected behavior - the DTO category flags define canonical applicability.

---

## Debt Tracker Update

No new debt items. The BehaviorIds duplication noted in BATCH-05 review remains as a low-priority item. The `Hrot.SimHost.Tests` and `Hrot.Network.NED.Tests` test strings are acceptable (network protocol strings are by definition ground truth).
