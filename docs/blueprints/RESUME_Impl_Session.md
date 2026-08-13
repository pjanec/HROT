# RESUME — implementation session · **mid-Batch-42: `BP-57`'s last mile**

> **Written immediately before a context compaction. Self-contained; assumes no prior conversation.**
> **You are the *implementation* session.** A separate *coordinator* session owns the tracker and
> writes the handoffs. Last updated **2026-08-13**.
>
> ⚠⚠ **Batch 42 is HALF DONE and NOT reported.** §2 and §3 are pushed; **§1, §4, §5 remain**, and
> ⛔ **`BP-57` cannot be ticked until §1 lands** — there is still nowhere in the UI to declare a local.

| | |
|---|---|
| **Repo** | `pjanec/HROT` |
| **Implementation branch — PUSH HERE** | ⭐ **`claude/hrot-implementation-j1jvin`**, at **`57cd616`**, clean and pushed |
| **Coordinator branch — do NOT push** | ⭐ **`claude/blueprint-authoring-status-gm0akp`**, at **`2f512d2`**, already merged into mine |
| **Live handoff** | 📄 **[HANDOFF_Batch42_Local_Variables_Wiring.md](HANDOFF_Batch42_Local_Variables_Wiring.md)** |
| **Counts** | **63 open · 105 done** — ⚠ *derive, never hand-count:* `python3 scripts/tracker-counts.py --check` |
| **Next free ids** | rows **BP-234+** · diagnostics **BP1671+** |

⛔ **No PR unless the user explicitly asks.** There has never been one in this programme.
⛔ **Never put a model identifier** in a commit message, code comment, or anything else pushed.

---

## 0 · First actions, in this order

```bash
git fetch origin claude/blueprint-authoring-status-gm0akp
git merge origin/claude/blueprint-authoring-status-gm0akp --no-edit   # rule 7
python3 scripts/tracker-counts.py --check                              # expect 63 / 105
```

Then read the live handoff's **§1**, which is what remains.

---

## 1 · ⛔ What Batch 42 still owes

| | | |
|---|---|---|
| **§1** | ⭐⭐ **project the source — the `Local Variables` section** | **NOT STARTED.** The blocker for ticking `BP-57` |
| **§4** | the node badge | **NOT STARTED.** ⚠ Moves **NodeEdit Core (208)** and **UI (131)**; the handoff's sanctioned stop point |
| **§5** | doc comment · **the tracker** | **NOT STARTED.** ⚠ Batch 41 did not touch the tracker either — `BP-57`'s row records **none** of the editor work |

⭐ **§1 is mostly wiring, not building.** `BlueprintLocalVariableSchemaSource` is complete, tested and
**still orphaned** — nothing constructs it outside its own tests. What §1 needs:

| | |
|---|---|
| a sixth section in `BlueprintMyBlueprintModel` | `_sections` is `static readonly`; `Retarget(IEditableAsset?, BlueprintAsset?)` is **asset-only** |
| the current graph | ✅ **the wiring exists**: `AiCanvasContext.CurrentGraphId`, already consumed by `GraphSignatureWindow` (**`BP-72`**). ⛔ Do not invent a second mechanism. `EditorSubsystem.cs:~2280` already passes it to the signature window two calls below the My-Blueprint retarget |
| the source follows the canvas for free | it reads through a **delegate**, never a captured `Graph` |
| ⭐ **the `[+]` refusal** | the source already refuses a `Macro` graph **out loud** via its `refuse` callback with a message naming `BP1664`'s reason. §1 must route that to `IEditorIndicators` (the surface `BP-223` repaired). ⛔ **A silently missing button is not an option** (`Q26-B2`, `BP-76`, `BP-77`) |

---

## 2 · What Batches 41–42 actually shipped

| commit | |
|---|---|
| `3e79c1c` | **§1 · the locals schema source** — `BlueprintLocalVariableSchemaSource`, an `IVariablesSchemaSource` over `Graph.LocalVariables` |
| `748f1f7` | **§3 · picker + title** — locals offered in `variables.all`; the raw-GUID bug fixed |
| `57cd616` | **delete refuses while referenced · undo on every gesture** |

