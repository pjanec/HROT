# Animation Control — Task Detail

> **Reference design documents** (in `.dev/anim-ctrl/`). This document does
> **not** restate design content — every task references the relevant DD
> chapter(s). Read the DD section together with the task for full context.
>
> - **Mini design:** `AnimationControl_BrainMuscle_MiniDesign_v0_3.md` (entry point / architecture altitude)
> - **DD-1** `DD-1_MuscleCharacterRuntime_v1_2.md` — Muscle runtime, `IAnimationBackend`, ECS systems, slots, capability gating, phase ordering
> - **DD-2** `DD-2_AnimationReplication_v1_1.md` — DDS topics, QoS, intent/status/side-buffer/event translators
> - **DD-3** `DD-3_EventCatalog_AnimationNotify_v1_3.md` — event family, `AnimNotifyCategory`, catalog entries, `[AnimMarkerPicker]`/`[MontagePicker]`, `BP2016`/`BP2017`
> - **DD-4** `DD-4_TKB_AnimationDescriptor_v1_2.md` — `CharacterAnimationDefDto`, translator, query API, `ANIM001`–`ANIM007`
> - **DD-5** `DD-5_BlueprintPrimitives_v1_1.md` — nine AiPrimitive nodes, codegen, `ANIM008`–`ANIM012`
> - **DD-Fake** `DD-Fake_FakeAnimationBackend_v1_1.md` — `FakeAnimationBackend`, `FakeAnimBackendState`, diagnostics
> - **DD-Tests** `DD-Tests_AnimationControl_v1_1.md` — 3-layer test pyramid, 8 integration scenarios, `PumpUntil`
>
> **Scope:** Full surface — fake backend, ECS runtime, TKB, events/catalog,
> Blueprint primitives, **replication (DD-2)**, **Stride backend smoke +
> networked stage-2 tests**, and editor drawers. The networkless stage-1
> pipeline (Phases 0–5, 7) is the verifiable core; Phases 6 and 8 extend it.
>
> **Task IDs:** `ANC-<phase>-<n>`. Distinct from the `ANIM001`–`ANIM012`
> **validator-rule** IDs and `BP2016`/`BP2017` used inside the DDs.
>
> **Status tracker:** see [TASK-TRACKER.md](./TASK-TRACKER.md). Deferred /
> follow-up items: see [DEBT-TRACKER.md](./DEBT-TRACKER.md).

---

## Codebase grounding (verified against the indexed graph)

These existing types are the integration surface the tasks build on. Verified
present unless marked ⚠.

| Concept (DD reference) | Real symbol / location |
|---|---|
| Channel base shape (DD-1 §5.1, mini §3.3) | `LocomotionChannel`/`WeaponChannel`/`InteractionChannel` — `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/ChannelComponents.cs`. Fields: `ushort ActiveAction; uint BehaviorInstanceId; uint ActionInstanceId; uint DispatchedInstanceId; NodeStatus Status; fixed byte Params[..]; fixed byte State[..]` |
| Channel byte budgets (mini §3.3) | `BehaviorConstants` — `ActionParamsByteSize=32`, **`ActionStateByteSIze=32`** (sic — typo in real source), `MaxChannelSizeBytes=96`, `MaxActionTypes=64`. `FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorConstants.cs` |
| Dispatcher base (DD-1 §6) | `DispatcherSystemBase` + `LocomotionDispatcherSystem`/`WeaponDispatcherSystem`/`InteractionDispatcherSystem` — `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/` |
| Capabilities (DD-1 §12/§13, mini §9) | `ActorCapabilities` (enum), `ActorCapabilityState`, `PreviousCapabilities` — `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BehaviorComponents.cs`. `CanMove`=bit0 (test `ActorCapabilities_CanMove_Is_Bit0`) |
| Component IDs | `GlobalComponentIds` — `FDP/Engine/Fdp.Core/GlobalComponentIds.cs` (animation block 220–249 per DD-Fake §11.1) |
| AiPrimitive dispatch (DD-5 §11) | `BlueprintDispatchKind.AiPrimitive`, `AiPrimitiveEmitter`, `AiPrimitiveLowering`, `AiPrimitiveHosting` enum (`BTreeAction`,…), `BlueprintRegistry.RegisterAiPrimitive` — `Hrot.Blueprints.Compiler` + `FDP.Toolkits.Blueprints` |
| WhenNode (DD-5 §8, DD-3 §8) | `WhenNode` + full Stage2/5/6/7 pipeline — `Hrot/Subsystems/Blueprints/` |
| Event catalog (DD-3 §4) | `IEngineEventCatalog`, `BuiltInEngineEventCatalog`, `EngineEventCatalogEntry` — `Hrot.Blueprints.Compiler/Compiler/Catalogs/`; `EngineEventCatalog` — `FDP/Toolkits/Fdp.Toolkits/Blueprints/Catalogs/` |
| TKB translator (DD-4 §4) | `ITkbEntityTranslator` — `FDP/Engine/Fdp.Core/Abstractions/`; `TkbDescriptorRegistry` — `FDP/Toolkits/Fdp.Toolkits/Tkb/` |
| Event translators (DD-2 §5, DD-3 §5) | `INetworkEventTranslator` — `FDP/Engine/Fdp.Core/Abstractions/INetworkEventTranslator.cs` |
| Diagnostic window (DD-Fake §7.3) | `IWindowRegistrar` — `FDP/Engine/Fdp.Presentation/ImGui/`; `SharedAiWindowRegistrar` — `Hrot.Editor.AiShared/Windows/` |
| Editor query API home (DD-4 §5/§9.6) | `Hrot.Editor.AiShared` (has `Catalog/` — `AssetCatalog`, `IAssetCatalog`) |
| Test bootstrap (DD-Tests §2.3) | `SimHostNodeBootstrapper` — `Hrot/Subsystems/Hrot.SimHost/`; `SteppingTimeController` — `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/` |

