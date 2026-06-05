# Persistence Unification (BTree/HSM to JSON) — Technical Debt Tracker

**Reference:** [`TASK-TRACKER.md`](./TASK-TRACKER.md) · [`TASK-DETAIL.md`](./TASK-DETAIL.md) · [`BTree_HSM_JSON_Persistence_Detailed_Design.md`](./BTree_HSM_JSON_Persistence_Detailed_Design.md)

> Debt discovered during a batch goes here (P2/P3). P1 never enters the tracker — it becomes Corrective Task 0 of the next batch. Do not delete RESOLVED rows.

| ID | Source | Description | Priority | Target Batch | Status |
|----|--------|-------------|----------|--------------|--------|
| PU-D01 | BATCH-01 | HSM `FromDto` lives in net8 `Hrot.Hsm.Editor` (uses `HsmAsset`'s internal ctor). The Phase-2 Roslyn generator (netstandard2.0) needs a public factory seam or an ns2.0 HSM builder to construct from the DTO. | P2 | PU-202 | OPEN |
| PU-D02 | BATCH-01 | HSM DTO persists `EventName`, not `EventId` (ids reassigned sequentially on `FromDto`). The JSON load path must match events by name, not id. | P2 | PU-301 | OPEN |
| PU-D03 | BATCH-01 | `HrotDocumentTypes.BTree`/`.Hsm` constants added but NOT registered with the migration system (no `RegisterDocType` passthrough) — intentional for zero-behavior-change; wire when the load/migration path lands. | P3 | PU-301 | OPEN |
| PU-D04 | BATCH-03 | PU-205 migration-equivalence asserts `generatorOutput == EmitTopologyCore(ToDto(model))` (faithful JSON→core routing), not a DIRECT compare to the committed `SampleScout.cs`/`SampleGuard.cs` topology core. Transitively covered (BATCH-02 full byte-identical gate + shared `EmitInternal`), but the direct "behavior unchanged vs today" compare should land at PU-401 when real generated `.cs` exists in `obj/` (strip the committed `.cs` `[*Layout]` method block; exact-string compare before decommit). | P2 | PU-401 | OPEN |
| PU-D05 | BATCH-03 | Two `*_EquivalenceTest_FailsLoudly_WhenDiverged` tests are vacuous (`reference + "// DIVERGED" != reference` is a tautology). Remove/replace with a real divergence-detection test when PU-D04 lands. | P3 | PU-401 | OPEN |
| PU-D06 | BATCH-04 | **Migration-equivalence criterion contradiction (escalated to user).** BATCH-02's "byte-identical gate" was tautological (adapter↔core + `.Contain()` on committed `.cs`), so the emit core was never proven byte-identical to the committed `SampleScout.cs`/`SampleGuard.cs` — and diverged (invalid `[BTreeDefinition]` AssetId; interleaved HSM order), fixed in BATCH-04. Committed `.cs` are hand-structured → exact text reproduction likely unachievable. Contradicts D1/§6.4/§11 ("regenerated `.cs` byte-identical to committed `.cs`"). **Recommended:** PU-401 equivalence = compile committed `.cs` AND regenerated `.cs`, compare resulting `BehaviorTreeBlob`/`HsmDefinitionBlob` (blob/behavioral equivalence). Subsumes PU-D04/PU-D05. | P1 (escalated) | PU-401 | OPEN |
| PU-D07 | BATCH-04 | `InternalsVisibleTo` added to `FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj` for `Hrot.AiEditor.Generators.Tests` (integration test drives registration internals). Acceptable; flagged. | P3 | — | OPEN |
| PU-D08 | BATCH-04 | `BTreeAssetContributor` drops BB/context type names (`ToDtoWithTypeNames` test workaround). Root-cause fix at PU-301. | P3 | PU-301 | OPEN |

Legend:
- P1 = Critical (never enters tracker; always becomes Corrective Task 0 in next batch)
- P2 = Should fix (tracked here, assigned target batch)
- P3 = Nice to have (tracked here, best-effort)
- Status: OPEN / RESOLVED (do not delete resolved rows)

---

**Pre-existing baseline (NOT this thread's debt — do not "fix" as regressions):** DEBT-006 (10 Blueprints golden/snapshot), DEBT-008, SpatialHashSystem AV in EditorPreview, ClusterOpE2e DDS crash, flaky sub-80 ns perf (DEBT-014), ~26 pre-existing warnings (DEBT-BCP-004).
