<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-07
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - ../../UX/DESIGN_Scenario_Lifecycle_Review.md — THE design (LC-1…LC-21, approved R-4166)
  - PROTOCOL_Two_Lanes_Self_Coordinating.md — the protocol, three lanes (ui, backend, behaviors)
-->

# HANDOFF — coordinator → ui, backend, behaviors: the scenario lifecycle fixes

**For:** all three lanes, AFTER the handoff each is on now. **From:** coordinator. **Dispatched at `f523103e`**.
⭐ Scope FROZEN there. ⛔⛔ Anything needing the user is skipped (R-4156).

🔒 User, `2026-11-07`: *"revise the whole process of creation a scenario … focus on the accessibility of user interface, on the practical stuff"*
→ the walkthrough [DESIGN_Scenario_Lifecycle_Review.md](../../UX/DESIGN_Scenario_Lifecycle_Review.md) → *"Otherwise, all approved."* (R-4166;
Q9 revised: window layouts stay per node and perspective — NOT per seat).

⭐ **Claiming:** the lane column is a SUGGESTION. Write *"L-n: started (lane)"* in your status file and push it BEFORE coding; ⛔ never start an
item another lane's status file claims. A lane that runs out of its own items takes the next unclaimed one.

## The items

| # | id | what | finding | suggested lane | ⏳ needs |
|---|---|---|---|---|---|
| **L-1** | CE-4315 | Preview's status note: *"Preview — changes are thrown away on Stop"*; Save in Preview refused with the same reason (`RunNoteStatusBarSection.cs:23`, `RunModeChoice.cs:370`) | LC-8 | ui | — |
| **L-2** | CE-4320 | the **RulesBundle** opens Mission summary when Live starts and when the exercise ends, on EVERY host that composes it — move CE-4240 (`EditorSubsystem.OpenSummaryWhenTheExerciseEnds`, `:314`, `:2638`) into the bundle; the Player opens its Trainee debrief at the end (Player HUD PH-6 / PH-7) | LC-15, LC-18 | ui | — |
| **L-3** | CE-4322 | *End exercise* ALWAYS works for an Instructor / Author: fires `instructor-end` when the scenario has it, else ends with the result *"Ended by the instructor"* (`InstructorEnd.cs:16, :39`); ■ in Live asks once *"End the exercise? / Keep running"* | LC-17 | behaviors (rules) | — |
| **L-4** | CE-4312 | numbered unit names by type and side, unique in the scenario — pass the placement's name generator (`EntityAuthoring.cs:81` passes none) | LC-2 | behaviors | — |
| **L-5** | CE-4321 | a ⏪ **Review** button beside ▶ (the DAR choices of the mode menu); the Player's **"REVIEW — the exercise is paused"** banner for the whole DAR (its time bar is suppressed, `PlayerApplication.cs:1563`) | LC-16 | behaviors | — |
| **L-6** | CE-4318 | one **Problems** window + a count on the toolbar: dangling references, a rule whose subject is gone, a unit without side / brain, an objective with no units on its side; click = select the culprit; ⛔ never blocks Preview / Live | LC-13 | backend (the checks) · ui window by whoever claims it | — |
| **L-7** | CE-4316 | **Run to …** in Preview: as fast as the machine can until a time or an event (a phase begins, a rule fires), then pause | LC-10 | backend | — |
| **L-8** | CE-4324 | recordings default to a fixed data folder beside the scenarios, not TEMP (`OrchestrationConstants.cs:16-20`) | LC-21 | backend | — |
| **L-9** | CE-4323 | the Replay Browser's *File ▸ Open Exercise Recording…* = the editor's picker (`RecordingPickerLauncher`, one opener) | LC-19 | backend | — |
| **L-10** | CE-4317 | the Run setup (variant / params) on the mode menu, for Preview AND Live | LC-11 | ui | — |
| **L-11** | CE-4319 | ▶ in its mode's colour (Live green, Preview violet, DAR amber) | LC-14 | ui | — |
| **L-12** | CE-4311 | an empty scenario's map hint (*Right-click ▸ Add Entity*) until the first entity | LC-1 | ui | — |
| **L-13** | CE-4313 | ORBAT: *New group here* (an empty HQ of a chosen echelon), a *Commander* row on the Unit form, the drop says what it will do (*Attach to …* / *Board …*) | LC-5 | ui | — |
| **L-14** | CE-4314 | *Copy plan* / *Paste plan* as catalogue actions; onto a multi-selection each unit gets its own copy (new task ids) | LC-6 | ui (after P-7) | P-7 |
| — | (A5) | Duplicate / Copy / Paste carry the start state AND the mission plan (new task ids) — already A5 in ui's handoff; this fixes WHAT it copies | LC-3 | ui | — |
| — | (B-14) | the dirty dot in the title (U5) — supersedes AQ108 D20 | LC-12 | ui | — |
| — | (RX6) | *+ Add* in the Rules window shows the gallery first; the engine list under *More…* | LC-7 | behaviors (V-4) | — |

⛔ **Not here:** LC-20 bookmarks and notes — the coordinator designs E7 first (CE-4325).

## Gates

The CLAUDE.md gate contract. ⭐ Fidelity (R-4146): L-2, L-5, L-11, L-13 against the house style; render the real frame. ⭐ Row 8: L-3 and L-7
change the run on every node ⇒ the rules / run-mode suites and a cluster rail.

---
_coordinator lane, 2026-11-07_
