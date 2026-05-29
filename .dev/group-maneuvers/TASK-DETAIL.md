# Squad Coordination — Task Detail

**Reference:** See the design doc set for full context:
- [`Squad_Coordination_Design_v1_1.md`](./Squad_Coordination_Design_v1_1.md) — architecture
- [`Step_1_5_TargetMemory_3D_Reconciliation.md`](./Step_1_5_TargetMemory_3D_Reconciliation.md) — pre-step (must be merged green)
- [`../utility-ai/Utility_AI_Design_v1_1.md`](../utility-ai/Utility_AI_Design_v1_1.md) — the Utility core that role/fire/slot/maneuver assignment reuses
- [`../utility-ai/Utility_AI_Editor_Design_v1_2.md`](../utility-ai/Utility_AI_Editor_Design_v1_2.md) — visual authoring for `ManeuverSelect`
- [`../utility-ai/Runtime_Tuning_Console_and_AI_Overlays_Design_v1_0.md`](../utility-ai/Runtime_Tuning_Console_and_AI_Overlays_Design_v1_0.md) — overlays (`SquadAssignmentOverlaySource`)
- [`../utility-ai/Curve_Editor_in_StructEdit_Guide_v1_1.md`](../utility-ai/Curve_Editor_in_StructEdit_Guide_v1_1.md) — used by the editor for `ManeuverSelect` curves

**Assumed-completed dependencies** (must be merged green before Phase 0 of this workstream starts):
- All six Utility AI phases (P0–P6, see [../utility-ai/TASK-TRACKER.md](../utility-ai/TASK-TRACKER.md)) — currently P0–P2 are landed; P3–P6 land before squad work begins.
- The **3D Cognitive Spatial Awareness Promotion** — `TargetMemory`, `EqsResult`, and the EQS cover query are 3D-native. `EqsResult` is 32 B (aligned, `PositionZ` field).
- [`Step_1_5_TargetMemory_3D_Reconciliation.md`](./Step_1_5_TargetMemory_3D_Reconciliation.md) — Utility readers consume the 3D `TargetMemory`.
- Navmesh tactical-feature extraction is **not yet** in plan; the squad layer ships against a `FakeDangerAreaProvider` (Phase 2 below).

**Debt:** [DEBT-TRACKER.md](./DEBT-TRACKER.md)

Each task below has a unique id and explicit success conditions (usually unit-test specs). Tasks reference design-doc sections instead of duplicating content.

Convention: task ids `SQD-PNN-MM` where `PN` is the phase and `MM` is the in-phase index.

---

## Phase 0 — Prerequisites & state layout

**Goal:** Land the up-front layout reconciliation, the `ManeuverSelect` decision-kind extension, and the fake-provider scaffolding so Phase 1 code compiles against real APIs.

### TASK-SQD-P0-01: Shrink `AssignmentSlot` and migrate `ThreatMatrixAssignmentSystem`

**Design reference:** `Squad_Coordination_Design_v1_1.md` §3.1; user-clarified BB-layout decision (combination of options 2 + 3). Existing structs: [`ThreatMatrixAssignmentState.cs`](../../FDP/Toolkits/Fdp.Toolkits/Utility/Group/ThreatMatrixAssignmentState.cs), [`ThreatMatrixAssignmentSystem.cs`](../../FDP/Toolkits/Fdp.Toolkits/Utility/Group/ThreatMatrixAssignmentSystem.cs).

**Scope:**
- Shrink `AssignmentSlot` from `Size = 64` to a packed 16-byte layout: `long AssignedTargetHandle (8)` + `float AssignmentScore (4)` + `byte FocusFireCount (1)` + `byte Flags (1)` + `ushort _pad (2)`. Verify `sizeof(AssignmentSlot) == 16`.
- `AssignmentSlotArray` stays `[InlineArray(16)]`. Total assignment footprint: 16 × 16 = **256 bytes**.
- Update `ThreatMatrixAssignmentSystem` to use the new layout. `GetSlot`/`GetAssignedTarget`/`SetAssignment` signatures unchanged.
- `ThreatMatrixAssignmentState.Project(ref Blackboard1024)` is **removed** in Phase 0 — the standalone projection is replaced by an embedded sub-region inside `SquadCognitiveState` (P0-02). Migrate every call site (production + tests) to read `SquadCognitiveState.Project(ref bb).Assignment` instead.
- The Phase-1.07 invariant — non-truncating threat ranking over a 16-member roster — still holds (assignment array length unchanged).

**NOT included:** Other `SquadCognitiveState` sub-regions (P0-02). Manoeuvre-selection wiring (Phase 3).

**Constraints:**
- The shrink must be **binary-compatible at the call-site level**: code that already calls `GetSlot(i).AssignedTargetHandle = …` keeps compiling. Only the internal layout changes.
- Migration is a single atomic PR — no intermediate state where some systems read the old 64 B and others read the new 16 B.

**Success Conditions:**

SC-P0-01-1: `sizeof(AssignmentSlot) == 16`.

SC-P0-01-2: `sizeof(AssignmentSlotArray) == 256` (16 × 16).

SC-P0-01-3: All existing `ThreatMatrixAssignmentSystem` and `LeaderAssignmentDecision` tests pass with no behavior change (focus-fire bias, greedy order, member-veto consideration read).

SC-P0-01-4: A round-trip test writes `AssignmentScore = 0.42f`, `FocusFireCount = 3`, `Flags = 0x05` to slot 7, reads them back — values byte-exact, no aliasing with adjacent slots.

SC-P0-01-5: No production call site references the removed `ThreatMatrixAssignmentState.Project(ref bb)` after the migration; the symbol is deleted (or kept as `[Obsolete(error: true)]` for one PR cycle, then deleted).

---

### TASK-SQD-P0-02: `SquadCognitiveState` — single contiguous projection

**Design reference:** `Squad_Coordination_Design_v1_1.md` §3.1; user-clarified upfront-layout decision.

**Scope:**
- New file `FDP/Toolkits/Fdp.Toolkits/Squad/State/SquadCognitiveState.cs`.
- One `[StructLayout(LayoutKind.Sequential)]` struct holding **every** squad working-state field upfront, in this declaration order (offsets locked by the test in SC-P0-02-2):

  ```csharp
  public struct SquadCognitiveState
  {
      // --- maneuver scalars (16 B) ---
      public ushort ManeuverKind;        // catalog entry (§8); 0 = none
      public ushort PhaseId;             // squad-HSM phase
      public uint   ActiveFeatureId;     // FNV-1a of navmesh polygon id (§5.2)
      public uint   PhaseEnteredTick;
      public uint   Flags;               // bit 0: missionOverride; bits 1..15 reserved

      // --- element partition (32 B) ---
      public ElementPartition Elements;  // see below

      // --- slot assignment (96 B) ---
      public SlotAssignmentArray Slots;  // element→slot index + per-slot rotation/burn

      // --- role assignment (32 B) ---
      public RoleAssignmentArray Roles;  // member→role-id + assignment score

      // --- fire/threat assignment (256 B, migrated from §3.1 / P0-01) ---
      public AssignmentSlotArray Assignment;

      // --- shared-awareness sub-region — contact pool (rest, ≤ 592 B) ---
      public SquadContactPool Contacts;
  }
  ```

  Sub-struct sizes (locked in this task):
  - `ElementPartition`: `[InlineArray(16)] byte MemberElementIndex` + `uint LastRepartitionTick` + 12 B pad = 32 B.
  - `SlotAssignmentArray`: `[InlineArray(16)] SlotState` where each `SlotState = { byte ElementIndex, byte SlotKind, ushort Flags, uint LastTransitionTick } = 8 B` → 8 × 12 = 96 B (12 slots; remainder unused at smaller squad sizes).
  - `RoleAssignmentArray`: `[InlineArray(16)] RoleSlot` where each `RoleSlot = { byte RoleId, byte _pad, ushort Reserved } = 4 B`? Re-check: 4 × 16 = 64 B. **Final RoleSlot uses 2 B (RoleId, _pad)** so total = 32 B.
  - `AssignmentSlotArray`: 256 B (from P0-01).
  - `SquadContactPool`: occupies the remaining budget. Size is `1024 - (16 + 32 + 96 + 32 + 256) = 592 B`. At 32 B per pooled contact (entity id 8 + posXYZ 12 + threat 4 + lastSeenTick 4 + sourceMembersMask 2 + flags 2) the pool fits 18 contacts. Cap: **16 contacts** (matches `PerceptionConstants.MaxTrackedTargets`, leaves 80 B headroom for future).

