<!--STATUS
state: LIVE — ui → coordinator: ui's share of the R-3066 unification pause is built
updated: 2026-10-29
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../Architect_Question_108_Run_Mode_Picker.md — §2.10 (CE-1437), §2.11 (CE-1438), §2.11a (CE-4101), §2.12 (CE-1439), §2.13 (CE-1440), as built
  - NOTE_Ui_To_Coordinator_Unification_Audit.md — the audit these rows came from
-->
# NOTE — ui → coordinator: ui's unification share is built (R-3066)

| row | what | ui commit |
|---|---|---|
| R-4001 fold | the unsaved round has no timeout; *Checking…* + Cancel | `88ed709fe` |
| CE-1437 | the toolbar time control composed once per process, picked by rule; one armed-default table; SimHost's panel through `UserTransport` | `efe168d3b` |
| CE-1436 (ui half) | the time adapter and the editor session read the node's `ClusterView`, fold nothing themselves; CGF asks *Save and go live* | `88d377c38` |
| CE-1438 | one confirmation model + one draw helper; "a run is running" defined once | `f8b1ff65e` |
| CE-1439 | the Orchestrator hosts the shared scenario session; its Start screen offers Open / New / Recording; its panel's controls go through the one surfaces | `57f069942` |
| CE-1440 | HTTP's preview entry = the toolbar's dry-run intent (it used to land paused); one unsaved asker for UI and HTTP (#12 → CE-4101) | `c8306f432` |
| **CE-4101** | **one guarded door in and out** (`ScenarioDoor` in the session, UI asker confirms / HTTP asker refuses); no public raw open; every entry point incl. 3 HTTP routes the inventory lacked; the one Idle intent; New Exercise and the panel's Load into Live no longer lose edits; exit through the door on every operator host (documents first, scenario last); the menu registered once by the owner; back-to-Start has no action (recorded) | `5ed720237`, `71e8ac588` |

**Gates (merged tree `71e8ac588`, `--no-build` after building each test project):** Presentation 1127/0/59 · Orchestrator 373/0 · ClusterRunner
377/2 (the two pre-existing `EverySystem…` rails) · AiShared 2174/1/10 (pre-existing `CE1150_*`) · Editor 671/3/3 (three load-flaky HTTP rails, pass
alone) · Integration `DebugApi|TheRunChoiceIsOnEveryHostTests` 51/4 in the combined run — ⚠ the four CGF-constructing rails fail there on
`Failed to create participant` (DDS) and **all six CGF-constructing rails pass alone (re-run by me)**. Every new rail red-proved by a compiling mutation.

**Open after this:** CE-1441 (small leftovers — fixed as their files are touched) · R-3068 ② – ④ (the armed mode read from `ClusterView`, sent by
the operator load path) — waits on backend's CE-3341 field · the recorded-scenario hash for AAR → Preview's two buttons (backend, CE-3339 paused).
**Ids allocated:** none new (CE-1437 – CE-1441 were filed with the audit).

_ui lane, 2026-10-29_
