# BATCH-08: Slice 2 — Hot-reload ghost-slot fix (re-provision on grown WorkingState)
**Tasks:** S2-3   **Phase:** Slice 2 hot-reload   **Est:** ~12h
**Dependencies:** S2-1, S2-2 (BATCH-06 committed). Touches the same provisioning path.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your contract.
2. `.dev/btree-ai-action-binding/SLICE2-DESIGN.md` §10 Flaw 2 (the mandated fix) + §9 hot-reload.
3. `.dev/btree-ai-action-binding/TASK-DETAIL.md` §S2-3 — the two named tests + exact assertions.
4. `.dev/btree-ai-action-binding/reviews/BATCH-06-REVIEW.md` (DEBT-AIB-027 note) + the S2-2 provisioning helpers you will modify.
5. Codebase-memory MCP first.

## The hazard (verbatim)
On a Hard Reload that GROWS a stateful WorkingState, the old partition slot keeps its smaller `PayloadSize`; the new thunk projects a larger struct over it → silently overwrites the adjacent slot. `ResetSlot` can't help (it zeroes, doesn't resize). Fix: do NOT rely on inline `ResetSlot`; instead `TryDetach` the old slot and re-provision the correctly-sized one — driven by re-publishing `AssignBehaviorEvent`.

## Root-cause already located (dev-lead)
BATCH-06's `BehaviorIngressSystem.AttachSlotsToMemory` **skips a slot whose key is already attached** (`if (TryGetSlotOffset(...)) continue;`). That skip is the ghost-slot bug: after a hard reload grows the WS, the existing (smaller) slot is skipped and keeps its old size. So the core fix lives in the provisioning path, plus a coordinator hook to actually re-run provisioning.

## Key current-code facts (verified by dev-lead — exact paths/lines)
- **Runtime coordinator** = `FDP/Toolkits/Fdp.Toolkits/Behavior/AiHotReloadCoordinator.cs`. `ApplyReload` (~lines 286-335) = HARD reload (full registry replace); `ApplyQuickReload` (~178-222) = soft/merge. Both fire `OnReloadCompleted`. **Investigate first** whether this coordinator (or the host that owns it) has access to the `EntityRepository`/world `Bus` needed to enumerate entities + publish events. (Editor coordinator `Hrot/Subsystems/Hrot.Editor/AiHotReloadCoordinator.cs` `DrainPendingCallbacks` ~235-340 fires `OnReloadCompleted(ReloadCompletedInfo)`.)
- **`BehaviorState`** (`Fdp.Toolkit.Behavior.Components`): `int ActiveBehaviorHash; uint InstanceId; byte BrainTier;`. Enumerate via `repo.Query().With<BehaviorState>()` then filter by `ActiveBehaviorHash`.
- **`AssignBehaviorEvent`** `{ Entity Entity; string BehaviorName; string JsonParams; }`. **There is NO stored JsonParams on the entity.** BehaviorName is recoverable via `BehaviorRegistry.TryGetName(hash, out name)`. Re-publishing with `JsonParams = ""` is acceptable: managed-asset `ParseParams` re-writes baked defaults; runtime per-assignment JSON override is already unsupported (DEBT-AIB-021). (Note in the report that a hard reload thus resets params to defaults — acceptable hard-reload semantics.)
- **Manifest** `BehaviorDefinition.StatefulWorkingSlots : IReadOnlyList<StatefulSlotInfo>`; `StatefulSlotInfo(int SlotKey, int PayloadSize, uint StructureHash)`.
- **Partition API**: `TryDetach(byte* mem, int key)` (returns bytes to free list + dense-compacts slot table); `TryAttach(byte* mem, int key, int size, ulong hash, out int off)` (allocates, sets InstanceVersion=1); `TryGetSlotOffset`; `GetSlot(mem, idx)` returns `ref BlueprintSlotEntry { int BlueprintId; uint InstanceVersion; ushort PayloadOffset; ushort PayloadSize; uint StructureHash; }`. `CopyToLargerTier` for upgrades. (`FDP/Toolkits/Fdp.Toolkits/Blueprints/Partitioning/BlueprintBlackboardPartitions.cs`.)
- **S2-2 provisioning helpers** to modify live in `BehaviorIngressSystem.cs` (BATCH-06): `ProvisionStatefulSlots`, `AttachManifestSlots`, `AttachSlotsToMemory`, `DetachStatefulSlots`, `UpgradeTier`, tier free/used queries.

## Tasks (sequence; do not start Task 2 until Task 1 is implemented + tested + all tests pass)

