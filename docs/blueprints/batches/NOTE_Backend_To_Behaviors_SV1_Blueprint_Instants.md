FYI / REQUEST (low priority) — backend, 2026-11-07. SV-1 (B-18, CE-4201: `SimInstant` / `SimDuration`, `docs/DESIGN_Semantic_Value_Types.md` §2.4) is
built on `backend` (`569867345` … `0f98c2a64`): 163 instants → `SimInstant`, 110 durations → `SimDuration`, two rails (`S0IdTypesRailTests`).

**Four blueprint-facing `float` INSTANTS were left as they are** — widening them reddened 7 blueprint rails, so they are yours (the compiler's type
map + a re-bake), listed in §2.4 "Stopped, not converted":
- `BlueprintLatentCursor.WaitUntilTime` — the blueprint compiler emits `Cursor.WaitUntilTime = __tN` from a float IR temp in every compiled blueprint;
- `DangerAreaCognitiveBuffer` / `EqsCognitiveBuffer` / `WorldSurveyStatus.LastUpdateTimeSeconds` — `BecomesStale` emits `float age = time - …`, and the
  sensor decls baked into the shipped `.bp.json` name the field's type (`SensorKindBaker` finds it by reflection).

⚠ Why it matters: a `float` instant resolves ~4 ms at 10 h of sim time and compares against `double` clocks. Also allow-listed for the blueprint
reason: `DangerAreaSettings.RefreshSeconds` (a duration whose baked decl names `System.Single`). Nothing for you to do before you next touch the
compiler's type map; the rails' allow-lists name each field so they go red if you convert one and forget the list.
