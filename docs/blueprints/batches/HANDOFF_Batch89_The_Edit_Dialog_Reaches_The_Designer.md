<!--STATUS
state: LIVE
updated: 2026-08-19
current-answer: this whole file - the Batch 89 dispatch.
stale-below: nothing.
known-rot: none.
known-conflict: none.
-->
# HANDOFF — Batch 89: **the edit dialog reaches the designer** *(`BP-327`, REOPENED)*

> 📌 **Dispatched at `7c2279851`.** ⭐ **Branch from THIS commit** *(rule 7)* — the handoff itself.
> ⛔⛔ **YOUR SCOPE IS FROZEN AT THIS SHA.** ⭐ Documents changing after it are **FYI ONLY**.
> ⚠ **If a later document INVALIDATES an item — STOP AND REPORT.** ⛔ **Do NOT adapt, do NOT revert.**
> ⭐ **Rule 3: allocate your own ids and state them.** ⭐ **Rule 1b: push
> `chore: started batch 89 at 7c2279851` FIRST, before any code.**
>
> ⭐⭐ **User, `2026-08-19`:** *"pls let's write a batch the unblocks the D and includes BP-327"* —
> ⭐ **`D`** is part D of 📄 [`GUIDE_Blueprint_Visual_Check.md`](GUIDE_Blueprint_Visual_Check.md),
> **9 rows blocked** on one missing call.

---

## 1. ⭐⭐⭐ THE DEFECT — **one uncalled method. Measured, do not re-derive**

```
grep -rn "EditModal" --include=*.cs .          → 4 hits, NONE of them a Draw() call
```

| 📐 measured `2026-08-19` | |
|---|---|
| **`VariableEditModal`** | ✅ **complete** — `ComponentEditDrawer` body · **OK** · **Cancel** · a **greyed OK with the reason on hover** *(guide `F3`)* · a refusal arm |
| **its construction** | ✅ **non-null in ALL THREE registrars** — `PerspectiveWorkspaceServices` **requires** `facetEditService`, so the `if` at `PerspectiveWorkspaceRegistrar:313` always passes |
| **the gestures that open it** | ✅ **attached to every table** — Batch 87's `BoundTables` |
| 🔴🔴 **`EditModal.Draw()`** | ⛔⛔ **ZERO callers, production AND test.** The four hits are: the construction *(`:328`)*, the property *(`:602`)*, and two test asserts that it is non-null |

⇒ ⭐⭐ **The gesture opens a session; the modal holds it; no frame renders it.**

### ⛔⛔ This is `BP-327`, and its row is CLOSED ON THE WRONG CRITERION

📄 **Tracker row `BP-327`, verbatim** — *"the write is COMPLETE and UNREACHABLE BY A DESIGNER"*, with
its own exit condition: ⭐ *"the right batch draws the modal and runs the guide's `F2`/`F3`."*

⚠⚠ **Batch 87 built a class that CAN draw. Nothing calls it** ⇒ **the row's sentence still describes
today, word for word.** 📌 **Third consecutive turn of one pattern:** Batch 84 built a write path
nothing drew · Batch 87 built the drawer **nothing calls**.

### ⛔ Why the existing test cannot see it

⭐⭐ **`TheEditDialogIsDrawnTests` is GREEN and structurally blind** — 📐 it **constructs the modal
itself** *(`new VariableEditModal(binder, …)`)*, so it proves `Draw()` works and can never ask whether
anyone calls it. ⛔⛔ **DO NOT EXTEND IT.** 📌 **`R-67`:** *a rail that builds its own composition root
cannot see a composition-root defect.*

---

## 2. 🛠 **`89a` — draw the modal** ⭐⭐⭐ **the item**

📄 **Design basis:** `BP-327`'s own exit criterion *(above)* · `R-67` · ruling 9 *(one mechanism)*.

### ⭐⭐ WHERE it goes — **and why the two obvious places are wrong**

