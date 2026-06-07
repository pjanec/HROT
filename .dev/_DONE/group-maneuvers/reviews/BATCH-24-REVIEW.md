# BATCH-24 Review

**Status: APPROVED**

## Tests
- Squad-only: 68/68 pass (0 failures)
- Full suite: 101 failures — all in pre-existing Navigation/Geographic/Spatial.Eqs/Replication
  categories; no BATCH-24 tests fail. `DangerAreaProviderTests` zero-alloc failure is flaky
  under parallel execution and passes in isolation.

## Code Review

### `SquadCognitiveState.cs` — PASS
`_r1 (ulong)` correctly split into `uint LastManeuverSelectTick + uint _r1hi`. Total pool size
stays 592 B; layout tests unaffected.

### `CommanderUtilityTickSystem.cs` — PASS
- Static class; guards correct (Blackboard1024 + UtilityResultBuffer required).
- MissionOverride bit-0 check skips scorer, retaining forced ManeuverKind.
- Cadence gate: first-run bypass (`LastManeuverSelectTick == 0`) and interval check correct.
- Trace pointer: `Unsafe.AsPointer` on RW ref — safe pattern matching codebase convention.
- Writes `output.Top().WinningPostureId` via `state.ManeuverKind` — correct.

### `SquadInputs.cs` — PASS
- 5 new readers, all `ctx.Self = commander`.
- FNV-1a-16 constants computed; no collisions with existing 0xBA51 / 0x2457.
- All readers are zero-alloc; each walks UnitRoster at most once.
- `ActiveFeatureKindIs` encodes `DangerAreaKind` in `ctx.Params.BlueprintId` low byte — clean.
- `SquadPoolThreatAggregate`: normalises by 16.0f (max 16 contacts * 1.0f) — consistent.
- Default-safe pattern consistent across all readers.

### `ForceManeuverMapper.cs` — PASS
- JSON deserialized with `PropertyNameCaseInsensitive = true` — correct.
- `TryMap` returns false when `Blackboard1024` missing — SC-P3-03-3 satisfied.
- `ClearForceManeuverMapper` clears bit correctly.
- Both mappers stateless after construction (interface contract met).

### `ManeuverSelectStarterDecision.cs` — PASS
- `CurveKind.InverseLinear` used for Hold's SquadAmmoRollup consideration — valid deviation
  from `yShift=1f` (struct doesn't support yShift; InverseLinear is semantically identical).
- Option balance: StreetCrossing → DangerAreaCross wins (ActiveFeatureKindIs weight 0.9),
  OpenGround → BoundOverwatch wins (SquadStrengthRatio 0.8 + OpenGround 0.7 dominates) — both
  confirmed by integration tests.

## Success Criteria Verification
| SC | Result |
|---|---|
| SC-P3-01-1: stub ManeuverSelect selects higher option | PASS |
| SC-P3-01-2: MissionOverride skips scorer | PASS |
| SC-P3-01-3: cadence gate holds then fires at interval | PASS |
| SC-P3-01-4: trace buffer populated | PASS |
| SC-P3-02-1: SquadStrengthRatio full/partial health | PASS |
| SC-P3-02-2: ActiveFeatureThreatRating no active feature | PASS |
| SC-P3-02-3: ActiveFeatureKindIs kind flip | PASS |
| SC-P3-02-4: SquadAmmoRollup full/exhausted | PASS |
| SC-P3-02-5: zero-alloc | PASS (isolated) |
| SC-P3-03-1: ForceManeuver sets ManeuverKind + flag | PASS |
| SC-P3-03-2: ClearForceManeuver clears flag; scorer resumes | PASS |
| SC-P3-03-3: no Blackboard1024 returns false | PASS |
| SC-P3-04-1: StreetCrossing → DangerAreaCross | PASS |
| SC-P3-04-2: OpenGround → BoundOverwatch | PASS |
| SC-P3-04-3: trace populated | PASS |
| SC-P3-04-4: MissionOverride retains Hold | PASS |

## No issues found. Ready to commit.