### ⭐ Decisions taken — do not re-litigate

| | |
|---|---|
| **Written to the SHARED interface** | so the unification's `U-6` **absorbs** it instead of undoing it. ⛔ It *implements* `IVariablesSchemaSource`; it never *changes* it — that is `U-5`'s `V2` and would move the **AiShared** gate |
| **`Role`/`Scope` setters not implemented** | `Q-k` ruled them read-only for blueprints. They are default-bodied members; leaving them is the contract's intended shape |
| ⭐⭐ **`CountNodesReferencingVariable` counts BY ID, ACROSS THE ASSET** | by id because `FindLocalIndex` has no name fallback (a node carrying the NAME is not a reference); across the asset because a node in **another graph** carrying the id is the dangling case `BP1670` refuses |
| **`IsUnused` follows that count** | the asset-variable source hardcodes `false`; this one can afford the truth |
| ⭐ **Delete REFUSES while referenced**, naming the count and the graphs | a delete that silently removes the designer's nodes is the bigger surprise, and the repo already ruled that way (`DeleteItem`'s own comment). ⚠ **Diverges deliberately in one direction:** a local's references can sit in a graph the designer cannot see from the current canvas, so the count tells them something `BP1670` would only reveal after a build |
| ⭐ **That ruling makes the undo honest for free** | no nodes are ever removed ⇒ the undo entry has only declarations to restore ⇒ `BP-225`'s "restored the decl and forgot the references" trap is **unreachable**, not merely avoided |
| **Undo = snapshot, all graphs, deep copies** | mirroring `RecordItemEdit`. All graphs because a BP-24 graph switch between edit and undo must not silently restore nothing; deep because rename mutates in place |
| **No-op gestures record no entry** | `BP-204`'s degenerate case |
| **Picker widened to locals and NOTHING else** | ⛔ `WorkingState`/`Parameters` are `BP-226`'s unfixed space; struct FQNs are `BP-228`'s. Test-locked |
| **A `Macro` graph is read-only, not absent** | `BP1664`. A surface that vanishes teaches nothing |

---

## 3 · Where the code is

| file | |
|---|---|
| `Hrot.Blueprints.Editor/Variables/BlueprintLocalVariableSchemaSource.cs` | ⭐ the source. `record`/`refuse` are optional ctor args (4th/5th) |
| `Hrot.Blueprints.Editor/Host/BlueprintDocumentFactory.cs` | `LocalVariableUndoRecorder` + `SnapshotLocals`/`RestoreLocals`; picker registration at ~`:279` passes `() => switcher.CurrentGraph` |
| `Hrot.Blueprints.Editor/Host/BlueprintPickerSources.cs` | `BlueprintVariablePickerSource(asset, currentGraph)` · `RowLabel` is the headless seam for the `(local)` suffix |
| `Hrot.Blueprints.Editor/Host/BlueprintNodeModel.cs` | `ResolveVariableName` — now also searches every graph's `LocalVariables` |
| **tests** | `Tests/Editor/LocalVariableAuthoringTests.cs` (11) · `LocalVariablePickerAndTitleTests.cs` (9) · `LocalVariableDeleteAndUndoTests.cs` (9). **42 locals tests total, all green** |

⚠ **`BlueprintMyBlueprintModel.cs` is UNTOUCHED** — that is §1.

---

## 4 · Gates

The eight, solution **`IOS-IG-SimHost.sln`** (⚠ **not** `Hrot.sln`).
⚠⚠ **The two NodeEdit gates take NO `--no-build`** — they silently do not run with it.

**Handoff baseline, post-Batch-41:** build **0 errors / 69 warnings** · Blueprints **3289 total /
3279 passed / 0 failed / 10 skipped** · ⭐ **AiShared 1213 — must NOT move** · BTree **612** ·
Breakpoints **130** · Generators **193** · NodeEdit Core **208** · UI **131**.

