<!--STATUS
state: LIVE
updated: 2026-08-19
current-answer: section 4 - RECOMMENDED ANSWERS, awaiting the user's approval.
stale-below: nothing.
known-rot: none.
known-conflict: none. It answers what DESIGN_Variable_Watch_Pinning.md cannot be built
  without, and what Batch 93 stopped on.
-->
# ⭐ Architect Question 46 — **what does a `VariableRow` MEAN?** *(`BP-344`)*

> ⛔⛔ **NOT RELAYED.** The architect is generally unavailable *(`2026-08-16` user ruling)*.
> ⭐⭐ **I analyse and RECOMMEND, the user APPROVES.**
>
> 📌 **Opened `2026-08-19`, by Batch 93 STOPPING** — on the stop condition my own handoff wrote.
> ⛔⛔ **And on a premise I got wrong:** I said Batch 90's live arms meant *"a pinned row carries its
> arm with it ⇒ live in the Watch with no new polling code."* ⭐ **Measured false.**

---

## 0. ⭐⭐ INVENTORY *(`R-74` — the graph, `2026-08-19`)*

```
search_graph(name_pattern=".*(VariableRow|RowSource|ReadValueObject|ReadRawValue|HasEverBeenWritten).*",
             file_pattern="Hrot/**")                                          → total 27
grep -rn "HasEverBeenWritten" --include=*.cs (excl obj/bin/.dev)              → total 31
grep -rn "new VariableRow(" --include=*.cs Hrot/ | grep -v Tests              → total 2 files
```

### ⭐ The FOUR row sources — **only two are affected**

| # | source | affected? |
|---|---|---|
| ⭐ **1** | **`SectionVariableRowSource`** *(Details, blueprint)* — the **object** arm at `:105`, the **byte** arm at `:118` | ⛔ **YES** |
| ⭐ **2** | **`BlackboardSectionRowSource`** *(Details, AI)* — the same shape at `:81` | ⛔ **YES** |
| **3** | `FixedVariableRowSource` | ⭐ **no** — the caller supplies finished rows |
| **4** | `PinnedVariableRowSource` *(Watch)* | ⭐ **no** — it stores what it is given; ⭐⭐ **that it does so is CORRECT** |

### ⭐⭐ Who sets `HasEverBeenWritten` — **the cost of `Q46-B`, enumerated**

| | count | where |
|---|---:|---|
| ⭐ **production construction sites** | **3** | `WatchRowBridge:58` · `BlackboardSectionRowSource:101` · `VariableRowSources:138` *(the shared `NewRow`)* |
| **the record's own default** | 1 | `VariableRow.cs:115` |
| **readers** | 2 | `VariableValueFormatter:88`, `:111` |
| ⚠ **test mentions** | ~28 | ⭐ **the reason `Q46-B` recommends an OPTIONAL arm over a type change** |

---

## 1. ⭐⭐ THE MEASUREMENT *(Batch 93, five permanent rails)*

```
frame1 details value = 10
frame2 DETAILS value = 99        ⭐ the Details table IS live
frame2 WATCH   value = 10        ⛔ the pinned row is FROZEN at pin time
same row instance?  True
hand-built live-arm row = 99     ⭐ …a row whose arm closes over the SOURCE stays live
```

### ⭐⭐⭐ The distinction I missed

The arms **are** invoked every frame. ⛔ **But the arm a row SOURCE builds closes over THAT FRAME'S
VALUE, not over the provider:**

```csharp
var value = live![v.Name];  …  readObject: () => value      // SectionVariableRowSource:105
var bytes = cached;         …  ()          => bytes         // …:118, BlackboardSectionRowSource:81
```

⇒ ⭐⭐ **Liveness in Details comes from REBUILDING THE ROW each frame** *(`VariableTableModel.Build()`
→ `GetRows()`)* — ⛔ **not from the delegate.** `PinnedVariableRowSource.GetRows()` returns its stored
records untouched ⇒ ⭐ **a pinned row is a snapshot taken at pin time.**

### ⭐⭐ What this NARROWS — and it is the useful half

⭐⭐⭐ **The store, the window, the table and the row TYPE are all fine.** A **hand-built** row whose arm
closes over the source stays live through the pinned store *(railed)*.
⇒ ⛔ **the gap is in the two row SOURCES** — **nothing `93a`/`93b` was asked to build.**

### ⛔⛔ AND A SECOND HALF THE VALUE FIX DOES NOT TOUCH

📌 `BP-338` made `HasEverBeenWritten` a per-name, per-frame **measurement** — ⚠ **but it is a `bool` on
the record**, decided when the row is built. ⇒ ⛔ **a variable the run writes AFTER it was pinned reads
`(pending)` in the Watch forever**, while Details shows its value. **Railed.** ⚠ Guide row `C9` is
about the opposite error; **this is its mirror.**

---

## 2. ⭐ THE QUESTION

> ### ⭐⭐⭐ Does a `VariableRow` mean *"this frame's values"*, or *"an accessor onto a source"*?

⭐ **Today it is BOTH, inconsistently** — the record carries delegates *(accessor-shaped)* whose bodies
capture values *(snapshot-shaped)*. ⛔ **That ambiguity is the defect**, not either answer.

