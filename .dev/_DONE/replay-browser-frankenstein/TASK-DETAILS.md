# Replay Browser Frankenstein — TASK DETAILS

**Reference:** [DESIGN.md](./DESIGN.md) (chapter numbers in success conditions point here)
**Tracker:** [TASK-TRACKER.md](./TASK-TRACKER.md)
**Debt:** [DEBT-TRACKER.md](./DEBT-TRACKER.md)

Each task lists its scope, the chapters of DESIGN it implements, and a binary success condition (typically the unit tests that must pass). Tasks are grouped by phase.

---

## Phase P1 — Metadata extension + validated group loading

Implements DESIGN §4 and SC-1.

### RBF-P1T1 — `RecordingMetadata` schema extension

**Scope.** Add `ExerciseId : Guid` and `NodeId : int` to `RecordingMetadata` ([FDP/Engine/Fdp.Core/FlightRecorder/Metadata/RecordingMetadata.cs](../../FDP/Engine/Fdp.Core/FlightRecorder/Metadata/RecordingMetadata.cs)). Both default-safe (`Guid.Empty`, `0`).

**DESIGN refs.** §4.1.

**Success conditions.**
- `RBF_P1T1_Metadata_RoundTripsExerciseId` — `MetadataSerializer.Serialize` → `Deserialize` preserves a non-empty `ExerciseId`.
- `RBF_P1T1_Metadata_RoundTripsNodeId` — same for `NodeId = 7`.
- `RBF_P1T1_Metadata_LegacyJsonDeserializes` — JSON written without the new fields deserializes successfully with defaults.

### RBF-P1T2 — `RecordingConfiguration.NodeId`

**Scope.** Add `public required int NodeId { get; init; }` to [RecordingConfiguration.cs](../../FDP/Toolkits/Fdp.Toolkits/Replay/RecordingConfiguration.cs). Update all existing instantiations (tests, demo recorders) to pass a value.

**DESIGN refs.** §4.2.

**Success conditions.**
- Project compiles after the change; all existing recording tests still pass with explicit `NodeId = 0`.
- `RBF_P1T2_Configuration_NodeIdRequired` — omitting `NodeId` in initializer is a compile error (use a generated harness or document via a comment + test that asserts the property is `required`).

### RBF-P1T3 — `RecordingModule` stamps metadata into `AsyncRecorder`

