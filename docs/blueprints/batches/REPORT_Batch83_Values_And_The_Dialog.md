# REPORT — Batch 83: **rows `58` → `59` → `59b`** · ⭐⭐ **long unattended run, ALL THREE LANDED**

> 📌 **Started at `0f475af`** *(rule 1b marker, pushed before any code)*, on dispatch `14881c13f`
> ff-merged at `b2317c9`. ⭐ **Rule 4:** re-pulled before the final commit.
> ⭐ **IDs allocated: `BP-319` `BP-320` `BP-321`** · ⭐ **and `BP-01` CLOSED.**
> ⭐ **`DEBT-AIB` rows touched: NONE.** ⭐ **Quarantine: 12 scenario · 0 FastHSM** — unchanged.
> ⭐⭐ **One commit per item, full gate set before each, pushed after each** *(protocol rules 1 & 5)*.
> ⛔ **No visual check, and no claim that anything "renders correctly"** *(rule 8)* — rails only.

| item | row | commit | verdict |
|---|---|---|---|
| **1** | `58` — the Value column | `91b8dc4` | ✅ **landed** |
| **2** | `59` — the StructEdit dialog | `61cd666` | ✅ **landed**, fork resolved ✅ |
| **3** | `59b` — the Watch panel | `d898df1` | ✅ **landed**, and **`BP-01` closed** |

⭐ **Nothing was left unreached.** ⛔ No item was stopped, split or deferred.

---

## 0. ⭐⭐⭐ The through-line: **every item was WIRING, not construction**

📐 **All three MEASURE-FIRSTs found the same thing** — the component existed, complete and tested, and
**nothing in production called it**:

| item | what already shipped | 🔴 what was measured |
|---|---|---|
| **58** | `VariableRunState`, the formatter, elision, the pretty-printed tooltip, `(pending)`/`<unreadable>` | **`RunState` was set by NOTHING in production** *(tests only)*, and the INITIAL arm had **no source at all** ⇒ the column had exactly **one** meaning |
| **59** | `VariableEditLauncher`, `VariableEditGestureBinder`, `VariableEditPolicy`, both `EditScope`s | ⛔⛔ **constructed ONLY IN TESTS — zero production call sites** ⇒ **the eleventh instance** |
| **59b** | `MarshalFromBytes`, the shared table, the shared formatter | `HandlePinValueChanged` was **an empty body**; the panel rendered **raw hex** — ⛔ **`BP-01`, still live** |

⇒ ⭐⭐ **The pattern is now eleven deep, and this batch closed three of them in one run.**

---

## 1. 🛠 Item 1 — row `58`, the Value column *(`BP-319`)*

📌 **ruling 3:** *"ONE Value column, meaning switched by run state — **initial** when not running,
**current** when running or paused"* · ⛔ *"the coordinator argued two columns and is **overruled**."*

| ⭐ built | |
|---|---|
| **`VariableValue.ModeFor`** | ⭐ **THE one place** that answers *"initial or current?"* — ⛔ not a bool per call site. ⚠ **`Replay` reads CURRENT**, per ruling 3's *"live / replay / preview"*: showing a declared default over recorded data would mislabel it as a plan |
| **`RunStateSource`** | ⭐⭐⭐ **DERIVED** from the `IDebugSessionRegistry` the registrar **already holds** ⇒ ⛔ not a new argument the composition root can forget |
| **`VariableRow.ReadInitialJson`** | fed by **all three real sources** — blackboard entries, blueprint declarations, graph locals |
| the view | ⭐ **ONE resolved mode per `Build()`**, not one per cell |

### ⚠ Two judgement calls I had to make unattended, and their basis

**① The handoff said *"`DefaultLiteral` already owns JSON→literal conversion — do not write a second
converter."*** 📐 **Measured: it is the wrong tool and unreachable** — it produces **C# source for the
compiler**, not display text, and it is `internal` to `Hrot.Blueprints.Compiler`, which `AiShared`
sits below. ⇒ ⭐ **I wrote no converter at all**: the initial arm renders the stored JSON **as stored**.
⛔ The instruction's *intent* — don't duplicate the conversion — is honoured exactly.

