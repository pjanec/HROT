# Debt Tracker — cgf-scn

All P2/P3 issues found during batch reviews are recorded here.
P1 issues are addressed immediately in a corrective task at the start of the next batch.

| ID | Source | Priority | Description | Target Batch | Status |
|----|--------|----------|-------------|--------------|--------|
| D-001 | BATCH-01 review | P3 | `CgfLogicPack` constructor accepts concrete `ScenarioEntityCreationRequestSource` instead of `IEntityCreationRequestSource`; unnecessarily tight coupling | BATCH-03+ | Open |
| D-002 | BATCH-01 review | P2 | 3 stale system-count assertions in `Hrot.SimHost.Tests` | BATCH-02 | ✅ Resolved |
| D-003 | BATCH-02 review | P3 | `BehaviorParamRemapperCompiler` silently skips read-only `[RemapNetworkId]` properties; should warn at compile time | BATCH-04+ | Open |

---

## Legend
- **P1** — Critical; fixed immediately in next batch corrective task (never enters this table)
- **P2** — Important; should be addressed within 1-2 batches
- **P3** — Minor; address when convenient
- ✅ — Resolved
