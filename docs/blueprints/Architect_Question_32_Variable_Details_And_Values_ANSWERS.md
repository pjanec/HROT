<!--STATUS
state: LIVE
updated: 2026-08-15
current-answer: section 0 is the ruling spec; section 4 is the MASTER SEQUENCING TABLE (56-61)
note: carries the standing NO VISUAL CHECKS suspension until the Details panel and access infrastructure are unified
-->
# Architect Question #32 — **ANSWERS**: the variable Details panel

> ⭐⭐ **User ruling, `2026-08-14`, verbatim in substance.** ⛔ **This supersedes every lean in the
> question document, including the two the coordinator argued for.**
>
> ⛔⛔ **AND A SEQUENCING RULING: NO VISUAL CHECKS** until the Details panel is implemented **and** the
> emitters and all access infrastructure are unified.
> ⇒ 📌 **[VISUAL_CHECK_Guide.md](VISUAL_CHECK_Guide.md) is SUSPENDED**, not cancelled. `BP-243` — the
> one defect its first run found — stands as the argument for running it again later, on a surface
> that is finished.

---

## 0. The ruling, as a spec

| # | ruled | was |
|---|---|---|
| **1** | **Details hosts the list of vars**, as designed | `U-6`, unchanged |
| **2** | ⭐ **Selection routes:** click a **global** in My Blueprint ⇒ the list of **globals / working state**. Click a **local** ⇒ the locals of the **currently selected graph** | new, and it is the panel's whole navigation model |
| **3** | ⭐⭐ **ONE Value column, meaning switched by run state** — **initial** when not running, **current** when running or paused, across **live / replay / preview** | ⛔ **`Q32-A1`. The coordinator argued `A2` (two columns) and is overruled** |
| **4** | **Value is READ-ONLY in the cell.** Tooltip shows it **full size and pretty-printed** (structs) | new |
| **5** | ⭐ **A three-dot button** right of the value opens a **StructEdit-based editing window**, **OK / Cancel**, initialised to the variable's current value | `Q32-B3`, promoted from *"vectors only"* to **everything** |
| **6** | ⭐⭐ **The same Details panel is REUSED for every asset type** — HSM, BTree, Blueprint | ⇒ **this is a cross-host deliverable, not a blueprint one** |
| **7** | ⭐ **Write target follows run state:** running ⇒ writes the **live blackboard**; not running ⇒ writes the **initial value in JSON** | new |
| **8** | ⭐⭐ **Unify the emitters** — *"as the global vars and working state vars are the same stuff, it makes no sense to emit them differently"* | ⇒ **`Q32-E`, decided: unify** |
| **9** | ⛔⛔ **"I need a clean solution, no keeping two implementations for the same concept"** | ⇒ **the standing constraint over all of it.** `U-16` is not optional cleanup; it is the acceptance criterion |

---

## 1. ⭐⭐ What this costs — measured, and it is mostly reuse

| piece | state |
|---|---|
| the table + columns | ✅ **exists** — `VariablesPanelControl`, already `Name · Type · Bytes · Value · Role · Scope` |
| initial-value **storage** | ✅ **exists** — `DefaultValueJson`, and ⭐ **already honoured for BOTH kinds** (`AiPrimitiveEmitter:133` / `InstanceEmitter:183`) |
| initial-value **setter** | ✅ `IBlackboardManagedAsset.UpdateVariableDefaultValueJson` — **HSM and BTree implement it**; ⛔ **blueprint does not** |
| live-value **read** | ✅ `ILiveValueProvider.GetLiveVariableValues` drives the column; `MarshalFromBytes` formats. ⛔ **blueprints never supply it** |
| StructEdit editor | ✅ **exists** — `IEditSession` / `EditDocument` / `IComponentEditService`, reflection-driven |
| 🟠 **live-value WRITE** | ⭐ **The PRIMITIVE exists — `IEntityCommandBuffer.SetComponentRaw`** *(and `AddEmptyComponent` documents blackboards as components)*. ⛔ **What is missing is the editor-side path to it:** `IBlueprintDebugSession` has no write at all. ⇒ **wiring, not invention** |

---

## 2. ✅ The live write — **all three sub-questions RULED** *(user, `2026-08-14`)*

**`IBlueprintDebugSession` has no write.** Its surface is Attach/Detach · breakpoints · **watches** ·
entity filter · Continue/StepOver/StepInto. ⚠ **`Watch.WriteValue<T>` is the RUNTIME writing into the
watch buffer — not the editor writing into the entity.** ⇒ **ruling 7's running half must be built**,
and now every constraint on it is settled:

| | ⭐ **RULED** |
|---|---|
| **2a — when?** | ⭐⭐ **Queue changes via FDP command buffers.** ✅ **The seam exists:** `IEntityCommandBuffer` / `EntityCommandBuffer` in `Fdp.Core`, and the write primitive is **`SetComponentRaw(entity, typeId, ptr, size)`**. ⚠ **Playback is `ecb.Playback(world)`** — ⛔ **NOT `EntityRepository.FlushCommandBuffers()`, which has no callers; see §2.1.** ⇒ ⛔ **no bespoke queue, no mid-tick write** |
| **2b — replay?** | ⭐⭐ **NO changing value during replay.** ⇒ **show it, refuse the write.** ✅ **Coordinator's lean confirmed** — a write during replay diverges the run from the recording it is replaying |
| **2c — cluster?** | ⭐⭐ **The concern does not exist. The brain and blackboard live on a SINGLE CGF node (and the editor), and are NEVER replicated in distributed mode.** ⇒ ⛔ **there is no authoritative-copy problem to solve**, and the coordinator's *"changes locally and nowhere else"* worry was **misinformed about the architecture** |

