<!--STATUS
state: LIVE
updated: 2026-08-19
current-answer: the whole file — Batch 93's report. The batch STOPPED; §2 is the reason.
stale-below: nothing
known-rot: nothing
known-conflict: §2 contradicts HANDOFF_Batch93 §2's value-feed premise; §4 contradicts R-77's
  "exists TWICE"; §5 contradicts HANDOFF_Batch93 §1's "CommandCatalog.ToggleWatch DOES NOT EXIST".
  All three are measured here; the handoff and R-77 are wrong on those points.
-->

# REPORT — Batch 93: **STOPPED at the value feed** *(watch pinning)*

> **Dispatched at** `ad9f1cd93` *(the handoff's own header; the commit carrying the handoff is
> `c4b85bf`)* · **started at** `de2c8d4` · scope frozen at the dispatch sha.
> **Base for every red:** `ad9f1cd93`.

---

## 1. ⭐ WHAT HAPPENED

| item | verdict |
|---|---|
| **`93a`** — the gesture + the mechanism | ⛔⛔ **STOPPED — `BP-344`.** §7's own stop condition is met |
| **`93b`** — each host supplies its data | ⛔ **NOT STARTED** — it is `93a`'s other half; §7: *"land both"* |
| **`93c`** — restart survival | ⛔ **NOT BUILT.** ⚠ Its one independently-correct piece was measured and **declined with a reason** — `BP-345`, §4 |
| ✅ **what DID land** | ⭐ **the measurement, as five permanent rails** — `APinnedRowIsASnapshotTests` |

**IDs allocated (rule 5): `BP-344`, `BP-345`, `BP-346`.**
**`DEBT-AIB` partitions touched: none.**

⚠ **User-visible consequence, stated plainly as §7 requires:** **a variable cannot be pinned to the
Watch panel at all** — and, separately, **a pin would not survive a scenario reload.**

---

## 2. ⛔⛔ THE STOP — **the value feed does NOT come free**

📄 **The handoff, verbatim (§2):** *"Batch 90's live arms are read PER FRAME, and a pinned row carries
its arm with it ⇒ a row pinned from a live Details row is live in the Watch with no new polling code."*

📄 **And §7, verbatim:** *"If the value feed does NOT come free from Batch 90's arms — STOP AND REPORT.
⛔ Do not build a poller on your own judgement; ⭐ that is a design question and it is mine to take to
the user."*

### 📐 The measurement — one live map, two surfaces, one frame apart

```
frame1 details value = 10
frame2 DETAILS value = 99        ⭐ the Details table IS live
frame2 WATCH   value = 10        ⛔ the pinned row is FROZEN at pin time
same row instance?  True         ⛔ PinnedVariableRowSource returns the record it was given
hand-built live-arm row = 99     ⭐ …but a row whose arm closes over the SOURCE stays live
```

### ⭐⭐⭐ The distinction the premise misses

The arms **are** invoked every frame. ⛔ **But the arm a row source builds closes over THAT FRAME'S
VALUE, not over the provider:**

| source | the arm it builds |
|---|---|
| `SectionVariableRowSource.ToRow:105` *(object)* | `var value = live![v.Name]; … readObject: () => value` |
| `SectionVariableRowSource.ToRow:118` *(bytes)* | `var bytes = cached; … () => bytes` |
| `BlackboardSectionRowSource.ToRow:81` | the same shape |

⇒ ⭐⭐ **Liveness in Details comes from REBUILDING THE ROW each frame**
*(`VariableTableModel.Build()` → `GetRows()`)* — ⛔ **not from the delegate.**
`PinnedVariableRowSource.GetRows()` returns its stored records untouched ⇒ ⭐ **a pinned row is a
snapshot taken at pin time.**

### ⭐⭐ What is NOT broken — and it narrows the fix sharply

A **hand-built** `VariableRow` whose arm closes over the source **does** stay live through the pinned
store, unchanged *(railed)*. ⇒ ⭐⭐⭐ **the gap is in the two row SOURCES — not in the store, the
window, or the table**, i.e. **nothing `93a`/`93b` was asked to build.**

### 📐 I SIZED THE CANDIDATE FIX RATHER THAN GUESSING — two probes, both un-applied

