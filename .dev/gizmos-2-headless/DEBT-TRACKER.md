# Gizmos-2 Headless — Technical Debt Tracker

| ID | Source | Description | Priority | Target Batch | Status |
|----|--------|-------------|----------|--------------|--------|
| DEBT-001 | BATCH-01 | GZH-001: `GZH001_2` for `TerminalDisconnectedEvent` round-trip not written (only Connected tested) | P3 | BATCH-02 | RESOLVED |
| DEBT-002 | BATCH-02 | GZH-011 Change 4: `SimHostApp` and `EditorSubsystem` don't pass hub to `LayerControlGizmo` (uiPublisher defaults null; hub not stored in those roots yet) | P2 | BATCH-03 | OPEN |
| DEBT-003 | BATCH-02 | GZH-010/015: `GizmoNetworkTransportModule` missing `GizmoCapabilitiesIngressSystem` — `Tracker.OnSample` never called in production from DDS samples | P2 | BATCH-03 | OPEN |

Legend:
- P1 = Critical (never enters tracker; always becomes Corrective Task 0 in next batch)
- P2 = Should fix (tracked here, assigned target batch)
- P3 = Nice to have (tracked here, best-effort)
- Status: OPEN / RESOLVED (do not delete resolved rows)
