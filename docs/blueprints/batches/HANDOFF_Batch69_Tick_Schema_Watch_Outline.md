# HANDOFF — Batch 69: **make the table LIVE** — `C-tick` · `DEBT-AIB-009` · `C-watch` · `C-outline`

> 📌 **Dispatched at `363094511`.** Frozen per rule 1 *(rule 1a: re-dispatch only while this sha is NOT
> in your history)*. ✅ **Batch 68 MERGED at `79f23be63`** — gates re-run, snapshots unchanged.
> ⭐ **Rule 7 / Rule 4.** ⛔ **Rule 3: the coordinator allocates no ids.**
> ⭐ **One commit per item · per-item STOP conditions.**

---

## 0. ⭐⭐⭐ Batch 68 — **two calls of yours I am adopting, one correction to MY design**

| | |
|---|---|
| ⭐⭐⭐ **refusing to wire the world tick was RIGHT** | under it red clears whenever any frame advances — **including while paused**, which is the exact case the ruling exists for. ⭐ **A nullable `AssetTick` that makes a row inert rather than wrong, asserted so it reads as a decision, is the correct shape.** ⇒ **item 1 finishes it** |
| 🔴🔴 **`DEBT-AIB-021` contradicted `DESIGN_Parameter_Model.md` §3.2 — and you were right** | I wrote *"scenario JSON overlays, runtime wins"* as **universally shipped**. It is **true of the curated path and FALSE of the generated managed-asset path**, which discards the incoming json. ⭐ **Corrected in the design, with the path table and the implementation note.** ⛔ **Not this batch** |
| ⚠ **count corrected** | **18 open `DEBT-AIB`, not my ~22.** ⭐ **Your tracker read wins over my estimate** |

---

## 1. 🔴🔴 `C-tick` — **the per-`(asset, entity)` tick counter** ⭐ *the item that makes the table live*

📄 **`DESIGN_Variable_Details_And_Editing.md` §4a** — the ruling, verbatim: *"a non-frozen CGF behavior
tick, i.e. the asset tick/update call."*

| | measured by you, confirmed by me |
|---|---|
| ⛔ **what exists** | `_view.Tick` → `ISimulationView.Tick` → `EntityRepository.SimulationTick` — **the world frame clock** |
| 🔴 **what does not** | **any per-instance counter.** `BlueprintTickSystem` calls `def.Tick(...)` and stamps none |
| ⭐ **the seam is already cut** | `AssetTick` is a per-row nullable delegate; **`null` ⇒ inert.** ⇒ **supply a real one and nothing else changes** |

### What the counter must satisfy — ⭐ **these come straight from the ruling**

| | |
|---|---|
| ⭐⭐ **it advances ONLY when that asset's tick/update actually runs** | ⛔ **not per frame, not per world tick** |
| ⭐⭐⭐ **and only when NOT frozen** | `BlueprintTickSystem` already returns early on `deltaTime <= 0f` ⇒ **a counter stamped inside the tick path gets this for free.** 📐 **Confirm that is where you put it** |
| ⭐ **keyed per `(asset, entity)`** | ⛔ **not per asset** — the same asset on two entities ticks independently *(§1a: entity is part of row identity)* |
| ⚠ **an unscheduled asset keeps its highlight** | ⭐ **correct, and the ruling says so** — the value has not had a chance to change |

### 🔴 STOP conditions

| | |
|---|---|
| ⭐ **where does it live?** | 📐 **The slot already carries `InstanceVersion`.** ⚠ **Do NOT reuse that** — it is the **latent-cursor staleness token** *(bumped on hard reload, compared against `BlueprintLatentCursor.InstanceVersion`)*. ⛔ **A second meaning on one field is the trap this programme keeps finding.** ⇒ **a new counter, and say where you put it and why** |
| ⚠ **BTree/HSM hosts** | ⭐ **Blueprint Instances first** — they have `BlueprintTickSystem` and a slot. 📐 **If BTree/HSM cannot get one cheaply, say so and leave those rows `null`** — inert is the designed fallback, and **partial is fine here** |

**rails:** ⭐ **the frozen case, end to end**: N world frames with `deltaTime <= 0` ⇒ **the counter does
not advance and the highlight persists** · a **Step** advances it once · **two entities running one
asset advance independently** · ⛔ **a row with no source stays inert** *(your existing assertion must
still pass)*.
**impact:** runtime + editor. ⛔ **`StructureHash` / `persistence-shape` MUST NOT move.**

---

## 2. ⭐⭐ `DEBT-AIB-009` — **Track C's ground truth, and the same shape as `E4`**

> 📄 *"hardcoded-DTO reflection **not wired in production DI**"* — the render path takes
> `_actionSchemaExporter` and **neither production constructor supplies it.**

⇒ ⛔⛔ **A value column over a schema nothing supplies.** ⭐ **You named this yourself as the thing to
read before `C-watch` — so it is item 2, before it.**

⚠ **This is the third instance of one pattern:** `HsmValidator`'s resolvers *(fixed in `E4`)* ·
`_actionSchemaExporter` *(here)* · and the **injected-but-defaulted** `sharedScopeKeys` *(item 5)*.
📐 **In your report, say whether these are three instances of a fixable pattern or three coincidences** —
if there is a shared cause *(a DI convention that lets an optional dependency default silently)*, **that
is worth more than the three fixes.**

**rail:** the exporter **is supplied by the production constructor**, proven the way `E4`'s was — ⭐ **a
test that goes through the production path, not a hand-injected one.**
**impact:** editor DI. ⛔ **hash / persistence MUST NOT move.**

---

## 3. `C-watch` — **the Watch panel shares the row renderer**

📄 **`DESIGN_Variable_Details_And_Editing.md` §1a, §1b, §7.**

