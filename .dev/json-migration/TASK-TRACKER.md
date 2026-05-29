# JSON Migration System — Task Tracker

**Reference:** see [TASK-DETAILS.md](./TASK-DETAILS.md) for full task descriptions and success conditions.
**Design:** [Migration-system.md](./Migration-system.md).
**Tech-debt log:** [DEBT-TRACKER.md](./DEBT-TRACKER.md).

Legend: `[ ]` not done · `[x]` done · **(GATE)** = architect approval required before next task.

---

## Phase 1 — Core infrastructure

**Goal:** ship `Fdp.Core.Serialization.Migrations` as a self-contained, fully-tested library. No engine code outside `Fdp.Core` is touched (only exception: the M-1 extraction in JM-P1-007 reaches into `Fdp.Toolkits.ReplayBrowser.Diff`).

- [x] **JM-P1-001** Foundation types (DocumentMeta, MigrationDirection, MigrationReport, MigrationWarning, MigrationException, SnapshotEntry, SidecarFileInfo, SidecarKind, FdpDocumentTypes) [details](./TASK-DETAILS.md#jm-p1-001--foundation-types)
- [x] **JM-P1-002** JsonEnvelope with streaming peek [details](./TASK-DETAILS.md#jm-p1-002--jsonenvelope-streaming-peek)
- [x] **JM-P1-003** JSONPath parser / applicator [details](./TASK-DETAILS.md#jm-p1-003--jsonpath-parserapplicator)
- [x] **JM-P1-004** MigrationContext + scope stack [details](./TASK-DETAILS.md#jm-p1-004--migrationcontext--scope-stack)
- [x] **JM-P1-005** MigrationRegistry + IJsonDocumentMigrator [details](./TASK-DETAILS.md#jm-p1-005--registry--ijsondocumentmigrator)
- [x] **JM-P1-006** MigrationPipeline **(GATE)** [details](./TASK-DETAILS.md#jm-p1-006--migrationpipeline-gate)
- [x] **JM-P1-007** DomDiffer extraction from Fdp.Toolkits **(GATE)** [details](./TASK-DETAILS.md#jm-p1-007--domdiffer-extraction-from-fdptoolkits-gate)
- [x] **JM-P1-008** DiffToJournalConverter + UnknownsJournal + HashUtilities [details](./TASK-DETAILS.md#jm-p1-008--difftojournalconverter--unknownsjournal--hashutilities)
- [x] **JM-P1-009** IMigrationStorage + InMemoryMigrationStorage [details](./TASK-DETAILS.md#jm-p1-009--imigrationstorage--inmemorymigrationstorage)
- [x] **JM-P1-010** FileSystemMigrationStorage [details](./TASK-DETAILS.md#jm-p1-010--filesystemmigrationstorage)
- [x] **JM-P1-011** ReadOnlyMigrationAdapter **(GATE)** [details](./TASK-DETAILS.md#jm-p1-011--readonlymigrationadapter-gate)
- [x] **JM-P1-012** PersistentMigrationAdapter + Round-Trip Diff **(GATE)** [details](./TASK-DETAILS.md#jm-p1-012--persistentmigrationadapter--round-trip-diff-gate)
- [x] **JM-P1-013** MigrationServices + MigrationBootstrap **(GATE)** [details](./TASK-DETAILS.md#jm-p1-013--migrationservices--migrationbootstrap-gate)
- [x] **JM-P1-014** Phase 1 acceptance gate **(GATE)** [details](./TASK-DETAILS.md#jm-p1-014--phase-1-acceptance-gate-gate)

---

## Phase 2 — Envelope rollout

**Goal:** every JSON read/write call site in the engine adopts `$meta` via the migration adapters. No schema versions change. HROT-side migration modules are stubs (no migrators yet).

- [x] **JM-P2-001** Write integration-patches document (05) [details](./TASK-DETAILS.md#jm-p2-001--write-integration-patches-document-doc-05)
- [x] **JM-P2-002** HrotDocumentTypes + PassthroughFormatsModule + skeleton modules [details](./TASK-DETAILS.md#jm-p2-002--hrotdocumenttypes--passthroughformatsmodule-hrot-side)
- [x] **JM-P2-003** Patch scenario read/write paths [details](./TASK-DETAILS.md#jm-p2-003--patch-scenario-readwrite-paths)
- [x] **JM-P2-004** Patch blueprint read/write paths [details](./TASK-DETAILS.md#jm-p2-004--patch-blueprint-readwrite-paths)
- [x] **JM-P2-005** Patch TKB read/write paths [details](./TASK-DETAILS.md#jm-p2-005--patch-tkb-readwrite-paths)
- [x] **JM-P2-006** Patch road network read/write paths [details](./TASK-DETAILS.md#jm-p2-006--patch-road-network-readwrite-paths)
- [x] **JM-P2-007** Patch replay-metadata paths (incl. federation + export) [details](./TASK-DETAILS.md#jm-p2-007--patch-replay-metadata-paths-incl-federation--export)
- [x] **JM-P2-008** Patch passthrough writers (Orchestrator, MapInteractionConfig, NodeConfiguration, StructEdit) [details](./TASK-DETAILS.md#jm-p2-008--patch-passthrough-writers-orchestrator-mapinteractionconfig-nodeconfiguration-structedit)
- [x] **JM-P2-009** Bootstrap wiring (role-driven NodeBootstrapper + editor + CLI) **(GATE)** [details](./TASK-DETAILS.md#jm-p2-009--bootstrap-wiring-role-driven-nodebootstrapper--editor--cli-gate)
- [x] **JM-P2-010** Committed fixture envelope-migration script [details](./TASK-DETAILS.md#jm-p2-010--committed-fixture-envelope-migration-script)
- [x] **JM-P2-011** Phase 2 CI regression run **(GATE)** [details](./TASK-DETAILS.md#jm-p2-011--phase-2-ci-regression-run-gate)

---

## Phase 3 — First migrator pair

**Goal:** the first real v1↔v2 schema change goes through the full pipeline end-to-end on a deliberately small case (e.g. `EntityInfo.Tags`).

- [x] **JM-P3-001** Author first migrator pair [details](./TASK-DETAILS.md#jm-p3-001--author-first-migrator-pair-recommended-entityinfotags)
- [x] **JM-P3-002** Author paired test corpus (v1 + v2) [details](./TASK-DETAILS.md#jm-p3-002--author-paired-test-corpus-v1--v2)
- [x] **JM-P3-003** Register migrator pair; bump CurrentVersion [details](./TASK-DETAILS.md#jm-p3-003--register-migrator-pair-bump-currentversion)
- [x] **JM-P3-004** Update host bootstraps to use the module [details](./TASK-DETAILS.md#jm-p3-004--update-host-bootstraps-to-use-the-module)
- [x] **JM-P3-005** T4/T5 sample run [details](./TASK-DETAILS.md#jm-p3-005--t4t5-sample-run)
- [ ] **JM-P3-006** Architect dry-run **(GATE)** [details](./TASK-DETAILS.md#jm-p3-006--architect-dry-run-gate-gate)

---

## Phase 4 — Editor + CLI integration

**Goal:** the editor UI surfaces migration outcomes (warnings, degraded-mode banners). The `Hrot.ClusterRunner --mode migrate` subcommand becomes available.

- [x] **JM-P4-001** Editor: warning modal on migration [details](./TASK-DETAILS.md#jm-p4-001--editor-warning-modal-on-first-up--or-down-migration)
- [x] **JM-P4-002** Editor: degraded-mode banner [details](./TASK-DETAILS.md#jm-p4-002--editor-degraded-mode-banner)
- [x] **JM-P4-003** Editor: "Migration history" menu item [details](./TASK-DETAILS.md#jm-p4-003--editor-migration-history-menu-item)
- [x] **JM-P4-004** CLI: `--mode migrate` subcommand [details](./TASK-DETAILS.md#jm-p4-004--cli-hrotclusterrunner---mode-migrate-subcommand)
- [x] **JM-P4-005** CLI: progress reporting [details](./TASK-DETAILS.md#jm-p4-005--cli-progress-reporting)
- [ ] **JM-P4-006** Manual QA **(GATE)** [details](./TASK-DETAILS.md#jm-p4-006--manual-qa-gate-gate)

---

## Phase 5 — CI corpus rollout (steady state)

**Goal:** ongoing maintenance. No defined end. Tasks below are templates the team applies on every new migrator pair / corpus addition.

- [ ] **JM-P5-001** Corpus expansion (ongoing) [details](./TASK-DETAILS.md#jm-p5-001--corpus-expansion)
- [x] **JM-P5-002** Baseline refresh process documented [details](./TASK-DETAILS.md#jm-p5-002--baseline-refresh-process)
- [x] **JM-P5-003** Per-migrator PR checklist committed [details](./TASK-DETAILS.md#jm-p5-003--per-migrator-pr-checklist)
- [ ] **JM-P5-004** Quarterly stale-sidecar audit (recurring) [details](./TASK-DETAILS.md#jm-p5-004--quarterly-stale-sidecar-audit)
