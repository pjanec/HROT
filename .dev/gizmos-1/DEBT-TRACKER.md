# FDP Declarative Gizmo & Presentation Framework -- Technical Debt Tracker

| ID | Source | Description | Priority | Target Batch | Status |
|----|--------|-------------|----------|--------------|--------|
| D-001 | BATCH-01 | StringInternMap uses unsynchronized Dictionary; document single-writer requirement or add lock | P3 | BATCH-03 | OPEN |
| D-002 | BATCH-02 | EntityRepository in tests not disposed — minor test hygiene | P3 | backlog | OPEN |
| D-003 | BATCH-02 | Selection predicate not yet wired in game-host kernel registration; DataDrivenGizmoSystem/BehaviorGizmoManagerSystem will always draw all (null predicate) until wired | P2 | GZ015 | OPEN |
| D-004 | BATCH-05 | RichTextRenderer.ParseChunks allocates List<> per call — acceptable now (Raylib.DrawText also allocates), revisit if used in hot path outside rendering | P3 | backlog | OPEN |
| D-005 | BATCH-05 | HandleInput in DebugGizmoLayer publishes GizmoInteractionStartedEvent but cannot push proxy tool (no canvas/tool-stack reference). Needs higher-level wiring to re-activate proxy tool. | P2 | GZ020 | OPEN |
| D-006 | BATCH-05 | RichTextRenderer uses Unsafe.As<FixedString32, byte> — brittle if FixedString32 layout changes; add a layout assertion (StructLayout fixed-size check) | P3 | backlog | OPEN |

Legend:
- P1 = Critical (never enters tracker; always becomes Corrective Task 0 in next batch)
- P2 = Should fix (tracked here, assigned target batch)
- P3 = Nice to have (tracked here, best-effort)
- Status: OPEN / RESOLVED (do not delete resolved rows)
