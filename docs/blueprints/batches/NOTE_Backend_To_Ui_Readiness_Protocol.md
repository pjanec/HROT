<!--STATUS
state: LIVE — backend → ui (and coordinator FYI): the readiness protocol is approved (R-3069–R-3072); ui's half is U1
updated: 2026-10-29
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../Architect_Question_110_Readiness_Protocol.md — the protocol decisions A–E and the slice ids
  - ../../DESIGN_Cluster_Load_Phase.md — §11 (cluster ready, play ready, cancel, the UML)
  - ../PLAN_Run_Modes_And_Content_Build.md — §6 the build order
-->
# NOTE — backend → ui: readiness — what backend builds, what ui's controls read (U1)

🔒 **User, `2026-10-29`:** *"The HTTP server or UI simply can't be enabled before the node has joined the cluster"* · a load *"must be cancellable by the user"* · *"I would like to be ready for different simular situation and prepare the protocol for that"* · *"approved, write it up and update the plan and present it"*.

⭐ This is what CE-4103 ① (an open in the editor's first second) really was: no host gated its controls on the cluster being ready.

| backend builds | ui reads it for |
|---|---|
| **CE-3344** `ClusterView.CanOperate` + `WaitingFor` (cluster ready: every mandatory name AND role reported ready, own node in sync, not Degraded); HTTP cluster routes answer 409 + `WaitingFor` | greyed run controls / File › Open / the scenario door with *"waiting for Brain"* etc. — ⭐ via the per-host-kind gate policy, never a per-host check |
| **CE-3345** the local phase + cluster play-ready + load progress (step names); `ClusterSlave.CancelLoad(reason)` | the **loading screen** (progress + **Cancel** → `CancelLoad`; the load then fails everywhere and the cluster returns to Idle) · the **lobby screen** of a Player joining during a load |
| already there: `ClusterView.HasInventory` | the pickers: *"waiting for the cluster's list…"* instead of a false empty |

Order: CE-3344 first (ui can start the greyed controls on `CanOperate` as soon as it lands), CE-3345 next (loading screen, cancel, lobby). A NOTE follows each landing.

_backend lane, 2026-10-29_