| | |
|---|---|
| ⭐⭐ **`PinnedSource(rowIds)`** | rows from **ARBITRARY assets and entities, mixed** ⇒ **this is the case `C-table`'s heterogeneous rail was written for.** ⛔ **If that rail is right, this is mostly wiring** |
| **defaults** | Watch `GroupBy = [Asset, Entity]` · **`Type` column hidden** *(monitoring — "not even the data type is important")*; Details `[]` and `Type` shown |
| ⭐ **stale rows** | a Watch row outlives its asset or entity. ⭐ **`Watch.IsStale` already exists — reuse it**; a stale row shows its last value **greyed** and **refuses its dialog** |
| 🔴 **the refresh gap is NOT an empty handler** | ⛔ **it needs Trace compile mode** — **Debug emits no `PinValueChanged` at all**, and `QuickReloadService:64` **hardcodes `CompilerMode.Debug`.** 📐 **Fix the mode, then the handler** |
| 🔴 **the 64-byte buffer** | `Watch._valueBuffer = new byte[64]`, `WriteValue` **throws** above ⇒ `MemberSlotList` (96) / `WaveState` (104) / `HillAttackSharedState` (136) **cannot pass through it.** ⭐ **You already proved the new formatter takes any length (asserted at 136)** ⇒ **the Watch path must use that, not the 64-byte one** |

**rails:** a pinned set spanning **two assets and two entities** renders with correct grouping and
**independent** highlight state · a stale row **renders greyed and refuses its dialog** · ⭐ **a
136-byte struct pins and renders** *(the buffer limit is not inherited)*.

---

## 4. `C-outline` — **BTree/HSM supply their own `IMyBlueprintModel`**

📄 **`DESIGN_Variable_Details_And_Editing.md` §1c** — sections are the classification.

⭐ **`C-sections` (Batch 66) did the blueprint model.** This is the same shape for the AI hosts:
**their own section list**, each section with its own `CreateCommandId`.

| | |
|---|---|
| ⭐ **the panel is already generic** | `MyBlueprintPanel` in `NodeEditor.UI`, `IMyBlueprintModel` in `NodeEditor.Core` ⇒ ⛔ **nothing about it is blueprint-specific** |
| ⚠ **sections differ per host** | show **only what the asset has.** ⭐ **`SectionLocalVariables`' rule applies: EMPTY rather than ABSENT** — *"a section that appears and disappears reads as a broken feature"* |
| ⛔ **still no `Role`/`Scope` control** | the section **is** the classification, on every host |

**rail:** ⭐ **headless** — a BTree asset and an HSM asset each yield their expected section list in
`SortOrder`, and **creating in a section produces a declaration of that kind**.

---

## 5. ⭐ Finish `E4` — `sharedScopeKeys` is threaded but left at its default

⭐ **You flagged this yourself:** `DEBT-AIB-028`'s recipe names only `_isStatefulSubtree`, so
`sharedScopeKeys` is passed but still defaults ⇒ ⛔ **rule 8b still cannot fire.**
📐 **Supply it the same way `_isStatefulSubtree` was supplied**, or ⭐ **say why it cannot be** — if it
needs `-028`(a)'s persisted `SubtreeAssetId`, **that is a legitimate answer and `E5` inherits it.**

---

## 6. ⛔ NOT in this batch

**`E0`** *(the HSM golden harness — its own batch, as ruled)* · `E3` · `E5` · `E6` · `E7a`/`E7b` ·
`DEBT-AIB-021`'s overlay fix · the Instance params seam · multi-occurrence · `G7`+`W10` · the
`InspectorWindow` "STATIC PARAMETERS" retirement.

---

## 7. Gates

**Baseline — coordinator-verified at `79f23be63`:** build **0 / 69** · Blueprints **3649 / 3639 / 0 / 10** ·
AiShared **1261** · BTree **615** · Breakpoints **134** · Generators **203** · Hsm.Editor **528** ·
Toolkits **1958** · NodeEdit **208 / 131** · tracker **open 61 / done 143**.

| | |
|---|---|
| ⭐ **add any suite the diff reaches** | you did this for `Hsm.Editor` unprompted — **keep doing it** |
| ⭐⭐ **`Fdp.Toolkits.Tests`** | a **full-suite red is not signal by itself**; ⛔ **a full-suite green is not evidence either.** `DEBT-AIB-030` |
| 🔴 **`StructureHash` unchanged · `persistence-shape` UNCHANGED** | ⭐ **item 1 touches runtime; the rest are editor** ⇒ a move means you touched emission |
| **per-item revert-goes-red** · `tracker-counts.py --check` · ⚠ **the two NodeEdit gates take NO `--no-build`** | |

---

## 8. Reporting

⭐⭐ **The gate table — one row per gate, verbatim command, result.**

**Per item:** ⭐ **where you put the tick counter and why not `InstanceVersion`** · ⭐ **whether the
frozen rail failed before item 1** *(it should — the highlight is inert today)* · ⭐⭐ **item 2's
question: three instances of a fixable DI pattern, or three coincidences?** · **what `C-watch` /
`C-outline` could NOT be verified without the visual check** · **`StructureHash` unchanged, stated
FIRST** · ⭐ **every id you allocated**.

⭐⭐⭐ **The carried question is CLOSED — thank you.** The partition *(Track C: `009`; parameter seam:
`001` `002` `008` `011` `021`; parameter model: `003` `004` `005` `025`; Track E: `022` `028` `029`
`031`; neither: `010` `030` `023` `024`)* **is now in the plan and I am scheduling from it.**
📌 **New standing ask, much smaller:** ⭐ **if a batch touches a row on that list, say so in one line** —
so the list decays as we work instead of being re-triaged.
