# HANDOFF — Batch 65: **Track B — struct support, all three now design-ruled**

> 📌 ⭐⭐ **RE-DISPATCHED `2026-08-16` — supersedes the `4ce68ba24` stamp.**
> ⭐ **Legal, and here is why:** rule 1 exists because an amendment is **invisible to a run already in
> progress**. ⛔ **This run had not started** — `4ce68ba24` was **not an ancestor of
> `origin/claude/hrot-implementation-j1jvin` (`5ef445f7e`)** when this was written, so nothing could
> have been missed. ⭐ **Re-stamped rather than silently edited, per rule 2**, so the change is visible.
> ⚠ **If you had already started, the earlier stamp is the one that binds — say so and I will re-issue.**
>
> ⭐⭐⭐ **NEW SINCE THE FIRST STAMP — read before starting:**
> 📄 **[`DESIGN_Parameter_Model.md`](DESIGN_Parameter_Model.md)** is now **THE** parameter authority and
> **supersedes every prior parameter design**. ⛔ **Its §0 is a "do not re-derive" table of ten things
> this programme got wrong** — several of them mine. ⭐ **`S5` is now IN this batch (§3b).**
> ✅ **Batch 64 item 1 MERGED at `5ef445f7e`.** ⭐ **Your sweep was right to stop on `W7` — it is
> re-specified and OUT of this batch.**
> ⭐ **Rule 7 / Rule 4.** ⛔ **Rule 3: the coordinator allocates no ids.**
> ⭐ **One commit per item · per-item STOP conditions.**

---

## 0. ⭐⭐ What changed since you last saw the plan

⭐ **The coordinator ran its own subagent scans over `.dev/` for the gaps you declared** *(Track C, the
two prerequisites, `W8`–`W12`)* — ⛔ **that sweeping is coordinator work, not yours; my putting it in
Batch 64 was the process error, and it is now recorded in `.claude/CLAUDE.md`.**
📄 **Result: [`PLAN_Remaining_Work.md`](PLAN_Remaining_Work.md) revision 2.** ⛔ **Track C is BLOCKED on
three user decisions; Track D was substantially rewritten.** ⇒ ⭐ **Track B is what is buildable, and
all three items now have a design record behind them.**

⚠⚠ **And I repeated the Batch-63 mistake twice in that plan** — I had called the
`IStructEditDrawer`/`DrawerRegistry` chain *"dead code, delete it"* and `BlueprintVariablesWindow`
*"redundant, retire it"*. ⛔ **Both are designed and neither is dead.** 📌 **Do not act on either.**

---

## 1. `S2` — struct size resolution ⭐ **the mandate is stated; build to it**

📄 **`.dev/_DONE/btree-ai-action-binding/reports/BATCH-03-REPORT.md:34`** — ⭐⭐ **a stated design mandate**:

> *"`StructSizeResolver` lives in `Hrot.AiEditor.Generators` (Roslyn-aware) and is **injected via
> `Func<string,int?>`**. The Persistence assembly stays netstandard2.0 / Roslyn-free. **This matches the
> design mandate.**"* — traced to a **user decision `2026-06-15`** (`TASK-DETAIL.md:58`)

⇒ ✅ **The placement question is CLOSED — injection, not a project reference.** ⭐ **The shipped
precedent to copy is `BTreeBlackboardPackHelper.Pack(vars, Func<string,int?>, out total)`.**

| | |
|---|---|
| 🔴🔴 **the constraint that changes the shape** | 📄 **same report `:100` — `DEBT-AIB-012`, filed `2026-06`:** *"The `StructSizeResolver` logic is a **third copy** of `ComputeStructSize` (alongside `BTreeActionGenerator` and `BehaviorParameterSizeAnalyzer`), all kept in sync by a 'keep in sync' comment."* ⇒ ⛔⛔ **a naïve `S2` makes a FOURTH copy.** 📐 **Reuse or consolidate — and say which** |
| ⭐ **it also answers your `W5` question** | the netstandard2.0/net8.0 wall duplicates **the algorithm**, not just the constant — **and it was filed in June** |
| **gate** | an unregistered user struct gets its **real** size · ⭐ **reuse Batch 60's `EmittedStateLayoutTests`** |
| 🛑 **STOP if** | consolidating the copies is not achievable inside this batch — ⭐ **then do `S2` via injection WITHOUT adding a copy, and report what consolidation would take** · **or** a **shipped** asset uses an unregistered struct *(live wrong-layout defect)* |

