<!--STATUS
state: LIVE
build-state: DESIGN — step 1 (the INVENTORY, §1) written 2026-11-06; step 2 (prior art + the target UX, §2–§3) and step 3 (the gap map, §4) not written yet
updated: 2026-11-06 (first version: the direction, and the inventory of what a user can edit today — coordinator)
current-answer: §0 the direction (user rulings) · §1 the inventory (what exists, where it is edited, what is wrong with it)
stale-below: nothing
known-rot: none
known-conflict: AQ26's two-axis resolution lets a HOST supply the isApplicable rule; R-4119 narrows it — for an entity, the rule reads the entity's components (§0)
related-designs:
  - ../CAPABILITIES.md — §14 is the product-facing summary of §1 (what a user can change, per mode)
  - ../blueprints/DESIGN_Details_Panel_View_Switching.md — OWNS the Details panel machinery (DetailsViewRegistry, descriptors, AppliesTo, the offer set); this design owns WHICH tabs, what goes in them, and their UX
  - README.md — the August scenario-authoring UX programme (Path A authoring, Path B runtime intervention, "same panels serve both"); this design widens it to the whole feature set
  - UX_Golden_Path.md — OWNS the walkable acceptance path (A1–A12, B1–B5)
  - Architect_Question_26_Entity_Action_Model.md — OWNS the action registration model (isApplicable / isEnabled / execute)
  - ../blueprints/Architect_Question_108_Run_Mode_Picker.md — OWNS the run modes, the indicator and Run Setup
  - ../DESIGN_Remote_Edit_Outcome.md — OWNS how an edit reaches the owning node and how its outcome is reported
  - ../DESIGN_Entity_Brain_View.md — OWNS the Brain tab
  - ../DESIGN_Rules_View_UI.md — OWNS the rules window and the rule editor
  - ../DESIGN_Director_Timeline_Editor.md — OWNS the shared timeline
  - ../DESIGN_Add_Entity_Picker.md — OWNS placing entities (the gallery)
-->

# DESIGN — the editing UX: what a user edits, where, and in which mode

## 0. The direction *(user, `2026-11-06`)*

🔒 *"it's time to take a wider look on what the editing of scenarios and actions during the editing phase as well as during the running
phase should include … the editing features should be available in same way for offline scenario editing and during a real exercise
running, so this should be shared. Although not everything needs to be available in both modes … It needs to be user-friendly and
practical."*

| ruling | what it fixes |
|---|---|
| **R-4118** | the **Details panel** is the one home for the detail of anything selected — its tabs, not standalone panels; the existing tabs are themselves in scope for practicality |
| **R-4119** | editing is gated by the **entity's components**, never by the node: the editor and CGF (and any node with the UI) offer the same editing. Authority to APPLY is separate — the remote-edit path |
| R-4117 | (context) scenario and entity pictures ship in git |

The plan: **① inventory** (§1, this version) → **② prior art and the target UX** (§2–§3) → **③ gap map, decisions with leans, handoffs** (§4).
Three modes throughout: **Edit** (offline), **Live** (an exercise running), **Review** (DAR during a run, AAR on a recording, the Replay Browser).

## 1. INVENTORY *(2026-11-06)*

