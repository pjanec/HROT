# Replay Browser — Federated Multi-Node "Frankenstein" Merged View — DESIGN

**Source talk:** [merged-view.md](./merged-view.md)
**Companion docs:** [TASK-DETAILS.md](./TASK-DETAILS.md), [TASK-TRACKER.md](./TASK-TRACKER.md), [DEBT-TRACKER.md](./DEBT-TRACKER.md), [ONBOARDING.md](./ONBOARDING.md)

---

## 1. Purpose and scope

Extend the existing Replay Browser so an operator can open the per-node `.fdp` recordings from one distributed exercise (Brain / Muscle / IG / …) together, align them on a wall-clock tick, dial per-node offsets, and inspect either a single node or a **mathematically correct merged ECS snapshot** synthesised from all loaded contexts.

This is an **offline post-mortem diagnostic**. It is explicitly exempt from the 60 Hz / zero-allocation frame budget of the live engine. The merged view is allowed to allocate, serialise to JSON, and re-deserialise on every operator action. Severe scrub stutter is an accepted condition.

The architecture has **one tier only** — the "Frankenstein" transient master synthesis. No fast inspector-level federation path. This is the explicit user decision recorded in the design talk: correctness over speed.

---

## 2. Architectural overview

```
┌────────────────────────────────────────────────────────────┐
│  Replay Browser UI  (Fdp.Presentation + Hrot.ReplayBrowser)│
│  ┌──────────────────────────────────────────────────────┐  │
│  │ Multi-file loader  │  View toggle  │ Per-node offset │  │
│  └──────────────────────────────────────────────────────┘  │
│                            │                                │
│           ┌────────────────┴────────────────┐               │
│           ▼                                 ▼               │
│   Single-Node binding              Merged-view binding      │
│   (existing path,                  (new transient repo,     │
│   ctx.SandboxRepo)                  rebuilt on every change)│
└─────────────┬──────────────────────────────────┬────────────┘
              │                                  │
              ▼                                  ▼
┌──────────────────────────────┐   ┌──────────────────────────┐
│  FederatedReplayManager      │   │  TransientMasterBuilder  │
│  • Dictionary<int,ctx>       │──▶│  • allocates fresh repo  │
│  • BaseWallTicks             │   │  • consensus mask        │
│  • NodeOffsets               │   │  • per-node Serialize    │
│  • SeekToTime(baseTicks)     │   │  • merge fragments       │
│  • coordinated per-ctx seek  │   │  • Deserialize→repo      │
└──────────┬───────────────────┘   └────────────┬─────────────┘
           │                                    │
           ▼                                    ▼
┌─────────────────────────┐    ┌────────────────────────────────┐
│ ReplayBrowserContext[]  │    │ FederatedGuidResolver          │
│ (one per loaded .fdp,   │    │ • hot-swap save/load maps      │
│  fully isolated)        │    │ • returns Entity.Null on miss  │
└─────────────────────────┘    └────────────────────────────────┘
```

The data layer stays **federated**: each `.fdp` lives in its own `ReplayBrowserContext` (existing class at [FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/ReplayBrowserContext.cs](../../FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/ReplayBrowserContext.cs)) with its own `EntityRepository` and `PlaybackController`. Merging happens **above the ECS memory layer**, exclusively through JSON round-trip via `ScenarioSerializer` + a custom `IGuidResolver`.

---

## 3. Code locations and dependencies

