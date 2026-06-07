# Universal Breakpoints — Technical Debt Tracker

| ID | Source | Description | Priority | Target Batch | Status |
|----|--------|-------------|----------|--------------|--------|
| D-BP-01 | BATCH-48 | `CgfNoOpTimeController.IsPausedByDebugger` returns false even when manager is paused; review when P10T3 temporal banner is wired to CGF perspective | P2 | BATCH-50 | RESOLVED (CgfNoOpTimeController now holds IDataBreakpointManager? back-ref; IsPausedByDebugger returns _bpManager?.IsPaused ?? false; other-fixes-2) |
| D-BP-02 | BATCH-48 | CGF `_bpPreTickSnapshot` only mirrors `CgfComponentRegistry`; may miss HrotNodeBuilder-internal component registrations for non-cognitive predicates | P3 | Backlog | OPEN (deferred: no API on HrotNodeBuilder to enumerate its internal component registrations beyond CgfComponentRegistry; CgfComponentRegistry.RegisterAll is the only RegisterAll call on the world and captures all runtime components in practice) |
| D-BP-03 | BATCH-49 | `BTreeEditorHostServices.SetBreakpointManager` constructs `BTreeBreakpointGutterRenderer(asset: null!)` — any call to `_asset.FindNode()` or `CountManagerBreakpoints()` will NRE in production; add null guard in renderer | P2 | BATCH-50 | RESOLVED |
| D-BP-04 | BATCH-49 | `GraphEditorWindow.SetBreakpointManager` added but canvas right-click handler is still a stub; `BlueprintBreakpointMenuPopulator.PopulateNodeMenu` never reached via UI interaction | P3 | Backlog | DEFERRED (FIX3-002 confirmed deferral: canvas rendering not yet implemented; right-click handler cannot be wired without a rendered node to click on; TODO(D-BP-04) comment added in GraphEditorWindow.DrawUI; wired when canvas batch implements node hit-testing) |
| D-BP-05 | BATCH-49 | Integration tests 14-16 (HotReload) call `mgr.OnHotReloadBegin/Completed` directly, bypassing `_aiCoordinator.OnReloadBegin` event subscription; need a test that fires the event via coordinator to verify end-to-end wiring | P2 | BATCH-50 | RESOLVED |

Legend:
- P1 = Critical (never enters tracker; always becomes Corrective Task 0 in next batch)
- P2 = Should fix (tracked here, assigned target batch)
- P3 = Nice to have (tracked here, best-effort)
- Status: OPEN / RESOLVED (do not delete resolved rows)
