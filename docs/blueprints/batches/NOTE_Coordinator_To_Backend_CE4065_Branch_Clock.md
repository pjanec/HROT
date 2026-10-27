<!--STATUS
state: LIVE — coordinator → backend: one finding from H3 step 0 (the branch from a replay)
updated: 2026-10-27
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Scenario_Regression.md — §10-6: the measurement this note summarises; H3 (RH-7) builds on the branch
-->
# NOTE — coordinator → backend: CE-4065, a branch from a replay comes up on the wrong sim clock

**Dispatched at** the commit that adds this file (`coordinator`).

| finding | measured | ask |
|---|---|---|
| ⛔ **CE-4065** — branched at replay sim **5 s**, the new live run's `GlobalTime.TotalTime` reads **12.71 s** | `--mode editor --headless`, `ua-civ-melee` live to 10 s (stepped over HTTP), Idle, `OperatingReplay`, seek `atSimSeconds: 5`, `POST /cluster/transition {target: OperatingLive}` ⇒ OperatingLive in < 1 s, *"Live-from-Replay branch complete … history 1465 frame(s)"*, steps run. 12.71 is the WALL seconds of that frame (23.7 s of wall for 10 s of sim) | the branch should resume on the frame's SIM time (DAR's sim axis). Anything on the sim clock after a branch is shifted by the paused wall time |
| ⚠ (same row) `target: LoadingLive` from a replay is accepted and **parks** the cluster in `LoadingLive` (clock frozen, 30 s) | same run | refuse it, or treat it as OperatingLive |

⭐ Not blocking: H3 (the branch harness, [Scenario Regression §10-6](../../DESIGN_Scenario_Regression.md)) aligns on the branch's own
clock (`t0 = clock at the branch − T`), so it will run either way — and will tell you when CE-4065's fix lands, because the offset
goes to zero.

_coordinator lane, 2026-10-27_
