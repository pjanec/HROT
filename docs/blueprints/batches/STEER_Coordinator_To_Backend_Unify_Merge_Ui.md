<!--STATUS
state: LIVE
updated: 2026-11-13
current-answer: the whole file
related-designs:
  - HANDOFF_Coordinator_To_Backend_Unify_Threaded_Defaults_On.md — the RUNNING batch this steers (started at faf06b8dd; U-1, U-2 done)
  - NOTE_Coordinator_To_Ui_Showcase_On_The_Parallel_Branch.md — why: the showcase's behaviour code starts on a branch from the unified one
  - ../../DESIGN_ImGui_Draw_Data_Renderer.md (on ui) — CE-1495, the renderer change being merged
-->

# STEER — coordinator → backend (the running unify batch): merge `ui` into the unified branch

🔒 User, `2026-11-13`: *"can we safely merge? if so write other steer please to the unified version has it"* — about `ui`'s 10 commits the
parallel branch lacks: 3 of the city battle showcase design, 7 of terrain / traffic / renderer work (CE-1493 / 1494 / 1495).

**Rule 1c:** a steer, not an amendment — an ADDED item (call it **U-1b**), after U-2 and before U-3. Everything else unchanged, including the
STEER that withdrew U-6.

## Measured — a trial merge of `origin/ui` into `origin/exp/threaded-build` (`d487f0afd`)

76 files, +2 762 / −112; **three conflicts**, all small:

| file | the two sides | ⭐ the resolution |
|---|---|---|
| `Hrot/Runner/Hrot.ClusterRunner/Presentation/FrameStats.cs` (2 hunks) | this branch formats every number invariant by construction (F-5, R-4221); `ui` adds the ImGui render time, the largest draw list and the per-phase garbage line (CE-1495) | ⭐ KEEP BOTH: `ui`'s new fields, written through this branch's invariant formatting — ⛔ never a culture-formatted line again |
| `Hrot/Runner/Hrot.ClusterRunner/Program.cs` (1 hunk) | this branch restructured the main loop (the threaded node: the UI's frame separate from the sim's tick); `ui` adds `Fdp.Presentation.Raylib.ImGuiRenderer.End()` at the end of the ImGui frame | ⭐ `ImGuiRenderer.End()` where the ImGui frame ends in EVERY loop variant (threaded, single-thread, `-m all`) — one call site per frame, ⛔ never inside the sim step |
| `docs/blueprints/Blueprint_Issues_Tracker.md` (2 hunks) | rows appended by both | keep both sides' rows |

⚠ Auto-merged, to check, not trust: `Hrot.Traffic/TrafficModules.cs` — `ui` only passes the drape into `mirror.Resolve(…)`; this branch's N-9 fix
(the traffic frame owns its data and is value-copied across worlds) must still hold — its rail must stay green.

## Gates for U-1b

| | |
|---|---|
| the traffic suites (`Hrot.Traffic.Tests` — road scene, runtime) and the N-9 traffic-frame rail | green |
| the ImGui renderer rails (`DrawListVertexRail`, `ImGuiRendererFrameRail`) **under `xvfb-run -a`** (not skipped) | green |
| the threaded 1 000-man session once (the M-7 chain) with the defaults ON — the renderer change runs in the threaded UI loop | every step ok, guard 0 |
| `[FrameStats]` under `cs-CZ` | a decimal point, with `ui`'s new fields present |

Then push, and continue with U-3. The `showcase` branch (NOTE above) is made from the head that carries U-1b.

---
_coordinator lane, 2026-11-13_