| New / changed | Project | Path | Notes |
|---|---|---|---|
| `RecordingMetadata.ExerciseId`, `NodeId` | `Fdp.Core` | [FDP/Engine/Fdp.Core/FlightRecorder/Metadata/RecordingMetadata.cs](../../FDP/Engine/Fdp.Core/FlightRecorder/Metadata/RecordingMetadata.cs) | additive fields, default-safe for legacy |
| `RecordingConfiguration.NodeId` | `Fdp.Toolkits` | [FDP/Toolkits/Fdp.Toolkits/Replay/RecordingConfiguration.cs](../../FDP/Toolkits/Fdp.Toolkits/Replay/RecordingConfiguration.cs) | already has `ExerciseId`; add `NodeId` |
| `RecordingModule` | `Fdp.Toolkits` | [FDP/Toolkits/Fdp.Toolkits/Replay/RecordingModule.cs](../../FDP/Toolkits/Fdp.Toolkits/Replay/RecordingModule.cs) | bridge: build `RecordingMetadata { ExerciseId, NodeId }` and pass to `new AsyncRecorder(path, metadata)`. `AsyncRecorder` itself unchanged. |
| `FederatedReplayManager` | `Fdp.Toolkits` (new) | `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Federation/FederatedReplayManager.cs` | headless |
| `FederatedGuidResolver` | `Fdp.Toolkits` (new) | `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Federation/FederatedGuidResolver.cs` | implements `IGuidResolver` |
| `TransientMasterBuilder` | `Fdp.Toolkits` (new) | `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Federation/TransientMasterBuilder.cs` | headless |
| `NetworkIdGuid` helper | `Fdp.Toolkits` (new) | `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Federation/NetworkIdGuid.cs` | long ↔ Guid encoding |
| Multi-file loader UI | `Fdp.Presentation` | [FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ReplayTimelinePanel.cs](../../FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ReplayTimelinePanel.cs) | extend `LoadFdpAsync` |
| Per-node offsets + view switch + paradox flagging | `Fdp.Presentation` (new panels) | `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/FederationPanel.cs` | new |
| Subsystem wiring | `Hrot.ReplayBrowser` | [Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs](../../Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs) | swap `_context` for `_manager`; rebind adapters |

**External assumptions / out-of-scope dependencies**

- **Universal Breakpoint integration is out of scope** for this work. The operator is assumed to obtain the wall-tick (`GlobalTime.TotalWallTicks`) by some external means (existing breakpoint hit panel, log line, manual entry) and paste it into the merged-view base-tick input. This decision was made explicitly during the design talk and is reaffirmed here. The merged-view UI provides a numeric base-tick input field; no breakpoint coupling is built in this iteration.
- The merged view assumes recordings contain `NetworkIdentity` and (where applicable) `NetworkAuthority`/`DescriptorOwnership` on entities that should be globally correlated. Entities without `NetworkIdentity` are local-only; they are excluded from cross-node correlation but can still be injected into the merged view via the designated Local-Entities Provider node (see §7.8).

---

## 4. Recording-side metadata (Phase P1)

### 4.1 Schema additions

```csharp
// RecordingMetadata.cs — additive only
public Guid ExerciseId { get; set; } = Guid.Empty;
public int  NodeId     { get; set; } = 0;
```

Legacy recordings deserialize with `ExerciseId = Guid.Empty` and `NodeId = 0`. The federation loader treats `Guid.Empty` as "unknown exercise" and refuses to group such files (see §4.3).

### 4.2 Recorder write path

