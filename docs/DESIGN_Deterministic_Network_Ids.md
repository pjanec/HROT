<!--STATUS
state: LIVE
build-state: DESIGN — ⛔ NO LONGER READY-TO-BUILD. Item ⓪'s enumeration (§2b, measured 2026-08-23) found
  THREE stale participants and TWO preview handlers, and item ③ is measurably impossible as written (§4b).
  ⇒ §4's items ①–③ need re-shaping before they are built. §2b is the new required reading.
updated: 2026-08-23
current-answer: §1 — the requirement is PREVIEW, and it is "a preview leaves no trace". The allocator
  counter is a trace it currently leaves. §3 the seam gap, §4 the design, §5 the UML, §6 the rails.
  §7–§9 are the history of two earlier wrong framings, kept because each contains a measured fact.
design-basis: 🔒 user 2026-08-23 (§1) · docs/designs/mgmt-1/DESIGN.md §5.7 (the cluster reset, built) ·
  PROGRAMME_Unification_And_Harness.md D6 (the original decision) · HN-010 (DeterminismRails).
known-conflict: ⚠ charter D6 names `RegisterWorldResetObserver` as the seam. 📐 MEASURED WRONG for this
  use: preview exit publishes NO WorldResetEvent (§2). D6's requirement stands; its seam does not.
known-rot: ⛔ §4 ④ says "hook it in PreviewClusterOpHandler so 'what preview saves' has ONE home" — 📐 that
  handler is registered on NO ClusterSlave and is EDITOR-ONLY (§2b). ⛔ §4 ③'s "Reset(Read()) is an identity
  on EVERY implementation" is IMPOSSIBLE for the two pooled allocators (§4b). Both corrected below; do not
  quote §4 ③/④ without §2b and §4b.
-->
# DESIGN — **a preview must leave no trace** *(the network-id counter, and what else)*

## 1. ⭐⭐⭐ THE REQUIREMENT — **user, `2026-08-23`**

> 🔒 *"the reset is still wanted feature in scenario preview — when we finish the preview the world resets
> but not so the network id allocator — so next preview (without restart of the app) does not start from
> same id as previous preview, but continues. Not desired; for repeated runs of the same we would like to
> have same ids."*

⭐⭐ **Stated as an invariant:** ⛔ **a preview is a DRY RUN. It must leave the process exactly as it found
it** ⇒ **preview N and preview N+1 produce identical ids.**

## 2. ⭐⭐⭐ THE MEASURED MECHANISM — **and why `D6`'s seam is the wrong one**

📐 `PreviewClusterOpHandler` *(`Hrot.Network.Orchestration`)*, driven by `EditorPreviewController`
*(`EditorSubsystem.cs:525-553`)*:

| step | what it does |
|---|---|
| **enter** — `TriggerLoadingPreview` | `var snap = new EntityRepository(); snap.SyncFrom(_liveRepo, includeTransient: true);` |
| **exit** — `TriggerUnloadingPreview` | `_liveRepo.SyncFrom(_snap, includeTransient: true); _snap.Dispose();` |

⇒ ⭐⭐⭐ **preview restores the world by a SNAPSHOT REWIND of the `EntityRepository` — and NOTHING ELSE.**

| 🔴 consequence | |
|---|---|
| ⛔⛔ **NO `WorldResetEvent` is published on preview exit** | ⇒ ⭐⭐ **charter `D6`'s named seam — `ScenarioFileService.RegisterWorldResetObserver` — IS NOT ON THE PREVIEW PATH AT ALL.** ⚠ Hooking the reset there would do **nothing** for this requirement. 📌 That was rule ① of this file's previous version; **it was wrong** *(§8)* |
| ⭐⭐⭐ **the allocator lives OUTSIDE the repository** | 📐 `EditorSubsystem.cs:1101` — `new SequentialIdAllocator()` in `Initialize`, handed to `NetworkSpawningSystem` *(`:1102`)*. ⛔ `SyncFrom` **cannot** restore it ⇒ ⭐ **exactly the user's symptom: the world rewinds, the counter does not** |

### 🔴🔴 AND IT IS A CLASS OF BUG, NOT ONE BUG

