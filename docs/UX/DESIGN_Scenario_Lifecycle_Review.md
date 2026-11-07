<!--STATUS
state: LIVE
build-state: DESIGN — a usability walkthrough; findings LC-1…LC-21 with leans, for the user
updated: 2026-11-07 (first version — the user: "revise the whole process of creation a scenario … focus on the accessibility of user interface, on the practical stuff")
current-answer: §2 the walkthrough (findings by step) · §3 the leans needing the user
stale-below: nothing
known-rot: none
known-conflict: AQ108 D20 ("no dirty indicator", on purpose) vs DESIGN_Edit_Mode_Undo U5 (a dirty flag) — LC-12 · R-4162 ("Save in Preview takes a checkpoint") vs a Preview is never recorded — LC-8
related-designs:
  - DESIGN_Editing_UX.md — the programme this walks through end to end
  - DESIGN_New_Scenario_And_Recipe.md · ../DESIGN_Add_Entity_Picker.md · DESIGN_Map_Interaction.md — steps 1–2
  - DESIGN_Mission_Plan_Editor.md · DESIGN_Rule_Editor_UX.md · DESIGN_Initial_State.md · DESIGN_Details_Forms.md — steps 3–5
  - DESIGN_Edit_Mode_Undo.md · DESIGN_Edit_Framework.md · DESIGN_Action_Catalogue.md — steps 6–8
  - ../DESIGN_Replay_Time_Axis_And_Review.md — OWNS DAR / AAR (steps 9–12)
  - DESIGN_Seats_And_Hats.md — the window layout per seat (LC-15)
  - DESIGN_Replay_Browser_UX.md — LC-19
-->

# DESIGN — the scenario lifecycle, walked end to end for usability

## 0. Why

🔒 *"revise the whole process of creation a scenario, meaning starting from the empty scenario, then placing some entities, defining their
mission plans, defining the rules and objectives, setting initial conditions … saving … previewing it, going back, changing something, another
preview, then saving the final one, switching to live and running this live, doing DAR, back to live, end live, do AAR … focus on the
accessibility of user interface, on the practical stuff, if what we designed is actually practical and easy and intuitive to use."* (user, `2026-11-07`)

## 1. INVENTORY — how this was measured

Three read-only sweeps (authoring · rules / save / preview · live / DAR / AAR) over the designs above and the code, by grep and
`scripts/cbm.sh search_code` (the index answered for one sweep and was stale for another — those findings are grep-only). The surprising
claims were re-checked by hand: no entity copy / duplicate in code; `EntityAuthoring.cs:81` passes no name generator; `InstructorEnd.cs:16, :39`
disables *End* without an `instructor-end` rule; `RunNoteStatusBarSection.cs:23` vs `RunModeChoice.cs:370`. ⚠ Nothing was run (the MCP was
down); window layouts on screen are NOT verified.

Status column: **built** · **queued** (designed and in a running handoff) · **designed** (not dispatched) · **none** (no design).

## 2. The walkthrough

### 2.1 Start an empty scenario — mostly good

| # | finding | status | ⭐ lean |
|---|---|---|---|
| LC-1 | *New ▸ Blank, on a terrain* asks the name first, writes the file, fits the map to the terrain (`EditorSubsystem.cs:4051`, `TerrainFramingLayer`). ⚠ Then an empty map and an empty ORBAT with no hint what to do next | built | an empty-scenario hint on the map: *"Right-click the map ▸ Add Entity, or press A"* — gone after the first entity |

### 2.2 Place the force

