<!--STATUS
state: LIVE
build-state: NOT-NEEDED-FOR-THE-HARNESS
updated: 2026-08-23
current-answer: ⛔⛔ READ §0b FIRST — N1 MEASURED THE PREMISE AND IT IS FALSE. The ids already repeat
  across a reload, so no reset is needed and §4's rules are NOT to be built. What survives is §0's
  record (the reset IS designed and built, in mgmt-1 §5.7), §2's two-intents insight and §3's hazards —
  which apply to whoever ever does need a reset (cluster LoadingLive), not to the harness.
stale-below: ⛔ §4 (the five rules) and §6 (what N1 must assert) are SUPERSEDED by §0b. §5's UML
  describes a wiring that was measured unnecessary — kept as the record of what was considered.
design-basis: ⭐⭐⭐ docs/designs/mgmt-1/DESIGN.md §5.7 "Centralized Network Identity Authority" (the
  owning design — master-owned, 2PC-triggered, DDS-broadcast reset) · §14 "Deterministic Batch Runs" ·
  docs/designs/hexag-2/DESIGN.md §4.2.5 (DdsIdAllocatorServer lifecycle) ·
  PROGRAMME_Unification_And_Harness.md D6 (the decision) · DESIGN_Regression_Net.md §7 N1 (the item).
known-conflict: ⚠ charter D6's caveats ②/③ are SUPERSEDED by §3 of this file — they were written
  without §5.7 and understate both hazards. D6's DECISION stands; its mechanism notes do not.
-->
# DESIGN — **deterministic network ids** *(charter `D6`, item `N1`)*

## 0. ⛔⛔⛔ WHERE THE DESIGN WAS — **the honest answer**

> 🔒 **User, `2026-08-23`:** *"where is the design for the deterministic network id allocation and
> reset"*

| what | where | state |
|---|---|---|
| ⭐⭐⭐ **network id ALLOCATION** | 📄 **`docs/designs/mgmt-1/DESIGN.md` §5.7** *"Centralized Network Identity Authority (`DdsIdAllocatorServer`)"* + 📄 `docs/designs/hexag-2/DESIGN.md` §4.2.5 *(lifecycle)* | ✅ **DESIGNED AND BUILT** |
| ⭐⭐⭐ **the RESET** | 📄 **the same §5.7** — ⭐ fully specified: **master-owned**, triggered inside the **2PC `LoadingReplay`** transaction, **broadcast over DDS**, clients flush their pool and re-fetch | ✅ **DESIGNED AND BUILT** — 📐 `DdsIdAllocatorServer.HandleReset` · `EIdRequestType.Req_Reset` · `EIdResponseType.Resp_Reset` · `DdsIdAllocator.Reset(startId)` |
| ⛔ **the DETERMINISTIC reset** *(the same ids on every run)* | ⛔⛔ **NOWHERE.** Only charter **`D6`** *(a decision row)* and `DESIGN_Regression_Net.md` §7 **`N1`** *(a one-line item)* — ⭐ **this file is that missing design** | ⭐ **now designed** |

### ⛔ And the process failure, stated plainly

