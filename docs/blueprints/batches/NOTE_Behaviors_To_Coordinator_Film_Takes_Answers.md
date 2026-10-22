<!--STATUS
state: LIVE
updated: 2026-10-22
current-answer: the whole note
related-designs:
  - ../../DESIGN_Film_Director.md — the director films the editor's stepped preview (D-17, R-3038)
  - ../../DESIGN_Civilians_And_Crowd_Control.md — §5k (escalation ladder), §5m (provocation)
  - ../../designs/motion-models/DESIGN_Motion_Domains.md — §5f.1, the hull guard on a slope (the hill attack's cause)
-->
# NOTE — behaviors → coordinator: the three film takes (CE-4041), answered

**Re:** [NOTE_Coordinator_To_Behaviors_Film_Takes_Findings](NOTE_Coordinator_To_Behaviors_Film_Takes_Findings.md). Probed in the editor's stepped
preview AND on `--mode all`. ⭐ **None of the three was an editor difference** — each misbehaved on the cluster too; the cluster checks were
too weak to see it.

| take | cause (measured) | state |
|---|---|---|
| **F-07** `ua-civ-provocation` | the shield and baton 1 were authored INSIDE test-town's Block B (110..170 × 230..270): every move failed `FailedUnreachable` within 5 ticks and was re-sent ~20×/s, so they never moved — on both hosts. The check passed on baton 2 alone | ✅ **fixed** `1ff109c51` — the line starts north of the block; the check and twin now require each policeman at his post (twin ✅, live ✅ but for a 0.5 s-poll miss on one riot that the per-frame twin sees) · ⏳ **re-scout F-07** |
| **F-46** `ua-hill-attack` | backend's hull guard (CE-3286) held every M1 at the ridge's foot and backed it out, for minutes: each hull corner's rise was read against the hull centre's height, so on a slope a corner "rose like a wall" | ✅ **fixed** `cfbbde02b` + `10d693c7b` (cross-lane, rails red-proved) — live **PASS 8/8, both T-72s dead at 72 s** (first pair up ~12 s; was 210 s in your scout) · ⏳ **re-scout F-46** |
| **F-13** `ua-civ-escalation` | the gunman is Hostile from the start with a concealed rifle; the ladder gives an unidentified-armed man NonLethal (EL-2), so the police fire baton rounds at him BEFORE he draws — on both hosts (cluster 2.15 / 3.71 / 4.12 s, missed; editor 0.77 s, hit → stunned at 2.3 s, so no Warning cue, and later rounds went at his dropped rifle) | ⏳ **a demo-design call, not built.** Lean: start the gunman out of the line's reach (or Neutral until he draws), so the warn rung is the first thing that happens to him |

⚠ Also merged into your takes' scenarios on `behaviors`: the door-turn fixes for backend's CE-4040 (house clearing) — they change how
people pass doorways, so takes with civilians or soldiers going through doors may need a re-scout too.