| probe | what it changed | result |
|---|---|---|
| **P1** | the **object** arm closes over `_liveObjects` instead of the frame's value | ⭐ **the pin goes live**, and **1489 of 1490** AiShared rails stay green — the only red is this batch's own characterization rail |
| **P2** | the **byte** arm reads lazily instead of capturing | ⭐ same: one red, **1489 green** |

⇒ ⭐ **The VALUE half is ~4 lines per arm and breaks nothing.**

### ⛔⛔ But the SECOND half is the real question — **`(pending)` freezes too**

📌 `BP-338` made `HasEverBeenWritten` a per-name, per-frame **measurement** — ⚠ but it is a **`bool` on
the record**, decided when the row is built. ⇒ ⛔ **a variable the run writes AFTER it was pinned reads
`(pending)` in the Watch forever**, while Details shows its value. **Railed**, and ⛔ **neither probe
fixes it.** ⚠ Guide row `C9` is about the opposite error; this is its mirror.

### ⭐⭐⭐ The decision this needs

> **Does a `VariableRow` MEAN *"this frame's values"* or *"an accessor onto a source"*?**

| | option | ⚠ cost |
|---|---|---|
| ⭐ **(a)** | both arms **and** `HasEverBeenWritten` become live closures | one meaning everywhere; ⚠ `HasEverBeenWritten` stops being a `bool` and every construction site changes |
| ⭐ **(b)** | keep the row a snapshot; give `PinnedVariableRowSource` a **re-resolve** step | ⛔ that is the per-tick poller §7 forbids me to invent, and it must respect `R-76`'s BINDING clock |
| ⛔ **(c)** | accept a frozen pin | **not viable** — a Watch panel that does not watch |

⭐ **My lean: (a).** It needs no new clock, it is what `R-76` already implies *(value per frame, binding
on selection change)*, and P1/P2 show its value half is nearly free. ⛔ **Not built.**

---

## 3. ⛔ WHY THE GESTURE WAS NOT BUILT EITHER

⚠ `93a`'s gesture is independent of the value feed **as code**, and I considered landing it alone.
⛔ **It would have shipped a defect**: a pin gesture whose artefact is a **frozen value that looks live
for exactly one frame** is the programme's signature failure with an extra twist — it is worse than a
missing way-in, because it reads as working. 📌 The handoff itself pairs them: *"`93a` + `93b` — the
whole visible feature."*

---

## 4. ⚠⚠ `BP-345` — **`R-77`'s state claim has ROTTED: FOUR, not two**

📄 **`R-77` verbatim** *(`Architect_Question_40…md:486`)*: *"`FindEntityByNetworkId` exists **TWICE** —
`ReplayBrowserSubsystem:933` and `EditorMissionService:54` … this design needs a third caller ⇒ ⛔ do
not add a fourth copy."*

📐 **Enumerated with `search_graph`** *(⛔ not grep — 📌 `R-74`)* — **`total: 4`**:

| # | where | query | read | null repo |
|---|---|---|---|---|
| 1 | `EditorMissionService:54` | `.With<NetworkIdentity>()` | ⛔ `GetComponent` | ⛔ unguarded |
| 2 | `ReplayBrowserSubsystem:933` | ⛔ everything + `HasComponent` | ⭐ `GetComponentRO` | ⭐ guarded |
| 3 | ⭐ **`EditorSubsystem:3869`** — **not in `R-77`** | `.With<NetworkIdentity>()` | ⛔ `GetComponent` | ⛔ unguarded |
| 4 | ⭐⭐ **`MapPickServiceBridge:121`** — **not in `R-77`** | ⭐⭐ **a CACHED `_networkQuery`** | ⭐ `GetComponentRO` | ⭐ guarded |

⇒ ⭐ **A watch-pinning caller would be the FIFTH**, and *"ten lines, twice"* is **four sites to
migrate**. ⭐⭐ **And neither of `R-77`'s candidates is the best one** — **#4 caches its query** where
the other three rebuild it per call; the handoff's comparison table could not see it because it never
enumerated.

⛔ **NOT unified here.** The stated reason to unify was *"this design needs a third caller"* — and
`BP-344` means this batch adds **no caller at all**. ⇒ ⭐ four sites across three subsystems outside
this batch's fence, for no new consumer, is not a change I will make on my own judgement. ⭐ **The
intent stays right; only the count was wrong.**

