# Stride Mock — Technical Debt Tracker

| ID | Source | Description | Priority | Target Batch | Status |
|----|--------|-------------|----------|--------------|--------|
| DT-001 | BATCH-01 / SC_SM002_4 | `BootstrapNode` does not assert Kernel.Initialize() called exactly once; test only verifies "called at least once" | P3 | BATCH-09 | OPEN |
| DT-002 | BATCH-02 / SM-003 | No test verifying `DeadReckoningSyncSystem` is present in kernel exactly once (not double-registered). Code is correct by construction; violation structurally impossible. | P3 | BATCH-09 | OPEN |
| DT-003 | BATCH-02 / SC_SM004_6 | TASK-DETAILS.md SC_SM004_6 spec typo: says `EffectType.Explosion` for `WeaponFireNotification` but correct value is `EffectType.Tracer`. Code and test are correct. | P3 | BATCH-09 | OPEN |
| DT-004 | BATCH-03 / SC_SM007_4 | `ResolveAppNodeId` is private static in Program.cs; the STRIDEMOCK=>700 mapping is present in source but has no automated unit test. Verified by code review only. | P2 | BATCH-05 | OPEN |
| DT-005 | BATCH-03 / SC_SM006_3a | `DemoTkbSetup.RegisterAll` spec call omitted because `HrotNodeBuilder` pre-registers TkbType 100 via `NedTkbCatalog.RegisterAll` inside `HrotEnvironment.CreateTkb()`. SM-008 developer must re-verify if the same pre-registration applies on the NedNetworkFactory path before calling DemoTkbSetup.RegisterAll. | P2 | BATCH-04 | OPEN |

Legend:
- P1 = Critical (never enters tracker; always becomes Corrective Task 0 in next batch)
- P2 = Should fix (tracked here, assigned target batch)
- P3 = Nice to have (tracked here, best-effort)
- Status: OPEN / RESOLVED (do not delete resolved rows)