**② No default declared.** ⭐ The cell renders the CLR type's **zero value**, because 📌 **`BP-247`**
rules *"`0` means leave it zero-initialised, for EVERY type"* ⇒ that **is** what the variable will start
as. ⛔ **Not a fourth vocabulary word** — the three existing cells stay three.

### ⭐⭐ The honesty rule you named, preserved and railed

`(pending)` *(the run has not written it)* · `<unreadable>` *(the bytes did not decode)* · an **initial
value** *(what it will start as)* — ⭐ **three distinct cells, three different meanings**, asserted
together in one rail so they cannot collapse.

### Gates — item 1, commit `91b8dc4`

| gate | command | `--no-build`? | result | Δ |
|---|---|---|---|---|
| solution | `dotnet build IOS-IG-SimHost.sln -t:Rebuild` | — | ✅ **0 err / 69 warn** | = |
| ⭐ **AiShared** | `dotnet test …/Hrot.Editor.AiShared.Tests.csproj --no-build` | yes | ✅ **1349** | **+19** |
| Blueprints | `…/Hrot.Blueprints.Tests.csproj --no-build` | yes | ✅ **3727 / 3737, 10 skipped** | = |
| BTree.Editor · Hsm.Editor | `--no-build` | yes | ✅ **615** · **551** | = |
| Generators · Breakpoints · Persistence | `--no-build` | yes | ✅ **270** · **134** · **136** | = |
| Scenarios · UrbanCombat | `--no-build` | yes | ✅ **56/68 (12 skipped)** · **29** | = |
| ⭐ **Hrot.Editor.Tests** | `--no-build` | yes | ✅ **194** | *(newly run — it covers `LiveBlackboardValueProvider`)* |
| ⚠ **Toolkits** | `--no-build` | yes | 🔴 **1 red run 1, green run 2** | ④ |
| ⭐ **NodeEditor.Core / .UI / Fhsm** | *(no `--no-build`)* | ⛔ **NO** | ✅ **211** · **135** · **300** | = |
| tracker · rulings | `tracker-counts.py --check` · `rulings-check.py` | — | ✅ **open 66 / done 187** · ✅ **40/40** | = |

**Golden movement:** `git status --short -- '*Snapshot*' '*Golden*' '*golden*' '*.cs.txt'
'*persistence-shape*'` ⇒ **empty**. ⭐ **17 files, +590 / −13**, ⛔ **no emitter, DTO, asset or compiler
file** ⇒ `StructureHash` **could not** have moved. **Tree clean after every suite.**
🔴 **Revert-goes-red 8 / 19.**

---

## 2. 🛠 Item 2 — row `59`, the dialog *(`BP-320`)*

### ⭐⭐⭐ THE PRE-DECIDED FORK — **resolved ✅, and it was already the built choice**

📐 **Measured** `StructEdit.Core/IComponentEditService.cs`:

```csharp
IEditSession Open(object component, Type componentType, EditScope? scope = null, EditContext? context = null);
```

⇒ ⭐ **a boxed value and a CLR type. No entity, no component store, no world.** `EditScope.WholeComponent`
and `EditScope.ForField(path)` both already exist, and `IEditSession` has `Commit()`/`Cancel()`.
⇒ ✅ **the handoff's ✅ branch**, ⛔ **and no fallback to the blueprint-local stack was ever near.**

⭐⭐ **Better still: `VariableEditLauncher` (Batch 75) was ALREADY built on it**, with
`Properties ⇒ WholeComponent` / `EditValue ⇒ ForField` — ⭐ ruling 9's shared choice had been made and
then left unreachable.

### 🔴🔴 The eleventh instance