Five read-only sweeps (Details tabs; units and force structure; AI, missions and orders; scenario and world; run, live and review), each
over `docs/` first, then code. Graph queries run (`scripts/cbm.sh search_graph`, label Class): `.*Details.*(Tab|Section|Provider|Contributor|Registry|Host).*`
(9) and Interface (3); `.*Window$` (82); `.*Panel$` (46); `.*Inspector$` (4); `.*(Order|Mission|Behaviour|Behavior|Brain|Utility|Eqs|Blackboard).*(Panel|Window|Tab|Editor|Section|Tool|Inspector).*` (36);
`.*(Route|Area|Zone|Waypoint|PhaseLine|Objective).*(Tool|Gizmo|Editor|Panel|Window|View).*` (22); `.*(Weather|Environment|ScenarioClock|TimeOfDay|Traffic|Sumo|Crowd|Minefield|Obstacle|AirDefen|IndirectFire|Sound).*` (176);
`.*(ForceRelation|Relations|Alliance|Hostility|SideTable|ForceTable|Diplomacy).*` (**0**); `.*(Loadout|Ammo|Crew|Seat|Skill|Injury|Posture|Kit|SpawnArea|Respawn).*(Panel|View|Editor|Window|Section|Drawer)$` (**0**);
`.*(Inject|RunMode|Undo|Replay|Director|Timescale|TimeControl).*` (152). ⚠ The graph index is STALE in places (it lists `ExCon*Window`,
`EditorOrbatPanel`, `EntityPropertyInspector` — files gone); every row below was confirmed by grep. Coverage could not be checked
(`check_index_coverage` is not in the CLI). The per-mode feature matrix is [CAPABILITIES §14](../CAPABILITIES.md); this section keeps what
the design needs.

### 1.1 The Details panel today

Every ECS host composes `EntityDetailsBundle` (`Hrot.Editor.AiShared/Windows/EntityDetailsBundle.cs:59-96`): Editor, CGF, IG, SimHost,
Replay Browser, Stride. It gives every host **Components, Summary, Objectives, Brain** (+ **Event** when the host passes `EventFocus`).
The editor then adds its own tabs (`EditorSubsystem.cs:3295-3370`); AI perspectives add theirs through `PerspectiveWorkspaceRegistrar`.

| tab | offered when | edits | registered by | findings |
|---|---|---|---|---|
| **Scenario** (10) | map background, nothing selected | start time, offset, global weather, scenario parameters | **editor only** | weather drawn by the raw inspector; reachable only by clicking empty map |
| **Zones** | same as Scenario | "load zone" | **editor only** | a second tab for the same selection |
| **Macro** (20) | `MacroInstance` | macro, arguments; read-only without authority | **editor only** | ✅ correct authority check — could live on every host |
| **Components** (30) | one entity **and the host has an inspector** | every raw field (double-click → a pop-up `Edit <Type> [<index>]`) | all hosts | the main editor for side, name, health, ammo, kits, spawn facing — raw engine names, indices, TKB numbers |
| **Objectives** (32) | the entity is a rule's subject | none (link to the rule) | all hosts | links only where the host passed `OpenRule` (Editor, RB) |
| **Brain** (34) | `BehaviorState`, or an embed applies | Settings; embeds: **Assign: task, SOP, ROE** (on the undo list), **Blueprints** | all hosts; **the embeds editor only**; a second Brain tab on the Blueprint perspective | very long; section titles in engine words; two differently-built Brain tabs |
| **Summary** (35) | one entity | read-only | all hosts | fine |
| **Playable** (36) | one entity (**any** entity, not only units) | writes `Playable` directly (`EntityPlayableView.cs:96-104`) | **editor only** | no run-state, replay or authority check; no undo; side offers Friend/Hostile only |
| **Mission** (40) | the **editor's mission service** offers behaviours | the mission plan | **editor only** | host service as the test, not `BehaviorState`; duplicates the standalone Mission window; trigger typed as text; "Force Commit (Overwrite)" shown to the user |
| **Event** (100) | a clicked timeline event | read-only | Editor, RB | missing on CGF, IG, SimHost |
| Node Properties · Blueprint Node · Variables · Blackboard · Graph Signature · HSM Events · Parameter Sync · Decision · Runtime | asset/graph selections in the AI perspectives | graph authoring | per perspective; HSM Events **editor only**; Runtime only where the binder is called (Editor, CGF) | Node Properties and Blueprint Node are parallel; Blackboard offered on every asset; one "Runtime" title for every kind |

### 1.2 Standalone windows that edit a selection