---

## 5. ⚠ `BP-346` — **`CommandCatalog.ToggleWatch` exists, and means something else**

📄 The handoff: *"**`CommandCatalog.ToggleWatch` DOES NOT EXIST**."*
📐 **Measured** *(`search_graph`, `total: 7`)*: `NodeEditor.Core/CommandCatalog.cs:75` —
`public const string ToggleWatch = "editor.toggle-watch";` — plus `IDebugSession.ToggleWatch(PinId)`
with a **real implementation** at `BlueprintDebugToNodeEditAdapter:140`.

⭐⭐ **But it is PIN-scoped** — a canvas `PinId`, ⛔ not a variable row. ⇒ **the premise is false while
the conclusion is true**: the *variable* gesture is genuinely unbuilt. ⚠ **The trap:** the next
implementer reaches for the existing constant and silently binds the variable gesture to the pin-watch
command. ⇒ ⭐ **use a distinct id**, or first rule that the two watches are one concept.

✅ **`CanvasRenderer:684` confirmed exactly as described** — `MenuItem("Watch this Value")` inside
`BeginDisabled()`, no handler, in the **pin** menu beside *"Promote to Variable…"*. ⇒ 📌 the handoff's
own instruction applies — *"if a pin does not map cleanly to a variable row, LEAVE IT DISABLED and say
so."* ⭐ **Left disabled.**

---

## 6. ⭐⭐ GATES — the seven-row contract

| # | gate | command | result | Δ | `--no-build`? |
|---|---|---|---|---|---|
| 1 | AiShared | `dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests --no-build` | **1490 / 0 / 0** | **+5** *(the measurement rails)* | ✅ |
| 2 | BTree.Editor | `--no-build` | **622 / 0 / 0** | 0 | ✅ |
| 3 | Hsm.Editor | `--no-build` | **554 / 0 / 0** | 0 | ✅ |
| 4 | AiEditor.Generators | `--no-build` | **277 / 0 / 0** | 0 | ✅ |
| 5 | AiEditor.Persistence | `--no-build` | **143 / 0 / 0** | 0 | ✅ |
| 6 | Blueprints | `--no-build` | **3773 / 0 / 10 skip** | 0 | ✅ |
| 7 | Hrot.Editor | `--no-build` | **201 / 0 / 0** | 0 | ✅ |
| 8 | Breakpoints | `--no-build` | **143 / 0 / 0** | 0 | ✅ |
| 9 | NodeEditor.Core | *(built)* | **211 / 0 / 0** | 0 | ⛔ **NO — stale bin** |
| 10 | NodeEditor.UI | *(built)* | **135 / 0 / 0** | 0 | ⛔ **NO — stale bin** |
| 11 | Fhsm | *(built)* | **300 / 0 / 0** | 0 | ⛔ **NO — stale bin** |
| 12 | Fdp.Presentation *(`BP-337`)* | `--filter "…WindowManager"` | **146 / 0 / 0** | 0 | ✅ |

⛔ **`Fdp.Toolkits.Tests` NOT RUN** — 📌 `DEBT-AIB-030`.
⭐ **No RED anywhere** ⇒ nothing to confirm pre-existing against `ad9f1cd93`.
⭐ **Working tree CLEAN after every suite run**; both probes verified un-applied by
`git diff --name-only` returning **empty**.
⭐ **Quarantine counts unchanged**: Blueprints **10 skipped**, everything else **0**. ⛔ **No new skip.**

### ⭐ 7b — the scripts, UNFILTERED, with `EXIT`

```
$ python3 scripts/tracker-counts.py --check
TRACKER COUNTS DISAGREE WITH THE ROWS: … Total: table says open=69 done=209, rows say open=72 done=209
EXIT=1                     ⭐ EXPECTED — the summary table is DERIVED; this is the script working

$ python3 scripts/tracker-counts.py --check      # after applying the corrected table
tracker counts OK — open 72 / done 209 (+1 refuted)
EXIT=0

$ python3 scripts/rulings-check.py
67/67 rulings verified against their sources
WARN 1 cited source(s) changed after the ledger was last updated: docs/blueprints/PLAN_Remaining_Work.md
EXIT=0                     ⭐ the WARN is PRE-EXISTING (the coordinator's own Batch-93 plan edit)
```