⚠ **Pre-implementation re-checks (do in ANC-P0-08, log to DEBT-TRACKER if confirmed):**
- DD-Fake §7.3 names `MuscleCharacterHostSubsystem`; the actual host implementing `IWindowRegistrar` is `SimHostSubsystem`. There is no separate Muscle-Character subsystem yet.
- `WeaponChannelTranslator` was **deleted** in `cgf-scn-2`. DD-2 assumes "existing channel intent/status translator precedent." Confirm the current channel-replication mechanism (SmartEgress / descriptor egress) before implementing DD-2 translators.
- DD code uses `channel.ActionParams`/`ActionState`; real fields are `Params`/`State`. All codegen and test helpers must use the real names.

---

# Phase 0 — Foundations & shared contracts

**Goal:** Land the enum, capability bits, component IDs, channel param structs,
all ECS component definitions, and the `IAnimationBackend` interface so every
later phase compiles against fixed contracts.

### ANC-P0-01 — `AnimNotifyCategory` canonical enum
**Refs:** DD-3 §2; DD-1 §3 (note); DD-4 §2 (note).
Create the `byte` enum in `Hrot.Animation.Events` with values `Generic=0, Footstep=1, HitWindowOpened=2, HitWindowClosed=3` and reserved lifecycle values documented but unused on markers.
**Success:** Unit test asserts byte values match DD-3 §2; `RawNotifyEvent.Kind` and `NotifyMarkerDefDto.Kind` both reference this single type (no local `NotifyKind`/`NotifyMarkerKind` duplicate compiles).

### ANC-P0-02 — `ActorCapabilities` animation bits
**Refs:** mini §9; DD-1 §12.
Add `CanPlayAnimations=8`, `CanChangeStance=16`, `CanAim=32` to the existing `ActorCapabilities` `[Flags]` enum (`BehaviorComponents.cs`). Do **not** renumber existing bits.
**Success:** Test asserts the three new bits have values 8/16/32 and existing `CanMove=1`/`CanShoot=2`/`CanInteract=4` are unchanged; enum still fits a byte.

### ANC-P0-03 — `GlobalComponentIds` allocations (220–249)
**Refs:** DD-Fake §11.1.
Reserve and assign IDs in the 220–249 animation block for all animation components introduced in P0-05/P0-06 and `FakeAnimBackendState` (=240). One ID per component; document the block boundary.
**Success:** `GlobalComponentIds` compiles; a test asserts no duplicate IDs in 220–249 and that `FakeAnimBackendState`=240.

### ANC-P0-04 — Channel param/state structs + action-id constants
**Refs:** mini §3.2, §3.3; DD-1 §6; DD-5 §3.
Define `PlayMontageParams`, `StopMontageParams`, `PlayMontageQueueParams`, `LookAtPointParams`, `LookAtEntityParams`, `ReleaseLookParams` (each `unmanaged`, ≤32 B), and `AnimationActionIds`/`LookAtActionIds` (`ushort` constants matching `ActiveAction`). No `ClearMontageQueue` action id (queue truncation is side-buffer mutation — mini §3.2, DD-1 §6.4).
**Success:** Layout test: every params struct `sizeof ≤ BehaviorConstants.ActionParamsByteSize` (32). Each round-trips through a `fixed byte Params[32]` blob (`WriteParams`/read) without corruption.

### ANC-P0-05 — Replicated/contractual components
**Refs:** DD-1 §5.1; mini §3.3, §4.1.
Define `AnimationChannel`, `LookAtChannel` (channel shape, `Params`/`State` blobs), `StanceIntent`, `StanceStatus` (+`StanceTransitionPhase` enum), `StanceId` enum (DD-4 §3.2), `AnimationMontageQueue` (`[InlineArray(8)]` `Entries`, `Count`, `QueueVersion`), `MontageQueueEntry`, `AnimationMontageQueueState`. All `[ComponentId]` from the P0-03 block, `[DataPolicy(NoSave)]`.
**Success:** Layout tests: each channel ≤96 B; `AnimationMontageQueue` total ≤140 B; `AnimationMontageQueueState` and stance components =16 B. `[InlineArray]` mutation via Span-cast verified by a write-read test.

### ANC-P0-06 — Muscle-internal components
**Refs:** DD-1 §5.2.
Define `CharacterAnimationDefRuntime` (handle into per-class baked data + `BackendHandle`, `StanceCount`, `SlotCount`), `AnimationExecutorState` (`[InlineArray]` slot table, `MaxSlots=8`), `LookAtExecutorState`. Not replicated.
**Success:** Compile + layout test confirming `MaxSlots=8`; component-id assigned from the block; not registered with any egress.

### ANC-P0-07 — `IAnimationBackend` interface + supporting types
**Refs:** DD-1 §3.
Define `IAnimationBackend` and supporting types: `AnimationBackendHandle`, `SlotId`, `MontageAssetId`, `MontagePlaybackState`, `StanceTransitionState`, `RawNotifyEvent` (Kind = `AnimNotifyCategory`), `AnimationBackendConfig`, `AnimationBackendMetrics`. No Stride/engine-specific types leak (DD-1 §16).
**Success:** Interface compiles in `Hrot.MuscleCharacter.Animation`; a mock implementation in tests satisfies all members; no reference to any 3D-engine type.

