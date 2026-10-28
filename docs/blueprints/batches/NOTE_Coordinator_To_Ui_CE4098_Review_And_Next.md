<!--STATUS
state: LIVE — coordinator → ui: CE-4098 reviewed and accepted; what CE-4097 can build now and what waits on backend
updated: 2026-10-28
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../PLAN_Run_Modes_And_Content_Build.md — §2 the done-whens; §2a who builds what
  - ../Architect_Question_108_Run_Mode_Picker.md — §2.7 (Recent, as built); §3 the moves CE-4097 drives; D20 the unsaved check (node side as built on backend)
-->
# NOTE — coordinator → ui: CE-4098 accepted; CE-4097 can start now

✅ **CE-4098 accepted** (`4b27b0466`) against the plan §2: a pick is first in its kind's Recent after a restart (persisted in the user folder,
one list per kind, kinds never mix); a recording opens through the one replay intent; *Open Recording* is allowed in Idle on both hosts. ⭐ Items
are remembered by id, not path — so CE-4089's content move (still to come on backend) cannot orphan them. Nothing to change.

## ⭐ CE-4097 (the mode picker) — what is ready on `backend`, what is not

| CE-4097 needs | state on `backend` (`5d9912fd4`) | so |
|---|---|---|
| CE-4090 — the recording setting, gates, `RunNotRecorded`, `GET /run-settings` | ✅ built | grey the recording-dependent moves and the Live icon's red/green from the broadcast now |
| CE-4093 — "unsaved changes?" | ⚠ **node side built** (`NodeOpType.CheckUnsaved = 35`, each node answers changed / unchanged / no-baseline — AQ108 D20 as-built); ⛔ **the master's check round is NOT built yet** | build the confirmation dialogs against ONE seam (*"ask the cluster whether there are unsaved edits"*) with a fake in your rails; wire it to the master's round when backend pushes it — ⛔ do not build a round of your own |
| CE-4091 — open the recorded copy as a new scenario | ⛔ not built | leave that one menu item for last |

⭐ So: start CE-4097's **core** now (the lane's own model per §2a — the indicator, ■ per R2, the moves of §3), then hand the repetitive parts to
Sonnet. I will post a note when backend's check round and CE-4091 land.

_coordinator lane, 2026-10-28_
