<!--STATUS
state: LIVE — backend → behaviors: the answer to NOTE_Behaviors_To_Backend_Sight_Beyond_100m.md (both asks).
updated: 2026-10-19
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Sensors_And_Doctrine.md — §5.7a: the as-built correction (the dwell's clock on a module snapshot)
  - docs/blueprints/DESIGN_Time_Architecture.md — P8 ①: the snapshot clock fact this supersedes
  - docs/designs/look-direction/DESIGN_Look_Direction_And_FOV.md — §3.4: the demo that found it
-->

# NOTE — backend → behaviors: why the man at 113 m was never acquired — fixed (`CE-3267`)

Answers [`NOTE_Behaviors_To_Backend_Sight_Beyond_100m.md`](NOTE_Behaviors_To_Backend_Sight_Beyond_100m.md). Row:
[`CE-3267`](../Blueprint_Issues_Tracker.md).

**① What stopped the sighting — backend's defect, in BR-7.** Your first suspect was right, though not the formula. The EQS solver runs
as a background module on a SNAPSHOT of the world, and a snapshot carried no `GlobalTime`, so the memory stage read the time as 0 for
ever. The acquisition dwell (`now − VisibleSince ≥ AcquireAfter`) therefore never grew. Within 100 m a fully exposed man's dwell is exactly 0
and passes outright — your 70 m control — while past 100 m it is a fraction of a second, and it waited for ever.
⭐ **Fixed:** every snapshot provider now copies the frame's clock into the module's view (`EntityRepository.CopyClockFrom`), so the
113 m man is acquired within a tick or two. Nothing to change on your side; re-run `ld-sentry-surprise` with A at y = 190 when you next
launch (the runner must be rebuilt after the merge).

**② "A scenario's larger receptor range can never widen the implicit sensor's search" — not so (measured).** The visual sensor's
candidates come from its OPTICS range, not from `EqsSensor.SearchRadius`: `VisualSensorGenerator.Generate` asks the broadphase with the
range `VisualPerception.Optics` returns (`VisualPerception.cs:125-130`), which for the implicit sensor is the unit's live
`PerceptionReceptor.VisionRange` (×4 with binoculars or a scope up). `SearchRadius` (the TKB's 150 m) is not read on that path, so
the scenario's 300 m is honoured.

⚠ Also closed by ①: every heard-sound trace was stamped at time 0, so the editor's hearing gizmo (traces younger than a few seconds)
never drew one.

---
_backend lane, 2026-10-19_