### ANC-P0-08 — Verification spike (dependency re-checks)
**Refs:** "Codebase grounding" ⚠ list above; DD-1 §6, §17; DD-2 §2; DD-Fake §7.3.
Confirm the real `DispatcherSystemBase<TChannel>` generic signature and base hooks; the current channel intent/status replication mechanism (since `WeaponChannelTranslator` was deleted); and the host subsystem that should register the diagnostic window. Update affected task descriptions; log confirmed mismatches to DEBT-TRACKER.
**Success:** A short findings note appended here / to DEBT-TRACKER; DD-2 and DD-Fake tasks reference the confirmed real symbols.

---

# Phase 1 — `FakeAnimationBackend` (DD-Fake)

**Goal:** A deterministic, render-free `IAnimationBackend` whose entire per-entity
state lives in one Tier-1 component, validated by Layer-1 unit tests.

### ANC-P1-01 — `FakeAnimBackendState` component + sub-structs
**Refs:** DD-Fake §2 (§2.1–2.4).
Define the unmanaged component and `FakeSlotState`/`FakeAimState`/`FakeStanceState`, `[InlineArray(8)]` `FakeSlotsBuffer`, `[InlineArray(16)]` `FakePendingNotifyBuffer`, bools-as-bytes, `ulong FiredNotifyMask`. `[ComponentId(240)]`, `[DataPolicy(NoSave)]`.
**Success:** Size test ≈1 KB and <64 KB; layout deterministic (sequential). Compiles in `Hrot.MuscleCharacter.Animation.Fake`.

### ANC-P1-02 — Backend scaffold: `Initialize`, handle table, Register/Unregister
**Refs:** DD-Fake §3, §3.1, §3.2.
Implement the class, idempotent component registration, generation-counted handle slots, `RegisterEntity`/`UnregisterEntity`/`TryResolve`. Initialize stance to `def.SupportedStances[0]`.
**Success:** Unit tests: register returns valid handle; stale handle after unregister resolves false; re-register bumps generation.

### ANC-P1-03 — Slot operations
**Refs:** DD-Fake §3.3.
`PlayMontageOnSlot`, `CrossfadeMontageOnSlot` (= Play), `StopMontageOnSlot` (force blend-out), `QuerySlotState`. Span-cast mutation throughout.
**Success:** Layer-1 tests `PlayMontage_SetsSlotActive`, `PlayMontage_OverwritesPreviousMontageInSameSlot`, `PlayMontage_UnknownMontage_NoOps` (DD-Tests §3.2) pass.

### ANC-P1-04 — Locomotion / aim / stance operations
**Refs:** DD-Fake §3.4.
`UpdateLocomotionInputs`, `SetAimTarget` (first-acquire snaps), `ReleaseAim`, `RequestStanceChange`, `QueryStanceTransition`.
**Success:** Layer-1 aim tests (`SetAimTarget_ActivatesAimWithBlendInWeight`) and stance tests (`RequestStanceChange_StartsTransition`) per DD-Tests §3.2 pass.

### ANC-P1-05 — Notify drain + hard-assert + metrics
**Refs:** DD-Fake §3.5, §4.4, §3.6, §6.
`DrainNotifies` (shift remainder if dest smaller), `EmitNotify` (throws `InvalidOperationException` on 17th), `SnapshotMetrics`.
**Success:** `DrainNotifies_TransfersAllPendingToDest`, `DrainNotifies_HandlesSmallerDestBuffer`, `EmitNotify_OverflowThrowsInvalidOperationException` (DD-Tests §3.2) pass.

### ANC-P1-06 — Tick algorithm (slot/aim/stance advance)
**Refs:** DD-Fake §4, §4.1–4.3.
ECS-query tick; `AdvanceSlot` (blend weight, notify-crossing via `FiredNotifyMask`, natural end), `AdvanceAim`, `AdvanceStance`.
**Success:** `Tick_AdvancesElapsedTimeByDeltaTimesPlayRate`, `Tick_DeactivatesSlotOnNaturalCompletion`, `Tick_FiresNotifyWhenElapsedCrossesTimeSeconds`, `Notify_FiresExactlyOncePerPlay`, `PlayMontage_ResetsFiredNotifyMask`, `Tick_RampsAimBlendWeight`, `Tick_CompletesStanceTransition` pass.

### ANC-P1-07 — Synthetic footstep emission
**Refs:** DD-Fake §5.
`FakeBackendConstants` (`MinFootstepSpeed=0.3`, `FootstepStrideMeters=0.9`), `AdvanceFootsteps`, alternating foot, `PayloadVector` left zero (enriched downstream).
**Success:** `Footstep_EmitsAtStrideDistance`, `Footstep_AlternatesFeet`, `Footstep_NoEmissionWhenStill`, `Footstep_NoEmissionWhenAirborne` pass.

### ANC-P1-08 — Layer-1 unit test suite
**Refs:** DD-Tests §3 (§3.1–3.3).
Create `Hrot.MuscleCharacter.Animation.Fake.Tests` (xUnit), fixture per §3.1, all ~18 cases. JSON dump on failure.
**Success:** ~18 tests green, <0.5 s total; each independently runnable.

### ANC-P1-09 — Diagnostic ImGui window
**Refs:** DD-Fake §7 (§7.1–7.3).
`FakeAnimBackendInspectorWindow : IDiagnosticWindow`, list + detail views, registered via `IWindowRegistrar` on the **confirmed host subsystem** (per P0-08; not the non-existent `MuscleCharacterHostSubsystem`), headless-guarded.
**Success:** `DiagnosticsWindowTests`-style test (mirroring `Hrot.Editor.AiShared.Tests/Windows`) asserts registration in non-headless and no registration in headless. (UI rendering itself is manual.)

### ANC-P1-10 — JSON snapshot export + AAR integration
**Refs:** DD-Fake §8, §9.
`FakeAnimBackendSnapshotJson.Serialize` with `MontageAssetId`/`MarkerHash`→name resolution; "Copy JSON" button; rely on Tier-1 recorder fast-path.
**Success:** Serializer test: a known state produces JSON with expected slot/aim/stance fields and resolved montage/marker names.

