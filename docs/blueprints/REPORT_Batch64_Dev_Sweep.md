# REPORT — Batch 64 item 1: the `.dev/` design-record sweep

> **Implementation session, `2026-08-15`.** ⛔ **Reported, not applied** — per the handoff, the
> coordinator revises the plan. 🛑 **One record CONTRADICTS a dispatched design (`W7`) — see §1.**

**Scope of what I actually did:** grepped **2887** markdown files under `.dev/` for each remaining
item's own vocabulary, and read the files that hit. ⛔ **I did not read the corpus.** §4 lists what I
did not cover.

---

## 1. 🛑 `W7` — **CONTRADICTS.** The concurrent-region rule is already fully designed

📄 **`.dev/ai-hsm-btree-vis-edit/Blackboard_Authoring_Detailed_Design.md` §7.7, §9.1–§9.6.**

| dispatched `W7` | what the record says | verdict |
|---|---|---|
| **error** on concurrent writers | ⛔ **a WARNING** (`CrossRegionBlackboardConflict`) with a **per-conflict "Suppress this conflict"** affordance persisted as editor metadata (§9.3), plus a variable-level **"Allow concurrent writes"** checkbox (§9.4) | 🔴 **CONTRADICTS on severity** |
| permit concurrent readers | ✅ §9.6 — *"If a variable is only read by both regions and never written, no conflict exists"* | ✅ **CONFIRMS** |
| `W6` supplies the reader/writer oracle via a **static** read-only projection | ⛔ the record classifies by **whether the action mutates the ref parameter** (`p.X = …` ⇒ writer), with an **optional annotation** and the validator **conservatively assuming read-write when unannotated** — false positives handled by Suppress (§9.6) | 🔴 **REFINES/CONTRADICTS the mechanism** |
| *(not in the dispatch)* | ⭐ **extend the EXISTING infrastructure** — *"the blackboard-variable analog of the `OutputLaneMask` conflict the HSM already detects for `CommandLane` writes"* (§9.1) | ⭐ **ADDS** |
| *(not in the dispatch)* | §9.2 gives the **algorithm** (writers → hosting states → simultaneously-active pairs → diagnostic) and its cost; §9.5 adds a case the dispatch omits — **Approach B Sync-Out** conflicts | ⭐ **ADDS** |

⇒ ⛔ **Building `W7` as dispatched would ship a hard error where the design specifies a suppressible
warning, and a different writer-classification than the one designed.** ⚠ It would also be a second
implementation of a conflict validator that §9.1 says to extend — **ruling 9's shape**.

---

## 2. Track B

| item | record | verdict |
|---|---|---|
| **`S2`** | 📄 `.dev/btree-ai-action-binding/reports/BATCH-03-REPORT.md:34` — ⭐ **a stated design mandate**: *"`StructSizeResolver` lives in `Hrot.AiEditor.Generators` (Roslyn-aware) and is **injected via `Func<string,int?>`**. The Persistence assembly stays netstandard2.0 / Roslyn-free. **This matches the design mandate.**"* · `TASK-DETAIL.md:58` cites a **user decision, `2026-06-15`** | ✅ **CONFIRMS the lean** — injection, not a project reference. ⭐ The concrete precedent is `BTreeBlackboardPackHelper.Pack(vars, Func<string,int?>, out total)` |
| **`S2` ⚠ addition** | 📄 same report **`:100` — `DEBT-AIB-012`, already filed:** *"The `StructSizeResolver` logic is a **third copy** of `ComputeStructSize` (alongside `BTreeActionGenerator` and `BehaviorParameterSizeAnalyzer`). All three are kept in sync by the 'keep in sync' comment. A shared utility would be better but is **architecturally non-trivial**."* | 🔴 **REFINES** — ⛔ a naïve `S2` makes it a **FOURTH** copy. ⭐⭐ **And it answers the question I have been carrying since `W5`**: the netstandard2.0/net8.0 wall duplicates the *algorithm* as well as the *constant*, and that was filed in June |
| **`S3`** | 📄 `.dev/_DONE/blueprints-1/TASK-DETAIL.md:1840` — *"`MarshalFromBytes(byte[], Type)`: `MemoryMarshal.Read<T>` dispatch for primitives, **reflection-based for structs** (UI decode only, not on the probe path)"* · `blueprint-dbg-1/TASK-DETAIL.md:193` — *"Debug DD §8.5 — primitives/small structs only"* | ✅ **CONFIRMS** — ⭐ the struct arm was **designed in and never built**, not an invention. The design also fixes its shape (reflection, UI-only) and bounds it (*small* structs) |
| **`S4`** | **none found** — the `.dev/` hits for fixed-capacity lists are all unrelated programmes (squad/anim/utility-AI) | ⚪ **no record** |
| **`S5`** | 📄 `blueprint-finalize/batches/BF-BATCH-FIXEDSTRING-INSTRUCTIONS.md:33` — adding a type means adding it to **`SelectableTypeIds`** *"so the variable-create dropdown offers them"*; `blueprint-canvas-parity` reports show the same list driving `VariableCreateModal` | ⭐ **REFINES** — the records treat `SelectableTypeIds` as **the** picker list and never mention `EditorOfferableTypeIds`. ⇒ **`S5`'s "two pickers" is real and undocumented**: the second list grew later, on the compiler side |

