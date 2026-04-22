# CGF-1-BATCH-11 Review

**Batch:** CGF-1-BATCH-11  
**Reviewer:** Development Lead  
**Date:** 2026-04-06  
**Status:** **APPROVED** (with **P2 follow-ups** — fail-fast / test rigor)

**Report:** [CGF-1-BATCH-11-REPORT.md](../reports/CGF-1-BATCH-11-REPORT.md) — verified against **source**, [CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0306, and [CGF-1-DESIGN.md](../CGF-1-DESIGN.md) §5.6.

---

## Summary

**Part A** matches the report and instructions:

- **`PushToNodes_CopiesFileToAllTargets`** and **`PushToNodes_BadTarget_ReturnsPartialFailure`** are present in [`StorageGatewayTests.cs`](../../../Hrot.Orchestrator.Tests/StorageGatewayTests.cs) with real filesystem coverage.
- **`ClusterMaster`** XML uses plain prose for **`SerializeLocalTask.RemainingAcks`** ([`ClusterMaster.cs`](../../../Hrot.Orchestrator/ClusterMaster.cs) lines 45–48).

**Part B (CGF1-S0306)** is **substantively delivered**:

- Projects **`FDP.Toolkit.Scenario`** and **`FDP.Toolkit.Scenario.Tests`** exist, reference **`Fdp.Kernel`** only (no Hrot), and are in **`IOS-IG-SimHost.sln`**.
- **Non-generic** **`IEntityScenarioTranslator`** with **`BitMask256`**, **`Dictionary<string, object>`**, **`IGuidResolver`** — matches task detail.
- **`ScenarioSerializer`**: two-pass save/load, **`GetSaveableMask`** ∩ entity mask, translators then **`FdpAutoSerializer`**, subsystem header peek, **`ScenarioIgnoreTag`** skip, **`StoryTag`** on story load.
- **`FdpAutoSerializer`**: **`Expression.Field`**-based extract/inject, **`GetSaveableTypeIds()`** at build time.
- All **10** named unit tests exist and pass.

**Tests run (review):**

- `dotnet test Hrot.Orchestrator.Tests` — **22** passed.  
- `dotnet test FDP.Toolkit.Scenario.Tests` — **10** passed.

---

## Alignment with design §5.6

- N:M translators, consumption mask, auto fallback, **`SubsystemType`** filter, and exclusion mechanisms (**`DataPolicy.NoSave`**, **`[ScenarioIgnore]`**, **`ScenarioIgnoreTag`**) are reflected in code and tests.
- **`StoryTag` as `class`** is a justified deviation (managed component constraint on **`EntityRepository`**) — documented in the report; acceptable.

**Minor API deviation:** Task detail shows **`FdpAutoSerializer.Build(ComponentTypeRegistry registry)`**; implementation uses **`Build()`** and the **static** **`ComponentTypeRegistry`**. Behaviour matches the intended registry; the parameter is omitted (P3 hygiene / API alignment).

---

## Gaps vs “fail early and aloud” (review criterion)

The following are **intentional no-op** where the spec requires it:

- **`SubsystemType`** mismatch → immediate return (no entities) — **correct** per §CGF1-S0306.

**Silent / soft handling** that risks **masked corruption** or **debug difficulty** (schedule **P2** hardening in **BATCH-12** Part A):

1. **`Deserialize`**: if **`dom["Entities"]`** is missing or not a **`JsonObject`**, the method **returns without throwing** ([`ScenarioSerializer.cs`](../../../FDP/Toolkits/FDP.Toolkit.Scenario/ScenarioSerializer.cs) lines 188–189).
2. **Invalid entity keys** in **`Entities`** (`Guid.TryParse` fails): entries are **skipped** (lines 196–197, 206–207).
3. **`SaveResolver.Resolve(Entity)`**: unknown entity → **`Guid.Empty`** string (lines 319–322) instead of **`InvalidOperationException`** — can **serialize broken cross-refs** without surfacing the bug at save time.
4. **`LoadResolver.Resolve(string)`**: unknown GUID → **`default` Entity** (lines 341–346) — **silent** broken reference on load.
5. **Unknown JSON component keys**: **`FindTypeIdByName` < 0** → **`continue`** (lines 240–241) — typos in scenario files **drop data** without error.
6. **`Serialize`**: translator **`Extract`** values use a **`switch`** with **`_ => JsonValue.Create(rawValue?.ToString())`** (lines 108–117) — unsupported types become **strings** without failing fast.

**Tests:** **`FdpAutoSerializer_NoReflectionOnHotPath`** asserts **`UsesRuntimeReflection == false`** (a constant) and **`IsBuilt`**, plus a functional round-trip. It does **not** implement the task-detail option of **profiling / proving absence of `PropertyInfo.GetValue` on the hot path** — weaker than the written success condition (P3).

---

## Verdict on tests

Tests **do** exercise the behaviours that matter for **happy-path** CGF1-S0306: round-trip, N:M compression, mask deduplication, **`Entity`** GUID refs, **`NoSave`**, field ignore, entity ignore tag, story tag, subsystem filter. They **do not** yet enforce **strict** DOM validation or **fail-loud** semantics above — track as debt.

---

## Suggested commit message

```
feat(cgf-1): BATCH-11 scenario toolkit + gateway push tests

- FDP.Toolkit.Scenario: non-generic translators, IGuidResolver, FdpAutoSerializer,
  ScenarioSerializerBuilder/Serializer, StoryTag/ScenarioIgnoreTag
- Tests: 10 ScenarioSerializer scenarios; StorageGateway PushToNodes parity
- ClusterMaster: fix SerializeLocalTask RemainingAcks XML

Refs: CGF-1-BATCH-11, CGF1-S0306
```

---

## Next batch

**[CGF-1-BATCH-12](../batches/CGF-1-BATCH-12-INSTRUCTIONS.md)** — Part A: scenario **fail-fast** debt + stronger reflection assertion; Part B: **CGF1-S0307**.