---

# Phase 2 — TKB animation descriptor (DD-4)

**Goal:** Design-time JSON → runtime `CharacterAnimationDefRuntime`, plus the
editor query API and validation rules.

### ANC-P2-01 — `CharacterAnimationDefDto` + nested DTOs
**Refs:** DD-4 §2.
`CharacterAnimationDefDto` (`[TkbDescriptor("Anim.CharacterDef")]`) + `SlotDefDto`, `MontageDefDto`, `MontageNotifyRefDto`, `StanceTransitionDto`, `AimConfigDto`, `NotifyMarkerDefDto` (Kind = `AnimNotifyCategory`), `SlotCompositingMode`.
**Success:** Deserialization test from the §8.1 Sniper JSON; `[TkbDescriptor]` registers with `TkbDescriptorRegistry` (mirror `WeaponCapabilitiesDto_CarriesTkbDescriptorAttribute`).

### ANC-P2-02 — Stable ID hashing
**Refs:** DD-4 §3.1, §3.4.
`MontageAssetId = (int)(FNV1a64(name) & 0x7FFFFFFF)`; `MarkerHash = FNV1a32(name)`. Shared helper reused by editor + tests + `BakeForTest`.
**Success:** Determinism test (same name → same id across runs); known-vector test for at least one name.

### ANC-P2-03 — `AnimationTkbTranslator.Inject`
**Refs:** DD-4 §4, §5.3 (DD-1), §4.2.
Implement `ITkbEntityTranslator`; `GetConsumedDescriptors` yields the DTO; inject all replicated + Muscle-internal components, each guarded by `IsComponentTypeRegistered<T>()`; `LookAtChannel`/`LookAtExecutorState` only when `AimConfig != null`. No ordering deps.
**Success:** Translator test: promoting a Sniper template attaches the exact component set from DD-4 §8.3 (incl. conditional look-at components).

### ANC-P2-04 — Per-class baked cache + hot reload
**Refs:** DD-4 §4.1, §7, §9.1.
`ConcurrentDictionary<long, CharacterAnimationBakedData>`, `GetOrBake`, subscribe `ITkbHotReloadEvents.DescriptorChanged`, invalidate on change, `IDisposable` unsubscribe.
**Success:** Test: bake once → cached; `DescriptorChanged` for the class evicts; next promote re-bakes; unrelated descriptor change is ignored.

### ANC-P2-05 — `CharacterAnimationDefRuntime` baking + `BakeForTest`
**Refs:** DD-4 §4 (`BakeDef`), §8.3; DD-Tests §8, §11.2.
Build montage dict, stance set, transition table, slot table (priority-sorted), aim snapshot. Expose `BakeForTest(dto)` via `[InternalsVisibleTo("Hrot.Animation.Integration.Tests")]`.
**Success:** `TryGetMontageInfo` resolves a baked montage's slot/duration/notifies; `BakeForTest` produces equivalent data to the production path (parity test).

### ANC-P2-06 — `IAnimationTkbQueries` editor query API
**Refs:** DD-4 §5, §9.6.
Interface + `AnimationTkbQueries` impl in `Hrot.Editor.AiShared.Catalog`: `GetPlayableMontages` (excludes `IsStanceTransition`), `GetMontage`, `GetSupportedStances`, `SupportsAim`, `GetAvailableMarkers`, `GetMarkerName`, `ResolveMontageId`. Reuse current target-class context.
**Success:** Tests against the Sniper DTO: playable list excludes `Trans_*`; `GetAvailableMarkers` returns the marker union; `ResolveMontageId("Reload_Rifle")` matches the P2-02 hash.

### ANC-P2-07 — Validators ANIM001–ANIM007
**Refs:** DD-4 §6.
ANIM001 (montage exists), ANIM002 (stance supported), ANIM003 (aim config present), ANIM004 (marker exists — warning), ANIM005 (chain same-slot), ANIM006 (DTO transition montage exists), ANIM007 (DTO notify marker exists). DTO-level (006/007) run at TKB load.
**Success:** One positive + one negative compiler/loader test per rule; severities match DD-4 §6 (006/007/001/002/003/005 errors, 004 warning).

### ANC-P2-08 — TKB translator/query test suite
**Refs:** DD-Tests §11.2 (dedicated translator tests).
Dedicated tests for the translator + cache + queries (separate from animation system tests).
**Success:** Suite green; covers inject, hot-reload invalidation, query filtering.

---

# Phase 3 — Muscle ECS systems (DD-1)

**Goal:** The seven new systems + cleanup + capability-reactor extension, wired
in the correct phase order, validated by Layer-2 system tests.

### ANC-P3-01 — `AnimationDispatcherSystem`
**Refs:** DD-1 §6 (§6.1–6.5), §12.
`DispatcherSystemBase<AnimationChannel>` (real signature per P0-08), `PreSimulation`. ProcessPlayMontage / PlayMontageQueue / StopMontage; capability `CanPlayAnimations`; stages executor state only (no direct backend calls).
**Success:** `PlayMontageCommand_TriggersBackendPlay`, `..._NoCapability_FailsImmediately`, `..._UnknownMontage_FailsImmediately`, `SameInstanceId_NoActionTaken` (DD-Tests §4.2).

### ANC-P3-02 — `LookAtDispatcherSystem`
**Refs:** DD-1 §8, §12.
`PreSimulation`; LookAtPoint/Entity/ReleaseLook; `CanAim` (not for Release); entity-mode stores target for per-tick resolution.
**Success:** System tests: each action sets `LookAtExecutorState` correctly; release sets up blend-out; `CanAim` absent → `Status=Failure`.

