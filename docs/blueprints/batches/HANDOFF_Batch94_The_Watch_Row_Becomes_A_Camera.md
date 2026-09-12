<!--STATUS
state: LIVE
updated: 2026-08-19
current-answer: this whole file — the Batch 94 dispatch.
stale-below: nothing.
known-rot: none.
known-conflict: none. HANDOFF_Batch93 is SUPERSEDED by this file: its 93a value-feed
  premise was measured false by the batch itself, and Q46 §2 replaced it with the
  user's own specification. Build from THIS file, not from Batch 93's sections 2–4.
-->
# HANDOFF — Batch 94: **the watch row becomes a camera**

> 📌 **Dispatched at `58bf7df4e`.** ⭐ **Branch from the handoff commit** *(rule 7)*.
> ⛔⛔ **YOUR SCOPE IS FROZEN AT THIS SHA.** ⭐ Documents changing after it are **FYI ONLY**.
> ⚠ **If a later document INVALIDATES an item — STOP AND REPORT.** ⛔ **Do NOT adapt, do NOT revert.**
> ⭐ **Rule 3: allocate your own ids and state them.** ⭐ **Rule 1b: push
> `chore: started batch 94 at 58bf7df4e` FIRST, before any code.**

> ## ⭐⭐⭐ THE DESIGN BASIS, and it is unusually strong
> 📄 **[`Architect_Question_46_What_A_VariableRow_Means.md`](Architect_Question_46_What_A_VariableRow_Means.md)
> §2 is the USER'S OWN SPECIFICATION**, given verbatim on `2026-08-19` — ⭐ ledger **`R-103`**.
> ⛔ **It is not a recommendation of mine to weigh up.** ⭐ §4 of that document is mine: *how* to build it.
> ⚠ **Batch 93's sections 2–4 are SUPERSEDED** — its `93a` value-feed premise was **measured false by
> Batch 93 itself**, and that measurement is what produced `Q46`.

---

## 1. ⭐⭐ WHERE THIS STARTS

Batch 93 stopped correctly and landed **only rails**. ⭐ **Nothing of the gesture, the value feed or the
store wiring exists yet.** What it proved:

| 📐 measured, Batch 93 | |
|---|---|
| ⛔ **a pinned row is a SNAPSHOT** | the arms are invoked every frame but **close over that frame's value**, not over the provider. Liveness in Details comes from **rebuilding the row each frame**; `PinnedVariableRowSource` never rebuilds |
| ⭐ **the row TYPE, the store, the window and the table are all FINE** | a **hand-built** row whose arm closes over the source stays live through the pinned store — **railed** |
| ⭐ **the fix is small** | either arm made to close over the source flips **exactly one** rail and leaves **1489 of 1490** AiShared rails green |
| ⛔ **`(pending)` is a second, independent half** | `HasEverBeenWritten` is a `bool` decided at row-build time ⇒ a variable the run writes **after** you pinned it says `(pending)` for ever |

⭐ `APinnedRowIsASnapshotTests` **asserts the defect on purpose and says so** ⇒ ⭐⭐ **it is this batch's
acceptance test.** ⛔ **INVERT those rails, never delete them** — they are the only proof the fix works.

---

## 2. 🛠 **`94a` — the arms become cameras** *(`Q46` §4a · rule 1)*

⭐ **Both row sources, both arms.**

| # | site | today |
|---|---|---|
| **1** | `SectionVariableRowSource:105` *(object)* / `:118` *(bytes)* | `var value = live![v.Name]; … readObject: () => value` |
| **2** | `BlackboardSectionRowSource:81` | the same shape |

⇒ ⭐ **close over the PROVIDER, not over the looked-up value.** ~4 lines per arm.

⛔⛔ **BOTH arms, never one** — 📌 `Q46` §4a: a fix on the object arm alone makes pinning work on
Blueprint and **silently freeze on BTree/HSM**, which is exactly the split `U-6` removed.

⚠ **`PinnedVariableRowSource` does NOT change** — ⭐ *"it stores what it is given, and that is
correct."* ⛔ Do not make it re-resolve per frame: the Watch mixes arbitrary assets and entities, so
re-resolution needs a source per `(asset, entity)` and walks straight into spec §10, which is **open and
fenced out**.

---

## 3. 🛠 **`94b` — ONE tick source for every host** *(`Q46` §2 rule 2b + §4b · `R-103`)*

