# Visual Asset Comparison — Technical Debt Tracker

| ID | Source | Description | Priority | Target Batch | Status |
|----|--------|-------------|----------|--------------|--------|
| D-01 | BATCH-01 review | `CollectCallText` counts `(` inside string literals; breaks for comments containing parentheses. Emitter never produces such comments but is fragile. | P2 | BATCH-03 | OPEN |
| D-02 | BATCH-01 review | Subtree+sync test uses `Contains` assertions only — does not verify ordering of injected lines relative to builder call. Upgrade to line-order assertions. | P2 | BATCH-02 | RESOLVED |
| D-05 | BATCH-02 developer | HSM sanitizer lacks a test for a state with both stableId comment AND a visualId transition on the same state -- verify neither injection is confused. | P3 | BATCH-08 | RESOLVED |
| D-06 | BATCH-02 developer | Blackboard sanitizer handles `AssetId:` header form but no test covers it (only `OwningAssetId:` is tested). | P3 | BATCH-08 | RESOLVED |
| D-07 | BATCH-02 developer | HSM test for 3-level nested Child calls not present -- brace-depth scan could misidentify opener in deeply nested cases. | P3 | BATCH-08 | RESOLVED |
| D-08 | BATCH-02 developer | FakeCatalog/FakeAsset now duplicated across BTree + HSM test classes (4 classes). Consolidate to shared test helper. Supersedes D-04 scope. | P3 | BATCH-03 | RESOLVED |
| D-03 | BATCH-01 developer | `BTreeComparisonSanitizer` does not strip block-bodied `[BTreeDefinition]` thunks (design §3.3 step 6). No-op in practice (emitter always emits expression-bodied). | P3 | BATCH-03 | OPEN |
| D-04 | BATCH-01 review | `FakeCatalog`/`FakeAsset` duplicated across 3 BTree test classes. Consolidate to shared test helper. | P3 | BATCH-03 | RESOLVED (via D-08) |

| D-09 | BATCH-04 review | `DiscoverFromFolder` may match `.Blackboard.cs` companion via `OwningAssetId` before main file; prefer `AssetId:` matches over `OwningAssetId:` matches when both are present. | P3 | BATCH-07 | RESOLVED |
| D-10 | BATCH-04 review | `ComparisonExportBuilder` has no disk-based fixture round-trip test (real sanitized output piped through builder and compared to golden file). | P3 | BATCH-07 | RESOLVED |
| D-11 | BATCH-04 review | `LlmResponseParser` empty changes array case not explicitly unit-tested (covered only indirectly by fixture suite). | P4 | Backlog | OPEN |

| D-12 | BATCH-05 review | `PasteResponseModalState.Apply` rejects 0-change+no-warning responses (valid "nothing changed" result). Should check for TruncationWarning text specifically. | P2 | BATCH-07 | RESOLVED |
| D-13 | BATCH-05 review | BTree/HSM comparison toolbar wrappers created but not integrated into actual canvas host windows (no DrawUI() call site yet). Wire when host windows are finalized. | P3 | BATCH-07 | OPEN |

| D-14 | BATCH-06 developer | `variable_renamed` badge scans node Title/Subtitle with `Contains()` -- short variable names (e.g., "HP") may produce false positives on unrelated nodes. Needs a typed property bag on `INodeModel` for exact-match. | P3 | Backlog | OPEN |
| D-15 | BATCH-06 developer | `connection_changed` ElementId format ("guidA->guidB") not validated by schema -- if LLM uses a different separator the split fails silently. Needs stricter parse with logging. | P3 | Backlog | OPEN |
| D-16 | BATCH-06 developer | `BlackboardAuthoringWindow` per-row decoration is shown in a separate section below the variables table instead of true inline per-row coloring (as per design ss.6.7). Requires `VariablesPanelControl` refactor to accept decoration callback. | P2 | BATCH-08 | RESOLVED |
| D-17 | BATCH-06 developer | `ComparisonAnnotationRenderer` has no explicit Dispose -- if session registry is swapped while renderer is active the asset GUID becomes stale. Add cleanup path. | P3 | Backlog | OPEN |
| D-18 | BATCH-06 developer | `NodeEditor.Core` is now a dependency of `Hrot.Editor.AiShared` (was previously graph-model-agnostic). Consider moving `ComparisonAnnotationRenderer` to a separate `Hrot.Editor.AiShared.NodeEditor` project to keep the base shared project lean. | P2 | Backlog | OPEN |

Legend:
- P1 = Critical (never enters tracker; always becomes Corrective Task 0 in next batch)
- P2 = Should fix (tracked here, assigned target batch)
- P3 = Nice to have (tracked here, best-effort)
- Status: OPEN / RESOLVED (do not delete resolved rows)
