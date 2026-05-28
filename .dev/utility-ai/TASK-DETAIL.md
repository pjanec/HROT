# Utility AI — Task Detail

**Reference:** See the design doc set for full context:
- [`Utility_AI_Design_v1_1.md`](./Utility_AI_Design_v1_1.md) — architecture (v1.2)
- [`Utility_AI_SourceGenerator_Design_v1_1.md`](./Utility_AI_SourceGenerator_Design_v1_1.md) — source generator
- [`Utility_AI_Editor_Design_v1_2.md`](./Utility_AI_Editor_Design_v1_2.md) — editor
- [`Utility_AI_StarterPack_Examples_v1_1.md`](./Utility_AI_StarterPack_Examples_v1_1.md) — starter pack
- [`Runtime_Tuning_Console_and_AI_Overlays_Design_v1_0.md`](./Runtime_Tuning_Console_and_AI_Overlays_Design_v1_0.md) — tuning + overlays
- [`Curve_Editor_in_StructEdit_Guide_v1_1.md`](./Curve_Editor_in_StructEdit_Guide_v1_1.md) — curve widget wrap
- [`Build_Order_UtilityAI_Tuning_Overlays_v1_0.md`](./Build_Order_UtilityAI_Tuning_Overlays_v1_0.md) — six-phase plan
- [`PREREQ_Phase0_Bundle.md`](./PREREQ_Phase0_Bundle.md) — Phase-0 bundle (six items)

Each task below has a unique id and explicit success conditions (usually unit-test specs). Tasks reference design-doc sections instead of duplicating content. Where a task spans multiple design docs, the primary reference is named first.

Convention: task ids `UAI-PNN-MM` where `P0`/`P1`/…/`P6` is the phase and `MM` is the in-phase index.

---

## Phase 0 — Prerequisite bundle

**Goal:** Land the six codebase prerequisites in one batch so Phase 1 can compile against real APIs.

### TASK-UAI-P0-01: `WeaponState.MaxAmmo` cache

**Design reference:** `PREREQ_Phase0_Bundle.md` §P0.1; `Utility_AI_Design_v1_1.md` §6.7.

