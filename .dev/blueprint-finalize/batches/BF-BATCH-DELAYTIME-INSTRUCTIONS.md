# BF-BATCH-DELAYTIME: LatentDelay must wait a RELATIVE duration (time + d), not an absolute time
**Single objective. Est:** ~3h   **Dependencies:** BF-BATCH-SEQ2 (committed 2ddbc230).

## The one bug to fix
A `LatentDelay`/`Delay(d)` node currently lowers to `s.Cursor.WaitUntilTime = d` — the **raw duration as an absolute
sim-time**. The resume check is `if (time < WaitUntilTime) keep-waiting; else elapsed`. So the delay only waits
correctly the first time (when sim time happens to be < d); once sim time passes `d`, every future delay is instantly
"elapsed" and the behavior runs every tick with no wait. **It must wait `d` seconds from when the delay starts:**
set `WaitUntilTime = time + d` (current sim time + duration).

**Evidence (generated Count4 Tick, fresh-tick block):**
```csharp
var __t3 = 1f;                       // duration literal
s.Cursor.ResumeAt = 1;
s.Cursor.WaitUntilTime = __t3;        // BUG: absolute. Must be: time + __t3
return;
```

## Fix
Find where the LatentDelay's `WaitUntilTime` is computed/emitted (the delay op in
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage5_Schedule.cs` — search
`WaitUntilTime` and the `LatentDelayNode`/`BuildLatentDelayOp` path, and the corresponding emit in
`Compiler/Emit/`). Change the stored value from `duration` to `time + duration` (use the same `time` parameter the
resume check already reads). Apply consistently for **Instance** and **AiPrimitive** dispatch. Do not change the
resume comparison (`time < WaitUntilTime` stays).

## Tests required — PRESCRIBED (this is the assertion the prior test was missing)
Add to `Hrot.Blueprints.Tests` (e.g. `SequenceEmitIntegrationTests` or a new file). Build
`EventEntry → Sequence(Then0 → Count = Count+1, Then1 → Delay(d))`, `Dispatch=Instance`, with **d = 1.0**. Compile+load
and drive `Tick` directly, **starting at a LARGE absolute sim time so the absolute-vs-relative bug is exposed**
(start `time = 100.0`, not 0):
1. `Tick(time=100.0)` → `Count == 1`, suspended.
2. `Tick(time=100.5)` (half a period later) → **`Count == 1`** (still waiting — this FAILS today, because absolute
   `WaitUntilTime=1.0` is already < 100.5 so it would wrongly elapse).
3. `Tick(time=101.01)` (just past 100+d) → delay elapsed, cursor resets.
4. `Tick(time=101.02)` → `Count == 2` (second period started).
5. `Tick(time=101.5)` (half of the SECOND period) → **`Count == 2`** (the second delay also waits the full
   duration — the key assertion proving it's relative, not absolute).
6. `Tick(time=102.03)` → `Count == 3`.
Name it `Sequence_LatentDelay_WaitsFullDurationEachPeriod`. The step-2 and step-5 "still waiting" assertions are the
ones that catch the bug — do not omit them.

## Success Criteria
- [ ] `WaitUntilTime = time + duration` for Instance + AiPrimitive; resume check unchanged.
- [ ] `Sequence_LatentDelay_WaitsFullDurationEachPeriod` passes (incl. the step-2 & step-5 "still waiting" asserts).
- [ ] Full `Hrot.Blueprints.Tests` suite green except the one documented pre-existing
      `TickFrame_1000Frames_AllocatesZeroBytes` (unchanged); regenerate any golden that legitimately shifts (delay
      now emits `time + d`) after confirming the diff is only that.
- [ ] Report at `.dev/blueprint-finalize/reports/BF-BATCH-DELAYTIME-REPORT.md`.

## DO NOT STOP UNTIL VERIFIED GREEN
Run `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests` (no `BLUEPRINT_REGENERATE_SNAPSHOTS`) yourself and
read the result. Not done until `Failed: 0` (except the one documented zero-alloc test, which must remain unchanged —
do not touch it). On any other failure: diagnose, fix, re-run the whole suite; loop until green. Do not report
complete with red tests. End the report with the green full-suite output.

## Guardrails
One objective only (the delay duration). Do NOT touch other batches' committed files, edit user blueprint assets,
suppress diagnostics, or weaken assertions. Read `.dev/.guides/DEV-GUIDE.md` first.

---

## CORRECTIVE (round 2): the test is NOT unsatisfiable — wire the duration. Make it pass.
The compiler fix is verified correct (generated code now emits `WaitUntilTime = time + duration`). But
`Sequence_LatentDelay_WaitsFullDurationEachPeriod` was left RED, rationalized as "can't set a non-zero delay
duration." **That is wrong** — you just didn't wire it. This is a ~5-line test fix; do not stop until it is GREEN.

**Why it currently fails:** `Stage5_Schedule.BuildLatentDelayOp` (line ~1030) reads the duration from the
LatentDelayNode's **first non-exec data-IN pin** via `ResolveDataPin`; with **no wired pin it defaults to `0f`**, so
`WaitUntilTime = time + 0 = time` → the delay elapses instantly → step-2 "still waiting" fails. Your test gave the
Delay no duration source.

**Exact fix (mirror how `SequenceSchedulingTests` wires Literal→pin with explicit Guids):**
1. Give the `LatentDelayNode` a non-exec **data-IN** pin with a known Guid, e.g.
   `new Pin { Id = pinDelayDurIn, Name = "Duration", Direction = "In", IsExec = false, TypeRef = new() }` (plus its
   exec-in/exec-out pins as before).
2. Add a `LiteralNode { Id = litId, TypeId = "System.Single", ValueJson = "1.0", Pins = { new Pin { Id = pinLitOut,
   Name = "Value", Direction = "Out", IsExec = false, TypeRef = new() } } }`.
3. Add a `Link { FromNodeId = litId, FromPinId = pinLitOut, ToNodeId = delayId, ToPinId = pinDelayDurIn }`.
Now the duration resolves to 1.0 → `WaitUntilTime = time + 1.0`, and the step-2/step-5 "still waiting" assertions pass
with the fix (and would fail without it). Keep d = 1.0 and the start-at-time=100.0 schedule from the test spec above.

**Verify before reporting:** the generated source for this test asset shows `WaitUntilTime = <time> + 1` (NOT
`+ 0`, NOT absolute `1`). Then run the FULL suite (no regen flag) and confirm `Failed: 0` except the one documented
zero-alloc test. Do not report complete with the DELAYTIME test red.
