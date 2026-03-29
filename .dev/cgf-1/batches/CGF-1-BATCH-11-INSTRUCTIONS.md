# CGF-1-BATCH-11: Tech debt + CGF1-S0306 (serialization toolkit)

**Batch number:** CGF-1-BATCH-11  
**Tasks:** **Part A — BATCH-10 tech debt** → **CGF1-S0306** (Scenario/Story Serialization Toolkit)  
**Phase:** Phase 3 — persistence infrastructure  
**Estimated effort:** 22–30 hours (~2–4 h Part A + ~20–26 h S0306); may be split if needed.  
**Priority:** HIGH  
**Dependencies:** [CGF-1-BATCH-10](../reviews/CGF-1-BATCH-10-REVIEW.md) — APPROVED (**CGF1-S0301** complete)

---

## Sequencing note (lead)

- **CGF1-S0307** is **CGF-1-BATCH-12** — see [CGF-1-BATCH-12-INSTRUCTIONS.md](CGF-1-BATCH-12-INSTRUCTIONS.md).  
- **CGF1-S0302** (Portable Scenario Loading) remains **after** both **S0306** and **S0307** (toolkit + wiring).

---

## Onboarding

1. [.dev/.guides/DEV-GUIDE.md](../../.guides/DEV-GUIDE.md)  
2. [.dev/cgf-1/CGF-1-DESIGN.md](../CGF-1-DESIGN.md) §5.6  
3. [.dev/cgf-1/CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0306  
4. [.dev/cgf-1/reviews/CGF-1-BATCH-10-REVIEW.md](../reviews/CGF-1-BATCH-10-REVIEW.md) — gaps / debt rows  
5. [.dev/DEBT-TRACKER.md](../../DEBT-TRACKER.md) — rows **Target Fix = CGF-1-BATCH-11**

**Report:** `.dev/cgf-1/reports/CGF-1-BATCH-11-REPORT.md`

---

## Mandatory workflow

Complete **Part A** before **S0306** so gateway test coverage and doc hygiene do not lag.

---

## Part A — Tech debt (BATCH-10 follow-ups)

### A.1 — `PushToNodesAsync` unit tests (P3)

**File:** `Bagira.Orchestrator.Tests/StorageGatewayTests.cs` (extend)

- Add tests analogous to Pull: e.g. one NAS source file copied to **multiple** local temp destinations; assert counts and files on disk.  
- Add **partial failure** case (one bad target path or missing source).  
- Close the matching **DEBT-TRACKER** row when merged.

### A.2 — `DrillMaster` XML hygiene (P3)

**File:** `Bagira.Orchestrator/DrillMaster.cs`

- Fix invalid `<see cref="_remainingAcks"/>` on the `SerializeLocalTask` / pending-task summary — replace with correct member reference or plain prose.

### A.3 — DEBT-TRACKER

- Close **A.1** and **A.2** rows when done.  
- Leave **subprocess `dotnet run --mode ci`** and **IG SetFilter integration test** rows **Opportunistic** unless CI capacity appears.

---

## Part B — CGF1-S0306: Scenario/Story Serialization Toolkit

**Task definition:** [CGF-1-TASK-DETAIL.md §CGF1-S0306](../CGF-1-TASK-DETAIL.md#cgf1-s0306--scenariostory-serialization-toolkit)  
**Design:** [CGF-1-DESIGN.md §5.6](../CGF-1-DESIGN.md#56-stage-36--scenariostory-serialization-toolkit) (architecture diagram §2 file map lists expected source files)

Normative behaviour is **§CGF1-S0306**; use §5.6 for narrative (N:M translators, exclusion table, save pipeline).

### Deliverables (check against task detail — do not substitute the old generic-translator design)

1. **Project** **`FDP/Toolkits/FDP.Toolkit.Scenario`** — references **`Fdp.Kernel`** + **`System.Text.Json`** only (**no** `Bagira.*`).

2. **`IEntityScenarioTranslator`** (**non-generic**, N:M):
   - `BitMask256 GetConsumedComponentsMask()`
   - `CanTranslate`, `Extract` / `Inject` using **`Dictionary<string, object>`** scenario entries and **`IGuidResolver`** (signatures per §CGF1-S0306).

3. **`IGuidResolver`** — `Resolve(Entity)` (save) and `Resolve(string)` (load); **`ScenarioSerializer`** builds concrete save/load implementations backed by `Dictionary<Entity, Guid>` / `Dictionary<Guid, Entity>` during pass 1.

4. **`FdpAutoSerializer`**:
   - **`Build(ComponentTypeRegistry registry)`** — per registered component type, compile delegates with **`Expression.Property`** (extract / inject shapes per §CGF1-S0306).
   - **No** `Type.GetProperties()` / **`PropertyInfo.GetValue`** on the hot path.
   - Skip **`[ScenarioIgnore]`** fields at compile time; patch **`Entity`**-typed fields via **`IGuidResolver`**.
   - Types with **`DataPolicy.NoSave`** must be **omitted from delegate compilation** (they never appear in the saveable mask — see note below).

5. **`ScenarioSerializerBuilder`** — **`RegisterTranslator(IEntityScenarioTranslator)`** (no type parameter); **`Build()`** runs **`FdpAutoSerializer.Build`**, freezes translators, returns **`ScenarioSerializer`**.

6. **`ScenarioSerializer`**:
   - **`Serialize(EntityRepository, ScenarioHeader)`** — pass 1: enumerate entities **without** **`ScenarioIgnoreTag`**, build save **`IGuidResolver`**. Pass 2 per entity: start from **`repo.GetSaveableMask(entity)`**; run translators (**clear consumed bits** via mask), then **`FdpAutoSerializer`** on remaining bits.
   - **`Deserialize(EntityRepository, JsonObject, bool asStory = false, string? storyId = null)`** — peek **`Header.SubsystemType`**; on mismatch **return without** creating entities; else two-pass load with **`IGuidResolver`**, translator **`Inject`** + auto-serializer; if **`asStory`**, stamp **`StoryTag`** on every created entity.

7. **`ScenarioHeader`** — `record ScenarioHeader(string SubsystemType, int SchemaVersion = 1)`.

8. **`[ScenarioIgnore]`** — field-level exclusion. **`ScenarioIgnoreTag`** — empty component with **`[DataPolicy(DataPolicy.NoSave)]`**; serializer enumerates with **`.Without<ScenarioIgnoreTag>()`** (or equivalent) so whole entities are skipped.

9. **Integration with existing FDP policy:** call **`EntityRepository.GetSaveableMask()`** as the starting mask; do **not** duplicate **`DataPolicy.NoSave`** logic (see §CGF1-S0306 note).

10. **Artifacts** — align with design §6 file map where applicable (e.g. **`ScenarioEntityDto.cs`** if the implementation needs a shared DTO type; **`ScenarioIgnoreAttribute.cs`**).

### Test project: `FDP.Toolkit.Scenario.Tests`

Implement **all** §CGF1-S0306 success conditions (names must match intent):

| Test | Intent (short) |
|------|----------------|
| `RoundTrip_1to1_PreservesAllFields` | No custom translators; **`FdpAutoSerializer`** round-trip for 3 × `DummyPosition`. |
| `NtoM_CustomTranslator_CompressesComponents` | e.g. **`MissileOrdnanceTranslator`** consumes `BallisticProjectile` + `PhysicsCollider` → single **`OrdnanceDef`** DOM key; round-trip restores both components. |
| `ConsumptionMask_PreventsDuplication` | After translator extract, consumed bits cleared; auto-serializer **does not** emit those components. |
| `EntityCrossReference_ResolvedViaIGuidResolver` | **`GuidedTarget.TargetId: Entity`** ↔ GUID string in DOM; deserialize resolves handle. |
| `DataPolicyNoSave_ComponentExcluded` | e.g. **`SimVelocity`** with **`NoSave`** absent from DOM. |
| `ScenarioIgnore_FieldExcluded` | Saved field present; **`[ScenarioIgnore]`** field absent in JSON. |
| `ScenarioIgnoreTag_EntitySkipped` | Entity with **`ScenarioIgnoreTag`** missing from **`dom["Entities"]`**. |
| `StoryLoad_StampsStoryTag` | **`asStory: true`** → every created entity has **`StoryTag`**. **Note:** BATCH-11 shipped a scenario-local **`StoryTag`** (`string`, managed class). **CGF-1-BATCH-12 §A.7** replaces it with the canonical **`FDP.Toolkit.Replay.StoryTag`** (`Guid`) — see BATCH-12 instructions. |
| `SubsystemType_MismatchSkipsDeserialize` | Wrong header → **`EntityCount`** unchanged. |
| `FdpAutoSerializer_NoReflectionOnHotPath` | After **`Build()`**, **`Serialize`** path does not invoke **`PropertyInfo.GetValue`** (profiling stub or delegate inspection per task). |

Add new projects to the solution and document paths + any test-only component types in the batch **report**.

---

## Success criteria

- [x] Part A: `PushToNodesAsync` tests; `DrillMaster` XML fixed; DEBT rows closed. — [review §Summary](../reviews/CGF-1-BATCH-11-REVIEW.md#summary)  
- [x] Part B: CGF1-S0306 success conditions — all listed **`FDP.Toolkit.Scenario.Tests`** green.  
- [x] Solution build clean; relevant test projects green.  
- [x] **DEBT-TRACKER** updated; **CGF-1-TASK-TRACKER** marks **S0306** `[x]` (**S0307** → BATCH-12).  
- [x] Report filed.

---

## Reference

- [CGF-1-BATCH-10 review — gaps](../reviews/CGF-1-BATCH-10-REVIEW.md#gaps-and-risks-non-blocking)  
- **Review:** [CGF-1-BATCH-11-REVIEW.md](../reviews/CGF-1-BATCH-11-REVIEW.md) — APPROVED  
- **Next:** [CGF-1-BATCH-12](CGF-1-BATCH-12-INSTRUCTIONS.md) — fail-fast debt + **CGF1-S0307**.  
- **After S0307:** **CGF1-S0302** (portable scenario loading / `EditLoadDsmHandler`).
