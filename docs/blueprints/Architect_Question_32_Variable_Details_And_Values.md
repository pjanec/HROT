# Architect Question #32 — the variable **Details** panel: initial values and live values

> **Coordinator, `2026-08-14`.** ⭐ **Raised by the user** after running `VISUAL_CHECK §A`:
> *"there is no detail panel for variables… there should be a variable table listing all vars with
> their details (type etc). I think there should be also their editable initial value if exercise not
> started yet (planning mode) and there should be the current value if exercise is running."*
>
> ⭐ **Number taken per `CLAUDE.md` rule 3a** — `git ls-tree` across **all** remote branches; highest
> existing is `#31`.
> ⚠ **`U-6` already exists in the plan** *("Details hosts the table; selection routes")*. ⛔ **The two
> VALUE requirements are new and are what this question is for.**

---

## 0. ⭐⭐ The short version: almost none of this is new

**Measured on the merged tree at `ee4d134ab`.** Every one of the three asks lands on a seam that
**already exists**, and in two cases **BTree and HSM already use it and blueprints do not.**

| the ask | what exists today | the actual gap |
|---|---|---|
| **a table of all variables with type etc.** | ✅ **`VariablesPanelControl`** — shared, in `Hrot.Editor.AiShared`. Columns are already **Name · Type · Bytes · Value · Role · Scope · ✕** (`:302-308`) | ⛔ **nothing** — it is in the **wrong window.** ⭐ **That is exactly `U-6` + `U-16`, already planned** |
| **editable initial value** | ✅ **`DefaultValueJson`** on `ParameterDecl` *and* `VariableDecl`, serialized, reachable through `BlueprintDeclaration:143-146` (get **and** set). ⭐ **Already the compiler's contract** — `GraphTypes.cs:69`: locals are *"re-initialised from `VariableDecl.DefaultValueJson` on entry"*. ✅ **`IBlackboardManagedAsset.UpdateVariableDefaultValueJson`** exists, and **`HsmAsset:147`** and **`BehaviorTreeAsset:362`** implement it | ⛔ **the blueprint source does NOT implement it**, and ⛔ **no cell edits it** |
| **current value while running** | ✅ the **`Value` column already exists and is already live-driven** — `liveValues` dictionary, `—` when absent (`:431-436`). ✅ Supplied by **`ILiveValueProvider.GetLiveVariableValues`** at `BlackboardAuthoringWindow:501`. ✅ Blueprints have **`BlueprintDebugSession.MarshalFromBytes`**, **`BlueprintRuntimeInspectorPane`**, **`WatchPanelWindow`** | ⛔ **blueprints never supply `liveValues`** — only the BTree/HSM window does. ⚠ **And `BP-01`: the Watch panel shows RAW HEX**, while `MarshalFromBytes` is *"complete, tested, and used at 4 other sites in the same file"* |

⇒ ⭐⭐ **This is a wiring task wearing the clothes of a feature.** ⛔ **The one genuinely new decision is
`Q32-A`** — what the Value column shows in each mode.

---

## Q32-A — ⭐ **One Value column that switches mode, or two columns?**

The user's wording implies a **mode switch**: initial *"if exercise not started yet"*, current *"if
exercise is running"*.

| | option | for | against |
|---|---|---|---|
| **A1** ⭐ | **one `Value` column, mode-switched** — editable initial when stopped, read-only live when running | ⭐ **the column already exists and already falls back to `—`**; matches the user's words exactly; no width cost in an already-7-column table | ⛔ **the same cell means two different things**, and a designer glancing at it mid-run cannot see what the value will be next run |
| **A2** | **two columns** — `Initial` (always editable) and `Current` (`—` when stopped) | ⭐ **never ambiguous**; you can compare *drifted-to* against *starts-at*, which is the actual debugging question | ⛔ 8 columns in a side panel; `Current` is dead weight most of the time |
| **A3** | **one column, `Initial`, plus live value as a hover tooltip** | narrowest | ⛔ **a value you must hover to see is not a watch window**; defeats the purpose while running |

⚖️ **Claude's lean: `A2`, against the user's phrasing — and it is worth one round-trip to confirm.**
⭐ **Reasoning:** the user asked for a mode switch because they were describing *when each is useful*,
not asking for one cell to be overloaded. ⛔ **The moment an exercise is running, "what does it start
at" is the question you most want answered** — that is precisely when a drifted value looks wrong and
you need to know whether it drifted or was authored wrong. ⚠ **`A1` hides exactly that.**
📐 **If width is the objection, `A2` with `Current` auto-hidden when no session is attached gives both.**