⭐⭐⭐ **`D6` was written without reading §5.7 — the design that owns the thing it proposes to reset.**
📌 **`R-129` exactly**: I measured the code *(`Reset` exists everywhere, zero production callers)* and
reasoned from **how it IS**. ⇒ ⛔ **`D6` reads as *"a dormant local capability to wire up."*** 📐 The
truth: **a built cluster protocol with a designed owner, trigger and broadcast.** ⚠ **And `N1` was
dispatched on that reading — twice** *(the design's own UML obligation ④ says a design with no UML is not
dispatchable; the regression net's diagrams cover the golden harness, ⛔ **not this**)*.

## 0b. ⛔⛔⛔ AS-BUILT — **`N1` MEASURED THE PREMISE AND IT IS FALSE. NOTHING HERE IS TO BE BUILT.**

> 📐 **`HN-010`, `DeterminismRails.cs`, `2026-08-23`** — measured by the implementation session
> **before** it ever saw this file *(it was pushed after their last merge)*.

| 📐 measured | ⇒ |
|---|---|
| ⭐⭐⭐ **the ids ALREADY repeat across a reload in ONE process** | ⛔⛔ **no reset is needed. `D6`'s four caveats are not paid, and §4's five rules are NOT to be built** |
| ⭐⭐⭐ **and `N1` as specified could never have tested a reset anyway** | 🔴 the item bundled *"wire the reset on `WorldResetEvent`"* with *"two fresh processes"* — 📐 but **two fresh processes each start their allocator at its own baseline**, so that comparison exercises allocation **ORDER**, never the **RESET.** ⭐ Their rail **split the two claims** and measured each |
| ⭐⭐ **two fresh processes agree byte-for-byte** | 8 entities, ids **1000–1007** ⇒ allocation order is deterministic **without intervention** |
| ⭐⭐ **36 of 41 panel dumps identical; exactly 2 kinds drift**, both wall-clock feeds | ⭐ a **control rail** pins the volatile set to exactly those two **in both directions** ⇒ `D6` caveat ① is honoured **structurally**, not by discipline |

### ⭐⭐⭐ WHY they repeat — **the ids are DATA, not ALLOCATIONS** *(traced `2026-08-23`)*

⚠ **`HN-010` measured THAT they repeat; it did not say WHY.** ⭐ Traced, because *"it happens to repeat"*
is a much weaker foundation for a golden than *"it repeats by construction"*:

| 📐 | |
|---|---|
| ⭐⭐⭐ **`scenarios/hill-attack/scenario.json` CONTAINS the ids** | 📐 **eight `NetworkIdentity` blocks, values `1000`–`1007`** — ⭐ **exactly what `N1` measured.** ⇒ **the ids are authored data in the file** |
| ⭐⭐ **the load path restores them** | `ScenarioFileService.LoadScenario` → `SoftClear` *(empty the repo)* → deserialise the DOM → each entity's `NetworkIdentity` comes back **as authored** ⇒ ⭐ **same file, same ids — any process, any number of reloads** |
| ⭐⭐⭐ **and the allocator is NEVER CONSULTED on this path** | 📐 the `AllocateId()` call *(`StagingEntityExtractor.cs:216` → `PreAllocatedNetworkId`)* sits behind **`HrotEditLoadHandler`**, and 🔴 **`grep "HrotEditLoadHandler" --include=*.cs` finds NO production construction site — only tests.** ⇒ that is the **orchestrated cluster edit-load** path, ⛔ **not the editor's file-open path** |

⇒ ⭐⭐⭐ **`D6` asked *"can we reset the allocator so the ids repeat?"* — the ids repeat because they were
NEVER ALLOCATED.** ⛔ The reset would have had nothing to do.

### 🔴🔴 THE CAVEAT THIS EXPOSES — **it holds for AUTHORED entities, NOT for RUNTIME SPAWNS**

⭐⭐ **This is the part worth carrying forward, and it is a limit on the goldens, not on the reset:**

| | authored entities *(in the file)* | ⛔ entities SPAWNED during the run |
|---|---|---|
| id source | ⭐ **the scenario JSON** | 🔴 **`NetworkSpawningSystem` → `AllocateId()`** *(it is constructed **with** the allocator, `EditorSubsystem.cs:1102`)* |
| two fresh processes | ⭐ identical | ⭐ identical *(each starts at `1000`)* |
| ⛔ **a RELOAD in one process** | ⭐ identical | 🔴 **DRIFTS** — the allocator is created once in `Initialize` and **nothing resets it** |

⇒ ⭐⭐⭐ **A golden over a scenario that spawns entities at runtime is safe across fresh processes and
NOT safe across a reload.** ⛔ **That is the case in which `D6` comes back** — and 📄 §3's hazards are
what it would have to respect. ⚠ **Not a problem today** *(`hill-attack` is fully authored)*; ⭐ **a
problem the moment a curated scenario spawns.**

### ⛔ The error was MINE, and it is worth naming precisely

⭐ **This design's hazard analysis was right and is still useful.** ⛔ **Its PREMISE was never measured.**
📐 I measured the allocator's mechanics exhaustively — three shadowing types, pre- vs post-increment,
`Reset()`'s default, §5.7's broadcast — and **never asked the one question that decides whether any of it
matters**: ⭐⭐⭐ *is the allocator even in the path that assigns ids to scenario-loaded entities?*
⇒ 📐 **It is not, for repeatability purposes** — the ids already repeat.

⚠⚠ **That is the mirror of the failure this file opens by naming.** §0 says `D6` reasoned from *how the
code IS* without reading the owning design. ⛔ **§4 then reasoned from the DESIGN without measuring
whether the mechanism was needed.** ⭐⭐ **Both halves need checking — the design for intent, the code for
fact** — and I did each one in the round where the other was required.

### ⭐ What SURVIVES, and for whom

| ⭐ keeps its value | why |
|---|---|
| ⭐⭐⭐ **§0's record: the reset IS designed and BUILT** *(`mgmt-1` §5.7 — master-owned, DDS-broadcast)* | ⛔ `D6`'s *"dormant local capability"* description is still **wrong**, and the next person to want a reset must not re-derive that |
| ⭐⭐ **§2's two-intents insight** *(collision-avoidance goes FORWARD; determinism goes BACK — same method, contradictory)* | ⭐ this is the thing that would cause real damage if someone "unified" the two call sites |
| ⭐⭐ **§3's hazards ①–③** | ⭐ they bind **whoever ever calls `Reset` in anger** — 📌 hazard ① *(`Reset()`'s default ≠ the construction value, differently on each allocator)* is a live trap in a **built** protocol |
| ⛔ **§4, §5, §6** | **superseded.** ⭐ Kept as the record of what was considered and why it was not needed — ⛔ **not as instructions** |