- Add `SquadCognitiveState.Project(ref Blackboard1024 bb)` using `Blackboard1024.Project<T>` (the Utility P0.5 helper).
- Component-id allocation: claim the next free id in `Fdp.Core/GlobalComponentIds.cs` for a sibling marker component `SquadStateMarker` (a zero-byte tag attached to commander entities) so query filters can find squad-state-bearing entities cheaply. **`SquadCognitiveState` itself is NOT a separate component** — it projects onto the existing `Blackboard1024` on commander entities.

**NOT included:** Maneuver primitives (Phase 1); perception merge (Phase 2); danger-area sensor (Phase 2).

**Constraints:**
- Total `sizeof(SquadCognitiveState)` MUST be `<= Blackboard1024.ByteSize` (1024) — assertion at registration.
- All fields are unmanaged value types; no `fixed` members other than via `[InlineArray]`.
- Apply the `[InlineArray]` defensive-copy rule (Utility §8.2): every write path casts to `Span<T>` first.
- `[DataPolicy.NoSave]` — squad cognitive state is transient.

**Success Conditions:**

SC-P0-02-1: `sizeof(SquadCognitiveState) <= 1024`.

SC-P0-02-2: A pinned offset-table test asserts: `Elements@16`, `Slots@48`, `Roles@144`, `Assignment@176`, `Contacts@432` (or whatever the final compile-time offsets are — pin them and fail loudly if anyone reorders fields).

SC-P0-02-3: `SquadCognitiveState.Project(ref bb)` and `ref var s = ref Blackboard1024.Project<SquadCognitiveState>(ref bb)` alias the same memory (write through one, read through the other).

SC-P0-02-4: `default(SquadCognitiveState)` zero-initializes; `ManeuverKind == 0` and `Assignment.GetSlot(0).AssignedTargetHandle == 0`.

SC-P0-02-5: An offset-collision diagnostic at commander-entity registration: if another system has claimed any byte of `Blackboard1024` on a commander entity, assert + log the colliding type name. (See Utility §10.1 collision-check; adapt for squad layer.)

---

### TASK-SQD-P0-03: Add `ManeuverSelect` to `DecisionKind` + source-gen support

**Design reference:** `Squad_Coordination_Design_v1_1.md` §8.0; `Utility_AI_Design_v1_1.md` §4.2 (`DecisionKind`).

**Scope:**
- Extend `DecisionKind` enum in [`UtilityCore.cs`](../../FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityCore.cs): add `ManeuverSelect = 3`.
- `UtilityDecisionGenerator` (Phase-2 source-gen, [`UtilityDecisionGenerator`](../../FDP/Toolkits/Fdp.Toolkits.Analyzers/)) accepts `Kind = ManeuverSelect` like `PostureSelect` (fixed-set selector). The generated registrar entry carries the kind discriminator.
- `UtilityAuthoringAnalyzer` ([`UtilityAuthoringAnalyzer`](../../FDP/Toolkits/Fdp.Toolkits.Analyzers/)) handles the new kind for UT0101+ diagnostics. UT0151 (new): a `ManeuverSelect` decision MUST scope its `Context` to `Self` (the commander); using `Candidate` (no candidate set) or `Target` (no per-target binding) emits the new diagnostic.
- Utility editor (Phase-5 of the Utility AI workstream): treat `ManeuverSelect` as another fixed-set decision; the existing card-table renders it. No bespoke editor surface for this Phase-0.

**NOT included:** Any actual `ManeuverSelectDecision` definition (Phase 3). Mission-override wiring (Phase 3).

**Constraints:**
- Source-gen output (`UtilityDecisionCatalog.g.cs`) is byte-identical for all pre-existing decisions — additive only.
- Analyzer: zero false positives on the existing starter pack.

**Success Conditions:**

SC-P0-03-1: `DecisionKind.ManeuverSelect == 3`; values `ThreatRanking=0, WeaponSelection=1, PostureSelect=2` unchanged (binary-stable).

SC-P0-03-2: A trivial test `[UtilityDecision(Kind = DecisionKind.ManeuverSelect)] public sealed class TestManeuver : IUtilityDecisionDefinition { … }` compiles and lands in the generated catalog with `Kind == ManeuverSelect`.

SC-P0-03-3: UT0151 fires on a `ManeuverSelect` decision whose options use `Ctx.Candidate` or `Ctx.Target`.

SC-P0-03-4: Pre-existing starter-pack tests are green (no analyzer regression on `ThreatRanking` / `WeaponSelection` / `PostureSelect`).

---

### TASK-SQD-P0-04: `FakeDangerAreaProvider` scaffolding

**Design reference:** `Squad_Coordination_Design_v1_1.md` §5, §11. Precedent: [`FakeNavmeshProvider`](../../FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/FakeNavmeshProvider.cs).

