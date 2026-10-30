<!--STATUS
state: LIVE — ui → coordinator: the run-modes batch and the editor-fixes batch are built
updated: 2026-10-29
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../Architect_Question_108_Run_Mode_Picker.md — §2.6 (CE-4096), §2.7 (CE-4098), §2.8 / §2.8a (CE-4097), §2.9 (CE-4083), as built
  - ../PLAN_Run_Modes_And_Content_Build.md — §2 the done-whens
-->
# NOTE — ui → coordinator: run modes built (CE-4098, CE-4096, CE-4097, CE-4083)

| task | ui commit | done-when | deviations / gaps (all in AQ108) |
|---|---|---|---|
| CE-4098 | `eb191dcc9` | ✅ accepted by you | — |
| CE-4096 | `14281300c` + `6d7b0cea3` (providers built once) | ✅ accepted by you | — |
| CE-4097 core | `15d2e0d5d` | every §3 row driven by a rail; ■ = `POST /sim/stop` (one rail) | §2.8 deviations table |
| CE-4097 part two | `9d045f5d0` | D22 explicit routes, the real unsaved round (one per move, menu or HTTP), DAR / AAR words, timeline menu, `/run-mode`, `/status.armedMode`, Save = checkpoint, status note, File fold, D5 branch return, CE-4091 buttons | §2.8a: no "ask to keep recordings" (no setting exists — whole-tree grep), checkpoint-here only at the live edge, the recorded-hash compare waits on backend ([NOTE](NOTE_Ui_To_Backend_Recorded_Scenario_Hash.md)) |
| CE-4083 | `4cb975e71` | no status-bar time control on any host; SimHost gained the toolbar one; a rail per host family | §2.9 |

⭐ **Found, filed, routed:** CE-1436 (→ backend) — two cluster ingress translators and two cluster read models; CGF / SimHost / IG cannot
read the unsaved round's answer ([frame](NOTE_Ui_To_Backend_One_Cluster_Ingress_And_Read_Model.md)). ⚠ My first branch-origin note claimed no
recording knows its parent from a one-folder grep — you caught it; corrected in the note and AQ108 §2.8.

**Gates (merged tree `4cb975e71`, `--no-build` after building each test project):** Presentation 1116/0/59 · Fdp.Presentation 655/0/1 ·
Editor 667/0/3 · SimHost 2047/0/6 · ClusterRunner 358/2 (both pre-existing: `EverySystemForgetsTheLastWorld`, `EverySystemStateFieldIsClassified`) ·
AiShared 2167/1/10 (pre-existing `CE1150_*`) · ClusterRunner.Integration `DebugApi` + `TheRunChoiceIsOnEveryHostTests` 48/0 (the whole suite does
not gate — killed after ~30 min, the known DDS finding). `tracker-counts.py --check` OK; AQ108's 15 Mermaid blocks parse.
**Ids allocated:** CE-1436.

_ui lane, 2026-10-29_
