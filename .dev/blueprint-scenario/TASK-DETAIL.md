# TASK-DETAIL — Blueprint ↔ Entity Assignment & Scenario Persistence

**Design of record:** [`BLUEPRINT-SCENARIO-DESIGN.md`](./BLUEPRINT-SCENARIO-DESIGN.md) — read it first. Task
descriptions below reference its sections (§N) rather than restating them. **Tracker:**
[`TASK-TRACKER.md`](./TASK-TRACKER.md). **Debt:** [`DEBT-TRACKER.md`](./DEBT-TRACKER.md). Developer contract:
`.dev/.guides/DEV-GUIDE.md`.

Conventions: paths are repo-relative; "success conditions" are the unit/integration tests that must pass (plus
build 0 errors and 0 net-new failures in the touched test projects). Do not weaken existing tests or regenerate
snapshots; if behavior legitimately changes a test, list it by name with old→new.

### Success-condition rules (read before claiming any task done)
A task is **done only when every listed success condition is a real, passing test** — verified by the lead against
the diff, not the agent's report. To count:
1. **One test per bullet, asserting the exact stated value/count/state** — not `Assert.True(true)`, not a tautology,
   not a log line. If a bullet says "== N slots", assert the count equals N; "no upgrade occurred", assert the tier
   component type is unchanged; "no exception", wrap the real call and assert it does not throw.
2. **The test must drive the real production path.** No mock/stub that returns the expected answer; exercise the
   actual `BlueprintAttachService`/translator/system/registry. Mocks are only for the time controller / ImGui
   context, never for the unit under test.
3. **UI tasks (BSA-204/205): assert on a headless view-model, not on ImGui.** Extract the decision/diff/projection
   logic into a plain class with public methods returning data; tests assert on that. ImGui draw code stays a thin
   shell. "Renders X" is NOT a success condition — "the view-model computes X" is.
4. **Report**: list the new test names + the full before/after failing-set by name, and the exact `dotnet test`
   command(s). Do **not** delete/skip/`[Fact(Skip=…)]` a failing test to go green. If blocked, STOP and report.
5. Build 0 errors; **0 net-new** failures vs. the documented baseline; **no snapshot regeneration**
   (`BLUEPRINT_REGENERATE_SNAPSHOTS` unset) — a changed golden means STOP + report the diff.

---

## Phase 1 — Core foundation (`Fdp.Toolkits.Blueprints`)

### BSA-101: Mark blackboard components `NoSave`

**Design:** §2. **Touches:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/Components/BlueprintBlackboard{1024,4096,16384}.cs`.

Add `[DataPolicy(DataPolicy.NoSave)]` to all three `BlueprintBlackboard*` structs (mirrors the AiPrimitive
`Blackboard1024`). This stops volatile runtime bytes leaking into scenario JSON; checkpoints/recorder still capture
them.

**Success conditions:**
- Unit test: reflection over each of the three components asserts the `DataPolicy.NoSave` flag is present.
- Serialization test: an entity carrying a `BlueprintBlackboard1024` is scenario-serialized → the produced JSON does
  **not** contain a `"BlueprintBlackboard1024"` key.
- Build 0 errors; existing blueprint runtime/partition tests unchanged.

> ⚠️ BSA-101 makes old scenarios with `BlueprintBlackboard*` keys un-injectable → **must land together with BSA-202's
> black-hole keys** (or scenario load throws). They are a single reviewable unit; do not commit BSA-101 alone.

### BSA-102: Unified attach/detach seam in core, keyed by `BlueprintId`

**Design:** §3, §10. **Touches:** new `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintInstanceService.cs` (core);
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Runtime/BlueprintAttachService.cs` (reduce to forwarder).

Move the attach logic out of the editor assembly into core, keyed by the runtime `int blueprintId` (+ `BlueprintRegistry`),
so CGF/genesis and mid-runtime events can call it without an editor dependency. Wrap the existing core primitives
(`BlueprintBlackboardPartitions.Initialize/TryAttach/TryDetach`, tier selection, `def.InitDefault`).

