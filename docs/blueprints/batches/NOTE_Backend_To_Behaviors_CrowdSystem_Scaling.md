<!--STATUS
state: LIVE — backend → behaviors: a NOTE (a measurement on your system, from the civilian perf gate).
updated: 2026-10-17
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §7.4a the perf gate as built and what it measured (THIS note's basis); §5f CrowdSystem (yours)
-->

# NOTE — backend → behaviors: `CrowdSystem` grows ~quadratically with the crowd *(found by CE-3222)*

The civilian perf gate ([§7.4a](../../DESIGN_Civilians_And_Crowd_Control.md), `python3 scripts/perf-civilians.py 100 300`) measured
your `CrowdSystem` on CGF at **0.45 ms/frame for 100 civilians and 3.06 ms for 300** — ×6.8 for ×3, the shape of an all-pairs pass.
`BrainTickSystem` grew ×2.8 over the same step, so the crowd pass is the one that will hurt first at 500+.

Not urgent at 300 (it is ~2 % of CGF's frame there); worth a spatial bucket (the `SpatialHashSystem` grid SimHost already keeps, or
your own) before the 500-civilian demos. Re-measure with the same command; it prints `CrowdSystem` in its table.

⚠ Two backend findings from the same run, for context: `WeaponAmmoMirrorSystem` was the same shape and is fixed (`CE-3240`);
`EqsResultUpdateSystem` on SimHost is open (`CE-3241`).

## ⛔ Update `2026-10-17` (later) — at 500 it is CGF's biggest system

With the backend's three crowd-scaling fixes in (`CE-3240`, `CE-3241`, `CE-3242` — the 500-civilian scenario now loads in 13 s), the
same command at **500 civilians** measures `CrowdSystem` at **103.6 ms/frame (max 159)** against **3.16 at 300** — ×33 for ×1.7, far
past quadratic, so something changes shape between 300 and 500 (one crowd group swallowing the town? a pass per member per member
of its group?). It is now CGF's largest system — more than the recorder — and CGF runs 338 ms/frame at 500. ⭐ This is the one that
decides whether 500 is playable: `python3 scripts/perf-civilians.py 300 500` reproduces it in ~6 minutes.

**Ids allocated:** none (backend rows CE-3240…CE-3243 are in the tracker).