## 1. INVENTORY — **the queries, and what they found**

| query run | total | result |
|---|---|---|
| `search_graph(name_pattern=".*IdAllocator.*|.*IdManager.*")` | **171** | ⭐⭐ **this is what grep missed**: it surfaced `DdsIdAllocatorServer` *(in-degree **11**)* and the `docs/designs/mgmt-1` §5.7 / `hexag-2` §4.2.5 **design sections** — ⛔ `D6` had cited neither |
| `grep -rn "class SequentialIdAllocator"` | **3** | 🔴 **three DIFFERENT types with one name** — `Hrot.Core.Network` *(real)* · `EditorSubsystem` *(private nested)* · `EditorHarness` *(test nested)* |
| `grep "Resp_Reset|Req_Reset"` in `Fdp.Network.Cyclone/Services/` | **6** | ✅ **§5.7's broadcast reset is BUILT**, not designed-only |
| `grep "RegisterWorldResetObserver|WorldResetEvent"` *(non-test)* | **10** | 🔴 **`WorldResetEvent` is published from THREE sites** in `ScenarioFileService` *(`:100`, `:188`, `:234`)*, and ⭐ **already has a consumer** — `IgApplication.cs:989` |
| `grep ".Reset(" --include=*.cs` *(non-test)* | **0** | ⭐ `D6`'s measurement holds: **zero production CALLERS.** ⚠ **But that is a fact about callers, ⛔ not about the mechanism** |

## 2. ⭐⭐⭐ THE INSIGHT — **two resets, OPPOSITE directions, one method**

| | §5.7's reset *(built)* | ⭐ this design's reset |
|---|---|---|
| **why** | ⛔ **collision avoidance** — new entities must not collide with ids baked into a `.fdprec` | ⭐ **repeatability** — run 1 and run 2 of one scenario must produce **the same** ids |
| **direction** | 🔴 **FORWARD, to a high-water mark**: `SafeStartId = max(all nodes' MaxNetworkId) + 10 000` | ⭐⭐ **BACK to the SAME start, every time** |
| **reproducible?** | ⛔⛔ **NO, by construction** — it depends on the recording's contents | ⭐ **yes — that is the entire requirement** |
| **when** | once, at `LoadingReplay`; ⛔ **explicitly NOT re-reset on `ReplaySeek`** | ⭐ at **`LoadingLive` / scenario load** |

⇒ ⭐⭐⭐ **They are the same method with contradictory intents.** ⛔⛔ **So this design must not be
implemented by changing §5.7's path** — it is a **second, differently-triggered call site** of a protocol
that already exists, and 🔒 **§5.7's replay behaviour is untouched.**

