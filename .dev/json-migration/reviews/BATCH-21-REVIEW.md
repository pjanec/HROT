# BATCH-21 Review

**Status: APPROVED**

---

## Scope Check

| Task | Description | Result |
|------|-------------|--------|
| D-019 | Document sync-wrapper decision in 05-integration-patches.md | PASS |
| D-026 | ArgumentNullException guard in EntityPatch.AddField + T_EP_13 | PASS |
| D-027 | TransformComponent tests T_EP_14/15/16 | PASS |
| D-028 | InferCasing tests T_EP_17/18/19/20 via MatchExisting | PASS |
| JM-P5-001 | v1/v2 minimal-entity + empty-entities corpus pairs | PASS |
| JM-P5-004 | Stale-sidecar audit | CLEAN |

---

## Verification

### Tests run

```
dotnet test "Hrot/Engine/Hrot.Common.Tests/Hrot.Common.Tests.csproj" --no-build
  --> Passed: 54, Failed: 0  (was 34 before; 8 new T_EP_13..20 added)

dotnet test "Hrot/Engine/Hrot.Common.Tests/Hrot.Common.Tests.csproj" --no-build
     --filter "EntityPatch|T_EP"
  --> Passed: 20, Failed: 0
```

Build succeeded. Only pre-existing `Hrot.Blueprints.Tests` CS0234/CS0246 errors.

---

## Design Alignment

All changes are strictly within scope:

- **D-019**: Documentation only. Key Finding 5 now records the chosen implementation (option b: sync wrapper) and the justification (no `SynchronizationContext` on startup threads, cascading async would be excessive). Correctly notes the future-work implication.
- **D-026**: Minimal guard. The `ArgumentNullException` is thrown with an actionable message directing callers to use `JsonValue.Create((object?)null)` explicitly. The redundant `?.` on `defaultValue?.DeepClone()` remains (dead code after the guard) — noted as a P3 cleanup item below.
- **D-027**: Three tests cover the three meaningful behaviours of `TransformComponent`: mutation, skip on absent component, and sibling modification. All use the `[Fact]` + `Assert.Equal` pattern matching the file style.
- **D-028**: Four tests cover all four casing inference outcomes (all-Pascal, all-camel, empty/tie, equal-split). Tested indirectly via public API as designed (private method stays private). Clean separation of concerns.
- **JM-P5-001**: Corpus files have correct `$meta.schemaVersion` and correct content. Empty-entities v2 is structurally correct (migration is a no-op; only version bumps). `BASELINES.md` inventory updated.
- **JM-P5-004**: Audit is clean. No remediation needed.

---

## Test Quality

All new tests assert values and behaviour:
- T_EP_13: `Assert.Throws<ArgumentNullException>` — exact type, not AggregateException.
- T_EP_14: `Assert.Equal("Colonel", rank)` — value check.
- T_EP_15: `Assert.Equal(0, callCount)` — behaviour check (no call on absent component).
- T_EP_16: `Assert.Equal(42, newComp)` — sibling value present.
- T_EP_17–20: `Assert.True(component.ContainsKey("Tags"/"tags"/"Field"/"NewField"))` + negative assertion — double-sided key checks confirm the correct casing and exclude the wrong one.

---

## Early Failure Discipline

- `EntityPatch.AddField` now fails early and loudly with a clear `ArgumentNullException` message rather than silently inserting JSON null.
- No new silent exception paths introduced.

---

## Debt Tracker Updates

New debt added:

| ID | Description | Priority |
|----|-------------|----------|
| D-034 | `EntityPatch.AddField(JsonNode defaultValue)`: the `defaultValue?.DeepClone()` null-conditional is now dead code after the ArgumentNullException guard. Change to `defaultValue.DeepClone()`. Minor cleanup. | P3 |
| D-035 | Corpus fixtures `v1_minimal-entity` and `v1_empty-entities` have no dedicated T4/T5 test methods. Add `V1MinimalEntity_MigratedThroughPipeline_MatchesV2MinimalEntity` and `V1EmptyEntities_MigratedThroughPipeline_MatchesV2EmptyEntities` to `Phase3MigratorTests.cs`. | P3 |

Resolved:
- D-019 ✅ D-020 ✅ D-026 ✅ D-027 ✅ D-028 ✅

---

## Suggested Git Commit Message

```
fix: BATCH-21 -- D-019/026/027/028 EntityPatch hardening + JM-P5-001/004 corpus + doc

D-019: Document GetAwaiter().GetResult() sync-wrapper decision in 05-integration-patches.md
D-026: Add ArgumentNullException guard to EntityPatch.AddField(JsonNode defaultValue)
D-027: Add TransformComponent tests T_EP_14/15/16 to EntityPatchTests.cs
D-028: Add InferCasing tests T_EP_17/18/19/20 via AddField+MatchExisting
JM-P5-001: Add v1/v2 minimal-entity and empty-entities corpus fixture pairs
JM-P5-004: Stale-sidecar audit -- workspace is clean (0 snapshot dirs, 0 journal dirs)
```

*(Already committed as `4b3b1b63`.)*
