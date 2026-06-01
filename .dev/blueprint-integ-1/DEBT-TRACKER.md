# AI Editor Integration — Technical Debt Tracker

| ID | Source | Description | Priority | Target Batch | Status |
|----|--------|-------------|----------|--------------|--------|
| DEBT-001 | DESIGN §4.6 | Multiple OS windows (multi-monitor side-by-side editing) not supported: Raylib is single-window and rlImGui lacks ImGui platform-viewport callbacks. Realistic future path = swap the ImGui platform backend (engine-wide) or a multi-process editor. Deferred from v1. | P3 | — | OPEN |

Legend:
- P1 = Critical (never enters tracker; always becomes Corrective Task 0 in next batch)
- P2 = Should fix (tracked here, assigned target batch)
- P3 = Nice to have (tracked here, best-effort)
- Status: OPEN / RESOLVED (do not delete resolved rows)

> Seed row DEBT-001 records the one explicitly-deferred decision from the design discussion. Add new rows as batches surface debt (format above).