---

## 3. `W6` and Track D

| item | record | verdict |
|---|---|---|
| **`W6`** | ⚪ **none found** in the three coordinator-named pointers (`AN3-REPORT`, `AN7-REPORT`, `ai-hsm-btree-vis-edit-2/DECISIONS.md`) — their "read-only" hits are about **inspector labels**, not component access | ⚪ **no record**. ⚠ But §9.6 above **does** rule on writer-vs-reader classification, which is what `W6` exists to supply ⇒ **read `W7`'s answer before building `W6`** |
| 📐 **`[SharedAiCondition]` count, re-measured** | **ZERO production usages.** All 10 mentions are the attribute's own machinery (`ActionRegistry`, `BTreeActionGenerator`, the two analyzers, `IActionSchemaExporter`) plus **3 test-fixture files** | ✅ `W6`'s STOP does **not** fire — the count is ~0, as the design session measured |
| **`W8`–`W12`** | ⚪ **none found** — no hits for *reserved input variable*, *initializer picker*, *Construction initializer*, `InitializerKind` | ⚪ **no record** *(⚠ low confidence — I do not have `W8`–`W12`'s full text, so I grepped the handoff's three-word summary of each)* |

---

## 4. ⛔ What I did **not** cover — stated, not implied

- **`.dev/_DONE/` (2178 files)** swept only where a keyword hit; ⛔ **not read as a corpus.**
- **Track C's panels** (`C1`–`C7`): the named record —
  `Blackboard_Authoring_Detailed_Design.md` — is about **authoring** (source-of-truth model, DTO
  files, bin-packing, aliasing). ⛔ **It does not rule on the value column, StructEdit, the write path
  or the Watch panel.** ⚠ I did **not** sweep `.dev/blueprint-dbg-1/2`, `main-toolbar-*` or
  `ai-hsm-btree-vis-edit-2/` for those — that is the largest remaining gap.
- **`W8`–`W12`** searched by summary vocabulary only.
- **The two prerequisites** in `PLAN_Remaining_Work.md §5` (the surgical ECB field-write; the paused
  snapshot-vs-live pass) — **not swept at all.**

---

## 5. ⭐⭐ The carried question — where a session should look FIRST

📐 **Which hard-won findings were already written down?** Three, from one afternoon of grepping:

| we derived it in | it was already written in |
|---|---|
| **Batch 63** — the standalone `BTreeTick` is a deliberate hosting path | `SLICE1-DESIGN.md:82` / `SLICE2-DESIGN.md` §6.2 |
| **Batch 61 (`W5`)** — the netstandard2.0/net8.0 wall duplicates things with only a comment holding them | `BATCH-03-REPORT.md:100` — **`DEBT-AIB-012`**, filed `2026-06` |
| **`S3`** — the struct arm is missing, not impossible | `_DONE/blueprints-1/TASK-DETAIL.md:1840` |

⇒ ⭐ **The pattern: the DESIGN file states the intent, the REPORT file states the debt.**
📌 **Look-first order, for `.claude/CLAUDE.md`:** ① the programme's `*-DESIGN.md` / `*Detailed_Design.md`
② its `reports/*-REPORT.md` "notes/debt" tails ③ `TASK-DETAIL.md` for the user decision that authorised it.
⛔ **Batch instructions and reviews are the least useful — they restate the design.**