⭐⭐⭐ **The general fault: state outside the `EntityRepository` survives the preview rewind.**
📐 Measured — `PreviewClusterOpHandler` references **only `_liveRepo`**; `grep NetworkEntityMap` in it is
**empty**. And `Initialize` builds **two** mutable non-ECS things and gives both to the spawn system:

| ⛔ | outside the repo | rewound by preview? |
|---|---|---|
| **`idAllocator`** *(`:1101`)* | ✅ yes | 🔴 **NO** — the confirmed defect |
| **`entityMap`** *(`NetworkEntityMap`, `:895`)* | ✅ yes | ⚠ **UNKNOWN — enumerate before building** *(§4 item ⓪)*. ⭐ A comment on the record/replay path says `EcsRecordReplayController` *"rebuilds the map"* — ⛔ **that is a different path**, not preview |

⇒ ⭐⭐ **Fixing only the allocator would leave the same defect standing next door.** ⛔ **`⓪` comes first.**

## 2b. ⭐⭐⭐ ITEM ⓪ — **THE ENUMERATION** *(measured `2026-08-23`; this is what §2 twice failed to state)*

### ⭐⭐ What `NetworkSpawningSystem` holds outside the `EntityRepository`

📐 Its constructor takes four non-repo things *(`:37-44`)*, and the editor's call site is `:1102`:

| outside the repo | mutable? | rewound by preview? | 🔴 consequence |
|---|---|---|---|
| ⭐ **`INetworkIdAllocator`** *(`:1101`)* | ✅ | 🔴 **NO** | the reported defect — ids drift |
| 🔴🔴 **`NetworkEntityMap`** *(`:895`)* | ✅ | 🔴 **NO** | ⛔⛔ **and `Register` THROWS on a duplicate id** — see below |
| 🔴 **`EntityLifecycleModule`** | ✅ | 🔴 **NO** | `_pendingConstruction` / `_pendingDestruction` are keyed by **`Entity` handles the rewind invalidates**, plus `_blueprintRequirements` |
| ✅ `ITkbDatabase` | ⛔ read-only catalogue | n/a | fine |

⇒ ⭐⭐ **THREE participants, not one.** 📌 §4 ⑤ said *"two justifies a small list; one does not"* — **three
settles it**, and the list is the deliverable, not a general `IPreviewStateCapture` vocabulary.

### 🔴🔴🔴 THE FINDING THAT STOPS THE BUILD — **fixing the allocator ALONE makes it WORSE**

📐 `NetworkEntityMap.Register` *(`:36-38`)*:

```csharp
if (_netToEntity.ContainsKey(netId))
     throw new InvalidOperationException($"NetworkId {netId} already registered");
```