| # | finding | status | ⭐ lean |
|---|---|---|---|
| LC-2 | ⭐ **every placed unit gets its type's name** — the placement takes a name generator, production passes none (`EntityAuthoring.cs:81`) ⇒ ten "Rifle squad"s; the ORBAT filter, conditions and `@Key` references become ambiguous | none | **numbered names by type and side** (*Rifle squad 1, 2 …*), unique in the scenario; renaming stays the Unit form's Name row |
| LC-3 | ⭐ **no Copy / Paste / Duplicate of an entity** (grep: none); the only substitute is *Save selection as macro* | queued (A5 in the ui handoff — *Duplicate*) | Duplicate / Copy / Paste carry EVERYTHING authored — the start state AND the mission plan (with its task ids new) |
| LC-4 | side is chosen before placing (*Add Entity ▸ Friendly / Hostile / Neutral*) and changeable after — good. Repeat placement stays armed — good. ⚠ every unit faces north | built · heading drag queued (P3, behaviors) | — |
| LC-5 | ⭐ **the ORBAT cannot be built from loose units**: no *New group / HQ*, no commander field; groups come only from composite types. Dropping a unit on a NON-unit row silently means *embark* (`SharedOrbatPanel.cs:142-240`) | none (CAPABILITIES §14.2 ⏳) | ORBAT menu *New group here* (an empty HQ of a chosen echelon), a *Commander* row on the Unit form, and the drop says what it will do before release (*"Attach to 1st Platoon"* / *"Board Truck 2"*) |

### 2.3 Mission plans, rules, objectives, start state

| # | finding | status | ⭐ lean |
|---|---|---|---|
| LC-6 | ⭐ **a plan per unit, by hand** — no copy of a plan to other units, no apply-to-selection; only the group's commander plan (E6) | none | *Copy plan* / *Paste plan* as catalogue actions (onto a multi-selection: each gets its own copy); the Orders tab on a multi-selection shows *"3 units — same plan"* or *"different plans"* |
| LC-7 | creating an objective = *Rules ▸ + Add* → a flat menu of ENGINE names (Trigger, HoldArea, Count, HitRatio …) → a params form (`RuleAdd.cs:19`) | queued (RX5 / RX6 — behaviors: gallery, sentences, entry points) | the gallery is the first thing *+ Add* shows; the engine list moves under *More…* |
| — | start state rows (Aboard, Carries, Weapon, Knows about …) | built (U-23 / U-24) | — |

### 2.4 Save → Preview → back → change → Preview → save

| # | finding | status | ⭐ lean |
|---|---|---|---|
| LC-8 | ⭐ **Preview tells a lie**: the status bar says *"Running — edits are not saved unless you save"* (`RunNoteStatusBarSection.cs:23`) while Save is REFUSED in Preview (`RunModeChoice.cs:370`). And R-4162 says Save in Preview takes a checkpoint — but a Preview is never recorded, so there is nothing to checkpoint | built (wrong text) · ruling conflict | the note says **"Preview — changes are thrown away on Stop"**; Save in Preview stays refused with that reason; **amend R-4162: a checkpoint in Live only** |
| LC-9 | a fix made DURING Preview is lost on Stop, silently | none (AQ108 D11 allows edits) | keep the rule (Preview is throw-away) — LC-8's honest text is the fix; *"keep this change"* later only if asked for |
| LC-10 | ⭐ **testing minute 30 means watching minute 0–30 every iteration** — Preview always starts at the authored state, max 10× (`TransportIcons.cs:19`) | none | **"Run to …"** in Preview: as fast as the machine can until a time or an event (a phase begins, a rule fires), then pause |
| LC-11 | Preview takes no Run setup (variant / params) — only Live reuses the picker's | none | the Run setup lines on the mode indicator menu apply to Preview AND Live |
| LC-12 | ⭐ **no unsaved indicator, no autosave, and undo is never cleared** — `EditHistory` has no Clear (an undo after opening another scenario hits the wrong entities); AQ108 D20 rejected a dirty indicator, Undo U5 designs one; autosave D23 designed, CE-4099 open | undo clear + dirty flag queued (B-14) · autosave designed, not dispatched | **a dirty dot in the title** (U5 — supersede D20) and **autosave every 2 min with recovery** (D23) dispatched |
| LC-13 | ⭐ **no problems list** — a rule on a deleted unit, a unit with no brain or plan, an objective nobody can reach: found only when nothing happens in Preview (reason in the log) | none (old UXR-34 P0, AQ25 Q25-E never built) | **one Problems window** + a count on the toolbar: dangling references, rules whose subject is gone, units without side / brain, an objective on a side with no units; click = select the culprit. ⛔ never blocks Preview / Live |

### 2.5 Live → DAR → Live → End

