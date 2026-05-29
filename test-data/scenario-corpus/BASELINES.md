# Baseline Refresh Process

This checklist covers how to regenerate T5 migration baselines when a migrator
changes a default field value or adds/removes a field from the scenario format.

---

## When to Regenerate

Regenerate baselines when:
- You add a new migrator that changes a default field value.
- A T5 corpus test fails unexpectedly after a schema version bump.
- A migrator's `Up` logic alters a field that was previously stable in committed fixtures.

---

## Prerequisites

```
dotnet build IOS-IG-SimHost.sln -c Debug
```

---

## Steps

1. **Identify which corpus file needs updating.**
   Run the T5 tests to see the diff between expected and actual output:
   ```
   dotnet test IOS-IG-SimHost.sln -c Debug --no-build --filter "T5"
   ```
   The failure message shows the corpus file path and the field(s) that changed.

2. **Inspect the migration output manually.**
   Read the failing assertion carefully. Confirm the new output is correct and
   intentional (not a bug in the migrator).

3. **Update the baseline file.**
   Edit the relevant JSON file under `test-data/scenario-corpus/` to match the
   new expected output.

4. **Re-run T5 tests to confirm green.**
   ```
   dotnet test IOS-IG-SimHost.sln -c Debug --no-build --filter "T5"
   ```

5. **Commit atomically.**
   The baseline update and the migrator change **must be in the same commit**.
   Never commit a baseline update without the migrator that causes it.

---

## Corpus Inventory

| Path | Version | Description |
|------|---------|-------------|
| `multi-version/v1_complete/scenario.json` | v1 | Full-featured scenario: multiple entities, EntityInfo + SimTransform, used by T4/T5 round-trip tests. |
| `multi-version/v2_complete/scenario.json` | v2 | Migration target for the v1_complete pair. Tags field added to EntityInfo. |
| `multi-version/v1_minimal-entity/scenario.json` | v1 | Minimal scenario: single entity with only EntityInfo (Name + ForceId). No SimTransform. |
| `multi-version/v2_minimal-entity/scenario.json` | v2 | Migration target for v1_minimal-entity. Tags: [] added. |
| `multi-version/v1_empty-entities/scenario.json` | v1 | Edge case: valid scenario with empty entities map. |
| `multi-version/v2_empty-entities/scenario.json` | v2 | Migration is a no-op (no entities to process). |

---

## Warning

Updating a baseline changes the accepted output for *all future* migration runs.
Always verify the new output against the design doc and the PR checklist at
`.dev/json-migration/PR-CHECKLIST.md` before committing.