---

## 2. `S4` — fixed-list `Capacity` ⭐ **the branch is SPECIFIED; it was never built**

📄 **`docs/blueprints/Blueprint_List_Variables_Design.md` §3, lines 63–72** *(⚠ outside `.dev/` — the
sweep found it in `docs/blueprints/`)*:

> *"**`StaticTypeRegistry.TryResolve`**: new branch — when `Capacity > 0` and the element resolves
> unmanaged, return the list `IrTypeRef` (unmanaged, real size). This is what lets it **pass `BP1503`**
> (unlike a plain `T[]`, which stays `IsUnmanaged=false, SizeBytes=0`)."*

⇒ 🔴 **Not a bug — a designed-but-unbuilt branch.** ⭐ **Honour the rest of §3 while you are there:**
**`SizeReliable = false`** *(the section supersedes an earlier `true`)* and the **`__List_{Elem}_{N}`**
wrapper name. ⚠ **Related: `Architect_Question_19_Fixed_Capacity_List_Variables.md` and
`Blueprint_Fixed_List_Variables.md:29`** — 📐 **read them; the sweep did not open every sibling doc**
*(`Blueprint_Fixed_Collections_Design.md`, `Architect_Question_21_Action_DTO_Fixed_Lists.md` were
NOT opened)*.

| | |
|---|---|
| **gate** | a declared fixed list **keeps its capacity** and does not degrade to a scalar; ⭐ **red-first** |
| 🛑 **STOP if** | a sibling design doc contradicts §3 — ⭐ **§3 is the one the sweep read; it may not be the latest** |

---

## 3. `S3` — the `MarshalFromBytes` struct arm ⭐ **designed in from the start, never built**

