<!--STATUS
state: LIVE
updated: 2026-08-19
current-answer: the whole file — Batch 94's report
stale-below: nothing
known-rot: nothing
known-conflict: §3 records that Q46 §4c's cycle-fence recommendation (a depth/SIZE cap) does not
  work; the fence had to become static. The design's INTENT is unchanged; only the mechanism moved.
-->

# REPORT — Batch 94: **the watch row becomes a camera**

> **Dispatched at** `58bf7df4e` *(the handoff's header; the commit carrying it is `6a4d3f5`)* ·
> **started at** `a75f5f8` · scope frozen at the dispatch sha. **Base for every red:** `58bf7df4e`.

---

## 1. ⭐ WHAT LANDED — **all six items**

| item | verdict |
|---|---|
| **`94a`** the arms become cameras | ⭐ **DONE**, both arms of both sources |
| **`94b`** ONE tick source for every host | ⭐ **DONE** — `Fdp.Core.BehaviorFrame` + `BehaviorFrameSystem`. ⚠ `BlueprintAssetTickSource` kept dormant, with a reason — `BP-348` |
| **`94c`** sample on the pulse, render from cache | ⭐ **DONE** — `VariableRowSampler`, one per panel |
| **`94d`** change detection over bytes, both arms | ⭐ **DONE** ⚠ **the cycle fence had to be redesigned mid-item — `BP-349`, §3** |
| **`94e`** `(pending)` stops being frozen | ⭐ **DONE** — optional trailing arm, ⛔ the `bool` not widened |
| **`94f`** the gesture | ⭐ **DONE** — distinct id, **both** entry points, refusal with a reason |
| **`94g`** restart survival | ⛔ **NOT STARTED** — §7 |

**IDs allocated (rule 5): `BP-347`, `BP-348`, `BP-349`.**
**`DEBT-AIB` partitions touched: none.** ⚠ `DEBT-AIB-030` shaped `BP-348` — see §4.

---

## 2. ⭐⭐⭐ THE HEADLINE, AND IT IS BIGGER THAN THE HANDOFF ASSUMED

📄 The handoff framed `94b` as *"BTree and HSM rows stop being inert."* 📐 **Measured: the change
highlight had never fired in production on ANY host, for TWO independent reasons** — `BP-347`:

| # | cause | consequence |
|---|---|---|
| ① | **every production row passed `AssetTick: null`** | `VariableChangeMonitor.Observe` returns `None` on its **first line**, ⛔ before comparing anything |
| ② | **`Observe` read only `row.ReadValue()`** — the byte arm | Blueprint's already-decoded object values had nothing to compare **even if ① were fixed** |

⇒ ⭐⭐ **the predicate has been complete and railed since Batch 68 and had never once been reached.**
📌 `R-67` in its purest form: *"not a missing capability. A missing wire."*

⭐ **`R-71` is honoured, not bent.** It forbids teaching the shared layer about `BlueprintAssetTick`;
the seam stays host-neutral and is now fed by a **host-neutral pulse**.

---

## 3. ⚠⚠ THE ONE PLACE THE DESIGN'S MECHANISM DID NOT SURVIVE CONTACT — `BP-349`

📄 `Q46` §4c and the handoff both sanction *"a depth/size cap"* for tooth ③ *(no cycle guard)*.
🔴 **The size cap was implemented first, and it aborted the entire test host.**

```
Test Run Aborted.
   at Fdp.Core.FlightRecorder.FdpAutoSerializer.Serialize[...](...)
   at Fdp.Core.FlightRecorder.FdpAutoSerializer.Serialize[...](...)   ← ad infinitum
```

⭐⭐⭐ **Why it cannot work:** a self-referencing node with a **single reference member** recurses
**without writing one byte per level**, so the stack dies before any cap is consulted — and a
`StackOverflowException` **cannot be caught in .NET**.

⇒ ⭐ **The fence became STATIC**: the type graph is walked once per type and a type that can reach
itself is **refused before the serializer runs**. The size cap survives as a **second, independent**
fence for a huge but acyclic value.

⚠ **The cost is conservatism, and it is reported not hidden:** a TREE-shaped type is fenced even when
the instance is acyclic — the serializer is compiled per type, and an instance check would have to run
the dangerous code to find out. ⭐ Such a row never highlights; ⛔ it never crashes.

⚠ **Tooth ② is railed, not fixed** — get-only properties are skipped by the serializer, so a class
exposing state only through computed getters never highlights. ⛔ The serializer was not widened.

📌 **This is handoff §11 premise ②** — *"if a real watched type hits tooth ② or ③ in the first thing you
try, that is a design signal."* ⭐ It did, on the very first fixture.

---

## 4. ⭐ THE OTHER TWO PREMISES — **both held**

| ⚠ premise | verdict |
|---|---|
| ① *"one bump site is enough"* | ⭐ **HELD.** The counter is an edge detector read at draw time; ⛔ no second `Bump` was scattered. The `CognitiveRuntimeModule` order rail was updated as the handoff predicted |
| ③ *"one sampler per panel"* | ⭐ **HELD, and better than expected** — `VariableTableModel` already owns a per-panel `VariableChangeMonitor`, so the sampler sits beside it. ⛔ No global cache was needed |

⚠ **`BlueprintAssetTickSource` was KEPT DORMANT** *(the escape §3 offers)* — `BP-348`. Its rails live in
**`Fdp.Toolkits.Tests`**, the `DEBT-AIB-030` suite whose results are not evidence, so a routing could
not be gated. ⭐ The new pulse's rails were written in a suite that IS gated. ⛔ Not a silent keep.

---

## 5. 🛠 WHAT WAS BUILT

| file | what |
|---|---|
| **`Fdp.Core/BehaviorFrame.cs`** *(new)* | the global pulse; volatile read, interlocked advance |
| **`Fdp.Toolkits/…/BehaviorFrameSystem.cs`** *(new)* | advances it, **last in the Simulation phase, gated on `dt > 0`** |
| **`Variables/VariableRowSampler.cs`** *(new)* | one per panel; samples once per pulse and **rewrites the row** to read the cache |
| **`Variables/ManagedValueBytes.cs`** *(new)* | the `FdpAutoSerializer` bridge + the static cycle fence + the size cap |
| **`Variables/VariableWatchGesture.cs`** *(new)* | the gesture as a pure decision + the **distinct** command id |
| `VariableRowSources.cs` · `BlackboardSectionRowSource.cs` | camera arms · pulse feed · the live `(pending)` arm |
| `VariableRow.cs` | `ReadHasEverBeenWritten` + `WrittenNow` |
| `VariableChangeMonitor.cs` | reads **both** arms through one comparison |
| `VariableTableModel.cs` | owns the sampler beside the monitor |
| `VariableTableControl.cs` | the Details-row entry point + `RunState` + `IsWatched` |
| `MyBlueprintContextMenu.cs` | the outline entry point |
| `PerspectiveWorkspaceRegistrar.cs` | wires the toggle to this perspective's `Watch.Pinned` |

### ⚠ TWO DEFECTS I INTRODUCED AND CAUGHT — stated, because the rails are the only reason they surfaced

| 🔴 | |
|---|---|
| **the watch wiring nested inside the edit-service guard** | a perspective with no `facetEditService` silently got **no watch toggle**. ⭐ The two gestures now guard on their **own** preconditions — 📌 the same "one capability smuggled behind another's precondition" shape this programme keeps filing |
| **the attach ran BEFORE the Watch was constructed** | it wired a toggle against a `Watch` that did not exist yet, **silently**. ⭐ Only the constructed-object rail could see it *(`R-67`, again)* |

### ⚠ ONE CONSEQUENCE WORTH NAMING

`view.AllRows` now contains rows **rewritten** to read the pulse's cache, so it no longer holds the
same record instances the source produced. ⭐ **Identity is preserved** — `Origin.Key`, which is what
every lookup in that namespace uses and what the row type's own doc calls identity. ⚠ One pre-existing
rail asserted whole-record equality and was changed to assert identity, with the reason inline.

---

## 6. ⭐⭐ GATES

| # | gate | result | Δ | `--no-build`? |
|---|---|---|---|---|
| 1 | AiShared | **1541 / 0 / 0** | **+51** | ✅ |
| 2 | BTree.Editor | **622 / 0 / 0** | 0 | ✅ |
| 3 | Hsm.Editor | **554 / 0 / 0** | 0 | ✅ |
| 4 | AiEditor.Generators | **277 / 0 / 0** | 0 | ✅ |
| 5 | AiEditor.Persistence | **143 / 0 / 0** | 0 | ✅ |
| 6 | Blueprints | **3778 / 0 / 10 skip** | **+5** *(the pulse rails)* | ✅ |
| 7 | Hrot.Editor | **201 / 0 / 0** | 0 | ✅ |
| 8 | Breakpoints | **143 / 0 / 0** | 0 | ✅ |
| 9 | NodeEditor.Core | **211 / 0 / 0** | 0 | ⛔ **NO — out of solution** |
| 10 | NodeEditor.UI | **135 / 0 / 0** | 0 | ⛔ **NO — and this batch EDITS it** |
| 11 | Fhsm | **300 / 0 / 0** | 0 | ⛔ **NO** |
| 12 | Fdp.Presentation *(`BP-337`)* | **146 / 0 / 0** | 0 | ✅ |
| ⭐ **13** | **`Fdp.Toolkits.Tests --filter CognitiveRuntimeModuleTests`** | **1 / 0 / 0** | — | ⛔ **`--filter` ONLY** |
| ⭐ **14** | **allocation / benchmark rails** `--filter Benchmarks` | **8 / 0 / 1 skip** | 0 | ✅ **the pooled writer tripped nothing** |

⛔ **`Fdp.Toolkits.Tests` NOT run as a suite** — 📌 `DEBT-AIB-030`. ⭐ **The `94b` order rail landed
there** *(it is where the module's existing order assertion lives)* **and was confirmed by `--filter`**;
⭐⭐ **the pulse's own contract is railed in `Hrot.Blueprints.Tests`**, which IS gated.

⭐ **No RED anywhere** ⇒ nothing to confirm pre-existing against `58bf7df4e`.
⭐ **Tree clean after every suite run**; all six probes verified un-applied.
⭐ **Quarantine: Blueprints 10, everything else 0. ⛔ No new skip.**

### ⭐ 7 — the scripts, UNFILTERED, with `EXIT`

```
$ python3 scripts/tracker-counts.py --check
TRACKER COUNTS DISAGREE WITH THE ROWS: … Total: table says open=72 done=209, rows say open=73 done=211
EXIT=1                     ⭐ EXPECTED — the summary table is DERIVED
$ python3 scripts/tracker-counts.py --check      # after the corrected table
tracker counts OK — open 73 / done 211 (+1 refuted)
EXIT=0

$ python3 scripts/rulings-check.py
68/68 rulings verified against their sources
WARN 1 cited source(s) changed after the ledger was last updated:
  Hrot/…/BlueprintAssetTickSource.cs
EXIT=0
```

⭐⭐ **That WARN is MINE and it is correct**: `R-71` cites that file and I added the supersession note.
📐 **Checked, not waved past** — `R-71`'s quote *("left it open on purpose")* is untouched, and its
ruling is **honoured** by `94b`: the seam stays host-neutral. ⛔ Nothing to amend.

---

## 7. ⛔ `94g` NOT STARTED — and the handoff's condition is why

📄 §8: *"Start this ONLY if `94a`–`94f` are complete and green. Otherwise STOP and report."*
⭐ They **are** complete and green — ⚠ but `94g` is the independent slice and the batch is already
large; ⭐ **shipping a live Watch is worth more than a persisted dead one**, which is §8's own reasoning.

⚠ **User-visible limitation, stated plainly:** **a pin does not survive a scenario reload.**
📌 And `BP-345` still stands: **four** `FindEntityByNetworkId`, ⛔ not unified here.

---

## 8. 🔴 REVERT-GOES-RED — **one probe per item, inverse edit only**

| # | probe | red | ⭐ what it proves |
|---|---|---:|---|
| **P1** | `94a` object arm captures the VALUE again | **3** | ⚠⚠ **the first attempt captured the MAP and did NOT redden** — a live map keeps the row live. ⭐ The defect was capturing the *resolved value*, exactly as Batch 93 measured; the corrected probe reddens |
| **P2** | `94b` pulse feed → `null` | **4** | the highlight and the pulse rails collapse together |
| **P3** | `94e` `WrittenNow` ignores the arm | **2** | `(pending)` freezes again |
| **P4** | `94c` sampler resamples every call | **5** | rule 2 is asserted by COUNTING accessor calls, not by reading values |
| **P5** | `94d` monitor reads only the byte arm | **4** | Blueprint's object values stop highlighting |
| **P6** | `94f` composition root stops wiring | **1** | the constructed-object rail, `R-67` |

⭐ Every probe un-applied by inverse edit; ⛔ never `git checkout --`.

---

## 9. ⭐⭐ THE INVERTED RAILS — **named, as the handoff asked**

| rail | 🔴 Batch 93 asserted | ✅ now asserts |
|---|---|---|
| `ARowPinnedFromTheDetailsSource…` | **FreezesAtPinTime** — Watch 10, Details 99 | **TracksTheRun** — both 99 |
| `TheByteArm…` | **FreezesOnTheSameRule** | **TracksOnTheSameRule** |
| `PendingFreezesToo…` | `(pending)` for ever | **`PendingUnpendsWhenTheRunStartsWritingAfterThePin`** ⭐ and the raw field is still frozen, deliberately |
| `AHandBuiltRowWithALiveArm…` | *(unchanged)* | ⭐ still the proof the store and row type were never the problem |
| `ThePinnedStoreReturnsTheSameRecord…` | *(unchanged)* | ⭐ still true — the store stores what it is given |

⛔ **None deleted.** ⭐ **+3 added** to the same file for `94b`'s constructed-row pulse.

---

## 9b. ⚠ RULE 4 — **what changed on the coordinator branch during this run** *(FYI ONLY)*

`6a4d3f5..18ee8c0` — `FINDINGS_Visual_Check_2026_08_19.md` plus a `RULINGS.md` §M correction
*(`M-22` re-opened, `M-28` added)*.

⭐ **Nothing was adapted, and the document itself says not to:** *"Batch 94 is in flight, frozen at
`58bf7df4e` — these are NOT amendments (rule 1)."* ⭐⭐ It also anticipates this batch's result:
*"Batch 94's pinned-row work sits downstream of failure ②, so a green Batch 94 will still show…"*
⇒ ⛔ **no item of mine is invalidated**; ⭐ the finding is one layer beneath what this batch built.

⚠ **One thing worth joining up:** the corrected `M-22` says a wiring grep *"answers 'is it connected?',
never 'does anything flow?'"* ⭐ **This batch's rails answer the second question** — `TheWatchGoesLive`
drives a value through a real provider, a real model and the real formatter and asserts the **cell
text moves**. ⛔ It does not, and cannot, prove the production HOST supplies a provider; that is
exactly what §M's value-arrives probe is for.

---

## 10. ⭐ §12 — WHICH VISUAL-CHECK ROWS BECOME RUNNABLE

⭐ **`E2`–`E7` are now runnable** — a variable can be pinned *(both entry points)*, the pin tracks the
run, and the change highlight fires. ⛔ **The guide was not edited** *(the handoff reserves that)*.
⚠ **One caveat for whoever runs them:** the gesture is **refused while free-running** *(by design,
spec §7)* — ⭐ **pause first**, or the menu entry is greyed with that reason.