### Task 1: Ghost-slot-safe re-provisioning in BehaviorIngressSystem (core fix)
**File:** `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/BehaviorIngressSystem.cs`
**Scope:** change the per-slot attach logic so that for each manifest slot it compares the manifest against any **existing** slot with the same key:
- not attached → attach (as today).
- attached with **same** `PayloadSize` AND `StructureHash` → leave it (idempotent; no churn — preserves working state across a no-op/soft reload).
- attached with **different** `PayloadSize` or `StructureHash` → `TryDetach` then re-`TryAttach` at the manifest size/hash. **Before re-attaching, ensure the tier still fits** (recompute aggregate over the manifest incl. the resized slot; trigger the same synchronous `UpgradeTier` path if needed). The resized slot's working state resets (expected on a structural reload). Adjacent slots MUST remain intact (detach dense-compacts + reattach into freed space — verify no overlap).
**Also (DEBT-AIB-027, do now since it gates the detach decision):** make the manifest `StructureHash` **layout-sensitive** so it changes when WorkingState grows/changes. Simplest sufficient approach: fold the resolved `PayloadSize` (and, if cheaply available, field count/types) into the hash in `BTreeBridgeEmitCore.EmitStatefulWorkingSlotsArray` instead of hashing only the type-name string. At minimum the hash MUST differ when the struct size differs. Update the emitter test if it pins the hash. (Note: `PayloadSize` comparison alone already catches the size-growth ghost-slot case; the hash catches same-size layout changes. Implement the PayloadSize comparison as the primary guard regardless.)

**Tests required** (`FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/` — system-level, mirror `BehaviorIngressStatefulTests`):
- `HardReload_GrowsWorkingState_NoNeighborCorruption` — provision two adjacent stateful slots (keyA size 4, keyB size 4); write a sentinel into keyB's payload. Then re-assign the SAME behavior whose manifest now has keyA grown (e.g. size 32, different StructureHash) — i.e. simulate a hard reload that grew keyA's WorkingState. Assert: (a) keyA is now a correctly-sized slot (`GetSlot`/`PayloadSize == 32`), (b) keyB's sentinel bytes are intact (no overflow corruption), (c) both keys still resolve. (If the growth forces a tier upgrade, assert keyB's sentinel survived that too.)
- `HardReload_SameSize_PreservesWorkingState` — re-assign the same behavior with the SAME manifest after writing working-state into a slot; assert the slot is NOT detached/reset (working-state bytes preserved) — proves the idempotent path doesn't churn.

### Task 2: Coordinator re-publishes AssignBehaviorEvent on hard reload
**Files:** `FDP/Toolkits/Fdp.Toolkits/Behavior/AiHotReloadCoordinator.cs` and/or the host that owns the world (investigate).
**Scope:** on a **Hard** reload (`ApplyReload`, not `ApplyQuickReload`), for every entity whose `BehaviorState.ActiveBehaviorHash` is a reloaded BTree behavior, re-publish `AssignBehaviorEvent { Entity, BehaviorName = registry name, JsonParams = "" }` so the Input-phase provisioning (Task 1) detaches old ghost slots and re-provisions correctly. Do **not** call inline `ResetSlot` for BTree synthetic slots.
- **If the coordinator cannot reach the `EntityRepository`/world Bus** through any clean existing hook (a held reference, an injected callback, or `OnReloadCompleted` carrying the world): **STOP and report this as a breaking design flaw** — do NOT introduce a global/static world handle or other hack. Describe the available hooks so the dev-lead can decide.
- If a clean hook exists, use it. Prefer reusing an existing reload-completed event over adding new surface.

**Tests required:**
- `HardReload_RepublishesAssignBehaviorEvent` — set up a world with ≥1 entity whose `BehaviorState.ActiveBehaviorHash` == a registered BTree behavior; trigger the hard-reload path (`ApplyReload` or the chosen hook); assert an `AssignBehaviorEvent` was published for each such entity (read the bus, or assert via the chosen mechanism). Assert the inline-`ResetSlot`-for-BTree path is NOT taken.

## Global rules
- `dotnet build-server shutdown` before codegen verification (Task 1 touches the emitter).
- Byte-identity gate `Hrot.AiEditor.Persistence.Tests` MUST stay green; managed-only guards intact; existing `Managed==false` assets unchanged. Updating the StructureHash algorithm changes only the *stateful-asset* manifest values — no current shipped asset uses `ThreeParamReusableStateful`, so byte-identity is unaffected; confirm.
- Run the FULL touched-project suites green (0 net-new failures); behavior tests via `Behavior` filter. Known non-regressions: 2 MigrationEquivalence; ~24 non-`Behavior` Fdp.Toolkits failures.
- Never weaken a test. Fail loud. Fix root causes. Only stop on the Task-2 coordinator-world blocker described above (or another genuine design contradiction) — write it at the top of the report.

## Success Criteria
- [ ] Task 1: ghost-slot-safe re-provision (PayloadSize/StructureHash-driven detach+reattach + tier-refit) + layout-sensitive StructureHash; both Task-1 tests pass; neighbor-intact proven.
- [ ] Task 2: coordinator re-publishes AssignBehaviorEvent for affected entities on hard reload (or STOP-and-report if no clean world hook); test passes.
- [ ] Clean rebuild 0 errors; byte-identity 129/0; no net-new failures.
- [ ] Report at `.dev/btree-ai-action-binding/reports/BATCH-08-REPORT.md`.

## Report Requirements
Answer: how you made StructureHash layout-sensitive; the exact detach/reattach + tier-refit logic and how you proved neighbor-intact; whether the coordinator had clean world/bus access and which hook you used (or why you stopped); the hard-reload-resets-params-to-defaults semantic; any deviation; weak points; suggested commit message. Do NOT ask comprehension questions.