**Scope:**
- Add `public int MaxAmmo;` field to `WeaponState` in [`FDP/Toolkits/Fdp.Toolkits/Combat/Components/CombatComponents.cs`](../../FDP/Toolkits/Fdp.Toolkits/Combat/Components/CombatComponents.cs).
- Update [`CombatTkbTranslator.cs:67-79`](../../FDP/Toolkits/Fdp.Toolkits/Combat/Translators/CombatTkbTranslator.cs#L67-L79) to set `MaxAmmo = primary.InitialAmmunition` alongside `Ammo`.
- No other call site mutates `MaxAmmo`.

**NOT included:** Multi-mount work (TASK-UAI-P0-02), ammo readers (Phase 1).

**Constraints:**
- `WeaponState` must remain `[StructLayout(LayoutKind.Sequential)]` unmanaged.
- No reflection, no managed fields.
- Field order: keep existing fields in their current positions; append `MaxAmmo` at end to minimize binary churn.

**Success Conditions:**

SC-P0-01-1: `sizeof(WeaponState)` is `16` bytes after the change (12 → 16 with the new int + no extra padding).

SC-P0-01-2: A TKB spawn with `InitialAmmunition = 30` produces `WeaponState { Ammo = 30, MaxAmmo = 30, … }`.

SC-P0-01-3: Firing 30 rounds drops `Ammo` to 0; `MaxAmmo` remains 30.

SC-P0-01-4: `default(WeaponState).MaxAmmo == 0`; downstream readers must handle this without throwing.

---

### TASK-UAI-P0-02: Multi-mount weapon entities

**Design reference:** `PREREQ_Phase0_Bundle.md` §P0.2.

**Scope:**
- Define `WeaponMountInfo` component in `Fdp.Toolkit.Combat` (fields: `int MountIndex`, `ulong WeaponGuid`, `float EffectiveRange`). Register a new `GlobalComponentIds.WeaponMountInfo` id.
- Update [`CombatTkbTranslator.cs`](../../FDP/Toolkits/Fdp.Toolkits/Combat/Translators/CombatTkbTranslator.cs) to iterate `suite.Mounts`. Index 0 stays on the owner (back-compat). Indices 1..N create child entities carrying `WeaponState` + `WeaponMountInfo` + `PartMetadata { ParentEntity = owner, InstanceId = i }`.
- Add `WeaponMountQuery.EnumerateMounts(repo, owner, Span<Entity> dest)` static helper in `Fdp.Toolkit.Combat`, returning the count and stable index order (owner first if it carries `WeaponState`).

**NOT included:** AI scorer logic (Phase 1).

**Constraints:**
- No allocations on the enumeration hot path.
- Mounts beyond `dest.Length` are truncated; caller is responsible for buffer size (typical = 4).
- `WeaponMountInfo.EffectiveRange` reads `WeaponCapabilitiesDto.EffectiveRange` if a `WeaponCapabilities` descriptor is present, else `0f`.
- The new component does **not** carry `[DataPolicy.NoSave]` — mount configuration should round-trip through scenarios.

**Success Conditions:**

SC-P0-02-1: A TKB definition with `Mounts.Count == 3` produces 3 total `WeaponState` components: one on the owner, two on child entities. Each child carries `WeaponMountInfo` + `PartMetadata.ParentEntity == owner`.

SC-P0-02-2: `WeaponMountQuery.EnumerateMounts(repo, owner, dest)` returns 3; `dest[0] == owner`; `dest[1]` and `dest[2]` are the children in `MountIndex` order.

SC-P0-02-3: A `Mounts.Count == 1` definition spawns one `WeaponState` on the owner and zero children — back-compat with existing single-mount entities.

SC-P0-02-4: Modifying `WeaponState.Ammo` on one mount does not affect the others.

SC-P0-02-5: `WeaponMountInfo.EffectiveRange` matches `WeaponCapabilitiesDto.EffectiveRange` from the same TKB definition when present; reads `0f` when absent.

---

### TASK-UAI-P0-03: Raise `MaxTrackedTargets` to 16

**Design reference:** `PREREQ_Phase0_Bundle.md` §P0.3; `Utility_AI_Design_v1_1.md` §8.1.

**Scope:**
- Change [`FDP/Toolkits/Fdp.Toolkits/Perception/PerceptionConstants.cs:11`](../../FDP/Toolkits/Fdp.Toolkits/Perception/PerceptionConstants.cs#L11) from `4` to `16`.
- No code changes required in `TargetMemory` / `SensorContactList` — both use `fixed T[PerceptionConstants.MaxTrackedTargets]` already.
- Audit perception tests that asserted "table fills at 4 entries"; update to 16 where appropriate.

**NOT included:** Utility scorer assertion (Phase 1 ships that).

**Constraints:**
- `sizeof(TargetMemory)` grows from ≈ 104 to ≈ 404 bytes; verify no downstream serializer / DDS topic hardcodes the smaller size.
- `sizeof(SensorContactList)` grows from ≈ 56 to ≈ 212 bytes; same audit.

**Success Conditions:**

SC-P0-03-1: `PerceptionConstants.MaxTrackedTargets == 16`.

SC-P0-03-2: `sizeof(TargetMemory)` calculated at compile time fits within any per-entity component budget the engine enforces (read the budget from the engine's existing component-size constraints).

SC-P0-03-3: All existing perception tests (`FDP/Toolkits/Fdp.Toolkits.Tests/Perception/`) pass green after the raise.

SC-P0-03-4: Spawn 16 contacts visible to a single perceiver; `TargetMemory.Count == 16` and `ThreatScores` sorted descending.

SC-P0-03-5: Spawn the 17th contact with a higher threat than the lowest; the lowest is evicted (existing `AddOrUpdateTarget` eviction path holds).

---

### TASK-UAI-P0-04: `UnitRoster.Add` / `IndexOf` helpers

**Design reference:** `PREREQ_Phase0_Bundle.md` §P0.4.

**Scope:**
- Add two static methods to [`UnitRoster.cs`](../../FDP/Engine/Fdp.Core/CommandHierarchy/UnitRoster.cs):
  ```csharp
  public static int Add(ref UnitRoster roster, long packedEntity, ushort designation = 0);
  public static int IndexOf(ref UnitRoster roster, long packedEntity);
  ```

**NOT included:** Squad-hierarchy systems (already exist).

**Constraints:**
- Zero allocations.
- `Add` returns the new slot index (0..15) or -1 if full; does not throw.
- `IndexOf` returns -1 if not present.
- Methods must be `static` and take `ref UnitRoster` (the struct holds `fixed` arrays).

**Success Conditions:**

SC-P0-04-1: `Add` returns sequential indices 0..15 for 16 sequential calls; the 17th returns -1 without mutating the roster.

SC-P0-04-2: `IndexOf` returns the correct slot for present `packedEntity` values; returns -1 for absent.

SC-P0-04-3: 10⁶ Add/IndexOf calls cause zero managed allocations (GC allocation tracker assertion).

---

### TASK-UAI-P0-05: `Blackboard1024.Project<T>` helper

**Design reference:** `PREREQ_Phase0_Bundle.md` §P0.5; `Utility_AI_Design_v1_1.md` §10.1.

**Scope:**
- Add `public static ref T Project<T>(ref Blackboard1024 bb) where T : unmanaged` to [`BehaviorComponents.cs`](../../FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BehaviorComponents.cs), wrapping `Unsafe.As<Blackboard1024, T>(ref bb)`.
- Method annotated `[MethodImpl(MethodImplOptions.AggressiveInlining)]`.

**NOT included:** Migration of existing call sites (raw `Unsafe.As` remains valid).

**Constraints:**
- Zero overhead vs. raw `Unsafe.As` (verify via release-build inspection or microbenchmark).
- No managed allocations.

**Success Conditions:**

SC-P0-05-1: A write through `Project<MyState>(ref bb)` is visible via the underlying `Blackboard1024.Memory` bytes (mutual aliasing confirmed).

SC-P0-05-2: 10⁶ `Project<T>` calls allocate zero managed bytes.

SC-P0-05-3: A microbenchmark shows `Project<T>` and raw `Unsafe.As<>` differ in no measurable way (release build).

---

### TASK-UAI-P0-06: `UtilityTestWorld` helper

**Design reference:** `PREREQ_Phase0_Bundle.md` §P0.6; `Utility_AI_StarterPack_Examples_v1_1.md` §0.

**Scope:**
- Create `Hrot.AI.Tests/Utility/UtilityTestWorld.cs` (or wherever the existing AI tests live — match precedent). Class is `internal sealed`, implements `IDisposable`.
- Surface listed in `PREREQ_Phase0_Bundle.md` §P0.6 (constructor + `SpawnAgent`, `SpawnWeaponMount`, `SetWeaponAmmo`, `SeedContact`, `SpawnEqsSensor`, `SpawnLeader`, `SpawnSquadMember`, `AssignmentFor`, `Fnv1a32`).
- The constructor registers all components the AI readers touch (Health, WeaponState, WeaponMountInfo, TargetMemory, SensorContactList, EqsSensor, EqsCognitiveBuffer, PartMetadata, UnitRoster, UnitSubordinate, Blackboard1024, Position, plus the Utility-layer components added in Phase 1).

**NOT included:** Tests that exercise the Utility scorer (Phase 1+); those are clients of this helper.

**Constraints:**
- No DDS, no Muscle modules.
- Component registration uses whatever public API `EntityRepository` exposes (read at implementation time).
- All public methods return either `void` or a strongly-typed `Entity`; no `object`.

**Success Conditions:**

SC-P0-06-1: `new UtilityTestWorld()` constructs without exceptions; `Dispose()` releases without leaks.

SC-P0-06-2: `SpawnAgent(1f, 1f)` returns an entity carrying every component listed in §0 of the starter pack.

SC-P0-06-3: `SpawnWeaponMount(owner, mountIndex: 1, …)` creates a child entity with `WeaponState` + `WeaponMountInfo { MountIndex = 1, … }` + `PartMetadata.ParentEntity == owner`.

SC-P0-06-4: `SeedContact` calls real `TargetMemory.AddOrUpdateTarget`; the contact lands in slot 0 with the supplied score.

SC-P0-06-5: `SpawnEqsSensor(self, blueprintId, topScore, count, instanceId)` produces a child carrying `EqsSensor.BlueprintId == blueprintId`, `EqsSensor.Epoch == 1`, and `EqsCognitiveBuffer` whose `GetSpanRO()[0].Score == topScore`.

SC-P0-06-6: `Fnv1a32(string)` matches the formula used by `BTreeActionGenerator.ComputeHash` (32-bit FNV-1a basis = 2166136261, prime = 16777619, no truncation for the 32-bit form). Hash parity is asserted against a pinned vector (e.g. `Fnv1a32("CoverQuery")` produces a constant value documented in the test).

---

### TASK-UAI-P0-07: Phase-0 integration test (gate)

**Design reference:** `PREREQ_Phase0_Bundle.md` "Phase-0 exit gate".

**Scope:**
- One xUnit test in `Hrot.AI.Tests` (or wherever the helper lives) named `Phase0_Bundle_Integration` that:
  - Instantiates `UtilityTestWorld`.
  - Spawns a multi-mount agent (3 mounts) via `SpawnAgent` + two `SpawnWeaponMount` calls.
  - Spawns a leader and a squad of 3 members.
  - Writes assignments through `Blackboard1024.Project<ThreatMatrixAssignmentState>`.
  - Adds 16 contacts to the leader's `TargetMemory`.
  - Reads back state through each new API.

**Constraints:**
- No reliance on Phase-1 utility scorer; this is a pure infrastructure test.
- The test fails noisily if any of the six P0 items isn't in place.

**Success Conditions:**

SC-P0-07-1: The test passes green.

SC-P0-07-2: All 16 `TargetMemory` slots are populated; `Count == 16`.

SC-P0-07-3: Each mount's `WeaponState.Ammo` is independently modifiable and observable.

SC-P0-07-4: Reading the leader's `ThreatMatrixAssignmentState` via `Project<T>` returns the values written through the same projection.

---

## Phase 1 — Runtime core + trace buffer

**Goal:** The scoring core, the assignment system, the trace buffer, and the four starter-pack decisions, validated headless against the Phase-0 helper.

### TASK-UAI-P1-01: Scoring core data structures

**Design reference:** `Utility_AI_Design_v1_1.md` §4 (canonical structs), §5 (curves), §8 (storage), §12 (source structure).

**Scope:**
- Create `Fdp.Toolkits/Utility/Core/` with `UtilityConsideration`, `UtilityOption`, `UtilityDecisionDef`, `ResponseCurve`, `ScoringMode`, `InputContext`, `InputParams`, `UtilityConstants` (with `TopN = 16`).
- All structs `[StructLayout(LayoutKind.Sequential)]` unmanaged where applicable.

**NOT included:** Aggregator, scorer, curve evaluation (separate tasks).

**Constraints:**
- Struct sizes documented and unit-asserted (no padding surprises).
- No reflection.

**Success Conditions:**

SC-P1-01-1: Each struct's size is stable across builds (deterministic; assert via `sizeof()` in a unit test).

SC-P1-01-2: `UtilityConstants.TopN == 16`.

SC-P1-01-3: The cap-invariant assertion (`MaxTrackedTargets <= TopN`) is in place and fires if a future raise breaks it.

---

### TASK-UAI-P1-02: Curve evaluation (`Curve.Evaluate`)

**Design reference:** `Utility_AI_Design_v1_1.md` §5 (response curves).

**Scope:**
- Implement `ResponseCurve.Evaluate(float input) -> float` for every `CurveKind`: `Linear`, `InverseLinear`, `Threshold`, `Bell`, `Step`, `Logistic`, `Quadratic`, `InverseQuadratic`, `PiecewiseLinear`.
- Output is clamped to `[0, 1]`. Input domain `[0, 1]`.

**NOT included:** Curve editor widget (Phase 3).

**Constraints:**
- All curves are pure; no allocations.
- `PiecewiseLinear` stores points in a side-table keyed by curve id; lookup is O(log N) over typically ≤ 8 points.

**Success Conditions:**

SC-P1-02-1: For each `CurveKind`, the curve evaluates to known reference values at `input ∈ {0, 0.25, 0.5, 0.75, 1.0}` matching pinned vectors documented in the test.

SC-P1-02-2: Output is always in `[0, 1]` for any input in `[0, 1]` (property test, 10⁴ random samples per kind).

SC-P1-02-3: `Step` and `Threshold` curves output `0` below the threshold and ≥ `0.95` above it.

SC-P1-02-4: `PiecewiseLinear` is monotonic between control points; output exactly equals the y-value at each control x.

---

### TASK-UAI-P1-03: Aggregator (product-with-compensation + sum)

**Design reference:** `Utility_AI_Design_v1_1.md` §4.3, §4.4, §5.4.

**Scope:**
- Implement `Aggregator.Aggregate(Span<float> curveOutputs, Span<float> weights, ScoringMode mode) -> float`.
- `WeightedProduct`: each term is `curve^weight`; final score is `rawProduct + (1 - rawProduct) * (1 - 1/n) * rawProduct`.
- `WeightedSum`: `Σ(w · curve) / Σw`.

**NOT included:** Reader binding, decision dispatch.

**Constraints:**
- No allocations.
- Numerically stable for `n` up to 16 (the largest authored consideration count we expect).

**Success Conditions:**

SC-P1-03-1: A single consideration with `curve = 0.5` and `weight = 1.0` yields score `0.5` in both modes.

SC-P1-03-2: Two product-mode considerations `(0.5, w=1)` and `(0.5, w=1)`: raw product = 0.25; modificationFactor = 0.5; makeUp = 0.375; finalScore = 0.25 + 0.375 * 0.25 ≈ 0.34375.

SC-P1-03-3: Three weighted-sum considerations `(0.6, w=1), (0.4, w=2), (0.0, w=1)` produce `(0.6 + 0.8 + 0) / 4 = 0.35`.

SC-P1-03-4: A product-mode option with any consideration whose `curve(input) == 0` produces score 0 (hard-gate property).

---

### TASK-UAI-P1-04: `UtilityResultBuffer` and trace buffer

**Design reference:** `Utility_AI_Design_v1_1.md` §8 (storage), §9 (trace).

**Scope:**
- `UtilityResultBuffer` with `[InlineArray(16)]` Top-N entries (candidate handle + final score + winning posture).
- `UtilityTraceWorkingMemory1024` (per-entity ring buffer; mirrors `BTreeTraceWorkingMemory1024` / `HsmTraceWorkingMemory1024` shape).
- `UtilityDebugFlags` component (gates trace emission, like `BehaviorDebugFlags`).
- Both buffer types use the **span-cast write discipline** documented in `EqsCognitiveBuffer.GetSpanRW()` and architecture §8.2.

**NOT included:** Overlays / editor preview consumers (Phase 4 / 5).

**Constraints:**
- Buffer types unmanaged; no managed fields.
- Trace emission is zero-cost when `UtilityDebugFlags.TraceEnabled == false` (single flag check).

**Success Conditions:**

SC-P1-04-1: A write to `UtilityResultBuffer[i]` via `GetSpanRW()` is observable on read via `GetSpanRO()`; a direct indexer write (`buf.Results[i] = …`) is **silently lost** — assert this trap is documented in the type's comment and a test in the suite proves the trap (regression guard).

SC-P1-04-2: With `TraceEnabled = false`, a scoring pass writes zero entries to `UtilityTraceWorkingMemory1024`.

SC-P1-04-3: With `TraceEnabled = true`, a scoring pass writes one trace entry per (option × consideration), plus a header for the winner + runner-up margin.

---

### TASK-UAI-P1-05: `UtilityScorer` core tick path

**Design reference:** `Utility_AI_Design_v1_1.md` §3, §4, §6.

**Scope:**
- `UtilityScorer.Evaluate(repo, self, decisionId, target?)` — runs the scoring core for one decision on one entity, populates `UtilityResultBuffer` and (if tracing) `UtilityTraceWorkingMemory1024`.
- `UtilityScorer.SelectPosture(repo, self, decisionId)` — convenience for `PostureSelect` decisions, returns the winning `OptionId` directly.
- Per-decision hysteresis bonus applied for `PostureSelect` (architecture §4.5).
- Reader dispatch via the source-gen registrar (a stub registrar is acceptable in Phase 1; the real one ships in Phase 2).

**NOT included:** Source generator (Phase 2).

**Constraints:**
- No allocations on the hot path.
- Reader dispatch is via unmanaged function pointer (matches HSM kernel pattern).
- Re-score cadence is per-tick by default; a follow-on slice may decimate (architecture Q-4 deferred).

**Success Conditions:**

SC-P1-05-1: Evaluating a decision with three product-mode options where one has a gating curve outputting 0 produces 0 final score for that option.

SC-P1-05-2: The winning option's score is at index 0 of `UtilityResultBuffer.Results` after `Evaluate`; the runner-up margin equals `Results[0].Score - Results[1].Score` and is recorded in the trace.

SC-P1-05-3: Hysteresis: an active posture with hysteresis bonus 0.08 holds when the marginal alternative scores within 0.08; switches when the alternative exceeds the active + 0.08.

SC-P1-05-4: A `ThreatRanking` evaluation over 16 contacts returns 16 ranked results sorted descending by score.

---

### TASK-UAI-P1-06: Standard input readers (catalog)

**Design reference:** `Utility_AI_Design_v1_1.md` §6 (real-component mapping table).

**Scope:**
- Implement readers in `Fdp.Toolkits/Utility/Inputs/StandardInputs.cs`, each annotated `[UtilityInput(Name = "…")]`:
  - `AmmoFraction`, `WeaponHasAmmo`, `WeaponReadiness` (read `WeaponState`)
  - `HealthFraction`, `ContactHealthFraction` (read `Health`)
  - `DistanceToContext` (`Position` vs. stored target position)
  - `ContactThreatLevel` (`TargetMemory.ThreatScores[i]`)
  - `HasLineOfSight` (`TargetMemory.Modalities[i] & Visual`)
  - `IsAssignedTarget` (commander projection via `UnitSubordinate.Commander` → `Blackboard1024.Project<ThreatMatrixAssignmentState>`)
  - `EqsTopScore`, `EqsResultCount` (resolve child EQS sensor by `BlueprintId`)
  - `EnemyStrengthRatio` (derived: sum of `TargetMemory.ThreatScores` vs. own)
  - `HaveLiveTarget`, `AllyAdvancingNearby`, `Constant` (architecture §11.1 starter-pack inputs)
  - `WeaponRangeBandFit`, `WeaponEffectivenessVsTarget` (per-mount, read `WeaponMountInfo.EffectiveRange`)
- Sensor-child resolution helper: `UtilityInputCtx.TryFindEqsChild(owner, blueprintId, out child)` — single linear pass over child entities, with a per-`UtilityResultBuffer` slot cache (architecture §6.6).

**NOT included:** Source-gen registrar (Phase 2); Custom propertyPath reader (deferred per architecture §6.5).

**Constraints:**
- Every reader returns a value in `[0, 1]` (or clamps).
- No allocations.
- Readers are `static`, signature `(in UtilityInputCtx ctx) -> float`.

**Success Conditions:**

SC-P1-06-1: `AmmoFraction` returns `Ammo / MaxAmmo`, clamped, returning `0` if `MaxAmmo == 0`.

SC-P1-06-2: `HasLineOfSight` returns `1` iff `TargetMemory.Modalities[i] & (byte)SensorModality.Visual != 0`, else `0` — backed by a unit test with all four combinations of (Visual set / unset) × (Acoustic set / unset).

SC-P1-06-3: `EqsTopScore` resolves the child whose `EqsSensor.BlueprintId` matches; returns `GetTop().Score` if `IsReady`, else `0`.

SC-P1-06-4: `DistanceToContext` clamps to `[0, 1]` based on a reader-internal max range constant (e.g. 1000 m → 0; 0 m → 1).

SC-P1-06-5: `IsAssignedTarget` returns `1` iff the commander's projected `ThreatMatrixAssignmentState` slot for `self` equals `ctx.Candidate`.

---

### TASK-UAI-P1-07: `ThreatMatrixAssignmentSystem` (squad greedy assignment)

**Design reference:** `Utility_AI_Design_v1_1.md` §10 (group fire coordination).

**Scope:**
- Implement the leader's greedy assignment pass over `(member × target)` using the same scoring core.
- Reads `UnitRoster` (members) + leader's `TargetMemory` (targets); writes per-member assignments into `Blackboard1024.Project<ThreatMatrixAssignmentState>`.
- Focus-fire bias: damp targets once `k` shooters are already on them; a target whose predicted incoming exceeds its survivability is "consumed."

**NOT included:** Network replication of assignment state (intentional; assignments are consideration inputs, not orders).

**Constraints:**
- O(n·m log(n·m)) for n=m=16 is well within budget.
- The system runs on the leader entity only; members read their own slot.

**Success Conditions:**

SC-P1-07-1: A squad of 3 members and 2 targets (one soft, one heavy) routes the launcher-armed member to the heavy target and the rifles to the soft target.

SC-P1-07-2: With `focusFireCap = 2`, no target receives more than 2 assigned shooters.

SC-P1-07-3: A near-death member's posture decision (run separately) selects `Flee` despite an assignment — the veto path, asserted in the starter-pack `LeaderAssignmentTests.Wounded_Member_Vetoes_Assignment_And_Breaks_Off`.

---

### TASK-UAI-P1-09: Integration nodes (BTree / HSM / Blueprint)

**Design reference:** `Utility_AI_Design_v1_1.md` §7.

**Scope:**
- `UtilitySelectorNode` (BTree): a smarter `Selector` that scores each child's attached consideration set and ticks the highest-scoring child. Re-scores on a configurable cadence; integrates with `ObserverSelector` semantics so a higher-scoring option can abort a running lower-scoring branch.
- `UtilityTransitionArbiter` (HSM): an `[HsmGuard]`-shaped arbiter bound to a `UtilityDecisionDef`; returns true when the guarded state has the highest utility among the candidate set.
- `ScoreDecisionNode` (Blueprint): runs a named `UtilityDecisionDef`, outputs the winning `OptionId` or the Top-N handle for candidate kinds.
- `ReadRankedResultNode` (Blueprint): reads rank `i` of a candidate decision (entity/weapon + score), paralleling `ReadEqsResultNode`.

**NOT included:** Authoring-side UI for these nodes (lives in the existing BTree/HSM/Blueprint editors; small enum-addition touches).

**Constraints:**
- No allocations on the hot path.
- The BTree node honors `UtilityDecisionDef.HysteresisBonus` for the active child.
- Blueprint nodes write into per-entity `UtilityResultBuffer` (synchronous; no async state machine).

**Success Conditions:**

SC-P1-09-1: `UtilitySelectorNode` with three child branches selects the branch whose decision option has the highest final score; switching is suppressed within the hysteresis window.

SC-P1-09-2: `UtilityTransitionArbiter.Evaluate` returns `true` for the guarded state iff that state's option won the latest decision evaluation.

SC-P1-09-3: `ScoreDecisionNode` invoked in a Blueprint produces a winning `OptionId` matching a direct `UtilityScorer.SelectPosture` call against the same fixture.

SC-P1-09-4: `ReadRankedResultNode` with `rank = 0` returns the same handle as `UtilityResultBuffer.Top()`.

---

### TASK-UAI-P1-08: Starter-pack decisions + integration tests

**Design reference:** `Utility_AI_StarterPack_Examples_v1_1.md` §1–§5.

**Scope:**
- `ThreatRankingDecision`, `WeaponSelectionDecision`, `CombatPostureDecision`, `LeaderAssignmentDecision` — each authored as a `[UtilityDecision]` C# class with `static void Build(IUtilityDecisionBuilder b)`.
- The five integration tests (starter pack §1.2, §2.2, §3.2, §4.3, §5).

**Constraints:**
- Definitions use only catalog inputs from TASK-UAI-P1-06.
- Tests use `UtilityTestWorld` from Phase 0.

**Success Conditions:**

SC-P1-08-1: All starter-pack tests pass green.

SC-P1-08-2: The trace test (§5) reads back per-consideration breakdown for the winning option through `UtilityTraceWorkingMemory1024`.

SC-P1-08-3: Hysteresis test passes (a 1% input nudge does not flip the active posture).

SC-P1-08-4: The wounded-member veto test passes (assignment present, posture selects Flee).

---

## Phase 2 — Source generator + analyzer

**Goal:** `In.*` accessors, `UtilityInputRegistrar.g.cs`, `UtilityDecisionCatalog.g.cs`, and the `UT####` diagnostics.

### TASK-UAI-P2-01: `UtilityInputGenerator`

**Design reference:** `Utility_AI_SourceGenerator_Design_v1_1.md` §3.

**Scope:** As specified in §3.1–§3.5: incremental generator, recognized shape, emitted registrar, `In.*` partials, FNV-1a hash with 16-bit truncation matching `BTreeActionGenerator.ComputeHash`.

**Success Conditions:**

SC-P2-01-1: A test compilation with three `[UtilityInput]` methods produces `UtilityInputRegistrar.g.cs` containing three registration calls, plus `UtilityInputAccessors.g.cs` containing three `In.<Name>` accessors.

SC-P2-01-2: **Hash parity** — the gen-time hash equals a runtime reference implementation (32-bit FNV-1a, low 16 bits) for a pinned battery of names (e.g. `"AmmoFraction" → 0xA3F1` documented in the test).

SC-P2-01-3: Two reader names that collide on the 16-bit hash trigger `UT0103` at the second definition.

SC-P2-01-4: A non-`static`, non-`float`, or wrong-signature `[UtilityInput]` method triggers `UT0110`/`UT0111`/`UT0112` respectively and is omitted from the registrar.

---

### TASK-UAI-P2-02: `UtilityDecisionGenerator`

**Design reference:** `Utility_AI_SourceGenerator_Design_v1_1.md` §4.

**Scope:** Runtime-build accessor (Option A) plus best-effort gen-time manifest (Option B fallback). Per-decision `.Id` partial-class constant emitted.

**Success Conditions:**

SC-P2-02-1: A `[UtilityDecision]` class produces a catalog entry whose `builder` calls the class's `Build` method.

SC-P2-02-2: `CombatPostureDecision.Id` exists as a `const int` equal to FNV-1a-32 of the `AssetId` GUID.

SC-P2-02-3: A `Build` body using only `.Option(...).Consider(...)` produces a `full` manifest entry (all options + considerations extracted).

SC-P2-02-4: A `Build` body using a `foreach` loop produces a `partial` manifest entry (the editor falls back to runtime reflection).

---

### TASK-UAI-P2-03: `UtilityAuthoringAnalyzer`

**Design reference:** `Utility_AI_SourceGenerator_Design_v1_1.md` §6.

**Scope:** All `UT####` diagnostics in §6 table.

**Success Conditions:**

SC-P2-03-1: One xUnit fixture per `UT####` that should trip it; the diagnostic is reported at the expected location. (One fixture per row of the §6 table.)

SC-P2-03-2: `UT0130` purity check fires when `Build` reads `EntityRepository`, `DateTime.Now`, or a static mutable field; stays silent for a clean `Build`. Pattern copies `EqsTemplatePurityAnalyzer` (`EQS_002`).

SC-P2-03-3: `UT0120` (unknown input) resolves across referenced assemblies via `context.Compilation.GlobalNamespace`; a custom `[UtilityInput]` in an upstream project is recognized as valid.

---

### TASK-UAI-P2-04: Startup handshake

**Design reference:** `Utility_AI_SourceGenerator_Design_v1_1.md` §5.

**Scope:** `UtilityAutoDiscovery.ScanAndRegister(out registry)` — one-time reflective scan for `[UtilityRegistrar]` types at simulation startup; results cached.

**Success Conditions:**

SC-P2-04-1: `ScanAndRegister` finds all `[UtilityRegistrar]`-attributed types in the loaded assemblies and invokes their `RegisterAll`.

SC-P2-04-2: Subsequent simulation ticks do not perform any reflection (assert by failing on any reflection call after `ScanAndRegister` returns).

---

## Phase 3 — Standalone curve widget

### TASK-UAI-P3-01: `CurveWidget.Draw` host-agnostic widget

**Design reference:** `Curve_Editor_in_StructEdit_Guide_v1_1.md` §3 (Step 2); `Utility_AI_Editor_Design_v1_2.md` §5.

**Scope:**
- `public static bool CurveWidget.Draw(string id, ref UtilityCurve curve, in CurveWidgetOptions opts)` in a shared UI assembly referenced by both the Utility Editor and the tuning console.
- `UtilityCurve` struct in `Fdp.Toolkit.Behavior` (architecture §5.3 fields).
- Plot evaluates the **runtime curve function** (no preview-math drift).
- Locked params per `CurveKind` shown disabled (Editor DD E-2).

**Success Conditions:**

SC-P3-01-1: Dragging a slope handle updates the `m` field bidirectionally (handle ↔ numeric field).

SC-P3-01-2: The plotted output equals `ResponseCurve.Evaluate(input)` from TASK-UAI-P1-02 at 16 sample points across `[0, 1]` (no separate preview math).

SC-P3-01-3: Switching `CurveKind` from `Linear` to `Step` greys (does not hide) `m` and `k` fields; layout does not reflow.

SC-P3-01-4: `PiecewiseLinear` points stay x-sorted after every edit; output clamped to `[0, 1]`.

---

## Phase 4 — AI overlays + tuning console Slice 1

### TASK-UAI-P4-01: `AiOverlayFlags` + per-entity gating

**Design reference:** `Runtime_Tuning_Console_and_AI_Overlays_Design_v1_0.md` §6.

**Scope:** Extend `BehaviorDebugFlags` family with `AiOverlayFlags`; reuse `TraceBufferLifecycleSystem` lifecycle.

**Success Conditions:**

SC-P4-01-1: An entity without `AiOverlayFlags` emits zero overlay primitives from any AI overlay source.

SC-P4-01-2: Setting `AiOverlayFlags.UtilityDecision` on an entity makes the next frame emit a `StructInspector`-anchored overlay primitive for that entity.

---

### TASK-UAI-P4-02: Five overlay sources

**Design reference:** `Runtime_Tuning_Console_and_AI_Overlays_Design_v1_0.md` §7.

**Scope:**
- `PerceptionOverlaySource`, `TargetMemoryOverlaySource`, `EqsOverlaySource`, `UtilityDecisionOverlaySource`, `SquadAssignmentOverlaySource`.
- Layer-masked via existing `LayerControlGizmo` (each on a distinct `LayerMask256` layer).
- `OverlayBudgetArbiter` honors `GlobalDebugSettings.MaxGizmoFrameMs`; shed-priority order documented in §6.2.

**Success Conditions:**

SC-P4-02-1: `UtilityDecisionOverlaySource` reads from `UtilityTraceWorkingMemory1024` and emits a single anchored multi-line label per flagged entity, matching the wireframes §2 layout.

SC-P4-02-2: Over the per-frame budget, `OverlayBudgetArbiter` sheds the lowest-priority active family before the highest.

SC-P4-02-3: Squad-assignment overlay draws solid line to assigned target and dashed line to actually-engaged target; lines diverge when the member has vetoed (test: wounded-member scenario from starter pack §4.3 produces visibly diverging lines).

---

### TASK-UAI-P4-03: `TuningRegistry` + `TuningConsoleGizmo` (Slice 1, scalars)

**Design reference:** `Runtime_Tuning_Console_and_AI_Overlays_Design_v1_0.md` §3, §4, §5.

**Scope:**
- `TuningRegistry`, `Tunable`, `[Tunable]` attribute (source-gen-discovered in a follow-on; manual registration acceptable in Slice 1).
- `UtilityTuningBinder` auto-registers every loaded `UtilityDecisionDef`'s weights / curve params / hysteresis as tunables with dotted names.
- `TuningConsoleGizmo` as a generalized `LayerControlGizmo` (StructInspector-backed, `OnStructUpdate(json)` apply path).
- Frame-top apply via enqueue/drain; `TuningChangeEvent` recorded to Flight Recorder.
- Curves expanded to 4 scalar fields (`m, k, b, c`) in this slice; the visual widget arrives in Phase 6.

**Success Conditions:**

SC-P4-03-1: Out-of-range commits are clamped to `[Min, Max]` and surfaced as console warnings.

SC-P4-03-2: A commit is enqueued and applied at frame top, never mid-tick (assert with a probe system that reads the tunable mid-frame).

SC-P4-03-3: **Replay honesty:** recording a session with a mid-run tuning change and replaying it re-applies the change at the same wall-tick; post-change frames are bit-identical.

SC-P4-03-4: A Brain-owned tunable edited from ExCon (DDS mode) reaches the Brain via `GizmoInteractionBatch` and applies.

SC-P4-03-5: A Muscle-owned tunable edited from ExCon forwards to Muscle (owner routing).

---

## Phase 5 — Utility editor (card-table)

### TASK-UAI-P5-01: `UtilityDecisionAsset` model + `ManagedWindow` host

**Design reference:** `Utility_AI_Editor_Design_v1_2.md` §3, §4.

### TASK-UAI-P5-02: Input catalog browser + curve inspector (calls TASK-UAI-P3-01)

**Design reference:** `Utility_AI_Editor_Design_v1_2.md` §5, §6.

### TASK-UAI-P5-03: Live preview + in-editor debug (reads Phase-1 trace, throttled 10 Hz)

**Design reference:** `Utility_AI_Editor_Design_v1_2.md` §7, §9.

### TASK-UAI-P5-04: `UtilityFluentEmitter` (lossless round-trip)

**Design reference:** `Utility_AI_Editor_Design_v1_2.md` §8.

### TASK-UAI-P5-05: Comparison integration (sanitizer + tuning-diff fast lane)

**Design reference:** `Utility_AI_Editor_Design_v1_2.md` §10; `Utility_AI_Editor_Wireframes.md` §7.

### TASK-UAI-P5-06: Shared-infra extensions (4 small touches)

**Design reference:** `Utility_AI_Editor_Design_v1_2.md` §11.

**Phase-5 consolidated success conditions:**

SC-P5-1: Emitter round-trip: model → emit → Roslyn-parse → reflect → structural equality. Byte-stable emit → re-emit.

SC-P5-2: Live preview's per-consideration numbers equal a direct `UtilityScorer` run on the same fixture (no drift).

SC-P5-3: Hot-reload classification correct (Cosmetic / Soft / Hard per Editor DD §8.5).

SC-P5-4: Partial-manifest decisions open read-only with a banner; emit path unreachable for them.

SC-P5-5: `IRefactorService` rename across `[UtilityInput]` updates all `.Consider(In.…)` references.

SC-P5-6: Structure-equal versions trigger the tuning-diff fast lane (no LLM call); structure-differ versions produce a sanitized export.

---

## Phase 6 — Tuning console Slice 2 + bridge + polish

### TASK-UAI-P6-01: `UtilityCurveFieldEditor` + `UtilityCurveFieldDrawer`

**Design reference:** `Curve_Editor_in_StructEdit_Guide_v1_1.md` §3 (Steps 3–5).

### TASK-UAI-P6-02: Piecewise translate-on-apply

**Design reference:** `Curve_Editor_in_StructEdit_Guide_v1_1.md` §6.

### TASK-UAI-P6-03: Editor ↔ console bridge

**Design reference:** `Runtime_Tuning_Console_and_AI_Overlays_Design_v1_0.md` §8.2.

### TASK-UAI-P6-04: Snapshot / restore (revert group / revert all)

**Design reference:** `Runtime_Tuning_Console_and_AI_Overlays_Design_v1_0.md` §11 T-5.

**Phase-6 consolidated success conditions:**

SC-P6-1: Console curve edits produce JSON byte-identical to scalar edits of the same curve.

SC-P6-2: `PiecewiseLinear` round-trips through translate-on-apply: managed `DynamicArray` edit → fixed-size buffer at frame-top apply → readback equals input (clamping at buffer capacity surfaced as a console warning, not silent).

SC-P6-3: Clicking a decision name in the utility overlay opens the tuning console focused on that decision's group.

SC-P6-4: "Revert group" restores authored defaults captured at registration; per-entity overrides clear on entity destroy.

---

*End of TASK-DETAIL. Phases 1–6 expand by reference to the design docs; Phase 0 is fully self-contained here because it touches code outside the Utility layer.*