### ANC-P3-03 — `StanceTransitionSystem`
**Refs:** DD-1 §9, §12, §13.
Descriptor-pair driver (not a dispatcher); `Version`/`AckVersion`; `CanChangeStance` (silently ack on absence); drives backend `RequestStanceChange`; updates `StanceStatus`; stages `StanceChangedEvent`.
**Success:** System tests: new version triggers transition; same-stance target → immediate Completed; missing capability silently acks.

### ANC-P3-04 — `MontageQueueAdvanceSystem`
**Refs:** DD-1 §7 (§7.1).
`Simulation` (early); `QueueVersion` observation; blend-out → crossfade-to-next or natural end; the 1-frame mid-blend-out race is documented behavior.
**Success:** System test with fake backend: advances `CurrentEntryIndex` when slot enters blend-out and a next entry exists; ends queue when none.

### ANC-P3-05 — `AnimationRuntimeBridgeSystem`
**Refs:** DD-1 §10, §17.
`Simulation` (mid); first-tick `RegisterEntity`; pump locomotion inputs; apply staged montage/crossfade/stop; resolve look-at (entity alive check); calls `backend.Tick` once after the per-entity loop.
**Success:** System test: staged play results in `backend.PlayMontageOnSlot` with correct args; entity-mode look-at resolves world point from `SimTransform`.

### ANC-P3-06 — `NotifyEventEmitterSystem`
**Refs:** DD-1 §11 (§11.1); DD-Fake §11.3.
`PostSimulation` (early); drains `RawNotifyEvent`s; maps to typed events; enriches `FootstepEvent.WorldPosition` from `SimTransform`; discards lifecycle events from drain.
**Success:** System test: footstep raw event → `FootstepEvent` with `WorldPosition` from transform; generic → `AnimNotifyEvent`; lifecycle kinds discarded.

### ANC-P3-07 — `AnimationStateReporterSystem`
**Refs:** DD-1 §18, §11.1.
`PostSimulation` (late, after bridge tick); synthesizes `MontageStarted/Ended/SectionAdvanced` + `StanceChanged`; writes `Status=Success`; queue completion → clears state + Success; EndReason classification (Natural/Interrupted/BlendedOutByNext/Failed).
**Success:** `OnNaturalCompletion_WritesStatusSuccess`, `OnNaturalCompletion_PublishesMontageEndedEvent`, `OnInterruption_PublishesEventWithReasonInterrupted` (DD-Tests §4.2).

### ANC-P3-08 — `AnimationBackendCleanupSystem`
**Refs:** DD-1 §14, §20.5, §17.
`PostSimulation` (late); watches `PendingDestroy` + `CharacterAnimationDefRuntime`; calls `UnregisterEntity`; clears handle; runs before chunk reaper.
**Success:** Test: tagging an entity `PendingDestroy` triggers exactly one `UnregisterEntity`; handle cleared.

### ANC-P3-09 — Capability-change reactor extension
**Refs:** DD-1 §13, §20.6, §20.7.
**Extend the existing** capability reactor (do not add a system): on `CanPlayAnimations` high→low force-stop slots + `Status=Failure` + bump `DispatchedInstanceId` + clear queue state; on `CanAim` loss stage ReleaseAim + `Status=Failure`; on `CanChangeStance` loss let in-flight transition finish. Uses `PreviousCapabilities`.
**Success:** Tests: mid-montage `CanPlayAnimations` loss stops slots and fails channel; `CanAim` loss releases aim; in-flight stance transition not snapped. (P0-08 confirms the exact reactor to extend.)

### ANC-P3-10 — Phase-ordering registration
**Refs:** DD-1 §17, §2.
Register all systems in the documented `PreSimulation`/`Simulation`/`PostSimulation` slots, preserving the invariants (backend tick before drains; reporter after bridge; cleanup before reaper; status writes before egress).
**Success:** A registration/ordering test asserts relative order of the eight systems + reactor matches DD-1 §17.

### ANC-P3-11 — Layer-2 system test suite
**Refs:** DD-Tests §4 (§4.1–4.3).
Create `Hrot.MuscleCharacter.Animation.Tests`; per-system fixtures per §4.1; ~10–12 tests.
**Success:** Suite green, <1 s; each system tested in isolation against the fake backend.

---

# Phase 4 — Events & Engine Event Catalog (DD-3)

**Goal:** The eight event types, picker attributes, catalog registrations, and
the two When-node validator rules.

### ANC-P4-01 — Eight event types + mandatory attributes
**Refs:** DD-3 §3 (§3.1–3.2), §9.7.
`MontageStartedEvent`/`MontageEndedEvent`(+`MontageEndReason`)/`MontageSectionAdvancedEvent`/`StanceChangedEvent`/`FootstepEvent`/`HitWindowOpenedEvent`/`HitWindowClosedEvent`/`AnimNotifyEvent`, each `[EventId(82xx)]` (**8201–8213, block 8200–8299**) + `[DataPolicy(NoRecord)]`, `Entity Target` first.
> ⚠ **Architect ruling — supersedes DD-3 §3/§9.7.** The original `8000–8099`
> block is **revoked**: `Hrot.Common.Events.GlobalActionRequestedEvent` already
> occupies `[EventId(8059)]` ([verified](Hrot/Engine/Hrot.Common/Events/GlobalActionRequestedEvent.cs)),
> which would hard-crash `EventTypeRegistry` or interlace with animation ids.
> Use **`8200–8299`** instead; assign `8201`…`8213` in the same order DD-3 §3
> lists `8001`…`8013` (Started=8201, Ended=8202, SectionAdvanced=8203,
> StanceChanged=8204, Footstep=8210, HitWindowOpened=8211, HitWindowClosed=8212,
> AnimNotify=8213). DD-3 doc text still says 8000–8099 — see DEBT-TRACKER.
**Success:** Test: all eight registered with `EventTypeRegistry`, ids in 8200–8299, no collision with 8059 or any existing id; `Target` field present.