---

## 3. ⭐ What binds any answer

| id | binds |
|---|---|
| ⭐⭐ **`R-76`** | ⛔ **TWO CLOCKS:** VALUE every frame · **BINDING only on selection change.** ⛔ Re-resolving a binding per tick churns row identity under the cursor |
| **`BP-338`** | ⭐ `(pending)` is a **per-name, per-frame measurement** — ⛔ never *"a reader exists"* |
| **ruling 9** | ⛔ one implementation per concept — ⛔ **not two kinds of row** |
| **`R-49`** | ⛔ no per-variable codegen |
| ⚠ **spec §10** | ⛔ **watching variables from DIFFERENT assets in one panel is OPEN** — *"the poll would span debug sessions"* |

---

## 4. ⭐⭐⭐ THE SUB-QUESTIONS — **with recommended answers**

### ⭐⭐⭐ `Q46-A` — which meaning wins?

| ⭐⭐⭐ **RECOMMENDED: *"an ACCESSOR onto a source."* The arms become live closures.** |
|---|

| option | verdict |
|---|---|
| ⭐⭐⭐ **(a) live closures** | ⭐ **one meaning everywhere**, no new clock, and it is what `R-76` already implies *(value per frame)*. 📐 **Batch 93 sized the value half: ~4 lines per arm, and 1489 of 1490 AiShared rails stay green** |
| **(b) keep the snapshot; `PinnedVariableRowSource` RE-RESOLVES each frame** | ⚠ **superficially the tidier model** — it is exactly what Details does *(rebuild per frame)* — ⛔ **but the Watch mixes ARBITRARY assets and entities**, so re-resolving needs a source per `(asset, entity)` ⇒ ⭐⭐ **it walks straight into spec §10's open question**, which is fenced out. ⛔ **Not now** |
| ⛔ **(c) accept a frozen pin** | ⛔⛔ **not viable — a Watch panel that does not watch** |

⚠ **Note for later:** ⭐ **(b) is the better END state** once §10 is answered. ⭐ **(a) does not block
it** — a live closure is a degenerate accessor. ⛔ **Do not build (b) now.**

### ⭐⭐⭐ `Q46-B` — how does `HasEverBeenWritten` follow?

| ⭐⭐⭐ **RECOMMENDED: an OPTIONAL trailing delegate arm that WINS when present — ⛔ not a type change.** |
|---|

⚠ **Batch 93 costed this as *"`HasEverBeenWritten` stops being a `bool` and every construction site
changes."*** 📐 **Measured `2026-08-19`: THREE production sites set it** — `WatchRowBridge:58` ·
`BlackboardSectionRowSource:101` · `VariableRowSources:138` — ⚠ **but ~28 TEST sites also name it.**

⇒ ⭐⭐⭐ **Use the shape Batch 90 already established for `ReadValueObject`:** a **trailing, `null`-by-
default** `ReadHasEverBeenWritten` that is **preferred when present**.
⭐ **Zero existing construction sites change**, production or test. ⭐ **One precedent, not a new idiom**
*(ruling 9)*. ⛔ **Do not widen the `bool` into a delegate.**

### ⭐⭐ `Q46-C` — do BOTH arms change, or only the object arm?

| ⭐⭐⭐ **RECOMMENDED: BOTH — object and bytes.** |
|---|

⛔ **A fix on one arm would make pinning work on Blueprint and silently freeze on BTree/HSM** —
📌 exactly the split `U-6` removed. ⭐ **Batch 93's `P2` already showed the byte arm is the same 4 lines.**

### ⭐ `Q46-D` — what happens to the characterization rails?

| ⭐⭐⭐ **RECOMMENDED: INVERT them, never delete them.** |
|---|

⭐ `APinnedRowIsASnapshotTests` **asserts the defect on purpose** and says so. ⇒ ⭐⭐ **it is the
acceptance test for this fix** — ⛔ deleting it would remove the only proof the fix works.

### ⚠ `Q46-E` — the `ToggleWatch` id *(`BP-346`)*

| ⭐⭐⭐ **RECOMMENDED: a DISTINCT command id for the variable gesture.** |
|---|

📐 `CommandCatalog.ToggleWatch = "editor.toggle-watch"` **exists** and is **PIN-scoped**
*(`IDebugSession.ToggleWatch(PinId)`, implemented at `BlueprintDebugToNodeEditAdapter:140`)*.
⛔ **My handoff said it did not exist — false**; ⭐ **the conclusion held: the VARIABLE gesture is
unbuilt.** ⚠ **The trap Batch 93 names: the next implementer reaches for the existing constant and
silently binds the variable gesture to the pin-watch command.**
⇒ ⭐ **Distinct id now.** ⚠ **Whether a pin-watch and a variable-watch are ONE concept is a `Q38`/`Q44`
question** *(the third watch feed)* — ⛔ **`R-27` gates it; do not settle it here.**

---

## 5. ⭐ What this costs if approved

⭐ **Small — and Batch 93 already proved most of it:** two arms × ~4 lines *(probed, 1489 green)* + one
optional trailing delegate + inverting five rails. ⇒ ⭐⭐ **then `93a`/`93b` become buildable as written.**