> ⭐⭐⭐ **User, verbatim:** *"the brain (cgf) does not tick ANY behavior when dt=0 so the tick source is
> not dependent on behavior type."*

📐 **Coordinator-measured, and true:**

| | |
|---|---|
| `BlueprintTickSystem:51` · `BTreeTickSystem:55` · `HsmTickSystem:103` | ⭐ **all three open `if (deltaTime <= 0f) return;`** |
| ⛔⛔ **but `ModuleHostKernel.UpdateInternal:483`** | **`_liveWorld.Tick()` is called UNCONDITIONALLY, before any `dt` check** ⇒ ⛔ **`SimulationTick` advances while paused.** Sampling on it would clear the red under a breakpoint — 📌 what Batch 68 correctly refused |

### ⭐ Build

| # | |
|---|---|
| **①** | ⭐⭐ **one global behaviour-frame counter** — a `uint`, bumped by a tiny system in the Simulation phase, gated on `dt <= 0f`. 📐 **`CognitiveRuntimeModule.SimulationSystems` is a plain ORDERED ARRAY** *(`ChannelArbitration → CognitiveInterrupt → BTree → Hsm128 → Hsm64 → CognitiveCleanup`)* ⇒ ⭐ **append after `CognitiveCleanupSystem`**; ⚠ **`CognitiveRuntimeModule_RegistersAllTickSystems` asserts that order and must be updated** |
| **①b** | ⚠ **`BlueprintTickSystem` lives in a DIFFERENT module**, so *"last in the behaviour phase"* cannot be guaranteed across modules. ⭐⭐ **It does not matter, and here is why:** the counter is an **EDGE DETECTOR** — *"has the sim moved since I last sampled?"* — and ⭐ **the sampling happens at DRAW time on the UI thread** *(`94c`)*, reading whatever the sim holds then. ⇒ ⛔ **where in the phase the bump lands does not change which value is read.** ⭐ Bump anywhere below a `dt` gate |
| **②** | ⭐ **every host's rows read that ONE source** through the existing `VariableRow.AssetTick` seam. ⛔ **The seam does not change — only its feed.** ⭐⭐ **BTree and HSM rows stop being inert for the first time** |
| **③** | ⭐⭐ **rail the CONSTRUCTED row** *(`R-67`)* — assert `AssetTick is not null` on a row built by the **production** path, ⛔ never on the registrar's source |

### ⭐⭐ And it DELETES machinery rather than wiring it

📐 `BlueprintAssetTickSource` + `BlueprintAssetTick` exist, are documented and railed, and have **ZERO
production callers** *(`M-27`)*. ⭐ One `uint++` per frame costs nothing ⇒ ⛔ **no `Enabled` flag, no
`Attach`/`Detach` refcount, no per-instance `ConcurrentDictionary` write.**

⇒ ⭐ **`R-13`: this is duplicate CODE ⇒ ROUTE.** Move `BlueprintAssetTickTests`' rails onto the new
counter and drop the per-`(asset, entity)` table. ⛔⛔ **Not a silent deletion — say so in the report,
and say what the rails became.**
⚠ **If routing the rails turns out to cost more than the whole of `94b`, KEEP the table dormant and
report that instead.** ⭐ The batch's value is the new counter, not the removal.

---

## 4. 🛠 **`94c` — sample on the pulse, render from cache** *(`Q46` §2 rules 2–4 · `R-76`)*

| rule | build |
|---|---|
| **2** | ⭐ **sample when the counter has MOVED since this row last sampled** — at most once per counter value |
| **3** | ⭐⭐ **the value is cached and drawn EVERY UI frame from the cache**, ⛔ with no accessor call |
| **4a** | ⭐ **pin while running-but-PAUSED ⇒ sample IMMEDIATELY**, and make that first sample the **baseline** so nothing highlights on the first frame |
| **4b** | ⭐ **pin while PLANNING ⇒ do not sample**; the cell shows `(pending)` because the cache is unfilled — ⛔ **not** because nobody writes it |

### ⭐⭐⭐ WHERE the cache lives — **read this, it is the part I got wrong once**

> ⭐⭐ **User, verbatim:** *"watch panel rows are not identical instances to details panel rows… each
> completely independent on each other knowing nothing about each other. Of course they are ticked at
> the same time."*

