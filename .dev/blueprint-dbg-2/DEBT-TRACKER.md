# Debt Tracker — Node-Granular Stepping (blueprint-dbg-2)

P2 = should fix (target batch) · P3 = nice-to-have. Never delete rows; mark ✅ when resolved.

| ID | Pri | Item | Found | Target | Status |
|----|-----|------|-------|--------|--------|
| DBG2-D1 | P3 | `FlightRecorderExample.cs:62` and `ShowcaseGame.cs:224` use `GlobalVersion-1`/`GlobalVersion` as a frame index in example/showcase code — correct in normal play (GV==ST), would over-capture during debug. Harmless (examples), migrate to `SimulationTick` if ever exercised under debug. | BATCH-00 | best-effort | open |
| DBG2-D2 | P3 | Pre-existing reds NOT introduced by us: Fdp.Core.Tests 2 timing benchmarks (pass in isolation); Fhsm 2; ModuleHost 6 (convoy `Assert.Same()`); Hrot.Blueprints.Tests 7 (incl. `TickFrame_1000Frames_AllocatesZeroBytes`). Track separately; do not let them mask new regressions. | BATCH-00 | open |

## Carried design risks (watch during review)
- **Frame-clock reader miss (P1-class if it slips):** a `GlobalVersion` reader that should be `SimulationTick` but isn't will break ONLY during a debug session; normal play keeps both clocks in lockstep so the regression suite won't catch it. BATCH-00 must produce a complete classified reader audit + invariant assert. Re-scan after any later batch that adds `GlobalVersion`/`.Tick` reads.
- **Managed-component capture alloc:** `RecordSubTickDelta` allocates for dirty managed chunks. Acceptable while debugging; ensure it NEVER runs on the normal (non-debug) tick path — guard behind debug-active. Confirm the `TickFrame_1000Frames_AllocatesZeroBytes` test stays green.
- **Mid-tick ECB invisibility:** deferred structural ops absent from mid-tick captures — must be shown as not-yet-applied, never as resolved (BATCH-02/03).
