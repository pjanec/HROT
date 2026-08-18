<!--STATUS
state: LIVE
updated: 2026-08-18
current-answer: §1–§5 (this whole file; it is a batch report, not a design)
stale-below: nothing
known-rot: none
known-conflict: none
-->

# REPORT — Batch 84: **items `0` → `2` → `3` → `4`** · ⭐⭐ **ALL FOUR LANDED**

> 📌 **Started at `3ef7f4b`** *(rule 1b marker, pushed before any code)*, on dispatch `d1e8a03`,
> handoff ff-merged at `76bf225`. ⭐ **Rule 4:** re-pulled and merged before this commit.
> ⭐ **IDs allocated: `BP-322` … `BP-330`.** ⭐ **`DEBT-AIB` rows touched: NONE.**
> ⭐ **Quarantine: 12 scenario · 0 FastHSM** — unchanged.
> ⭐⭐ **One commit per item, full gate set before each, pushed after each.**
> ⛔ **No visual check, and no claim that anything "renders correctly"** *(📌 `R-62`)* — rails only.

| item | commit | verdict |
|---|---|---|
| **0** — the two wiring defects | `593cdd8` | ✅ **landed** · `BP-322` `BP-323` |
| **2** — the staging entry point + the `+8` | `d4bd7bc` | ✅ **landed** · `BP-324` (+ `BP-325` filed) |
| **3** — the write while paused | `28cde74` | ✅ **landed** · `BP-326` (+ `BP-327` filed) |
| **4** — the two routing defects *(droppable)* | `7b6152c` | ✅ **landed** · `BP-328` `BP-329` |

⭐ **Nothing was left unreached.** ⛔ No item was stopped, split or deferred.

---

## 0. ⭐⭐⭐ The through-line: **every premise I was handed was checked, and three moved**

| premise | verdict |
|---|---|
| *"`IDataBreakpointManager` exposes whole-component `StageMutation` only"* | ⚠ **imprecise** — the 4-arg `StageMutation(…, baseline)` already sets `ByteOffset` by DIFFING. ⭐ What was missing is the **offset-addressed** shape |
| *"the `+8` is in two places"* | ⚠ **ten** — 2 in the editor *(routed)*, **8 inside `AiPrimitiveEmitter` as GENERATED SOURCE** *(⛔ not touchable without moving goldens ⇒ `BP-325`)* |
| *"4b: the selection is a SNAPSHOT"* | ⚠ **half true** — the **rows already followed the canvas**; ⛔ the **HEADING** was frozen ⇒ the panel **contradicted itself** |
| *"ruling 14: a whole-component write exceeds `MaxComponentSize`"* | ⛔ **FALSE** *(`R-65`)* — `1024 > 1024` is false. ⭐ **The real reason is SHARING**, and 🔴 **the false claim was in my own Batch 83 comment.** Corrected |
| *"`Q32` §2.1: bounds-check and fail LOUDLY"* | ✅ right — ⚠ **and the engine already checks**, at PLAYBACK. ⭐ The staging check moves the failure to **the designer's OK button** |

### 🔴🔴 And one finding about **my own previous batch**

📐 **`VariableEditCommit` shipped complete and tested in Batch 83 with ZERO production call sites.**
The gesture binder opened a dialog session and ⛔ **nothing ever committed it** ⇒ **even the
NOT-RUNNING write I reported as LANDED could not land.** ⚠⚠ **The twelfth instance of this
programme's shape — and exactly what `R-67` predicts of rails that construct the thing they assert
on.** ⭐ Item 3 built the missing `Accept()`.

---

## 1. ⭐ ITEM 0 — the composition root stops dropping shared services *(`BP-322` `BP-323`)*

| | |
|---|---|
| 🔴 **`0a`** | `facetEditService` → BTree `:2134` ✅ · HSM `:2158` ✅ · **Blueprint `:2162` ⛔ OMITTED**, 42 lines below where it was built ⇒ *"Edit value…"* and *"Properties…"* **dead on the perspective the user was looking at.** ⚠ The same list also dropped `expressionTargetFieldAccessor`, `aggregatorService`, `liveValueProvider` |
| 🔴 **`0b`** | run state came from `ActiveSession`, which `SyncActiveDebugSession` sets from **the active DOCUMENT's kind** ⇒ opening any blueprint read `Running` ⇒ ⛔⛔ **row 58's INITIAL arm was UNREACHABLE IN PRODUCTION** |

### ⭐⭐⭐ Fixed **structurally**, not by passing one more argument