### ANC-P4-02 — Picker attributes + drawers
**Refs:** DD-3 §3.3, §3.4; DD-5 §7.
`[AnimMarkerPicker]` on `AnimNotifyEvent.MarkerHash`; `[MontagePicker]` on every `MontageId` field (lifecycle + hit-window + generic). Property-drawer dispatch substitutes dropdowns sourced from `IAnimationTkbQueries.GetAvailableMarkers` / `GetPlayableMontages`; mirror `[HsmEventPicker]`/`[MapPickableEntity]`.
**Success:** Drawer tests: a `uint MarkerHash`/`int MontageId` field with the attribute renders the name dropdown and resolves the picked name to the compile-time hash.

### ANC-P4-03 — Catalog entries (incl. FootstepEvent exclusion)
**Refs:** DD-3 §4 (§4.1–4.3), §5.2, §5.3, §6.
Register the eight events into `BuiltInEngineEventCatalog` (display name, `Animation/*` category, `TargetFieldName="Target"`, filterable fields, QoS=Reliable). `FootstepEvent`: Muscle-side catalog only, `PropagatesAcrossNodes=false`, absent from Brain dropdown.
**Success:** Extend `BuiltInEngineEventCatalog_HasExpectedEntries` to assert the seven Brain-visible animation entries with correct categories/target field, and that `FootstepEvent` is excluded Brain-side.

### ANC-P4-04 — `BP2016` / `BP2017` validator rules
**Refs:** DD-3 §6.1 (BP2016 warning — When-node on BestEffort event), §5.2 (BP2017 error — Brain When-node on `PropagatesAcrossNodes=false`).
Extend the When-node Stage-2 validator (`V_WhenNodeRules`).
**Success:** Validator tests: BestEffort-event When-node emits BP2016 (warning, non-blocking); Brain-targeted When-node on a local-only event emits BP2017 (error).

---

# Phase 5 — Blueprint authoring primitives (DD-5)

**Goal:** Nine AiPrimitive nodes + getters, codegen with `[InlineArray]` safety,
and four authoring validators, usable across BTree/HSM/Blueprint.

### ANC-P5-01 — `PlayMontageNode` + `StopMontageNode`
**Refs:** DD-5 §3.1, §3.2; §14.4 (`-1f` default sentinel).
Drawers + codegen writing `AnimationChannel` (real `Params` blob, `ActionInstanceId++`). `-1f` blend-time → resolved to TKB default at input stage.
**Success:** Lowering/emit golden tests (mirror `AiPrimitive_WithChannelCommand_*`) produce the DD-5 §3.1/§3.2 code; ANIM001 enforced on the picked montage.

### ANC-P5-02 — Queue-mutation nodes (`PlayMontageChain`/`Enqueue`/`ClearQueue`)
**Refs:** DD-5 §3.3, §3.4, §3.5, §9; DD-1 §4.3; DD-2 §4.1.
Codegen must use Span-cast (Pattern A) or Get→Mutate→Set (Pattern B); bump `QueueVersion`; Enqueue/Clear do **not** bump `ActionInstanceId`; capacity guard; chain length ≤8.
**Success:** §9.4 regression test: compile+execute a 3-entry chain, assert `Count==3` and `Entries[0..2]` carry expected montage ids (catches silent write-loss). Enqueue at capacity is a logged no-op.

### ANC-P5-03 — `SetStanceNode`
**Refs:** DD-5 §4.1.
Descriptor write bumping `StanceIntent.Version`; stance dropdown filtered by `GetSupportedStances`; `-1f` blend = TKB default.
**Success:** Emit test matches DD-5 §4.1; ANIM002 enforced.

### ANC-P5-04 — Look-at nodes
**Refs:** DD-5 §5.1–5.3.
`LookAtPointNode`/`LookAtEntityNode`/`ReleaseLookNode` writing `LookAtChannel`.
**Success:** Emit tests match §5; ANIM003 enforced on Point/Entity (not Release).

### ANC-P5-05 — Getter nodes
**Refs:** DD-5 §6.1, §6.2; §14.3.
`GetMontageQueueProgressNode` (4 outputs), `GetCurrentStanceNode` (3 outputs), pure reads.
**Success:** Emit tests produce the RO-read codegen; no `ActionInstanceId` reference.

### ANC-P5-06 — Validators ANIM008–ANIM012
**Refs:** DD-5 §10; §3.3 (ANIM012).
ANIM008 (enqueue without chain — warning), ANIM009 (release without acquire — warning), ANIM010 (codegen self-check — internal), ANIM011 (cross-subsystem context — error), ANIM012 (chain length >8 — error). Per-graph data-flow analysis only (§14.1).
**Success:** Positive/negative test per rule; ANIM010 has a dedicated emitted-AST Pattern-A/B recognition test in the compiler suite.

### ANC-P5-07 — AiPrimitive registration + cross-subsystem reuse
**Refs:** DD-5 §11; §1.
Register all nine action nodes + two getters as AiPrimitives (`RegisterAiPrimitive`, `AiPrimitiveHosting`), usable as BTree action, HSM action body, and Blueprint imperative node.
**Success:** Reuse test: the same primitive compiles/dispatches in a BTree-action and an HSM-action hosting (mirror `AiPrimitive_WithAllDecorations_*`).

### ANC-P5-08 — `PlayMontageChainNode` custom drawer (editor)
**Refs:** DD-5 §14.5.
Custom array drawer (add/remove/reorder/per-entry sub-drawer). Editor-team-owned ticket — tracked here for completeness; runtime side (P5-02/06) does not block on it.
**Success:** Drawer renders chain entries with per-entry montage picker + blend/rate/section; or, if deferred, a DEBT-TRACKER entry referencing the editor ticket.

