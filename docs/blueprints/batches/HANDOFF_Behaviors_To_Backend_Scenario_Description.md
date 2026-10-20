<!--STATUS
state: LIVE — behaviors → backend: a FRAME (goal, fences, measured traps, leans); backend designs the details as step 1
updated: 2026-10-20
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/DESIGN_Distributed_Scenario_Persistence.md — §4 OWNS the one save (ScenarioSaveCore) and the slice merge this field must ride
  - docs/DESIGN_Terrain_Zones_And_Assets.md — §2.1e ① OWNS the header's TerrainName and the "a name, nothing else" fence this field must respect
-->

# HANDOFF — behaviors → backend: a scenario carries its own DESCRIPTION — what it shows and how to judge it

**Dispatched at:** the commit that adds this file on `behaviors` (2026-10-20). Scope frozen there; later documents are FYI only.

> 🔒 **User, 2026-10-20:** *"i think each scenario needs a string description what it is all about and how to judge if successfull, in a
> header (description field)"* · *"Description could be handed off to backend."*

## Goal

Every scenario file says, in its header, **what it is for and how to tell whether it worked** — readable by a person opening the file, the
editor, the demo-check script and the MCP — and **the text survives an editor load → save** (that is the hard part).

## ⚠ Measured traps (behaviors, 2026-10-20) — why a field in the JSON alone is not enough

| trap | where |
|---|---|
| ⛔ both save sites build a FRESH header: `new ScenarioHeader("Hrot.Scenario", TerrainName: TerrainDefinition.ResidentName(world))` — anything else in the loaded header is dropped | `HrotScenarioSaveHandler.cs:98` (every node's slice) · `ScenarioFileService.cs:104` (the editor shim) → `ScenarioSaveCore.Write` |
| ⛔ the slice merge REBUILDS the canonical header with `TerrainName` only | `ScenarioMergeCore.cs:160-162` |
| ✅ the precedent that works: the terrain name survives load → save because the loader stamps it RESIDENT (`TerrainDefinition.ResolvedName`, `TerrainResidency.cs:300`) and both save sites read it back | CE-3015 |
| ⚠ the description must not depend on the terrain: a scenario may have no terrain (`TerrainName` null) | `ScenarioHeaderDto.TerrainName` doc |
| ✅ today: 88 scenarios under `scenarios/`, **none** carries a description | `grep -l '"description"' scenarios/*/scenario.json` → 0 |

## Leans *(backend designs the details; a lean that turns out wrong is a finding, not a silent change)*

| # | question | ⭐ lean |
|---|---|---|
| D1 | where | `header.description` — one string beside `terrainName` (`ScenarioHeader` record + `ScenarioHeaderDto`). It is metadata, not an asset reference, so it stays inside the header's "a name and nothing else" fence for assets |
| D2 | shape | ONE string, two parts by convention: *"What: … · Judge: …"* — what the scenario shows, then what a viewer (or the check script) must see for a pass. No schema beyond that |
| D3 | round-trip | the loader stamps it RESIDENT on every node (as `ResolvedName`, but not tied to the terrain); both save sites write it back; the merge carries it (every slice holds the same text — take it, warn on a mismatch as for the terrain) |
| D4 | rails | load → save → merge keeps the text; a scenario without one still loads and saves |
| D5 | the 88 texts | the lane that authored a scenario writes its description — ⭐ behaviors will fill its own (most of `scenarios/`) once the field lands; a "every shipped scenario has one" rail comes after that, not before |
| D6 | showing it | ui later (the scenario browser / load dialog) — not in this handoff; the MCP / debug HTTP may echo it where it already reports the loaded scenario |

⭐ Behaviors already writes `header.description` into NEW scenarios (from 2026-10-20, starting with AQ96's demo) — the DTO ignores the unknown
field today, so the file is the record until D3 lands.

## Acceptance

- a scenario with a description loaded in the editor and saved (single node and a cluster save with its merge) keeps the text verbatim
- `ScenarioHeaderDto` exposes it; the design doc (Distributed Scenario Persistence §4) shows the field in its save sequence

---
_behaviors lane, 2026-10-20_
