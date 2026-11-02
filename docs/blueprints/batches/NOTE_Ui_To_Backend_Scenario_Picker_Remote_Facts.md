<!--STATUS
state: LIVE — ui → backend: the scenario picker's facts and preview on a host without the content folder (CE-1460)
updated: 2026-11-02
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../Architect_Question_108_Run_Mode_Picker.md — D25 the wide picker; §2.18 its as-built (CE-4141)
  - ../../DESIGN_Distributed_Scenario_Persistence.md — where scenario content lives (the NAS / master), §4d the header Description
-->
# NOTE — ui → backend: scenario facts and preview for the picker on every host (`CE-1460`)

The scenario picker now shows the selected scenario's `preview.png`, its header `Description` and a few facts
(terrain, subsystem, entity count). Built as CE-4141 (`8419b4a5f`, `6f6d2104f`; AQ108 §2.18).

⚠ **It reads them from the local content folder** (`ScenarioCatalogContributor` → `{scenariosRoot}/{name}/scenario.json`).
A host without that folder — a remote editor, CGF on another machine, ExCon — receives only NAMES
(`AssetInventoryTopic.LocalScenariosJson`, a `string[]`), so it shows the name and *"preview and description not
available on this host"*.

## Ask (lean, yours to design)

| | lean |
|---|---|
| ① the facts | the master's inventory carries, per scenario: `description`, `terrain`, `subsystem`, `entityCount`, `hasPreview` (+ a content hash so a client can cache). Extending `AssetInventoryTopic` with a JSON array beside `LocalScenariosJson` is the smallest change; it is published every 5 s, so keep the per-scenario record small (no image bytes) |
| ② the image | fetched on demand by name, NOT in the inventory: a request/reply (or the existing file-fetch / `PrefetchFiles` path, if it can serve one file to a non-node) returning the PNG bytes; the picker's image cache (`PickerImageCache`, keyed by string) takes a host loader, so a `"remote:<name>"` key can be served by it with no picker change |
| ③ who writes `preview.png` | AQ108 D25's lean: *File ▸ Set preview from the current view* (ui) and the director's stills — not needed for this ask |

ui will consume ① and ② through the same `ScenarioPaneFacts` / image-cache seams; nothing in the picker changes shape.

_ui lane, 2026-11-02_
