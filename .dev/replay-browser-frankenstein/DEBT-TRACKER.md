# Replay Browser Frankenstein — Technical Debt Tracker

| ID | Source | Description | Priority | Target Batch | Status |
|----|--------|-------------|----------|--------------|--------|
| D01 | BATCH-01 review | `SeekAll` / `SetNodeOffset` tests do not verify per-node offset displacement (no test checks that a node with non-zero offset lands on a different frame than the base node) | P3 | BATCH-03 | RESOLVED — test `RBF_P2T1_SeekAll_WithNodeOffset_NodeLandsOnDifferentState` added in BATCH-03. |
| D02 | BATCH-01 review | `SetNodeOffset` for an unknown NodeId silently succeeds (writes to backing dict, ignored in SeekAll); inconsistent with `SetLocalEntitiesProvider` which throws `ArgumentOutOfRangeException` | P3 | BATCH-03 | RESOLVED — guard added in `FederatedReplayManager.SetNodeOffset`; test `RBF_P2T1_SetNodeOffset_UnknownNodeId_Throws` added in BATCH-03. |
| D03 | BATCH-02 review | `RBF_P3T3_DeserializeWith_InlineArrayHandleResolves` silently omitted — developer neither wrote the test nor added a debt entry. Determine if `FdpAutoSerializer` handles inline-array `Entity` fields through the resolver; write the test if supported, document as "not supported" otherwise. | P3 | BATCH-03 | RESOLVED — `FdpAutoSerializer` explicitly throws `InvalidOperationException` at `Build()` for any snapshotable component type that has an `[InlineArray]` or fixed-buffer field with element type `Entity`. The constraint is intentional (documented in `FdpAutoSerializer` source). A test is not feasible without bypassing the registration guard; the behavior is already covered by the throw path. |
| D04 | BATCH-02 review | Developer omitted Q1-Q5 insight answers from BATCH-02 report. Process note only — no code fix needed. | P3 | (process) | RESOLVED — Q1-Q5 answered fully in BATCH-03 and BATCH-04 reports. |
| D05 | BATCH-04 review | `OnLoadGroup` lambda in `ReplayBrowserSubsystem` only catches `LoadGroupException`. `File.ReadAllText` inside `FederatedReplayManager.LoadGroup` can throw `IOException` (missing `.meta.json`) or `UnauthorizedAccessException`; `MetadataSerializer.Deserialize` can throw `JsonException`. These propagate unhandled, crashing the render thread. Fix: also catch `IOException`, `UnauthorizedAccessException`, `JsonException` and return a user-readable rejection string. | P2 | BATCH-05 | OPEN |
| D06 | BATCH-04 review | `_searchPanel.CurrentFilePath` is always sourced from `_context.CurrentFdpPath` (the single-node context, never loaded in federated mode). Search panel shows a blank path in all federated scenarios. Fix: when a `_manager` is present, source `CurrentFilePath` from `_manager.Contexts[_manager.LocalEntitiesProviderNodeId].CurrentFdpPath`. | P3 | BATCH-05 | OPEN |
| D07 | BATCH-04 review | When `IsMergedViewActive` causes `DrawContent` to early-exit, any in-progress `_searchTask` continues to run unimpeded (consuming CPU) and its results are silently discarded; `_searchCts` is never cancelled by the mode switch. Fix: cancel the CTS at the top of the early-exit path (or in `SetViewMode`) when a task is still running. | P3 | BATCH-05 | OPEN |

Legend:
- P1 = Critical (never enters tracker; always becomes Corrective Task 0 in next batch)
- P2 = Should fix (tracked here, assigned target batch)
- P3 = Nice to have (tracked here, best-effort)
- Status: OPEN / RESOLVED (do not delete resolved rows)
