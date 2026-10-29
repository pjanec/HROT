<!--STATUS
state: LIVE — backend → ui (and coordinator): the armed mode becomes ONE cluster-wide fact (R-3068, AQ108 D12a)
updated: 2026-10-29
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../Architect_Question_108_Run_Mode_Picker.md — D12a (the amendment), D12 (amended), D22 (HTTP stays explicit)
  - ../../DESIGN_Cluster_View.md — the node's ClusterView, where every host reads the armed mode
  - ../RULINGS.md — R-3068
-->
# NOTE — backend → ui: the armed mode is synced across the cluster (R-3068)

🔒 User, `2026-10-29`: *"Is the armed mode intended to be synced across the cluster? It should be."* · *"if the host who triggers the loading then
sends also the armed mode selection requirement"* · *"the choice what kind of armed mode will be sent from each host together with the scenario load
should be just some bootstrap parameterization of a shared code."*

| | who | what |
|---|---|---|
| ① | backend (CE-3341, queued after the content move) | `SetRunSettings` gains `armed` (wire value 24 unchanged; an old sender means "no change"); the master holds it, never resets it on a load, broadcasts it with the cluster state; `ClusterView` exposes it; `GET /run-settings` shows it |
| ② | ui | `RunModeChoice` READS the armed mode from the node's `ClusterView` — it stops owning `Armed` (one owner: the master) |
| ③ | ui | the ONE shared operator load path sends `SetRunSettings{armed = RunArmedDefaults.For(hostKind)}` FIRST, then the unchanged load request (same topic, same writer ⇒ ordered). `RunHostKind` is the host's bootstrap parameter — no host-specific code |
| ④ | ui | the indicator menu and `POST /run-mode` change it through the same op, so every host's indicator follows |
| ⛔ | — | HTTP / agent loads send NO armed mode (D22). `ScenarioLoadSequence` is shared with the HTTP routes, so the send is NOT in it — it is in the operator UI's load path (its `beforeLoad` hook is NOT the place) |

Backend posts a note when ① lands; ② – ④ can be written against it now (the field name `armed`, values Preview | Live | AAR as `RunArmed`).

_backend lane, 2026-10-29_
