# BATCH-OFX-03 Review

**Batch:** BATCH-OFX-03  
**Reviewer:** Development Lead  
**Date:** 2026-06-03  
**Status:** APPROVED WITH CORRECTION

---

## Summary

10 tasks implemented. New tests pass per project-level runs. One pre-existing P1 defect (ComponentId collision 257-259 between nav and squad components) was discovered and fixed during review.

---

## Correction Applied During Review

**ComponentId collision (P1 -- fixed before commit):**

`DangerAreaSensor`, `DangerAreaCognitiveBuffer`, and `MovementModeIntent` were assigned IDs 257, 258, 259 in `GlobalComponentIds.cs` (by BATCH-23). These IDs were already occupied by `NavAgentProfile`, `NavigationCorridorMuscle`, and `NavigationCorridorPreview` in `NavigationContractsComponentIds.cs` (introduced by navig-2 BATCH-04). The collision caused 48 `Phase3IntegrationTests` to throw `InvalidOperationException` when nav and squad components were registered in the same process.

Fix: reassigned squad IDs to 262, 263, 264 (above the 257-261 nav block) in `GlobalComponentIds.cs`. `Fdp.Core` builds clean; struct attributes reference the constant names so the update is automatic.

---

## Test Quality Assessment

- **OFX-007**: `MergeContact_NewerLowerThreat_UpdatesPosition` -- member 0 at tick 10, member 1 at tick 20 (newer, lower threat, distinct XYZ). Asserts exact `contact.PositionX/Y/Z` match the newer sighting. Position and threat-score are independently asserted.

- **OFX-014**: Three boundary tests:
  - `Advance_AtExactDwellTick_DoesNotAdvance` -- `currentTick=100, dwell=100` -> `false`
  - `Advance_OneTick_AfterDwell_DoesAdvance` -- `currentTick=101, dwell=100` -> `true`
  - `Advance_DwellTimeoutZero_NeverAdvances` -- `currentTick=99999, dwell=0` -> `false`

- **OFX-016**: `PurityAnalyzer_EQS002_ReportsLocationAtImpureIdentifier_NotMethodDeclaration` extracts source text at the diagnostic's `SourceSpan` and asserts `identifierText == "_hitCache"` (not `"Build"`). Direct verification of squiggle location.

- **OFX-015**: `EmitAndRoundTrip_UtilityDecisionAsset_StructuralEquality` -- builds a 2-consideration asset, emits C#, parses with `CSharpSyntaxTree.ParseText`, walks AST for `.Consider(...)` invocations, extracts input names and asserts both `HealthFraction` and `ThreatRange` present. Real Roslyn parse.

- **OFX-017**: `PollIngress_NotAliveDisposed_RemovesCacheEntry` -- injects NotAliveDisposed sample, asserts `_childEntityCache` no longer contains the key; `RemoveCacheEntry_NonExistentKey_IsNoOp` guards against KeyNotFoundException.

- **OFX-008**: 3 tests on `ComputeDashParams(zoomLevel)` -- at zoom=1 returns base values (6f, 4f); zoom=2 halves them (3f, 2f); zoom=0.5 doubles them (12f, 8f). Inverse-zoom-stable.

- **OFX-020**: `DrawAnnotation_EdgeMidpoint_DrawsAtGeometricMidpoint` -- two nodes with known canvas positions, asserts draw position is arithmetic mean. Exact coordinate check.

---

## Verdict

**Status: APPROVED (with ComponentId correction committed together)**

All 10 tasks implemented. Test quality is high. Pre-existing ComponentId collision fixed inline.

---

## Commit Message

```
fix: remaining fixes (BATCH-OFX-03) + ComponentId collision repair

Tasks: OFX-007, OFX-008, OFX-013, OFX-014, OFX-015, OFX-016, OFX-017, OFX-020, OFX-021, OFX-026

- OFX-007: MergeContact position guard separated from threat-score guard (lastSeenTick independent)
- OFX-008: DrawAnnotation uses dashed stroke with inverse-zoom ComputeDashParams
- OFX-013: RoleSlotAssignmentPrimitive clears roles before greedy assignment (stale RoleId fix)
- OFX-014: PhaseSequencer.Advance uses strict > and 0-dwell early return (off-by-one + immediate abort fix)
- OFX-015: UtilityFluentEmitter round-trip test via Roslyn CSharpSyntaxTree parse + AST extraction
- OFX-016: EQS002 diagnostic location points at impure identifier (id.GetLocation not method symbol)
- OFX-017: EqsResultIngressTranslator removes child-entity cache entry on NotAliveDisposed
- OFX-020: EdgeMidpoint badge computed as geometric midpoint of both endpoint nodes
- OFX-021: EvaluateSensor skips re-generation when Phase == _AwaitingRaycasts
- OFX-026: AssignmentSlot round-trip test extended to cover Flags=0x05

ComponentId fix (pre-existing P1 collision):
- DangerAreaSensor, DangerAreaCognitiveBuffer, MovementModeIntent reassigned to 262, 263, 264
  (previously collided with NavAgentProfile, NavigationCorridorMuscle, NavigationCorridorPreview at 257-261)
```
