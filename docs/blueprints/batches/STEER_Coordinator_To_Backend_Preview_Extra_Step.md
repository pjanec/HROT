<!--STATUS
state: LIVE
updated: 2026-11-13
current-answer: the whole file
related-designs:
  - STATUS_Windows_Findings.md (on exp/threaded-build) — F-3 (CE-4503) and F-D2, the running work this steers; NOT amended
  - STATUS_Windows_Threaded_Rerun.md (on exp/windows-threaded-measure) — V-4, the measurement
  - ../blueprints/DESIGN_Time_Architecture.md — §17 stepped mode
-->

# STEER — coordinator → backend (the running Windows-findings batch): F-D2 is probably the EXTRA STEP — fix the paused entry

🔒 User, `2026-11-13`: *"yes"*. **Not an amendment (rule 1c):** inside F-3's own scope ("find what differs and fix it at its source").

| | |
|---|---|
| ⭐ **the Windows measurement** (V-4) | every stepped run with the SAME step count was equal at 1 / 4 / 12 threads; 2 of 7 threaded runs ended at `simTime` **2.0167 = 121 steps** where their peers ended at **2.0 = 120** — the 57 differing types and 7 extra entities all follow from that one step. The switch-off editor ALWAYS ends at 121; the threaded one usually at 120 |
| ⭐ **first** | check `simTime` on F-D2's failing runs (the gate prints it). Same count ⇒ F-D2 is something else; keep chasing it |
| ⭐⭐ **then fix it at the source** | entering the preview PAUSED (`/preview/enter {startPaused:true}`) takes exactly ZERO steps — on the serial and the threaded kernel alike; today one step slips through before the pause takes hold (always serial, a race threaded). A rail pins the step count after a paused entry in both kernels, red-proved |
| ⭐ **and the gate** | `stepped-hash.py` compares runs only at equal `simTime`, and says so loudly when they differ (exit 2, "a run took a different number of steps"), never as a state difference |
| unchanged | everything else in the batch, and the queued E-2…E-4 |

---
_coordinator lane, 2026-11-13_
