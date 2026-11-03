<!--STATUS
state: LIVE — ui → backend: saving while the scenario runs in Preview kills the editor (CE-4250)
updated: 2026-11-03
current-answer: the whole note
stale-below: nothing
related-designs:
  - NOTE_Ui_To_Backend_Scenario_Save_Null_Entity.md — the sibling case (CE-4224, Entity.Null), answered
  - ../../MANUAL_TEST_Scenario_Evaluation.md — row A4 (found while running it)
-->
# NOTE — ui → backend: **save during Preview → `SaveResolver: entity … not in the save map` → editor dies (CE-4250)**

Repro (live, `ui@1d423f71e`): open `showcase-bridgehead` for editing → *Preview* runs ~45 s → *Save* ⇒ `InvalidOperationException: [ScenarioSerializer] SaveResolver: entity Entity(142, v1) is not in the save map`, thrown out of **`TargetMemoryTranslator.Extract`** (`Hrot/Engine/Hrot.EcsNode/Serializers/`, yours: S8 B). A unit's target memory points at an entity the preview spawned, which the save map rightly does not hold; the exception is unhandled, so the process goes.

| side | ⭐ lean |
|---|---|
| **yours — the translator** | a runtime-only reference (target memory is a sensing cache, not authored intent) that names an entity outside the save map is **dropped from the save**, with one warning naming it — never a throw. ⚠ Same family as CE-4224; consider the same rule for every translator that saves an `Entity` field of RUNTIME state |
| **ui's — the editor's Save during Preview** | ⭐ ui will make *Save* while Preview runs save the AUTHORED scenario (the restore point), not the previewed world, or refuse with *"stop the preview to save"*. That is ui's half, but saving runtime caches is wrong in any case, so your guard is still needed |

_ui lane, 2026-11-03_