- `AttachToEntity(EntityRepository world, BlueprintRegistry registry, int blueprintId, Entity entity)` →
  classified result (Attached / AlreadyAttached / NotRegistered / NotInstanceKind / NoSlotAvailable). Idempotent.
- `DetachFromEntity(EntityRepository world, int blueprintId, Entity entity)` → bool (via
  `BlueprintBlackboardPartitions.TryDetach`, which dense-compacts the slot table).
- The existing editor `BlueprintAttachService.AttachToEntity(world, registry, BlueprintAsset, entity)` becomes a thin
  forwarder: `BlueprintIdHash.Compute(asset.AssetId)` → core seam.

**Success conditions:**
- Move/port `BlueprintAttachServiceTests` to exercise the core seam by `blueprintId`; all existing assertions pass
  (attach allocates a slot + runs `InitDefault`; idempotent re-attach = `AlreadyAttached`; unregistered/non-Instance
  classified; tier chosen by `StateSize`).
- New: `DetachFromEntity` frees the slot and dense-compacts (attach A,B,C; detach B; assert A,C remain, count==2,
  slots contiguous).
- The editor forwarder produces identical results to the prior implementation (regression).
- No assembly reference from `Fdp.Toolkits` to `Hrot.Blueprints.Editor` is introduced (verify project refs).

---

## Phase 2 — Static scenario assignment (CGF genesis)

### BSA-201: `BlueprintAssignmentDto`, `InitialBlueprintsIntent`, registration

**Design:** §4, §10. **Touches:** new DTO + intent component (core/common, alongside `GenesisIntentComponents.cs`
pattern); `GenesisIntentRegistry.RegisterAll`.

- `BlueprintAssignmentDto { Guid AssetId; Dictionary<string,object>? Overrides }` — `Overrides` **null in MVP**
  (serialized only when non-null; see §6 for the deferred override semantics).
- `InitialBlueprintsIntent` — `[DataPolicy(DataPolicy.Transient)]` + `[ComponentId(...)]` managed component holding
  `List<BlueprintAssignmentDto> Blueprints`.
- Register: add `world.RegisterManagedComponent<InitialBlueprintsIntent>()` to `GenesisIntentRegistry.RegisterAll`
  (next to `InitialPassengersIntent`/`InitialUnitSubordinateIntent`).

**Success conditions:**
- Registration test: after `GenesisIntentRegistry.RegisterAll(world)`, `world.SetManagedComponent(entity, new
  InitialBlueprintsIntent{...})` does **not** throw and round-trips via `GetManagedComponent`.
- DTO JSON round-trip: a `BlueprintAssignmentDto` with a set `AssetId` and null `Overrides` serializes without an
  `Overrides` key and deserializes equal; a populated `Overrides` round-trips key/values.
- A unique `ComponentId` is assigned (no collision — add to the Hrot component-id enum/const set).

### BSA-202: `BlueprintStateTranslator` (Extract / Inject / DOM keys + legacy black-hole)

**Design:** §4 (incl. the `GetOutputDomKeys` requirement and the legacy black-hole), §10, §11 (AssetId). **Touches:**
new `BlueprintStateTranslator : IEntityScenarioTranslator` in CGF's scenario path; registered via
`ScenarioSerializerBuilder.RegisterTranslator` where CGF builds its serializer; **plus the compiler emit** (see
prerequisite).

- **PREREQUISITE — populate `BlueprintDefinition.AssetId` (Design §11).** The property exists
  (`BlueprintDefinition.cs:28`, `init`) but the registrar emit does **not** set it (verified) → runtime
  `def.AssetId == Guid.Empty`, which would make Extract's reverse-lookup yield empty. Emit
  `AssetId = <asset guid>` in the generated Instance `BlueprintDefinition` (`CSharpEmitter`/registrar) and any
  in-memory registration path. Do **not** add/`required` the property. *(Land this before/with the Extract logic.)*