## 3. ⚠⚠ THE MEASURED HAZARDS — **these SUPERSEDE charter `D6`'s caveats ② and ③**

| 🔴 | hazard | consequence |
|---|---|---|
| **①** | ⭐⭐⭐ **`Reset()`'s DEFAULT IS NOT THE CONSTRUCTION VALUE — on BOTH allocators, differently.** 📐 `Hrot.Core.Network`: `_next = 1`, `AllocateId() => Interlocked.Increment(ref _next)` ⇒ **pre-increment, first id = 2**; `Reset(0)` ⇒ **first id = 1**. 📐 `EditorSubsystem`'s nested: `_next = 1000`, `AllocateId() => _next++` ⇒ **post-increment, first id = 1000**; `Reset(0)` ⇒ **first id = 0** | ⛔⛔ **A reset run and a fresh-process run produce DIFFERENT ids** ⇒ 🔴 **`N1` would go red for a reason that is the fix's own fault.** ⇒ ⭐⭐⭐ **RULE: always `Reset(explicitStart)` matching that allocator's construction value — NEVER `Reset()`** |
| **②** | ⭐⭐ **THE CLUSTER RESET IS A BROADCAST, NOT A LOCAL CALL** *(§5.7)*. Clients **pre-allocate a pool** *(`_availableIds`)*; the server's `Resp_Reset` is what makes them **flush and re-fetch** | ⛔⛔ **Resetting per-node locally leaves every client holding ids from the OLD range** ⇒ ⚠ **charter `D6` caveat ② called distributed mode "a hazard" — 📐 it is not a hazard, it is a DESIGNED PROTOCOL, and the design already answers it** |
| **③** | 🔴 **THREE types named `SequentialIdAllocator`**, one of them `private` inside `EditorSubsystem` | ⛔ a fix applied to `Hrot.Core.Network`'s **does not touch the editor's**, which is the one the harness runs. ⚠ `D6` caveat ④ named the shadowing; ⭐ **hazard ① is why it matters** |
| **④** | ⭐ **`WorldResetEvent` has THREE publish sites** *(`ScenarioFileService:100,188,234`)*, and `RegisterWorldResetObserver` *(`:84`)* is a **separate** callback seam | ⭐ subscribe **once**, to the seam or the event — ⛔ **not per call site**, or a fourth site added later silently misses |
| **⑤** | ⚠ **ids are not the only non-determinism** | ⭐ `N1` diffs **panel dumps too**; expect float formatting, dictionary order, wall-clock stamps, spawn order. ⛔⛔ **`D6` caveat ①: do NOT widen an ignore-list to make it green** |

## 4. ⭐⭐ THE DESIGN