**Scope.** In [RecordingModule.RegisterSystems](../../FDP/Toolkits/Fdp.Toolkits/Replay/RecordingModule.cs#L48), build a `RecordingMetadata { ExerciseId = _config.ExerciseId, NodeId = _config.NodeId }` and pass it as the second argument to `new AsyncRecorder(_config.FilePath, metadata)`. The recorder already writes the held metadata to `.meta.json` on `Dispose`, so no changes to `AsyncRecorder` itself are required.

**DESIGN refs.** §4.2.

**Success conditions.**
- `RBF_P1T3_RecordingModule_WritesExerciseIdToSidecar` — install module with `ExerciseId = X`; tick at least one frame; dispose; assert `.meta.json` round-trips to a `RecordingMetadata` whose `ExerciseId == X`.
- `RBF_P1T3_RecordingModule_WritesNodeIdToSidecar` — same for `NodeId = 7`.
- `RBF_P1T3_AsyncRecorder_NoCtorChangeRequired` — explicit assertion (compile-time / API snapshot) that `AsyncRecorder`'s public surface is unchanged.

### RBF-P1T4 — `FederatedReplayManager.LoadGroup(string[] paths)`

**Scope.** New class [Federation/FederatedReplayManager.cs](#) skeleton with the `LoadGroup` static-or-instance entry point. Loads each `.meta.json` via `MetadataSerializer.Deserialize` and validates per DESIGN §4.3.

Validation rules (binding):
1. Reject if any `ExerciseId == Guid.Empty` (with reason "unknown exercise").
2. Reject if not all `ExerciseId` values are identical (with reason "exercise mismatch: {set}").
3. Reject if any two files share the same `NodeId` (with reason "duplicate NodeId {id}").

On success, instantiate one `ReplayBrowserContext` per file, call `LoadRecording(path)` on each, store in the internal `Dictionary<int, ReplayBrowserContext>`.

**DESIGN refs.** §4.3, §5.1, §5.2.

**Success conditions.**
- `RBF_P1T4_LoadGroup_HappyPath` — three synthetic `.fdp`+`.meta.json` pairs with identical `ExerciseId` and distinct `NodeId`s load into three isolated contexts.
- `RBF_P1T4_LoadGroup_RejectsExerciseMismatch` — two files with different `ExerciseId` cause rejection; no contexts allocated.
- `RBF_P1T4_LoadGroup_RejectsDuplicateNodeId` — two files with the same `NodeId` cause rejection.
- `RBF_P1T4_LoadGroup_RejectsEmptyExerciseId` — file with `ExerciseId == Guid.Empty` rejected.
- `RBF_P1T4_LoadGroup_DisposesAllOnError` — when the third file fails to load mid-batch, the first two contexts are disposed.

---

## Phase P2 — Federation runtime infrastructure

Implements DESIGN §5, §6.1, partial §6.3.

### RBF-P2T1 — `FederatedReplayManager` time state + `SeekAll` + local-provider state

**Scope.** Add to the manager:

- `long BaseWallTicks { get; private set; }`
- `IReadOnlyDictionary<int, long> NodeOffsets { get; }` over an internal mutable dict
- `int LocalEntitiesProviderNodeId { get; private set; }` — defaults to the lowest NodeId in `Contexts` at `LoadGroup` time
- `void SetBaseWallTicks(long)`, `void SetNodeOffset(int nodeId, long offsetTicks)`, `void SetLocalEntitiesProvider(int nodeId)` — each mutates state and fires `OnTimeChanged` (the seek setters additionally call `SeekAll`; the provider setter does NOT seek but still raises the event because the merged view must rebuild)
- `event Action? OnTimeChanged` — fires after every `SeekAll` AND after `SetLocalEntitiesProvider`
- `void SeekAll()` — iterates all contexts, computes `BaseWallTicks + NodeOffsets[nodeId]` (default `0` if absent), calls `ctx.Playback.SeekToWallClockTicks(ctx.SandboxRepo, target)` on each.

**DESIGN refs.** §5.1, §7.8.

**Success conditions.**
- `RBF_P2T1_SeekAll_SeeksEachContext` — manager with two contexts seeks both to `BaseWallTicks + offset` via verified frame indices.
- `RBF_P2T1_SetBaseWallTicks_FiresOnTimeChanged` — single event fires per setter call.
- `RBF_P2T1_SetNodeOffset_FiresOnTimeChanged` — single event per setter call.
- `RBF_P2T1_DefaultOffsetIsZero` — a node with no entry in `NodeOffsets` is seeked to `BaseWallTicks`.
- `RBF_P2T1_LocalEntitiesProvider_DefaultsToLowestNodeId` — after `LoadGroup` of nodes `{2, 5, 1}`, the provider is `1`.
- `RBF_P2T1_SetLocalEntitiesProvider_FiresOnTimeChanged` — single event per setter call.
- `RBF_P2T1_SetLocalEntitiesProvider_RejectsUnknownNodeId` — setting an id not in `Contexts` throws `ArgumentOutOfRangeException`.

### RBF-P2T2 — `FederatedReplayManager` lifecycle + dispose

**Scope.** Implement `IDisposable`; `Dispose()` disposes every owned `ReplayBrowserContext` and clears state. Double-dispose is a no-op (mirroring `ReplayBrowserContext.Dispose`).

**DESIGN refs.** §5.2.

**Success conditions.**
- `RBF_P2T2_Dispose_DisposesAllContexts` — assert each context's `Playback` is null after manager dispose.
- `RBF_P2T2_DoubleDispose_NoThrow`.

### RBF-P2T3 — Subsystem wiring: `ReplayBrowserSubsystem` owns a manager

**Scope.** In [ReplayBrowserSubsystem.cs](../../Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs), replace the single `_context` field with `_manager : FederatedReplayManager`. When zero or one files are loaded the manager still works; the subsystem queries `_manager.Contexts` to obtain the active single-node context. Keep a single `_activeRepo : EntityRepository` reference that `Update` reads and passes to gizmo systems.

For initial wire-up, default to Single-Node view with the first loaded node selected (no merged view yet — that lands in P4).

**DESIGN refs.** §5, §6.1.

**Success conditions.**
- `RBF_P2T3_Subsystem_InitialState_ManagerExistsEmpty` — after `Initialize`, `_manager.Contexts` is empty, no exceptions.
- `RBF_P2T3_Subsystem_LoadOneFile_BindsSingleNode` — loading one `.fdp` populates `_manager.Contexts[0]` (or the file's NodeId) and the subsystem's adapter is bound to that context's `SandboxRepo`.
- `RBF_P2T3_Subsystem_ExistingSeekStillWorks` — `_manager.SetBaseWallTicks(t)` produces the same `CurrentFrame` as the prior single-context `ctx.Playback.SeekToWallClockTicks(repo, t)`.

---

## Phase P3 — Frankenstein synthesis engine

Implements DESIGN §7 and SC-2, SC-3.

### RBF-P3T1 — `NetworkIdGuid` helper

**Scope.** New static class `Fdp.Toolkit.ReplayBrowser.Federation.NetworkIdGuid` with `From(long) : Guid`, `ToLong(Guid) : long`. Encoding: little-endian pack of the long into bytes 0..7 of the Guid; bytes 8..15 zero.

**DESIGN refs.** §7.1.

**Success conditions.**
- `RBF_P3T1_NetworkIdGuid_RoundTrips` — `ToLong(From(value)) == value` for `{0, 1, -1, long.MinValue, long.MaxValue, 0xDEAD_BEEF_CAFE_BABE}`.
- `RBF_P3T1_NetworkIdGuid_IsParseable` — `Guid.TryParse(From(value).ToString(), out _)` is true.

### RBF-P3T2 — `FederatedGuidResolver`

**Scope.** New class implementing [IGuidResolver](../../FDP/Toolkits/Fdp.Toolkits/Scenario/IGuidResolver.cs) per DESIGN §7.2. Save/load maps are hot-swappable. `Resolve(string)` returns `Entity.Null` on miss (no throw).

**DESIGN refs.** §7.2, §7.6.

**Success conditions.**
- `RBF_P3T2_Resolver_SaveMapHit_ReturnsString` — save map `{E1: "abc"}` makes `Resolve(E1) == "abc"`.
- `RBF_P3T2_Resolver_SaveMapMiss_ReturnsNull` — save map empty makes `Resolve(E1) == "null"` (literal string).
- `RBF_P3T2_Resolver_LoadMapHit_ReturnsEntity` — load map `{"abc": E2}` makes `Resolve("abc") == E2`.
- `RBF_P3T2_Resolver_LoadMapMiss_ReturnsEntityNull` — load map empty makes `Resolve("missing") == Entity.Null` (and does NOT throw).
- `RBF_P3T2_Resolver_HotSwap_SaveMap` — calling `SetSaveMap` swaps the active map.

### RBF-P3T3 — `ScenarioSerializer.DeserializeWith(IGuidResolver)` overload

**Scope.** Add a public/internal overload to [ScenarioSerializer.cs](../../FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializer.cs) per DESIGN §7.5. The overload:

- Accepts `EntityRepository repo`, `JsonObject dom`, `IGuidResolver loadResolver`, `Dictionary<string, Entity> preAllocated`.
- **Bypasses the `Header.SubsystemType` filter unconditionally** — the merged DOM holds the union of multi-subsystem components by design, so the existing short-circuit on subsystem mismatch must NOT apply here (see DESIGN §7.5).
- Reuses pass-2 injection verbatim, routing every translator `Inject` call AND every `FdpAutoSerializer.TryInject` call's resolver parameter through the supplied `loadResolver`. **No call site in the new overload may fall back to the engine's default `LoadResolver`** — auto-serializer paths in particular must explicitly forward the supplied resolver, otherwise paradoxes will throw on nested handles (inline-array handle fields, etc.).
- Does NOT call `repo.CreateEntity()` itself; instead looks up each entity key in `preAllocated`. An entity key absent from `preAllocated` is a programming error in the caller and may throw.
- Permits the resolver to return `Entity.Null` without throwing.

**DESIGN refs.** §7.5.

**Success conditions.**
- `RBF_P3T3_DeserializeWith_IgnoresSubsystemFilter` — DOM with `Header.SubsystemType = "Foreign.Subsystem"` (different from the serializer's own type) still deserialises all entities and injects all components. This is the opposite of the existing `Deserialize` behaviour and the whole point of the overload.
- `RBF_P3T3_DeserializeWith_InjectsComponentsViaCustomResolver` — pre-allocated entities receive components, including resolved relational handles routed through the supplied resolver.
- `RBF_P3T3_DeserializeWith_AcceptsEntityNullFromResolver` — DOM with a relational handle the resolver maps to `Entity.Null` deserialises without throwing; the field ends up `Entity.Null`.
- `RBF_P3T3_DeserializeWith_ResolverReachesAutoSerializer` — pin the resolver-forwarding: a component injected via the auto-serializer path (no custom translator) with a relational handle resolves through the supplied `FederatedGuidResolver`, NOT the engine's default `LoadResolver`. Verify by supplying a resolver that records call counts.
- `RBF_P3T3_DeserializeWith_InlineArrayHandleResolves` — a component containing an inline array of `Entity` fields where one slot is a paradox resolves to `Entity.Null` for that slot without throwing, proving the auto-serializer correctly forwarded the resolver into its nested-handle path.
- `RBF_P3T3_DeserializeWith_DefaultDeserializeStillThrowsOnMissingGuid` — regression: the original `Deserialize` (default `LoadResolver`) still throws when a relational handle is missing, proving the change is non-invasive.

### RBF-P3T4 — Consensus-mask helper

**Scope.** A pure helper that, given two `BitMask512`s, produces `extract = candidate AND NOT alreadyClaimed`. If no `BitwiseAndNot` exists on `BitMask512`, add it (or inline using `BitwiseNot` + `BitwiseAnd`).

**DESIGN refs.** §7.3.

**Success conditions.**
- `RBF_P3T4_ConsensusMask_AndNot_AllBitsCovered` — exercise across at least three 64-bit lanes including bits 0, 63, 64, 511.
- `RBF_P3T4_ConsensusMask_EmptyClaimed_ReturnsCandidate` — `extract == candidate` when `alreadyClaimed.IsEmpty()`.

### RBF-P3T5 — `TransientMasterBuilder.Build(manager)`

**Scope.** New class. Inputs: `FederatedReplayManager` (gives contexts) + `ScenarioSerializer` (passed at construction). Output: a freshly populated `EntityRepository` ready to bind into the UI. Steps per DESIGN §7.3, §7.4, §7.5:

1. Allocate empty `EntityRepository` and prime it (reuse `ReplayBrowserContext`'s priming flow — see RBF-P3T6).
2. Correlate entities by `NetworkIdentity.Value` across contexts.
3. Pre-allocate transient entities; build the resolver's `_loadMap`.
4. Build master DOM envelope with the serializer's `SubsystemType` in the header.
5. For each global ID, order contexts (primary owner first, then ascending `NodeId`); build consensus mask per context; build the per-node save map; call `SerializeEntity(localRepo, localEntity, resolver, extractionMask)`; merge fragment properties into the entity's master node.
6. Call the new `ScenarioSerializer.DeserializeWith(transientRepo, masterDom, resolver, preAllocated)`.

**DESIGN refs.** §7.3, §7.4, §7.5.

**Success conditions.**
- `RBF_P3T5_Build_TwoNodes_SplitAuthority` — two synthetic contexts with `NetworkIdentity=42`; node A owns `SimTransform`, node B owns a tag-only component on the same network ID. Built master repo contains one entity with both components.
- `RBF_P3T5_Build_GhostExcluded` — node A has `NetworkIdentity=42` + authoritative `SimTransform`; node B has the same ID with `SimTransform` present but not in its `AuthorityMask` (ghost). Built repo's `SimTransform` matches node A's payload, not B's.
- `RBF_P3T5_Build_RelationalHandleRemapped` — entity X has `UnitSubordinate.Commander = local_Y_handle_on_nodeA`. Built repo's X has `Commander == transient_master_handle_for_Y`.
- `RBF_P3T5_Build_MissingTargetResolvesToEntityNull` — entity X on node A references entity Y; Y is absent everywhere at the seek time. Built repo's X.Commander == `Entity.Null`; no throw.
- `RBF_P3T5_Build_SplitBrainConflict_PrimaryOwnerWins` — both nodes (re)claim `NavigationIntent` due to a time offset; the entity's `NetworkAuthority.PrimaryOwnerId` points to node A; the resulting component matches node A's data; node B's slice for that component is discarded.
- `RBF_P3T5_Build_RebuildableCheaply` — calling `Build` twice in a row from the same manager state produces equivalent repos (same entity counts and component sets).

*(Inclusion / exclusion rules for entities without `NetworkIdentity` are covered exclusively by RBF-P3T7's test set — do not duplicate them here.)*

### RBF-P3T6 — Extract `PrimeAppDomainAndSandbox` to shared helper

**Scope.** [ReplayBrowserContext.cs](../../FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/ReplayBrowserContext.cs) currently owns a private static `PrimeAppDomainAndSandbox(repo, bus)`. Refactor to `internal static` on a shared utility (e.g. `RepositoryPriming.RegisterDiscoveredComponents`) so `TransientMasterBuilder` can call it without duplicating the reflection scan.

**DESIGN refs.** §7.4 step 1.

**Success conditions.**
- Existing `ReplayBrowserContext` tests still pass.
- `RBF_P3T6_Priming_RegistersComponentsOnFreshRepo` — invoking the helper on an empty repo registers at least one well-known `[ComponentId]` type.

### RBF-P3T7 — Local-Entities Provider injection in `TransientMasterBuilder`

**Scope.** Extend `TransientMasterBuilder.Build` to inject entities from the `LocalEntitiesProviderNodeId` context that have NO `NetworkIdentity` (local-only). Per DESIGN §7.8:

- After the correlation pass, walk the provider context for entities without `NetworkIdentity`.
- For each, generate a deterministic synthetic Guid from `(providerNodeId, entity.Index, entity.Generation)` via a stable hash; the source string carries a `LOCAL_NODE_` prefix so synthetic keys are recognisable in JSON dumps.
- Pre-allocate transient entities for these synthetic keys and register them in the resolver's `_loadMap`.
- Seed the provider node's `_saveMap` with these local entities so handles on global components pointing to provider-local entities resolve correctly.
- Extract local entities using their **full present-component mask** (`entityIndex.GetComponentMask`), NOT the `AuthorityMask`. Document this difference inline.
- Locally-owned local entities on other (non-provider) nodes are intentionally omitted.

`FederatedReplayManager.LocalEntitiesProviderNodeId` defaults to the lowest-numbered loaded node; `SetLocalEntitiesProvider(int)` fires `OnTimeChanged` (handled in RBF-P2T1's contract).

**DESIGN refs.** §5.1, §7.8.

**Success conditions.**
- `RBF_P3T7_LocalEntities_ProviderEntitiesAppearInMaster` — provider node has an entity without `NetworkIdentity` carrying `SimTransform`. Built master repo contains an entity with the same `SimTransform` payload.
- `RBF_P3T7_LocalEntities_NonProviderLocalsExcluded` — non-provider node has an entity without `NetworkIdentity`. It is NOT present in the built master.
- `RBF_P3T7_LocalEntities_UseFullPresenceMask_NotAuthorityMask` — provider local entity has a component present but with `AuthorityMask` bit cleared (synthetic ghost-like state); the component IS extracted (because local entities use the full presence mask).
- `RBF_P3T7_LocalEntities_GlobalHandleToLocalResolves` — a global entity owned by the provider holds a relational handle to a provider-local entity; in the built master the handle resolves to the synthetic local transient master entity (not `Entity.Null`).
- `RBF_P3T7_LocalEntities_SwitchProviderRebuilds` — calling `SetLocalEntitiesProvider(otherNodeId)` followed by `Build` produces a different master: the previous provider's locals are gone, the new provider's locals appear.
- `RBF_P3T7_SyntheticGuid_ParseableAndDeterministic` — `Guid.TryParse(syntheticGuid.ToString(), out _)` is true; building twice with identical inputs yields the identical Guid value.

---

## Phase P4 — UI binding and paradox visualisation

Implements DESIGN §6, §8 and SC-5; together with P3 covers SC-4 (gizmo compatibility is structural — the UI just rebinds to the transient repo).

### RBF-P4T1 — Multi-file open dialog

**Scope.** Extend [ReplayTimelinePanel.cs](../../FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ReplayTimelinePanel.cs) `LoadFdpAsync` to support multi-select via `IFileDialogService`. Replace the call to `_context.LoadRecording(path)` with a call into the manager's `LoadGroup(paths)`. On rejection, surface an ImGui modal with the rejection reason.

**DESIGN refs.** §8.1.

**Success conditions.**
- `RBF_P4T1_LoadFdpAsync_PassesAllPathsToManager` — fake `IFileDialogService` returns multiple paths; the panel calls `manager.LoadGroup` exactly once with the full set.
- `RBF_P4T1_LoadFdpAsync_RejectionShowsModal` — on rejection from the manager, the panel sets a modal state flag with the rejection reason text.

### RBF-P4T2 — `FederationPanel` (new ImGui panel)

**Scope.** New `Fdp.Presentation.Panels.ReplayBrowser.FederationPanel`. UI elements per DESIGN §8.2:

- Mode radio: Single-Node | Merged.
- When Single-Node: dropdown listing `manager.Contexts.Keys` with friendly labels.
- **Local-Entities Provider dropdown** (shown only when Merged is active): lists `manager.Contexts.Keys` with the current `LocalEntitiesProviderNodeId` selected; changes call `manager.SetLocalEntitiesProvider(nodeId)`.
- Base wall-tick input (long, with -1/+1/-10/+10 frame buttons that step within the primary node's frame index).
- Per-node offset row for each entry in `manager.Contexts.Keys`: numeric offset + step buttons + warning glyph when non-zero.
- Top-of-panel banner: "Causality may not hold — non-zero offsets active" whenever any offset != 0.

The panel writes through to `manager.SetBaseWallTicks`, `manager.SetNodeOffset`, and `manager.SetLocalEntitiesProvider`. View mode is published via a callback the subsystem subscribes to.

**DESIGN refs.** §8.2, §7.8.

**Success conditions.**
- `RBF_P4T2_OffsetEdit_CallsManagerSetNodeOffset` — editing the per-node spinner invokes `SetNodeOffset(node, value)`.
- `RBF_P4T2_BaseTickEdit_CallsManagerSetBaseWallTicks`.
- `RBF_P4T2_NonZeroOffset_ShowsWarningGlyph` — internal "show warning" predicate true iff any offset != 0.
- `RBF_P4T2_ModeToggle_FiresViewModeChanged`.
- `RBF_P4T2_ProviderDropdown_HiddenInSingleNode` — provider dropdown is not rendered when mode == SingleNode.
- `RBF_P4T2_ProviderDropdown_VisibleInMerged_DefaultsToManagerValue`.
- `RBF_P4T2_ProviderDropdownChange_CallsManagerSetLocalEntitiesProvider`.

### RBF-P4T3 — Subsystem mode swap + repo rebind

**Scope.** In `ReplayBrowserSubsystem`, hold `_activeRepo : EntityRepository` and `_viewMode : enum {SingleNode, Merged}`. On view-mode change OR on `manager.OnTimeChanged` (when in Merged — covers base-tick changes, offset changes, AND `SetLocalEntitiesProvider`), rebuild via `TransientMasterBuilder.Build` and swap `_activeRepo`. Tear down and re-create `RepositoryAdapter`, `SelectionInteractionSystem`, and `DebugGizmoLayer`'s repo binding so all downstream tools see the new repo.

In Single-Node mode `_activeRepo` is simply `_manager.Contexts[selectedNodeId].SandboxRepo`.

Additional gating behaviours wired here (test seams shared with RBF-P4T6 and RBF-P4T7):

- Expose `bool IsMergedView()` on the subsystem; pass that delegate into `ReplayTimelinePanel` (for the Play disable, RBF-P4T6) and into the inspector flagging path (RBF-P4T4 sets `InspectorState.IsMergedView`).
- On entering Merged View, force `_timelinePanel.IsPlaying = false` and `_searchPanel.CurrentFilePath = null`.
- On leaving Merged View, restore `_searchPanel.CurrentFilePath` to the active context's `CurrentFdpPath`.

**DESIGN refs.** §6, §6.2.1, §6.2.2, §8.4, §8.5, §7.8.

**Success conditions.**
- `RBF_P4T3_SingleNodeMode_BindsToCtxRepo` — `_activeRepo == contexts[selected].SandboxRepo`.
- `RBF_P4T3_MergedMode_BindsToTransientMaster` — `_activeRepo == lastBuiltMasterRepo`.
- `RBF_P4T3_OnTimeChangedInMerged_RebuildsMaster` — when the manager fires `OnTimeChanged` and mode is Merged, `Build` is invoked once; the previous master is disposed.
- `RBF_P4T3_ProviderChangeInMerged_RebuildsMaster` — calling `manager.SetLocalEntitiesProvider(newId)` in Merged mode triggers exactly one rebuild via `OnTimeChanged`.
- `RBF_P4T3_ModeSwitchToSingle_DisposesTransientMaster` — switching back to Single-Node disposes the transient master.
- `RBF_P4T3_ModeSwitchToSingle_RestoresSearchPath` — `_searchPanel.CurrentFilePath` is restored to the active context's `CurrentFdpPath`.
- `RBF_P4T3_ModeSwitchToMerged_ForcesIsPlayingFalse` — `_timelinePanel.IsPlaying` is false after the switch.
- `RBF_P4T3_GizmoSystemsReceiveActiveRepo` — `Execute` calls in the subsystem `Update` pass `_activeRepo`.

### RBF-P4T4 — Inspector field flagging for `Entity.Null` paradoxes

**Scope.** Extend `InspectorState` with `bool IsMergedView` (poked by `FederationPanel` / subsystem). Extend the component reflector's entity-field rendering so a field whose runtime value is `Entity.Null` AND `IsMergedView` is rendered with a warning colour and the tooltip text:

> "Referenced entity not present in federated snapshot. This may be due to a manual time offset, or a recorded cluster desync in the original live run."

The flag triggers regardless of whether any offset is currently non-zero — cross-node references can also break for reasons recorded in the original live run (packet loss, transient desync) even at zero offsets.

**DESIGN refs.** §8.3, SC-5.

**Success conditions.**
- `RBF_P4T4_NullEntityField_InMerged_RendersWarning_RegardlessOfOffset` — flag predicate true when `Entity.Null` + merged, both with and without non-zero offsets.
- `RBF_P4T4_NullEntityField_InSingleNode_NoWarning` — flag predicate false in Single-Node mode regardless of value.
- `RBF_P4T4_NonNullEntityField_NoWarning` — flag predicate false when value is a live `Entity`.
- `RBF_P4T4_TooltipMentionsBothCauses` — the tooltip string contains both "time offset" and "desync" so the operator sees both possible causes.

### RBF-P4T5 — Documentation: severe stutter is expected

**Scope.** Add a one-line note next to the mode toggle in `FederationPanel` and a paragraph in [ONBOARDING.md](./ONBOARDING.md) confirming SC-6: Merged-view scrub may visibly stutter; this is by design.

**DESIGN refs.** §9, SC-6.

**Success conditions.**
- Visual: the panel renders the disclaimer text whenever Merged is active. Tested via a unit assertion that the panel's `DrawContent` invokes `Gui.TextDisabled` with a string containing "stutter" or "offline" while in Merged.

### RBF-P4T6 — Disable continuous playback in Merged View

**Scope.** In [ReplayTimelinePanel](../../FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ReplayTimelinePanel.cs) the Play / Pause toggle (`_rb_play_pause`) must be **disabled (greyed out)** whenever the active view mode is Merged. The panel needs to know the active view mode — pass an `Func<bool> isMergedView` (or a small `IViewModeQuery` interface) into its constructor and ungate the existing `hasRecording` check with that flag. While in Merged View:

- Play button is rendered disabled. Hover tooltip: *"Continuous playback is disabled in Merged View. Use Step-Forward/Backward or the timeline slider."*
- `IsPlaying` is forced `false` on entering Merged View.
- The existing auto-step accumulator path remains untouched — it is naturally inert when `IsPlaying == false`.
- Step-Forward, Step-Backward, timeline slider, and base-tick spinner remain ENABLED. Each of their actions performs exactly one rebuild via the existing `OnTimeChanged` chain.

Switching back to Single-Node View re-enables Play.

**DESIGN refs.** §6.2.1.

**Success conditions.**
- `RBF_P4T6_Play_DisabledInMerged` — when `isMergedView()` returns true, the Play button is rendered with `Gui.BeginDisabled()` (or `enabled=false`).
- `RBF_P4T6_Play_EnabledInSingleNode` — when `isMergedView()` returns false and a recording is loaded, the Play button is enabled.
- `RBF_P4T6_EnterMerged_ForcesIsPlayingFalse` — flipping to Merged View while `IsPlaying == true` results in `IsPlaying == false`.
- `RBF_P4T6_StepForwardStillWorksInMerged` — Step-Forward invokes the underlying context step and triggers exactly one `OnTimeChanged`.
- `RBF_P4T6_PlayTooltipContainsDisclaimer` — tooltip string contains "disabled in Merged View".

### RBF-P4T7 — Disable search in Merged View

**Scope.** The Replay Search service operates by spinning up an isolated `PlaybackController` against a specific on-disk `.fdp` path; it cannot search the synthesised transient master. In Merged View the `ReplaySearchPanel` must therefore be quiesced:

- On entering Merged View, set `_searchPanel.CurrentFilePath = null` (existing field assigned in `ReplayBrowserSubsystem.Update`; the update loop must be made mode-aware).
- The `ReplaySearchPanel` renders a centred status string when `CurrentFilePath == null` AND the active mode is Merged: *"Search is disabled in Merged View. Switch to Single-Node View to search a specific recording."*
- On switching back to Single-Node View, `CurrentFilePath` is restored to the currently selected context's `CurrentFdpPath`.

Any in-progress search must complete (or be cancelled if `ReplaySearchPanel` already supports cancellation) on the mode switch; do not introduce new cancellation surface for this task.

**DESIGN refs.** §6.2.2.

**Success conditions.**
- `RBF_P4T7_EnterMerged_NullsSearchPanelPath` — after the subsystem flips to Merged, `_searchPanel.CurrentFilePath == null`.
- `RBF_P4T7_LeaveMerged_RestoresSearchPanelPath` — after flipping back to Single-Node, `_searchPanel.CurrentFilePath == contexts[selected].CurrentFdpPath`.
- `RBF_P4T7_SearchPanel_RendersDisabledOverlayInMerged` — panel renders a status string containing "Search is disabled" when in Merged.
- `RBF_P4T7_SearchPanel_NoOverlayInSingleNode` — when in Single-Node with a valid `CurrentFilePath`, the disabled overlay is not rendered.

---

## Phase P5 — Corrective: subsystem wiring excises legacy `_context`

Post-implementation review of P2/P4 found that `ReplayBrowserSubsystem` kept both the new `FederatedReplayManager _manager` AND the legacy `ReplayBrowserContext _context`. The two are not in sync — `ReplayTimelinePanel` still drives `_context` while the merged-view rebuild reads from `_manager.Contexts`. Net effect: scrubbing the timeline in Merged View does not update the Frankenstein repository. These tasks are corrective; they MUST land before any user-facing release.

DESIGN refs for the whole phase: §6.4 (binding "no legacy `_context`" constraint), §6.2.3 (diff policy).

### RBF-P5T1 — Excise `ReplayBrowserContext _context` from `ReplayBrowserSubsystem`

**Scope.** Delete the `private ReplayBrowserContext _context` field and every reference to it in [Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs](../../Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs). The replacements (consistent with RBF-P4T3's `_activeRepo` reference):

- `_context.SandboxRepo` → `_activeRepo` for read-only adapter/gizmo/selection paths.
- `_context.HistoryService` → take the history service from the currently active context (`_manager.Contexts[selectedNodeId].HistoryService` in Single-Node; in Merged, the history service is sourced from the Local-Entities Provider's context for diagnostic continuity).
- `_context.SeekToFrame(n)` → resolve frame `n` of the currently active context to a wall-tick value (`ctx.Playback.GetFrameMetadata(n).WallClockTicks`) and call `_manager.SetBaseWallTicks(targetTicks)`. The manager seeks every node and fires `OnTimeChanged`, which the subsystem already handles for rebuild + adapter swap.
- `_context.StepForward()` / `_context.StepBackward()` → advance/rewind `_manager.BaseWallTicks` by the wall-tick delta to the next/previous frame of the **primary node** (typically the lowest-NodeId loaded context); call `_manager.SetBaseWallTicks(newTicks)`. Step-forward returns `true` iff a next frame existed on that primary node.
- `_context.CurrentFdpPath` → `_manager.Contexts[selectedNodeId].CurrentFdpPath`.
- `_context.CurrentFrame` (used by `EventBrowserPanel.CurrentFrameProvider`) → `_manager.Contexts[selectedNodeId].CurrentFrame` (the panel observes the per-node single-node frame in Single-Node mode; in Merged mode the value is whatever the primary node reads).

The subsystem must remain functional with **zero** loaded files (post-`Initialize`, pre-`LoadGroup`): all `_manager?.…` accesses must null-guard.

**DESIGN refs.** §6.4, §5, §6.1, §6.2.

**Success conditions.**
- `RBF_P5T1_Subsystem_NoContextField` — reflection / source-grep assertion that no `ReplayBrowserContext` field exists on `ReplayBrowserSubsystem`.
- `RBF_P5T1_Subsystem_EmptyManager_NoNullRef` — `Initialize` → `Update` → `DrawUI` cycle does not throw before any file is loaded.
- `RBF_P5T1_SingleNode_SeekViaManager` — loading one file and triggering a slider seek causes `_manager.BaseWallTicks` to change to the corresponding wall-tick of the requested frame, and `_activeRepo == _manager.Contexts[0].SandboxRepo` (or whichever single NodeId).
- `RBF_P5T1_Merged_SeekRebuildsTransientMaster` — in Merged mode, a seek triggered through the new code path produces a NEW transient master instance (verified by reference inequality with the previous one).
- `RBF_P5T1_EventBrowser_CurrentFrameProvider_UsesActiveContext` — the event browser's frame provider reflects the active single-node context's `CurrentFrame`.

### RBF-P5T2 — `ReplayTimelinePanel` drives `FederatedReplayManager` directly

**Scope.** Refactor [ReplayTimelinePanel.cs](../../FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ReplayTimelinePanel.cs) so that:

- The constructor takes a `FederatedReplayManager? manager` reference (plus an `Func<int> getSelectedNodeId` to know which context the slider should mirror for label/frame readout) instead of the bare `ReplayBrowserContext _context`.
- Every `_context.SeekToFrame(n)` becomes `manager.SetBaseWallTicks(targetWallTicks)` where `targetWallTicks` is resolved from frame `n` of the currently selected context via `ctx.Playback.GetFrameMetadata(n).WallClockTicks`.
- Every `_context.StepForward()` / `StepBackward()` becomes a wall-tick delta advance / retreat on the manager (per RBF-P5T1 semantics).
- The slider's max value, current value, and labels read from the selected context but **never** mutate it directly — all mutation routes through the manager.
- `LoadFdpAsync` no longer falls through to `_context.LoadRecording(paths[0])`. The `OnLoadGroup` callback fully owns the load; on success the panel simply clears its UI state and returns; on rejection it sets the modal flag and returns.
- The "Save to JSON" export uses `manager.Contexts[selectedNodeId].CurrentFdpPath` as the input path.

The Play-button disable (RBF-P4T6) and tooltip already in place are preserved.

**DESIGN refs.** §6.4, §8.1, §6.2.1.

**Success conditions.**
- `RBF_P5T2_Panel_NoContextField` — reflection assertion that `ReplayTimelinePanel` no longer holds a `ReplayBrowserContext` field.
- `RBF_P5T2_SliderMove_CallsSetBaseWallTicks` — simulated slider change at frame N invokes `manager.SetBaseWallTicks(GetFrameMetadata(N).WallClockTicks)` exactly once.
- `RBF_P5T2_StepForward_AdvancesBaseWallTicks` — Step-Forward advances `manager.BaseWallTicks` to the wall-tick of the primary node's next frame.
- `RBF_P5T2_StepBackward_RewindsBaseWallTicks`.
- `RBF_P5T2_LoadGroup_DoesNotDoubleLoad` — on a successful `OnLoadGroup`, no per-file `LoadRecording` call happens from the panel. Verified by spying that `FederatedReplayManager.LoadGroup` is invoked exactly once and no additional `ReplayBrowserContext.LoadRecording` happens afterward.
- `RBF_P5T2_LoadGroup_RejectionStillShowsModal` — regression coverage for RBF-P4T1's `RBF_P4T1_LoadFdpAsync_RejectionShowsModal` against the refactored code path.

### RBF-P5T3 — Diff engine routed through `_activeRepo` with two-rebuild before/after cycle

**Scope.** The reactive diff path in `ReplayBrowserSubsystem.Update` currently passes `_context.SandboxRepo` to `ComputeEntityDiff` and uses `_context.StepForward(suppressHistory: true)` as the step callback. Replace this with a manual two-shot extraction that works in both Single-Node and Merged modes per DESIGN §6.2.3:

1. Compute the previous wall-tick by querying the primary node's frame `currentFrame - 1`: `prevTicks = primaryCtx.Playback.GetFrameMetadata(currentFrame - 1).WallClockTicks`.
2. `manager.SetBaseWallTicks(prevTicks)` — this fires `OnTimeChanged`, the subsystem rebuilds the transient master (in Merged) or seeks the single-node context.
3. Serialise the selected entity from `_activeRepo`: `before = _scenarioSerializer.SerializeEntity(_activeRepo, entityInActiveRepo, resolver, mask)`. In Merged mode the entity handle is looked up by `NetworkIdentity`-derived synthetic Guid (or by the synthetic local-only Guid when applicable) — reuse the resolver's `_loadMap` keys to find the transient master entity for the originally-selected logical identity.
4. `manager.SetBaseWallTicks(currentTicks)` — rebuilds again, restoring the "after" state.
5. Serialise again from `_activeRepo` for the "after" DOM.
6. `_diffPanel.CurrentDiffs = _diffService.ComputeTreeDiff(before, after, epsilon)`.

Implementation notes:

- In Single-Node mode the two seeks are O(binary-search-on-frame-index) — cheap. The behaviour is equivalent to today's single-context diff.
- In Merged mode the two seeks each trigger a full transient-master rebuild — slow, accepted (SC-6 + §6.2.3).
- The selected-entity tracking across rebuilds must be by **stable global identity** (`NetworkIdentity.Value` or the synthetic local-provider Guid), NOT by the volatile transient-master `Entity` handle (which differs across rebuilds). Capture the global identity at the time of selection.
- The diff path must null-guard `_manager` and only run when at least one file is loaded.

**DESIGN refs.** §6.2.3, §6.4.

**Success conditions.**
- `RBF_P5T3_Diff_SingleNode_StillProducesDiff` — regression: selecting an entity and seeking by one frame in Single-Node mode still yields a populated `CurrentDiffs` list equivalent to the pre-refactor output for the same input.
- `RBF_P5T3_Diff_Merged_ProducesDiff` — in Merged mode, the same selection + frame step yields a non-empty `CurrentDiffs` list. Verify the diff reflects authoritative federated state by checking at least one component value matches the merged extraction (not the legacy single-context extraction).
- `RBF_P5T3_Diff_Merged_TwoRebuilds` — instrument `TransientMasterBuilder.Build` with a counter; one diff cycle increments it by exactly 2 (one for "before", one for "after"). After the diff completes the manager is left at the original `BaseWallTicks` (no off-by-one drift).
- `RBF_P5T3_Diff_StableIdentityAcrossRebuilds` — the selected entity's NetworkIdentity (or synthetic local Guid) is preserved across the two rebuilds; the diff is computed for the same logical entity even though the transient master `Entity` handle differs in the "before" and "after" rebuilds.
- `RBF_P5T3_Diff_NoCrashOnMissingEntity` — if the entity exists in "after" but is missing from "before" (or vice versa, e.g., it was destroyed in the gap), the diff path treats the missing side as `null` and still produces a valid `CurrentDiffs` list (a "wholly added" or "wholly removed" diff).

### RBF-P5T4 — Disable "Seek to Previous/Next Change" arrows in Merged View

**Scope.** Pass an `IsMergedViewQuery` (a `Func<bool>`) into `ComponentDiffPanel` (or evaluate the gate in the subsystem when wiring `OnSeekToChangeRequested`). When the gate returns true:

- The `##prev_change` and `##next_change` transport buttons are rendered with `Gui.BeginDisabled` (or the existing `enabled` flag set to false).
- Hovering shows the tooltip: *"Step-change search is disabled in Merged View. Switch to Single-Node View to seek to the next change."*
- The `OnSeekToChangeRequested` callback is never invoked while in Merged View, even if the button somehow fires (defense-in-depth in the subsystem: short-circuit when `IsMergedView()` is true).

**DESIGN refs.** §6.2.3.

**Success conditions.**
- `RBF_P5T4_PrevChange_DisabledInMerged` — button renders disabled in Merged View.
- `RBF_P5T4_NextChange_DisabledInMerged` — button renders disabled in Merged View.
- `RBF_P5T4_PrevNextChange_EnabledInSingleNode` — both buttons enabled in Single-Node mode when a file is loaded and no search is in progress.
- `RBF_P5T4_SubsystemShortCircuit_NoSeekInMerged` — even if the panel is somehow forced to fire `OnSeekToChangeRequested(±1)` while Merged is active, the subsystem ignores it (no background `SeekToNextChangeAsync` is started).
- `RBF_P5T4_TooltipContainsDisclaimer` — tooltip string contains "Step-change search is disabled in Merged View".

---

## Cross-phase notes

- All new code lives under `Fdp.Toolkits/ReplayBrowser/Federation/` (headless) and `Fdp.Presentation/ImGui/Panels/ReplayBrowser/` (UI). The Hrot subsystem only wires existing types.
- The `ScenarioSerializer` instance reused by the builder is the same one already constructed in [ReplayBrowserSubsystem.Initialize](../../Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs#L115) via `HrotScenarioSerializerFactory.Build(behaviorRegistry)`.
- Universal Breakpoint integration is **out of scope** and not part of any task here; see DESIGN §3 assumptions.
- Performance is intentionally untested (SC-6). Tasks must not add stopwatch-based regression gates.

---