- **Extract:** for each `BlueprintBlackboard{1024,4096,16384}` on the entity, resolve `byte* memory`, call
  `BlueprintBlackboardPartitions.GetSlotCount(memory)`, loop `0..count-1` with `GetSlot(memory, i)` reading
  `BlueprintSlotEntry.BlueprintId`; map id→`AssetId` via `registry.TryGetById(id).AssetId`; emit a
  `BlueprintAssignmentDto[]` under the DOM key `"BlueprintAssignments"`. (MVP: no overrides extracted.)
- **Inject:** parse the `"BlueprintAssignments"` array → set an `InitialBlueprintsIntent` managed component on the
  entity. **No-op** for the legacy blackboard keys.
- **`GetOutputDomKeys`:** returns `"BlueprintAssignments"` **and** `"BlueprintBlackboard1024"`,
  `"BlueprintBlackboard4096"`, `"BlueprintBlackboard16384"` (the latter three are claimed only to black-hole legacy
  volatile data so `FdpAutoSerializer` never sees them).

**Success conditions:**
- **AssetId populated:** after compiling + registering an Instance blueprint,
  `registry.TryGetById(id, out def)` → `def.AssetId == <asset guid>` (not `Guid.Empty`).
- Round-trip: entity with two Instance blueprints attached (use BSA-102) → `Extract` yields JSON containing a
  `"BlueprintAssignments"` array of the two `AssetId`s and **no** `BlueprintBlackboard*` key → `Inject` produces an
  `InitialBlueprintsIntent` with two `BlueprintAssignmentDto`s carrying those `AssetId`s (non-empty).
- Legacy black-hole: a hand-authored scenario fragment containing a `"BlueprintBlackboard1024"` key
  **deserializes without throwing** and the key is ignored (no component injected from it).
- `GetOutputDomKeys()` returns exactly the four expected keys.
- Mirrors `BrainBlackboardTranslator` (no-op `Inject` for claimed legacy keys).

### BSA-203: `BlueprintMaterializationSystem` (tier pre-provision + ceiling guard + ECB removal)

**Design:** §4 (materialization steps), §5 (pre-provisioning + absolute ceiling). **Touches:** new
`BlueprintMaterializationSystem` in CGF genesis, `Input` phase (registered where CGF registers
`GenesisMaterializationSystem`).

For each entity with `InitialBlueprintsIntent`:
1. Resolve each `AssetId` → `def` via `BlueprintIdHash.Compute(assetId)` + registry; **skip + log** unresolved.
2. Aggregate Σ`def.StateSize` and the blueprint **count**; pick the smallest tier satisfying **both** slot-count and
   payload-byte bounds (1024: 4/928, 4096: 8/3936, 16384: 16/16096). **Absolute-ceiling guard:** if aggregate
   exceeds 16 slots or 16096 bytes, **log + truncate** the list — never blind-upgrade or throw.