⇒ ⛔⛔ **NOT one process-wide cache keyed by `(AssetId, Entity, VariablePath)`.** That would couple a
Watch row to the Details row of the same variable, which the ruling forbids.
⇒ ⭐⭐⭐ **ONE sampler+cache instance PER PANEL**, keyed by row identity **within that panel**.
⭐ **One implementation of the class, N instances** — ⛔ that is not a ruling-9 violation, it is the
ruling-9 shape *(`R-103`: independent instances of one row class)*.

⚠ **`VariableRow` is an immutable `sealed record` and Details rebuilds it every frame** ⇒ ⭐ *"cached on
the row"* means **cached against the row's IDENTITY in its panel's sampler**, ⛔ never a mutable field
on the record — a field would be discarded on the next rebuild.

### ⭐ The pulse is read, not pushed

⭐ The counter advances on the sim thread; the sampler reads it **at draw time** and samples only when it
has moved. ⛔ **No callback from the tick loop into the UI, no lock.**
⚠ **Name the consequence in the report:** if the UI is slower than the sim, the sampler sees the
**latest** value and **misses intermediate changes**. ⭐ That is correct and unavoidable for a watch
panel — ⛔ do not add buffering to chase it.

---

## 5. 🛠 **`94d` — change detection** *(`Q46` §2 rules 6–9 · §4c)*

> ⭐⭐⭐ **User, verbatim:** *"we have fast pre-compiled binary serializer mechanism for any component and
> i guess it can be used for any class. it produces bytes. we compare these bytes. No way comparing
> rendered text!"*

| value kind | ⭐ how |
|---|---|
| **unmanaged / struct, ANY size** | ⭐⭐ **already solved** — `ReadRawValue` is `ReadOnlySpan<byte>` and `VariableChangeMonitor` already `SequenceEqual`s it. ⛔ **There is no size limit to remove**; rule 7 is met today |
| **managed — class, string** | ⭐⭐⭐ **serialize to bytes with `FdpAutoSerializer` and compare those bytes** |

### ⭐ The serializer — measured `2026-08-19` *(`M-27`)*

**`Fdp.Core.FlightRecorder.FdpAutoSerializer`** — Expression-tree JIT, compiled once per type then
cached. ⭐ **`Fdp.Core` is ALREADY a `ProjectReference` of `Hrot.Editor.AiShared`** ⇒ nothing to add.
⭐ Covers primitives · enums · `string` · `Entity` · `T[]` · `List` · `Dictionary` ·
`ConcurrentDictionary` · `HashSet` · `Queue` · `Stack` · `ConcurrentBag`, and **CASE Z recurses into any
other class or struct**.

| ⛔ **tooth** | ⭐ **what you must do about it** |
|---|---|
| **① it is GENERIC** — `Serialize<T>(T, BinaryWriter)`, and a row holds `object` | ⭐⭐ **copy the shape that already exists**: `FdpPolymorphicSerializer.CompileWriteDelegate` builds `(writer, obj) => FdpAutoSerializer.Serialize<T>((T)obj, writer)` by `MakeGenericMethod` and caches per `Type` — **~15 lines**. ⛔ **Without** its `[FdpPolymorphicType]` registry, which we cannot require of arbitrary watched types |
| **② get-only properties are SKIPPED** *(`CanRead && CanWrite`)* | ⚠ a class exposing state only through computed getters serializes to **nothing** ⇒ its changes are invisible. ⭐ **Do not "fix" the serializer** — it is flight-recorder infrastructure. ⭐ **Report it**; such a row simply never highlights |
| **③ ⛔⛔ NO CYCLE GUARD** | a back-reference **recurses until the stack dies.** ⭐ This is the one that can take the editor down |

### ⛔⛔ THE FENCE — non-negotiable

> ⭐⭐ **The first time a type throws, or exceeds a depth/size cap, record THAT TYPE as
> not-comparable and never serialize it again.** ⭐ Such a row **never highlights**.
> ⛔ **It must never crash the editor**, and ⛔ **it must never fall back to comparing text.**

⭐ **Reuse one pooled `MemoryStream` + `BinaryWriter`** per sampler so a per-tick snapshot does not
allocate a stream per row. ⚠ **The allocation-trait rails will see it otherwise.**

### ⚠ And fix what the monitor reads

📐 `VariableChangeMonitor.Observe` reads **only** `row.ReadValue()` — the **byte** arm ⇒ ⛔ **Blueprint's
object-arm values could never highlight at all.** ⭐ **Both arms feed the same comparison.**

---

## 6. 🛠 **`94e` — `(pending)` stops being frozen** *(`Q46` §4e · `BP-338`)*

