# CGF-1-BATCH-12: Scenario fail-fast debt + CGF1-S0307 (save/load wiring)

**Batch number:** CGF-1-BATCH-12  
**Tasks:** **Part A — BATCH-11 review follow-ups (tech debt)** → **CGF1-S0307** (Application-Layer Scenario Save/Load Wiring)  
**Phase:** Phase 3 — persistence  
**Estimated effort:** 30–42 hours (~6–10 h Part A + ~24–32 h S0307)  
**Priority:** HIGH  
**Dependencies:** [CGF-1-BATCH-11](../reviews/CGF-1-BATCH-11-REVIEW.md) — APPROVED (**CGF1-S0306** complete)

---

## Sequencing note

- **CGF1-S0302** (Portable Scenario Loading) remains **after** **S0307** unless the lead explicitly reprioritises.

---

## Onboarding

1. [.dev/.guides/DEV-GUIDE.md](../../.guides/DEV-GUIDE.md)  
2. [.dev/cgf-1/CGF-1-DESIGN.md](../CGF-1-DESIGN.md) §5.7  
3. [.dev/cgf-1/CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0307  
4. [.dev/cgf-1/reviews/CGF-1-BATCH-11-REVIEW.md](../reviews/CGF-1-BATCH-11-REVIEW.md) — “fail early and aloud” gaps  
5. Existing **`FDP.Toolkit.Replay.StoryTag`** ([`StoryTag.cs`](../../../FDP/Toolkits/FDP.Toolkit.Replay/StoryTag.cs)) — canonical **`struct`** with **`Guid StoryId`** (`ReplayComponentIds.StoryTag`).  
6. [.dev/DEBT-TRACKER.md](../../DEBT-TRACKER.md) — rows **Target Fix = CGF-1-BATCH-12**

**Report:** `.dev/cgf-1/reports/CGF-1-BATCH-12-REPORT.md`

---

## Mandatory workflow

Complete **Part A** first so **`ScenarioSerializer` / `FdpAutoSerializer`** do not accumulate silent-failure modes while **S0307** wiring starts calling them from real DSM paths.

---

## Part A — Tech debt (BATCH-11 review)

### A.1 — **`ScenarioSerializer` / resolver fail-fast** (P2)

**Files:** `FDP/Toolkits/FDP.Toolkit.Scenario/ScenarioSerializer.cs` (and small helpers if needed)

Replace **silent drops** with **loud failures** for **corrupt or inconsistent** input/output (distinct from **valid** subsystem-type skip):

| Situation | Current behaviour | Required direction |
|-----------|-------------------|---------------------|
| Matching subsystem but **`Entities` missing / wrong node type** | `return` | **`InvalidOperationException`** (or dedicated scenario exception) with a clear message |
| **`Entities` key not parseable as `Guid`** | `continue` | **Throw** — scenario file is invalid |
| **`SaveResolver.Resolve(Entity)`** for entity not in save map | `Guid.Empty` | **Throw** — programmer/data bug at save time |
| **`LoadResolver.Resolve(string)`** for unknown GUID when a reference is required | `Entity` default | **Throw** or document **strict** vs **lenient** mode; default must be **strict** for production wiring |
| **JSON component key** with no matching registered type | `continue` | **Throw** — unknown scenario component (typo / version skew) |

**Preserve** the **specified** no-op: **`Header.SubsystemType` ≠ configured subsystem** → return without creating entities (§CGF1-S0306).

Add **unit tests** in **`FDP.Toolkit.Scenario.Tests`** that expect exceptions for the above cases (at least 2–3 representative tests so regressions are caught).

### A.2 — **Translator `Extract` value typing** (P2 / P3)

**File:** `FDP/Toolkits/FDP.Toolkit.Scenario/ScenarioSerializer.cs`

Remove or narrow the **`switch`** **`default`** that stringifies arbitrary **`object`** values. **Fail fast** if a translator returns an unsupported payload type (or document an explicit allow-list).

### A.3 — **`FdpAutoSerializer_NoReflectionOnHotPath` rigor** (P3)

**File:** `FDP/Toolkits/FDP.Toolkit.Scenario.Tests/ScenarioSerializerTests.cs`

Strengthen the test toward the **task-detail** wording: e.g. a **test-only** delegate wrapper / counter that would observe **`PropertyInfo.GetValue`** if the hot path called it — not only **`UsesRuntimeReflection == false`**.

### A.4 — **Optional API hygiene**

If low-churn: align **`FdpAutoSerializer.Build()`** signature with **`Build(ComponentTypeRegistry registry)`** from §CGF1-S0306 **or** document in XML why the static registry is the single source of truth.

### A.5 — **`DrillMaster.FanOutSerializeLocal` call site** (P2 — from BATCH-10 debt)

When implementing **S0307** save path, **must** invoke **`FanOutSerializeLocal`** (or equivalent) from orchestrator **SaveScenario / SerializeLocal** handling so the storage-gateway integration is **end-to-end**. Close the **DEBT-TRACKER** row that tracks “no call site” when done.

### A.7 — **Unify `StoryTag` — single canonical type (`Guid`)** (P2 — product consistency)

**Problem:** **BATCH-11** introduced **`FDP.Toolkit.Scenario.StoryTag`** as a **managed `class`** with **`string? StoryId`** and a **separate** **`ScenarioComponentIds.StoryTag` (201)**. **`FDP.Toolkit.Replay`** already defines **`FDP.Toolkit.Replay.StoryTag`** as an **unmanaged `struct`** with **`Guid StoryId`** and **`ReplayComponentIds.StoryTag` (84)**, used by **`StoryRecorderModule`** and tests. Two different types with the same conceptual name breaks queries, story stop/delete, and recording filters.

**Required direction:**

1. **Canonical ECS component:** exactly **`FDP.Toolkit.Replay.StoryTag`** (`Guid StoryId`). **Remove** **`FDP.Toolkit.Scenario.StoryTag`**, remove **`ScenarioComponentIds.StoryTag`**, and **free** component ID **201** from scenario-only registration (update [`ScenarioComponentIds.cs`](../../../FDP/Toolkits/FDP.Toolkit.Scenario/ScenarioComponentIds.cs) and all tests).
2. **`ScenarioSerializer.Deserialize`:** change the story parameter from **`string? storyId`** to **`Guid? storyId`** (or overload with clear deprecation path). When **`asStory == true`**, require a **non-empty `storyId`** (`Guid` not `Empty`) or **throw** — do not silently stamp **`Empty`**.
3. **Project reference:** add **`FDP.Toolkit.Scenario` → `FDP.Toolkit.Replay`** so the scenario toolkit can stamp **`FDP.Toolkit.Replay.StoryTag`**. **If** that reference pulls an **unacceptable** transitive dependency into the scenario project, **stop and escalate**: preferred alternative is to **move `StoryTag` into `Fdp.Kernel`** (single struct, **`Guid StoryId`**) and update **Replay** + **Scenario** to use the kernel type — still **one** type, **one** component ID, **`Guid` only**.
4. **Wire / JSON:** DSM payloads may still carry **`StoryId` as a string** in **`PayloadJson`**; **parse to `Guid` once** at the application boundary before calling **`Deserialize(..., storyId: guid)`**. Do not persist story identity as arbitrary strings on the component.
5. **Tests:** update **`StoryLoad_StampsStoryTag`** (and any **`FDP.Toolkit.Replay`** tests if signatures change) to assert **`FDP.Toolkit.Replay.StoryTag`** and **`Guid`** equality.
6. **Docs:** align [CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0306 / §CGF1-S0308 and [CGF-1-DESIGN.md](../CGF-1-DESIGN.md) §5.6 / §5.8 with the unified type (lead: already updated in repo when this batch lands).

### A.8 — **DEBT-TRACKER**

Close **Part A** rows when merged; add new rows only if scope is intentionally deferred (with **Target Fix**).

---

## Part B — CGF1-S0307: Application-Layer Scenario Save/Load Wiring

**Task definition:** [CGF-1-TASK-DETAIL.md §CGF1-S0307](../CGF-1-TASK-DETAIL.md#cgf1-s0307--application-layer-scenario-saveload-wiring)  
**Design:** [CGF-1-DESIGN.md §5.7](../CGF-1-DESIGN.md#57-stage-37--application-layer-scenario-saveload-wiring)

Implement **all** work items (1–7) in the task detail, including:

1. **`GlobalContextDsmHandler`** (`Bagira.Orchestrator`) — save/load **`Orchestrator.json`**, **`MasterTimeController.SeedState`**, **`OrchestratorContextTopic`**.  
2. Register on the **Orchestrator’s** **`DrillSlave`** at startup.  
3. **`ScenarioLoadDsmHandler`** in **`Bagira.SimHost`** and **`Bagira.CGF`** — header peek, **`ScenarioSerializer.Deserialize`**, mismatch → success no-op.  
4. Wire **`SimHostApp` / CGF composition** (`OnLoad` or current equivalent).  
5. **`TransitionPlanner`**: **`ScenarioId`** → **`OperationStep(PrefetchScenario, scenarioId)`** before first **`TransitionStep`**.  
6. **`StorageGatewayModule`**: **`PrefetchScenarioAsync`**, NAS layout, **`NodeOpCommand(PrefetchFiles, …)`** per spec.  
7. Extended save path + **`scenario_manifest.json`**.

**Story identity:** Any code path that stamps or filters by story membership must use **`FDP.Toolkit.Replay.StoryTag`** and **`Guid`** — complete **Part A §A.7** before relying on story tags in integration tests.

### Integration tests

Task detail names **`Bagira.Orchestrator.Integration.Tests`**. **If that project does not exist**, create it, reference orchestrator + SimHost (and DDS deps as needed), add to **`IOS-IG-SimHost.sln`**, and implement:

- **`ScenarioSaveLoadTests.RoundTrip_SimHost_EntitiesMatchAfterLoad`**  
- **`ScenarioSaveLoadTests.OrchestratorContextRestored_AfterLoad`**  
- **`ScenarioSaveLoadTests.SubsystemTypeFilter_CGFFileNotLoadedBySimHost`**

Use **real temp directories** (and local paths substituting for **`\\NAS\...`** where appropriate) with **explicit assertions** — no silent swallow of I/O or parse errors in tests.

---

## Success criteria

- [x] Part A: fail-fast behaviour + tests; reflection test strengthened; **`FanOutSerializeLocal`** wired; **`StoryTag`** unified (**`Fdp.Kernel.StoryTag`**, **`Guid`**); DEBT updated. — [review §Summary](../reviews/CGF-1-BATCH-12-REVIEW.md#summary)  
- [x] Part B: CGF1-S0307 core deliverables + **3** integration tests green — [review §Gaps](../reviews/CGF-1-BATCH-12-REVIEW.md#gaps-vs-task-detail-cgf1-s0307) for deferred execution/wiring.  
- [x] Solution build clean; relevant test projects green.  
- [x] **DEBT-TRACKER** / **CGF-1-TASK-TRACKER** updated.  
- [x] Report filed.

---

## Reference

- **Review:** [CGF-1-BATCH-12-REVIEW.md](../reviews/CGF-1-BATCH-12-REVIEW.md) — APPROVED (with follow-ups)  
- [CGF-1-BATCH-11 review — gaps](../reviews/CGF-1-BATCH-11-REVIEW.md#gaps-vs-fail-early-and-aloud-review-criterion)  
- **Next:** [CGF-1-BATCH-13](CGF-1-BATCH-13-INSTRUCTIONS.md) — BATCH-12 debt + **CGF1-S0302**.
