<!--STATUS
state: LIVE — coordinator → ui: CE-4101, one guarded door IN (open a scenario / recording) and one OUT (leave the open scenario to Idle) (part of the R-3066 unification pause); widened 2026-10-29 before ui started it
updated: 2026-10-29
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../Architect_Question_108_Run_Mode_Picker.md — §3 (every move and its confirmation), §2.11 (CE-1438 as built), D20 (the unsaved check before a step that loses edits), D22 (HTTP stays explicit)
  - ../Blueprint_Issues_Tracker.md — CE-4101 (this), CE-1437 (the toolbar composed once — the precedent), CE-1438, CE-1439
-->
# NOTE — coordinator → ui: one guarded door in and one out — opening a scenario or recording, and leaving it (CE-4101)

> ⭐ **WIDENED `2026-10-29`** (user: *"Yes"*, on *"are there other similar duplications left?"*) — the second door, §"The leave door" below.
> It absorbs your CE-1440's *"go to Idle four times"* item and my CE-4100 ② (back-to-Start / exit guard): ⭐ mark both rows "→ CE-4101".

🔒 **User, `2026-10-29`**, on CE-1438's *"Stop & Load on every host"*: *"this again looks like missing unification, isnt there still dupliction?"*
— ⭐ yes. ⛔ I accepted CE-1438 as "fixed"; that was premature — the DECISION is one, the DOOR is not.

## What is measured (`origin/ui`, grep — ⚠ the graph was not run for this pass, so the caller set may be incomplete: run `search_graph` first)

The decision is ONE: `RunModeRunGuard` ("a run is running") + `RunModeChoice` (Stop & Load, Save / Discard / Cancel). ⛔ But
`IScenarioSession.OpenForEdit` / `OpenRecording` / `LoadForLive` stay public and unguarded, so each entry point must REMEMBER the choice:

| entry point | guarded? | where |
|---|---|---|
| File › Open Scenario / Open Recording | ✅ but wired by hand three times, each with an unguarded `else` fallback | `EditorSubsystem.cs:4483,4528`, `CgfSubsystem.cs:2949,2951`, `OrchestratorSubsystem.cs:333` |
| the asset browser's scenario pick | ⛔ the session directly | `CgfSubsystem.cs:2529`, `EditorSubsystem.cs:3753`, `OrchestratorSubsystem.cs:316` |
| New from a recipe | ⛔ `ScenarioNewAssetService.cs:138` → `LoadScenarioByName` → `_session.OpenForEdit` | |
| another editor load | ⛔ `EditorSubsystem.cs:2490` | |
| the Orchestrator panel's *Load into Live* | ⛔ `ClusterPanelRunControls.cs:93` → `Session.LoadForLive` — no Stop & Load, ⚠ **no unsaved check** (a loss) | |
| HTTP loads | ✅ their own 409, reading the same `RunModeRunGuard.Of` | `DebugApiService.cs:1306` |

## The leave door — measured the same way (`origin/ui`, grep)

The "go to Idle" intent is built in ≥ 8 places; only `RunModeChoice` asks first:

| path | asks about unsaved edits? | where |
|---|---|---|
| New Exercise | ⛔ asks "stop?" over a run, then publishes Idle with **no unsaved check** — a loss | `EditorScenarioSession.cs:171` |
| the raw ■ | ⛔ publishes Idle directly | `ClusterTimeTransportAdapter.cs:185` |
| the director's live stop | ⛔ publishes Idle directly | `EditorDirector.cs:85` |
| a load's first half | ✅ the shared `ScenarioLoadSequence.cs:60` | |
| HTTP | its own builders | `DebugApiService.Master.cs:208,443`, `DebugApiService.Cluster.cs:99` |

⭐ **Precedents done RIGHT in this codebase** — copy their shape: `WindowManager.SwitchPerspective` is only a wrapper over the one
`TrySwitchPerspective` (no way around the Start refusal); `ISaveInterception` is set on the session by the run choice's constructor, so every save
path (CGF's direct `SaveCurrent` too) becomes a checkpoint in Live / AAR by construction.

## ⭐ The lean — the guard is part of the door, not a step callers remember

| # | what |
|---|---|
| 1 | ⭐⭐ **the session's open / load make the ONE decision** (a run ⇒ Stop & Load · unsaved edits ⇒ Save / Discard / Cancel · never-saved ⇒ Save As) and answer through an **injected asker**: UI hosts inject the one confirmation (`RunModeConfirmation`); HTTP injects *refuse* ⇒ its 409 (D22 unchanged). ⛔ No unguarded public open remains — a raw open, if the moves themselves need one, is internal to the run choice |
| 1b | ⭐⭐ **the LEAVE door**: the session gains ONE guarded *leave the open scenario* — New Exercise, back to `Start`, exit, ■ from edit, the director's stop — same decision (unsaved ⇒ Save / Discard / Cancel; a run ⇒ stop it), same injected asker; the Idle intent built ONCE (reuse `ScenarioLoadSequence`'s); HTTP's routes call it with *refuse* |
| 2 | ⭐ **the scenario menu is registered ONCE** in the shared composition — the CE-1437 precedent (toolbar) — so the three per-host `ScenarioMenuCommands.Register` lambdas and their fallbacks are deleted, not fixed |
| 3 | ⭐ **a rail that fails when production code reaches an unguarded open** (the constructed objects, not source text) + a rail per entry point above: over a live run each asks Stop & Load; over unsaved edits each asks Save first; HTTP answers 409 |

**Rejected:** route the ~6 missing callers through `RunModeChoice` one by one — the remember-to-wire pattern that produced this · enforce only
at the master — it can refuse but cannot show *Save / Discard / Cancel*.

⭐ **Who builds:** your own model for step 1 (where the decision moves and what the asker looks like — UML into AQ108 §2.11 first), then Sonnet
for the call-site moves and the rails — one Sonnet agent at a time (CLAUDE.md). **Done when:** every row of BOTH tables is guarded by construction, the
grep for an unguarded open — and for a `TransitionStateIntent { TargetState = Idle }` outside the one builder — in production returns nothing, and the AQ108 as-built is folded. Report with the gate contract.

_coordinator lane, 2026-10-29_