📌 Batches 80, 82 and 83 each fixed one instance that way **and the fourth happened anyway** — because
*"forgot to pass it"* stayed **expressible**. ⇒ **`PerspectiveWorkspaceServices`** holds the shared
services **once** and **REQUIRES** the edit service and both clock signals; three argument lists became
**one construction path**. ⛔ The omission is no longer a thing a caller can write.

### ⭐⭐ The clock, and why the ORDER is load-bearing

```
isSimUp  ← IPreviewController.IsInPreviewMode
isFrozen ← IDataBreakpointManager.IsPaused OR IEngineDebugTimeController.IsPausedByDebugger
```
📐 **The editor BOOTS in `TimeMode.Deterministic`** ⇒ the freeze flag is **true on a dead editor**.
⛔ Deriving from it alone would report `Paused` — ⭐⭐ **the very state row `59c` lets a designer write
the live world in.** Railed as `FrozenWithTheSimDown_IsStillPlanning_NotPaused`.

### ⭐⭐⭐ §6a's anti-vacuity check — **and it caught one of MY rails**

| probe | result |
|---|---|
| un-pass `facetEditService` for Blueprint | ✅ **reddens 2**, and **only on `blueprint`** |
| make `ActiveSession` non-null with the sim down | ✅ **reddens 4** |

⚠⚠ **The obvious version of the run-state rail did NOT redden.** With no document open,
`ActiveSession` is null anyway, so `WithNoSimulationRunning_…ReadsPlanning` asserts the right thing
about **the wrong state**. ⭐ The real rail puts the editor in `R-66`'s exact state — **a session
active AND the sim down** — via a new internal `EditorSubsystem.AiDebugRegistry` hook. ⛔ **Found by
running the probe, not by writing the test.**

---

## 2. ⭐ ITEM 2 — `StageFieldMutation`, and one owner for the `+8` *(`BP-324`, `BP-325` filed)*

⭐ **Nothing was built in `Fdp.Core`** *(📌 `R-64`: `SetComponentFieldRaw` ships end to end)*.

```csharp
void StageFieldMutation(Entity, Type componentType, int byteOffset, ReadOnlySpan<byte> bytes)
```

| ⭐ decision | why |
|---|---|
| **additive** | ⛔ `StageMutation`'s whole-component behaviour untouched — it has a production caller |
| **the interface DEFAULT THROWS** | ⛔ **must not forward** — 📌 `R-65`: `Blackboard1024` is **ONE component shared by BTree, HSM and Blueprint at disjoint offsets** ⇒ the fallback clobbers them. ⚠ **Cite the sharing, never the size** |
| **bounds-checked, LOUD** | ⚠ the engine checks too, in `ComponentTable.SetRawAt` — ⛔ **at PLAYBACK**, one step later on the sim thread, where the row and the dialog are gone |
| **`WorkingStateLayout` owns the `+8`** | ⛔ the emitter's eight stay put: rewriting emitted text **moves the goldens**, which the handoff marks a **STOP** ⇒ `BP-325` |

⭐ **Composition rail:** three queued writes to two fields **all land, in order** — ⛔ a whole-component
write loses a field **and** an ordering, which are different failures, both asserted.

🔴 **Revert probes:** silent-drop bounds check ⇒ **4** · `HeaderBytes = 0` ⇒ **10**, of which ⭐⭐ **SIX
are PRE-EXISTING `AiPrimitiveStateMetadataTests`/`StateSnapshotTests`** ⇒ **the extraction is real, not
cosmetic.**

---

## 3. ⭐ ITEM 3 — the write while paused *(`BP-326`, `BP-327` filed)*

```
Planning ⇒ the declaration's JSON   Paused ⇒ the LIVE blackboard   Running / Replay ⇒ NOWHERE
```
📌 **Ruling 15** narrows ruling 7, and ⛔ **free-running refusing is a DECISION, not a later batch.**

⭐⭐ **`TargetFor` is DERIVED from `VariableValue.ModeFor`**, not written beside it ⇒ ⛔ the displayed
value and the write target cannot disagree about which arm is live. ⚠ The paused/free-running split is
layered **on top**, because `ModeFor` asks *"which value?"* and this asks *"may I, and where?"*.

⭐⭐ **A missing live writer is `LiveWriteUnavailable`, ⛔ not a quiet refusal** — the run state said yes
and the mechanism did not arrive; 📌 that is the silent-default shape and it earns its own word.