📐 And the editor **never prunes the map**: `PruneDeadEntities`' only production caller on this path is
`DisposalMonitoringSystem`, registered by `ReplicationLogicModule` / `NedReplicationModule` — ⛔ but the
editor uses **`OfflineNetworkFactory`**, whose `CreateReplicationModule()` returns a
**`NullReplicationModule`** *(and that even builds its own throwaway `new NetworkEntityMap()` for
`GhostCreationSystem`, not the editor's)*.

⇒ ⛔⛔⛔ **Today the DRIFT is what HIDES the map leak.** Restore the allocator and preview 2 re-issues
preview 1's ids into a map that still holds them ⇒ **`InvalidOperationException` on the spawn.** ⭐ Not
"possible" — **certain**, in the editor, on the second preview that spawns anything.
⇒ ⭐⭐ **item ① MUST NOT ship without the map.** 📌 Exactly what item ⓪ exists to catch.

### 🔴🔴 AND THERE ARE **TWO** PREVIEW HANDLERS — one concept, already diverging

| | `ReferencePreviewHandler` | `PreviewClusterOpHandler` |
|---|---|---|
| home | `Fdp.Toolkits/Orchestration/Handlers/` | `Hrot.Common/Orchestration/Handlers/` |
| interface | `IClusterStateHandler` | `IClusterOpHandler` |
| ⭐ **registered on a ClusterSlave** | ✅ **5 production sites** — IG, CGF ×2, SimHost, ExCon | 🔴 **NONE** |
| driven by | the **2PC broadcast** | the editor, directly via `TriggerLoadingPreview/Unloading` |
| the snapshot | `snap.SyncFrom(_liveRepo)` | `SyncFrom(_liveRepo, includeTransient: **true**)` |

⇒ ⛔⛔ **§4 ④'s *"one home"* names the EDITOR-ONLY handler.** ⭐ Hooking the save/restore there would give
the editor the fix and leave every cluster node without it — ⚠ **the precise hardwiring §2c forbids.**
⭐ They already disagree on `includeTransient`, so this is a live ruling-9 duplicate, not a latent one.

## 2c. ⭐⭐⭐ USER RULING — **PREVIEW IS NOT EDITOR-ONLY, AND THE RESET MUST BE CLUSTER-WIDE** *(`2026-08-23`)*

> 🔒 **User, verbatim:** *"note the preview could work also in distributed env so no hardwiring directly
> just for editor, reset must be cluster wide"*

⭐⭐ **Measured consequences, and one of them inverts §7's advice:**

| ⭐ | |
|---|---|
| ⭐⭐⭐ **The cluster-wide mechanism ALREADY EXISTS and needs no new broadcast** | 📐 both handlers are `PrepareState` handlers for `LoadingPreview`/`UnloadingPreview` — **the master broadcasts, every node commits LOCALLY.** ⇒ ⭐ a per-node capture/restore inside the handler **is** the cluster-wide reset. ⛔ Do not add a second broadcast |
| ⛔⛔ **But the editor's preview never goes through 2PC** | 📐 `EditorPreviewController` calls `Trigger*` directly, and `PreviewClusterOpHandler` is on no slave ⇒ ⭐⭐ **the fix must live where BOTH entry points pass** — the private commit helpers — ⛔ never in `EditorPreviewController` |
| 🔴🔴 **`INetworkIdAllocator.Reset` IS ALREADY CLUSTER-WIDE — and that is exactly why it is the WRONG primitive here** | 📐 `DdsIdAllocator.Reset(startId)` **writes a global `Req_Reset` to the server**, which broadcasts to every client and flushes their pools. ⇒ ⛔ using it to restore a preview would reset the **whole cluster's** id authority BACKWARD and flush pools other nodes are mid-way through — and §7's `Resp_Reset` is designed to move the high-water mark **FORWARD** for collision avoidance. ⭐⭐ **Two intents, one method — now with the measurement that makes §7's warning concrete** |
| ⚠ **`BlockIdManager.Reset` ignores its argument** | 📐 it `_localPool.Clear()`s and its own comments admit the semantics are unsettled. ⇒ there is no scalar position to restore |

⇒ ⭐⭐⭐ **The shape the steer implies:** the preview bracket restores **each node's own allocator position**,
driven by the existing 2PC; ⛔ it is **not** expressed as `INetworkIdAllocator.Reset`; and ⭐ an allocator
that cannot express a restorable position *(pooled/DDS)* must **say so** rather than silently do the wrong
thing.

## 4b. ⛔⛔ ITEM ③ IS IMPOSSIBLE AS WRITTEN — **`Reset(Read())` cannot be an identity on every implementation**

📐 Measured across all five:

| implementation | init | `AllocateId()` | `Reset(s)` | `Reset(Read())` identity? |
|---|---|---|---|---|
| `Hrot.Core.Network.SequentialIdAllocator` | `_next = 1` | `Interlocked.Increment` ⇒ **pre**, first id **2** | `_next = s` | ✅ |
| `EditorSubsystem`'s **private nested** | `_next = 1000` | `_next++` ⇒ **post**, first id **1000** | `_next = s` | ✅ |
| `IgSequentialIdAllocator` | `_nextId = 1` | `Interlocked.Increment` | `_nextId = s` | ✅ |
| 🔴 **`BlockIdManager`** | a **`Queue<long>` of leased ids** | `Dequeue()` | ⛔ **`Clear()`, argument IGNORED** | ⛔ **NO — the pool IS the state and `Reset` destroys it** |
| 🔴🔴 **`DdsIdAllocator`** | local `Queue<long>` **+ server state** | `Dequeue()` | ⛔ **global DDS broadcast + clear** | ⛔ **NO — and it mutates the whole cluster** |

⭐⭐ **The three scalar ones agree on the identity even though they DISAGREE on the meaning** *(last-issued
vs next-to-issue)* — ⇒ ⭐ **a read member must be defined BY THE IDENTITY, not by "the next id":** a name
like `PeekNextId()` is false for one and `LastIssuedId()` is false for the others. ⛔ Neither name is safe;
the contract is *"the value that, passed to the restore, puts this allocator back"*.

⇒ ⭐⭐ **Recommendation for the coordinator:** make restorability an **opt-in capability** rather than a
widened universal interface — the three scalar allocators implement it, the two pooled ones do not, and the
preview bracket then **reports** that this node cannot guarantee reproducible ids. ⛔ A universal member
that two implementations cannot honour is a lie the type system would stop advertising.

## 3. ⛔⛔ THE SEAM GAP — **the counter cannot be READ**

```csharp
// FDP/Toolkits/Fdp.Toolkits/NetworkSpawning/Abstractions/INetworkIdAllocator.cs — the WHOLE interface
public interface INetworkIdAllocator : IDisposable
{
    long AllocateId();                    // consumes
    void Reset( long startId = 0 );       // writes
}
```

⭐⭐⭐ **There is no way to read the current value.** ⇒ **save/restore is not expressible today**; the
interface needs a read member. ⛔ **And you cannot fake it with `AllocateId()`** — that burns an id, and
📐 **it returns a different thing on each implementation** *(next-to-issue vs last-issued — §4's hazard)*.

## 3b. INVENTORY — **the queries, and what they found**

| query run | total | result |
|---|---|---|
| `search_graph(name_pattern=".*IdAllocator.*\|.*IdManager.*")` | **171** | ⭐⭐ **what grep could not do**: surfaced `DdsIdAllocatorServer` *(in-degree **11**)* **and the design sections that own it** — `docs/designs/mgmt-1/DESIGN.md` §5.7, `hexag-2` §4.2.5 *(§7)*. ⛔ Charter `D6` cited neither |
| `search_graph(name_pattern="LoadScenarioByName\|LoadScenario", label="Method")` | **42** | ⭐ traced the **authored** load path to `ScenarioFileService.LoadScenario` *(`SoftClear` → deserialise)*, ⛔ **not** through the allocator |
| `grep "interface INetworkIdAllocator"` | **1** | 🔴 **two members, neither readable** — `AllocateId()` · `Reset(startId)` *(§3)* |
| `grep -rn "class SequentialIdAllocator"` | **3** | 🔴 `Hrot.Core.Network` · **`EditorSubsystem` private nested** · `EditorHarness` test nested *(§4 ③)* |
| `grep "HrotEditLoadHandler"` *(non-definition)* | **11** | 🔴 **all tests — NO production construction site.** ⇒ the `StagingEntityExtractor.Extract(…, idAllocator)` path *(`:216`)* is not live |
| `grep "WorldReset\|SoftClear\|Snapshot\|Restore"` in `PreviewClusterOpHandler.cs` | **3** | ⭐⭐⭐ **only `_snap.Dispose()`** — ⛔ **no `WorldResetEvent`, no `SoftClear`.** The rewind is `_liveRepo.SyncFrom(_snap)` and nothing else *(§2)* |
| `grep "NetworkEntityMap\|entityMap"` in `PreviewClusterOpHandler.cs` | **0** | 🔴 **preview does not touch the entity map** ⇒ §2's class-of-bug, and item ⓪ |
| `grep "new NetworkEntityMap\|new SequentialIdAllocator"` in `EditorSubsystem.cs` | **2** | `:895` and `:1101` — ⭐ **both in `Initialize`, both outside the repo, both handed to `NetworkSpawningSystem` `:1102`** |

⚠ **What the enumeration did NOT cover, stated so nobody over-reads it:** ⛔ **other subsystems' preview
paths** *(this measured the EDITOR's)*, and ⛔ **non-ECS state held by modules rather than by `Initialize`**
— ⭐ **that is exactly what item ⓪ is for.**

## 4. ⭐⭐⭐ THE DESIGN

| # | ⭐ |
|---|---|
| **⓪** | ⭐⭐⭐ **FIRST, ENUMERATE what else preview fails to rewind** *(§2's class-of-bug)*. ⭐ Start with `NetworkEntityMap`; report the list. ⛔ **A one-thing fix to a class-of-bug is the finding, not the fix** |
| **①** | ⭐⭐ **SAVE / RESTORE around the preview bracket — ⛔ NOT reset-to-a-constant.** ⭐ Restoring the **pre-preview** value cannot collide with authored ids; ⛔ a fixed constant can. ⭐⭐ **And it is what preview already IS** — the counter is simply state the snapshot does not cover, so **extend the bracket**, do not add a reset |
| **②** | ⭐⭐ **Add a READ member to `INetworkIdAllocator`** *(§3)*. ⚠ **5+ implementations** — `SequentialIdAllocator` *(`Hrot.Core.Network`)* · the editor's private nested one · `DdsIdAllocator` · `IgSequentialIdAllocator` · test doubles |
| **③** | 🔴🔴 **PIN WHAT THE COUNTER MEANS — this is where the two implementations DISAGREE.** 📐 `Hrot.Core.Network`: `_next = 1`, `Interlocked.Increment` ⇒ **pre**-increment, `_next` is **last issued**. 📐 The editor's nested: `_next = 1000`, `_next++` ⇒ **post**-increment, `_next` is **next to issue**. ⇒ ⭐⭐⭐ **`Reset(Read())` MUST be an identity on BOTH**, and that is a **rail**, not a comment *(§6)* |
| **④** | ⭐ **Hook it in `PreviewClusterOpHandler`**, beside the snapshot it already takes — ⭐ so *"what preview saves"* has **one** home. ⛔ **Not in `ExitPreviewMode`**, which would make the editor a second place that knows the list |
| **⑤** | ⚠ **`PreviewClusterOpHandler` is in `Hrot.Network.Orchestration` and today knows only `_liveRepo`.** ⇒ ⭐ it must be **given** what to save. ⛔ **Do not reach for a general `IPreviewStateCapture` vocabulary until `⓪` says how many participants there are** — ⭐ **two justifies a small list; one does not** |

## 5. ⭐⭐⭐ THE UML

### 5.1 Class view

```mermaid
classDiagram
    class EditorPreviewController {
        <<existing>>
        +EnterPreviewMode(startPaused)
        +ExitPreviewMode()
    }
    class PreviewClusterOpHandler {
        <<existing — extend HERE>>
        -EntityRepository liveRepo
        -EntityRepository snap
        +TriggerLoadingPreview()
        +TriggerUnloadingPreview()
    }
    class EntityRepository {
        <<existing>>
        +SyncFrom(other, includeTransient)
    }
    class INetworkIdAllocator {
        <<interface — NEEDS A READ MEMBER>>
        +AllocateId() long
        +Reset(startId) void
    }
    class NetworkSpawningSystem {
        <<existing>>
    }
    class NetworkEntityMap {
        <<existing — rewound? item 0>>
    }

    EditorPreviewController --> PreviewClusterOpHandler
    PreviewClusterOpHandler --> EntityRepository : snapshot and rewind
    PreviewClusterOpHandler ..> INetworkIdAllocator : save on enter, restore on exit
    PreviewClusterOpHandler ..> NetworkEntityMap : IN QUESTION
    NetworkSpawningSystem --> INetworkIdAllocator : AllocateId during preview
    NetworkSpawningSystem --> NetworkEntityMap
```

### 5.2 Sequence — ⭐⭐ **two previews, and why the second one drifts today**

```mermaid
sequenceDiagram
    autonumber
    participant Op as Operator
    participant Ctl as EditorPreviewController
    participant H as PreviewClusterOpHandler
    participant Repo as EntityRepository
    participant Alloc as IdAllocator

    Note over Op,Alloc: PREVIEW 1
    Op->>Ctl: EnterPreviewMode
    Ctl->>H: TriggerLoadingPreview
    H->>Repo: snap.SyncFrom(live)
    H->>Alloc: read counter (NEW - today nothing)
    Note over Alloc: counter = 1008
    Op->>Ctl: run, entities spawn
    Ctl->>Alloc: AllocateId x N
    Note over Alloc: counter = 1013
    Op->>Ctl: ExitPreviewMode
    Ctl->>H: TriggerUnloadingPreview
    H->>Repo: live.SyncFrom(snap)
    H->>Alloc: Reset(1008) (NEW)
    Note over Alloc: today it STAYS 1013 - the drift

    Note over Op,Alloc: PREVIEW 2 - must be identical to PREVIEW 1
    Op->>Ctl: EnterPreviewMode
    Ctl->>Alloc: AllocateId x N
    Note over Alloc: fixed: 1008.. again. today: 1013..
```

## 6. ⭐⭐ THE RAILS

| ⭐ | |
|---|---|
| ⭐⭐⭐ **two consecutive previews in ONE process produce identical ids** | ⛔ this is the user's requirement stated as a test, and **it must be seen to FAIL first** — 📐 it fails today |
| ⭐⭐⭐ **`Reset(Read())` is an IDENTITY — asserted on EVERY implementation** | 🔴 **item ③'s pre/post-increment disagreement is a real trap**, and a parameterised rail over the implementations is the only thing that keeps a fifth one honest |
| ⭐⭐ **a preview leaves NO trace** — the general form | ⭐ whatever `⓪` enumerates gets a row. ⛔ **One row per participant**, so a future non-ECS singleton fails loudly rather than silently persisting |
| ⚠ **and the authored-load case must not regress** | 📐 `HN-010` already pins it *(ids `1000`–`1007` from the file)* — ⭐ this change must not disturb it |

## 7. ⭐ THE ORIGINAL FINDING THAT STANDS — **the cluster reset is designed AND BUILT**

📄 **`docs/designs/mgmt-1/DESIGN.md` §5.7** *"Centralized Network Identity Authority"*: the reset is
**master-owned**, triggered inside the 2PC `LoadingReplay`, **broadcast** as `Resp_Reset`, and clients
**flush their pooled ids and re-fetch**. 📐 Built: `DdsIdAllocatorServer.HandleReset`, `Req_Reset`,
`Resp_Reset`, `DdsIdAllocator.Reset`.

⚠ **Two intents, opposite directions — do NOT unify the call sites:** §5.7 resets **FORWARD** to a
high-water mark *(collision avoidance, deliberately not reproducible)*; ⭐ **preview restores BACKWARD to a
captured value.** ⛔ Same method, contradictory purposes.

⚠ **And `D6`'s *"zero production callers"* is true of the CALLERS, not of the mechanism.**

## ⛔ HISTORY — **two earlier framings of this file, both wrong, each with a fact worth keeping**

### 8. ⛔ v1 — *"reset on scenario load, hooked on `WorldResetEvent`"*

🔴 **Wrong seam.** 📐 Measured `2026-08-23`: **preview exit publishes no `WorldResetEvent`** ⇒ the
`RegisterWorldResetObserver` hook is not on the path. ⭐ **Kept because it is the reason `D6`'s seam must
not be quoted for this work.**

### 9. ⛔ v2 — *"not needed at all"*

🔴 **Wrong scope, and it was right about the wrong half.** 📐 `HN-010` measured that **authored** entities
get their ids **from the scenario file** *(`scenarios/hill-attack/scenario.json` carries `1000`–`1007`)*
and the allocator is never consulted on that path — ⭐ **true, and still true.** ⛔ But it concluded *"so no
reset is needed"*, having only ever looked at **scenario load**. ⚠ **The very same document then wrote the
caveat that names this requirement** — *"entities SPAWNED at runtime DO go through the allocator, and
nothing resets it ⇒ drifts across a reload in one process"* — ⭐⭐ **and preview is exactly that case.**

⇒ ⭐⭐⭐ **The lesson, stated once:** 📌 I answered *"is a reset needed?"* by measuring **one workflow** and
generalised to **all**. ⛔ **The scope of a negative claim is the scope you measured**, and the caveat I
wrote in the same breath was already pointing at the case I had not.