| ⛔ candidate | 📐 why not |
|---|---|
| **inside a `ManagedWindow.DrawClientArea`** | ⛔⛔ **`ManagedWindow.Render:157` returns early on `if (!_isOpen)`**, and `:165` again on perspective mismatch ⇒ **the dialog would vanish whenever that window is closed** — a defect that looks exactly like the current one |
| **a line in `EditorSubsystem`** | ⛔ **three registrars ⇒ three lines to forget.** 📌 **`R-67` is the whole reason `AiDetails`, `MyBlueprint` and `Variables` are built and registered BY THE REGISTRAR** — do not reintroduce what that pattern exists to prevent |

⭐⭐⭐ **The right slot already exists and is documented as such** — `WindowManager:551`:
*"Draw file dialog service last so the modal overlays all other windows."*

| ⭐ build | |
|---|---|
| **`WindowManager.RegisterFrameOverlay(Action draw)`** *(name yours)* | ⭐ a list, drawn in that **same final slot**, after `_statusBar.Render` and beside the file dialog. ⚠ **`FDP/` is NOT a submodule** — it is in-repo and editable |
| **the registrar registers its own modal** | in `RegisterWindows`, next to `RegisterCore(...)` ⇒ ⛔ **the composition root gains NOTHING to forget** |
| ⛔ **do NOT move the file dialog onto the new hook** | 📌 *"no rush removals"* — ⭐ note it as a follow-up, it is a behaviour change in another subsystem |
| ⛔ **do NOT gate the overlay by perspective** | ⭐ `IsOpen` already gates it, and **a modal that survives a perspective switch is correct** |

### ⭐⭐⭐ THE RAIL — **the only one that can see this defect**

⭐ **Assert on the CONSTRUCTED `WindowManager`:** after a real `RegisterWindows`, the overlay list
**contains the registrar's modal draw**, and drawing the frame reaches it.
⛔ **Not on the registrar's source. ⛔ Not on `EditModal` being non-null** *(two tests already do that,
and both were green throughout this defect)*.

⚠ **Revert probe, mandatory:** remove the registration line ⇒ ⭐ **the new rail is the ONLY thing that
reddens**, and every existing test stays green. 📌 **If something else reddens too, say which** — that
would mean the old tests were less blind than measured.

---

## 3. 🛠 **`89b` — the popup id is shared by three modals** ⚠ *(small, and stoppable)*

📐 **Measured:** `VariableEditModal.Title` is a **`public const string "Edit variable"`**, and
`Draw()` uses it for **both** `OpenPopup` and `BeginPopupModal`. ⇒ ⭐ once `89a` lands, **three
registrars draw three modals under ONE ImGui id every frame.**

⚠ **It is correct TODAY only because `if (!IsOpen) return` fires first for the other two** — ⛔ an
undocumented guard standing between two popups with the same id.
📌 **This repo has already paid for popup-id confusion once** — `AssetPickerModal:185-189` carries the
diagnosis: *"the popup opens under one id while `BeginPopupModal` waits on another, so it never
renders."*

⭐ **Give the modal a per-instance popup id** *(e.g. seeded from the registrar's perspective suffix, the
same way every window takes an `idOverride`)*. ⚠ **`Title` is `public const` and referenced by tests** —
⭐ keep a display title, make the **id** the instance-scoped thing.

⛔ **If this turns out to be more than a contained change, STOP and report it** — `89a` alone is a
complete, shippable batch.

---

## 4. 🛠 **`89c` — `BP-327`'s disposition** ⭐ *(yours to decide, rule 3)*

⭐⭐ **My recommendation: REOPEN `BP-327` and close it against its ORIGINAL criterion**, rather than
filing a new id — ⛔ it is the same defect, one level up, with the same exit condition.
⚠ **That is `done 204 → 203` before your own close** — ⭐ **`tracker-counts.py --check` must pass, and
state the arithmetic.**

⛔ **If you would rather file a new row, that is your call** *(rule 3)* — ⭐ **just say which you did and
why**, and make `BP-327`'s row point at it.

⭐⭐ **The close criterion this time is NOT "the class exists":** ⇒ *"a designer can reach the dialog"*,
evidenced by **the rail on the constructed `WindowManager`** and by the revert probe.