⭐ **The session stages** *(📌 `R-63`: a direct write to `ActiveView` is lost on resume)*, refuses unless
frozen, and adds the header through `WorkingStateLayout` ⇒ **the caller passes the LAYOUT's offset and
never its own `+8`.**

### ⭐⭐⭐ `R-55` **DISCHARGED** — ruling 12's gate, in an acceptance list at last

⭐ **Asserted:** one byte source, the value moves, **both surfaces render it with NO step and NO
resume.**
⛔ **NOT asserted, stated plainly:** the literal *"within one frame"* is the **host loop's tick**, which
no headless rail runs. ⭐ What makes it true is that both panels read through **ONE control and ONE
formatter**, so there is no second cache to go stale.

🔴 **Revert probes:** `TargetFor(Paused)⇒Nowhere` ⇒ **6** · session ignores `_isPaused` ⇒ **1** ·
`Accept` stops committing ⇒ **3**.

---

## 4. ⭐ ITEM 4 — the two routing defects *(`BP-328` `BP-329`)*

| | |
|---|---|
| **`4a`** | ⛔ the **TYPE** could not name the clicked row ⇒ nothing could be highlighted however it drew. ⭐ `SelectedVariablePath` + `IsSelected`, ⛔⛔ **kept OFF the change highlight** — §1b's red/yellow are statements about the **SIMULATION**, and mixing selection in would make the monitor **lie** |
| **`4b`** | ⭐⭐ **the symptom was worse than "Details does not follow the graph"** — the **rows** followed and the **label** did not, so the panel **contradicted itself** |

⭐ **`IVariableRowSource` was NOT reshaped** — the handoff said STOP AND REPORT if it needed to be; ⭐ it
did not, **because the rows were never the broken half.**

⚠⚠ **The existing rail could not have caught `4b`:** `SwitchingGraph_ChangesWhichLocalsTheClickResolvesTo`
switches graph **BEFORE** the click ⇒ it proves the resolver is live *at click time* and says nothing
about the published selection. ⭐ The new one switches **AFTER**.

🔴 **Revert probes:** freeze `CurrentHeading` ⇒ **2 + 1** · drop the selection in transit ⇒ **2**.

---

## 5. ⭐⭐ THE GATE TABLE — **the rule-8 contract, all seven rows, per item**

### ⭐ 1 · per gate, with Δ vs the Batch 83 baseline · ⭐⭐ 2 · the `--no-build` column

| gate | `--no-build`? | item 0 | item 2 | item 3 | item 4 | Δ vs baseline |
|---|---|---|---|---|---|---|
| `dotnet build IOS-IG-SimHost.sln` | — | 0 err | 0 err | 0 err | **0 err** | ⭐ see the warning note |
| `Hrot.Editor.AiShared.Tests` | ✅ | 1370 | 1370 | 1390 | **1397** | **+28** |
| `Hrot.Blueprints.Tests` | ✅ | 3752 | 3759 | 3769 | **3772** | **+35**, skipped **10** unchanged |
| `Hrot.BTree.Editor.Tests` | ✅ | 615 | 615 | 615 | **615** | — |
| `Hrot.Hsm.Editor.Tests` | ✅ | 551 | 551 | 551 | **551** | — |
| `Hrot.AiEditor.Generators.Tests` | ✅ | 270 | 270 | 270 | **270** | — |
| `Hrot.Diagnostics.Breakpoints.Tests` | ✅ | 134 | 143 | 143 | **143** | **+9** |
| `Hrot.AiEditor.Persistence.Tests` | ✅ | 136 | 136 | 136 | **136** | — |
| `Hrot.Editor.Tests` | ✅ | 194 | 194 | 194 | **194** | — |
| `Fdp.Examples.Scenarios.Tests` | ✅ | 56 | 56 | 56 | **56** | — · **12 skipped** |
| `Fdp.Examples.UrbanCombat.Tests` | ✅ | 29 | 29 | 29 | **29** | — |
| `Fdp.Toolkits.Tests` | ✅ | 1964 | 1964 | 1964 | **1964** | — |
| `NodeEditor.Core.Tests` | ⛔ **NO** | 211 | 211 | 211 | **211** | — |
| `NodeEditor.UI.Tests` | ⛔ **NO** | 135 | 135 | 135 | **135** | — |
| `Fhsm.Tests` | ⛔ **NO** | 300 | 300 | 300 | **300** | — |

⭐ **Verbatim:** `dotnet test <csproj> --no-build` for every ✅ row; ⛔ **no `--no-build`** for the three
out-of-solution projects *(they report a STALE BIN)*.

