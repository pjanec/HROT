<!--STATUS
state: LIVE — ui → backend: R-1043 (user) — two master-side facts for "Load always loads into the current armed mode"
updated: 2026-10-29
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../Architect_Question_108_Run_Mode_Picker.md — §2.15 (the R-1043 row), §2.14 (ui's CE-3341, decision ③ superseded)
  - ../RULINGS.md — R-1043 (amends R-3068)
  - NOTE_Backend_To_Ui_Armed_Mode_Wire_Built.md — your CE-3341 half these build on
-->
# NOTE — ui → backend: the master's INITIAL armed mode, and the mode of the LAST load (R-1043)

🔒 **User, `2026-10-29`:** *"Load always load into the current armed mode. It doesn't depend what host it is. The editor single node cluster armed mode is the
preview, which makes the difference from the non-editor cluster. … There doesn't need to be any file config for default armed mode. It should be derived
from the parameters of the bootstrap. The orchestrator in the single node editor simply gets the edit as nitial armed mode, which should be the only
difference in the bootstrap code."* · on AAR / DAR armed (ui's lean: remembered cluster-wide, not per host): *"Approved, add it to R-1043 and note backend"*.

## What ui needs from the master (two small facts)

| # | ask | lean |
|---|---|---|
| ① | **the INITIAL `ClusterMaster.ArmedMode` is a bootstrap parameter** (today a literal `Live`, `ClusterMaster.RunSettings.cs:44`) | a constructor / `OrchestratorCore` option, default **Live**; the editor's in-process orchestrator passes **Preview** (ui changes that one call in `EditorSubsystem`). ⛔ no file config |
| ② | **the mode the LAST scenario load went into** — `Live` (`LoadingLive`) or `Preview` (`LoadingEdit`) — held beside `ArmedMode`, set when a load round STARTS (or completes — your call, say which), broadcast on the cluster state like `ArmedMode` (`ClusterStateTopic` int, additive; `ClusterFactsIngress.StateOf`; `ClusterView.LastLoadMode`) | before any load it reads the **initial** armed mode (①). ⭐ It is what Load Scenario uses while AAR or DAR is armed — so every host resolves the same load |

## What ui does (CE-1443)

The one operator load path reads `ClusterView.ArmedMode`: Live ⇒ an explicit load into Live (paused at the start), Preview ⇒ an explicit load into edit,
AAR / DAR ⇒ `ClusterView.LastLoadMode`. ⛔ No host sends a default on open any more (ui withdraws its CE-3341 `OperatorOpening` → `HostDefault` and
`RunArmedDefaults`). Until ② lands, AAR / DAR armed falls back to Preview (edit), which is ① on the editor.

⚠ **Cross-lane:** ① touches your `ClusterMaster`; if you prefer, ui adds the parameter itself when it builds CE-1443 — say so here and it is ours.

_ui lane, 2026-10-29_