| window | what it edits | verdict for §3 |
|---|---|---|
| Entity Inspector (`FdpEntityInspectorWindow`), Entity Watch (`EntityWatchPanel`), `ComponentEditWindow` | components of the selection, live fields, one component's fields | duplicate the Components tab — keep a list/outliner only; edit inline |
| Mission (`MissionPanel`) | the selection's mission | a tab (duplicate on the editor; the only mission UI on CGF) |
| Waypoint Editor | the selected waypoint | a tab |
| Zone Editor | zone geometry / obstacles | a tab (overlaps Zones) |
| Map Configuration | map / scenario configuration | likely the Scenario tab |
| Rules, Game master | rules, injects | ⚖ stays a window (a list of many rules) — the selected rule's detail belongs in Details |
| ORBAT, Spawner/Add Entity, Director, Preview, Replay Browser windows, AI Watch, Breakpoints | outliner / tool / debugger | stay (not a selection's detail); Replay Browser's tools are spread over several windows |
| Component Diff, DER inspector | a replay entity | candidate Review-mode tabs |
| `UtilityDecisionWindow` | — | dead (constructed only in tests); the Decision tab replaced it |

### 1.3 Gated by node, not by the entity (against R-4119)

1. Playable, Macro, Mission, Scenario, Zones and the Brain embeds (Assign, Blueprints) are added **only by the editor** — a CGF or SimHost user selecting the same entity does not get them.
2. Event and Objectives' links exist only where the host passed an optional service (`EventFocus`, `OpenRule`).
3. Components depends on whether the host has an inspector, not on the entity.
4. Mission asks the editor's mission service, not the entity's `BehaviorState`.
5. HSM Events is editor-only (CGF builds the HSM registrar without it); Runtime panes only where the binder is called.
6. The mission and waypoint editors are tabs on the editor and windows on CGF.
7. The opposite fault: **Playable checks nothing** — offered for any entity, writes during Live and Review, no authority routing.

### 1.4 Cross-cutting findings

| # | finding | evidence |
|---|---|---|
| 1 | **raw component editing is the main editor** for side, name, health, ammunition, deploy kits, spawn-area side and facing, global and regional weather, door states | §1.1 Components row; Weather design E-3 |
| 2 | **little can be undone**: one `EditHistory` covers parameters, rules and the AI section; place, drag, rotate, delete, ORBAT, embark, Playable are not undoable (R-1047 defers entity undo, CE-1462) | `EditHistory.cs:5` |
| 3 | **Live changes are not confirmed** (UXR-72), Delete has no confirmation (UXI-16) | Golden Path B1 |
| 4 | **a change sent to another station is silent** except in the rules window — `EditOutcomeLog` exists, map drag / clock / Details / Delete do not read it | Remote_Edit_Outcome §4–§5 |
| 5 | **engine vocabulary in the UI**: "Assign: task, SOP, ROE", HoldFire / StayOnTask, raw behaviour names in the mission picker, trigger ids (`BehaviorFinished`), `Edit EntityInfo [123]` | AI and units sweeps |
| 6 | **useful actions only over HTTP/MCP**: heal, damage, resupply, possess-by-id, ownership transfer, `/trace/observe`; reserved menu ids Repair / Reinforce / Resupply / Transfer / Teleport have no handler | `GlobalActionIds.cs:28`; `DebugApiRouteDocs.cs` |
| 7 | **no matrix of which action is allowed in which mode** (weather in Live? spawn during DAR? edits in AAR?) | AQ108 §3 allows editing while running, nothing per action |
| 8 | **missing editors**: traffic (file only), scenario description and picture, terrain change, sides and relations, crowd-level settings, scenario checks, utility weights, EQS, phases/phase lines, bookmarks and notes on recordings, copy/paste, multi-edit, empty groups | CAPABILITIES §14 ⏳ rows |
| 9 | **Path B (the live instructor's path) was never walked** | `UX_Golden_Path.md:317-340` |
| 10 | **docs out of step**: Entity Commanding still NOT-BUILT though built; Zones READY-TO-BUILD though the tools exist | AI sweep |

## 2. Prior art *(step 2 — not written yet)*

## 3. The target UX *(step 2 — not written yet)*

## 4. Gap map, decisions, handoffs *(step 3 — not written yet)*
