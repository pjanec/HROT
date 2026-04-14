# Technical Debt Tracker: hexag-2 (OrchestratorSubsystem Hexagonal Architecture)

| ID | Priority | Source | Description | Target Batch | Resolved |
|----|----------|--------|-------------|--------------|---------|
| HEXAG2-DEBT-001 | P2 | Design | `ClusterOpEgressTranslator` still consumes `ClusterOpIntent` wrapper instead of canonical typed intents. Must be rewritten in HEXAG2-S012. | BATCH-03 | |
| HEXAG2-DEBT-002 | P2 | Design | `GlobalContextClusterOpHandler` uses `_participant` directly for context loading. Needs `_networkFactory.Participant` accessor after HEXAG2-S008. | BATCH-03 | |
| HEXAG2-DEBT-003 | P3 | Design | `SimHostApp.OnLoad()` creates DDS participant directly. Needs factory pattern enforcement (tracked under HEXAG2-S012). | BATCH-03 | |
| HEXAG2-DEBT-004 | P3 | Design | `CgfApplication` constructor creates DDS participant and translators directly. Needs factory pattern enforcement (tracked under HEXAG2-S012). | BATCH-03 | |
| HEXAG2-DEBT-005 | P3 | BATCH-01 review | Pre-existing audit test `ExConSubsystem_HasNoDirectClusterMasterReference` hard-codes wrong relative path to ExConSubsystem.cs. Should use absolute or workspace-relative path. | BATCH-03 | |
