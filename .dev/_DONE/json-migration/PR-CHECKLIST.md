# Per-Migrator PR Checklist

Use this checklist for every PR that touches
`Hrot/Engine/Hrot.Common/Scenario/Migrations/Migrators/`.

Derived from design *07 §9.1* (standard migrator-PR checklist) and lessons
learned in BATCH-17 and BATCH-18.

---

## Before Opening the PR

### Schema & Design

- [ ] Schema change has been reviewed and approved separately before authoring the migrator.
- [ ] Migrator pair (Up + Down) both implemented.
- [ ] Up and Down migrators are **adjacent-version-only** (no version-skipping).

### Implementation

- [ ] Migrator pair registered in `ScenarioMigrationModule` (or the relevant module).
- [ ] `CurrentVersion` in `ScenarioMigrationModule` bumped by exactly 1.
- [ ] `EntityPatch` helpers used where applicable (rename, add, remove, transform fields).
- [ ] `AddField` default values are **non-null** (no silent JSON nulls -- see D-026 pattern).
- [ ] Down migrator removes/reverts exactly what Up added (no orphaned fields).

### Tests

- [ ] Round-trip test: `v_n -> v_(n+1) -> v_n` produces original document (T2).
- [ ] "User edits survive" test: `v_n` user edits -> up -> down -> edits preserved (D-023 pattern).
- [ ] `EntityPatchTests` coverage for any new `EntityPatch` helper methods used.
- [ ] T4 corpus test passes (load at current version).
- [ ] T5 migration round-trip test passes, **or** baseline updated per `test-data/scenario-corpus/BASELINES.md`.

### Corpus

- [ ] A `v_n` scenario fixture and its `v_(n+1)` equivalent added to the test corpus, **or** existing fixture updated.
- [ ] `BASELINES.md` consulted if any baseline file was updated.

---

## After Review

- [ ] No unintended fields removed from unknown-schema documents.
- [ ] No `MigrationWarning.Level.Error` used as a silent swallow.
- [ ] Architect has approved the migrator pair, corpus addition, and any baseline regeneration.
- [ ] Commit is **atomic**: migrator source, tests, corpus files, and any baseline changes are in one commit.

---

## Reference

- Design: `.dev/json-migration/Migration-system.md` §9.1, §10
- Baseline refresh: `test-data/scenario-corpus/BASELINES.md`
- Task tracker: `.dev/json-migration/TASK-TRACKER.md`
