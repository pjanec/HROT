# AI Editor Integration — Technical Debt Tracker

| ID | Source | Description | Priority | Target Batch | Status |
|----|--------|-------------|----------|--------------|--------|
| DEBT-001 | DESIGN §4.6 | Multiple OS windows (multi-monitor side-by-side editing) not supported: Raylib is single-window and rlImGui lacks ImGui platform-viewport callbacks. Realistic future path = swap the ImGui platform backend (engine-wide) or a multi-process editor. Deferred from v1. | P3 | — | OPEN |
| DEBT-002 | BATCH-01 | NodeEdit test baseline was **red** (mid-flight): `NodeEditor.UI.Tests` did not compile (`HitTester` missing Z-layer constants), `RegionLayoutComputerTests` used an old signature, 5 `IContainerNodeModel` stubs missing `RegionOrientation`. BATCH-01 repaired production (`HitTester` Z-order, `CanvasInput` BPF-029, `RegionLayoutComputer` overload) to satisfy pre-existing tests. Future NodeEdit work should be aware these are now load-bearing. | P3 | — | RESOLVED (baseline green) |
| DEBT-003 | BATCH-01 | NodeEdit `PickerRegistry.Get<TItem>` returned `null` (unfinished). Implemented in BATCH-06 (real adapter lookup + tests). | P2 | Phase 2 | RESOLVED |
| DEBT-004 | BATCH-01 | `SilkIconProvider` catalog-coverage test hardcodes the BTree/HSM icon-key list rather than deriving it from `BTreeNodeCatalog`/`HsmNodeCatalog`; could drift if catalogs add keys. | P3 | — | OPEN |
| DEBT-005 | BATCH-01 | `ImGuiClipboard` round-trip not verifiable headlessly; icon key→cell mapping in `SilkIconProvider` is best-effort semantic and needs a visual pass once the canvas renders. | P3 | Phase 2 | OPEN |
| DEBT-008 | BATCH-05 | `Hrot.ClusterRunner.Integration.Tests` full suite aborts with `InvalidOperationException: Call RegisterSystems before RegisterProviders` (`SimHostNodeBootstrapper.RegisterSpawningPipeline:294`). **Verified pre-existing** (identical at Batch-04 baseline with Batch-05 stashed) — SimHost bootstrap ordering, unrelated to AI editor. `EditorSubsystemBoot` subset passes 10/10. Blocks using the full integration suite as a green gate until SimHost fixes it. | P2 | — | OPEN |
| DEBT-007 | BATCH-04 | `EditorSubsystem.BlueprintWindowRegistrar` kept as a `=> null` compat shim (internal property) after retiring the registrar. Remove once no test/external caller references it. | P3 | — | OPEN |
| DEBT-006 | BATCH-02 | `Hrot.Blueprints.Tests` has **10 pre-existing failures** (mid-flight repo): 6 `Compiler.*EmitGoldenTests` (golden-source drift), 2 `Demos.*Snapshot`, 1 `Runtime.AllocationFreeTests`, 1 `Editor.ConditionSummaryAttachmentTests`. Unrelated to AI-editor integration. Gates the Phase 4 "green Blueprints suite" criterion — must be triaged/regenerated before Phase 4 sign-off. | P2 | Phase 4 | OPEN |

Legend:
- P1 = Critical (never enters tracker; always becomes Corrective Task 0 in next batch)
- P2 = Should fix (tracked here, assigned target batch)
- P3 = Nice to have (tracked here, best-effort)
- Status: OPEN / RESOLVED (do not delete resolved rows)

> Seed row DEBT-001 records the one explicitly-deferred decision from the design discussion. Add new rows as batches surface debt (format above).
