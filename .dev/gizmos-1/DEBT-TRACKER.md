# FDP Declarative Gizmo & Presentation Framework -- Technical Debt Tracker

| ID | Source | Description | Priority | Target Batch | Status |
|----|--------|-------------|----------|--------------|--------|
| D-001 | BATCH-01 | StringInternMap uses unsynchronized Dictionary. DrawTextLong is called by multiple ECS systems concurrently during parallel ECS iteration; will corrupt internal buckets or throw IndexOutOfRangeException. Must switch to ConcurrentDictionary or add a spinlock around Intern/TryResolve. | **P1** | BATCH-10 | RESOLVED |
| D-002 | BATCH-02 | EntityRepository in tests not disposed — minor test hygiene | P3 | backlog | OPEN |
| D-003 | BATCH-02 | Selection predicate not yet wired in game-host kernel registration; DataDrivenGizmoSystem/BehaviorGizmoManagerSystem will always draw all (null predicate) until wired | P2 | superseded by TASK-GZ031 | RESOLVED |
| D-004 | BATCH-05 | RichTextRenderer.ParseChunks allocates List<> per call — zero-allocation mandate folded into TASK-GZ014 constraints (use stackalloc or ReadOnlySpan, no List<> per draw call) | P3 | TASK-GZ014 | RESOLVED |
| D-005 | BATCH-05 | HandleInput in DebugGizmoLayer publishes GizmoInteractionStartedEvent but cannot push proxy tool (no canvas/tool-stack reference). Needs higher-level wiring to re-activate proxy tool. | P2 | superseded by TASK-GZ025 | RESOLVED |
| D-006 | BATCH-05 | RichTextRenderer uses Unsafe.As<FixedString32, byte> — brittle if FixedString32 layout changes; layout assertion mandate folded into TASK-GZ014 constraints (static constructor Debug.Assert on size) | P3 | TASK-GZ014 | RESOLVED |
| D-007 | BATCH-09 | SpatialHashGrid not exposed via public service interface; SpatialGridGizmo deferred until infrastructure change enables gizmo systems to read grid cells | P2 | backlog | OPEN |

| D-008 | BATCH-24 | Dead `_gizmoSystem` field in `SimHostVisualization` (and matching `gizmoSystem` param on `Initialize`). ExclusiveCaptureProxyTool was the only caller; now gone. Remove field + param. | P2 | BATCH-25 | OPEN |
| D-009 | BATCH-24 | Per-frame `new List<(Entity, int)>()` allocation in `DataDrivenGizmoSystem` step 1b (teardown scan). Pre-allocate as a field or allocate only when a violation is found. | P2 | TBD | OPEN |
| D-010 | BATCH-24 | `ConstructionOrder` path (step 2) in `DataDrivenGizmoSystem` does not grant exclusive focus, unlike late-activation (step 2b). If a gizmo rule that requires exclusive focus matches a newly constructed entity, the gizmo will not receive input. Harmonize the two paths. | P2 | TBD | OPEN |
| D-011 | BATCH-24 | Misleading comment in step 1b of `DataDrivenGizmoSystem.Execute`: "Injected (on-demand) gizmos have RuleIndex == -1; skip them." Injected gizmos live in `_injectedGizmos`, never in `_activeGizmos`, so the guard is unreachable dead code. Remove or correct the comment. | P3 | TBD | OPEN |
| D-012 | BATCH-24 | `using System.Linq;` added to `DataDrivenGizmoSystem` for `.Any()` in step 2b (closure alloc per event). Replace with a manual loop to keep LINQ out of ECS system files. | P3 | TBD | OPEN |

Legend:
- P1 = Critical (never enters tracker; always becomes Corrective Task 0 in next batch)
- P2 = Should fix (tracked here, assigned target batch)
- P3 = Nice to have (tracked here, best-effort)
- Status: OPEN / RESOLVED (do not delete resolved rows)