---

# Phase 6 — Replication (DD-2)

**Goal:** Cross-node DDS for the animation contract. Depends on P0-08 confirming
the current channel-replication mechanism.

### ANC-P6-01 — `AnimationChannel` intent/status translators
**Refs:** DD-2 §2.1, §2.2, §2.4; §6.
Intent egress (Brain) / ingress (Muscle) + status egress (Muscle) / ingress (Brain); SmartEgress dirty on `ActionInstanceId`+`Params`; topics + Reliable/TransientLocal. `State` not replicated.
**Success:** Round-trip test (loopback or translator unit): intent write on Brain ghost appears on Muscle ghost; status write propagates back; `State` blob never shipped.

### ANC-P6-02 — `LookAtChannel` intent/status translators
**Refs:** DD-2 §2.3.
Same pattern; entity-ref params resolved via `NetworkEntityMap`.
**Success:** Round-trip test incl. `LookAtEntity` target resolution.

### ANC-P6-03 — Stance descriptor translators
**Refs:** DD-2 §3.
`StanceIntent`/`StanceStatus` egress/ingress; `TransitionProgress` in payload but excluded from dirty trigger.
**Success:** Round-trip test; egress fires on Phase/CurrentStance/AckVersion change, not on `TransitionProgress` alone.

### ANC-P6-04 — Side-buffer replication
**Refs:** DD-2 §4 (§4.1–4.5); §10.2.
`AnimationMontageQueue` egress (dirty on `QueueVersion`, ships only `Count` live entries) + ingress (zeros tail); `AnimationMontageQueueState` egress (dirty on `CurrentEntryIndex`/`InBlendOutWindow`, `EntryElapsedSeconds` ride-along). `ObservedQueueVersion` not replicated.
**Success:** Serializer test: `Count=3` ships ≤60 B and deserializes with entries 0–2 set, 3–7 zeroed; `QueueVersion` bump drives egress, scalar `EntryElapsedSeconds` change alone does not.

### ANC-P6-05 — Seven event translator pairs
**Refs:** DD-2 §5; DD-3 §5 (§5.1–5.3), §6.
`INetworkEventTranslator`/ingress for the seven cross-node events (all except `FootstepEvent`), Reliable+Volatile, keyed on `Target`.
**Success:** Per-event serialize/deserialize round-trip test; `FootstepEvent` has no translator.

### ANC-P6-06 — Topic/QoS registration + observability
**Refs:** DD-2 §6 (§6.1), §9 (§9.1).
Register all 15 topics with the documented QoS; instrument publish rate / bandwidth / dirty false-positive / round-trip latency hooks.
**Success:** Topic-table test asserts 15 topics with correct Reliability/Durability; instrumentation counters exist.

---

# Phase 7 — Integration tests, networkless stage-1 (DD-Tests)

**Goal:** The eight end-to-end scenarios over the full Muscle pipeline + fake
backend, sharing one bootstrap.

### ANC-P7-01 — `PumpUntil` + `IPumpableHarness` (shared infra)
**Refs:** DD-Tests §5.2, §7.1, §11.3.
Promote `PumpUntil`/`PumpFrames` + `IPumpableHarness` to the shared integration-test infra project (frame-budgeted, throws `TimeoutException` with diagnostic dump).
**Success:** Unit test: condition met returns early; never-true condition throws after `maxFrames` with the named condition + dump in the message.

### ANC-P7-02 — Animation diagnostics + command helpers
**Refs:** DD-Tests §7.2, §7.3, §7.4.
`DumpAnimationDiagnostics` (animation-test-local), `WriteParams<T>`, `IssuePlayMontage`.
**Success:** Helper tests / used by P7-04+; `WriteParams` throws when `sizeof(T) > 32`.

### ANC-P7-03 — Integration fixture + inline TKB test data
**Refs:** DD-Tests §5.1, §8.
`AnimationIntegrationFixture : IPumpableHarness, IDisposable` over `SimHostNodeBootstrapper(networkFactory:null)`; `SpawnHumanoid`, `ResetWorld`; `TestData.MinimalCharacterDef()` via `BakeForTest`. `IClassFixture` shared across scenarios.
**Success:** Fixture bootstraps once; `ResetWorld` destroys test entities + drains bus; a smoke test spawns + ticks without error.

### ANC-P7-04 — Scenario 1: happy-path single montage
**Refs:** DD-Tests §6 Scenario 1.
**Success:** `PlayMontage_RunsToCompletionAndReportsSuccess` — dispatcher ack → Running → Success; `MontageEndedEvent{NaturalEnd, Reload id}` observed.

### ANC-P7-05 — Scenario 2: notify at keyframe
**Refs:** DD-Tests §6 Scenario 2.
**Success:** `PlayMontage_NotifyFiresAtAuthoredKeyframe` — `AnimNotifyEvent` for `MagOut` received within budget.

### ANC-P7-06 — Scenario 3: stop → Interrupted
**Refs:** DD-Tests §6 Scenario 3.
**Success:** `StopMontage_MidPlayInterruptsAndPublishesInterruptedEvent` — `MontageEndedEvent.EndReason==Interrupted`.

### ANC-P7-07 — Scenario 4: stance transition
**Refs:** DD-Tests §6 Scenario 4.
**Success:** `StanceIntent_DrivesTransitionAndPublishesStanceChangedEvent` — `CurrentStance==Crouched`; single `StanceChangedEvent{Standing→Crouched}`.