**Scope:**
- New folder `FDP/Toolkits/Fdp.Toolkits/Squad/DangerArea/Fake/`.
- `DangerAreaDescriptor` struct (matches design §5.2 exactly): `FeatureId`, `ThreatRating`, `Kind`, `Center (Vector3)`, `ExtentsXY (Vector2)`, `AngleRad`, `ZFloor`, `ZCeiling`, `NearSideHandle (Vector3)`, `FarSideHandle (Vector3)`. Verify final `sizeof` and pin it (expected ≈ 72 B with natural alignment).
- `IDangerAreaProvider` interface: `void Refresh(EntityRepository repo, Entity squadCommander, Span<DangerAreaDescriptor> dest, out int count)`.
- `FakeDangerAreaProvider` impl: holds a `List<DangerAreaDescriptor>` populated from a fluent helper `FakeDangerAreaProvider.Builder.AddStreetCrossing(near, far, …)` / `AddCrestLine(...)` / `AddIntersection(...)` / `AddChokePoint(...)` / `AddOpenGround(...)`. No allocation on `Refresh` (results are copied into the caller's span).
- `DangerAreaKind` enum: `OpenGround = 0, StreetCrossing = 1, Intersection = 2, ChokePoint = 3, CrestLine = 4` (extensible per §5.2).
- `FeatureId` is the FNV-1a-32 of a developer-provided **stable string key** in the fake (the navmesh polygon hash is replaced by an authored id); the real provider (out of scope) will compute it from the navmesh.

**NOT included:** Real navmesh-driven provider (out of scope, blocked on engine plan). `DangerAreaCognitiveBuffer` and the sensor child-entity (Phase 2). EQS cover query reuse (Phase 4).

**Constraints:**
- Zero allocations on `Refresh`.
- Hand-authored descriptors only — no geometry simulation.

**Success Conditions:**

SC-P0-04-1: `sizeof(DangerAreaDescriptor)` matches the pinned constant declared in `DangerAreaDescriptor.cs`.

SC-P0-04-2: A builder authoring 3 features (one StreetCrossing, one CrestLine, one ChokePoint) yields a provider whose `Refresh(...)` writes exactly 3 descriptors with the correct `Kind` and `FeatureId` (stable across runs given identical input keys).

SC-P0-04-3: `FeatureId == Fnv1a32("street-east-01")` for an explicitly named feature (pinned hash).

SC-P0-04-4: 10⁶ `Refresh` calls allocate zero managed bytes (GC tracker).

---

### TASK-SQD-P0-05: Phase-0 integration gate test

**Design reference:** `Squad_Coordination_Design_v1_1.md` §3.1 + this task tracker's Phase-0 exit gate.

**Scope:**
- New test fixture `SquadPhase0IntegrationTests` in `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/` (or wherever the squad tests will live — match precedent set by Utility tests).
- The fixture spawns a commander entity with `UnitRoster (3 members) + Blackboard1024`, projects `SquadCognitiveState`, writes `ManeuverKind = 1`, writes an `AssignmentSlot` for member 0, reads them back from the same `Blackboard1024.Memory` raw bytes. Verifies the layout claim in §3.1 ("single contiguous projection") holds end-to-end.
- A second test instantiates a `FakeDangerAreaProvider`, asks it to populate 4 features into a span, runs them through the (still-stub) Phase-0 catalog API — verifies the descriptors round-trip with their `FeatureId`/`Center`/`ZFloor`/`ZCeiling` intact.
- A third test compiles a `[UtilityDecision(Kind = ManeuverSelect)]` definition (no real readers yet, just a stub) and confirms the generated catalog carries `Kind == ManeuverSelect`.

**NOT included:** Real maneuver authoring (Phase 3), perception merge (Phase 2), sensor lifecycle (Phase 2).

**Success Conditions:**

SC-P0-05-1: All three integration tests pass.

SC-P0-05-2: The pre-existing Utility starter-pack integration tests pass with the shrunk `AssignmentSlot` (no regression).

SC-P0-05-3: `dotnet build` of the whole solution is clean (no analyzer warnings on the new code).

---

## Phase 1 — Primitives library

**Goal:** The five Brain-resident primitives (§2) as a clean Brain API — three of them reuse the existing Utility allocation matrix, two are new. All unit-type-agnostic.

### TASK-SQD-P1-01: Element partition primitive (with hysteresis)

**Design reference:** `Squad_Coordination_Design_v1_1.md` §2 primitive 1, §4.1 hysteresis.

**Scope:**
- New file `FDP/Toolkits/Fdp.Toolkits/Squad/Primitives/ElementPartitionPrimitive.cs`.
- API:
  ```csharp
  public static void Partition(
      ref SquadCognitiveState state,
      ReadOnlySpan<MemberPartitionInput> inputs,   // per-member scores per element kind
      int elementCount,                            // 2 or 3 typically
      float decisiveGap,                           // hysteresis threshold (§4.1)
      out int repartitionsCount);
  ```
- Each call computes the highest-scoring element per member, but a member keeps its current `Elements.MemberElementIndex[i]` unless the new winner's score exceeds the current element's score by `decisiveGap` (anti-flip-flop, the same idea as the posture-selector bonus in Utility §4.5).
- `MemberPartitionInput` carries per-element scores produced by the caller (the caller is the squad HSM or the script doctrine; the primitive doesn't fetch raw state).
- Writes back into `state.Elements`. Bumps `LastRepartitionTick`.

**NOT included:** Element-kind catalog. The numeric scores come from the caller, which is the per-maneuver doctrine.

**Constraints:**
- Zero allocation; pure C# over the SoA in `ElementPartition`.
- Deterministic: same inputs → same partition.
- Hysteresis must be observable in a unit test (member doesn't move under a marginal score swing).

**Success Conditions:**

SC-P1-01-1: A 3-member squad with `inputs` favoring element 0 for member 0 by `+0.5` gets `MemberElementIndex[0] == 0`.

SC-P1-01-2: After a small flip in member 0's scores (element 1 wins by `+0.05` < `decisiveGap = 0.15`), member 0 stays in element 0 (hysteresis holds).

SC-P1-01-3: After a decisive flip (element 1 wins by `+0.30` > `0.15`), member 0 moves to element 1 and `repartitionsCount == 1`.

SC-P1-01-4: 10⁶ `Partition` calls allocate zero managed bytes.

---

### TASK-SQD-P1-02: Tactical-feature reference handles

**Design reference:** `Squad_Coordination_Design_v1_1.md` §2 primitive 2, §5.2 (`FeatureId` tracking).

**Scope:**
- New file `FDP/Toolkits/Fdp.Toolkits/Squad/Primitives/TacticalFeatureRef.cs`.
- API:
  - `TacticalFeatureRef Acquire(ref SquadCognitiveState state, uint featureId)` — sets `state.ActiveFeatureId = featureId`, returns a lightweight ref struct holding `(featureId, descriptorIndex)` resolved from the `DangerAreaCognitiveBuffer` (Phase 2; for now, only `featureId` is stored — the descriptor lookup is added when the buffer lands).
  - `bool TryRefresh(ref SquadCognitiveState state, …, out DangerAreaDescriptor descriptor)` — O(N) scan over the buffer by `FeatureId` (matches §5.2 "exactly as `TargetMemory` tracks contacts"). N is ≤ the descriptor cap (small).

**NOT included:** EQS cover query routing (Phase 4).

**Constraints:**
- Geometry lives in Muscle; this primitive only carries handles.
- `TryRefresh` returns `false` when the buffer is empty or the `FeatureId` was evicted; the caller decides whether to abort the maneuver.

**Success Conditions:**

SC-P1-02-1: `Acquire` writes `state.ActiveFeatureId`; a second call with the same id is idempotent.

SC-P1-02-2: `TryRefresh` against a fake buffer carrying 3 descriptors returns `true` only for matching `FeatureId` values; `false` otherwise.

SC-P1-02-3: Buffer eviction (re-`Refresh` with a different set) loses the old descriptor; `TryRefresh` returns `false` and `state.ActiveFeatureId` is unchanged (caller's responsibility to react).

---

### TASK-SQD-P1-03: Role / slot assignment primitive (allocation-matrix reuse)

**Design reference:** `Squad_Coordination_Design_v1_1.md` §2 primitive 3, §6 (high-weight consideration). Reuses the existing greedy assignment over the Utility scoring core ([`ThreatMatrixAssignmentSystem`](../../FDP/Toolkits/Fdp.Toolkits/Utility/Group/ThreatMatrixAssignmentSystem.cs)).

**Scope:**
- New file `FDP/Toolkits/Fdp.Toolkits/Squad/Primitives/RoleSlotAssignmentPrimitive.cs`.
- API:
  ```csharp
  public static void AssignRoles(
      ref SquadCognitiveState state,
      ReadOnlySpan<RoleSlotCandidate> candidates,
      Span<float> scoreMatrix,                    // [member × candidate] (caller-provided buffer)
      UtilityDecisionDef assignmentDecision);    // a [UtilityDecision] of Kind=ThreatRanking shape, scored per (member,candidate) pair
  ```
- Internally runs the same greedy assignment as `ThreatMatrixAssignmentSystem` (call into it through a small adapter — DO NOT duplicate the matrix logic). The "target" axis is replaced with the role/slot candidate axis; the scoring core is identical.
- Writes back into `state.Roles[memberIndex].RoleId` and (where applicable) `state.Slots[elementIndex]`.
- Re-runs on phase change — the caller invokes this primitive whenever `state.PhaseId` is bumped.

**NOT included:** The decision asset definitions for each role kind (those land per-maneuver in Phase 5).

**Constraints:**
- The score matrix is **caller-allocated** (typically a `stackalloc Span<float>` of size `members * candidates ≤ 256`). Zero allocation in this primitive.
- Greedy assignment is O(n·m log(n·m)); same complexity envelope as the fire-allocation matrix.

**Success Conditions:**

SC-P1-03-1: A 4-member squad assigning roles (`Pointman`, `Suppressor`, `Flanker`, `Sector`) with hand-authored scores produces the same greedy assignment as `ThreatMatrixAssignmentSystem` over the equivalent (member × candidate) matrix.

SC-P1-03-2: Re-running `AssignRoles` after bumping `state.PhaseId` overwrites `state.Roles` with the new assignment.

SC-P1-03-3: Calling with `candidates.Length == 0` is a no-op; `state.Roles` unchanged.

---

### TASK-SQD-P1-04: Phase sequencer with turn-taking (squad-HSM substrate)

**Design reference:** `Squad_Coordination_Design_v1_1.md` §2 primitive 4, §9.

**Scope:**
- New file `FDP/Toolkits/Fdp.Toolkits/Squad/Primitives/PhaseSequencer.cs`.
- API:
  ```csharp
  public static void Advance(
      ref SquadCognitiveState state,
      ReadOnlySpan<PhaseEvent> events,           // completion events arriving this tick
      uint currentTick,
      float dwellTimeoutSeconds);                // fallback when no completion event
  ```
- Reads queued completion events (`ShotFired`, `DefiladeReached`, `FarSideReached`, `BoundComplete`, `VetoDetected`, `Abort`) and transitions `state.PhaseId` per a per-maneuver transition table (the table is supplied by the maneuver doctrine — the primitive is the engine, not the table).
- Tracks minimal-exposure dwell: `currentTick - PhaseEnteredTick` against `dwellTimeoutSeconds`. If no completion event fires within the timeout window, transitions to the maneuver's `RecoveryPhaseId`.

**NOT included:** The transition tables themselves (those live per-maneuver in Phase 5). Event sources (Phase 4 wires Brain/Muscle event ingress).

**Constraints:**
- Pure state machine; deterministic given event order.
- Event order resolution: events processed in span order; the primitive does not re-order them.

**Success Conditions:**

SC-P1-04-1: A transition table `{ Phase 0 + FarSideReached → Phase 1 }`, fed `events = [FarSideReached]`, transitions `state.PhaseId` from 0 to 1 and bumps `PhaseEnteredTick`.

SC-P1-04-2: An empty `events` span with `currentTick - PhaseEnteredTick > dwellTimeout` transitions to the recovery phase.

SC-P1-04-3: A `VetoDetected` event always overrides other events arriving the same tick and routes to the recovery phase (the design's "broken rotation detected" path).

---

### TASK-SQD-P1-05: Exposed-slot rotation with burn/reuse

**Design reference:** `Squad_Coordination_Design_v1_1.md` §2 primitive 5; generalizes [`HillAttackMutableState`](../../Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackDtos.cs)'s `BurnedSlotsMask` / `WaveUsedSlotsMask`.

**Scope:**
- New file `FDP/Toolkits/Fdp.Toolkits/Squad/Primitives/SlotRotation.cs`.
- API:
  ```csharp
  public static int AcquireSlot(ref SlotRotationState rotation, int totalSlots);     // returns next unused, non-burned slot; -1 if exhausted
  public static void ReleaseSlot(ref SlotRotationState rotation, int slotIndex);
  public static void BurnSlot(ref SlotRotationState rotation, int slotIndex);        // permanent (member died at this slot)
  ```
- `SlotRotationState` is a sub-region inside `SquadCognitiveState.Slots` (each `SlotState` carries `byte UsedSlotsMask, byte BurnedSlotsMask` — fits the 8-byte `SlotState` shape already pinned in P0-02; OR a separate small adjacent struct, decided at implementation time and pinned in the offset test).

**NOT included:** Authoring the firing-line / crossing-lane slot sets — those are per-maneuver.

**Constraints:**
- Bitmask ops, no allocation.
- Up to 16 slots per "exposure ring" — fits a `ushort` mask.

**Success Conditions:**

SC-P1-05-1: `AcquireSlot` returns sequential indices 0..7 over 8 calls into a fresh rotation of `totalSlots == 8`; the 9th returns -1.

SC-P1-05-2: `BurnSlot(3)` then `ReleaseSlot(3)` keeps slot 3 unavailable to future `AcquireSlot` calls (burn dominates release).

SC-P1-05-3: After all slots are burned, `AcquireSlot` returns -1 (forces the caller to repartition or abort).

---

## Phase 2 — Shared situational awareness + danger-area sensor

**Goal:** The one genuinely new mechanism (§4 merge) + the new EQS-shaped sensor pipeline (§5).

### TASK-SQD-P2-01: `SquadPerceptionMergeSystem` (10 Hz + event-driven)

**Design reference:** `Squad_Coordination_Design_v1_1.md` §4 (cadence S-2).

**Scope:**
- New file `Hrot/Subsystems/Hrot.AI.Brain/Squad/SquadPerceptionMergeSystem.cs` (or matching the existing AI-systems folder convention; verify at implementation time).
- Brain system. For each entity with `UnitRoster + Blackboard1024 + SquadStateMarker`:
  - Walk subordinates via `UnitRoster`; read each subordinate's `TargetMemory` (3D, post-promotion).
  - Merge contacts by entity id into `state.Contacts` (the `SquadContactPool` sub-region of `SquadCognitiveState`). Per contact: keep max threat, most-recent 3D position, OR-ed `Modalities`, OR-ed `SourceMembersMask` (which members saw it).
  - Capacity-bounded (16, matching `PerceptionConstants.MaxTrackedTargets`); insertion-sorted by threat (consistent with `TargetMemory.AddOrUpdateTarget`'s descending sort).
- Cadence: decimated to **10 Hz** (`Time.SimTime % 0.1s` style — match the existing perception/EQS cadence in the engine; check `AutonomousPerceptionModule` for the precedent timer). Plus event-driven: when any subordinate's `TargetMemory` reports a new contact or evicts one, re-merge on the same tick.

**NOT included:** The `SquadKnowsContact` Utility input reader (P2-02).

**Constraints:**
- Zero allocation per tick.
- 3D positions throughout — no Z hardcoded to 0 (lessons from [Step_1_5_TargetMemory_3D_Reconciliation.md](./Step_1_5_TargetMemory_3D_Reconciliation.md)).
- The event-driven trigger reads a per-member `TargetMemory.ChangeEpoch` (add if absent — see [DEBT-TRACKER.md](./DEBT-TRACKER.md) entry if this requires a prereq).

**Success Conditions:**

SC-P2-01-1: A 3-member squad with three distinct contacts (one per member, no overlap) merges to `state.Contacts.Count == 3`; `SourceMembersMask` for each contact has exactly one bit set.

SC-P2-01-2: Two members seeing the same contact → `state.Contacts.Count == 1`; `SourceMembersMask` has two bits set; `ThreatScore == max` of the two; `Position == most-recent`.

SC-P2-01-3: At 10 Hz (sim ticks at 60 Hz), the system runs roughly every 6 ticks; jitter under 1 tick (verify with a frame counter on a deterministic test).

SC-P2-01-4: A new contact arriving on member 1 triggers a re-merge on the same tick (event-driven path), not waiting for the next 10 Hz window. Verify by spawning a contact at tick T and asserting `state.Contacts` reflects it at tick T+1.

SC-P2-01-5: At the 17th distinct contact, the lowest-threat contact is evicted (cap invariant); insertion sort preserved.

---

### TASK-SQD-P2-02: `SquadKnowsContact` Utility input reader

**Design reference:** `Squad_Coordination_Design_v1_1.md` §4 ("readable by members"). Reader convention: `Utility_AI_Design_v1_1.md` §6.

**Scope:**
- New `[UtilityInput(Name = "SquadKnowsContact")]` static method in a new `SquadInputs.cs` (Brain catalog extension — sibling of `StandardInputs.cs`).
- The reader takes `UtilityInputCtx` with `Context = Candidate` (a candidate contact entity id), walks the member's `UnitSubordinate.Commander` → projects `SquadCognitiveState` → checks if the candidate's entity id is in `state.Contacts`. Returns `1f` if known, `0f` if not.
- Add a complementary `SquadContactThreatLevel(Context = Candidate)` reader that returns the squad-pooled threat for the candidate (or `0f` if absent).

**NOT included:** Squad-level commander-tier inputs (Phase 3 brings `SquadStrengthRatio`, etc.).

**Constraints:**
- Cache the commander handle per agent via the existing input-context caching mechanism (see Utility §6.6 "EQS multi-sensor child cache" — same trick, different field).
- Zero allocation.

**Success Conditions:**

SC-P2-02-1: A member of a squad whose pool contains contact X gets `SquadKnowsContact(X) == 1f` even when its own `TargetMemory` doesn't list X.

SC-P2-02-2: A member of a squad whose pool does NOT contain X gets `SquadKnowsContact(X) == 0f`.

SC-P2-02-3: A non-squad-member (no `UnitSubordinate.Commander`) gets `SquadKnowsContact == 0f` (default-safe).

SC-P2-02-4: `SquadContactThreatLevel(X)` returns the pool's threat score for X, matching what `SquadPerceptionMergeSystem` wrote.

---

### TASK-SQD-P2-03: `DangerAreaSensor` + `DangerAreaCognitiveBuffer` components

**Design reference:** `Squad_Coordination_Design_v1_1.md` §5.1 (lifecycle), §5.2 (schema). User-clarified direction: bespoke sensor-shaped components, NOT inside EQS.

**Scope:**
- New folder `FDP/Toolkits/Fdp.Toolkits/Squad/DangerArea/`.
- `DangerAreaSensor` component: `{ uint BlueprintId, uint Epoch, float RefreshIntervalSeconds, float LastRefreshSimTime }`. Carries the query config. Component-id allocated in `GlobalComponentIds.cs`.
- `DangerAreaCognitiveBuffer` component: `{ int Count; InlineArray<8> DangerAreaDescriptor Slots }`. Holds the result set. Component-id allocated in `GlobalComponentIds.cs`. Cap = **8 descriptors** (the squad rarely tracks more than a few features simultaneously; can be raised in a follow-on).
- Lifecycle: the commander spawns a **child entity** carrying `DangerAreaSensor + DangerAreaCognitiveBuffer + PartMetadata { ParentEntity = commander, InstanceId = … }`. Mirror the EQS multi-sensor pattern (Utility §6.6) so the commander can own multiple sensor children if it ever needs to. The result-routing system writes into the child's buffer.
- New `DangerAreaRefreshSystem` (Brain, runs at the configured interval): calls into the `IDangerAreaProvider` (the fake in Phase 0, the real navmesh provider later) and writes results into the child's buffer; bumps `Epoch`.

**NOT included:** The fake provider (P0-04 already landed). The EQS cover query (Phase 4 wires it). The squad HSM consumer (Phase 5).

**Constraints:**
- `[InlineArray]` defensive-copy rule applied — all writes via `Span<DangerAreaDescriptor>`.
- Zero allocation per refresh.
- `Epoch` bump is the cache-invalidation signal for downstream readers (matches the `EqsSensor.Epoch` precedent).

**Success Conditions:**

SC-P2-03-1: A commander spawns one sensor child carrying both components and `PartMetadata.ParentEntity == commander`; `DangerAreaRefreshSystem` writes 3 descriptors from a fake provider into the buffer; `buffer.Count == 3`.

SC-P2-03-2: `Epoch` increments on each successful refresh; downstream cache invalidation can observe it.

SC-P2-03-3: A commander with two sensor children (different `BlueprintId`s) refreshes them independently — each buffer carries its own descriptors.

SC-P2-03-4: The buffer is 3D-native: writing a descriptor with `ZFloor=1f, ZCeiling=5f` reads back identically (no Z lost to packing).

---

### TASK-SQD-P2-04: Phase-2 integration test (perception merge + sensor)

**Design reference:** §4, §5.

**Scope:**
- Fixture: 4-member squad on a fabricated map; 2 members see contact A (high threat), 1 sees contact B (low threat). Commander hosts a `DangerAreaSensor` child fed a fake `StreetCrossing` descriptor.
- Tick the world; assert `state.Contacts` has both A and B with correct `SourceMembersMask`; assert the danger-area buffer has the StreetCrossing.
- Bump member 4 to also see B (a second sighting); re-tick; assert B's `SourceMembersMask` grew and `state.Contacts` is still capacity-bounded.

**Success Conditions:**

SC-P2-04-1..3 as scripted above. SC-P2-04-4: zero allocations across 100 ticks (allocation tracker).

---

## Phase 3 — Maneuver selection (commander-tier Utility)

**Goal:** `ManeuverSelect` as a real `[UtilityDecision]` running on the commander; mission orders can force a maneuver via the existing tactical-intent rail.

### TASK-SQD-P3-01: Commander-tier Utility scorer pipeline

**Design reference:** `Squad_Coordination_Design_v1_1.md` §8.0; `Utility_AI_Design_v1_1.md` §4.

**Scope:**
- Commander entities tick the Utility scorer over their `ManeuverSelect` decisions. Wire a `CommanderUtilityTickSystem` that:
  - For each commander with a `ManeuverSelect`-bound BTree/HSM/Blueprint node, calls `UtilityScorer.Score(...)` with `Context = Self` (the commander).
  - Writes the winning `OptionId` into `state.ManeuverKind`.
  - On `Flags.bit0 (MissionOverride) == 1` the scorer is **skipped** and `state.ManeuverKind` retains its forced value (see P3-03).
- Cadence: decimated to ~10 Hz (matches the §4 merge cadence and the Utility re-score rhythm hinted in `Utility_AI_Design_v1_1.md` Q-4).
- Trace: writes to a per-commander `UtilityTraceWorkingMemory1024` (already exists for agents — same component, same buffer). Debug overlays (Phase 5) read it.

**NOT included:** Specific maneuver-selection considerations (P3-02). The mission-override mapper (P3-03). The starter-pack `ManeuverSelectDecision` (P3-04).

**Constraints:**
- Reuses `UtilityScorer.Score` unchanged — `ManeuverSelect` is just another `Kind`.
- Zero allocation per commander per tick.

**Success Conditions:**

SC-P3-01-1: A commander running a stub `ManeuverSelect` decision with two options (`DangerAreaCross`, `Hold`) and trivial constant scores selects the higher one; `state.ManeuverKind` is set.

SC-P3-01-2: With `state.Flags |= MissionOverride` the scorer does not run and `state.ManeuverKind` is unchanged.

SC-P3-01-3: The 10 Hz cadence holds (verify with a tick counter).

SC-P3-01-4: The trace buffer carries the per-consideration breakdown (raw → norm → curve → weighted), readable by the existing Utility debug inspector.

---

### TASK-SQD-P3-02: Squad-level commander Utility considerations

**Design reference:** `Squad_Coordination_Design_v1_1.md` §8.0 ("squad strength ratio, the active danger area's threat rating and kind, member-state aggregates from the contact pool, ammo/health rollups").

**Scope:**
- New readers in `SquadInputs.cs` (sibling of `StandardInputs.cs`):
  - `SquadStrengthRatio(Self = commander)` — `sum(member.Health.Current) / sum(initial)` across `UnitRoster`. Normalized 0–1.
  - `SquadAmmoRollup(Self = commander)` — average `WeaponState.Ammo / MaxAmmo` across all mounts of all members. Normalized 0–1.
  - `ActiveFeatureThreatRating(Self = commander)` — reads `state.ActiveFeatureId` → lookup in `DangerAreaCognitiveBuffer` → returns the descriptor's `ThreatRating`. 0 if no active feature.
  - `ActiveFeatureKindIs(Self = commander, Params.Kind = DangerAreaKind)` — parameterized; returns 1 if the active feature's `Kind` matches the param, else 0. (Pattern: a parameterized boolean, like the EQS `EqsTopScore("CoverQuery")` style — kind packed into `InputParams`.)
  - `SquadPoolThreatAggregate(Self = commander)` — sum of `state.Contacts.ThreatScores`, normalized by a reader-owned max.
- Each is a `[UtilityInput]` reader with normalization owned by the reader (Utility §6.2).

**NOT included:** The decision definition that uses them (P3-04).

**Constraints:**
- Each reader walks `UnitRoster` at most once per call; zero allocation.
- Commander handle is the implicit `Self`; the reader uses `ctx.Self`.

**Success Conditions:**

SC-P3-02-1: A full-health squad gets `SquadStrengthRatio == 1.0f`; after one member dies (Health = 0), it drops proportionally.

SC-P3-02-2: A commander with no `ActiveFeatureId` gets `ActiveFeatureThreatRating == 0`.

SC-P3-02-3: `ActiveFeatureKindIs(Kind = StreetCrossing)` returns 1 when the active feature is a street crossing, 0 otherwise; switching the active feature flips the reading next tick.

SC-P3-02-4: `SquadAmmoRollup` returns 1.0 for a fully-loaded squad and 0 after all members exhaust ammo.

SC-P3-02-5: 10⁶ reader calls allocate zero managed bytes.

---

### TASK-SQD-P3-03: Mission-override mapper (force a maneuver)

**Design reference:** `Squad_Coordination_Design_v1_1.md` §8.0 (mission override); existing rail: [`ITacticalOrderMapper`](../../FDP/Toolkits/Fdp.Toolkits/Behavior/TacticalOrderMapper/ITacticalOrderMapper.cs), [`TacticalIntentResolutionSystem`](../../Hrot/Subsystems/Hrot.CGF/Systems/TacticalIntentResolutionSystem.cs).

**Scope:**
- New `ForceManeuverMapper : ITacticalOrderMapper` in `Fdp.Toolkit.Squad.Mappers`.
- `TargetIntentId = "ForceManeuver"`.
- `TryMap(self, repo, jsonParams, out AssignBehaviorEvent)` parses `jsonParams` as `{ "maneuverKind": <ushort>, "featureId": <uint?> }`, writes `state.ManeuverKind`, `state.Flags |= MissionOverride`, optionally `state.ActiveFeatureId`. Returns a no-op `AssignBehaviorEvent` (since the maneuver dispatch itself remains the squad HSM, but the intent must still resolve through the rail to keep the audit trail uniform).
- An accompanying `ClearForceManeuver` mapper clears the override bit.

**NOT included:** Per-maneuver intent payloads (those are per-maneuver in Phase 5).

**Constraints:**
- Stateless after registration (the `ITacticalOrderMapper` contract).
- JSON parse: use the existing `SearchPredicateDto`-style minimal parser the engine uses for intent payloads (verify at implementation time).

**Success Conditions:**

SC-P3-03-1: Publishing `AssignTacticalIntentEvent { IntentId = "ForceManeuver", Json = "{\"maneuverKind\":1}" }` against a commander causes `state.ManeuverKind == 1` and `state.Flags & MissionOverride != 0`.

SC-P3-03-2: Re-publishing `ClearForceManeuver` clears the bit; the scorer (P3-01) resumes selecting.

SC-P3-03-3: A commander without a `Blackboard1024` returns `false` from `TryMap` (capability missing — the contract's escape hatch).

---

### TASK-SQD-P3-04: `ManeuverSelectDecision` starter-pack (worked example)

**Design reference:** `Squad_Coordination_Design_v1_1.md` §8.0 (optional follow-on); paralleling Utility's starter pack.

**Scope:**
- New `ManeuverSelectStarterDecision` in `FDP/Toolkits/Fdp.Toolkits/Squad/StarterPack/`.
- Three options:
  - `DangerAreaCross` (Mode.WeightedProduct): high weight on `ActiveFeatureKindIs(StreetCrossing | ChokePoint)`, medium on `SquadStrengthRatio` (Linear), low on `SquadAmmoRollup` (Threshold).
  - `BoundOverwatch` (Mode.WeightedProduct): high weight on `SquadStrengthRatio` (Linear), medium on `ActiveFeatureKindIs(OpenGround)` (Linear), medium on `ActiveFeatureThreatRating` (Logistic).
  - `Hold` (Mode.WeightedProduct): high on `ActiveFeatureThreatRating` (Linear — high threat → hold), low on `SquadAmmoRollup` (InverseLinear — low ammo → hold).
- Integration test: a commander with a `StreetCrossing` active feature and full squad selects `DangerAreaCross`; flipping the feature to `OpenGround` makes `BoundOverwatch` win.

**NOT included:** The maneuver execution logic (Phase 5).

**Success Conditions:**

SC-P3-04-1..3: integration test as scripted above; all three options' breakdowns appear in the Utility trace buffer with the winner highlighted.

SC-P3-04-4: Source-gen catalog includes the decision; the analyzer is silent (no diagnostics).

---

## Phase 4 — Authority, rotation engine, movement mode

**Goal:** The two-level-by-weight authority model (§6) wired through member Utility decisions, the event-driven rotation engine (§9), and the `MovementMode` posture bit (§6.1).

### TASK-SQD-P4-01: `AssignedRole` / `AssignedSlot` member considerations

**Design reference:** `Squad_Coordination_Design_v1_1.md` §6 (authority model). Member-side analog of the existing `IsAssignedTarget` consideration ([`StandardInputs.cs`](../../FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/StandardInputs.cs)).

**Scope:**
- New `[UtilityInput]` readers in `SquadInputs.cs`:
  - `AssignedRole(Self = member, Params.RoleId)` — 1 if `state.Roles[memberIndex].RoleId == Params.RoleId`, else 0.
  - `AssignedSlot(Self = member, Params.SlotKind)` — 1 if the member is assigned a slot of that kind in `state.Slots`, else 0.
- These are intended to be used as **high-weight considerations** (≥ 0.8 in product mode, the design's "maneuver discipline is a much higher bias weight" rule).

**NOT included:** The decisions that use them (those live per-maneuver in Phase 5).

**Constraints:**
- Each reader resolves the member's commander once per tick via the existing commander-handle cache (Utility §6.6 child-cache pattern, repurposed for the commander link).

**Success Conditions:**

SC-P4-01-1: A member with `state.Roles[i].RoleId == Suppressor` gets `AssignedRole(RoleId=Suppressor) == 1f` and `AssignedRole(RoleId=Flanker) == 0f`.

SC-P4-01-2: A non-squad-member (no `UnitSubordinate.Commander`) reads 0 (default-safe).

SC-P4-01-3: The reader fires within the existing per-tick budget (no measurable regression on a 16-member squad).

---

### TASK-SQD-P4-02: Veto detection + broken-rotation recovery

**Design reference:** `Squad_Coordination_Design_v1_1.md` §6 (veto from self-preservation), §9 (squad-HSM transitions on veto).

**Scope:**
- New `SquadVetoDetectionSystem` (Brain, on commanders):
  - For each member, compare `state.Roles[i] / state.Slots[…]` (the leader's assignment) against the member's `BehaviorState.ActiveBehaviorHash` (or whichever component carries the member's actual behavior choice — verify at implementation time, likely the existing tactical-intent resolution result).
  - If the divergence persists for `> vetoConfirmTicks` (configurable, e.g. 3 ticks at 10 Hz = 0.3 s), emit a `PhaseEvent.VetoDetected` into the squad HSM's event queue (`SquadCognitiveState` has a tiny event ring or a per-tick scratch span — pin the layout in P0-02 if not already there; otherwise it lives in a transient scratch buffer adjacent to `SquadCognitiveState`).

**NOT included:** The recovery phase logic itself (that's per-maneuver, Phase 5).

**Constraints:**
- Hysteresis (`vetoConfirmTicks`) prevents one-tick blip false vetos.
- Zero allocation.

**Success Conditions:**

SC-P4-02-1: A member assigned `Engage(targetA)` but whose own Utility selects `Flee` for `> 3` ticks triggers `PhaseEvent.VetoDetected` for that member.

SC-P4-02-2: A single-tick divergence (member re-aligns next tick) does **not** trigger the event (hysteresis holds).

SC-P4-02-3: The dominant self-preservation consideration is recorded in the trace alongside the veto event (read from the member's `UtilityTraceWorkingMemory1024`) — for overlay display in Phase 5.

---

### TASK-SQD-P4-03: Event-driven rotation engine (hybrid event/timer)

**Design reference:** `Squad_Coordination_Design_v1_1.md` §9.

**Scope:**
- Wire the four completion-event sources into the squad HSM's event queue:
  - **ShotFired** — the Brain ordered an `ActionIdAimAndFire` and `WeaponChannel` reports the round was fired (existing channel-completion event; verify hook).
  - **DefiladeReached / FarSideReached** — the locomotion channel's intent-success event (carries enough payload to know which intent succeeded). Maps from Muscle-issued completion to a `PhaseEvent` in the queue.
  - **BoundComplete** — same as DefiladeReached but tagged for the bounding-overwatch maneuver (the squad doctrine differentiates by phase context).
  - **TimerFallback** — emitted by `PhaseSequencer.Advance` (P1-04) when no completion event arrives within the dwell window.
- A small `SquadEventIngressSystem` watches the per-member channel events and translates them into squad-level `PhaseEvent`s scoped to the right commander.

**NOT included:** The per-maneuver dwell tunings (per-maneuver in Phase 5).

**Constraints:**
- The hill-attack BTrees already produce all four events (see [`HillAttackCommanderNodes.cs`](../../Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackCommanderNodes.cs) — `Condition_IsWaveCompleted` uses `BehaviorState.ActiveBehaviorHash` to detect bound-complete). The squad ingress system uses the same hooks; verify at implementation time.

**Success Conditions:**

SC-P4-03-1: A fired round on a squad member triggers exactly one `PhaseEvent.ShotFired` scoped to the commander.

SC-P4-03-2: A locomotion success event ("intent reached") with the right intent-id translates to `PhaseEvent.FarSideReached`.

SC-P4-03-3: A phase with no completion event fires `PhaseEvent.TimerFallback` exactly when `currentTick - state.PhaseEnteredTick > dwellTimeout`.

SC-P4-03-4: Hill-attack parity hook (consumed by Phase 5's 8.4 test): the rotation engine, configured with the tank-platoon's transition table, reproduces today's `HillAttackCommanderNodes` wave behavior on a fabricated fixture.

---

### TASK-SQD-P4-04: `MovementMode` intent (squad posture bit → Muscle)

**Design reference:** `Squad_Coordination_Design_v1_1.md` §6.1.

**Scope:**
- New `MovementMode` enum: `{ Default = 0, Covered = 1, Fast = 2 }`.
- A bit in `SquadCognitiveState.Flags` (allocate bits 8..9 — pin in P0-02 offset test). When the squad HSM enters a danger-zone phase, it sets the flag; on exit it clears.
- New `MovementModeIntent` per-member component (small: just `byte Mode`). The squad publishes it via a new `SquadMovementModeBroadcastSystem` (Brain) — for each member, write the intent based on the commander's flag.
- Muscle consumes the intent. **Out of scope here**: the actual Muscle path-shaping logic. We only define the intent and the Brain-side broadcast; the Muscle consumer is a separate engine team commit (or already exists — verify at implementation time and decide whether to file a Muscle prereq task).

**NOT included:** Muscle-side path shaping; cover-aware geometry.

**Constraints:**
- One enum, one bit, one component, one system. No squad-side pathing.

**Success Conditions:**

SC-P4-04-1: Setting `state.Flags |= CoveredMovement` on a commander writes `MovementModeIntent.Mode = Covered` on every `UnitRoster` subordinate within one tick.

SC-P4-04-2: Clearing the bit reverts to `MovementModeIntent.Mode = Default`.

SC-P4-04-3: Members without a commander (no `UnitSubordinate`) are unaffected.

---

## Phase 5 — Maneuver catalog (infantry-first, integration tests)

**Goal:** Each catalog entry is a configuration of the five primitives; each doubles as an integration test (fabricated-world fixture, Utility starter-pack discipline). **Per the design §11 sequencing: infantry first (8.1, 8.2, 8.3), then 8.4 hill-crest parity, then 8.6 briefer.**

### TASK-SQD-P5-01: 8.1 Danger-area crossing (canonical infantry case)

**Design reference:** `Squad_Coordination_Design_v1_1.md` §8.1.

**Scope:**
- New `DangerAreaCrossingManeuver` in `Hrot/Subsystems/Hrot.AI.Behaviors/Squad/Maneuvers/` (or matching the existing maneuver folder convention).
- Five-phase squad HSM: `SetSecurity → CrossElement → FarSideCoverEstablished → CollapseSecurity → Reform`.
- Configures the primitives:
  - **Element partition (P1-01)**: 2 elements — crossing + security/overwatch.
  - **Tactical-feature ref (P1-02)**: `state.ActiveFeatureId` = the danger area's id.
  - **Role/slot assignment (P1-03)**: a `DangerAreaCrossingRoleDecision` scoring each member for `{Crossing, Security}` roles.
  - **Phase sequencer (P1-04)**: transition table per phase list above; completion events: `FarSideReached` advances Phase 1→2, etc.
  - **Slot rotation (P1-05)**: crossing lanes — first crosser uses lane 0, second uses lane 1, etc.; `WaveUsedSlotsMask` rotates.
- First-across is re-assigned to the covering role on Phase 2 entry (the §8.1 "first-across re-assigned" mechanism — done by re-running P1-03 with a different decision).
- EQS cover query (per §5.3): a separate (now-3D) EQS query fires when entering Phase 2 to find overwatch positions; the squad reads its top result.

**Integration test (fabricated fixture):**
- 4-member infantry squad; fake `StreetCrossing` descriptor at `(0,0,0)`; far-side handle at `(20,0,0)`.
- Trigger: commander's `state.ManeuverKind = DangerAreaCrossing` (set by P3 scorer or P3-03 override).
- Tick to completion (or timeout); assert:
  - All members reach the far side (their `Position.Value.X >= 20`).
  - Each phase entered exactly once.
  - Slot rotation: no two members used the same crossing lane in the same wave.
  - First-across spent ≥ 1 tick in the `Covering` role on Phase 2.

**Success Conditions:**

SC-P5-01-1..6 as scripted above. SC-P5-01-7: trace inspection — every phase transition has a labeled event source.

---

### TASK-SQD-P5-02: 8.2 Bounding overwatch (open-field + urban variants)

**Design reference:** `Squad_Coordination_Design_v1_1.md` §8.2.

**Scope:**
- `BoundingOverwatchManeuver` — two elements, leapfrog. Squad HSM alternates which element holds `Moving` vs. `Covering` each bound; role bias flips each transition.
- Two variants share the same primitive config but differ in slot authoring:
  - **Open-field**: rushes between cover; slots authored as world-space waypoints fed by an EQS cover query.
  - **Urban**: corner-to-corner; slots authored as building edges fed by the same EQS query parameterized differently.
- Completion: `BoundComplete` event drives swap.

**Integration test:**
- 4-member infantry squad over a 60 m open-field fixture; one initial threat at (60,0,0).
- Assert: at least 2 bound swaps occurred; never had >2 members exposed (moving) simultaneously; threat-suppression fire continuous (no full pause).

**Success Conditions:**

SC-P5-02-1..5 as scripted above.

---

### TASK-SQD-P5-03: 8.3 Suppress-and-maneuver

**Design reference:** `Squad_Coordination_Design_v1_1.md` §8.3.

**Scope:**
- `SuppressAndManeuverManeuver`.
- Element partition into `BaseOfFire` (high suppress-role bias, hold position) and `Assault` (advance bias along Muscle-pathed flank).
- The danger-area/threat reference anchors both.

**Integration test:** the `BaseOfFire` element fires continuously while the `Assault` element flanks; assert suppression duration ≥ assault duration.

**Success Conditions:**

SC-P5-03-1..4 as scripted.

---

### TASK-SQD-P5-04: 8.4 Hill-crest hull-down rotation (cross-unit parity proof)

**Design reference:** `Squad_Coordination_Design_v1_1.md` §8.4.

**Scope:**
- `HillCrestHullDownManeuver`.
- Wave element partition (2 at a time if platoon > 3).
- Authored firing-line + defilade-baseline segments (iteration-1 authored lines — the seam where a hull-down sensor would later plug in).
- Creep-to-LOS as the event-terminated exposed-slot task.
- Round-robin or matrix fire allocation.
- Burned/used-slot rotation.

**Parity test:** the new engine configured this way reproduces the behavior of the existing `PlatoonHillAttack` BTrees ([`HillAttackCommanderNodes.cs`](../../Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackCommanderNodes.cs)) on the existing hill-attack scenario fixture (`scenarios/hill-attack/`).
- Assertions:
  - Number of waves dispatched matches the legacy run (within ±1 due to timing jitter).
  - Burned-slot mask after a tank-loss matches.
  - Final state (all tanks defilade / mission complete) matches.
  - The "resume-trap" avoidance — active scanning during creep, not cached perception — holds (verify via a fixture where the cached perception is stale; the new engine should still trigger creep abort on a fresh `TargetMemory` change).

**Success Conditions:**

SC-P5-04-1: Hill-attack parity test green.

SC-P5-04-2: Burn/reuse semantics: 2 burns over a 6-slot ring leave 4 usable; rotation visits them in order.

SC-P5-04-3: Resume-trap fixture passes — creep abort uses live perception.

---

### TASK-SQD-P5-05: 8.6 Briefer catalog entries (stack-and-room-entry + travelling overwatch)

**Design reference:** `Squad_Coordination_Design_v1_1.md` §8.6.

**Scope:**
- `StackAndRoomEntryManeuver`: sector-assignment-heavy. Stack on a door; enter in sequence with assigned sectors of fire. Exercises role/slot assignment where slots are *sectors of fire* (the slot kind is `SectorOfFire { float AngleStart, float AngleEnd }`).
- `TravellingOverwatchManeuver`: lead element moves, trail element overwatches at distance; bounding's looser cousin. Exercises **element-split without rotation**.

**Lighter detail than 8.1–8.4** per the design's "lighter detail in v1" note. Integration tests verify the primitives exercised: rotation, role/sector assignment, element split, turn-taking, burn/reuse — together with 8.1/8.2/8.3/8.4 the catalog covers every primitive.

**Success Conditions:**

SC-P5-05-1: Stack-and-room-entry assigns 4 members to 4 distinct sectors (180° / 4 = 45° each, no overlap).

SC-P5-05-2: Travelling overwatch maintains a lead-trail distance ≥ a configurable threshold.

SC-P5-05-3: Catalog primitive-coverage check: a static analysis over the catalog confirms every primitive (1–5) is exercised by at least one maneuver.

---

## Phase 6 — Three-way authoring shells (§7)

**Goal:** The primitives are a library all three authoring forms call into equally. Squad HSM is the preferred default; Blueprint and dedicated-script paths must be on parity.

### TASK-SQD-P6-01: Squad HSM authoring shell (FastHSM)

**Design reference:** `Squad_Coordination_Design_v1_1.md` §7 item 1.

**Scope:**
- A `SquadHsmShell` that takes a maneuver's transition table + per-phase entry/exit callbacks (calls into the primitives) and runs it via the existing FastHSM machinery (the same engine the catalog tests in Phase 5 use under the hood).
- This task formalizes what Phase 5 used informally — extracts the common shell so future maneuvers don't re-implement boilerplate.

**Success Conditions:**

SC-P6-01-1: Refactoring `DangerAreaCrossingManeuver` (P5-01) to use the shell does not change its observable behavior; integration tests stay green.

SC-P6-01-2: Authoring a new trivial maneuver (e.g., a 2-phase "Form Up → Move Out") takes < 50 lines of code using the shell.

---

### TASK-SQD-P6-02: Blueprint host for squad logic

**Design reference:** `Squad_Coordination_Design_v1_1.md` §7 item 2; existing `SquadState` Blueprint precedent (`Hrot.Blueprints.Tests/TestAssets/Recipes/SquadState.bp.json`).

**Scope:**
- Blueprint nodes wrapping the primitives: `PartitionElementsNode`, `AssignRolesNode`, `AdvancePhaseNode`, `AcquireSlotNode`.
- A worked Blueprint authoring of a small maneuver (e.g., the bounding-overwatch's "swap on bound" sub-logic).

**Success Conditions:**

SC-P6-02-1: The Blueprint nodes appear in the existing Blueprint editor catalog with correct pin layouts.

SC-P6-02-2: The worked Blueprint maneuver runs end-to-end on the bounding-overwatch fixture and produces the same outcomes as the HSM version (within determinism tolerance).

---

### TASK-SQD-P6-03: Dedicated-script path (parity with `PlatoonHillAttack`)

**Design reference:** `Squad_Coordination_Design_v1_1.md` §7 item 3.

**Scope:**
- The hill-crest maneuver (P5-04) is **already** the dedicated-script path test — this task makes that explicit by documenting the seam and adding a regression that the imperative form keeps working alongside the new HSM form.

**Success Conditions:**

SC-P6-03-1: Both the existing `HillAttackCommanderNodes` BTree and the new `HillCrestHullDownManeuver` HSM produce identical scenario outcomes; the test runs them side-by-side.

SC-P6-03-2: Removing either form (deactivating it via the existing BTree-deactivator) does not break the other.

---

## Phase 7 — Debug & overlays (§10)

**Goal:** The squad coordination overlay extends `SquadAssignmentOverlaySource` (already sketched in `Runtime_Tuning_Console_and_AI_Overlays_Design_v1_0.md` §7.5) and surfaces the new maneuver state.

> **Dependency:** This phase requires the Utility AI Phase-4 overlays (`AiOverlayFlags`, `BehaviorDebugFlags` family, the overlay-budget arbiter) — assumed merged.

### TASK-SQD-P7-01: `SquadCoordinationOverlaySource` (extends `SquadAssignmentOverlaySource`)

**Design reference:** `Squad_Coordination_Design_v1_1.md` §10; `Runtime_Tuning_Console_and_AI_Overlays_Design_v1_0.md` §7.5.

**Scope:**
- Per flagged commander entity (`DebugState.Flags & AiOverlayFlags.SquadAssignment`):
  - Color each member by `state.Elements.MemberElementIndex[i]` (palette of N element colors, picked deterministically per element index).
  - Label each member with their `state.Roles[i].RoleId` (textual: "Pointman", "Suppressor", …).
  - Draw the **active danger area** as an extruded OBB (`Center`, `ExtentsXY`, `AngleRad`, `ZFloor`/`ZCeiling`) — the 3D extent renders better than a flat box, matching the design's call-out.
- Layer-masked, budget-honored per the existing overlay infra.

**Success Conditions:**

SC-P7-01-1: Toggling `AiOverlayFlags.SquadAssignment` on a commander makes the overlay visible; toggling off makes it disappear.

SC-P7-01-2: Element color persistence — a member that stays in element 0 keeps the same color across ticks (no flickering).

SC-P7-01-3: The danger-area box's Z extent visibly differs between a ground-level street crossing and a bridge-deck variant in a multi-level fixture.

---

### TASK-SQD-P7-02: Assignment-vs-actual divergence lines + veto labels

**Design reference:** `Squad_Coordination_Design_v1_1.md` §10 ("assignment lines: leader→member→assigned-slot (solid) vs. dashed actual"); §6 veto display.

**Scope:**
- For each member: solid line to assigned slot/role anchor; dashed line to what the member is actually doing (read from the member's `BehaviorState` / behavior assignment).
- When the two diverge (veto, P4-02), label the dashed line with the **dominant self-preservation consideration** (read from the member's Utility trace — the consideration with the highest `1 - curve(x)` weight contribution that pushed the assigned option toward 0). This is the same "why did it pick this" trace mechanism Utility §9 already implements.

**Success Conditions:**

SC-P7-02-1: A member doing exactly what the leader assigned shows solid + dashed coincident (or only solid).

SC-P7-02-2: A vetoing member shows divergent dashed line with a non-empty label.

SC-P7-02-3: The label updates as the dominant consideration changes tick-to-tick.

---

### TASK-SQD-P7-03: Squad HSM phase + dwell timer + merged contact pool markers

**Design reference:** `Squad_Coordination_Design_v1_1.md` §10.

**Scope:**
- Anchored on the commander entity:
  - Phase ID (textual: "Phase 2: FarSideCoverEstablished") + dwell timer (`currentTick - PhaseEnteredTick`, displayed in seconds).
  - The merged contact pool from `state.Contacts` as world-space markers distinct from per-member perception (different glyph, larger). Hover-over reveals `SourceMembersMask` (which members saw it).

**Success Conditions:**

SC-P7-03-1: The phase label updates immediately on a phase transition.

SC-P7-03-2: The dwell timer ticks at sim speed; on transition it resets to 0.

SC-P7-03-3: The pool markers visibly differ from per-member `TargetMemoryOverlay` markers.

SC-P7-03-4: Overlay-budget shedding (per `Runtime_Tuning_Console_and_AI_Overlays_Design_v1_0.md` §6.2): with 50 squads observed simultaneously, the lowest-priority overlay shed (Channels first, etc.) keeps the frame within `MaxGizmoFrameMs`.

---

*End of TASK-DETAIL. The catalog (§8) is the integration-test gate; Phase 7 closes the observe→tune loop. Hill-crest parity (P5-04) is the cross-unit-type proof. Three-way authoring (Phase 6) is the API-shape guarantee.*
