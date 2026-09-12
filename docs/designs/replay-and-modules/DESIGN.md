<!--STATUS
state: LIVE
updated: 2026-09-11
current-answer: §2.1 is the TARGET table and §3.10 the gap list. ⚠ This document was written as a
  DESIGN of replay isolation, and §1.1 is titled "Broken Replay Isolation" — so its tables describe
  how it was MEANT to be, not how it IS. Read §2.1a before quoting the NetworkLifecycleSystemGroup row.
known-rot: ⛔⛔ §2.1's row "NetworkLifecycleSystemGroup | Disabled during replay | block ghost
  create/promote/destroy" is NOT the as-is, measured 2026-09-11 (§2.1a): every production site passes
  that group exactly ONE member (GhostCreationSystem), whose Execute is an EMPTY BODY, so toggling the
  group's Enabled flag changes no behaviour at all. GhostPromotionSystem and NetworkGatewaySystem have
  never been passed to it by any site, and GhostDestructionSystem — §3.10.3's first "must be moved
  inside" — was DELETED outright by CE-144, so that half of §3.10.3 is moot rather than outstanding.
  ⛔ §2.1's second documented protection, GhostCreationSystem.BypassLifecycle, is written by three
  production sites and READ BY NONE. Filed as CE-259ap.
  ⛔⛔ CORRECTED 2026-09-11: §2.1's "TogglableInputGroup disabled => block live DDS ingress" Reason is
  ALSO false as built — that group holds the LOGIC-PACK input systems (§2.4), while all 11 production
  CycloneNetworkIngressSystem registrations are direct and outside every togglable group, and
  SetSystemsEnabled touches no ingress system and no participant. ⇒ live DDS ingress REACHES a node in
  RunningReplay (railed + red-proofed).
  🔴🔴 RETRACTED SAME DAY, and it REVERSES A SEVERITY: an earlier version of this block said "the ONLY
  thing protecting the ghost path is that the restore path never writes EntityMetadataCold.LifecycleState
  — default Constructing (0), not Ghost". That premise was measured with a grep for the SETTER and the
  restore uses a RAW COLD-CHUNK COPY (EntityMetadataCold carries [FieldOffset(84)] LifecycleState). ⇒
  lifecycle IS recorded and restored wholesale, a ghost comes back A GHOST, and the ungated
  GhostPromotionSystem mutates entities the log owns. Read §2.1b, never this retracted line.
  ⛔⛔ A FOURTH inert protection, measured 2026-09-11 (§2.1g): NedReplicationModule.AfterSeekCallback
  returns a NON-NULL EMPTY lambda whose only statement is a commented-out ResetTracking() call, and
  ResetTracking exists in zero C# files — while T-RMF-23 is ticked DONE. Every "is it wired" check passes.
  ⭐⭐ The right gate EXISTS and is under-adopted: CycloneNetworkIngressSystem.IsWorldStateFrozen skips
  exactly the WorldState translators, but its one production writer is CgfSubsystem's DEBUGGER halt
  (DQ30-C), not replay. CE-259ap's lean is to adopt it.
  🔴🔴🔴 §2.1e IS SUPERSEDED BY §2.1f: its central claim — "≥20 change-detection caches are broken by a
  rewind, so a rewind SUPPRESSES a needed publish" — is FALSE. Every holder read is an EQUALITY compare
  (not a >= monotonic guard), a keyframe repo.Clear() wipes the managed EgressPublicationState (managed
  tables share _componentTables) and its absence FORCES a publish, and egress runs during playback so the
  cache tracks the log. ⇒ class B is SELF-HEALING; the reflection subscription rail and the generalised
  boundary event are DROPPED, and class C (the ELM's non-recorded pending-construction protocol) is the
  only remaining defect. Do NOT quote §2.1e's class-B conclusion or its step 2/3 plan.
  🔴🔴🔴 §2.1h — the class-C defect is WIDER than §2.1f said: LifecycleSystem is a DIRECT registration, so
  CheckTimeouts runs EVERY TICK DURING PLAYBACK, and its currentFrame - StartFrame is uint. A rewind makes
  that wrap to ~4.29e9, the timeout fires, and cmd.DestroyEntity is called on a stale pre-replay handle —
  with the generation guard DEBUG-ONLY (Fdp.Core.csproj defines FDP_PARANOID_MODE only for Debug). ⇒ the
  ELM must be cleared at every WORLD REPLACEMENT (PrepareReplay, every seek, the live boundary), not only
  at resume. Filed as CE-259ar.
  ⭐ §2.1i measures the participant sets: both production ctor sites pass an EMPTY list and
  RegisterRequirement has ZERO callers, so the ack set is at most {gatewayModuleId} today.
stale-below: §2.1e's class-B conclusion and its 3-step plan (superseded by §2.1f); the retracted
  lifecycle line above; §2.1f's "asymmetry in BeginDestruction" flag (moot — see §2.1i). The other §2.1
  rows were not re-measured on 2026-09-11 and carry no claim either way.
-->
# Design: Replay Isolation and Modern Module System

## 1. Problem Statement

Two separate but intertwined problems are addressed together because their solutions share the same foundation.

### 1.1 Broken Replay Isolation

Replay is supposed to hermetically seal Brain and Muscle nodes from live network influence so historical ECS state can be replayed faithfully. However, the current implementation has at least three concrete gaps, all verified against the live codebase:

**Gap A — SimHostApp passes an empty group to the replay handler.**
`SimHostApp.OnLoad` creates `var simulationSystemGroup = new SimulationSystemGroup()` (empty, no systems ever added) and passes it to `NodeBootstrapper.BuildOrchestration`. The actual simulation systems live in `_kernelGroup` (a plain `SystemGroup` created right after). When the replay handler flips `simulationSystemGroup.Enabled = false`, it stops nothing because the group is empty. The real simulation systems keep running on top of replayed ECS data.

**Gap B — Input phase is never disabled during replay.**
The design requires that all input-phase systems (network ingress, behavior ingress, fire-processing queries, etc.) be suspended during replay so live operator commands and live DDS traffic cannot corrupt the historical state being played back. No code currently disables the input phase for any node.

**Gap C — CgfSubsystem passes `simGroup: null` to its replay handler.**
In `CgfSubsystem.Initialize`, the `ReferenceReplayLoadHandler` is constructed with `simGroup: null, lifecycleGroup: null`. This means the CGF node's replay handler disables nothing when replay begins.

### 1.2 Legacy Module System Accumulation

The FDP engine provides two module systems:

- **Legacy**: `ComponentSystem` (abstract base class) + `SystemGroup` (topological sorter) + five `StandardSystemGroups` (`InputSystemGroup`, `SimulationSystemGroup`, `PostSimulationSystemGroup`, `PresentationSystemGroup`, `ExportSystemGroup`). This was the first-generation design.
- **Modern**: `IEcsModuleSystem` + `ISystemRegistry` + `[UpdateInPhase]` + `ModuleHostKernel`. This is the current engine model. Toolkit systems (CycloneEgressSystem, PlaybackTickSystem, AutonomousPerceptionModule, etc.) already use it.

The two live side-by-side. The legacy system is still used by all Hrot game systems (CombatModule, GroundKinematicsModule, MissionControlModule, etc.) and is bridged into the modern kernel via adapter classes (`CgfInputGroupAdapter`, `SimulationGroupModule`, `PostSimulationGroupAdapter`) in `Hrot.Common.Infrastructure`. These adapters are the primary source of complexity for the replay problems above.

The goal is to complete the migration: convert all remaining game systems to `IEcsModuleSystem`, delete the legacy base classes, and delete the adapter classes. This forces correct composition-root wiring by making incorrect wiring a compile error.

---

## 2. Replay Architecture: Final Decisions

### 2.1 What Runs During Replay

The following table reflects the final decisions from the design discussion:

| Phase / Group | During Live | During Replay | Reason |
|---|---|---|---|
| `TogglableInputGroup` | Enabled | **Disabled** | Block live DDS ingress and operator commands |
| `TogglableSimulationGroup` | Enabled | **Disabled** | Block AI, kinematics, combat logic |
| `TogglablePostSimulationGroup` (BallisticsSystem, LinearKinematicsSystem, CarKinematicsSystem) | Enabled | **Disabled** | Physics integration mutates SimTransform and would overwrite restored positions |
| `NetworkLifecycleSystemGroup` | Enabled | **Disabled** | Block ghost create/promote/destroy during playback |
| `GhostDestructionSystem` | Enabled | **Disabled** | Must be moved inside `NetworkLifecycleSystemGroup`; a stray DDS DISPOSE during replay would delete historical entities |
| `DeferredTakeoverSystem` | Enabled | **Disabled** | Must be moved inside `NetworkLifecycleSystemGroup`; would illegally mutate authority masks on historical entities |
| `PlaybackTickSystem` | Not registered | **Running** | Drives replay frame-by-frame restore of ECS state |
| Export phase (CycloneEgressSystem, SmartEgressSystem, OwnershipEgressSystem) | Runs normally | Runs normally | IG nodes receive historical state from network; timeline seek requires a forced-dirty workaround (see Section 3.10) |
| `RecorderTickSystem` | Runs when RecordingModule active | **Not registered** | RecordingModule is uninstalled at exercise end; it is mutually exclusive with ReplayModule |


### 2.1a ⛔⛔ AS-IS — **the `NetworkLifecycleSystemGroup` row is NOT built, and the gate is INERT** *(measured `2026-09-11`)*

⚠ **§2.1 above is the TARGET.** This subsection is what the code does, measured while relocating
`GhostPromotionSystem` into `EntityCreationPack` *(`P2`; [`../../DESIGN_Role_Affinity_Ownership.md`](../../DESIGN_Role_Affinity_Ownership.md) §6a)*.

| the row claims | measured |
|---|---|
| the group holds `LifecycleSystem`, `GhostPromotionSystem`, `NetworkGatewaySystem` | ⛔ **none of the three, at any site.** Every construction site passes **`GhostCreationSystem` alone** — `NedReplicationModule.cs:219`, `BdcReplicationModule.cs:61`, and all eight test sites |
| disabling it blocks ghost **create** | ⛔ **No.** `GhostCreationSystem.Execute` is `{ }` — *"No-op: system is registered for pipeline consistency."* Ghosts are made by `CreateGhost(...)`, called **directly by the ingress translators** on the Input phase, a path no scheduler gate reaches. ⇒ **toggling `Enabled` changes nothing** |
| disabling it blocks ghost **promote** | ⛔ promotion was never in the group; it was registered standalone *(and now comes from `EntityCreationPack`)*. ⚠ **But LATENT, not live** — see the row below |
| disabling it blocks ghost **destroy** | ⭐ **moot**: `GhostDestructionSystem`, §3.10.3's first *"must be moved inside"*, was **DELETED** by `CE-144`. Destruction now goes through `NetworkSpawningSystem.ProcessDestroy` → the ELM |
| `GhostCreationSystem.BypassLifecycle` skips lifecycle + map registration during replay | ⛔ **written by 3 production sites, READ BY NONE.** `CreateGhost` does not consult it; it unconditionally sets `Ghost` and registers in the map |
| 🔴🔴 **does a replay deliver ghosts to promote at all?** | ⛔⛔ **YES — and an earlier version of this row said NO. RETRACTED `2026-09-11`.** That row's premise came from a grep for `SetLifecycleState`, and **the restore path does not use a setter**: 📐 `RecorderSystem` writes the entity index's **cold chunk** raw (`ENTITY_INDEX_COLD_TYPE_ID`), `PlaybackSystem.ApplyChunkData` restores it via `RestoreColdChunkFromBuffer`, and `EntityMetadataCold` carries `[FieldOffset(84)] LifecycleState`. ⇒ **an entity recorded while it was a ghost comes back as a ghost**, matches the promotion query, and the ungated `GhostPromotionSystem` advances it to `Constructing` and applies the TKB template — **mutating entities the LOG owns, with no live ingress involved.** ⇒ promotion's absence from the gate is **LIVE, not latent** |
| 🔴🔴 **`TogglableInputGroup` disabled ⇒ "block live DDS ingress"** *(the row's own Reason)* | ⛔⛔ **FALSE AS BUILT, measured `2026-09-11`.** That group holds the **logic-pack** input systems (`MissionControlExecutionSystem`, `FireProcessingSystem`, …) — §2.4 lists them. Every one of the **11** production `CycloneNetworkIngressSystem` registrations is a **direct** `RegisterSystem`/`RegisterGlobalSystem`, never into a togglable group; and `ReferenceReplayLoadHandler.SetSystemsEnabled` toggles only the four groups, touching **no** ingress system and **no** DDS participant. ⇒ 🔒 **live DDS ingress REACHES a node in `RunningReplay`** |

⇒ ⛔⛔⛔ **THE HONEST SUMMARY, after the `2026-09-11` follow-up measurement: ALL THREE mechanisms §2.1
names for the ghost path are inert or absent.** The lifecycle group gates a no-op; `BypassLifecycle` is
unread; and `TogglableInputGroup` does not contain the ingress systems at all. ⛔⛔ **And nothing stands behind them** — the
*"but the restore never writes `Ghost`"* consolation was **retracted `2026-09-11`** (see the table row):
the restore writes lifecycle as part of the cold chunk, so **recorded ghosts come back as ghosts** and the
ungated `GhostPromotionSystem` mutates them.

⭐⭐ **A live `EntityMaster` arriving mid-replay is, by itself, BENIGN — and that correction came from the
user.** 📐 Measured: the recording stores a **`MaxNetworkId`** high-water mark
*(`RecorderSystem`/`AsyncRecorder` → `RecordingMetadata.MaxNetworkId` → `ReplayModule.MaxNetworkId` →
`ReferenceReplayLoadHandler` → `ReplayConsensusAggregator` takes the cluster max)*, so a post-recording id
need not collide; a keyframe does **`repo.Clear()`** (`PlaybackSystem.ApplyFrame`, `frameType == 1`), so a
stray live entity is wiped; and a stale `NetworkEntityMap` entry cannot resolve WRONG because
`NetworkIdResolver.ResolveNetworkId` **verifies** a map hit against the entity's own `NetworkIdentity` and
degrades to a scan. ⇒ ⛔ **the earlier framing of this as "corruption from live ingress" was OVERSTATED.**
⚠ Two caveats: **no consumer was found that actually rebases an id allocator past `MaxNetworkId`** *(the
chain ends at the orchestrator's aggregate — not measured as wired)*, and the map is re-synced **only on
seek** *(`EcsRecordReplayController`'s `_afterSeek`)*, not per frame.

⇒ ⭐⭐⭐ **So the real defect is the one that needs no live traffic: promotion mutating RESTORED ghosts.**

⭐⭐⭐ **BUT THE RIGHT GATE ALREADY EXISTS AND IS UNDER-ADOPTED** — `CycloneNetworkIngressSystem`
`.IsWorldStateFrozen`, a `Func<bool>` asked **once per `Execute`** that skips exactly the
`TranslatorClass.WorldState` translators while letting control-plane ingress through *(so a resume can
still arrive)*. ⛔ It has **one** production writer — `CgfSubsystem.WireWorldStateFreezeGate`, driven by
the **DEBUGGER halt** (`CgfClusterDebugTimeController.IsWorldStateFrozen => _halted`, `DQ30-C`) — not by
replay, and on CGF only. ⇒ ⭐ **the replay fix is to adopt that seam**, not to populate the lifecycle group
*(wrong layer — `CreateGhost` is called from the translators, which no scheduler gate reaches)* and not to
implement `BypassLifecycle` *(the same job, one level coarser)*.

📄 Filed as **`CE-259ap`**; not fixed here, because adopting the gate **changes replay behaviour on every
host** and nothing has yet measured what depends on the current behaviour.

📐 **The measurement is pinned by rails**, in the feature's own suite
*(`Hrot.SimHost.Tests/ReplayLoadClusterOpHandlerTests`)*:
`RunningReplay_DoesNotStopADirectlyRegisteredInputPhaseSystem` *(the real handler, a real
`Commit(PrepareReplay)`, a real kernel — an `Input`-phase system registered the way every ingress system
is keeps executing)* and `TheReplayPathWiresNoWorldStateFreeze_AndIngressIsNeverInATogglableGroup`.
⭐ Both inverse-edit red-proofed: putting the probe inside the group reddens the first, and wrapping one
module's ingress in a `TogglableInputGroup` reddens the second.

#### 2.1b ⭐⭐⭐ WHY PROMOTING A REPLAYED GHOST IS WRONG — **it is not the ECS writes** *(measured `2026-09-11`)*

🔒 **The user's question, and it is the right one:** *"What is wrong about promoting a ghost from replay?
How does replay work? Does it replay the lifecycle state?"*

⭐⭐ **YES — replay replays the lifecycle state, and that is exactly why the ECS half is harmless.**
`PlaybackSystem.ApplyFrame` restores **raw chunks by index** each frame:

| what is restored | how |
|---|---|
| component masks | the **hot** chunk, `typeId == -1` → `RestoreHotChunkFromBuffer` |
| ⭐ **entity metadata, incl. `LifecycleState`** | the **cold** chunk, `typeId == -2` → `RestoreColdChunkFromBuffer`; `EntityMetadataCold.LifecycleState` sits at `[FieldOffset(84)]` |
| component data | one chunk per registered component type |
| ⭐ **event buffers** | `ReadAndInjectEvents` + `eventBus.ClearCurrentBuffers()` |
| the whole world, on a keyframe | `repo.Clear()` (`frameType == 1`) |

⇒ ⭐ **three of `PromoteGhost`'s four effects are OVERWRITTEN by the log** — the TKB `Inject` writes,
`SetLifecycleState(Constructing)` and the `GhostStateTracker` removal. **Redundant, not corrupting.**

### 🔴🔴 THE HARM IS THE FOURTH EFFECT — **a stateful protocol the log cannot rewind**

`PromoteGhost` ends in `_lifecycleModule.BeginConstruction(...)`, and that call:

| | |
|---|---|
| inserts into **`EntityLifecycleModule._pendingConstruction`** | a plain `Dictionary<Entity, PendingConstruction>` — ⛔ **not ECS state, so never recorded and never restored** |
| ⛔⛔ **throws if the entity is already there** | `if (_pendingConstruction.ContainsKey(entity)) throw new InvalidOperationException($"Entity {entity.Index} already in construction")` |
| 📐 and nothing resets it | `EntityLifecycleModule` has **no** `Clear()`/`Reset()`, and `ReferenceReplayLoadHandler`, `ReplayModule` and `EcsRecordReplayController` contain **zero** references to the ELM |

⇒ 🔒 **THE WORLD REWINDS; THE ELM DOES NOT.** Frame *N* promotion registers the entity as pending; the log
restores it to `Ghost`; frame *N+1* promotion calls `BeginConstruction` again ⇒ **throw** — and with
`FdpConfig.FailFastOnModuleException` defaulting `true` and `SystemScheduler.ExecuteSystem`'s `try/catch`
commented out, it **surfaces** rather than being swallowed.

⚠ **Conditional, and the conditions are characteristic of replay.** `DrainInstantComplete` clears the entry
only when `RemainingAcks.Count == 0 && currentFrame > StartFrame`, so it sticks when either the node has ELM
participants whose acks never arrive during playback, or — ⭐ **the replay-specific one** — **a SEEK rewinds
the replayed frame counter so `currentFrame > StartFrame` goes FALSE**, which is what seeking *is*.

⇒ ⭐⭐⭐ **This is why §2.1's gate named `LifecycleSystem` AND `GhostPromotionSystem` together:** both drive
that same non-recorded protocol, and the ack drainer is ungated during replay for the identical reason.
⛔ Neither was ever passed to the group.

⛔⛔ **And "how often does a recording capture a mid-flight ghost" is NOT a question worth asking** — 🔒 user:
*"if it can happen, it will one day. Who cares how often, needs to be handled every time."* An earlier note
here asked it; struck.

#### 2.1c ⭐⭐⭐ GATE, OR RECORD THE PENDING STATE? — **the user's question, and the answer is a third option** *(`2026-09-11`)*

🔒 **User:** *"is the gate promotion and the ack drainer at runtime really the right fix? shouldn't we
record/restore the pending promotions dictionary?"*

⭐⭐ **The instinct is right that gating alone is incomplete. But recording it would REVERSE AN EXPLICIT
POLICY, and there is a cheaper option that fixes the part gating misses.**

| the answer rests on | measured |
|---|---|
| replay is **state-RESTORE**, not re-execution | `PlaybackSystem.ApplyFrame` restores raw chunks; §2.1 disables input/sim/post-sim — physics *"would overwrite restored positions"* |
| lifecycle **is** recorded | cold chunk, `EntityMetadataCold.LifecycleState` @ offset 84 |
| ⭐⭐ **the ghost's promotion bookkeeping is DELIBERATELY NOT recorded** | `GhostStateTracker` carries **`[DataPolicy(DataPolicy.Transient)]`**, and `Transient = NoSnapshot \| NoRecord \| NoSave` — the enum's own doc: *"Completely transient… UI caches, temporary buffers, debug metrics"* |
| `_pendingConstruction` is not ECS state at all | a plain `Dictionary<Entity, PendingConstruction>`, holding a `HashSet<int>` of module ids — ⛔ not blittable, so not chunk-recordable as-is |
| ⛔ **a restored `Constructing` entity is a ZOMBIE** | `QueryBuilder.WithLifecycle`: *"Default: **Active** (excludes Constructing and TearDown)"* ⇒ invisible to every default query |
| ⭐ the codebase already reconciles non-recorded state at a boundary | `EcsRecordReplayController._afterSeek` → `NetworkEntityMap.RebuildFromWorld` |

### ⛔ ① RECORDING IT IS A MODEL CHANGE, NOT A BUG FIX

The recording has **already decided** this class of state is transient. To record the pending dictionaries
you must reverse `DataPolicy.Transient` on the tracker, add non-ECS side-channel state to the recording
format *(or move the dictionary into a component and replace `HashSet<int>` with a bitmask)*, and grow every
recording. ⇒ that is adopting a **faithful re-execution** model in a system built as **state restore**.

### ⭐ ② GATING IS CONSISTENT WITH THE EXISTING MODEL — **but only covers DURING playback**

The log is authoritative for lifecycle; the systems that would fight it are silenced. That is §2.1's own
design. ⛔ It says nothing about what happens when live resumes.

### 🔴 ③ THE HOLE GATING DOES NOT COVER, AND THE USER'S INSTINCT POINTS STRAIGHT AT IT: **branch-to-live**

`PrepareLive` after a replay resumes live simulation from the restored frame. An entity recorded
**mid-`Constructing`** comes back `Constructing` with **no `_pendingConstruction` entry and no
`GhostStateTracker`** *(both transient)* ⇒ **nothing will ever complete it**, and being non-`Active` it is
invisible to every default query. ⚠ A permanent zombie — and 📐 measured: nothing in the `PrepareLive` path
touches the ELM.

### ⭐⭐⭐ ④ THE LEAN — **RECONCILE AT THE BOUNDARY, DO NOT RECORD CONTINUOUSLY**

On `FinalizeReplay`/`PrepareLive`, rebuild the ELM's pending state from the **restored lifecycle states** —
⭐ exactly the shape `EcsRecordReplayController` already uses for `NetworkEntityMap.RebuildFromWorld` on
seek, in the same class, for the same reason *(a non-recorded index that time travel invalidates)*.

| option | cost | fixes |
|---|---|---|
| ⛔ record the dictionaries | format change · policy reversal · bigger recordings · `HashSet<int>` not blittable | playback **and** branch |
| ⭐ gate during playback *(§2.1's design)* | one composition change | playback only |
| ⭐⭐⭐ **gate + reconcile on branch** | the gate, plus one method with an existing precedent | playback **and** branch, at a fraction of the cost |

⚠ **What would change this lean:** if replay must become **re-executable** *(deterministic re-simulation
from a frame, not just scrubbing)*, then recording is right and the whole `Transient` policy needs
revisiting. ⛔ **That is a product question, not an implementation one** — and it is the one to put to the
user before building either.

#### 2.1d ⭐⭐⭐ GOING LIVE FROM A RECORDED STATE — **what the recording omits, and why RE-DERIVING beats RECORDING** *(`2026-09-11`)*

🔒 **User:** *"imagine we want to go to live from a recorded state…"* ⭐ Not hypothetical — `PrepareLive`
after a replay is a shipped path (`LiveFromReplayTests`), so **the restored frame must be a VALID LIVE
STARTING STATE**, which is a far stronger requirement than "scrubbing looks right".

📐 **Enumerated: everything the Flight Recorder omits** *(`DataPolicy.NoRecord`/`Transient`, components
only — event types are replaced wholesale by `ReadAndInjectEvents`)*. ⭐⭐ **The useful split is NOT
transient-vs-recorded, it is SELF-HEALING vs NOT:**

| omitted state | policy | does live traffic refill it on resume? |
|---|---|---|
| `NetworkTransform` · `NetworkVelocity` | `NoRecord` | ✅ **yes** — the owner republishes and dead reckoning drives from it. Recording them would be waste |
| `EgressPublicationState` | `Transient` | ✅ yes — a *"have I published"* cache; worst case one redundant republish |
| `ForceNetworkPublish` | `Transient` | ✅ a one-shot tag; absent IS the normal state |
| `GlobalDebugSettings` · `DebugState` | `Transient` | ✅ irrelevant to simulation |
| `GenesisIntentComponents` ×7 | `Transient` | ✅ creation-time intents, already consumed |
| 🔴 **`GhostStateTracker`** | `Transient` | ⛔ **NO** — nothing re-creates it, and promotion needs it to evaluate soft timeouts |
| 🔴 **`PendingNetworkAck`** | `Transient` | ⛔ **NO** — its own doc: *"entities awaiting network acknowledgment … removed after publishing lifecycle status"* ⇒ an entity restored mid-wait never publishes it |
| ⚠ `MissionAdapterState` | `Transient` | ⚠ **not measured** — CGF mission-adapter state |
| 🔴🔴 **`EntityLifecycleModule._pendingConstruction` / `_pendingDestruction`** | not ECS state at all | ⛔ **NO** |

⇒ ⭐⭐⭐ **The `Transient` policy is RIGHT for the first group and WRONG for the second.** `GhostStateTracker`
carries the same attribute as UI caches and debug metrics, and the enum's own doc describes that group as
*"UI caches, temporary buffers, debug metrics"* — ⛔ **mid-handshake protocol state is none of those.** That
misclassification, not the gate, is the root of the branch-to-live hole.

### ⛔⛔ BUT RECORDING `RemainingAcks` WOULD BE WORSE THAN NOT RECORDING IT

⭐ The **authoritative** fact IS recorded — `LifecycleState`. So *"which entities are mid-construction"* is
**derivable** from the restored world *(`Constructing` + `TkbIdentity`)*. ⛔ The only part that is NOT
derivable is **how far the handshake had got** — `RemainingAcks`.

🔴 **And restoring that is not meaningful, because it is a DISTRIBUTED handshake.** Your node's half is
restored to frame *T*; the peers that already sent those acks are not, and they will not resend. ⇒ a
faithfully-restored `RemainingAcks` **waits forever for an ack nobody owes** — a deadlock, strictly worse
than starting over. ⚠ Recording it only becomes correct if **every** node restores the **same** frame with
the **same** partial state, in lockstep, which replay does not guarantee per node.

### ⭐⭐⭐ SO THE MODEL FOR GOING LIVE IS: **restore the WORLD from the log, then RE-DERIVE every in-flight protocol from it**

⭐ One `ResumeFromRestoredState()` pass at the `FinalizeReplay`/`PrepareLive` boundary:

| for each restored entity | do |
|---|---|
| `Ghost` | re-attach `GhostStateTracker`, stamped with the **resume** frame *(so soft timeouts run from now, not from a recorded past)* |
| `Constructing` + `TkbIdentity` | re-open a construction with a **fresh** participant set — re-run the handshake rather than resurrect a stale one |
| `TearDown` | re-open a destruction, same reasoning |
| any | clear the egress publication caches so the first live frame republishes a full baseline |

⭐ **The precedent is in the same class:** `EcsRecordReplayController._afterSeek` already re-derives
`NetworkEntityMap` via `RebuildFromWorld`, for exactly this reason — a non-recorded index that time travel
invalidates. ⇒ this is that pattern applied to the other three.

⇒ ⭐⭐ **And it subsumes the gate question:** with the protocol re-derived at the boundary, gating during
playback stays desirable *(it stops pointless work and the `BeginConstruction` throw)* but is no longer the
thing correctness rests on. ⛔ **Neither half is built.** 📄 `CE-259ap`.

#### 2.1e 🔴🔴🔴 SUPERSEDED BY §2.1f — **THE SWEEP — `ResumeFromRestoredState` AS PROPOSED IS NOT RELIABLE, for two measured reasons** *(`2026-09-11`)*

> 🔴🔴🔴 **SUPERSEDED THE SAME DAY BY [§2.1f](#21f----retraction--class-b-is-self-healing-21es-biggest-class-was-not-a-defect-2026-09-11-decided-by-code-analysis-at-the-users-instruction).**
> ⛔ **This section's central claim — that ≥20 class-B change-detection caches are broken by a rewind — is
> FALSE**, and so are its step-2 (generalised boundary event) and step-3 (reflection subscription rail)
> remedies. ⭐ **What survives and is still correct:** the *process* lesson that three hand sweeps by a
> motivated author each missed holders *(and that a hand-maintained enumeration therefore cannot be kept
> true)*, and the class-C half. ⇒ **read §2.1f before quoting anything below.**

🔒 **User:** *"do the sweep for `Dictionary<Entity,` in module fields, check if reconstructing on
`resumeFromRestoredState` is reliable the way you suggest."* ⭐ It is not. Here is the measurement.

🔴🔴🔴 **CORRECTED `2026-09-11`, SAME DAY — THE SWEEP BELOW WAS TOO NARROW AND ITS COUNTS ARE WRONG.**
🔒 User: *"are you using codebase memory as companion to grep which is known to omit lots of occurrences?"*
⛔ **I ran it on bare grep.** Re-running with `search_code` and then **reading** each field instead of
bucketing by name found three defects, and **the dominant one was my own query design, not grep**:

| # | what was wrong | proof |
|---|---|---|
| **①** | ⛔⛔ **the SHAPE was too narrow — I swept only `Dictionary<Entity,…>`.** Per-entity state keyed by **NETWORK ID** (`Dictionary<long,…>`) is equally per-entity and equally rewind-sensitive | 🔴 **`CycloneNetworkCleanupSystem._trackedEntities` is `Dictionary<long, Entity>`** ⇒ **my sweep would have missed the very holder §3.10.4 already names.** So is **`EntityRequestFinalizationSystem._tracked`** — pending entity-creation requests, in the pack `P2` just extended |
| **②** | ⛔ **I bucketed by NAME PATTERN** (`_last*`/`_known*`/`_prev*`/`_tracked*`), so class B under-counted | `MapRouteEgressTranslator._publishedVersions` is a textbook change-detection cache and matched none of those prefixes |
| **③** | ⚠ **a third shape exists: ECS components marked `Transient`** — not a field sweep at all | `EgressPublicationState.LastPublishedTickMap` is exactly a class-B cache living inside a component the recorder omits *(§2.1d)* |
| ⭐ | grep-vs-graph was NOT the main error | `search_code` found 7 files grep's modifier-anchored regex dropped — 📐 **all locals, not cross-frame fields**, so the file coverage held. ⛔ The damage came from ① and ② |

⇒ ⭐⭐⭐ **AND THAT IS THE ARGUMENT, NOT A FOOTNOTE: if three sweeps by an author who knew what he was
looking for still missed the design's own example, NO enumeration can bound this set.** ⛔ A
`ResumeFromRestoredState()` listing holders is unmaintainable **by demonstration**, not by prediction.
⇒ 🔒 **the seam must be SUBSCRIBE-based, with a rail asserting subscription** — §3.10.4's own prescription.

📐 **The classification below is therefore QUALITATIVE — the classes are right, the counts are a FLOOR,
not a total.** Swept `Dictionary<Entity,…>` · `HashSet<Entity>` · `Queue<Entity>` fields (tests/examples
excluded), classified by **what the holder DOES**:

| class | count | does a restore invalidate it, and can it be RE-DERIVED? |
|---|---|---|
| **A — per-frame scratch** *(`_entityList`, `_toAdd*`, `_stale*`, `_visited`, `_destructionLog`, `_promotionQueue`/`_inQueue`)* | **15** | ✅ **safe, needs nothing** — rebuilt every frame. 📐 Verified for the one in the system this programme touched: `GhostPromotionSystem._inQueue` is removed from on dequeue |
| 🔴🔴 **B — change-detection caches** *(`_last*`, `_published*`, `_known*`, `_prev*`, `_tracked*`)* | **≥ 20** | ⛔⛔ **CANNOT be re-derived — the world does not know what was SENT.** They can only be **INVALIDATED.** ⚠ Known additions the first pass missed: `MapRouteEgressTranslator._publishedVersions` · `CycloneNetworkCleanupSystem._trackedEntities` *(network-id keyed)* · `EntityDamageEgressTranslator._lastPublished` · `MissionControlExecutionSystem._missionVersions`/`_taskOrder` · `EgressPublicationState.LastPublishedTickMap` *(inside a `Transient` component)* |
| **C — pending protocol promises** *(`_pending*`, `_tracked`)* | **≥ 10** | ⚠ partly — *which* entities is derivable from `LifecycleState`; *how far the handshake got* is not, and must be restarted. ⚠ Additions the first pass missed: 🔴 **`EntityRequestFinalizationSystem._tracked`** *(pending entity-creation requests — in the pack `P2` extended)* · `EntityInfoIngressTranslator._pendingSubordinates`/`_pendingUnspawnedSubordinates` · `MapRouteIngressTranslator._pendingRoutes` · `MapVisualOverlayIngressTranslator._pendingOverlays` · `BinaryGhostStore.StashedData` |
| **D — bindings to external engine objects** *(`_visuals`, `_bodies`, `_bound`, `_routes`, `_active*`, `_injected*`)* | **8** | ✅ mostly self-heal — they reconcile by **liveness**, and the restore bumps generations so stale keys go dead and get pruned |
| the remainder *(diag accumulators, guid maps, selection sets, debug history)* | ~29 | ✅ presentation/diagnostic — no simulation consequence |

### 🔴 REASON 1 — **my three cases were all class C. The BIGGEST class was untouched.**

⭐⭐ **15 of the 17 class-B holders are EGRESS TRANSLATORS.** After a rewind the cache says *"already
published V"* while the restored world holds *V′* ⇒ the translator **skips the publish** ⇒ **peers never
learn the restored state.** ⛔ Re-deriving is impossible in principle: the ECS world records what IS, never
what was **sent**. ⭐ The only correct operation is to **clear** them, which forces a full baseline
republish — safe, at the cost of one burst.

⚠ **And that burst is a known, documented cost**: §3.10.4 describes the inverse *"scrub flood"* — *"severe
DDS congestion and visual pop-in"* — so a resume-time baseline burst must be deliberate, not accidental.

### 🔴 REASON 2 — **an ENUMERATING fix rots by construction, and this section is the PROOF**

📐 Dozens of holders across ~45 files **today**, in **at least three shapes** *(`Entity`-keyed fields,
network-id-keyed fields, and caches inside `Transient` components)*, and the pattern is idiomatic: every new
egress translator adds a `_lastPublished…`. ⇒ ⛔ a hand-written `ResumeFromRestoredState()` that NAMES its
holders is wrong the day someone adds the next one, and nothing would tell them.
⭐⭐⭐ **The correction block at the top of this section is the demonstration:** three sweeps by an author
who knew the target still missed `CycloneNetworkCleanupSystem._trackedEntities` — **the one holder this
design had already named** — and `EntityRequestFinalizationSystem._tracked`, in the pack that had just been
edited. 🔒 **If the enumeration cannot be produced reliably by hand, it cannot be MAINTAINED by hand.**

### ⭐⭐⭐ WHAT IS RELIABLE — **a boundary EVENT holders SUBSCRIBE to, which this design ALREADY PRESCRIBED**

⭐⭐ §3.10.4 already specified exactly this, for exactly one holder: *"`ReferenceReplayLoadHandler` (or
`PlaybackTickSystem`) must expose a `SeekCompleted` callback or event. `CycloneNetworkCleanupSystem`
registers with this callback and clears `_trackedEntities` when a seek completes."*
📐 **Measured `2026-09-11`: NOT BUILT** — `CycloneNetworkCleanupSystem._trackedEntities` exists, and no
`SeekCompleted` hook exists anywhere.

⇒ ⭐ **So the fix is to GENERALISE that prescription, not to invent a new entry point:** one boundary event
raised on **seek** *and* on **`FinalizeReplay`/`PrepareLive`**, which holders opt into. ⭐ And the
subscription is checkable — a rail can assert every class-B/C holder subscribes, which an enumeration in one
method can never guarantee.

| ⭐ the classification rule, so the next author decides correctly without reading this section | |
|---|---|
| **reconciles against the world every frame** *(liveness-checked)* | ✅ subscribe to nothing — the restore's generation bump prunes it |
| **caches a VALUE it compares against** | ⛔ **subscribe and CLEAR** — it cannot be re-derived, and a stale entry SUPPRESSES a needed publish |
| **holds a PROMISE it waits on** | ⛔ **subscribe and RESTART** — re-derive the set from `LifecycleState`, with a FRESH participant set *(§2.1d: restoring partial ack progress deadlocks)* |

⇒ ⛔⛔ **`CE-259ap`'s earlier lean — "one `ResumeFromRestoredState()` pass re-deriving three things" — is
SUPERSEDED by this.** It was right about class C and blind to class B, and it chose the one shape that
cannot be kept true.

⚠⚠ **And four rails are GREEN over it** — `ReplayLoadClusterOpHandlerTests`, `LiveFromReplayTests`,
`NodeBootstrapperReplayTests`, `FullBranchPipelineTests`, **12/12** — because they assert the flag and the
group's `Enabled` **flip**, never that either has an **effect**. 📌 `R-142` ③'s shape: the setter is
tested, and the setter is all there is.

#### 2.1f 🔴🔴🔴 RETRACTION — **CLASS B IS SELF-HEALING. §2.1e's biggest class WAS NOT A DEFECT** *(`2026-09-11`, decided by CODE ANALYSIS at the user's instruction)*

> 🔒 **User, verbatim:** *"but you have to decide from analyzing the code as catching a failure is unreliable."*
> ⇒ ⛔ a non-reproducing rail proves nothing *(the same logic as "an absence in grep is an absence in your
> pattern")*, so the verdict below is composed from measured facts, not from an attempted repro.

⛔⛔ **§2.1e claimed ≥20 change-detection caches are broken by a rewind, because *"the cache says 'already
published V' while the restored world holds V′ ⇒ the translator SKIPS the publish ⇒ peers never learn the
restored state."* 🔴 THAT IS WRONG, and it is wrong three times over.**

⭐⭐⭐ **The reasoning error, stated once:** a *"last published value"* cache mirrors **WHAT THE PEER KNOWS**,
and the peer is **not rewound**. ⇒ a cache holding a value *"from the future"* is **not corruption** — the
peer really did receive it. What the peer needs is the **current** state, and an equality compare against
the current state delivers exactly that. ⇒ 🔒 **the cache suppresses a publish only when `cached ==
current`, in which case the peer ALREADY HOLDS the current value and skipping is CORRECT.**

| # | the mechanism | measured |
|---|---|---|
| **①** | ⭐⭐ **every hand-rolled holder is an EQUALITY compare, not a `>=` MONOTONIC guard** | `MapRouteEgressTranslator.cs:96-98` `lastVersion == routePlan.Version` · `AnimationMontageQueueEgressTranslator.cs:74` `lastVer == queue.QueueVersion` · `AnimationMontageQueueStateEgressTranslator.cs:62-64` · `AnimationChannelStatusEgressTranslator.cs:63-65` — all tuple/scalar `==` |
| **②** | ⭐⭐ **a keyframe WIPES the managed publication state, and its absence FORCES a publish** | `ManagedComponentTable<T>` is stored in the **same** `_componentTables` dictionary as the blittable tables (`EntityRepository.cs:1219`, `:1373`, `:1422`) and `Clear()` (`:423-439`) iterates `_componentTables.Values` ⇒ `EgressPublicationState` is wiped by the keyframe `repo.Clear()`; `SmartEgressUtil.ShouldPublish:92-95` then returns **`true`** — *"Default to safe behaviour: publish, so no data is silently dropped"* |
| **③** | ⭐⭐ **egress RUNS during playback, so the cache tracks the LOG frame by frame** | every `CycloneEgressSystem` registration is a **direct** `RegisterSystem`/`RegisterGlobalSystem` — `NedReplicationModule.cs:308`, `BdcReplicationModule.cs:82`, `IgNodeBootstrapper.cs:555`, `SimHostApp.cs`, ×4 `NedSimHost*Translators` — never into a togglable group; `SetSystemsEnabled` touches only the four groups; `ModuleHostKernel.cs:752` runs `SystemPhase.Export` **unconditionally** and no replay-conditional phase skip exists in the kernel or core |

⇒ ⭐⭐⭐ **The hazard §2.1e described would need a MONOTONIC guard** *(a rewind lowers the current
tick/version below the cached one ⇒ suppressed forever)*. 📐 **The one once-guard that exists —
`SmartEgressUtil.ShouldPublish:112-115`, `return !state.LastPublishedTickMap.ContainsKey(ordinal)` for
reliable descriptors — lives in the managed component mechanism ② wipes.** ⇒ **no surviving instance.**

⛔ **ONE HOLDER LEFT UNCLASSIFIED:** `MissionControlExecutionSystem._missionVersions`
(`Hrot.Common/Systems/MissionControlExecutionSystem.cs:70`, read at `:137`, written at `:191`/`:214`/`:237`)
is a **command-dedup** counter, not an egress cache. ⚠ Not classified — do not count it either way.

#### ⇒ WHAT THIS CHANGES IN THE PLAN

| ⭐ | |
|---|---|
| ⛔⛔ **DROP the reflection-based subscription rail** *(§2.1e step 3)* | ⭐ it would flag ~20 **self-healing** holders and be switched off within a batch — 📌 exactly the failure `CLAUDE.md` records for the optional-dependency sweep. ⚠ It was the answer to a problem that does not exist |
| ⛔ **DROP the generalised boundary event as a class-B remedy** | ⭐ §3.10.4's own example (`_trackedEntities`) is class A, not B: `CycloneNetworkCleanupSystem.Execute` step 1 **re-scans the whole world every frame** and re-adds (`CycloneNetworkCleanupSystem.cs:44-62`), and disposes only on a `DestructionOrder` **event** (`:65-104`) ⇒ it self-heals. 📌 The *"mass DISPOSE flood"* `ONBOARDING.md:252` describes belonged to a **liveness-scanning** version of that system that no longer exists |
| ⭐⭐⭐ **KEEP the class-C reconciliation, and it is now the ONLY defect** | ⭐ and it is BOUNDED — the ELM's `_pendingConstruction`/`_pendingDestruction` plus `EntityRequestFinalizationSystem._tracked`, each re-derivable from `LifecycleState`, **which IS recorded** (§2.1b). ⇒ a hand-written `ResumeFromRestoredState()` **does not rot here**, because its input is the recorded lifecycle rather than an open-ended cache inventory |

#### 2.1g ⛔⛔ A FOURTH INERT REPLAY PROTECTION — **`AfterSeekCallback` is a NON-NULL EMPTY LAMBDA** *(`2026-09-11`)*

📐 `NedReplicationModule.cs:111-114`:

```csharp
public Action? AfterSeekCallback =>
    _cleanupSystem != null ? (Action)(() => {
        //_cleanupSystem.ResetTracking();
    }) : null;
```

| 📐 measured | |
|---|---|
| ⛔⛔ **`ResetTracking` exists in ZERO C# files** | grep over the repo returns **one** hit — the commented-out line above. `CycloneNetworkCleanupSystem.cs` read **in full** (107 lines): no such method |
| ⛔ **yet `T-RMF-23` is ticked `[x]` DONE** | `.dev/_DONE/replay-and-modules/TASK-TRACKER.md:36`, and `reviews/BATCH-04-REVIEW.md:34` states *"`CycloneNetworkCleanupSystem.ResetTracking()` added"* with the property quoted as *"clean"* ⇒ it landed and was later removed, leaving the call site commented out to keep it compiling |
| 🔴 **a non-null empty lambda is STRICTLY WORSE than `null`** | ⭐ every downstream null-check passes (`StrideNodeBootstrapper.cs:402`, `CgfSubsystem.cs:982-986`, `SimHostNodeBootstrapper.cs:429`) and every rail asserting *"the afterSeek callback is wired"* is satisfied by a callback that does nothing |

⇒ ⭐ **The fix is one line and it is NOT to resurrect the method** *(§2.1f shows `_trackedEntities`
self-heals)*: **return `null` and say why**. ⛔ Do not leave a protection that reads as wired.
📄 `CE-259ap`.

#### 2.1h 🔴🔴🔴 THE ELM'S BOOKKEEPING IS NOT REWIND-SAFE, AND `CheckTimeouts` TURNS THAT INTO A DESTROY — **during playback, not just at resume** *(`2026-09-11`)*

⛔⛔ **§2.1f left the class-C fix at the RESUME boundary. That is too late**, and the user's framing is the
correct one: ⭐⭐⭐ **the ELM's dictionaries are bookkeeping ABOUT A WORLD, so they must be discarded at every
WORLD REPLACEMENT** — entering replay, **every seek**, and the live boundary — not only when resuming.

##### ⭐⭐ The hazard chain, each link measured

| # | link | status |
|---|---|---|
| **①** | `LifecycleSystem` is a **DIRECT** registration (`EntityLifecycleModule.cs:94`, `registry.RegisterSystem(new LifecycleSystem(this))`) — never inside a togglable group ⇒ ⭐ **`DrainInstantComplete` and `CheckTimeouts` run EVERY TICK DURING PLAYBACK** (`LifecycleSystem.cs:41`/`:44`) | ✅ measured |
| **②** | a pending entry survives into the replay — the module has **no** `Clear()`/`Reset()` and nothing in the replay path touches it | ✅ measured (§2.1b) |
| **③** | the replayed frame drops **below** the entry's `StartFrame` *(the normal case: the live counter is ahead of the recording; a backward **seek** does it too)* | ✅ by construction |
| **④** | `CheckTimeouts:358` computes `currentFrame - kvp.Value.StartFrame > _timeoutFrames` on **`uint`** ⇒ the subtraction **wraps to ~4.29 × 10⁹**, which exceeds any `_timeoutFrames` ⇒ timeout fires | ✅ measured |
| **⑤** | ⇒ `cmd.DestroyEntity(entity)` on a **stale handle from the pre-replay world** (`:371`) | ✅ measured |
| **⑥** | that index now holds a **different** restored entity | ⚠ **PLAUSIBLE, NOT PROVEN** — the restore reuses explicit indices (`RestoreEntity(index, …)`), so a collision is likely, but this session did not prove it for any given entity |
| **⑦** | 🔴 **the generation guard is DEBUG-ONLY** — `EntityIndex.cs:148-157` sits inside `#if FDP_PARANOID_MODE`, and `Fdp.Core.csproj:12-14` defines it under `Condition="'$(Configuration)'=='Debug'"` | ✅ measured |

⇒ ⭐⭐⭐ **Debug: a loud `"Entity … is stale"` throw. 🔴 RELEASE: NO GUARD** — `EntityIndex.DestroyEntity`
clears the component mask, bumps the generation and marks the slot inactive **on the innocent restored
entity.**

⚠ **Stated fairly — the WINDOW is narrow** *(see §2.1i: both production sites build the ELM with an EMPTY
participant list, so `_pendingConstruction` normally holds an entry for ONE frame)*. ⛔ **That narrows the
window, not the verdict** — 🔒 *user, `2026-09-11`: "if it can happen, it will one day. Who cares how often,
needs to be handled every time."*

##### ⭐⭐ R-129 — **THE DESIGN ALREADY RECORDED THIS, FOR THE OTHER TRIGGER**

📄 **`docs/DESIGN_Deterministic_Network_Ids.md` §2b** enumerates what `NetworkSpawningSystem` holds outside
the repository and its third row reads: *"🔴 `EntityLifecycleModule` · mutable ✅ · rewound by preview? 🔴
**NO** · `_pendingConstruction`/`_pendingDestruction` are keyed by **`Entity` handles the rewind
invalidates**."*

⇒ ⭐⭐ **The hazard was recorded for the EDITOR'S PREVIEW rewind and never generalised to REPLAY** — the same
mechanism, a different trigger. 📌 A durable lesson: *"is this state rewind-safe?"* has **two** triggers in
this codebase, and a doc that answers it for one is not an answer for the other.
⚠ **One correction to that row:** it lists `_blueprintRequirements` among the rewind-invalidated state.
⛔ It is not — that is **registration** state (like `_globalParticipants`), unaffected by a rewind. Only the
two `Entity`-keyed dictionaries are.

##### ⭐ The rule this produces

| ⭐ | |
|---|---|
| ⭐⭐⭐ **CLEAR on every world replacement** | `PrepareReplay` · **every seek** · `FinalizeReplay`/`PrepareLive` |
| ⭐⭐ **RECONSTRUCT only when resuming to a LIVE world** | ⛔ reconstructing during playback would re-open protocols the log is about to overwrite |
| ⭐ **the clear belongs ON the ELM, privately** | ⛔ `BeginConstruction:165-168` **throws** on re-entry, so a resume that does not clear first cannot run. ⚠ Do **not** add a public `ResetPending()` any caller can reach |

#### 2.1i ⭐⭐ WHAT THE PARTICIPANT SETS ACTUALLY ARE — **and why `BlueprintId` is the one datum that must be recovered**

📐 `BeginConstruction:171-175` computes the ack set as **`_globalParticipants ∪ _blueprintRequirements[blueprintId]`**.

| | what it is | keyed by | measured population |
|---|---|---|---|
| **`_globalParticipants`** | modules that must ACK **every** construction/destruction | — node state | ⛔ **both production ctor sites pass an EMPTY list** — `EditorSubsystem.cs:1384` `Array.Empty<int>()`, `HrotNodeBuilder.cs:238` `new List<int>()`. ⭐ The **one** production adder is `NetworkGatewaySystem.cs:88` (`_elm.RegisterModule(_gatewayModuleId)`) |
| **`_blueprintRequirements`** | modules that must ACK constructions **of one blueprint type only** | ⭐⭐ **`blueprintId`** | ⛔⛔ **`RegisterRequirement` has ZERO callers** — the union at `:172-175` is a **no-op today** |

⭐⭐ **Why the per-blueprint set exists at all:** not every entity type gives every module work to do. A
vehicle may need physics and turret setup to finish before it goes `Active`; a marker needs neither. Without
the per-blueprint set, **every** entity would wait for **every** registered module, making the slowest module
the floor for every spawn.

##### ⭐⭐⭐ THE DESIGN RECORD EXISTS — **and finding it took a TOPICAL search, not a name search** *(user, `2026-09-11`)*

> 🔒 **User:** *"search blueprint requirements in docs. they must have a good reason and maybe it is a bug
> they are not written today."* ⛔⛔ **An earlier version of this section said *"searched `docs/` and `.dev/`,
> no design record found."* That was WRONG** — and wrong in the exact way `CLAUDE.md` warns about: the search
> was anchored on the IDENTIFIER (`RegisterRequirement`, `blueprintRequirement`, "per-blueprint ack"). 📐 The
> record is in **`FDP/Engine/Fdp.ModuleHost/docs/ModuleHost-network-ELM-design-talk.md`** and never uses any
> of those words. ⇒ 📌 **search the TOPIC — "what must happen before an entity goes Active" — not the name.**

📐 **The intent, verbatim** *(§1, "The Interaction Model: Local ELM + Network Triggers")*:

| node | the design says |
|---|---|
| **originator (A)** | *"**Local ELM:** Node A's ELM coordinates local modules (Physics, AI). **Activation:** Once local modules ACK, the entity becomes `Active` locally."* |
| **replica (B)** | *"**Local Initialization:** Node B's Physics/Renderer modules initialize resources. **Activation:** Once Node B's local modules ACK, the entity becomes `Active` on Node B."* |
| **partial ownership** *(§2)* | the node must know *"**a priori** (via configuration or logic based on `DisType`) that it is supposed to own the Weapon"* — ⭐ **the per-entity-TYPE axis `_blueprintRequirements` implements** |
| **the peer barrier** *(§Part 1)* | *"To support the 'Reliable' option where Node A waits for Node B, we need to integrate the Network Gateway into the local ELM loop as a **blocking participant**."* |

⇒ ⭐⭐⭐ **The barrier exists so nothing simulates, draws or publishes a half-initialised entity**, and the
per-blueprint set is the refinement that stops a marker waiting on the physics module.

#### 2.1j 🔴 THE CONSTRUCTION BARRIER IS **VACUOUS**, NOT INERT — **the ELM IS the barrier, and it works; its PARTICIPANT REGISTRY is empty** *(`2026-09-11`, corrected the same day)*

> ⭐⭐⭐ **USER CORRECTION, and it is the right distinction:** *"isn't ELM the implementation of the barrier?
> entity in constructing state waiting for ack from all registered modules?"*
> ⛔⛔ **YES — and an earlier version of this section said the barrier was *"inert"* and *"the whole handshake
> is unwired."* That was WRONG and it pointed at the wrong fix.**

⭐⭐ **The ELM IS the barrier, and every part of the mechanism is present and correct:** the entity is held in
`Constructing`; `RemainingAcks` is the wait-set; `ProcessConstructionAck:283-293` promotes to `Active` the
moment the set empties; `CheckTimeouts:364-373` destroys the entity if it never does. ⛔ **Nothing is missing
from the ELM.**

⇒ 🔒 **It is a REGISTRY-DRIVEN barrier, and the registry is EMPTY.** *"Waiting for acks from all registered
modules"* is exactly what it does — and **"all registered modules" is the empty set**, so the wait is
satisfied **vacuously** and `DrainInstantComplete` promotes on the next frame.

⭐⭐ **AND THE ONE FRAME IS NOT WASTED — the invariant that DOES hold today:**
📐 `EntityLifecycleModule.RegisterSystems:92-94` registers `BlueprintApplicationSystem` **then**
`LifecycleSystem`, both `[UpdateInPhase(SystemPhase.BeforeSync)]`; within a phase, absent an `[UpdateAfter]`
edge, the scheduler runs them in **registration order**. `BlueprintApplicationSystem:33-40` consumes the
`ConstructionOrder` and injects the TKB template via the translators; `DrainInstantComplete:325` additionally
requires `currentFrame > StartFrame`. ⇒ 🔒 **"the TKB template is injected before the entity is `Active`"
holds BY CONSTRUCTION.**

⛔ **What does NOT hold is the design's stated invariant** — *"Physics/Renderer modules initialize resources …
once local modules ACK, the entity becomes `Active`"* — because **no such module registers or acks.**

##### ⭐⭐ TWO DIFFERENT DEFECTS, WHICH THE "INERT" WORDING CONFLATED

| axis | verdict | the fix |
|---|---|---|
| ⭐ **module barrier** *(ELM `RemainingAcks`)* | ✅ **implemented and correct — VACUOUS** because nobody registers | ⛔ **nothing to build in the ELM.** ⭐ The work is on the MODULE side: a module that allocates resources for an entity calls `RegisterModule` and `AcknowledgeConstruction`. ⚠ Small and local per module — **but it changes when entities go `Active` cluster-wide**, so it is an architect call |
| 🔴 **peer barrier** *(`ReliableInitType`/`PendingNetworkAck`)* | 🔴 **GENUINELY DANGLING — a produced component with no consumer** | `NetworkSpawningSystem.cs:159-160` **adds** `PendingNetworkAck`; its **only** reader is `NetworkGatewaySystem`, which is **never constructed in production**. ⇒ the tag accumulates unread and `ReliableInitType.AllPeers` has no effect |

⇒ ⭐ **Only the second is a broken wire.** The first is an adoption gap in a working mechanism — 📌 the seam
law's usual shape: *the mechanism exists and is under-adopted*, not missing.

| # | measured | ⇒ |
|---|---|---|
| **①** | `RegisterRequirement` — **zero callers** | the per-blueprint union at `BeginConstruction:172-175` is a no-op |
| **②** | both production ctor sites pass an **empty** participant list — `EditorSubsystem.cs:1384`, `HrotNodeBuilder.cs:238` | `_globalParticipants` starts empty |
| **③** | the **only** production `RegisterModule` caller is `NetworkGatewaySystem.cs:88` … | …and 🔴 **`NetworkGatewaySystem` IS NEVER CONSTRUCTED IN PRODUCTION** — `new NetworkGatewaySystem(...)` appears **only** in `NetworkGatewaySystemTests.cs:74/100/126` |
| **④** | ⇒ `EntityLifecycleModule.AcknowledgeConstruction` — the sole publisher of `ConstructionAck` — is called by **nothing in production** *(all five call sites are inside the never-built gateway)* | 🔒 **no construction is ever acked** |
| **⑤** | ⇒ every `BeginConstruction` takes the **zero-ack branch** (`:185-198`) and `DrainInstantComplete:325` promotes to `Active` on the next frame | ⭐ **construction is a ONE-FRAME formality, not a handshake** |
| **⑥** | the PEER axis is inert too: `PendingNetworkAck` is added at `NetworkSpawningSystem.cs:159-160` and **consumed only by the gateway** | ⇒ `ReliableInitType.AllPeers` — set by IG at `IgApplication.cs:3614`/`:3714`, and made per-request by the **fixed** `CE-143` — **has no effect** |
| **⑦** | `ConstructionOrder`'s other production readers are `BlueprintApplicationSystem.cs:33` *(applies the TKB template — the real work)* and `DataDrivenGizmoSystem.cs:304`. **Neither acks.** | ⇒ the order is used as a **notification**, never as a barrier |

##### ⚠⚠ TWO GREEN RAILS SIT OVER THIS — **`R-142` ③ again**

| rail | what it actually proves |
|---|---|
| `NetworkGatewayIntegrationTests` *(`PACK3-N004`)* | it asserts a `SpawnEntityCommand` with `ReliableInitType.AllPeers` **reaches `Active` on both nodes**. ⛔ With the barrier inert the entity reaches `Active` **whatever** the `InitType` — it would pass with `None`, and it passes with the gateway absent. 📌 Its own summary says it passed *after* *"PACK3-N002 (deletion of legacy `NetworkGatewaySystem` clones)"* — ⇒ **it passed BECAUSE the deletion left nothing wired**, and read that as confirmation of correct wiring |
| `NetworkGatewaySystemTests` *(3 tests)* | they construct the gateway directly ⇒ **they test a system no host runs** |

##### ⭐ What this does and does NOT license

| | |
|---|---|
| ⭐⭐ **the framing changes** | ⛔ an earlier version of this section called `_blueprintRequirements` *"an opt-in capability, not a vestige"* (the `BTreeTick` shape). ⚠ **Too generous:** the design shows a barrier that was **specified and never wired**, which is a different thing from a capability deliberately left dormant |
| ⛔⛔ **do NOT claim this breaks the product today** | ⚠ **NOT MEASURED:** whether an entity going `Active` one frame after creation — before physics/render set up resources — causes visible harm. Many ECS designs tolerate it (systems pick the entity up on their next tick). ⭐ The `TwoAck-DESIGN.md` §1 *"half-baked entity"* complaint is evidence the problem was FELT, but it is about the **IOS ack**, not local module readiness |
| ⛔ **and do NOT delete anything** | 🔒 `CLAUDE.md`: *"prefer ROUTING to DELETING"*. ⭐ The right question is whether to **wire** the barrier, and that is a user/architect call with cluster-wide blast radius |
| ⭐ **effect on the replay work** | it **simplifies** the reconstruction *(`RemainingAcks` is always empty ⇒ recomputation is trivially correct)* — ⚠ **but if the barrier is ever wired, recovering `BlueprintId` stops being theoretical and becomes load-bearing** |

📄 Filed as `CE-259au`.

⇒ ⭐⭐⭐ **THIS IS WHY THE RECONSTRUCTION NEEDS `TkbIdentity.TkbType` AND NOT JUST "re-open with the global
set".** `_globalParticipants` is pure node state and survives the rewind untouched; `_blueprintRequirements`
is keyed on the **blueprint id**, which lives only on the entity. ⇒ **recovering `BlueprintId` is what keeps
the recomputed ack set correct once anyone starts using requirements** — ⭐ and `TkbIdentity` is recorded
(no `[DataPolicy]`; *"lives on the entity forever"*), attached by **both** production paths
(`NetworkSpawningSystem.cs:148`, and ghosts carry it before `GhostPromotionSystem.cs:195` reads it).

##### ⚠ Two consequences of the empty sets, stated so nobody over-reads them

| | |
|---|---|
| ⭐ **construction is normally a ONE-FRAME state, not a distributed handshake** | with no participants, `BeginConstruction:185-198` takes the zero-ack branch and `DrainInstantComplete:325` promotes on the next frame (`currentFrame > StartFrame`) |
| ⛔⛔ **the zombie claim SURVIVES this** | `DrainInstantComplete:324` iterates **`_pendingConstruction`**, never a world query ⇒ a restored `Constructing` entity that is not in the dictionary is promoted by **nothing**, whatever the participant count |
| ⚠ **CORRECTION to §2.1f's flagged asymmetry** | `BeginDestruction:250` uses `_globalParticipants` only *(its own comment: "Default to global only for now")* while construction unions the blueprint set. ⛔ **That asymmetry is MOOT today** — `_blueprintRequirements` is never populated, so both compute the same set. ⭐ It is **latent**, and would bite the first time someone calls `RegisterRequirement` |

### 2.2 IG Nodes During Replay

IG nodes use `ListenerRecordReplayController` which is a no-op: they do not record and do not replay. During a replay session on Brain/Muscle nodes, the IG nodes continue to receive ECS state from the network (via their normal DDS ingress) exactly as they would in a live session. The Brain/Muscle nodes restore historical state from disk and their Export-phase systems (CycloneEgressSystem, etc.) broadcast it over DDS. IGs receive and render it normally. No changes are needed on the IG side.

### 2.3 Plan A Recording (Record Everything)

Every Brain/Muscle node records its full ECS state — both owned components and unowned ghost components. No per-component ownership filter is applied during recording. This ensures that seeking and rewinding work correctly because the full world state is available at any frame on every node.

IG/ExCon nodes use `ListenerRecordReplayController` (a no-op): they are not affected by recording or replay transitions.

### 2.4 Input Group Systems (What Gets Disabled)

Systems that go into `TogglableInputGroup` on the CGF (Brain) node:
- `MissionControlExecutionSystem` (currently in inputGroup in CgfLogicPack two-group overload)
- `BehaviorIngressSystem` (from `MissionControlModule`, currently in inputGroup)

Systems that go into `TogglableInputGroup` on the SimHost (Muscle) node:
- `FireProcessingSystem`, `RaycastSolverSystem`, `HitResolutionSystem` (from `CombatModule`, currently in inputGroup)
- `PersonalRouteAuthoringSystem` (navigation, currently in inputGroup)
- Physics query systems (`TerrainQuerySystem`, etc.)
- Any DDS ingress systems registered at `SystemPhase.Input` from the composition root

### 2.5 PostSimulation Group Systems (What Gets Disabled)

Systems that go into `TogglablePostSimulationGroup` on the SimHost (Muscle) node:
- `BallisticsSystem` — integrates ballistic trajectories into SimTransform positions
- `LinearKinematicsSystem` — integrates velocity into SimTransform position for linear movers
- `CarKinematicsSystem` — integrates vehicle speed into SimTransform position

**Why these must be disabled:** When `PlaybackTickSystem` restores a historical frame, it blits raw ECS chunk data directly into the `NativeChunkTable`, which restores all `SimTransform` components to their recorded historical values. If any kinematic integration system then runs in the same frame (after `PlaybackTickSystem` fires in PostSimulation), it reads the newly restored velocity/speed components and advances the position forward again — corrupting the replay with a double-integration artifact.

Systems that remain running freely in PostSimulation during replay:
- `PlaybackTickSystem` (from `ReplayModule`) — this is what drives the replay; it must always run
- Terrain/coordinate query resolution systems that are read-only
- Any system that only reads ECS state (does not mutate SimTransform or other physics components)

Systems NOT registered during replay (naturally absent):
- `RecorderTickSystem` — belongs to `RecordingModule` which is not installed during replay

Systems that stay running in Export phase (must NOT be disabled):
- `CycloneEgressSystem`, `SmartEgressSystem`, `OwnershipEgressSystem` — broadcast historical state to IG nodes

---

## 3. Modern Architecture: Target State

### 3.1 Class Deletions (Forces Solution-Wide Migration)

**From `Fdp.Core`** (deleted files trigger compile errors across the solution):
- `ComponentSystem.cs` — abstract base class
- `SystemGroup.cs` — group + topological sort
- `StandardSystemGroups.cs` — five concrete groups (`InputSystemGroup`, `SimulationSystemGroup`, `PostSimulationSystemGroup`, `PresentationSystemGroup`, `ExportSystemGroup`)

**From `Hrot.Common.Infrastructure`** (deleted files trigger compile errors in Hrot subsystems):
- `CgfInputGroupAdapter.cs` — bridges SystemGroup to IEcsModuleSystem at Input phase
- `LegacySystemGroupAdapters.cs` — contains `LegacySystemGroupAdapterBase`, `SimulationGroupModule`, `PostSimulationGroupAdapter`

### 3.2 New Composition Wrappers (Togglable Groups)

Three new classes go in `Fdp.ModuleHost.Scheduling`. Unlike `NetworkLifecycleSystemGroup` (which is a plain class with an `ExecuteGroup` method), these three implement **`ISystemGroup`** (from `Fdp.ModuleHost.Abstractions`). `ISystemGroup` extends `IEcsModuleSystem` and adds `Name` and `GetSystems()`. The `SystemScheduler.ExecuteSystem` method checks `if (system is ISystemGroup group)` and, when true, calls `ExecuteGroup` on the group which profiles each inner system individually in the diagnostic UI. Without `ISystemGroup`, the group appears as a single black-box entry in the profiler.

**`TogglableInputGroup`** — registered in `SystemPhase.Input`:
```csharp
// Fdp.ModuleHost.Scheduling.TogglableInputGroup
[UpdateInPhase(SystemPhase.Input)]
public sealed class TogglableInputGroup : ISystemGroup
{
    private readonly IEcsModuleSystem[] _innerSystems;
    public bool Enabled { get; set; } = true;
    public string Name { get; }

    public TogglableInputGroup(string name, params IEcsModuleSystem[] innerSystems)
    {
        Name = name;
        _innerSystems = innerSystems;
    }

    public IReadOnlyList<IEcsModuleSystem> GetSystems() => _innerSystems;

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (!Enabled) return;
        foreach (var sys in _innerSystems)
            sys.Execute(view, deltaTime);
    }
}
```

**`TogglableSimulationGroup`** — registered in `SystemPhase.Simulation`:
```csharp
// Fdp.ModuleHost.Scheduling.TogglableSimulationGroup
[UpdateInPhase(SystemPhase.Simulation)]
public sealed class TogglableSimulationGroup : ISystemGroup
{
    private readonly IEcsModuleSystem[] _innerSystems;
    public bool Enabled { get; set; } = true;
    public string Name { get; }

    public TogglableSimulationGroup(string name, params IEcsModuleSystem[] innerSystems)
    {
        Name = name;
        _innerSystems = innerSystems;
    }

    public IReadOnlyList<IEcsModuleSystem> GetSystems() => _innerSystems;

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (!Enabled) return;
        foreach (var sys in _innerSystems)
            sys.Execute(view, deltaTime);
    }
}
```

**`TogglablePostSimulationGroup`** — registered in `SystemPhase.PostSimulation`:
```csharp
// Fdp.ModuleHost.Scheduling.TogglablePostSimulationGroup
[UpdateInPhase(SystemPhase.PostSimulation)]
public sealed class TogglablePostSimulationGroup : ISystemGroup
{
    private readonly IEcsModuleSystem[] _innerSystems;
    public bool Enabled { get; set; } = true;
    public string Name { get; }

    public TogglablePostSimulationGroup(string name, params IEcsModuleSystem[] innerSystems)
    {
        Name = name;
        _innerSystems = innerSystems;
    }

    public IReadOnlyList<IEcsModuleSystem> GetSystems() => _innerSystems;

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (!Enabled) return;
        foreach (var sys in _innerSystems)
            sys.Execute(view, deltaTime);
    }
}
```

The replay handler acquires references to all three wrappers (plus `NetworkLifecycleSystemGroup`) and flips their `Enabled` flag during `PrepareReplay`/`FinalizeReplay`/`PrepareLive` commits.

**Note on `NetworkLifecycleSystemGroup`:** This existing class uses `ExecuteGroup` not `Execute` and is NOT an `IEcsModuleSystem`. It is called directly by the replication module's orchestration code. The three new togglable groups above are proper `IEcsModuleSystem` implementations registered with the kernel scheduler. Do not attempt to retrofit `NetworkLifecycleSystemGroup` into `ISystemGroup` — that is a separate workstream if needed.

### 3.3 System Migration: ComponentSystem to IEcsModuleSystem

Every system that currently extends `ComponentSystem` must be changed to implement `IEcsModuleSystem`. The mechanical change is:

**Before:**
```csharp
public class CarKinematicsSystem : ComponentSystem
{
    protected override void OnUpdate()
    {
        World.Query().With<SimTransform>().Each(...);
    }
}
```

**After:**
```csharp
[UpdateInPhase(SystemPhase.PostSimulation)]
public class CarKinematicsSystem : IEcsModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime)
    {
        view.Query().With<SimTransform>().Each(...);
    }
}
```

Key rules for the conversion:
- Replace `protected override void OnUpdate()` with `public void Execute(ISimulationView view, float deltaTime)`.
- Replace `World.Query(...)` with `view.Query(...)`.
- Replace `DeltaTime` with `deltaTime` parameter.
- Add `[UpdateInPhase(SystemPhase.X)]` matching the group the system was previously placed in.
- Keep `[UpdateBefore]` / `[UpdateAfter]` attributes unchanged for intra-group ordering.
- Systems that require immediate structural mutation (not deferred through command buffer) must use the EntityRepository downcast and **throw** on failure:

```csharp
public void Execute(ISimulationView view, float deltaTime)
{
    if (view is not EntityRepository repo)
        throw new InvalidOperationException(
            $"{GetType().Name} requires direct EntityRepository access and cannot run " +
            $"on a read-only snapshot view ({view.GetType().Name}). " +
            "Do not schedule this system on a background thread.");
    // direct mutation here
}
```

Throwing instead of silently returning is intentional. If a developer accidentally configures a direct-mutation system to run on a background thread against a read-only snapshot, the `ModuleHostKernel`'s circuit breaker catches the exception, immediately flags the module as failed, and surfaces the exact configuration error in the logs and the `ArchitectureDiagnosticsWindow`. A silent return would hide the misconfiguration permanently. `WrongPhaseException` (from `Fdp.Core.Phase`) is an alternative if the call site is always triggered by a phase mismatch, but `InvalidOperationException` is preferred here because the failure mode is a scheduling configuration error, not a phase protocol violation.

### 3.4 Composition Root Changes

**`SimHostCoreLogicPack`**:
- The overload `RegisterSystems(SystemGroup inputGroup, SystemGroup simGroup, SystemGroup postSimGroup)` is deleted.
- `RegisterSystems(ISystemRegistry registry)` is **NOT** used for the systems that need to be wrapped in togglable groups, because registering the same system instance into both the registry directly AND into a `TogglableSimulationGroup` that is also registered would cause a double-registration exception in `SystemScheduler`.
- Instead, `SimHostCoreLogicPack` exposes three read-only array properties:
  - `InputSystems` — returns the instantiated `IEcsModuleSystem[]` for `[UpdateInPhase(Input)]` systems
  - `SimulationSystems` — returns the instantiated `IEcsModuleSystem[]` for `[UpdateInPhase(Simulation)]` systems
  - `PostSimulationSystems` — returns the instantiated `IEcsModuleSystem[]` for `[UpdateInPhase(PostSimulation)]` systems
- `SimHostApp` reads these arrays and packs them into `TogglableInputGroup`, `TogglableSimulationGroup`, and `TogglablePostSimulationGroup` respectively, then registers the three wrapper groups with the kernel.
- `SimHostCoreLogicPack` continues to expose `RegisterSystems(ISystemRegistry registry)` for **non-toggled** systems (e.g., perception or diagnostic systems that should always run) but the three phase-specific arrays are the primary interface for composition.

**`CgfLogicPack`**:
- Adopts the exact same pattern as `SimHostCoreLogicPack` to avoid the double-registration trap. If `CgfLogicPack` called `registry.RegisterSystem()` directly AND `CgfSubsystem` also registered those same instances inside a `TogglableInputGroup`/`TogglableSimulationGroup`, the `SystemScheduler` would throw a duplicate-registration exception.
- Both overloads `RegisterSystems(SystemGroup simGroup)` and `RegisterSystems(SystemGroup inputGroup, SystemGroup simGroup)` are deleted.
- `RegisterSystems(ISystemRegistry registry)` is **not** used for the game-logic systems that need toggling.
- Instead, `CgfLogicPack` exposes two read-only array properties:
  - `InputSystems` — returns the instantiated `IReadOnlyList<IEcsModuleSystem>` for `[UpdateInPhase(Input)]` systems
  - `SimulationSystems` — returns the instantiated `IReadOnlyList<IEcsModuleSystem>` for `[UpdateInPhase(Simulation)]` systems
- `CgfSubsystem` reads these arrays and packs them into `TogglableInputGroup` and `TogglableSimulationGroup` respectively, then registers the wrappers with the kernel.
- `EditorSubsystem` reads the same arrays and calls `registry.RegisterSystem()` directly for each system (no toggling needed in the editor).

**Sub-modules** (`CombatModule`, `GroundKinematicsModule`, `MissionControlModule`, `CognitiveRuntimeModule`, `ActionDispatchModule`, `DamageAssessmentModule`):
- `RegisterSystems(SystemGroup group)` overloads are deleted.
- `RegisterSystems(ISystemRegistry registry)` becomes the real implementation using `registry.RegisterSystem(new XxxSystem(...))`.

### 3.5 ReferenceReplayLoadHandler Updates

The handler currently stores `SimulationSystemGroup? _simGroup` (legacy type). This changes to `TogglableSimulationGroup? _simGroup`. A new parameter `TogglableInputGroup? _inputGroup` is added. The `SetSystemsEnabled` helper toggles all three groups (`_inputGroup`, `_simGroup`, `_lifecycleGroup`) plus the bypass toggle.

```csharp
private void SetSystemsEnabled(bool enabled)
{
    if (_inputGroup     != null) _inputGroup.Enabled     = enabled;
    if (_simGroup       != null) _simGroup.Enabled       = enabled;
    if (_lifecycleGroup != null) _lifecycleGroup.Enabled = enabled;
}
```

### 3.6 NodeBootstrapper.BuildOrchestration Updates

The parameter `Fdp.Core.SimulationSystemGroup? simGroup` changes to `Fdp.ModuleHost.Scheduling.TogglableSimulationGroup? simGroup`. A new parameter `Fdp.ModuleHost.Scheduling.TogglableInputGroup? inputGroup = null` is added. The guard condition for registering `ReferenceReplayLoadHandler` changes accordingly:

```csharp
if (controller != null && (inputGroup != null || simGroup != null || lifecycleGroup != null))
{
    clusterSlave.RegisterHandler(new ReferenceReplayLoadHandler(
        controller, inputGroup, simGroup, lifecycleGroup, bypassToggle, localTempRoot));
}
```

### 3.7 SimHostApp Wiring

After the migration, `SimHostApp.OnLoad` will:
1. Instantiate all input-phase systems (from `_simCorePack`) as `IEcsModuleSystem` instances.
2. Pack them into `new TogglableInputGroup(inputSystems)`.
3. Instantiate all simulation-phase systems (from `_simCorePack`) as `IEcsModuleSystem` instances.
4. Pack them into `new TogglableSimulationGroup(simSystems)`.
5. Register these wrappers via `_kernel.RegisterGlobalSystem(inputGroup)` and `_kernel.RegisterGlobalSystem(simGroup)`.
6. Pass both to `NodeBootstrapper.BuildOrchestration`.
7. Delete the `_kernelGroup` field entirely — no more separate `SystemGroup` + `Run()`.

### 3.8 CgfSubsystem Wiring

After the migration, `CgfSubsystem.Initialize` will:
1. No longer create `_inputGroup` and `_simGroup` as `SystemGroup` objects.
2. Read `cgfLogicPack.InputSystems` and `cgfLogicPack.SimulationSystems` array properties; pack them into `new TogglableInputGroup(inputSystems)` and `new TogglableSimulationGroup(simSystems)` respectively.
3. No longer call `_context.Kernel.RegisterGlobalSystem(new CgfInputGroupAdapter(_inputGroup))`.
4. No longer call `_context.Kernel.RegisterModule(new CgfSimGroupModule(_simGroup))`.
5. Register the new togglable wrappers directly with the kernel.
6. Pass them to `ReferenceReplayLoadHandler` (fixing the `simGroup: null` bug).

### 3.9 EditorSubsystem Wiring

The Editor does not participate in replay (it uses `MasterSyncController`), but it also currently uses legacy adapters (`CgfInputGroupAdapter`, `SimulationGroupModule`, `PostSimulationGroupAdapter`). After the adapters are deleted, `EditorSubsystem` must register its systems directly via `ISystemRegistry` without any togglable group indirection (since replay toggling is not needed in the editor).

**EditorSubsystem directly instantiates both `CgfLogicPack` and `SimHostCoreLogicPack`**, so it must call their new APIs. Instead of creating dummy legacy `SystemGroup` objects, `EditorSubsystem.Initialize` extracts the `InputSystems`, `SimulationSystems`, and `PostSimulationSystems` properties from both packs and loops through them, calling `registry.RegisterSystem()` for each instance. Do **not** call `RegisterSystems(kernelRegistry)` on the packs for the toggled systems — those packs no longer implement that method for game-logic systems (only `SimHostCoreLogicPack` retains it for always-on non-toggled systems):

```csharp
// Inside EditorSubsystem.Initialize (after the migration):
// CgfLogicPack: register arrays directly (no toggling needed in the editor)
foreach (var sys in _cgfLogicPack.InputSystems)      kernelRegistry.RegisterSystem(sys);
foreach (var sys in _cgfLogicPack.SimulationSystems) kernelRegistry.RegisterSystem(sys);
// SimHostCoreLogicPack: same pattern for all three phases
foreach (var sys in _simCorePack.InputSystems)          kernelRegistry.RegisterSystem(sys);
foreach (var sys in _simCorePack.SimulationSystems)     kernelRegistry.RegisterSystem(sys);
foreach (var sys in _simCorePack.PostSimulationSystems) kernelRegistry.RegisterSystem(sys);
_simCorePack.RegisterSystems(kernelRegistry); // always-on systems (perception, diagnostics)
```

**`EditorSystemsModule`** — the module that currently executes `EditorCargoSystem` and other editor-specific systems by calling `_editorGroup.Run()` — must also be refactored. After the migration it registers its systems via `registry.RegisterSystem()` instead of holding a legacy `SystemGroup` and calling `Run()` on it.

`EditorHarness` (integration test harness) uses local `SimGroupModule` and `PostSimGroupModule` nested classes that wrap `SystemGroup`. These are also removed and replaced with direct system registration.

---

### 3.10 Deep Architectural Risks and Required Fixes

The following issues are not part of the module system migration per se, but they will break the replay experience if not addressed. They are captured here because they interact directly with the same subsystems being changed.

#### 3.10.1 SmartEgressSystem 10-Second Lag on Timeline Seek

`SmartEgressSystem` relies on systems calling `SmartEgressUtil.MarkDirty()` to instantly publish low-frequency declarative data (`EntityMission`, `WeaponState`, `EntityInfo`, etc.) when that data changes. During normal replay (frame-by-frame advance), simulation systems are disabled and `MarkDirty` is never called. `SmartEgressSystem` falls back to its 600-tick rolling heartbeat, meaning the IG receives a full state sync every 10 seconds. This is tolerable for sequential playback.

However, when an operator performs a timeline seek (`SeekToFrame`), the `PlaybackSystem` blits raw ECS chunk data directly into the `NativeChunkTable`. This bulk memory copy bypasses component setters and never triggers `MarkDirty`. The IG node displays stale pre-seek data for up to 10 seconds after the seek.

**Fix:** `PlaybackTickSystem` (or the replay orchestrator hook) must force-flag all active entities as dirty in `EgressPublicationState` immediately after executing a `SeekToFrame`. This triggers `SmartEgressSystem` to broadcast a full cluster state sync on the very next Export frame, snapping the IG to the new timeline position.

#### 3.10.2 GlobalTime Singleton Tug-of-War

In `ModuleHostKernel.Update()`, the live `TimeController` writes the current `GlobalTime` singleton into the ECS world every frame. During replay, `PlaybackSystem` runs in the `PostSimulation` phase and restores all recorded singletons from disk — including the historical `GlobalTime`.

This creates a per-frame overwrite cycle:
1. Kernel writes live `GlobalTime` (before Input phase)
2. Input and Simulation systems run with live time
3. `PlaybackSystem` runs in PostSimulation and overwrites `GlobalTime` with historical time
4. Export systems run with historical time

Time-dependent logic (velocity extrapolation, effect duration, TTL expiry) behaves inconsistently depending on which phase accesses the singleton.

**Fix:** When `ReplayModule` is active and `PrepareReplay` is committed, the `TimeController` must be instructed to stop writing live time into the world for the duration of the replay. `PlaybackSystem` then has full ownership of `GlobalTime`. When `FinalizeReplay` or `PrepareLive` is committed, the `TimeController` resumes.

#### 3.10.3 GhostDestructionSystem and DeferredTakeoverSystem Outside NetworkLifecycleSystemGroup

`NetworkLifecycleSystemGroup` is disabled during replay. However, two systems registered by `NedReplicationModule` are outside that group:

- `GhostDestructionSystem` (`[UpdateInPhase(SystemPhase.PostSimulation)]`) — registered outside the group. During replay, a stray or delayed network `DISPOSE` packet (from a late DDS delivery) would cause this system to delete a historical entity from the replaying world, creating an unfillable gap in playback.
- `DeferredTakeoverSystem` (`[UpdateInPhase(SystemPhase.BeforeSync)]`) — registered outside the group. During replay it could illegally mutate the `AuthorityMask` of historical entities based on network grant packets, corrupting the authority state.

**Fix:** Both systems must be moved inside `NetworkLifecycleSystemGroup` so they are automatically disabled when replay begins. This requires adding a `GhostDestructionSystem` slot and a `DeferredTakeoverSystem` slot to `NetworkLifecycleSystemGroup`'s constructor and its internal system array. No new wrapper class is needed and no new field is needed in `ReferenceReplayLoadHandler` — `NetworkLifecycleSystemGroup` is already wired to the handler.

#### 3.10.4 CycloneNetworkCleanupSystem Scrub Flood on Seek

`CycloneNetworkCleanupSystem` (`[UpdateInPhase(SystemPhase.Export)]`) maintains a `_trackedEntities` dictionary to detect when owned entities are destroyed and send DDS `DISPOSE` signals. This system runs during replay (Export phase is not disabled).

When an operator performs a timeline seek, the entire world state is annihilated and replaced by `PlaybackSystem`. `CycloneNetworkCleanupSystem` sees all previously-tracked entities as `IsAlive == false` and immediately sends `DISPOSE` for each of them — a mass-destruction DDS broadcast. This simultaneously tears down all IG ghost entities. The IG then immediately reconstructs them as the egress systems send fresh baseline publications. This spike causes severe DDS congestion and visual pop-in.

**Fix:** The `ReferenceReplayLoadHandler` (or `PlaybackTickSystem`) must expose a `SeekCompleted` callback or event. `CycloneNetworkCleanupSystem` registers with this callback and clears `_trackedEntities` when a seek completes, so it does not misinterpret the post-seek world state as mass destruction.

---

## 4. Migration Phases

### Phase 1 — Togglable Group Foundation

**Scope:** Pure additions. Nothing deleted. Existing code still compiles and runs.

1. Create `TogglableInputGroup` in `Fdp.ModuleHost.Scheduling`.
2. Create `TogglableSimulationGroup` in `Fdp.ModuleHost.Scheduling`.
3. Update `ReferenceReplayLoadHandler` to accept `TogglableInputGroup?` and `TogglableSimulationGroup?` (replacing `SimulationSystemGroup?`). Keep backward-compatible overloads during transition if needed.
4. Update `NodeBootstrapper.BuildOrchestration` signature to use new types.
5. Update existing tests that construct `ReferenceReplayLoadHandler` or call `BuildOrchestration` with the old types.

**Deliverable:** Both new classes exist and the replay handler can toggle them. Nothing else changes.

### Phase 2 — System Migration (Input/Simulation Systems)

**Scope:** Convert all `ComponentSystem`-based game systems to `IEcsModuleSystem`. This is the largest phase.

For each sub-module below, the change is:
- System classes: `ComponentSystem` → `IEcsModuleSystem`, `OnUpdate()` → `Execute(view, dt)`.
- Module's `RegisterSystems(ISystemRegistry)` becomes the real implementation; legacy `RegisterSystems(SystemGroup)` overloads are deleted.
- `[UpdateInPhase]` added to each system class.

**Sub-modules to convert (roughly ordered by complexity):**

| Module | Project | Key Systems |
|--------|---------|-------------|
| `CombatModule` | `Hrot.SimHost` | FireProcessingSystem (Input), RaycastSolverSystem (Input), HitResolutionSystem (Input), BallisticsSystem (PostSim) |
| `GroundKinematicsModule` | `Hrot.SimHost` | SpatialHashSystem (Sim), CarKinematicsSystem (PostSim), FormationTargetSystem (Sim), VehicleCommandSystem (Sim), NavigationExecutionSystem (Sim), LinearKinematicsSystem (PostSim) |
| `DamageAssessmentModule` | `Hrot.SimHost` or toolkit | Systems delivering authoritative damage |
| `MissionControlModule` | CGF toolkit | BehaviorIngressSystem (Input), MissionDirectorSystem (Sim) |
| `CognitiveRuntimeModule` | CGF toolkit | BTreeTickSystem (Sim), HsmTickSystem (Sim), ChannelArbitrationSystem (Sim), HsmDamageBridgeSystem (Sim) |
| `ActionDispatchModule` | CGF toolkit | LocomotionDispatcherSystem (Sim), WeaponDispatcherSystem (Sim) |
| Standalone CGF systems | `Hrot.CGF` | MissionControlExecutionSystem (Input), MissionAdapterSystem (Sim), HealthApplicationSystem (Sim), CgfThreatEvaluationSystem (Sim), RouteContextSystem (Sim) |
| Navigation bridges | Various | PersonalRouteAuthoringSystem (Input), NavigationIntentBridgeSystem (Sim), RouteTrajectorySyncSystem (Sim) |
| `GenesisMaterializationSystem` | `Hrot.SimHost` | Direct mutation, uses EntityRepository downcast |

**At the end of Phase 2:** All game systems implement `IEcsModuleSystem`. No system extends `ComponentSystem`. However, `ComponentSystem` and `SystemGroup` still exist — they just have no more subclasses in game code.

### Phase 3 — Application Wiring

**Scope:** Update composition roots to use new system types and togglable groups.

1. `SimHostCoreLogicPack`: activate `RegisterSystems(ISystemRegistry)`, delete legacy overloads.
2. `CgfLogicPack`: expose `InputSystems` and `SimulationSystems` array properties (same pattern as `SimHostCoreLogicPack`), delete legacy overloads.
3. `SimHostApp`: remove `_kernelGroup`, create and register `TogglableInputGroup` and `TogglableSimulationGroup`, wire to replay handler.
4. `CgfSubsystem`: remove legacy SystemGroup fields, wire togglable groups, fix `simGroup: null` bug.
5. `CgfApplication`: same as CgfSubsystem.
6. `EditorSubsystem`: remove adapter usage, register systems directly via `ISystemRegistry`.
7. `EditorHarness` and `SimHostInstance` test harnesses: remove SystemGroup usage.

**At the end of Phase 3:** The application wiring is clean. Replay isolation is correctly implemented. The legacy classes are still present but no longer used in production code.

### Phase 4 — Legacy Removal

**Scope:** Delete the legacy classes. This intentionally breaks any remaining references.

1. Delete `ComponentSystem.cs` from `Fdp.Core`.
2. Delete `SystemGroup.cs` from `Fdp.Core`.
3. Delete `StandardSystemGroups.cs` from `Fdp.Core`.
4. Delete `CgfInputGroupAdapter.cs` from `Hrot.Common.Infrastructure`.
5. Delete `LegacySystemGroupAdapters.cs` from `Hrot.Common.Infrastructure`.
6. Fix all remaining compile errors (tests that directly instantiate legacy types).
7. Confirm solution builds cleanly.

---

## 5. Key Invariants

### 5.1 Execution Order Preservation

The current execution order (`Input → Simulation → PostSimulation → Export`) is the responsibility of `[UpdateInPhase]` attributes. The modern `SystemScheduler` inside `ModuleHostKernel` reads these attributes and builds the correct topological execution graph. Legacy `[UpdateBefore]` / `[UpdateAfter]` attributes remain valid for intra-phase ordering.

### 5.2 Direct Mutation Systems

Systems that call `EntityRepository` methods directly (create entity, add/remove component) must use the downcast:
```csharp
if (view is not EntityRepository repo) return;
```
This is NOT a performance concern for synchronous modules (which are the majority). The downcast is a safety gate that returns a no-op if the system is ever accidentally configured to run on a background snapshot. These systems cannot be moved to background execution without redesign, which is acceptable.

### 5.3 The SimHostApp `_kernelGroup` Bug

The current `_kernelGroup` in `SimHostApp` is a plain `SystemGroup` that contains ALL simulation systems regardless of phase (input, sim, postSim all packed in together). After the migration, all systems declare their own phase via `[UpdateInPhase]` and are registered with the kernel directly (or via togglable wrappers). The `_kernelGroup` field and the `_kernelGroup.Run()` call in `OnUpdate` are both removed entirely. The kernel's `Update()` handles all execution.

### 5.4 The Empty SimulationSystemGroup Bug

The empty `SimulationSystemGroup` instantiated in `SimHostApp.OnLoad` (line near `BuildOrchestration`) serves no purpose and is removed. After Phase 3, a properly-populated `TogglableSimulationGroup` takes its place and is correctly wired to the replay handler.

### 5.5 The CGF `simGroup: null` Bug

The `ReferenceReplayLoadHandler` in `CgfSubsystem` passes `simGroup: null, lifecycleGroup: null`. This is fixed in Phase 3 when real `TogglableSimulationGroup` and `TogglableInputGroup` references are wired from the composition root.

### 5.6 Egress Keeps Running During Replay

`CycloneEgressSystem`, `SmartEgressSystem`, and `OwnershipEgressSystem` are all `[UpdateInPhase(SystemPhase.Export)]` and are registered by the replication module. They are NOT inside any togglable group. The kernel continues to run the Export phase during replay, broadcasting the restored historical ECS state over DDS. IG nodes receive this data and render it normally. This is the intended behavior.

### 5.7 PostSimulation Systems During Replay

`PlaybackTickSystem` (from `ReplayModule`) is `[UpdateInPhase(SystemPhase.PostSimulation)]`. It runs freely during replay because it is what drives the frame-by-frame restore of ECS state. It is NOT placed inside `TogglablePostSimulationGroup`.

`RecorderTickSystem` (from `RecordingModule`) does NOT run during replay. `RecordingModule` is installed only when an exercise is active and live recording is enabled. `ReplayModule` is mutually exclusive with `RecordingModule` — when the orchestrator transitions a node to replay mode, it uninstalls `RecordingModule` before installing `ReplayModule`. Therefore `RecorderTickSystem` is never registered in the kernel during replay.

`BallisticsSystem`, `LinearKinematicsSystem`, and `CarKinematicsSystem` are placed inside `TogglablePostSimulationGroup` and are disabled during replay. See Section 2.5 for details.

---

## 6. Dependency Graph

```
Fdp.Core
  (ComponentSystem, SystemGroup, StandardSystemGroups)
  |
  v
Fdp.ModuleHost.Abstractions
  (IEcsModuleSystem, ISystemRegistry, SystemPhase, UpdateInPhase)
  |
  v
Fdp.ModuleHost
  (ModuleHostKernel, SystemScheduler)
Fdp.ModuleHost.Scheduling
  (NetworkLifecycleSystemGroup, TogglableInputGroup*, TogglableSimulationGroup*)
  |
  v
Fdp.Toolkits
  (CombatModule, GroundKinematicsModule, etc.)
  |
  v
Hrot.Common
  (LegacySystemGroupAdapters, CgfInputGroupAdapter)  <-- DELETED in Phase 4
  |
  v
Hrot.CGF / Hrot.SimHost / Hrot.Editor
  (composition roots, application layer)
```

Items marked `*` are new. Items marked DELETED are removed in Phase 4.

---

## 7. Out of Scope

The following items are explicitly out of scope for this workstream:

- Changing the `AutonomousPerceptionModule` or any toolkit that already uses `IEcsModuleSystem` natively. These are already in the target state.
- Changing the recording format or playback mechanics.
- Adding DDS ingress toggling to the replication module (this would be a separate workstream if needed; current Plan A recording makes it unnecessary since the replaying node does not receive conflicting live data on the same ECS state).
- Moving any synchronous system to background execution (this is a separate performance workstream).
- The IG subsystem (already uses `ListenerRecordReplayController`; no changes needed).
- The ExCon subsystem (uses a similar listener pattern; no changes needed).