| # | finding | status | ⭐ lean |
|---|---|---|---|
| LC-14 | going live = arm *Live* in the mode menu, then ▶; unsaved edits ⇒ *"Save and go live"* — good. ⚠ which mode ▶ starts is only in its tooltip | built | ▶ shows its mode in its own colour (green Live, violet Preview, amber DAR) |
| LC-15 | ⭐ **the instructor's windows start closed** — Rules, Mission summary, Game master, Game summary all `IsOpen = false` (`RulesBundle.cs:23-38`) | designed (Seats §2.4: "window layout + HUD preset" per seat) | entering Live opens the seat's layout: an Instructor / Author gets Mission summary + Game master + the Event Timeline |
| LC-16 | ⭐ **DAR is hidden and, for trainees, invisible**: entered only from the mode menu (*DAR — last 30 s / 2 min …*) or a timeline right-click; Player nodes replay it but show NO indicator (their time bar is suppressed, `PlayerApplication.cs:1563`) | none for the Player | a **⏪ Review** button beside ▶ (opens the same choices); Player HUD: a banner *"REVIEW — the exercise is paused"* for the whole DAR |
| LC-17 | ⭐ **two different "ends"**: *End exercise* is DISABLED unless the scenario has a rule keyed `instructor-end` (`InstructorEnd.cs:16`); ■ stops at once, with no result and no question | built (both) | *End exercise* ALWAYS works for an Instructor / Author: fires `instructor-end` when present, else ends with the result *"Ended by the instructor"*; ■ in Live asks *"End the exercise? / Keep running"* (one question, not two) |
| LC-18 | the debrief: Trainee debrief / Game summary appear only on Editor, CGF, Replay Browser — **not on the Player**; nothing opens them at the end | designed (Player HUD PH-6 / PH-7) | at the end: the instructor's layout opens Game summary; the Player opens its Trainee debrief |

### 2.6 AAR

| # | finding | status | ⭐ lean |
|---|---|---|---|
| LC-19 | in the editor, AAR is reachable five ways (the 8-s toast, the mode menu, ▶ on AAR, *File ▸ Open Exercise Recording…* with scenario / time / length rows) — good. ⛔ **the Replay Browser has no list**: only *Load .fdp…* over GUID-named per-node folders (`ReplayTimelinePanel.cs:618`) | none | the Replay Browser gets the SAME *Open Exercise Recording…* picker (`RecordingPickerLauncher`, one opener) |
| LC-20 | ⭐ **no bookmarks or notes during Live** (TE-4 not built) — the instructor's "remember this for the AAR" has nowhere to go | none (E7, not designed) | design E7: *Bookmark* (one key, a name optional) and *Note* on the timeline in Live, listed in DAR and AAR |
| LC-21 | recordings default to a TEMP folder (`$TMP/FDP_Temp`, `C:\FDP_Temp`, `OrchestrationConstants.cs:16-20`) | built | a fixed data folder by default (beside the scenarios); temp only when asked |

## 3. For the user — the decisions

| # | question | ⭐ lean |
|---|---|---|
| **Q1** | numbered names (LC-2) | yes, *type + number*, unique per scenario |
| **Q2** | Duplicate carries the plan and the start state (LC-3) | yes, everything authored |
| **Q3** | ORBAT building (LC-5) | *New group here* + a Commander row + the drop says what it does |
| **Q4** | Copy / Paste plan (LC-6) | yes, as catalogue actions on a multi-selection |
| **Q5** | Preview text + amend R-4162 (LC-8) | yes — a checkpoint is Live only |
| **Q6** | *Run to …* (LC-10) | yes — the biggest time-saver of the loop |
| **Q7** | a dirty dot + autosave (LC-12) — supersedes AQ108 D20 | yes |
| **Q8** | the Problems window (LC-13) | yes, never blocking |
| **Q9** | Live opens the seat's windows; the end opens the debriefs (LC-15, LC-18) | yes |
| **Q10** | DAR: ⏪ Review button + the Player's banner (LC-16) | yes |
| **Q11** | *End exercise* always works; ■ asks once (LC-17) | yes |
| **Q12** | the Replay Browser's recording picker; recordings out of TEMP (LC-19, LC-21) | yes |
| **Q13** | bookmarks and notes (LC-20) — design E7 next | yes |

---
_coordinator lane, 2026-11-07_