### ⭐⭐⭐ 3 · golden movement, as a DIFF SHAPE

⛔ **ZERO. No golden, no `persistence-shape.txt`, no `StructureHash`** moved in any of the four
commits — `git status` after every suite showed **only the files each item edited**. ⭐ **Expected:**
nothing here is compiler-side, and `BP-325` exists precisely to keep it that way.

### ⭐ 4 · every RED confirmed pre-existing — **there were NONE**

⭐⭐ **Zero red in all four runs, in every suite**, against base `76bf225`.
⛔⛔ **`DEBT-AIB-030` was NOT invoked**, and could not have been: `Fdp.Toolkits.Tests` was **1964/1964
green in all four runs.** ⚠ 📌 The handoff warned this batch CAN reach it — it did not fire.

### ⭐ 5 · the tree was CLEAN after every suite run · ⭐ 6 · quarantine **12 scenario · 0 FastHSM**, unchanged — ⛔ no new skip

### ⭐ 7 · canon + ids

| | |
|---|---|
| `python3 scripts/tracker-counts.py --check` | ✅ **open 68 / done 197 (+1 refuted)** |
| `python3 scripts/rulings-check.py` | ✅ **57/57** *(46/46 pre-merge; the coordinator's 11 new rows merged in)* |
| **ids allocated** | ⭐ **`BP-322` `BP-323` `BP-324` `BP-325` `BP-326` `BP-327` `BP-328` `BP-329` `BP-330`** |

⚠ **Two ledger repairs, both under *"find the new home, NEVER delete the ruling"*:**
**`R-67`'s probe quoted THE DEFECT LINE ITSELF** ⇒ ⛔ it could only stay green while the bug lived, and
it **failed the moment the bug was fixed**. ⭐ Re-pointed at the durable rule in `CLAUDE.md`.
**`R-64`'s text** *("whole-component `StageMutation` only")* corrected as above.

---

## 6. 🔴 THE RULE-4 RE-PULL — **one finding, REPORTED not adapted**

📌 **Scope was frozen at `d1e8a03`.** The coordinator pushed **11 commits after it** *(`Q40` watch
pinning, `R-69`–`R-79`, process changes)*. ⭐ **None invalidates a landed item** — `Q40` is about
**pinning**, which my handoff put explicitly out of scope *(`R-68`: "do not invent a gesture")*, and I
did not.

### ⚠⚠ But `R-72` names a gap my item 3 leaves open — **`BP-330`**

> 📌 **`R-72`, post-dispatch:** *"THERE ARE TWO WATCH WINDOWS."*

📐 **Measured:** `AiWatchWindow` *(shared registrar ⇒ **all three perspectives**)* keeps its
`VariableTableControl` **private with no accessor** ⇒ ⛔ **nothing can bind the edit gestures to it.**
⇒ ⭐ item 3 satisfied **ruling 11** for `WatchPanelWindow` *(blueprint-only)*, ⛔ **and the shared
window — the one BTree and HSM actually see — still cannot edit.**

⛔⛔ **NOT FIXED, deliberately:** 📌 *"documents that change after the dispatch sha are FYI ONLY… if a
later document INVALIDATES an item, STOP AND REPORT. ⛔ Do NOT adapt."* ⭐ **And it should not be a
one-line fix anyway** — it hardens a duplicate `Q40` may be about to collapse.

---

## 7. ⭐ What this leaves for you

| carried | |
|---|---|
| 🔴 **`BP-327`** | ⭐⭐ **the dialog has no OK BUTTON.** The whole write path is built and **reachable only from code** — the modal's visual half is suspended *(Batch 68)*. ⛔ Not fixable in a batch that may not do visual work |
| ⚠ **`BP-330`** | the shared `AiWatchWindow` cannot edit — sequence with `Q40`'s two-windows decision |
| ⚠ **`BP-325`** | the emitter's eight `memory + 8` — ⭐ **needs a batch that EXPECTS golden movement** |
| **`60` = `U-16`** | ⚠ `R-60`: BTree/HSM still have no Details window *(`BP-317`)* |
| **`61`** | the shared cross-host outline |
| **stage `D1`–`D4`** | ⛔⛔ own batch. 🔴🔴 **`R-24`: `D2` must preserve field order or every deployed blackboard is wiped** |
| 🔴 **`2.7`, `2.40`/`2.41`** | still NOT BUILT *(settled Batch 79)* |
| ⛔⛔ **parked** | `E3` · `E5` · `E7a` · `Q36` · `Q37` |