---

## 5. 🛠 **`89d` — one false sentence in a doc comment** ⭐ *(trivial, mine to own)*

📐 `AiDetailsWindow`'s class doc still carries **my handoff's** line:
*"⭐ **The Value column comes free.** BTree and HSM already have live-value providers…"*
⛔ **Your own Batch 88 report §3.4 disproves it** — the Details table's live arm is `readRaw`, which no
production caller passes ⇒ `(pending)` on every host *(`BP-334`)*.
⭐ **Replace it with the measured statement and cite `BP-334`.** ⚠ **Doc only — ⛔ no behaviour change.**

---

## 6. ⛔ SCOPE FENCE — **what this batch is NOT**

| ⛔ | |
|---|---|
| **`BP-334`** *(the two live-value seams)* | ⭐ a **ruling-9 decision**, not a wiring item — ⛔ **not this batch** |
| **watch PINNING** | ⛔ unbuilt, and a design of its own *(`DESIGN_Variable_Watch_Pinning.md`)* |
| **the `⋮` three-dot button** | ⚠ ruling 5's unbuilt half — ⛔ **not here**; right-click works and part D only needs the dialog |
| **anything from `Q38`–`Q44`** | ⛔⛔ **`R-27`** — gated on the visual check, which this batch unblocks and does not run |
| **task group `D`** *(the orchestrator emitters)* | ⭐ ruled `2026-08-19`, ⛔ **a separate batch** |

---

## 7. ⭐ GATES — **the rule-8 contract, plus the two this batch owns**

| # | report |
|---|---|
| **1–7** | the standard contract — verbatim commands · **`--no-build` column** *(⛔ `NodeEditor.Core`, `NodeEditor.UI`, `Fhsm.Tests` report a STALE BIN)* · golden movement as a **diff shape** · every red confirmed **pre-existing vs `7c2279851`** · clean tree after every suite · both quarantine counts · `tracker-counts.py --check` + `rulings-check.py` + `design-digest.py --check` + **every id you allocated** |
| ⭐⭐ **8** | ⭐ **THE ENUMERATION: everything drawn per-frame OUTSIDE a `ManagedWindow`** — by `search_graph`/grep, with the query and its `total`. 📌 **`R-74`.** ⚠ **If a second modal is already in this position, say so** — ⛔ it changes whether the hook is new or a duplicate |
| ⭐⭐ **9** | ⭐ **What each rail ASKS**, and ⛔ **explicitly: that you did NOT extend `TheEditDialogIsDrawnTests`.** 📌 It is green through the defect it is named for |
| ⭐⭐⭐ **10** | ⭐ **The revert probe of `89a`**, un-applied with the **INVERSE EDIT** — ⛔ never `git checkout --` |

⭐ **Baseline** *(post-Batch-88 merge)*: AiShared **1446** · Blueprints **3767/3777/10** ·
BTree.Editor **615** · Hsm.Editor **551** · Hrot.Editor **201** · Breakpoints **143** ·
NodeEditor.Core **211** · NodeEditor.UI **135** · Fhsm **300** · tracker **open 66 / done 204** ·
rulings **65/65**.
⛔ **`Fdp.Toolkits.Tests`: do not run it** — 📌 `DEBT-AIB-030`, the identity ROTATES between runs.

## 8. ⭐⭐ If you must stop

⭐ **`89a` alone is a complete batch.** ⛔ **Landing it and reporting the rest is a clean end state** —
📌 Batches 85, 87 and 88 all stopped short and all three were right.
⚠ **If the overlay hook needs a decision the design does not settle — STOP.** ⛔ **Do not invent one.**

## 9. ⭐⭐⭐ WHAT THIS UNLOCKS — **state it in the report**

⭐ **9 guide rows**: `D2` · `D3` · `D4`–`D8` · `D10`, plus **`F1`**, and it makes **`C2`**'s
default-authoring route real instead of a detour through `InspectorWindow`.
⇒ ⭐⭐ **The user runs the visual check next.** ⛔ **This batch does not run it** — headless.
