<!--STATUS
state: LIVE — ui → coordinator: the scenario-authoring handoff, "Now" items built
updated: 2026-11-02
current-answer: the whole note
stale-below: nothing
related-designs:
  - HANDOFF_Coordinator_To_Ui_Scenario_Authoring.md — the handoff this reports on (frozen at d358a8728)
  - ../DESIGN_Map_Picking_Unification.md — §6 CE-4206 (design, §6.6–§6.8 as built)
  - ../../DESIGN_Entity_Hierarchy_Views.md — §4 CE-4213 as built
  - ../../DESIGN_Node_Features.md — §9 CE-4148 as built
  - ../../DESIGN_Add_Entity_Picker.md — §2g UML, §2h CE-4142 as built
  - ../Architect_Question_108_Run_Mode_Picker.md — §2.18 CE-4168 / CE-4141 as built
-->
# NOTE — ui → coordinator: scenario authoring, the "Now" items ([handoff](HANDOFF_Coordinator_To_Ui_Scenario_Authoring.md))

Merged `coordinator` into `ui` first (clean). Worked through ONE standing Sonnet worker (main checkout, one build at a time), Opus designing,
reviewing every diff and checking each UI live in the editor under xvfb.

| # | row | state | as built |
|---|---|---|---|
| ① | **CE-4206** reference type forms | ✅ BUILT (R1–R3), F1–F9 approved by the user | Map Picking §6 + §6.6–§6.8: `ReferenceLeafDrawers` (one leaf per type), `LiveRefScope : IRefScope`, Exact\|Key / Coordinates\|Entity / Circle\|Rectangle\|Area entity, `AreaShapeGizmo`, Bearing dial, Show on map, `RefKey` edit with uniqueness, schema `area`/`bearing`/`rotation`; the search box path (`ISpatialPickerContext`, `MapPickableBoundingBoxAttribute`) deleted — the one leaf. ⏳ macro Key-on-pick (S3a) |
| ② | **CE-4168** + **CE-4141** | ✅ BUILT, checked live | P1–P5 on the one picker; the scenario picker is its first source (wide, preview, description, facts, Open / Open and go live → `RunModeChoice.OpenAndGoLive` through the one door). ⏳ D24 run setup (S3); `macros/` listing (S3a resolver); remote facts → **CE-1460** (backend, [note](NOTE_Ui_To_Backend_Scenario_Picker_Remote_Facts.md)) |
| ③ | **CE-4213** Structure view | ✅ BUILT, checked live | `IHierarchyAdapter` adopted (span enumerator, Includes/CanReparent/Reparent), Command + Structure adapters, the switch; parts named by their component kind (AQ79 §0.10). ⏳ Scenario view (S3a) |
| ④ | **CE-4148** · **CE-4149** | ✅ DONE | `HudFeatures` (six `hud.*`) on every host incl. the Replay Browser (own boot gate — Live features are not recorded); the Player composes `HostGatePolicy.Player` |
| + | **CE-4142** thumbnail gallery | ✅ BUILT, checked live | §2g UML (it had none) → GL1–GL4: groups + chips, `ThumbnailMap` precedence, shared `CategorySidebar`, Add Entity opens the gallery. ⏳ the PNGs (director stills), G8 smart symbols (CE-4169) |

**Reds found and closed (all STALE RAILS, each traced to the change that outdated it):** CE-1455 (R-1043's `LastLoadMode`, `9e00ef397`) ·
the Orchestrator Start-menu rail (CE-4140, `bf3e8b629`) · CE-1456 (bisected to CE-4127, `ed569a146`).

**Ids allocated:** CE-1460. **Waiting on other lanes:** ⑤–⑧ and the Scenario view need backend's macros S3a; D24's run setup needs S3.

_ui lane, 2026-11-02_
