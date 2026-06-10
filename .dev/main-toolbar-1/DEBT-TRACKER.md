# DEBT-TRACKER — main-toolbar-1

Technical debt discovered during the Main Toolbar / Asset Browser / Unified Creation project.
Every debt is recorded here when found, assigned to a target batch/phase, and **must be
resolved (status ✅) before the project is declared done** (FINAL-REPORT gate).

**Key:** P0 = blocks correctness/gate · P1 = high · P2 = medium · P3 = low/nice-to-have

| ID | Pri | Found in | Description | Target | Status |
|----|-----|----------|-------------|--------|--------|
| DEC-1 | — | BATCH-01 | **Decision:** `AssetRoots` placed in `Hrot.Editor.AiShared` (not `Hrot.AI.Behaviors`) because the API is keyed by `AssetKind`, which is an editor type the game-side Behaviors assembly must not depend on. §13 "shared editor infra" option. | — | ✅ |
| DEC-2 | — | BATCH-01 | **Decision:** `AssetKind.Scenario` deferred to MTB-P5-T2 (adding it now ripples 127 usages/switches). T1 exposes scenario recipe root via dedicated `ScenariosRecipesRoot`; P5-T2 folds Scenario into `RecipesFor`. | MTB-P5-T2 | open |
| DEC-3 | — | setup | **Decision (SUPERSEDED by DEC-4):** Coder role fulfilled by Agent-tool sub-agent (general-purpose, sonnet). | — | ✅ |
| DEC-4 | — | setup | **Decision:** Coder role fulfilled by the **claude-worker-orchestrator MCP** worker (`start_worker`, model `pro` = deepseek-v4-pro), per user directive. Agent-tool sonnet/opus subagents are NOT used for delegation. | — | ✅ |