### ANC-P7-08 — Scenario 5: montage chain via queue
**Refs:** DD-Tests §6 Scenario 5.
**Success:** `PlayMontageQueue_ThreeEntriesPlaysInOrderAndReportsOneSuccess` — all three `QueueIndex` started; one final Success.

### ANC-P7-09 — Scenario 6: enqueue mid-play
**Refs:** DD-Tests §6 Scenario 6.
**Success:** `EnqueueMontage_DuringActiveQueueAppendsAndPlays` — appended entry (no `ActionInstanceId` bump) starts.

### ANC-P7-10 — Scenario 7: footstep cadence
**Refs:** DD-Tests §6 Scenario 7.
**Success:** `Locomotion_DrivesFootstepEventsAtCorrectCadence` — 5–8 footsteps over 3 s at 2 m/s, feet alternate.

### ANC-P7-11 — Scenario 8: look-at acquire/release
**Refs:** DD-Tests §6 Scenario 8.
**Success:** `LookAtPoint_AcquiresAndReleasesAimWithStatusTransitions` — Running on acquire, Success after release.

---

# Phase 8 — Stride backend + networked stage-2 (full-surface extension)

**Goal:** A real Stride `IAnimationBackend` (smoke-tested, not full-parity) and a
networked replication test suite. Both explicitly lower priority than the
stage-1 slice (DD-Tests §10, §11.4); sequence after Phases 0–7 are green.

### ANC-P8-01 — `StrideAnimationBackend` skeleton
**Refs:** DD-1 §15 (§15.1–15.2), §16.
Class + per-entity entry pool + `PerEntityBlendTreeBuilder : IBlendTreeBuilder`; implement all `IAnimationBackend` members against Stride; no engine types leak past the `.Stride` namespace.
**Success:** Compiles; a single-entity play/tick produces a non-crashing blend-tree build (no assertion on visual output).

### ANC-P8-02 — Stride scene/transform + notify mapping
**Refs:** DD-1 §15.3, §15.4.
Per-entity Stride entity placement (option A or B per existing rendering bridge — decide at impl time), clip-marker callback → `RawNotifyEvent`.
**Success:** Smoke test: a clip with a keyframed marker fires a `RawNotifyEvent` drained by the same path the fake uses.

### ANC-P8-03 — `StrideBackendSmokeTest` suite
**Refs:** DD-Tests §11.4.
Small suite: engine boots, pipeline ticks without crash — **not** a re-run of the eight AI-behavior scenarios.
**Success:** Suite green; boots Stride backend behind the same `IAnimationBackend` seam.

### ANC-P8-04 — Networked stage-2 integration suite
**Refs:** DD-Tests §10; DD-2 §8.
`Hrot.Animation.Network.Integration.Tests` over `HrotRunnerHarness` "simhost,cgf" loopback; reuse the eight scenarios with two `BootstrapNode` calls + extra round-trip frames.
**Success:** The eight scenarios pass across the Brain↔Muscle DDS round-trip (intent→status latency ~2 ticks/direction).

---

## Final coverage map — every resolved "final idea/issue" → task

Each resolution from the DDs' summaries is covered below (FINAL CHECK requirement).

**DD-1 §20:** 20.1 no ClearMontageQueue action → P0-04, P5-02; 20.2 MaxSlots=8 → P0-06, P1-01; 20.3 queue N=8 → P0-05; 20.4 backend threading (informational) → P0-07/P1-06; 20.5 PendingDestroy cleanup → P3-08; 20.6 extend capability reactor → P3-09; 20.7 PreviousCapabilities → P3-09.
**DD-2 §10:** 10.1 topic prefix → P6-06; 10.2 partial serialization → P6-04; 10.3 LookAt precision → P6-02; 10.4 TransitionProgress in payload → P6-03; 10.5 event fan-out → P6-05; 10.6 migration out of scope → noted (no task; TransientLocal covered by P6-01/03).
**DD-3 §9/§10:** 9.1/10.2 FootstepEvent excluded + BP2017 → P4-03, P4-04; 9.2 catalog API → P4-03; 9.3 MontageId picker → P4-02; 9.5 EndReason unified filterable → P4-03; 9.6 enum location → P0-01; 9.7 EventId block → P4-01; 10.1 AnimNotify Reliable + BP2016 → P4-03, P4-04; 10.3 [AnimMarkerPicker] → P4-02; 10.4 [EventId]/[DataPolicy] → P4-01.
**DD-4 §9:** 9.1 per-class cache → P2-04; 9.2 GUID ids deferred → DEBT (note); 9.3 hash collisions ok → P2-02/P2-06; 9.4 import boundary → external ticket (DEBT); 9.5 opaque AssetRef → P2-01; 9.6 query API location → P2-06.
**DD-5 §14:** 14.1 per-graph analysis → P5-06; 14.2 enqueue-at-capacity DebugProbe → P5-02; 14.3 multi-output getters → P5-05; 14.4 `-1f` sentinel → P5-01/P5-03; 14.5 chain custom drawer → P5-08.
**DD-Fake §11:** 11.1 ComponentId 220–249 → P0-03; 11.2 IWindowRegistrar → P1-09 (+P0-08 host check); 11.3 PayloadVector enrichment → P3-06; 11.4 best-effort oracle → P1-08/P3-11/P7 (fake-only) + P8-03 (Stride smoke).
**DD-Tests §11:** 11.1 shared IClassFixture → P7-03; 11.2 BakeForTest seam → P2-05; 11.3 PumpUntil split → P7-01/P7-02; 11.4 always-fake + Stride smoke → P7 + P8-03.
**Validator rules:** ANIM001–007 → P2-07; ANIM008–012 → P5-06; BP2016/BP2017 → P4-04.
**8 integration scenarios:** → P7-04…P7-11. **3 test layers:** L1 → P1-08; L2 → P3-11; L3 → P7.
