# Technical Debt Tracker: hexag-2 (OrchestratorSubsystem Hexagonal Architecture)

| ID | Priority | Source | Description | Target Batch | Resolved |
|----|----------|--------|-------------|--------------|---------|
| HEXAG2-DEBT-001 | P2 | Design | `ClusterOpEgressTranslator` still consumes `ClusterOpIntent` wrapper instead of canonical typed intents. Must be rewritten in HEXAG2-S012. | BATCH-03 | |
| HEXAG2-DEBT-002 | P2 | Design | `GlobalContextClusterOpHandler` uses `_participant` directly for context loading. Needs `_networkFactory.Participant` accessor after HEXAG2-S008. | BATCH-03 | |
| HEXAG2-DEBT-003 | P3 | Design | `SimHostApp.OnLoad()` creates DDS participant directly. Needs factory pattern enforcement (tracked under HEXAG2-S012). | BATCH-03 | |
| HEXAG2-DEBT-004 | P3 | Design | `CgfApplication` constructor creates DDS participant and translators directly. Needs factory pattern enforcement (tracked under HEXAG2-S012). | BATCH-03 | |
| HEXAG2-DEBT-005 | P3 | BATCH-01 review | Pre-existing audit test `ExConSubsystem_HasNoDirectClusterMasterReference` hard-codes wrong relative path to ExConSubsystem.cs. Should use absolute or workspace-relative path. | BATCH-03 | |
| HEXAG2-DEBT-006 | P3 | BATCH-02 review | Two `IOrchestrationTranslator` interfaces coexist: `Hrot.Common.Infrastructure.IOrchestrationTranslator` (slave, modular-2) and `Hrot.Core.Network.IOrchestrationTranslator` (master, new). Identical shape but separate namespaces. Should be unified or given distinct names to eliminate ambiguity in `NedNetworkFactory` where both namespaces are imported. | BATCH-04 | |
| HEXAG2-DEBT-007 | P2 | BATCH-02 review | `OrchestratorSubsystem.Initialize()` still calls `HrotEnvironment.CreateParticipant()` and constructs DDS readers/writers directly. Confirmed pre-condition for HEXAG2-S008; confirmed present before BATCH-03. | BATCH-04 | |