📐 `grep` for production constructions of `VariableEditLauncher` and `VariableEditGestureBinder`:
**three hits, all in `VariableEditGestureBinderTests`.** ⇒ ⛔ **zero production call sites.**
🛠 The registrar now builds and `Attach`es the binder from the **`IComponentEditService` it was already
given** *(the Inspector needs it)* ⇒ ⛔ **not a new argument.**

### ⭐⭐ `VariableEditCommit` — the not-running write, and only that half

⭐ It asks **the same `VariableValue.ModeFor` the Value column asks** ⇒ ⛔ **the write target and the
displayed value can never disagree about which arm is live.**
⛔⛔ **Running / paused / replay REFUSE** — 📌 row `59c` and ruling 14 *(the whole-component route
"exceeds `MaxComponentSize` and cannot work")*.
⭐ **A refusal does not COMMIT the session** — committing and discarding would leave the edit applied to
a boxed copy nobody keeps: it would **look accepted and vanish**, which is worse than a refusal.

⭐ **Hard stops honoured:** no running write · no `Role`/`Scope` controls · no `Variables`/`WorkingState`
merge.

### Gates — item 2, commit `61cd666`

| gate | `--no-build`? | result | Δ |
|---|---|---|---|
| solution `-t:Rebuild` | — | ✅ **0 / 69** | = |
| ⭐ **AiShared** | yes | ✅ **1369** | **+20** |
| Blueprints | yes | ✅ **3727 / 3737, 10 skipped** | = |
| BTree.Editor · Hsm.Editor · Generators · Breakpoints · Persistence | yes | ✅ 615 · 551 · 270 · 134 · 136 | = |
| Scenarios · UrbanCombat · Hrot.Editor | yes | ✅ 56/68 · 29 · 194 | = |
| ⭐ **Toolkits** | yes | ✅ **1964 — fully green this item** | = |
| ⭐ **NodeEditor.Core / .UI / Fhsm** | ⛔ **NO** | ✅ 211 · 135 · 300 | = |
| tracker · rulings | — | ✅ open 66 / done 187 · ✅ **40/40** | = |

**Golden movement: empty.** ⭐ **4 files, +431 / −0** *(three new, one registrar edit)*, ⛔ nothing
emitter-side. **Tree clean.** 🔴 **Revert-goes-red 7 / 20.**

---

## 3. 🛠 Item 3 — row `59b`, the Watch panel *(`BP-321`, and `BP-01` closed)*

📐 **Three defects, and they were one mistake — a private copy of something shared:**

| ⛔ | measured |
|---|---|
| **1** | `HandlePinValueChanged` was **`{ /* refresh row data */ }`** — the event arrived and nothing happened |
| **2** | the value column rendered **`Convert.ToHexString(w.LastValueBytes)`** — ⛔⛔ **`BP-01`, still live** |
| **3** | *"nothing before the run"* was spelled **`"--"`** — a second vocabulary for `(pending)` |

⭐⭐ **All three collapse into one change:** render through Track C's `VariableTableControl` over a new
`WatchRowBridge`. ⇒ ⛔ **the hand-rolled `BeginTable` is gone — it was the FOURTH variable table in the
editor.** Gestures go through **item 2's dialog** *(ruling 11 is a SHARING instruction)*; **ticks stay
per row** *(📌 "in Watch, rows tick at different rates")*; **row bytes are COPIED** from the watch's
reused 64-byte buffer, so a row reports what it **observed**, not what the buffer holds at draw time.

### ⭐⭐⭐ A revert probe caught a vacuous rail of MY OWN

⚠ Swapping the panel's decoder back to hex left **every rail GREEN** — because each test built its own
formatter and asked *that*. ⇒ ⭐ the panel now reports **what IT would render** (`CellText`), and the
probe bites. 📌 ***Ask the ARTEFACT, not something that merely resembles it*** — **the eighth
instance**, and the first one I found in my own work mid-batch.

⚠ **Also fixed:** `MockDebugSession.GetWatches()` returned a hard-coded `Array.Empty`, making every
Watch-panel test **vacuous about content** — a panel rendering nothing passed.

