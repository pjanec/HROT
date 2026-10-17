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

**Ids allocated:** none (backend rows CE-3240…CE-3243 are in the tracker).
