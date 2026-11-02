<!--STATUS
state: LIVE — coordinator → ui: scenario authoring surfaces (references, pickers, trees, macro looks)
updated: 2026-11-02
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Scenario_Macros.md — §14.4 SR-189 (Preview), SR-191 (transient look), SR-192 (member lines); SR-178 marker
  - ../../DESIGN_Entity_Hierarchy_Views.md — HV-1 … HV-5 (the switchable tree)
  - ../DESIGN_Map_Picking_Unification.md — CE-4206 extends it (the reference type forms)
  - HANDOFF_Coordinator_To_Backend_Scenario_Macros_S3a.md · HANDOFF_Coordinator_To_Behaviors_Objectives_Triggers.md — the parallel lanes; items marked ⏳ wait for them
-->
# HANDOFF — coordinator → ui: **scenario authoring surfaces**

**Dispatched at** `d358a8728` (`coordinator`) · ⛔ scope FROZEN at this commit (R-106).
🔒 **User, `2026-11-02`:** *"the ui stuff like the scenario picker or 'add entity' picker by the ui lane."*

## Now — no dependency
| # | row | what |
|---|---|---|
| ① | **CE-4206** (DESIGN FIRST) | the editor authors the reference type forms (`EntityLink` / `PointLink` / `AreaLink`: Exact · Key · Param), S0 / S9′ as built |
| ② | **CE-4168** | extend the ONE generic picker for the scenario picker (+ a `macros/` root listing, SR-173 / §14.1 ①) |
| ③ | **CE-4213** — Structure view first | the switchable tree (HV-1…HV-5); Command unchanged, Structure needs nothing new |
| ④ | **CE-4148** · **CE-4149** | gameplay HUD gated as Node Features (`hud.*`, R-4115); the Player's gate policy — Node Features S1 is built (`[NodeFeature]`, `NodeFeatures.On`) |

## ⏳ After backend's macros S3a
| # | row | what |
|---|---|---|
| ⑤ | **CE-4210** | bind-to-parameter on every field (SR-159…SR-166), *Open macro*, *Save selection as macro* |
| ⑥ | **CE-4213** Scenario view | entities under their macro instance (`MacroOrigin`) |
| ⑦ | SR-178 / SR-191 / SR-192 | the macro marker, transient entities at reduced opacity (a shape-condition bit), lines from a selected instance to its members (a `MacroMembersGizmo`, the SquadGizmo pattern) |
| ⑧ | SR-104 Z1 · SR-189 | *Add Entity ▸ Macro* (places an instance); the *Preview* button on an `InsertMacro` action (⏳ also after behaviors' S2) |

## Gates *(Rule 8)*
Touched classes; frame rails under `xvfb-run -a` for anything drawn; proof proportionate to risk (visual = one key rail).

_coordinator lane, 2026-11-02_