3. Add the single fitting `BlueprintBlackboard*` component up front.
4. Attach each resolved blueprint via the BSA-102 core seam.
5. Remove `InitialBlueprintsIntent` **via the `IEntityCommandBuffer`** (`cmd.RemoveManagedComponent<...>(entity)`),
   not a direct repo removal (avoids invalidating the query's chunk iterator — see §4 step 4).

**Success conditions:**
- One-frame, single-tier: 3 blueprints summing ≤ 928 bytes → after one `Input` tick the entity has exactly one
  `BlueprintBlackboard1024` with 3 occupied slots and **no** `BlueprintMaintenanceSystem` upgrade occurred.
- Correct tier from aggregate: blueprints summing > 928 but ≤ 3936 → a single `BlueprintBlackboard4096` is
  provisioned (not 1024-then-upgrade).
- Ceiling guard: 17 blueprints (or > 16096 bytes aggregate) → a warning is logged and the attachments are truncated
  to the tier capacity; **no exception**; scenario load completes.
- Resilience: an intent containing one unregistered `AssetId` → it is skipped+logged and the remaining valid
  blueprints attach.
- Intent removed after materialization (entity no longer has `InitialBlueprintsIntent`); removal is ECB-queued
  (no iterator-invalidation; runs clean over a multi-entity query).
- After materialization the attached blueprints tick (assert via `BlueprintTickSystem` on a subsequent frame).

### BSA-204: Entity Inspector per-tier summary renderers (read-only monitoring)

**Design:** §12.1, §12.4. **Depends on:** nothing critical (registry + partitions are core, exist). **Touches:**
three new `IEntityAwareImGuiRenderer` classes (Editor/Presentation), registered in `ImGuiRendererRegistry`.

Replace the **raw byte-dump** the Entity Inspector shows today for the blackboard components with a read-only
summary. Build one renderer per tier, `[ImGuiRenderer(typeof(BlueprintBlackboard1024))]` / `4096` / `16384`
(mirror the existing `BrainBlackboardRenderer`/`Blackboard1024Renderer`). `RenderValue` reads the memory, walks the
dense table via `GetSlotCount`/`GetSlot`, resolves each `BlueprintId` → name via the registry, renders a read-only
list (name, `InstanceVersion`, tick/latent-cursor status), and **returns `true`** to suppress the default
`ImGuiPropertyTree` byte-dump.

**Implementation note (for testability):** put the parse in a plain method, e.g.
`BlueprintTierSummary.Read(byte* memory, BlueprintRegistry) → IReadOnlyList<SlotSummary{ Guid AssetId; int BlueprintId; string Name; uint InstanceVersion; … }>`; the `RenderValue` body calls it then draws. Tests assert on
`Read`, not on ImGui.

**Success conditions:** (one passing test each; assert on `Read`, not rendering — see header rule 3)
- Given a `BlueprintBlackboard1024` with 3 attached blueprints (attach via BSA-102), `BlueprintTierSummary.Read`
  returns a list of **exactly 3** entries whose `BlueprintId`s equal the attached ids and whose `Name`s resolve via
  the registry (`Assert.Equal(3, list.Count)` + id/name equality).
- `RenderValue` returns **`true`** for a `BlueprintBlackboard*` value (byte-dump suppressed) — assert the bool.
- An un-`Initialize`d / zeroed tier → `Read` returns an **empty** list and does **not** throw
  (`Assert.Empty` inside the call; no exception).
- The renderer type exposes **no** mutation/commit API (no `IEditSession`/`IsDirty`) — structural assertion that it's
  read-only.

### BSA-205: "Entity Blueprints" authoring panel (assign / remove, staged commit)

**Design:** §12.2, §12.3 (incl. paused tier-upgrade + old-tier removal), §2 (authoring invariant), §5, §10.
**Depends on:** BSA-102 (seam + `CopyToLargerTier`), BSA-301 (events + remove-before-add ordering). **Touches:** new
`EntityBlueprintsPanel` (dedicated window, NOT the Entity Inspector / `ComponentEditDrawer`).

The authoring UI that closes the topic: assign/remove Instance blueprints on the selected entity so they bake into
the scenario on save and run in preview without saving. Detached view-model (§12.2): **Reality** = per-frame scan
across all three tiers (`GetSlotCount`/`GetSlot` → `BlueprintId` → name; `AssetId` for the DTO); **Intent** = a
local mutable `List<BlueprintAssignmentDto>` (uncommitted staging, never persisted on its own). Renders the diff +
**Projected Usage** (slots/bytes vs. current tier; yellow when an upgrade is needed, red + **Apply disabled** at the
16384 ceiling). **+ Add Blueprint…** uses `BlueprintPickerSources` filtered to `Instance`; **Revert All** clears
Intent.

**Commit (Apply) — one seam, two timings (gate on the debug time controller), §12.3:**
- **Paused:** at a frame-safe point (not inside `DrawUI`), if the post-Apply aggregate needs a larger tier,
  `repo.AddComponent` the larger tier + `BlueprintBlackboardPartitions.CopyToLargerTier` to migrate slots **then
  `repo.RemoveComponent<OldTier>(entity)`** (required — else double-tick + duplicate Extract); then BSA-102
  `DetachFromEntity` removals + `AttachToEntity` adds.
- **Running:** publish BSA-301 `Remove`/`AttachInstanceBlueprintEvent`s to `world.Bus`; the `Input`-phase system
  applies them next frame (removes-before-adds per BSA-301, so in-place swaps reuse freed capacity).

**Implementation note (for testability):** put all logic in a headless `EntityBlueprintsEditModel` with public,
ImGui-free members: `Reality` (from BSA-204's `Read` across tiers), `Intent` (`List<BlueprintAssignmentDto>`),
`Diff` → `{ Added[], Removed[] }`, `Projection` → `{ int Slots; int Bytes; BlackboardTier Tier; UsageStatus Status
}` where `UsageStatus ∈ { Ok, UpgradeNeeded, OverCeiling }`, and `BuildCommitPlan(bool paused)` → an ordered
`CommitPlan` (tier-upgrade step? detach list, attach list / event list). The panel's `DrawUI` only renders the
model and, on Apply, executes the plan via BSA-102 / BSA-301. **All success-condition tests assert on the model /
plan, never on rendering** (header rule 3).

**Success conditions:** (one passing test each)
- **Reality:** entity with two attached blueprints → `model.Reality.Count == 2` with the correct ids+names; the
  `Read` scan allocates nothing (no per-call managed allocation).
- **Diff staging:** stage one add + one remove → `Diff.Added.Count == 1 && Diff.Removed.Count == 1`, **and** the
  live slot table is byte-identical to before (assert no mutation until Apply).
- **Projection:** staged total within the tier → `Status == Ok`; crossing the tier slot/byte bound →
  `Status == UpgradeNeeded` and `Projection.Tier` is the larger tier; exceeding 16 slots / 16096 bytes →
  `Status == OverCeiling` (and the panel disables Apply on `OverCeiling` — assert the model flag the button binds to).
- **Paused commit + tier upgrade:** sim paused, Apply adds that overflow the current tier → after Apply the entity
  has **exactly one** (larger) `BlueprintBlackboard*` component, the old tier component is **absent**
  (`Assert.False(world.HasComponent<OldTier>(e))`), and every intended `BlueprintId` occupies **exactly one** slot
  (assert counts; no duplicates).
- **Running commit + ordering:** sim running, Apply of a same-size swap (remove X + add Y) → the model emits a
  `Remove` then an `Attach` event; after one `Input` tick the slot table matches Intent and the **tier component is
  unchanged** (no upgrade); assert the live blackboard is byte-identical during the publishing frame (no mid-tick
  mutation).
- **Invariant (§2):** attach via the model→Apply, run a preview, then `BlueprintStateTranslator.Extract` → the
  produced `BlueprintAssignmentDto[]` contains **exactly** the assigned `AssetId`s and **no** `Overrides`/drift
  bytes (assert array equality + all `Overrides == null`).

---

## Phase 3 — Dynamic / mid-runtime assignment

### BSA-301: Runtime mutation events + consuming system

**Design:** §7. **Touches:** new event structs + consuming system in `Fdp.Toolkits.Blueprints` (so blueprint nodes
and external systems can use them); `Input` phase.

- `AttachInstanceBlueprintEvent { Entity Entity; int BlueprintId }`,
  `RemoveInstanceBlueprintEvent { Entity Entity; int BlueprintId }`, and
  `ReplaceInstanceBlueprintEvent { Entity Entity; int OldBlueprintId; int NewBlueprintId }` — **unmanaged struct**
  events (`[EventId(...)]`), zero-alloc. (Attach backs both the §8 action node and BSA-205's live-mode add.)
- A system in the **`Input`** phase (mirrors `BehaviorIngressSystem`) drains them and calls the BSA-102 seam:
  Attach → `AttachToEntity`; Remove → `DetachFromEntity`; Replace → `DetachFromEntity(old)` then
  `AttachToEntity(new)` (detach-first per §3). A single net add may trigger a tier upgrade via
  `BlueprintMaintenanceSystem` — acceptable mid-runtime (unlike genesis §5, which pre-provisions).
- **Drain ordering (required, §7):** within a frame, apply **all `Remove` events before any `Attach`** — so an
  in-place swap (remove X + add same-size Y) frees & dense-compacts the slot first and the add reuses that
  capacity, instead of finding the tier full and forcing a spurious upgrade.

**Success conditions:**
- Publish `RemoveInstanceBlueprintEvent` → after the next `Input` tick the slot is gone (dense-compacted) and
  `BlueprintTickSystem` no longer ticks it.
- Publish `ReplaceInstanceBlueprintEvent` → old detached, new attached + `InitDefault`’d, and ticked on the
  subsequent `Simulation` frame (verify the new blueprint's effect).
- Idempotent/no-op: Remove of an absent id, or Replace where old is absent, does not throw.
- **Drain ordering:** publish `Remove(X)` + `Attach(Y)` (same size) into the same frame on an otherwise-full tier →
  after the `Input` tick both resolve with **no** tier upgrade (Y reused X's freed slot); assert the tier component
  is unchanged.

### BSA-302: `[SharedAiAction]` lifecycle node(s)

**Design:** §8 (verified ABI: `(ref Dto, Entity self, EntityRepository world)`). **Touches:** new
`BlueprintLifecycleLibrary` (core, compiled into the engine).

Provide `[SharedAiAction]` static methods, e.g.
`ReplaceInstanceBlueprint(ref ReplaceParams dto, Entity self, EntityRepository world)` and a Remove variant, that
`world.Bus.Publish(...)` the BSA-301 events (target defaults to `self`; optional target pin). Return
`Fbt.NodeStatus.Success`.

**Success conditions:**
- Unit: invoking the action method with a stub `EntityRepository` publishes the correct event (right `self`/target
  + ids) onto `world.Bus` (assert via `Bus.Read<T>()`).
- Codegen/integration: a blueprint graph using the node compiles, and the generated source emits the SharedAiAction
  call passing `self` + `world` (per `InlineActionLowering`); the node appears in the editor palette.
- End-to-end: a blueprint whose action publishes `ReplaceInstanceBlueprintEvent` → BSA-301 system swaps the
  blueprint on the next `Input` phase (one-frame latency, per §8).

---

## Phase 4 — Integration gate

### BSA-401: End-to-end scenario round-trip + dynamic swap (GATE)

**Design:** §1–§8 (whole pipeline). **Touches:** integration test only.

**Success conditions:**
- Author an entity with **two** Instance blueprints; save the scenario → assert JSON has `BlueprintAssignments`
  (two `AssetId`s) and **no** `BlueprintBlackboard*` keys → load → materialize (one tier, one frame) → both tick.
- Round-trip stability: load → save again → byte-identical assignment JSON.
- Dynamic: from a running blueprint action node, publish a Replace → the swap takes effect within one frame.
- Resilience: load a scenario referencing a deleted blueprint AssetId → load succeeds, that assignment is skipped
  with a logged warning.
- Backward-compat: load an old scenario file that still contains a `BlueprintBlackboard1024` key → loads without
  error (black-holed).

### BSA-402: Demo scenario fixture

**Design:** §1–§5, §12. **Depends on:** BSA-203, BSA-204. **Touches:** a committed demo scenario file + a test
referencing it. This is the original motivation — a ready-to-load scenario for repeatable blueprint testing.

Using the BSA-204 inspector, assign one or two Instance blueprints (e.g., `Count4`) to an entity in a small demo
scenario, save it, and commit the resulting `.scenario` (with the `BlueprintAssignments` block) as a test fixture.

**Success conditions:**
- The committed scenario JSON contains a `BlueprintAssignments` array (the chosen `AssetId`(s)) and **no**
  `BlueprintBlackboard*` keys.
- An integration test loads the fixture, materializes (BSA-203), and asserts the blueprint(s) attach and tick
  (e.g., `Count4`'s counter advances) — with **no** manual Compile/attach step.
- The fixture is small and self-contained (documented in the demo's README/onboarding so others can load it).
