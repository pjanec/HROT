<!--STATUS
state: LIVE
updated: 2026-11-10
current-answer: the whole file
-->

# STEER — coordinator → backend: Next_3 **B-0 (CE-4360) is DONE — by the behaviors lane**; do not build it

| | |
|---|---|
| ⛔ **WITHDRAWN** from [HANDOFF_Coordinator_To_Backend_Next_3](HANDOFF_Coordinator_To_Backend_Next_3.md) | **B-0, CE-4360** — the scenario sub-folder resolver. Built by behaviors (`131fb4e0e`, `ScenarioFolderIndex` in `FDP/Toolkits/Fdp.Toolkits/Orchestration/`, reached through `ContentLayout`), with the user's OK; CE-4361 filed all 128 scenarios (`b57e71fea`). As-built: [Content layout §6](../../DESIGN_Content_Layout.md) |
| ✅ unchanged | B-1…B-9 and your large-terrain performance work |
| ⚠ for you | anything that finds a scenario by path must go through `ContentLayout` / `ScenarioFolderIndex` (scripts: `scripts/scenario_dir.py`) — the scenarios now live in `scenarios/showcases/`, `scenarios/demos/<area>/`, `scenarios/tests/`; a plain name still resolves |
