<!--STATUS
state: LIVE
updated: 2026-08-18
current-answer: section 4 - APPROVED IN FULL by the user 2026-08-18 ("Q44 approved
  using your recommendations"). A and B were ruled by the user; C-F approved as
  recommended. Nothing here is built.
stale-below: nothing.
known-rot: none.
known-conflict: none. Q38 owns the DETAILS/variable family; this owns the
  BREAKPOINT family. Section 5 states the boundary.
-->
# ⭐ Architect Question 44 — **breakpoint UI unification**

> # ✅✅✅ APPROVED IN FULL — user, `2026-08-18`: *"Q44 approved using your recommendations."*
> ⭐ `A` and `B` were the user's own rulings; ⭐ **`C`–`F` approved as recommended.** ⛔ **Canon now** — `R-97`.
>
> ⛔⛔ **NOT RELAYED.** The architect is generally unavailable *(`2026-08-16` user ruling)*.
> ⭐⭐ **I analyse and RECOMMEND, the user APPROVES.**
>
> 📌 **Opened by the user, `2026-08-18`, on measuring `Q38`'s watch family:**
> ⭐ *"if `IsWatch` is only used now to see the hit count then i would say it naturally belongs to the
> breakpoint list row. And the databreakpointwindow should not be separate from another breakpoint
> windows — data breakpoint is still just a breakpoint so it belongs to one single breakpoint window
> listing all types (maybe with some filtering if useful). the breakpoint UI unification is a new area,
> new architect question."*
>
> ⭐⭐ **`A` and `B` below were RULED by the user, not proposed.** ✅ **`C`–`F` are now APPROVED too** *(`R-97`)*.

---

## 1. ⭐⭐ INVENTORY *(`R-74` — the graph, `2026-08-18`)*

```
search_graph(name_pattern=".*(Breakpoint|Watch).*(Window|Panel|Manager|Entry|Kind)$")   → total 51
```

### ⭐⭐⭐ FOUR breakpoint kinds — **not the two the question assumed**

| # | kind | identity | persisted as |
|---|---|---|---|
| 1 | **data breakpoint** | a **predicate** *(`SearchPredicateDto`)* + `Enabled` · `HitCount` · `IsBroken` | `DataBreakpointEntry` |
| 2 | **node breakpoint** | `(assetId, graphId, nodeId)` — ⭐ set from the **gutter** | `NodeBreakpointEntry` |
| 3 | ⚠ **"watch"** | ⛔⛔ **NOT A KIND — a `bool IsWatch` ON kind 1** | `WatchEntry` *(+ legacy `WatchPersistenceEntry`)* |
| 4 | 🔴 **one-shot / run-to-cursor** | ⛔ **deliberately INVISIBLE** — 📐 `BlueprintDebugSession:55`/`:519`: *"Cleared on hit or on `Continue()`. **Not exposed via `GetBreakpoints()`**, not forwarded to DBM, and auto-cleared on first hit"* | ⛔ **not persisted** |

### ⭐ The surfaces

| surface | lines | what it does |
|---|---|---|
| **`DataBreakpointManagerWindow`** → **`DataBreakpointManagerPanel`** | 18 → **225** | ⭐ **the only real management UI** |
| 🔴 **`AiBreakpointsWindow`** | **34** | ⛔ **a COUNT BANNER** — *"N active breakpoint(s). Open the global Data Breakpoints window for full management."* |
| **`AiWatchWindow`** | 107 | ⭐ **lists the `IsWatch` subset** — ⚠ **the breakpoint LIST lives here** |
| **per-host gutter + context menu** *(`BTree`/`Hsm` `BreakpointGutterRenderer` · `BreakpointContextMenuProvider`)* | — | ⭐ **SETTING a breakpoint** — ⛔ a different job from listing |

### ⭐ The store

**`IDataBreakpointManager`** *(in-degree **47**)* / **`DataBreakpointManager`** *(**1120** lines)* — one
store. ⭐ `BlueprintDebugSession` keeps **node** breakpoints and **forwards** them in
*(`SetDataBreakpointManager`)*.

---

## 2. ⭐⭐⭐ THE FINDING THAT OPENED THIS

📐 **Every non-test use of `IsWatch`:** the record · the save filter · three setters · two DTO sites ·
the restore path · `AiWatchWindow`'s read. ⛔⛔ **NOT ONE is in an evaluation or hit path.**

⇒ ⚠⚠ **An `IsWatch` breakpoint STILL BREAKS.** ⭐ It is *"a breakpoint that is also listed in the Watch
panel"* — ⛔ **not the non-breaking observer the word promises.**

⇒ 📌 **The user's ruling follows directly:** if the flag only buys *"show me its hit count"*, ⭐ **that
is a COLUMN on the breakpoint row**, not a second window.

---

## 3. ⭐ What binds any answer

| id | binds |
|---|---|
| **ruling 9** | ⛔ no two implementations of one concept |
| **`R-72`** | ⚠ the watch family was miscounted **twice** — ⭐ count with the graph |
| **`R-95`** | ⭐ the focused-surface model — ⛔ a breakpoint list is **not** focus-following |
| **`R-27`** | ⚠ `Q38` is gated on the visual check; ⭐ **this question inherits that gate** |
| **"no rush removals"** | ⛔ a surface goes only after its capability lands elsewhere |

---

## 4. ⭐⭐⭐ THE SUB-QUESTIONS

### ✅ `Q44-A` — one breakpoint window for all kinds? — **RULED**

⭐⭐⭐ **YES** *(user)*. ⭐ **ONE list, all kinds, with a KIND column and optional filtering.**
⛔ *"Data breakpoint is still just a breakpoint."*

| ⭐ recommended shape | |
|---|---|
| ⭐⭐ **keep `DataBreakpointManagerPanel` as the base** | **225 lines of real management UI** vs a 34-line banner — ⭐ **generalise the one that exists** *(the same call `Q38-D` made)* |
| ⭐ **`AiBreakpointsWindow` hosts THAT panel** | ⇒ ⛔ **the banner and its *"open the other window"* dead end both die** |
| ⭐ **`Kind` column + filter** | ⚠ **filter, ⛔ not tabs** — 📌 tabs would re-split what this question is merging |

### ✅ `Q44-B` — what happens to `IsWatch`? — **RULED**

⭐⭐⭐ **RETIRE it as a separate concept.** ⭐ **Hit count becomes a COLUMN on the breakpoint row**, where
it already conceptually lives.

| ⚠ but three things must be decided WITH it | |
|---|---|
| **the `watches.json` file** | ⭐ **legacy `WatchPersistence` writes only `IsWatch` rows.** ⛔ **Do not silently drop it** — ⭐ migrate: every `WatchEntry` becomes a plain breakpoint |
| ⭐⭐ **the Watch window then holds VARIABLES ONLY** | ⇒ 📌 **`Q38`'s three-feed merge becomes a homogeneous TWO-feed merge** — ⭐ **this question makes `Q38`'s cheaper, not harder** |
| ⚠ **the name** | ⛔ **"watch" must stop meaning "a breakpoint that breaks"** — ⭐ after this it means **only** *"a variable I am observing"* |

### ⭐⭐ `Q44-C` — is a node breakpoint the same ROW as a data breakpoint?

| ⭐⭐⭐ **RECOMMENDED: ONE row type with a `Kind` discriminator and a kind-specific "Location" cell.** |
|---|

⭐ They share **`Enabled` · `HitCount` · `IsBroken` · `DisplayName`** — ⛔ they differ only in **what
identifies them** *(a predicate vs `(asset, graph, node)`)*.
⚠ **That is one row with a polymorphic cell, ⛔ not two tables** — 📌 exactly the shape
`VariableRow` already proved for variables.
**Blast radius: LOW** — ⭐ `Breakpoint` already carries `SourceElementId` for the node case.

### ⭐⭐ `Q44-D` — does the one-shot / run-to-cursor kind appear in the list?

| ⭐⭐⭐ **RECOMMENDED: NO — and say so in the list's empty/■ state rather than silently omitting it.** |
|---|

📐 It is **deliberately** not in `GetBreakpoints()`, not forwarded, auto-cleared on first hit.
⭐ **Listing a row that vanishes on the next step would read as a bug.**
⚠ **But an invisible mechanism the user can trigger is exactly this programme's recurring shape** ⇒
⭐ **a transient indicator *(e.g. the gutter marker only)* is the honest middle.**

### ⭐ `Q44-E` — does SETTING a breakpoint move?

| ⭐⭐⭐ **RECOMMENDED: NO. The gutter + context menu stay per-host and untouched.** |
|---|

⭐ *"Set a breakpoint here"* is a **canvas** gesture about a **node**; *"list my breakpoints"* is a
**management** view. ⛔ **Different questions** — 📌 the same criterion `Q38-C` uses.

### ⭐⭐ `Q44-F` — sequencing against `Q38`

| ⭐⭐⭐ **RECOMMENDED: `Q44-B` FIRST, before `Q38-E` step 1.** |
|---|

📌 **`Q38-E` step 1 is *"collapse the two watch windows"*.** ⚠ **Doing that while the breakpoint rows
still live in the Watch window merges a heterogeneous surface.**
⇒ ⭐⭐ **Send the breakpoint rows home first; then the watch merge is variables-only and trivial.**
⛔ **`Q44-A`/`C` can follow at their own pace** — ⭐ only `B` is on `Q38`'s critical path.

---

## 5. ⛔ THE BOUNDARY WITH `Q38`

| owns | |
|---|---|
| ⭐ **`Q38`** | the **Details / variable** family — one chameleon + pinnable instances |
| ⭐ **`Q44`** | the **breakpoint** family — one list, all kinds |
| ⭐⭐ **the seam** | **`IsWatch`** — ⛔ **the ONLY object both questions touch**, and `Q44-B` removes it from `Q38`'s side |

⚠ **`R-27`'s gate applies here too** — ⛔ **do not build either before the post-Batch-88 visual check.**