📄 **`.dev/_DONE/blueprints-1/TASK-DETAIL.md:1840`** — *"`MarshalFromBytes(byte[], Type)`:
`MemoryMarshal.Read<T>` dispatch for primitives, **reflection-based for structs (UI decode only, not on
the probe path)**"* · 📄 **`.dev/_DONE/blueprint-dbg-1/TASK-DETAIL.md:193`** — *"Debug DD §8.5 —
primitives/**small** structs only"*.

⇒ ✅ **CONFIRMS the plan, and BOUNDS it:** ⭐ **reflection is the ruled mechanism**, ⛔ **UI-decode only
— keep it off the probe path**, and ⚠ **"small structs" is a stated limit, not an oversight.**

| | |
|---|---|
| ⭐ **what it closes** | **`BP-01`** — `Vector2/3/4`, `Quaternion`, `FixedString32/64/128` — **seven of the eighteen offerable types** — currently fall through to `return bytes` ⇒ *"the watch panel shows raw hex"* was never a panel bug |
| ⭐ **also needed** | **assembly-qualified `ResolveType`** — `Type.GetType(fqn)` without an assembly qualifier never finds a game struct, so the field is silently **skipped** |
| ⭐⭐ **the rail** | pin the marshaller against the **closed 18-type set** with a reflection test ⇒ **every offered type can be SHOWN**, not merely compiled |
| 🛑 **STOP if** | honouring *"not on the probe path"* is not possible without restructuring — report rather than widen it |

---

## 3b. ⭐ `S5` — ONE offerable type list *(ADDED `2026-08-16`)*

⭐ **Added because it is next in the order anyway, it is small, and it unblocks the Track C dialog.**

| | measured |
|---|---|
| **the defect** | the **parameter** combo (`ParameterRowsView`) reads `StaticTypeRegistry.EditorOfferableTypeIds` — **18 hardcoded primitives, NO structs**; the **variable** modal (`VariableCreateModal`) reads `BlueprintTypeSystem.SelectableTypeIds` **plus** `DiscoverBlackboardDtoStructTypes()` |
| ⇒ **the symptom** | ⭐⭐ **a variable can be struct-typed today; a parameter cannot** |
| **the precedent** | `U-8`'s own comment names this defect class: *"a designer could declare a struct variable in one window and not the other"* ⭐ *"Discovery IS the existence proof"* |
| ⚠ **the trap `U-8` already hit** | widening `SelectableTypeIds` fed the **list-element** picker too, producing a budget of *"≈ 4 bytes"* per struct element. 📐 **`Modal_BudgetHelper_KnowsEverySelectableUnmanagedElementSize` caught it — keep that test green** |

⭐ **Rail:** the parameter combo and the variable modal return the **same set, structs included** —
📄 `DESIGN_Parameter_Model.md` §8, last row. ⛔ **Two lists surviving fails it.**
📌 **Impact:** editor-side only ⇒ **`StructureHash` and `persistence-shape` MUST NOT move.**

---

## 4. ⛔ NOT in this batch

`S5` *(one picker — ⚠ it is a **UI** change and Track C is blocked)* · **all of Track C** · `W6`/`W7`
*(re-specified; `W7` is a suppressible warning extending `OutputLaneMask`, not an error)* · `W8`–`W12` ·
the `Fdp.Toolkits.Tests` race *(take it only if the run has room)*.

---

## 5. Gates

**Baseline — coordinator-run at `9edf13fdf`:** build **0 errors / 69 warnings** · Blueprints
**3618 / 3608 / 0 / 10** · AiShared **1216** · BTree **612** · Breakpoints **130** · Generators **196** ·
Toolkits **1942** · NodeEdit **208 / 131**.

| | |
|---|---|
| 🔴🔴 **`StructureHash` unchanged for all 43** | ⚠ **`S2` and `S4` can both move it** — an unregistered struct or a restored capacity **changes that asset's layout**. 📐 **If a SHIPPED asset moves, STOP: that is a live wrong-layout defect and it outranks the batch** |
| **`persistence-shape.txt`** | ⛔ **UNCHANGED** |
| ⭐ **golden Tier 1 unchanged** · Tier 2 declared per item · **per-item revert-goes-red** · `tracker-counts.py --check` | |

---

## 6. Reporting

### ⭐⭐⭐ NEW — **the gate table** *(added `2026-08-16`; it changes how I verify)*

⛔ **Report ONE ROW PER GATE: the exact command you ran, and the result.** ⭐ **This is what lets me
stop re-running all eight** — an omission or a wrong flag becomes visible instead of invisible.

| gate | command (verbatim) | result |
|---|---|---|
| solution build | | errors / warnings |
| each suite | | total / passed / failed / skipped |

⚠ **The two NodeEdit gates take NO `--no-build`.** ⭐ **State that you honoured it** — that is the known
failure mode, and it is the one I will always re-run.
⭐ **Also state: did any suite need a re-run to go green?** 📌 `Fdp.Toolkits.Tests` has a **known race**
*(1·1·2 failures on an identical binary)* — ⛔ **a flake there is NOT yours to fix**, but **it must be
reported, not smoothed over.**

⭐⭐ **Whether `S2` added a fourth `ComputeStructSize` copy or avoided it** · ⭐ **what consolidation
would take** · ⭐ **any sibling design doc that contradicts `S4`'s §3** · ⭐ **the `BP-01` type count
actually closed** · 🔴 **`StructureHash` unchanged, stated FIRST** · per-suite numbers **full and
filtered** · `tracker-counts.py --check` · ⭐ **every id you allocated**.

⭐⭐⭐ **The question to carry:** ⛔ **All three items in this batch were DESIGNED AND NEVER BUILT, and
one debt (`DEBT-AIB-012`) was filed in June and never surfaced to this programme.** 📐 **How many other
`DEBT-*` rows across the 40 trackers in `.dev/` and `.dev/_DONE/` are open, unowned, and inside our
blast radius?** ⭐ **That list is worth more than any single fix.**