⚠ **A closing INCREMENTAL build under-reports warnings.** Record that honestly rather than printing
`69` from memory — Batch 38 set that precedent and Batch 40 repeated it.

⛔ **The visual check has not run for SEVEN batches.** §1 and §4 are exactly what it would catch.
**Say so; never imply coverage.**

---

## 5 · Open findings that are mine

| | |
|---|---|
| **BP-228** 🔴 | struct type ids are **unvalidated pass-through** — `Totally.Made.Up.Type` compiles clean. Blocks the unification's `U-8` |
| **BP-229** 🔴 | `Compile` **mutates the caller's `Graph` objects** (the macro splice). Not reachable in production only because `QuickReloadService` has no caller |
| **BP-230** 🔴 | the **asset-variable** source's `Role`/`Scope`/reference-count members are stubs. ⭐ My locals source does not copy them; fixing the old one is `U-5` |
| **BP-231** 🟠 | `RemoveVariable`/`RenameVariable` don't maintain the order lists. ⭐ **Locals are immune** — the declaration list IS the order |
| **BP-232** 🟠 | `MakeUniqueName` checks `Variables` only ⇒ a `Parameter` and a `Variable` may share a name |
| **BP-233** 🟠 | `BP1650` carries a **fourth** copy of the latency predicate, still missing the inline-action case. Half-closed: `MacroLatency` is fixed |
| **BP-226 / BP-227** | the index space · the numeric `Dispatch` (**7** files, corrected Batch 40) |

📌 **A finding worth promoting** (handoff §2 asks): reordering locals is **not cosmetic for a
suspending graph** — declaration order feeds `FieldLayout` ⇒ `StructureHash` ⇒ **the blackboard is
re-initialised on next run.** Correct behaviour, but a designer dragging a row will not expect it.
**Decide whether it warrants a warning and record the reasoning either way.**

---

## 6 · ⚠ Process lessons — paid for, do not re-learn

| | |
|---|---|
| ⛔⛔ **NEVER `git checkout --` to undo a revert-probe** | It resets the file to **HEAD**, discarding *uncommitted* work. It cost the §2/§3 source edits this batch (recovered by re-applying). ⭐ **Un-apply the probe with the inverse edit instead** |
| ⛔ **`mv $F.bak $F` back-dates the file** | MSBuild then skips the recompile and the reverted binary survives. **`touch` after restoring** |
| ⛔ **Delegation + a dirty tree do not mix** | Sub-agents share ONE working tree, so builds must be sequential — which means holding uncommitted edits while an agent runs. **Commit your own work first, then delegate into a clean window** |
| ⭐⭐ **A revert that stays GREEN is a finding about your TESTS** | never evidence the fix was unnecessary |
| ⭐ **Confirm the handoff's claims before building on them** | this session has corrected the coordinator in **every batch since 29** — most recently by killing a gate the plan called its strongest (`U-10`'s byte identity: **0 of 58** shipped files survive even `Deserialize→Serialize`) |
| ⭐ **Report what you did NOT do** | Batch 41 stopped after §1/§3 and did not say so; the coordinator had to measure it. **Say where you stopped** |

---

## 7 · The wider programme

`BP-57` is the **last mile** of the locals feature: the compiler half is complete (Batches 37 + 39 —
Q27-A3 blackboard slots for suspending graphs, reset in the entry block, plus `BP1670`).

⏭ **After `BP-57` closes, the unification begins** —
📄 [PLAN_Variable_Unification_Tasks.md](PLAN_Variable_Unification_Tasks.md), reviewed by
📄 [REVIEW_Unification_Plan.md](REVIEW_Unification_Plan.md) (**run it, with five named changes**).
⭐ The two headline review findings: **`U-3` should go first** (four call sites, closes `BP-226`), and
**`U-10`'s byte-identity gate needs a corpus canonicalisation pre-step** or it is unwritable.