⭐ Add an **optional trailing `ReadHasEverBeenWritten` delegate, `null` by default, PREFERRED when
present** — ⭐⭐ **the exact shape Batch 90 established for `ReadValueObject`.**

📐 **Measured cost:** **3 production construction sites** *(`WatchRowBridge:58` ·
`BlackboardSectionRowSource:101` · `VariableRowSources:138`)* and **~28 test sites** name
`HasEverBeenWritten`. ⇒ ⭐ **an optional arm changes ZERO of them.**
⛔⛔ **Do NOT widen the `bool` into a delegate** — one precedent, not a new idiom *(ruling 9)*.

---

## 7. 🛠 **`94f` — the gesture** *(was `93a`/`93b`; spec §7 · `BP-346`)*

| ⭐ | |
|---|---|
| **the command** | ⭐⭐ **a DISTINCT command id for the VARIABLE gesture.** 📐 `CommandCatalog.ToggleWatch = "editor.toggle-watch"` **exists and is PIN-scoped** *(`IDebugSession.ToggleWatch(PinId)`, `BlueprintDebugToNodeEditAdapter:140`)*. ⛔⛔ **Do not reuse it** — 📌 `BP-346`, and Batch 93 named this trap exactly |
| **entry points** | ⭐⭐ **ONE command, TWO entry points**: the **My Blueprint row** context menu **and** the **Details table row**. ⛔ A one-surface gesture re-creates the split `U-6` removed |
| **what it does** | a **toggle** — *"Watch this variable"* / *"Stop watching"* ⇒ `Pinned.Pin(...)` / unpin. 📐 **`PinnedVariableRowSource.Pin` exists and its only caller is a TEST** ⇒ ⛔ **do not rebuild the store** |
| ⭐⭐ **when allowed** | **Planning** ✅ · **Paused/stepping** ✅ · ⛔ **free-running FORBIDDEN** · ⛔ **replay FORBIDDEN** *(spec §7)*. ⭐ Run state from `R-69`'s cluster state — ⛔ not a new notion of *"running"* |
| ⭐⭐⭐ **how it refuses** | **greyed + a tooltip saying WHY** — 📌 the user's own rule: *"same information value, no false expectations."* ⛔⛔ **never a click that dead-ends** |
| ⚠ **the canvas stub** | `CanvasRenderer:684`'s `"Watch this Value"` sits inside `BeginDisabled()` with no handler — ⚠ **it is a PIN menu, not a variable row.** ⭐ **If a pin does not map cleanly to a variable row, LEAVE IT DISABLED and say so.** ⛔ **Do not invent a pin→variable mapping** |

---

## 8. 🛠 **`94g` — restart survival** *(was `93c`; spec §5 · `R-75` · `R-102`)* — ⚠ **CONDITIONAL**

⛔⛔ **Start this ONLY if `94a`–`94f` are complete and green.** ⭐ **Otherwise STOP and report** — this
slice is independent, and shipping a live Watch is worth more than a persisted dead one.

| ⭐ | |
|---|---|
| **`R-102`, ruled** | `StagingEntityExtractor` publishes `oldToNewMap` through an **OPTIONAL `Action<IReadOnlyDictionary<long,long>>`**, wired to the orchestration bus **BY THE SUBSYSTEM**. ⛔ **Not** a bus dependency inside `Hrot.CGF` *(it would stop being a pure transform)*; ⛔ **not** a widened `Extract` return |
| **persistence** | **BY TRANSLATION** *(spec §5, `R-75`)* — ⛔ never a raw `Entity` handle |
| ⚠ **the resolver** | 📐 **`M-26`: there are FOUR `FindEntityByNetworkId`, not two**, and ⭐ **neither of `R-77`'s two is the keeper** — `MapPickServiceBridge:121` **caches its `_networkQuery`** while the other three rebuild per call. ⇒ ⭐ **a pinning caller would be the FIFTH.** ⛔ **Do NOT unify all four in this batch** *(`BP-345`)* — ⭐ **use the `MapPickServiceBridge` shape** and file the unification |

---

## 9. ⛔ WHAT MUST NOT BE BUILT