⇒ ⭐ **Nothing in the ruling is blocked any more.**

## 2.1 ⭐⭐ Rulings 10-12 *(user, `2026-08-15`)* — reuse, share, and **immediacy**

| # | ruled |
|---|---|
| **10** | ⭐ **Reuse the existing StructEdit generic value-editing dialog** — *"used by entity component inspector at least"* |
| **11** | ⭐⭐ **The runtime value change is the same mechanism the Watch panel should provide — SHARE it** |
| **12** | 🔴🔴 **It must work when the sim is FROZEN on a breakpoint or in deterministic stepping mode, and the change must appear IMMEDIATELY in both the Details and Watch panels — ⛔ not on the next step or on resume** |

### ⭐ Ruling 10 — what exists, and ⚠ there appear to be TWO of them

| | |
|---|---|
| ✅ **the FDP-level service** | **`IComponentEditService`** (StructEdit.Core), driven by **`Fdp.Toolkits/Diagnostics/Gizmos/UI/StructInspectorProjector.cs`** — ⭐ **this looks like the entity component inspector the user means**, and it is also what the ReplayBrowser's predicate/event compilers use |
| ⚠ **a blueprint-local one** | **`Hrot.Blueprints.Editor/Inspector/`** — `IStructEditDrawer<T>` · `DrawerRegistry` · `PrimitiveDrawers`, consumed by `InspectorWindow` and `BlueprintDetailsWindow`. ⭐ **Already an EDITING interface** — `bool Draw(string label, ref T value, DrawContext ctx)`, *"returns true if the value was modified"* |
| 📐 **The question, to MEASURE not assume** | ⛔ **Are these two implementations of one concept (ruling 9's target), or two different jobs that look alike?** ⚖️ **Coordinator lean: build the dialog on the FDP-level `IComponentEditService`** — it is the one already shared beyond blueprints, and ruling 6 wants one dialog for three hosts. ⚠ **But the coordinator has NOT proved the blueprint-local registry is redundant, and must not claim it is** |

### ⛔⛔ Ruling 12 — **the "conflict" was the coordinator's error. WITHDRAWN.**

> ⚠ **The first draft of this section claimed:** *"a frozen sim runs no ticks, so a queued write would
> only appear on resume; therefore when paused, flush the command buffer on the spot."*
> ⛔⛔ **Both halves were wrong, and the user corrected the premise:** *"breakpoint or step frozen sim
> does not mean nothing is ticking — behaviors should not tick and dt==0 so no physics applies."*

| the claim | the truth |
|---|---|
| ⛔ *"a frozen sim runs no ticks"* | ⭐⭐ **It ticks.** Freezing means **behaviours do not tick** and **`dt == 0`** so nothing integrates. **The host loop, its systems and command-buffer playback keep running** |
| ⛔ *"`FlushCommandBuffers()` is the existing playback point"* | 🔴 **`EntityRepository.View:43` has NO CALLERS AT ALL.** ⭐ **Playback is `ecb.Playback(world)`**, called from the systems and host loop. ⚠ **The coordinator named a dead API as "existing"** — the *"verify the consumer, not just the definition"* rule, broken again |

⇒ ⭐⭐ **There is no conflict, and the design gets SIMPLER:** the write is queued to the command buffer
**always**, and because ticks continue while frozen, it plays back **on the next tick — i.e. within a
frame** ⇒ **ruling 12 is satisfied by the plain path.** ⛔ **The special-case flush is withdrawn: it
would have been a SECOND write path, which is ruling 9's prohibition.**

| | |
|---|---|
| ⭐ **The write primitive already exists** | **`IEntityCommandBuffer.SetComponentRaw(Entity, int typeId, void* ptr, int size)`** — raw bytes into a component. ⭐ **And the interface already knows blackboards are components:** `AddEmptyComponent` is documented as *"bypasses the 1024-byte ECB payload limit for large components like blackboards"* |
| ⭐ **The freeze signal** | `IEngineDebugTimeController.IsPausedByDebugger` — `MasterSyncTimeControllerAdapter:29` maps it to `TimeMode.Deterministic`; `CgfSubsystem:830` to `_bpManager.IsPaused` |
| 📐 **Make it a GATE, not an assumption** | ⛔ **Do not assume "next tick" is fast enough — MEASURE it.** ⭐ **Gate: with the sim frozen on a breakpoint, a value change is visible in BOTH panels within one frame.** ⚠ **If it is not, that is a finding, and the fix is in the loop — not a second write path** |

### ⭐⭐⭐ Ruling 14 — **the command buffer needs a SURGICAL field write** *(user, `2026-08-15`)*

> ⭐⭐ *"the command buffer might need a special 'change concrete variable in a concrete blackboard
> component' because before the command applies another part might have changed other parts of the
> blackboard. it can not be full component overwrite only, but chirurgical change."*

⛔⛔ **Correct, and MEASURED — and the case is stronger than stated.**

| | |
|---|---|
| 🔴 **1 — the lost-update race the user named** | ⛔ **Every ECB write is WHOLE-COMPONENT:** `SetComponent<T>` · `SetComponentRaw(entity, typeId, ptr, size)` · `SetManagedComponentRaw`. ⭐ **`grep offset` over `EntityCommandBuffer.cs` returns NOTHING.** ⇒ queueing a whole-blackboard write means **reading it now and writing it back later**, clobbering every change any system made in between |
| 🔴🔴 **2 — and it would not even FIT** | ⭐⭐ **`EntityCommandBuffer:35` — `private const int MaxComponentSize = 1024; // Sanity check`**, and the interface's own words on `AddEmptyComponent`: *"Bypasses the 1024-byte ECB payload limit **for large components like blackboards**."* ⇒ ⛔ **a whole-component blackboard write cannot go through the ECB at all.** ⭐ **The surgical command is not the safer option — it is the ONLY one that works** |
| ✅ **3 — the read side already does exactly this** | `BlueprintDebugSession:1308-1312` — `int start = 8 + field.OffsetBytes; … bytes.Slice(start, field.SizeBytes)`. ⭐ **Offsets and sizes are already known and already used surgically to READ.** ⇒ **the write is the mirror of code that ships** |

📐 **The shape, for the implementation session to size:**

```
void SetComponentFieldRaw(Entity entity, int typeId, int byteOffset, void* src, int size)
```

| | |
|---|---|
| ⭐ **Offset base** | ⚠ **the read path uses `8 + OffsetBytes`** — there is an **8-byte header** before the fields *(cf. `InitDefaultWorkingState((WorkingState*)(memory + 8))`)*. ⛔ **Whoever computes the offset must own that `+8` in exactly one place, not two** |
| 🔴 **Bounds** | ⛔ **an out-of-range offset/size is MEMORY CORRUPTION, not a wrong value.** ⭐ **Bounds-check against the registered component size and fail LOUDLY** |
| **Composition** | ⭐ **N queued field writes to one component must all land, in order** — that is the property a whole-component write destroys |
| ⚠ **The residual race, stated honestly** | ⛔ **If a system writes the SAME field between the dialog opening and playback, last-writer-wins on that field.** ⭐ **That is inherent and acceptable** — the goal is not to clobber the **other** fields |
| ⚠ **Blast radius** | ⛔ **This is `Fdp.Core` — engine-level, every host, every subsystem.** 📌 **The smallest possible addition: one command, additive, no existing behaviour touched** |

## 2.2 ⭐⭐ Three coordinator answers *(`2026-08-15`)* — and one changes the write design

### Q — *"writing must be backed by EMITTED CODE, nothing easily expressed in a command buffer?"*

⚖️ **Half right — and the half that is wrong matters less than the reason you are right.**

| | |
|---|---|
| ⛔ **Emitted code is NOT needed for the write itself** | ⭐ **The READ path already works byte-level** — `BlueprintDebugSession:1308` slices `8 + field.OffsetBytes` and `MarshalFromBytes`es it. **No generated accessor, no emitted setter.** ⭐ **All 18 offerable types are blittable**, and the layout metadata the write needs is **already emitted and already consumed** |
| ⛔ **Data breakpoints do NOT need to be told** | ⭐ **They are SNAPSHOT-based, not write-notified:** `_preTickSnapshot` is *"filled by `DebugSnapshotProvider` every BeforeSync tick"*, and `Evaluate(bus, repo)` compares. ⇒ **a raw byte write is observed like any other state change.** ⛔ **A write-notification hook is NOT required** |
| 🔴🔴 **BUT YOU ARE RIGHT THAT THE PLAIN ECB PATH FAILS — for a different reason** | see below |

### 🔴🔴 The finding: **while paused, the editor is not looking at the live world**

```csharp
// DataBreakpointManager.cs:123
public ISimulationView ActiveView => _isPaused ? (ISimulationView)_preTickSnapshot : _liveRepo;

// :470-473  — on hitting a breakpoint
_postTickSnapshot.SyncFrom(_liveRepo);     // capture post-execution state
_liveRepo.SyncFrom(_preTickSnapshot);      // ⛔ REWIND the live world to start-of-tick
```

⛔⛔ **Two consequences, and both hit ruling 12 head-on:**

| | |
|---|---|
| **1** | ⭐ **While paused the panels read `_preTickSnapshot`, a DIFFERENT repository.** ⇒ a write queued to the ECB and played into `_liveRepo` **would not appear at all while frozen** — ⛔ **exactly the failure ruling 12 forbids, arriving by a route neither of us predicted** |
| **2** | ⚠ **Pausing REWINDS `_liveRepo` from `_preTickSnapshot`.** ⇒ **ordering matters**: a write applied to the live repo around a pause boundary can be **discarded by the rewind** |

📐 **So the write must target whatever `ActiveView` currently is — or the snapshot and the live world
must both receive it.** ⚖️ **Coordinator lean, offered as a starting point and NOT as a measured
design:** the edit goes through the command buffer **and** is applied to `ActiveView`, with the paused
case writing the snapshot that the panels actually read.
⛔ **This is now the hardest part of the whole feature, and it is a `Hrot.Diagnostics.Breakpoints`
question, not a panel question.** 📌 **It deserves its own design pass before 59c is built.**

⚠⚠ **Coordinator honesty:** this is cited from two code sites, not from running it. ⭐ **It is a strong
signal, not a proven mechanism** — 📐 **the implementation session must confirm what the panels read
while paused before designing around it.**

### Q — *"the Details panel is a chameleon; it must be modular, not a monolith"*

✅ **Agreed, and the pattern already exists — it should be extended, not invented.**
⭐ **`BlueprintDetailsWindow` already dispatches through `DrawerRegistry` / `IStructEditDrawer<T>`**, and
`BP-205` already put the **id scope at the panel** — *"scoping at the panel covers every drawer that
exists and every drawer anyone adds later."* ⇒ ⭐ **`U-6`'s *"the provider handles `Variable` and
`LocalVariable`"* is exactly one more provider in that registry.** ⛔ **A `switch` in the panel would be
the monolith the user is warning against.**

### Q — *"do HSM/BTree have a My Blueprint equivalent? Should it be unified?"*

⛔ **No, they do not — and the unification is well-founded.**

| host | what it has |
|---|---|
| **Blueprint** | ⭐ **`My Blueprint`** — 6 sections, the only real outline |
| **HSM** | `HsmEventsWindow` · `HsmGlobalsStrip` — ⛔ **no outline** |
| **BTree** | `Blackboard/LiveBlackboardPanel` — ⛔ **no outline** |
| ⭐ **`MyBlueprint` appears NOWHERE outside `Hrot.Blueprints.Editor`** | measured |

⭐⭐ **And the precedent for sharing is already built:** `Hrot.Editor.AiShared/Windows/` holds
`BlackboardAuthoringWindow` · `InspectorWindow` · `RuntimeInspectorWindow` · `AiWatchWindow` ·
`AiGraphCanvasWindow` · `SharedAiWindowRegistrar` · `PerspectiveWorkspaceRegistrar`. ⇒ **a shared
outline belongs beside them; "one shared window, per-host content" is the house pattern already.**

🔴🔴 **And the unification instinct is confirmed by a count — there are THREE surfaces that show
variables:** `BlueprintVariablesWindow` · `AiShared/BlackboardAuthoringWindow` ·
`BTree.Editor/Blackboard/LiveBlackboardPanel`. ⚠ **Plus `InspectorWindow` exists in BOTH `AiShared`
and `Hrot.Blueprints.Editor`.** ⇒ ⭐ **Ruling 9's target is bigger than `U-16` assumed:** retiring the
blueprint window alone leaves **two** implementations, not one.

📌 **Sections shown per asset type** is the right model — the descriptor list is already data
(`_sections`, a static `List<MyBlueprintSectionDescriptor>` with order + capability flags), ⭐ **so
per-host section sets are a change of DATA, not of structure.**

## 2.3 ⭐⭐⭐ **How does the UI know the layout?** — the detailed analysis *(`2026-08-15`)*

> **User:** *"how does the ui panel know the offset of variable and its memory layout to write directly?
> …likely should not spread variable layout and access logic over many places. my instinct is we might
> need some kind of variable registry with generated setters/getters per variable."*

### ⭐⭐⭐ Answer: **the registry already exists, it is hash-guarded, and it already does the GET half**

```csharp
// BlueprintDebugSession.CaptureAiPrimitiveState — the shipped READ path
ref readonly var bb = ref effectiveView.GetComponentRO<Blackboard1024>(self);
var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in bb, 1));

ulong storedHash = MemoryMarshal.Read<ulong>(bytes);
if (storedHash != def.StructureHash) return;          // ⭐⭐ THE GUARD

var layoutFields = mapIndex?.StateLayout.Fields;       // OffsetBytes · SizeBytes · Type
…  bytes.Slice(8 + field.OffsetBytes, field.SizeBytes)
// fallback: def.StateFields → descriptor.OffsetBytes · SizeBytes · ClrType
```

| | |
|---|---|
| ⭐ **The UI does NOT infer anything** | it **looks the layout up** in `DebugMapIndex.StateLayout.Fields` (or `BlueprintDefinition.StateFields`) — ⭐ **generated/registered data carrying offset, size and CLR type per variable** |
| ⭐⭐ **And it VALIDATES before trusting it** | ⛔ **the first 8 bytes of the blackboard ARE the `StructureHash`**, and the reader **refuses to decode** when it disagrees with the definition. ⇒ **a stale layout cannot silently misread — the `+8` is the guard, not padding** |
| ⇒ ⭐⭐⭐ **Your instinct is right in substance — and half of it is already built** | **the registry exists and the GETTER half ships.** ⛔ **What is missing is only the SETTER half** |

### ⚖️ **Generated per-variable setters, or one generic writer? — Coordinator recommends the generic writer**

| | for | against |
|---|---|---|
| **generated setter per variable** | type-safe; layout knowledge stays inside generated code | ⛔ **N setters × 458 assets** in output that **golden Tier 2 records line by line**; ⛔⛔ **and it makes reading GENERIC while writing is GENERATED — two mechanisms for one concept, which is ruling 9** |
| ⭐ **one generic writer over the existing registry** | ⭐⭐ **the exact mirror of a reader that already ships and works** — the existence proof is in the repo; **one layout authority**; no generated bloat | offset arithmetic lives in code ⇒ **must carry the same hash guard and a bounds check** |

⇒ 📐 **Recommendation: ONE `IVariableAccessor` with `Get` and `Set`, over the existing registry,
carrying the `StructureHash` guard — used by Details, Watch and anything later.** ⭐ **That is exactly
the *"not spread over many places"* the user asks for, and it is a smaller change than generating
setters.** ⛔ **The thing to avoid is not "offsets in code" — it is offsets in MORE THAN ONE place.**

### 🔴🔴 `Blackboard1024` — the real reason a whole-component write is wrong

⛔ **The coordinator's earlier claim that a whole-component write *"exceeds `MaxComponentSize` and
cannot work"* was WRONG. Corrected:** `EntityCommandBuffer:83` is `if (componentSize > MaxComponentSize)
throw`, and `Blackboard1024.ByteSize == 1024` ⇒ **1024 > 1024 is false. It fits, exactly.**

⭐⭐ **But the true argument is stronger than the size one ever was:**

```csharp
[ComponentId(GlobalComponentIds.Blackboard1024)]
public unsafe struct Blackboard1024 { public const int ByteSize = 1024; public fixed byte Memory[1024]; }
///  "Convention: each subsystem projects at a DISJOINT BYTE OFFSET."
```

⇒ ⛔⛔ **The blackboard is ONE component SHARED by BTree, HSM and Blueprint, each projecting its own
disjoint region.** ⇒ **a whole-component write does not merely clobber other fields — it clobbers
OTHER SUBSYSTEMS' STATE.** ⭐ **Ruling 14 stands, on much firmer ground.**

### ⭐⭐ Ruling 15 — **runtime writes ONLY while paused or deterministic-stepping** *(user)*

> *"the change of runtime var makes sense ONLY if sim is paused on breakpoint or deterministic time
> step. at that time nothing else changes the blackboard so it might be safe even from the ui directly."*

⭐ **This NARROWS ruling 7 and simplifies the design substantially:**

| | |
|---|---|
| ⛔ **Supersedes** | *"running ⇒ writes the live blackboard"*. ⭐ **The write surface is DISABLED unless paused/stepping** — ⇒ **no concurrent mutation to race with, so ruling 2a's whole reason weakens** |
| 📐 **⇒ the command buffer may be UNNECESSARY** | ⭐ **combined with §2.2's finding that the panels read `ActiveView` (`_preTickSnapshot`) while paused, writing DIRECTLY to that view is arguably the correct design** — it is the object the UI is actually showing, and ruling 12's immediacy falls out for free |
| 🔴🔴 **The one thing that MUST be measured first** | ⛔ **On pause, `:473` does `_liveRepo.SyncFrom(_preTickSnapshot)`. What happens on RESUME?** ⭐ **If nothing syncs the snapshot back, an edit made while paused is LOST when the sim continues** — ⚠ **which would be the silent-failure shape this programme keeps finding.** 📐 **Measure before designing; the coordinator has NOT run this** |

## 2.4 ⭐⭐⭐ **"Let's be consistent"** — measured, and it settles the design *(`2026-08-15`)*

> **User:** *"if we have generated read accessor and we know all the offsets why do we generate the
> specific accessor? lets be consistent."*

### ⭐⭐⭐ **There is NO generated accessor. The convention is: GENERATE THE DATA, HAND-WRITE ONE GENERIC ACCESSOR.**

| what is **generated** | where |
|---|---|
| ⭐ **`StateFields` — a name → descriptor dictionary** | **`CSharpEmitter:413`** emits `StateFields = new Dictionary<string, BlueprintFieldDescriptor>(StringComparer.Ordinal){…}` into the generated class |
| ⭐ **`BlueprintFieldDescriptor(Name, ClrType, OffsetBytes, SizeBytes, CategoryOrEmpty)`** | pure **data** |
| ⭐ **`StateLayoutField(Name, Type, OffsetBytes, SizeBytes)`** | `DebugMapBuilder`, fed by `CSharpEmitter:83`, serialized by `DebugMapSerializer` |

| what is **hand-written and generic** | where |
|---|---|
| ⭐⭐ **`BlueprintStateView.TryGetField<T>(string name, out T value)`** | `Fdp.Toolkits/Blueprints/BlueprintStateView.cs` — *"reads a field by name **using the definition's StateFields dict**"*, with a **size check** (`fd.SizeBytes != Unsafe.SizeOf<T>()`) and it exposes `StructureHash` |
| the editor's reader | `BlueprintDebugSession` — same pattern, same data |

⇒ ⛔ **`grep` finds NO per-variable generated getter or setter anywhere.** ⭐⭐ **So the user's premise
— *"we have a generated read accessor"* — is not the case, and that makes the consistent answer
unambiguous:**

> ⭐⭐⭐ **CONSISTENCY ⇒ add `TrySetField<T>` beside `TryGetField<T>`.**
> **One type. One place. Already host-neutral (`Fdp.Toolkit.Blueprints`). Already carrying the
> `StructureHash` and the size check that make a wrong write impossible.**

⭐ **This is ~15 lines mirroring a method that ships**, not a code-generation feature — and it is the
*same* answer the coordinator reached from ruling 9, arrived at independently from the user's
consistency argument. ⭐ **Two routes, one destination, is the strongest signal available here.**

⚠ **One honest caveat:** `BlueprintStateView` is documented *"Returned by
`BlueprintTestFixture.GetBlueprintState` for test assertions"* ⇒ ⛔ **it is TEST-FACING today.**
📐 **Promoting it to the production read/write seam is a deliberate decision, not a detail** — the
alternative is a production sibling, which would be **two implementations of one concept** and is
therefore the worse option under ruling 9. ⚖️ **Lean: promote it.**

### ⭐⭐ Ruling 16 — **write to BOTH the snapshot and the live component** *(user, `2026-08-15`)*

> *"we can not write just to active view if it is just the historical snapshot we are watching when
> paused — the value must be written also to the live ecs component so it is used once we resume."*

⛔ **The coordinator's *"write directly to `ActiveView`"* lean is CORRECTED.** ⭐ **Right: the snapshot
is what you SEE; the live repo is what RESUMES. Both must receive it.**

| | |
|---|---|
| **write target while paused** | ⭐ **both `_preTickSnapshot` (so the panels show it at once — ruling 12) and `_liveRepo` (so the sim uses it on resume)** |
| ⭐⭐ **And this DISSOLVES the open question** | 📌 §2.3 flagged *"measure what syncs back on resume, or a paused edit is silently lost."* ⇒ ⛔ **If both copies are written, the resume-sync DIRECTION NO LONGER MATTERS** — they already agree. ⭐ **A design that does not depend on the answer beats one that has to measure it first** |
| ⚠ **Still worth one test** | 📐 **assert it directly: edit while paused → resume → the value survives.** ⛔ **"They agree so it must work" is reasoning, not evidence** |

## 2.5 ⭐⭐⭐ **"How does the UI call a generic accessor?"** — it does not, and it never has

> **User:** *"how do you call generic getter setter from the ui. it needs compiled code calling the
> generic accessor."*

⭐⭐ **Exactly the right objection.** `TryGetField<T>` needs `T` at **compile time**; the UI holds a
`Type` at **run time** (`descriptor.ClrType`). ⛔ **The UI can never call it directly.**

### ✅ And the editor already solved this — it uses `(byte[], Type)`, not `T`

```csharp
// BlueprintDebugSession:1736 — the editor's NON-generic reader, hand-written
public static object? MarshalFromBytes(byte[] bytes, Type type)
{
    if (type == typeof(int))    return MemoryMarshal.Read<int>(bytes);
    if (type == typeof(float))  return MemoryMarshal.Read<float>(bytes);
    …  // bool uint long double byte sbyte short ushort ulong
    if (TryFormatFixedList(bytes, type, out var formatted)) return formatted;
    return bytes;                                    // ⛔ the fall-through
}
```

⇒ ⭐⭐⭐ **The layering is already three-tier, and only the middle tier is missing:**

| tier | read (ships) | write (to build) |
|---|---|---|
| **UI ↔ object** | drawers / StructEdit over `descriptor.ClrType` | ⭐ **StructEdit dialog, ruling 5** |
| **object ↔ bytes** | ✅ **`MarshalFromBytes(byte[], Type)`** | 🟠 **`MarshalToBytes(object, Type)` — must be written.** ⭐ **The pattern is established: `Marshal.StructureToPtr` at 4 sites** (`ComponentReflector:197` · `EntityJsonDumper` · `ImGuiPropertyTree` · `DtoDiagnosticMapper`) |
| **bytes ↔ blackboard** | ✅ offset slice + hash guard | ⭐ **`TrySetFieldRaw(name, ReadOnlySpan<byte>)`** |

⭐ **`TryGetField<T>`/`TrySetField<T>` stay as the typed engine/test face**, implemented as **one-line
wrappers over the raw span pair** ⇒ ⛔ **not two implementations — one implementation with two faces.**

### ⭐⭐ Why GENERATED accessors would not solve this either

⛔ **The UI still has only a `Type` at run time.** ⇒ a generated `SetHealth(float)` is **unreachable**
from a panel iterating a descriptor list; it would still need a **name → delegate table** to dispatch
through. ⭐⭐ **Generating setters does not remove the dynamic dispatch — it moves it, and adds N
methods per asset on the way.** 📌 **`MakeGenericMethod` is the third option and is rejected: per-row,
per-frame reflection on the read path, for a problem `(bytes, Type)` already solves.**

### 🔴🔴 And this **explains `BP-01`** — the marshaller is INCOMPLETE, by exactly 7 types

⭐ **`MarshalFromBytes` handles 11 primitives** — `int float bool uint long double byte sbyte short
ushort ulong` — plus fixed lists. ⛔⛔ **`Vector2` · `Vector3` · `Vector4` · `Quaternion` ·
`FixedString32/64/128` fall through to `return bytes;`** ⇒ **raw bytes.**

📌 **`BP-01` — *"Watch panel shows raw hex bytes"* — is not a panel bug. It is these seven missing
arms.** ⭐ **`EditorOfferableTypeIds` is exactly 18 and CLOSED**, so:

> ⭐⭐⭐ **Pin the marshaller against the offerable list with a reflection test** — the
> `DeclarationTagsMatchDeclarationKindTests` pattern the programme already uses. ⛔ **A 19th offerable
> type without a marshaller arm then fails at the gate.** ⚠ **Such a test would have caught `BP-01`
> years ago, and it closes `BP-01` and `U-8`'s promise (*"every offered type compiles"*) with one rail
> — now extended to *"and every offered type can be shown and edited."***

## 2.6 ⭐⭐⭐ **User-defined structs** — the user is right, and the gap is REGISTRATION, not accessors

> **User:** *"we need to support any user defined structs not just hardcoded 11 types. with correct
> layout only the compiler knows. still no need for generated accessors in a variable registry?"*

### 🔴🔴 First, the uncomfortable fact: **arbitrary user structs are NOT supported today**

```csharp
// StaticTypeRegistry:66-81 — verbatim
// Curated blittable structs used as Blueprint WorkingState vars (reflection-free compiler ->
// FQN + size declared here …). MemberSlotList … int Count (4) + 4 pad + long[8] (64) + byte[8]x3 (24) = 96
// (A general curated-struct registration mechanism -- vs. hardcoding each here -- is FUTURE WORK …)
["…MemberSlotList"]          = Unmanaged("…", 96),
["…WaveState"]               = Unmanaged("…", 104),   // "MemberSlotList (96) + 2x ushort (4) -> 8-aligned = 104"
["…HillAttackSharedState"]   = Unmanaged("…", 136),
```

⛔⛔ **THREE structs, hardcoded, with sizes computed BY HAND IN A COMMENT.** ⭐ **The file names the gap
itself** — *"a general curated-struct registration mechanism … is future work."* ⇒ **the user's premise
is correct and the coordinator's *"18 closed types"* framing was too small.**

### ⭐⭐⭐ And *"only the compiler knows"* — the compiler **doesn't** know. It emits code that ASKS.

```csharp
// CSharpEmitter:412-427 — the escape hatch, already shipping
bool layoutFromRuntime = asset.Variables.Any(f => !f.Type.SizeReliable);
string offset = layoutFromRuntime
    ? $"(int)Marshal.OffsetOf<{className}.State>(\"{f.Name}\")"   // ⭐ ask the CLR at runtime
    : f.Offset.ToString();
```

⭐⭐ **The generator is `netstandard2.0` and cannot load the user's assembly — so it EMITS C# that
resolves the layout where the type IS loaded.** ⇒ ⭐⭐⭐ **The user's instinct — *"it needs compiled
code"* — is CORRECT AND ALREADY REALISED. It is just compiled code for LAYOUT, not for accessors.**

### ⚖️ So: still no generated accessors — but **yes** to generated layout registration

| the problem | who solves it | generated? |
|---|---|---|
| **A · what size/offset does this user struct occupy in `State`?** | ⛔ **the gap.** Today: 3 hardcoded entries + the `!SizeReliable` runtime hatch | ⭐⭐ **YES — emit `Unsafe.SizeOf<TheUserStruct>()` / `Marshal.OffsetOf<State>(name)`.** ⭐ **Resolved by Roslyn at build time, so the reflection-free generator never needs to reflect** ⇒ **this IS the *"general registration mechanism"* the file calls future work** |
| **B · how do I turn those bytes into a value and back?** | ⭐ **the CLR, at runtime, where the type IS loaded** — `Marshal.PtrToStructure` / `StructureToPtr`, and StructEdit already edits arbitrary structs by reflection (`ComponentReflector:187` `Marshal.SizeOf(type)`, `:469` `type.GetFields(...)`) | ⛔ **NO. One generic arm covers every blittable struct** — ⭐ **and it replaces the 11-type if-chain AND fixes `BP-01`'s seven missing types AND supports user structs, all at once** |

⇒ ⭐⭐ **The answer to *"still no generated accessors?"* is: correct, none — but the thing you are
sensing IS missing, and it is A, not B.** ⛔ **A generated `SetWaveState(WaveState v)` would not help B
(the panel still holds only a `Type`) and would not fix A (the size still has to come from somewhere).**

### 🔴 The danger this exposes — hand-computed sizes are the `Vector3` defect waiting

⚠ **`WaveState = 104` was computed by a human in a comment.** ⭐ **The cross-host review already found
this class:** `FieldLayout.TypeAlignment` gives `Vector3` (12 B) **align 8** while the **CLR packs it at
4** ⇒ a size/alignment the compiler believes and the runtime does not.
⇒ ⭐⭐⭐ **The rail is the one that session already made *step 3a*: assert at runtime that
`Marshal.OffsetOf<State>(name) == descriptor.OffsetBytes` for every field of every corpus asset.**
⛔ **Golden Tier 1 CANNOT catch it — it records the COMPUTED offset**, so both sides agree while the
real field moves. 📌 **That gate belongs with this work, not after it.**

### 🔴 Ruling 13 — **the Watch panel must EDIT, and must show nothing before the run** *(user, `2026-08-15`)*

> ⭐⭐ *"watch panel MUST allow for value changes (and show nothing when exercise not running yet) —
> add to plan if this is not the case now."*

⛔ **It is not the case now. Both halves go in the plan.**

| | today | required |
|---|---|---|
| **editing** | ⛔ **read-only.** `WatchPanelWindow` exposes `LastRenderedWatches` and draws; ⭐ **`IBlueprintDebugSession` has no write at all** | ⭐ **the same edit path as the Details panel** — same dialog, same command buffer, same `SetComponentRaw` |
| **refresh** | 🔴🔴 **`WatchPanelWindow.cs:26` — `HandlePinValueChanged(PinValueChanged evt) { /* refresh row data */ }`** — ⛔ **a subscribed handler with an EMPTY BODY and a comment describing what it would do.** ⭐ **Trap #5, sitting exactly on ruling 12's path** | ⭐ **real** — ruling 12's immediacy runs through it |
| **before the run** | 📐 **unmeasured** | ⭐⭐ **shows NOTHING** |

### ⭐⭐ The asymmetry, stated so nobody "unifies" it by mistake

⛔ **The two panels behave DIFFERENTLY when the exercise is not running, and both are correct:**

| | not running | running / paused |
|---|---|---|
| **Details** | ⭐ **the INITIAL value**, editable (ruling 3) | the current value |
| **Watch** | ⭐⭐ **NOTHING** (ruling 13) | the current value, editable |

📌 **Why they differ:** ⭐ **Details is an AUTHORING surface that also shows runtime; Watch is a
RUNTIME surface only.** ⚠ **A watch on a value that does not exist yet has nothing to show, and
showing the JSON default there would be inventing a "current" value for an entity that has not been
spawned.** ⛔ **Do not "fix" this into consistency — ruling 9 forbids two implementations of one
concept, not two behaviours of two different concepts.**

### ➕ Ruling 5 extended *(same message)*

⭐ **Double-clicking the value cell opens the edit window too** — the three-dot button is an
*affordance*, not the only route. ⚠ **`BP-207`'s lesson applies: the gesture is fine, but it must be
DISCOVERABLE** — the three-dot button is what makes the double-click findable, which is why both exist.

---

## 3. ⭐ The emitter unification is SAFE, and here is the measurement

**Ruling 8 is the deepest change, and it is layout-neutral for every shipped asset.**

```
declaration-kind combinations across ALL shipped assets (458 files):
   193  (Variable)                 ← Instance
    32  (Parameter, WorkingState)  ← AiPrimitive
     7  (Parameter)
     5  (WorkingState)
   221  (no declarations)
   ⭐   0  with BOTH Variable and WorkingState
```

⇒ ⭐⭐ **`Variable ∪ WorkingState` equals the single populated list, in the same order, for all 58.**
⛔ **So `StructureHash` must be byte-identical and the golden corpus must not move** — that is the
gate, and it is a real one precisely because the union is a no-op **today** and will not be tomorrow.

⚠ **Keep the struct NAMES per dispatch kind** (`State` for Instance, `WorkingState`/`Params` for
AiPrimitive) — those are ABI, and renaming them is a separate, larger change nobody asked for.
⭐ **Unify what the emitters WALK, not what they are CALLED.**

---

## 4. Sequencing

| batch | what | why here |
|---|---|---|
| ⏭ **56** | ⭐⭐ **the emitter + access-path unification** (ruling 8) | ⛔ **compiler-side, fully headless, gated on `StructureHash`.** ⭐ **The user made it a precondition for the visual check, and it blocks none of the UI** |
| **57** | `U-6` — Details hosts the **shared** control + ruling 2's selection routing | ⛔ **the shared control, never a blueprint copy** (ruling 9) |
| **58** | the Value column: mode switch, read-only, pretty-printed tooltip (rulings 3-4) + blueprint's `ILiveValueProvider` and `UpdateVariableDefaultValueJson` | needs 57's host |
| **59** | the StructEdit dialog — ⭐ **three-dot button AND double-click** (rulings 5, 10) + the **not-running** write (ruling 7, half) | needs 58's column |
| ⭐ **59b** | 🔴 **the Watch panel: make `HandlePinValueChanged` real · EDITING through the same dialog · show NOTHING before the run** (rulings 11, 13) | ⛔ **ruling 12's immediacy runs through this handler** |
| ⭐⭐ **59c** | 🔴 **the ECB SURGICAL FIELD WRITE** (ruling 14) — `Fdp.Core`, additive, bounds-checked — **then** the running write on top of it, ⭐ **gated on "visible in BOTH panels within one frame while frozen"** (rulings 2a, 12) | ⛔ **the whole-component route is not merely unsafe, it exceeds `MaxComponentSize` and cannot work** |
| **60** | `U-16` — retire `BlueprintVariablesWindow` (ruling 9) | ⛔ **only after Details is proven**, or there is no editing surface at all |
| ⭐ **61** | 🆕 **the SHARED OUTLINE — `My Blueprint` unified across HSM / BTree / Blueprint**, per-host section sets as DATA | ⭐⭐ **User ruling: SEPARATE, and only AFTER the Details panel works for blueprints.** ⛔ **Not folded into `U-6`** |


---

## 5. ⭐⭐⭐ Ownership — **RULED: ONE session builds it, for ALL hosts**

> ⭐⭐ **User ruling, `2026-08-14`:** *"cross host it is. one single implem session (the one we are
> using) will be implementing for all hosts, no other session will implement until this is all done."*

⛔⛔ **The coordinator's proposed split is OVERRULED and is recorded here only so nobody re-proposes it.**

| | |
|---|---|
| ⭐ **`claude/hrot-implementation-j1jvin`** | **builds ALL of it, for HSM, BTree and Blueprint** — including `Hrot.Editor.AiShared` |
| ⛔ **every other session** | **does not implement until this is done.** ⚠ **Design and questions are fine; code is not** |
| ⭐ **Why this is the right call, not just a call** | ruling 9 is *"no keeping two implementations for the same concept."* ⛔ **Two sessions building one shared panel is the surest way to produce exactly two implementations** — the constraint would be violated by the process before a line of code disagreed |

⚠ **Recorded in `.claude/CLAUDE.md`** — that file is the only memory shared between sessions, and this
freeze binds sessions that will never read *this* document.

### ⚠ Consequence for the dispatched Batch 56

📌 **[HANDOFF_Batch56](batches/HANDOFF_Batch56_Emitter_Unification.md) §5 says `Hrot.Editor.AiShared` is
*"the CROSS-HOST session's territory — do not touch it."* ⛔ **That RATIONALE is superseded by this
ruling.** ⭐ **Its SCOPE stands unchanged:** Batch 56 is the emitter unification alone, and it has no
business in `AiShared` either way. ⛔ **The handoff is NOT amended** — rule 1 — and this note is the
correction.