⚠ **Ruling 12's immediacy gate is NOT this item's** *(it runs through `59c`'s running write)*. ⭐ **What
this item makes reachable:** both panels now read the **same rows through the same formatter**, so when
`59c` lands there is nothing further to share — the gate becomes a property of one write path.

### Gates — item 3, commit `d898df1`

| gate | `--no-build`? | result | Δ |
|---|---|---|---|
| solution `-t:Rebuild` | — | ✅ **0 / 69** | = |
| AiShared | yes | ✅ **1369** | = |
| ⭐ **Blueprints** | yes | ✅ **3737 / 3747, 10 skipped** | **+10** |
| BTree.Editor · Hsm.Editor · Generators · Breakpoints · Persistence | yes | ✅ 615 · 551 · 270 · 134 · 136 | = |
| Scenarios · UrbanCombat · Hrot.Editor | yes | ✅ 56/68 · 29 · 194 | = |
| ⚠ **Toolkits** | yes | 🔴 **1 red, runs 1–3** | ④ |
| ⭐ **NodeEditor.Core / .UI / Fhsm** | ⛔ **NO** | ✅ 211 · 135 · 300 | = |
| tracker · rulings | — | ✅ **open 65 / done 191** · ✅ **40/40** | +/− |

**Golden movement: empty.** ⭐ **4 files, +418 / −29** — ⛔ **the −29 is the hand-rolled `BeginTable`**,
not a golden. **Tree clean.** 🔴 **Revert-goes-red 2 + 3.**

---

## 4. ④ Every RED confirmed pre-existing — base `0f475af`

| red | verdict |
|---|---|
| `Fdp.Toolkits.Tests` — 1 test | ⭐⭐ **`DEBT-AIB-030`, NAMED this time:** `StatelessGizmoRegistryTests.SC_GZ022_2_Register_UnregisteredType_Throws` — ⭐ **one of the seven known rotating identities** *(the same one Batch 79 saw twice)*. 📐 `--filter Gizmos` ⇒ **`187 / 187`** in isolation. 📐 **`git diff --name-only 0f475af..HEAD -- FDP/` is EMPTY** ⇒ ⛔ **this batch's diff cannot reach that assembly at all** |

⚠ **It was red on runs 1–3 in item 3** *(vs green in item 2)* — ⭐ consistent with a process-global
registry race, ⛔ **not** with a regression, since the diff never touches `FDP/`.

## 5 · working tree CLEAN after every suite run. ⛔ No golden regenerated, in any item.
## 6 · quarantine — **12 scenario · 0 FastHSM**, unchanged. ⛔ No new skip.
## 7 · **ids: `BP-319` `BP-320` `BP-321`** *(+ `BP-01` closed)* · **started at `0f475af`**

---

## 5. ⭐ What this leaves for you

⭐⭐ **`R-62`'s visual check on Blueprint is now considerably more worth running** — the Value column
means something, the dialog opens, and the Watch shows decoded values. ⚠ **But I could not look at a
screen** *(rule 8)*: everything above is asserted through rails, and **nothing here claims a pixel is
correct.**

| carried | |
|---|---|
| ⭐ **`59c`** | the ECB surgical field write + the RUNNING write. ⭐ **Both panels are now ready for it** — one write path serves both |
| **`60` = `U-16`** | retiring a Variables window — ⚠ `R-60`: BTree/HSM still have no Details window *(`BP-317`)* |
| **`61`** | the shared cross-host outline — ⭐ where `BP-317` and NodeEdit's unused `DetailsPanel` belong |
| **stage `D1`–`D4`** | ⛔⛔ own batch. 🔴🔴 **`R-24`: `D2` must preserve field order or every deployed blackboard is wiped** |
| 🔴 **`2.7`, `2.40`/`2.41`** | still NOT BUILT *(settled Batch 79)* |
| ⛔⛔ **parked** | `E3` · `E5` · `E7a` · `Q36` · `Q37` |
