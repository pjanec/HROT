# BATCH-26 Review

**Status: APPROVED**

## Tests
- Squad-only: 86/86 pass (+7 over BATCH-25 baseline of 79)

## Code Review

### `SquadEventIngressSystem.cs` — PASS
- `PrevAmmoArray [InlineArray(16)]` instance field correct, zero-alloc.
- InlineArray write via `MemoryMarshal.CreateSpan/Unsafe.As` — matches established pattern.
- ShotFired: `currentAmmo < prevAmmoSpan[m]` — correct (detects decrease not increase).
- First-call behavior: prevAmmo initializes to 0; if member starts with ammo > 0, first
  call snapshots without firing (because `currentAmmo >= 0 == prevAmmoSpan[m]` if ammo > 0).
  This is correct — no false ShotFired on first tick.
- Nav events: `FarSideIntentId/BoundIntentId/DefiladeIntentId = 0` sentinel disables detection cleanly.
- All three nav event kinds checked in same branch — correct.

### `SquadEventIngressSystemTests.cs` — PASS (7 tests)
- SC-P4-03-1: ShotFired on ammo decrease (2 tests) ✓
- SC-P4-03-2: FarSideReached on intent match / mismatch (2 tests) ✓
- SC-P4-03-3: PhaseSequencer dwell-timeout integration (2 tests) ✓
- Extra: exactly-one-per-firing (1 test) ✓

### Deviations from spec
1. `unsafe` keyword on class — required due to `Unsafe.As` usage (same as VetoDetectionSystem).
2. `using Fdp.Toolkit.Behavior.Components` in test — needed because `Blackboard1024` lives there.
   Both are correct adaptations.

### TimerFallback design decision
TimerFallback is NOT emitted by `SquadEventIngressSystem`; `PhaseSequencer.Advance` handles
it directly. SC-P4-03-3 tests exercise `Advance` directly to verify dwell-timeout. Design
aligns with PhaseSequencer API.

## No issues found.
