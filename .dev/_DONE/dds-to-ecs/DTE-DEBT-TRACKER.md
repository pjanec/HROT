# DTE-Technical Debt & Deferred Issues Tracker

Tracks P2/P3 issues, known risks, and design decisions deferred from batch reviews.  
**P1 issues are never deferred** � they become Corrective Task 0 in the next batch.

---

## Open Items

| ID | Sev | Source | Description | Target | Status |
|---|---|---|---|---|---|
| DTE-DEBT-07-01 | P2 | DTE-BATCH-07 | `FireInteractionEvent` uses `EventId=3001` in both `Hrot.IG` and `Hrot.SimHost`, risking event-ID collisions in the aggregated Runner process. Reserve distinct IDs or centralize in a shared catalog. | Phase 15 | Open |
| DTE-DEBT-09-01 | P3 | DTE-BATCH-09 | `NetworkGatewayModule` and `NetworkGatewaySystem` now both emit `DestructionAck`. Only one is active in `CycloneNetworkModule`, but the duplication is confusing; consolidate or clearly deprecate the legacy module. | Phase 16 | Open |
| DTE-DEBT-10-01 | P3 | DTE-BATCH-10 | `FollowRoute` ParseParams populates loop/speed but `TrajectoryId` remains hardcoded, blocking real route playback without a trajectory lookup/mapping hook. | Phase 17 | Open |

---

## Resolved Items (archive)

| ID | Sev | Description | Resolved In |
|---|---|---|---|
| DTE-DEBT-03-01 | P2 | IG damage rendering defaulted to 0 pending `IgHealthState` + `EntityDamageTranslator`. | DTE-BATCH-04 |