| # | rule |
|---|---|
| **①** | ⭐⭐⭐ **Editor / one-node** *(`OfflineNetworkFactory`)*: on `WorldResetEvent`, call **`Reset(constructionStart)`** on the allocator that host actually uses. ⛔ **Never the parameterless overload** *(hazard ①)* |
| **②** | ⭐⭐⭐ **Cluster `LoadingLive`**: reset **through the master** — `DdsIdAllocatorServer.Reset(fixedStart)`, which broadcasts `Resp_Reset` and makes every client flush *(§5.7's built path)*. ⛔ **Never per-node** *(hazard ②)* |
| **③** | 🔒 **`LoadingReplay` is UNCHANGED** — it keeps §5.7's forward-only high-water `SafeStartId`. ⛔ **Determinism applies to `LoadingLive` only** |
| **④** | ⭐⭐ **The construction start becomes a NAMED CONSTANT, not a literal**, so *"reset to where a fresh process starts"* is checkable rather than remembered |
| **⑤** | ⭐ **One subscription**, on the `RegisterWorldResetObserver` seam *(hazard ④)* |

## 5. ⭐⭐⭐ THE UML

### 5.1 Class view

```mermaid
classDiagram
    class INetworkIdAllocator {
        <<interface>>
        +AllocateId() long
        +Reset(startId) void
    }
    class SequentialIdAllocator {
        <<existing, Hrot.Core.Network>>
        -long next
        +AllocateId() long
    }
    class EditorNestedAllocator {
        <<existing, PRIVATE in EditorSubsystem>>
        -long next
        +AllocateId() long
    }
    class DdsIdAllocator {
        <<existing, client>>
        -Queue availableIds
        +Reset(startId) void
        +OnRespReset() void
    }
    class DdsIdAllocatorServer {
        <<existing, MASTER ONLY>>
        +HandleReset(request) void
    }
    class ScenarioFileService {
        <<existing>>
        +RegisterWorldResetObserver(callback)
    }
    class IdStartConstants {
        <<new>>
        +CoreStart
        +EditorStart
    }
    class DeterministicIdReset {
        <<new>>
        +Attach(fileService, allocator)
    }

    INetworkIdAllocator <|.. SequentialIdAllocator
    INetworkIdAllocator <|.. EditorNestedAllocator
    INetworkIdAllocator <|.. DdsIdAllocator
    DdsIdAllocatorServer --> DdsIdAllocator : broadcasts Resp_Reset
    DeterministicIdReset --> ScenarioFileService : subscribes once
    DeterministicIdReset --> INetworkIdAllocator : Reset(explicit start)
    DeterministicIdReset ..> IdStartConstants : reads
    note for EditorNestedAllocator "post-increment, starts 1000"
    note for SequentialIdAllocator "pre-increment, starts 1"
```

### 5.2 Sequence — ⭐⭐ **the two paths, and why they differ**

```mermaid
sequenceDiagram
    autonumber
    participant Rail as N1 rail
    participant Svc as ScenarioFileService
    participant Det as DeterministicIdReset
    participant Local as Local allocator
    participant Master as DdsIdAllocatorServer
    participant Client as DdsIdAllocator

    Note over Rail,Local: ONE-NODE (editor) — the harness path
    Rail->>Svc: load scenario
    Svc->>Svc: ClearAll
    Svc->>Det: WorldResetEvent
    Det->>Local: Reset(EditorStart)
    Note over Det,Local: explicit start, never Reset() — hazard 1

    Note over Rail,Client: CLUSTER LoadingLive
    Rail->>Master: Reset(fixedStart)
    Master->>Client: Resp_Reset broadcast
    Client->>Client: flush availableIds, re-fetch
    Note over Master,Client: a local per-node reset would strand pooled ids

    Note over Rail,Client: LoadingReplay is UNCHANGED
    Note over Master: keeps forward-only SafeStartId (mgmt-1 5.7)
```

## 6. ⭐⭐ WHAT `N1` MUST ASSERT

| ⭐ | |
|---|---|
| ⭐⭐⭐ **two FRESH PROCESSES, same scenario ⇒ byte-identical id→entity mapping** | ⛔ not one process loading twice — that would pass on a leaked pool |
| ⭐⭐ **a RESET run equals a FRESH-PROCESS run** | 📌 **the direct test of hazard ①**, and the one most likely to fail first |
| ⭐ **every captured panel dump is byte-identical too** | ⭐ ids are the *enabler*; the dumps are the *deliverable* |
| ⛔ **and it must be seen to FAIL** | ⭐ perturb the start by one and confirm the rail reddens — ⛔ *"it was green first time"* is not evidence |

## 7. ⚠ OPEN — **named, not hidden**

| | ⭐ recommendation |
|---|---|
| **Does `LoadingLive` in a REAL cluster need this at all yet?** | ⭐⭐ **No — defer.** The harness is one-node *(the editor is a one-node cluster)*. ⇒ ⭐ **build rule ①, design rules ②/③ and leave them unbuilt**, so the cluster path is *decided* without being *speculatively coded* |
| **Is a fixed start safe when a scenario is loaded twice in one session?** | ⚠ **Yes if `ClearAll` truly empties the map** — 📐 `ScenarioFileService` publishes the event **after** `ClearAll` deliberately. ⛔ **Verify, do not assume** |
| **Does the editor's private nested allocator survive at all?** | ⭐ Hazard ③ says it is a **duplicate of a `Hrot.Core.Network` type** ⇒ ⭐⭐ **ruling 9 says one implementation** — ⚠ **but that is a separate change**, and ⛔ **not `N1`'s to make under a running batch** |