---

## Q32-B — Where is the initial value **edited**, and who applies it?

| | option | notes |
|---|---|---|
| **B1** ⭐ | **inline in the table cell**, blueprint source implements `UpdateVariableDefaultValueJson` | ⭐ **mirrors `HsmAsset:147` / `BehaviorTreeAsset:362` exactly** — the method is already on the shared interface and already called by the shared control. ⛔ **Blueprint is the only one of three hosts not implementing it** |
| **B2** | a separate **Details** sub-panel below the table | ⭐ more room for a struct-typed default (`Vector3` needs 3 fields) | ⛔ a second surface for one concept — **the exact thing `U-16` exists to remove** |
| **B3** | reuse **StructEdit** for the value editor | ⭐ `Q-j` already established StructEdit as the struct-shaped editor, and `BP-26`'s row notes *"a full StructEdit-generic predicate editor already exists"* | ⚠ heavier; ⭐ **but it is the only option that handles `Vector3`/`Quaternion` defaults properly** |

⚖️ **Lean: `B1` for scalars, `B3` for the 4 vector types**, decided by type rather than by panel.
⛔ **`B2` rejected** — it re-creates the two-surfaces problem `U-16` is closing.

⚠ **Two sub-rulings needed:**
1. ⭐ **Does an initial value apply to `WorkingState` at all?** Parameters and Variables clearly. ⛔ **Working state is per-run scratch** — offering an editable initial there may be meaningless, or may be exactly the local-reset semantics `Q27-A3` already ruled. 📐 **A ruling, not a guess.**
2. ⭐ **`Q-k` made Role/Scope read-only for blueprints. Does that extend to the initial value?** ⚖️ **Lean: no** — Role/Scope are structural; a default is authored data. **But it must be said out loud**, because `BP-230` was exactly a surface that let you edit something it then discarded.

---

## Q32-C — The live provider, and `BP-01`

| | option |
|---|---|
| **C1** ⭐ | **implement `ILiveValueProvider` for blueprints**, reading through `BlueprintDebugSession` — ⭐ **the BTree/HSM seam, reused verbatim**, so all three hosts show live values the same way |
| **C2** | route the table through `BlueprintRuntimeInspectorPane` instead | ⛔ **a fourth path to a value that already has three** |

⭐⭐ **And fix `BP-01` at the same time, one level down.** The Watch panel shows **raw hex** although
`MarshalFromBytes` — the function that would format it — sits **in the same file, complete and used at
four other sites**. ⇒ **formatting once, where the bytes are marshalled, gives the Watch panel, the
Runtime Inspector and this new column the same answer.** ⛔ **Formatting in the table alone would make
a fourth formatter and leave `BP-01` open.**

📐 **One question this cannot answer from code: what is the authoritative "is the exercise running"
signal** the panel should switch on? ⚠ **Not `IsPaused`** — a paused exercise is running.

---

## Q32-D — Sequencing, and ⚠ the cross-host collision risk

| | |
|---|---|
| ⭐ **Does this ride `U-6` or follow it?** | ⚖️ **Lean: `U-6` first, unchanged and small** — move the existing table into Details and prove it renders. **Then** values as a second slice. ⛔ **Bundling them means a red panel could be either change** |
| ⚠⚠ **`claude/cross-host-variable-model-3k8cfh` is working on the SHARED variable model right now** | ⛔ **`UpdateVariableDefaultValueJson` and `ILiveValueProvider` are on the shared `Hrot.Editor.AiShared` interfaces — their territory, not this session's.** ⭐ **Their `E-A` was explicitly scoped to BTree/HSM with the blueprint `DeclarationKind` mapping left OPEN.** 📐 **This question must be shown to them before either session touches the shared interfaces** |

---

## ⭐ Summary of what is actually being asked for

| | build? |
|---|---|
| the table itself | ⛔ **no — it exists.** `U-6`/`U-16` place it |
| initial-value **storage** | ⛔ **no — `DefaultValueJson` exists and the compiler already honours it** |
| initial-value **setter** | ⭐ **implement one method** the other two hosts already implement |
| initial-value **cell** | 🟠 **new** — scalars trivial, vectors want StructEdit |
| live-value **column** | ⛔ **no — it exists and is already live-driven** |
| live-value **provider** | ⭐ **implement one interface**, reusing `MarshalFromBytes` |
| `BP-01`'s hex | 🟠 **fix at the marshalling site** ⇒ three surfaces improve at once |