`RecordingConfiguration` already carries `ExerciseId` ([line 35](../../FDP/Toolkits/Fdp.Toolkits/Replay/RecordingConfiguration.cs#L35)). Add:

```csharp
public required int NodeId { get; init; }
```

The current orchestrator [RecordingModule.RegisterSystems](../../FDP/Toolkits/Fdp.Toolkits/Replay/RecordingModule.cs#L48) constructs `AsyncRecorder` with **no metadata** (the second constructor arg is omitted, so the recorder allocates a default `RecordingMetadata`). We therefore must:

1. Change `RecordingModule` to build a `RecordingMetadata { ExerciseId = _config.ExerciseId, NodeId = _config.NodeId }` and pass it as the second argument to `new AsyncRecorder(_config.FilePath, metadata)`.
2. `AsyncRecorder.Dispose` already writes the held `_metadata` to the sidecar; no recorder-side change is required beyond ensuring the new fields are included in the JSON (they are once §4.1 lands, because they are public properties on `RecordingMetadata`).

### 4.3 Group-load validation

`MetadataSerializer.Deserialize` (see [MetadataSerializer.cs](../../FDP/Engine/Fdp.Core/FlightRecorder/Metadata/MetadataSerializer.cs)) is used to read each `.meta.json`. The federated loader:

1. Loads every selected file's sidecar.
2. Rejects the batch if any `ExerciseId == Guid.Empty`.
3. Rejects the batch if not all `ExerciseId` values are identical.
4. Rejects the batch if any two files share the same `NodeId` (duplicate node recording).
5. On success, instantiates one `ReplayBrowserContext` per file and stores them in a `Dictionary<int, ReplayBrowserContext>` keyed by `NodeId`.

Rejections surface in the UI as a modal/toast with the specific reason; no contexts are created.

---

## 5. Federation infrastructure (Phase P2)

### 5.1 `FederatedReplayManager`

Owns the array of per-node contexts and the time state. Headless; no UI references.

```csharp
public sealed class FederatedReplayManager : IDisposable
{
    public IReadOnlyDictionary<int, ReplayBrowserContext> Contexts { get; }
    public Guid ExerciseId { get; }
    public long BaseWallTicks { get; private set; }
    public IReadOnlyDictionary<int, long> NodeOffsets { get; }

    /// <summary>
    /// The node whose local-only entities (entities without NetworkIdentity) are
    /// injected into the merged view. See §7.8. Defaults to the lowest-NodeId
    /// loaded context; UI overrides via FederationPanel.
    /// </summary>
    public int LocalEntitiesProviderNodeId { get; private set; }

    public event Action? OnTimeChanged;   // fires after every seek/offset change

    public void SetBaseWallTicks(long ticks);
    public void SetNodeOffset(int nodeId, long offsetTicks);
    public void SetLocalEntitiesProvider(int nodeId);  // fires OnTimeChanged so merged view rebuilds
    public void SeekAll();                // re-applies BaseWallTicks + offsets to every context
}
```

`SeekAll` iterates every context and calls

```
targetWallTicks_node = BaseWallTicks + NodeOffsets[nodeId]
context.Playback.SeekToWallClockTicks(context.SandboxRepo, targetWallTicks_node)
```

using the existing thread-safe binary-search method at [PlaybackController.cs:245](../../FDP/Engine/Fdp.Core/FlightRecorder/PlaybackController.cs#L245). Per-context seeks are memory-safe because the contexts are fully isolated.

Any change to `BaseWallTicks` or any entry in `NodeOffsets` triggers `OnTimeChanged`. In merged-view mode the UI listener responds by invalidating and rebuilding the transient master (§7).

### 5.2 Per-node entry / disposal

`FederatedReplayManager.Dispose()` disposes every owned `ReplayBrowserContext`. The manager owns the contexts' lifetime; the UI must not dispose them independently.

---

## 6. View modes (Phase P4)

### 6.1 Single-Node view (existing fast path)

A dropdown selects one Node ID from `FederatedReplayManager.Contexts.Keys`. The UI's `RepositoryAdapter` is constructed over `Contexts[selectedId].SandboxRepo` exactly as today (see [ReplayBrowserSubsystem.cs:107](../../Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs#L107)). Scrubbing is instantaneous because no synthesis happens. Per-node offsets are still honoured for that single context.

### 6.2 Merged view (the Frankenstein)

The UI's `RepositoryAdapter` is constructed over a single **transient master `EntityRepository`** produced by `TransientMasterBuilder.Build(manager)`. Every time `FederatedReplayManager.OnTimeChanged` fires (operator scrubs base tick, dials a node offset, or changes the local-entities provider), the merged-view binding:

1. Disposes the previous transient master.
2. Calls `TransientMasterBuilder.Build(manager)` to construct a new one.
3. Rebinds the existing `RepositoryAdapter` (and downstream gizmo systems, panels, layers) to the new repository.

All existing tools — Entity Inspector, gizmos, spatial layer — execute unmodified against the new repository because it is a concrete `EntityRepository`.

#### 6.2.1 Continuous-playback policy ("Play button is disabled")

Continuous Play in Merged View would trigger a full JSON round-trip rebuild ~60 times per second and effectively freeze the application (sub-1 FPS). This is unacceptable even under the offline-tool philosophy.

**Policy:** while Merged View is active, the Play button in `ReplayTimelinePanel` is **disabled (greyed out)**. The operator may still:

- drag the timeline slider (each release triggers one rebuild),
- click Step-Forward / Step-Backward (each click triggers one rebuild),
- edit the base wall-tick input directly,
- adjust per-node offsets (each commit triggers one rebuild).

Hovering the disabled Play button shows the tooltip *"Continuous playback is disabled in Merged View. Use Step-Forward/Backward or the timeline slider."*. Switching back to Single-Node View re-enables Play.

The current `ReplayTimelinePanel.Update` auto-step path (which uses `IsPlaying`) is gated by the same flag, so simply disabling the toggle is sufficient — no separate guard on the auto-step accumulator is needed.

#### 6.2.2 Search policy ("Search is disabled in Merged View")

`IRecordingSearchService` operates headlessly against a specific `.fdp` file path on disk by spinning up its own isolated `PlaybackController`. It cannot search a synthesised transient master because there is no on-disk file to bind to.

**Policy:** while Merged View is active, `ReplaySearchPanel.CurrentFilePath` is set to `null` and the panel renders a centred status string:

> *"Search is disabled in Merged View. Switch to Single-Node View to search a specific recording."*

When the operator switches back to Single-Node View, `CurrentFilePath` is restored to the selected context's `CurrentFdpPath` and the panel returns to its normal state.

#### 6.2.3 Component-Diff policy ("passive diff works; "next change" disabled")

The reactive `ComponentDiffService` must remain functional in Merged View — the operator analyses cluster state step-by-frame, and the diff between two adjacent frames is the primary investigative artefact.

**Policy:** while Merged View is active:

- **Passive diff stays on.** When the selected entity or time changes, the subsystem performs a *manual* two-step diff: it asks `FederatedReplayManager` to rewind by one tick delta, rebuilds the transient master, serialises the selected entity via `ScenarioSerializer.SerializeEntity(_activeRepo, entity, resolver, mask)` to get the "before" DOM; then asks the manager to advance to the original tick, rebuilds again, serialises to get the "after" DOM; finally feeds both DOMs into `ComponentDiffService.ComputeTreeDiff(before, after, epsilon)`. The existing `ComputeEntityDiff(repo, ...)` overload is unsuitable because it assumes a stable `EntityRepository` instance across the step callback, which the transient master is not (each rebuild allocates a fresh repo).
- **Cost.** Each diff costs two full builds of the transient master (severe stutter, acceptable per SC-6 — the operator is stepping one frame at a time).
- **"Seek to Previous/Next Change" buttons are DISABLED in Merged View.** Their algorithm spins a background `ReplayBrowserContext` and fast-forwards through the whole file computing diffs at hyperspeed; replicating that against the federated manager would require thousands of transient-master rebuilds and lock the UI for minutes. It also violates the §6.2.2 "search is disabled" policy in spirit. Hover tooltip on the disabled buttons: *"Step-change search is disabled in Merged View. Switch to Single-Node View to seek to the next change."*

### 6.3 Switching modes

A radio toggle in the new `FederationPanel` flips between Single-Node and Merged. On switch the UI rebinds its `RepositoryAdapter`, forces a synthesis if entering Merged, applies the Play-button gate (§6.2.1), the search-panel gate (§6.2.2), and the diff "next-change" button gate (§6.2.3).

### 6.4 Sole source of truth — no legacy `_context`

The subsystem must hold **exactly one** path to per-recording state: `FederatedReplayManager`. Any residual `ReplayBrowserContext _context` field on `ReplayBrowserSubsystem` is forbidden — its presence allowed timeline controls to bypass the manager entirely, leaving the merged view frozen on scrub.

- The single-node case is `_manager.Contexts[selectedNodeId]` (after a 1-file `LoadGroup`).
- The active repo in Single-Node mode is `_manager.Contexts[selectedNodeId].SandboxRepo`; in Merged mode it is the transient master. Both are surfaced through the same `_activeRepo` reference (§8.4).
- All UI panels (`ReplayTimelinePanel`, `ComponentDiffPanel`, `EventBrowserPanel`, `ReplaySearchPanel`) communicate with the manager — directly via injected references for state-bearing operations (timeline scrub, mode-aware behaviour), or indirectly via the subsystem-owned `_activeRepo` / `RepositoryAdapter` for read-only inspection.

This is restated as a binding constraint because corrective work (Phase P5) found that an undetected residual `_context` field disconnected the slider from the merged view.

---

## 7. The Frankenstein synthesis pipeline (Phase P3)

This is the heart of the change. Build runs per operator action; severe stutter is accepted.

### 7.1 Global key encoding — `NetworkIdGuid`

The engine's `ScenarioSerializer.Deserialize` requires every entity key in the DOM to parse as a `Guid` (see [ScenarioSerializer.cs:340-343](../../FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializer.cs#L340)). `NetworkIdentity.Value` is a `long`. We deterministically pack the long into a Guid so the engine deserializer is unchanged:

```csharp
public static class NetworkIdGuid
{
    // Packs the 8 bytes of `value` into the first 8 bytes of a Guid; remaining bytes zero.
    public static Guid From(long value);
    public static long ToLong(Guid g);
}
```

The resulting strings are still valid `Guid`s for `Guid.TryParse`. The format is internal to the merged-view subsystem; on-disk scenario files are unaffected.

### 7.2 `FederatedGuidResolver`

A custom `IGuidResolver` (interface at [IGuidResolver.cs](../../FDP/Toolkits/Fdp.Toolkits/Scenario/IGuidResolver.cs)) with two hot-swappable maps:

```csharp
public sealed class FederatedGuidResolver : IGuidResolver
{
    private Dictionary<Entity, string>? _saveMap;   // per-node local entity -> global Guid string
    private Dictionary<string, Entity>? _loadMap;   // global Guid string -> transient master entity

    public void SetSaveMap(Dictionary<Entity, string> map);
    public void SetLoadMap(Dictionary<string, Entity> map);

    // Save phase
    public string Resolve(Entity entity)
        => (_saveMap != null && _saveMap.TryGetValue(entity, out var s)) ? s : "null";

    // Load phase — DOES NOT THROW on miss (engine's default LoadResolver throws)
    public Entity Resolve(string guidStr)
        => (_loadMap != null && _loadMap.TryGetValue(guidStr, out var e)) ? e : Entity.Null;
}
```

The non-throwing `Resolve(string)` is what bypasses the engine's strict load-time validation. The engine's default `LoadResolver` (built privately inside `ScenarioSerializer.Deserialize` at line 347) throws on missing GUIDs. We **do not call the public `Deserialize` method that constructs the default `LoadResolver`** — see §7.5.

### 7.3 Consensus extraction mask (per node, per entity)

Goal: per node, extract **only the components that node both has present and owns**, and never extract a component a higher-priority node has already claimed (split-brain guard).

For each globally correlated entity (identified by `NetworkIdentity.Value`):

1. Resolve `NetworkAuthority.PrimaryOwnerId` (any context that has the entity will agree on this; the primary's context is processed first if it is loaded).
2. Order the node contexts: primary owner first, then remaining nodes in ascending `NodeId` order.
3. Walk a `BitMask512 alreadyClaimedMask` (initially empty).
4. For each ordered context that has the entity locally:
   - `presenceMask = entityIndex.GetComponentMask(entity.Index)` (existing accessor [EntityIndex.cs:218](../../FDP/Engine/Fdp.Core/EntityIndex.cs#L218))
   - `authorityMask = entityIndex.GetMetadata(entity.Index).AuthorityMask` (existing field [EntityMetadataCold.cs:17](../../FDP/Engine/Fdp.Core/EntityMetadataCold.cs#L17))
   - `candidate = presenceMask AND authorityMask`
   - `extract = candidate AND NOT alreadyClaimedMask`
   - `alreadyClaimedMask |= extract`
   - `fragment = scenarioSerializer.SerializeEntity(repo, localEntity, federatedResolver, extract)`

The `BitwiseAnd`, `BitwiseOr`, `IsEmpty`, and `IsSet` methods already exist on `BitMask512` (see [FDP/Engine/Fdp.Core/BitMask512.cs](../../FDP/Engine/Fdp.Core/BitMask512.cs)). We add (or use existing) a `BitwiseAndNot` helper if missing; otherwise the masking is expressed as `extract = candidate; var inv = alreadyClaimedMask; inv.BitwiseNot(); extract.BitwiseAnd(inv);`.

This algorithm matches the engine's existing model: `EntityMetadataCold.AuthorityMask` is "components owned by local authority"; recording a node freezes that local view at the wall tick. Ghost components from other nodes are present in `presenceMask` but absent from `authorityMask`, so they are mathematically excluded.

### 7.4 Master DOM construction (fragment merge)

`TransientMasterBuilder.Build(manager)`:

1. Allocate a fresh `EntityRepository`. Call the same `PrimeAppDomainAndSandbox` priming flow that `ReplayBrowserContext` uses (refactor that helper to a shared static, see Phase P3) so component types are registered.
2. **Correlate**: build `Dictionary<long, List<(nodeId, Entity)>>` mapping each global `NetworkIdentity.Value` to the local entities that carry it across all loaded contexts.
3. **Pre-allocate**: for every global ID, `transientRepo.CreateEntity()`, populate `_loadMap[NetworkIdGuid.From(id).ToString("N")] = newEntity`.
4. Build the master envelope:
   ```json
   { "Header": { "SubsystemType": "<scenario subsystem type>", "SchemaVersion": 1 }, "Entities": { } }
   ```
   `SubsystemType` must match the `ScenarioSerializer` instance used (see §7.5).
5. For each global ID, run the §7.3 extraction across ordered contexts. For each yielded `JsonObject` fragment, copy its top-level component properties into the entity's `mergedEntityNode`. **A duplicate component key is impossible** because the consensus mask prevents it; if encountered (defensive), log and discard the late copy.
6. Attach `mergedEntityNode` under the global Guid-string key in the envelope's `Entities` block. *(Note: extraction also performs a secondary pass for the Local-Entities Provider node, which injects entities without `NetworkIdentity` under synthetic Guid keys — detailed in §7.8.)*

### 7.5 Deserialisation into the transient master

The engine's public `ScenarioSerializer.Deserialize` constructs its own `LoadResolver` privately ([ScenarioSerializer.cs:347](../../FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializer.cs#L347)) and will throw on any cross-entity GUID that is not in its lookup. **That is the wrong behaviour for the merged view.** We have two options; the design picks the **non-invasive** one:

**Chosen approach — pre-allocate + side-load via a new `DeserializeWith(IGuidResolver)` overload.**

Add an internal overload to `ScenarioSerializer` that accepts an externally-provided `IGuidResolver` instead of constructing the default `LoadResolver`. The new method:

- **Skips the header `SubsystemType` filter.** In a distributed run the Brain (e.g. `Hrot.CGF`) and Muscle (e.g. `Hrot.SimHost`) nodes operate under different subsystem types, and the merged DOM holds the union of components from both. The existing `Deserialize` short-circuits when `Header.SubsystemType != _subsystemType`, which would silently drop the merged payload. `DeserializeWith` therefore **bypasses this filter unconditionally** — the caller (`TransientMasterBuilder`) controls both the DOM and the call site, so the filter would only ever be a footgun here.
- Skips the `CreateEntity` pass (caller pre-allocated all entities; the resolver already knows them).
- Re-uses the existing Pass 2 component-injection loop verbatim, but routes every relational-handle resolution through the supplied `FederatedGuidResolver`.
- **Forwards the supplied resolver to every `FdpAutoSerializer.TryInject` call.** Components that hold nested entity handles inside arrays / inline-arrays are unpacked by `FdpAutoSerializer`, which itself calls `IGuidResolver.Resolve(string)` on every encoded handle. If the auto-serializer falls back to the engine's default resolver, paradoxes will throw. `DeserializeWith` must explicitly thread the federated resolver through every `TryInject` call (and any translator `Inject` calls).
- Permits `Entity.Null` results from the resolver without throwing.

This is a focused, additive extension to `ScenarioSerializer` (one new public method). It does not change the semantics of the existing `Deserialize` used by all other callers.

The `ScenarioSerializer` instance reused is the same one already constructed by `ReplayBrowserSubsystem.Initialize` via `HrotScenarioSerializerFactory.Build(behaviorRegistry)` ([ReplayBrowserSubsystem.cs:115](../../Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs#L115)). `FederatedReplayManager` is given this serializer at construction. Because `DeserializeWith` skips the subsystem filter, the choice of serializer instance only matters for the translator set it carries (e.g., Hrot-specific N:M translators); auto-serialised plain components from any subsystem flow through unchanged.

### 7.6 Paradox semantics (relational desync)

When per-node offsets cause a fragment to reference an entity that is not present in *any* loaded context's snapshot at its offset time, the global ID is missing from `_loadMap`. `FederatedGuidResolver.Resolve(string)` returns `Entity.Null` and the engine writes `Entity.Null` into the field. No crash, no out-of-bounds read.

This is the only path through which paradoxes can produce dangling handles; the synthesis itself is mathematically complete because all routes go through the resolver. Synthesis success conditions (1)–(3) of the user-supplied success criteria are met by this design.

### 7.7 Synthesis cost

Per invocation:
- One full repository allocation + free.
- One pass per node to gather the correlation map.
- One `ScenarioSerializer.SerializeEntity` call per (node, entity) pair where the consensus mask is non-empty.
- One `DeserializeWith` call that visits every entity in the merged DOM.
- Per-entity component injection cost (existing translator + auto-serializer code paths).

This is **O(entities × components)** with JSON in the loop. Real-world cluster snapshots can take hundreds of milliseconds to seconds; acceptable per the design talk. Continuous playback is forbidden in Merged View (§6.2.1) precisely because this cost makes Play unusable.

### 7.8 Local-only entities and the "Local-Entities Provider" node

Entities that have **no `NetworkIdentity`** (local-only — typically visual effects, UI markers, camera anchors, local debug overlays) cannot be correlated across nodes. By default the merged view would simply omit them and the operator would see those entities **vanish** on switching from Single-Node to Merged. That is rarely what the operator wants when one node (usually the Brain / CGF) holds essentially all such markers.

**Resolution: a designated "Local-Entities Provider" node.**

`FederatedReplayManager` carries `LocalEntitiesProviderNodeId` (§5.1). On `LoadGroup` it defaults to the lowest-numbered loaded NodeId (typically the Brain). The UI exposes a dropdown to change it (§8.2). Any change to this setting raises `OnTimeChanged` so the merged view rebuilds.

`TransientMasterBuilder.Build` injects local-only entities from the designated provider as follows:

1. **Correlation pass (no change for global entities):** entities WITH `NetworkIdentity` are correlated by `NetworkIdentity.Value` exactly as in §7.3.
2. **Local-entity pass (provider only):** the builder walks the provider node's repo a second time, collecting every entity that does NOT carry `NetworkIdentity`.
3. **Synthetic global key:** each such entity gets a deterministic synthetic Guid built from `(LocalEntitiesProviderNodeId, entity.Index, entity.Generation)`. The string form is `"LOCAL_NODE_{NodeId}_ENT_{Index}_G_{Generation}"` packed into a Guid via a stable hash (e.g., MD5-of-string → Guid) so it is parseable by `Guid.TryParse` like every other key. Reserve a recognisable prefix on the source string so debug dumps tell synthetic local keys from `NetworkIdentity`-derived keys at a glance.
4. **Pre-allocation:** add to `_loadMap` the same way as global entities (`transientRepo.CreateEntity()` → bind to the synthetic Guid string).
5. **Save-map seeding:** when extracting the provider node, the `_saveMap` includes both correlated entities AND local-only entities — so any relational handle on a global entity that points to a local-only entity owned by the same provider node resolves correctly. Handles pointing to a local entity on a *different* node still resolve to `Entity.Null` (paradox case).
6. **Extraction mask for local entities:** because local-only entities cannot have cross-node ghosts, no consensus is needed. The builder extracts using the entity's **full present-component mask** (i.e., `entityIndex.GetComponentMask(entity.Index)`) — not the `AuthorityMask`. This is the explicit, documented difference between the two paths.

Behavioural consequences:

- **Brain owns local viz / markers** in the typical CGF/Muscle split; selecting the Brain as provider preserves them in Merged View.
- A local entity present on the Muscle node will NOT appear in the merged view unless the operator switches the provider to Muscle.
- Switching the provider triggers a full rebuild (one cost cycle).

---

## 8. UI — `FederationPanel` and inspector flagging (Phase P4)

### 8.1 Multi-file open

`ReplayTimelinePanel.LoadFdpAsync` ([line 389](../../FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ReplayTimelinePanel.cs#L389)) is replaced by a multi-file open via the existing `IFileDialogService`. The dialog request supports multiple selection. On confirm, the panel calls `FederatedReplayManager.LoadGroup(string[] paths)` which performs §4.3 validation and instantiates contexts. Validation failures display a modal with the specific rejection reason.

### 8.2 Federation panel

New `FederationPanel` shows:

- **Mode toggle**: Single-Node (with dropdown of loaded node IDs) | Merged View.
- **Local-Entities Provider dropdown** (visible only in Merged View): lists loaded node IDs, defaults to the lowest NodeId (typically the Brain/CGF). Changing the selection calls `FederatedReplayManager.SetLocalEntitiesProvider` which triggers a full rebuild. See §7.8.
- **Base wall-tick input** (long): numeric entry plus step-by-frame buttons. Editing fires `SetBaseWallTicks`.
- **Per-node offset row**: for each loaded node, a numeric tick offset, step-by-frame buttons, and a "causality may not hold" warning glyph that lights up whenever the offset for that node is non-zero.
- A global header banner appears whenever ANY node has a non-zero offset.

### 8.3 Inspector paradox flagging

The existing entity-field rendering path (component reflector / structure renderer) is extended so that fields typed `Entity` are inspected at draw time:

- If the value is `Entity.Null` AND the active view is Merged, render the field in a warning colour and attach a tooltip:
  *"Referenced entity not present in federated snapshot. This may be due to a manual time offset, or a recorded cluster desync in the original live run."*

Note that the flag triggers in Merged View regardless of whether any offset is currently non-zero: cross-node references can also break for reasons recorded in the original live run (packet loss, transient cluster desync) when offsets are exactly zero. The tooltip wording reflects both causes.

The renderer obtains the active-view state from the existing `InspectorState` (extended with a `bool IsMergedView` poked by the panel).

### 8.4 Mode switch wiring

In merged mode `RepositoryAdapter` is constructed over the transient master; in single-node mode it is constructed over the selected context's `SandboxRepo`. The subsystem keeps a single `RepositoryAdapter` instance whose underlying repo reference is swapped, so existing gizmo systems and panels do not need to be re-registered.

The gizmo systems (`DataDrivenGizmoSystem`, `GlobalGizmoManager`, `StatelessGizmoSystem`) are passed the active `EntityRepository` on `Execute(...)` ([ReplayBrowserSubsystem.cs:319-322](../../Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs#L319)). The subsystem `Update` selects the repo from the manager (single-node ctx.SandboxRepo or merged transient master) and passes it down. **Gizmos themselves require no changes.**

### 8.5 Selection interaction

`SelectionInteractionSystem` is currently constructed over `_context.SandboxRepo` ([ReplayBrowserSubsystem.cs:167](../../Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs#L167)). It is re-constructed when the active repo changes (mode switch and every merged-view rebuild), or refactored to accept the active repo per `Tick(...)` if cheaper.

---

## 9. Non-requirements (carried forward from the design talk)

- **No tier-1 inspector-level federation.** The architecture has only the transient-master path. This is the user's explicit decision.
- **No live cluster rewind.** Strictly post-mortem.
- **No corrective DDS publishing.**
- **No 60 Hz guarantees.** Severe stutter on scrub in merged mode is accepted.
- **No automatic breakpoint→replay handoff.** Operator pastes the wall-tick value.

---

## 10. Phase plan

| Phase | Goal | Headline deliverables |
|---|---|---|
| **P1** | Metadata extension + validated multi-file load | `RecordingMetadata.ExerciseId/NodeId`, `RecordingConfiguration.NodeId`, `AsyncRecorder` stamping, `FederatedReplayManager.LoadGroup` validation |
| **P2** | Federation infrastructure | `FederatedReplayManager` with `BaseWallTicks` + `NodeOffsets` + `SeekAll`; `ReplayBrowserSubsystem` rewired to own a manager instead of a single context |
| **P3** | Frankenstein synthesis | `NetworkIdGuid`, `FederatedGuidResolver`, `ScenarioSerializer.DeserializeWith(IGuidResolver)` overload, `TransientMasterBuilder` |
| **P4** | UI binding and paradox visualisation | Multi-file dialog, `FederationPanel`, mode swap + repo rebind, inspector field flagging |

Detailed tasks for each phase are in [TASK-DETAILS.md](./TASK-DETAILS.md).

---

## 11. Success conditions (binding)

The implementation is complete only when **all six conditions below are met**. These mirror the user-supplied criteria verbatim and are referenced from individual task success conditions.

**SC-1 Validated multi-file group loading.** Replay Browser accepts a set of `.fdp` files; validates matching `ExerciseId`; rejects mismatched groups; on success instantiates one isolated `ReplayBrowserContext` per file.

**SC-2 Mathematically correct ECS synthesis.** Transient master is built from authority-filtered slices using `NetworkAuthority` + `EntityMetadataCold.AuthorityMask`. Ghost data never overwrites authoritative state. All relational handles route through `ScenarioSerializer` + `FederatedGuidResolver` and resolve to valid transient-master entities.

**SC-3 Graceful relational paradox handling.** Manual time-offset desyncs that produce references to missing entities resolve to `Entity.Null` without crashing the deserialization pipeline.

**SC-4 Flawless gizmo and tool compatibility.** All existing gizmos, spatial queries, hierarchy traversals work against the merged view with **zero modifications to those tools**.

**SC-5 Accurate diagnostic UI feedback.** Per-node offset controls present; "causality may not hold" indicator displayed for any non-zero offset; `Entity.Null` fields rendered with warning colour + tooltip in Merged view.

**SC-6 Acceptance of performance degradation.** Merged-view scrub stutter is accepted and documented; the system is correct, not fast.

---