| ⛔ | why |
|---|---|
| **a second window** | `AiWatchWindow` is the survivor `Q38-E` already picks |
| **re-resolving `PinnedVariableRowSource` per frame** | ⇒ spec §10, **open and fenced out** |
| **per-variable codegen** | `R-49` |
| **a binding re-resolved per tick** | `R-76` — ⛔ it churns row identity under the cursor. ⭐ **VALUE per tick, BINDING only on selection change** |
| **comparing rendered text** | ⛔⛔⛔ `R-103`, the user's own words |
| **"fixing" `FdpAutoSerializer`** | it is flight-recorder infrastructure with its own rails. ⭐ Report tooth ②, do not widen it |
| **unifying the four `FindEntityByNetworkId`** | `BP-345` — ⭐ file it, do not do it here |

---

## 10. ⭐ GATES — the seven-row contract

⭐ **Baseline is Batch 93's table** *(AiShared **1490** · BTree.Editor **622** · Hsm.Editor **554** ·
Generators **277** · Persistence **143** · Blueprints **3773/0/10 skip** · Hrot.Editor **201** ·
Breakpoints **143** · NodeEditor.Core **211** · NodeEditor.UI **135** · Fhsm **300** ·
Fdp.Presentation **146 filtered**)*, base sha **`58bf7df4e`**.

| # | report |
|---|---|
| **1** | one row per gate: **verbatim command · pass/fail/skip · Δ vs baseline** |
| **2** | ⭐⭐ **a `--no-build` COLUMN.** ⛔ `NodeEditor.Core`, `NodeEditor.UI`, `Fhsm.Tests` are **out of solution ⇒ they MUST build** |
| **3** | ⭐⭐⭐ **golden movement as a DIFF SHAPE**, not a yes/no |
| **4** | every RED **confirmed pre-existing against `58bf7df4e`**, named |
| **5** | the working tree is **CLEAN after every suite run** |
| **6** | **both quarantine counts** — ⛔ **a new skip is a FINDING, not a fix** |
| **7** | ⭐ **`tracker-counts.py --check` and `rulings-check.py`, UNFILTERED, with `EXIT=`** — ⛔⛔ **do not pipe them through `tail`**: 📌 Batch 90 found that discards the banner **and** the exit code, and the remedy table then reads as a verdict |

⛔ **`Fdp.Toolkits.Tests` NOT RUN** *(`DEBT-AIB-030` — seven tests, identity ROTATES between runs)*.
⚠ **But `94b` touches the behaviour phase**, so if you add a rail for the counter, ⭐ **say which project
it landed in and confirm it by `--filter`.**

### ⭐ Extra, this batch only

| ⭐ | |
|---|---|
| **the inverted rails** | ⭐⭐ name each `APinnedRowIsASnapshotTests` rail and what it now asserts. ⛔ **Deleting one is a finding** |
| **the allocation traits** | ⭐ confirm the pooled writer did not trip them |
| ⭐⭐ **a live-pin rail** | pin a variable, advance the counter, assert the Watch value **moved** — ⛔ this is the batch's headline claim and it needs one unambiguous test |

---

## 11. ⭐⭐ IF YOU MUST STOP

⭐ **Stop at an item boundary, report what you measured, and DO NOT adapt the design.**
⚠ **Two premises of mine most likely to be wrong** — ⭐ **if either is, STOP and say so:**

| ⚠ | |
|---|---|
| **①** | *"one bump site is enough"* — ⭐ **`§3 ①b` argues the counter is only an EDGE DETECTOR.** ⛔ If you find the sampler must read at bump time rather than draw time, that argument collapses and the placement becomes load-bearing ⇒ **report it, do not scatter three `Bump` calls silently** |
| **②** | *"the fence makes managed comparison safe"* — ⭐ if a real watched type hits tooth ② or ③ **in the first thing you try**, that is a design signal, not a detail |
| **③** | *"one sampler per panel"* — ⭐ if the panels turn out to share a single renderer instance such that per-panel state has nowhere to live, ⛔ **do not fall back to a global cache** *(it breaks `R-103`'s independence)* — report it |

⛔⛔ **And the standing one:** if a document that changed after `58bf7df4e` invalidates an item, ⭐ **STOP
AND REPORT.** ⛔ **Do not adapt, do not revert** *(the rule that cost 20 minutes on Batch 81)*.

---

## 12. ⭐⭐⭐ WHAT THIS UNLOCKS

⭐ **`E2`–`E7` — the last SKIP rows in
[`GUIDE_Blueprint_Visual_Check.md`](GUIDE_Blueprint_Visual_Check.md)** become runnable, ⇒ the visual
check covers the whole surface for the first time.
⭐⭐ **And the change highlight goes live on all three hosts at once** — Blueprint through the object
arm, BTree/HSM through bytes, both compared the same way.