⚠⚠ **And note what the gate CANNOT see:** `R-77` passes `rulings-check.py` — its quote still exists
verbatim — while being **factually wrong about the code** *(§4)*. 📌 **That is exactly the
`⛔⛔ THE LEDGER MAY NOT ASSERT WHAT THE CODE IS` failure**: a **STATE CLAIM** in a ruling rots
silently. ⇒ ⭐ **`R-77` belongs in §M as a question with a command, not in the canon as a count.**

---

## 7. ⭐⭐ GATE 8 — GOLDEN

⛔ **Nothing here touches emission**, and nothing moved: the only file added is a test.
`MigrationEquivalenceTests` and both golden corpora green inside gates 4 and 6; **zero golden files
modified** *(`git status` shows one untracked test file and nothing else)*.

---

## 8. ⭐⭐⭐ GATE 9 — THE ENUMERATION *(`R-74` — `search_graph`, not grep)*

| | before | after |
|---|---|---|
| **production** callers of `PinnedVariableRowSource.Pin` | **0** *(only `TrackCWiringTests:235`, `WatchPinnedSourceTests`)* | ⛔ **0** — this batch adds none |
| `FindEntityByNetworkId` implementations | **4** *(§4)* | **4** — ⭐ **no fifth added**, which is `R-77`'s intent even though its count was wrong |
| `ToggleWatch` symbols | **7** *(1 const · 1 interface · 3 impls/stubs · 2 nodes)* | **7** — ⛔ unchanged |
| `CanvasRenderer:684` `"Watch this Value"` | disabled, no handler | ⛔ **still disabled** — deliberately |

---

## 9. ⭐⭐⭐ GATE 10 — WHAT EACH RAIL ASKS

⛔⛔ **No rail asserts that `Pin` was called.** ⭐ Each asks the **artefact** — the value a Watch row
would render.

| rail | it asks |
|---|---|
| `ARowPinnedFromTheDetailsSourceFreezesAtPinTime` | ⭐⭐⭐ Details reads **99**, the pinned row reads **10**, one frame later. ⚠ **Asserts the DEFECT on purpose** — it is the acceptance test for the fix, and is meant to be **INVERTED**, ⛔ never deleted |
| `TheByteArmFreezesOnTheSameRule` | ⛔ not object-arm-only, not Blueprint-only |
| `PendingFreezesToo_SoAVariableWrittenAfterPinningNeverUnpends` | ⭐⭐ the second half — `(pending)` never clears in the Watch |
| `AHandBuiltRowWithALiveArmStaysLiveWhenPinned` | ⭐⭐⭐ **the store and the row type are FINE** — this is what narrows the fix to the two sources |
| `ThePinnedStoreReturnsTheSameRecordItWasGiven` | why the freeze is total rather than partial |

---

## 10. 🔴 GATE 11 — REVERT-GOES-RED *(inverse edit only; ⛔ never `git checkout --`)*

⚠ **This batch built no feature, so the probes run the other way**: they **un-apply the DEFECT** and
show the characterization rails flip — which is what proves they are not vacuous.

| # | probe | red | ⭐ what it proves |
|---|---|---:|---|
| **P1** | object arm closes over `_liveObjects` | **1** | ⭐ exactly `ARowPinnedFromTheDetailsSourceFreezesAtPinTime` flips 10 → 99; **1489 other rails stay green** |
| **P2** | byte arm reads lazily | **1** | ⭐ exactly `TheByteArmFreezesOnTheSameRule` flips; **1489 green** |
| ⛔ **neither** | — | — | ⭐⭐ **`PendingFreezesToo` stays GREEN under both** — the `(pending)` half is a separate decision, not a side effect |

⭐ Both probes un-applied by inverse edit; `git diff --name-only` afterwards: **empty**.

---

## 11. ⭐ §8 — WHICH VISUAL-CHECK ROWS BECOME RUNNABLE

⛔ **None.** `E2`–`E7` all depend on pinning a variable to the Watch, and ⭐ **no pin gesture exists.**
⚠ They stay **SKIP**. ⛔ **The guide was not edited** *(the handoff reserves that)*.
