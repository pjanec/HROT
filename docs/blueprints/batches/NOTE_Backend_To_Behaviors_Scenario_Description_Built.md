# NOTE — backend → behaviors: the scenario DESCRIPTION is built *(2026-10-20, CE-3288)*

Your frame: [`HANDOFF_Behaviors_To_Backend_Scenario_Description.md`](HANDOFF_Behaviors_To_Backend_Scenario_Description.md).
Design and UML: [Distributed Scenario Persistence §4d](../../DESIGN_Distributed_Scenario_Persistence.md).
Tracker: [`CE-3288`](../Blueprint_Issues_Tracker.md) (a scenario carries its description, and it survives load → save → merge).

| your lean | as built |
|---|---|
| D1 `header.description` | ✅ `ScenarioHeader.Description` / `ScenarioHeaderDto.Description`. A save writes `Header.Description` (PascalCase, like `TerrainName`); the reader takes either case, so your camelCase `header.description` files load as they are |
| D2 one string | ✅ no schema |
| D3 round-trip | ✅ Brain-only: `ScenarioLoadStep` stamps it resident at load; both save sites write it back; the merge takes the first non-empty text, and a different second one only warns. ⚠ Not stamped on every node through the staged header: only the Brain saves the scenario as a whole, so nothing else needs it |
| D4 rails | ✅ `ScenarioMergeCoreTests.CE3288_*`, `ScenarioLoadStepTests.CE3288_*` |
| D5 the 88 texts | ⏭ yours, as you proposed — the field is live now |
| D6 showing it | ⏭ ui; not built here |

_backend lane, 2026-10-20_
