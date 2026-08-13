# RESUME — implementation session · ⭐⭐ **`BP-57` is CLOSED**

> **Written for a fresh session. Self-contained; assumes no prior conversation.**
> **You are the *implementation* session.** A separate *coordinator* session owns the tracker and
> writes the handoffs. Last updated **2026-08-13**.
>
> ✅ **Batch 43 is COMPLETE and reported.** The Local Variables section landed, all eight gates are
> green, and **`BP-57` is ticked** — the locals feature is done end to end, compiler and editor.

| | |
|---|---|
| **Repo** | `pjanec/HROT` |
| **Implementation branch — PUSH HERE** | ⭐ **`claude/hrot-implementation-j1jvin`** |
| **Coordinator branch — do NOT push** | ⭐ **`claude/blueprint-authoring-status-gm0akp`** (was at `93152d7`, merged into mine) |
| **Last handoff** | 📄 **[HANDOFF_Batch43_Local_Variables_Section.md](HANDOFF_Batch43_Local_Variables_Section.md)** — delivered in full |
| **Counts** | **63 open · 106 done** — ⚠ *derive, never hand-count:* `python3 scripts/tracker-counts.py --check` |
| **Next free ids** | rows **BP-235+** · diagnostics **BP1671+** |

⛔ **No PR unless the user explicitly asks.** There has never been one in this programme.
⛔ **Never put a model identifier** in a commit message, code comment, or anything else pushed.

---

## 0 · First actions, in this order

```bash
git fetch origin claude/blueprint-authoring-status-gm0akp
git merge origin/claude/blueprint-authoring-status-gm0akp --no-edit   # rule 7
python3 scripts/tracker-counts.py --check                              # expect 63 / 106
```

Then read whatever handoff is newest on that branch. **No batch is in flight.**

---

## 1 · What Batches 41–43 shipped — `BP-57` end to end

| commit | |
|---|---|
| `3e79c1c` | **the locals schema source** — `BlueprintLocalVariableSchemaSource`, an `IVariablesSchemaSource` over `Graph.LocalVariables` |
| `748f1f7` | **picker + title** — locals offered in `variables.all`; the raw-GUID bug fixed |
| `57cd616` | **delete refuses while referenced · undo on every gesture** |
| `f116266` | ⭐⭐ **the Local Variables SECTION** — the surface all of the above was built behind |

### ⭐ Decisions taken — do not re-litigate

| | |
|---|---|
| **Written to the SHARED interface** | so the unification's `U-6` **absorbs** it instead of undoing it. ⛔ It *implements* `IVariablesSchemaSource`; it never *changes* it — that is `U-5`'s `V2` and would move the **AiShared** gate. ✅ AiShared held at **1213** across all three batches |
| ⭐⭐ **`CountNodesReferencingVariable` counts BY ID, ACROSS THE ASSET** | by id because `FindLocalIndex` has no name fallback (a node carrying the NAME is not a reference); across the asset because a node in **another graph** carrying the id is the dangling case `BP1670` refuses |
| ⭐ **Delete REFUSES while referenced**, naming the count and the graphs | matches `DeleteItem`'s existing policy, and ⚠ **diverges in one direction deliberately**: a local's references can sit in a graph invisible from the current canvas |
| ⭐ **That ruling makes the undo honest for free** | no nodes are removed ⇒ `BP-225`'s trap is **unreachable**, not merely avoided |
| **Undo = snapshot, all graphs, deep copies** | mirroring `RecordItemEdit`. No-op gestures record no entry (`BP-204`) |
| ⭐⭐ **The section follows the canvas through `Func<Guid>`** | `AiCanvasContext.CurrentGraphId` — **the mechanism `BP-72` already chose** for the signature window. ⛔ Not a second one: the switcher is per-document (factory), the model is per-perspective (composition root), and neither references the other |
| ⭐ **Present and EMPTY, never absent** · **`[+]` present on a Macro graph and refusing out loud** | `Q26-B2`. Forced anyway — `_sections` is `static readonly`, so `CanCreateItems` cannot vary per graph |
| ⭐ **`local:` items route to the source, not to `RenameItem`/`DeleteItem`** | `RecordItemEdit`'s snapshot covers the asset's declaration lists only ⇒ routing locals through it yields an undo that restores nothing |
| **Picker widened to locals and NOTHING else** | ⛔ `WorkingState`/`Parameters` are `BP-226`'s space; struct FQNs are `BP-228`'s. Test-locked |

---

## 2 · Where the code is

| file | |
|---|---|
| `Hrot.Blueprints.Editor/Variables/BlueprintLocalVariableSchemaSource.cs` | ⭐ the source. `record`/`refuse` are optional ctor args (4th/5th) |
| `Hrot.Blueprints.Editor/Windows/BlueprintMyBlueprintModel.cs` | ⭐ the section: `SectionLocalVariables`, `CommandCreateLocalVariable`, `CurrentGraph`, `SyncCurrentGraph()` |
| `Hrot.Blueprints.Editor/Windows/BlueprintMyBlueprintWindow.cs` | builds the source + modal; `Refuse` → `IEditorIndicators`; `LastRefusal`/`Locals` are the headless seams |
| `Hrot.Blueprints.Editor/Host/BlueprintDocumentFactory.cs` | `LocalVariableUndoRecorder` · `TryFindLocal` · `DuplicateLocal` · `MakeUniqueLocalName` · `RegisterCreateLocalVariableCommand` |
| `Hrot.Blueprints.Editor/Host/BlueprintPickerSources.cs` | `RowLabel` is the headless seam for the `(local)` suffix |
| `Hrot.Subsystems/Hrot.Editor/EditorSubsystem.cs` `~:2261` | passes `ctx?.CurrentGraphId` and `ctx?.Indicators` to the My Blueprint window |
| **tests** | `Tests/Editor/LocalVariableAuthoringTests.cs` (11) · `LocalVariablePickerAndTitleTests.cs` (9) · `LocalVariableDeleteAndUndoTests.cs` (9) · `LocalVariableSectionTests.cs` (15). **44 locals tests, all green** |

---

## 3 · Gates

The eight, solution **`IOS-IG-SimHost.sln`** (⚠ **not** `Hrot.sln`).
⚠⚠ **The two NodeEdit gates take NO `--no-build`** — they silently do not run with it.

**Post-Batch-43, all eight run:** build **0 errors / 69 warnings** · Blueprints **3313 total / 3303
passed / 0 failed / 10 skipped** · ⭐ **AiShared 1213 — unmoved** · BTree **612** · Breakpoints **130** ·
Generators **193** · NodeEdit Core **208** · UI **131**.

### ⭐ Run the five `--no-build` suites in PARALLEL

Measured this batch: sequential ≈ 3 m 40 s of test execution, parallel ≈ **2 m 05 s**, bounded by
Blueprints. They only read the tree. The two NodeEdit gates must stay sequential (they build).

### ⚠ Two gate-script traps, both paid for

| | |
|---|---|
| ⛔ **`grep -E "Passed!\|Failed!"` DROPS the `[FAIL]` line** | so a flake reports a number and loses its identity — that happened in Batch 42 and the test could not be named. ⭐ **Always include `\[FAIL\]` in the pattern** |
| ⚠ **`--logger "console;verbosity=normal"` prints `Test Run Successful.` + `Total tests:`**, not `Passed!` | grep for both forms |

⚠ **A closing INCREMENTAL build under-reports warnings.** Record honestly rather than printing `69`
from memory.

⛔ **The visual check has not run for NINE batches.** *"Present and empty"* and *"follows the canvas"*
are exactly what a headless test can pass while the panel draws nothing. **Say so; never imply coverage.**

---

## 4 · Open findings that are mine

| | |
|---|---|
| **BP-228** 🔴 | struct type ids are **unvalidated pass-through** — `Totally.Made.Up.Type` compiles clean. Blocks `U-8` |
| **BP-229** 🔴 | `Compile` **mutates the caller's `Graph` objects** (the macro splice). Not reachable in production only because `QuickReloadService` has no caller |
| **BP-230** 🔴 | the **asset-variable** source's `Role`/`Scope`/reference-count members are stubs. ⭐ The locals source does not copy them; fixing the old one is `U-5` |
| **BP-231** 🟠 | `RemoveVariable`/`RenameVariable` don't maintain the order lists. ⭐ **Locals are immune** — the declaration list IS the order |
| **BP-232** 🟠 | `MakeUniqueName` checks `Variables` only ⇒ a `Parameter` and a `Variable` may share a name |
| **BP-233** 🟠 | `BP1650` carries a **fourth** copy of the latency predicate, still missing the inline-action case. Half-closed |
| **BP-234** 🟠 | ⭐ **new, Batch 43** — editing a suspending graph's locals silently re-initialises its blackboard. ⚖️ **Ruled: no per-gesture warning** — add/delete change the same hash by the same mechanism, so warning on the drag would imply the other two are safe |
| **BP-226 / BP-227** | the index space · the numeric `Dispatch` (**7** files) |

📌 **One thing the section proved and did NOT patch:** `BlueprintLocalVariableSchemaSource.AddVariable`
appends unconditionally, so a **modal** can create two locals of one name. The guard lives in
`BlueprintMyBlueprintWindow.CreateLocalVariable`, host-side, so the source's contract stays as `U-6`
will find it. ⚠ Reported rather than changed, per the handoff's instruction.

---

## 5 · ⚠ Process lessons — paid for, do not re-learn

| | |
|---|---|
| ⛔⛔ **NEVER `git checkout --` to undo a revert-probe** | It resets the file to **HEAD**, discarding *uncommitted* work. It cost the §2/§3 source edits in Batch 42. ⭐ **Un-apply the probe with the inverse edit instead** — three probes were run that way this batch with no loss |
| ⛔ **`mv $F.bak $F` back-dates the file** | MSBuild then skips the recompile and the reverted binary survives. **`touch` after restoring** |
| ⛔ **Delegation + a dirty tree do not mix** | Sub-agents share ONE working tree ⇒ builds must be sequential ⇒ you hold uncommitted edits while an agent runs. **Commit first, then delegate into a clean window** |
| ⭐⭐ **A revert that stays GREEN is a finding about your TESTS** | never evidence the fix was unnecessary |
| ⭐ **Confirm the handoff's claims before building on them** | this session has corrected the coordinator in **every batch since 29**. This batch: the handoff said `Changed` must fire *"or the panel shows the previous graph's locals"* — ⛔ **wrong against the code**: `MyBlueprintPanel.DrawSections` calls `GetItems` **every frame** and its `Changed` handler is an empty lambda. The delegate is what makes it follow the canvas; `Changed` is the contract |
| ⭐ **Report what you did NOT do** | Batches 41 and 42 both stopped early without saying so, and the coordinator had to measure it |
| ⭐ **Check what a shared menu offers before assuming an item is inert** | `MyBlueprintContextMenu` offers **Duplicate** for every `IsRenamable` item — so the locals rows needed a duplicate arm that no handoff asked for, or the entry would have appeared and done nothing |

---

## 6 · The wider programme

⏭ **`BP-57` is closed, so the unification begins** —
📄 [PLAN_Variable_Unification_Tasks.md](PLAN_Variable_Unification_Tasks.md), reviewed by
📄 [REVIEW_Unification_Plan.md](REVIEW_Unification_Plan.md) (**run it, with five named changes**).
⭐ The two headline review findings: **`U-3` should go first** (four call sites, closes `BP-226`), and
**`U-10`'s byte-identity gate needs a corpus canonicalisation pre-step** or it is unwritable.

📌 **Still unbuilt from the locals programme, deliberately deferred:** the **node badge** distinguishing
a local from an asset variable on the canvas. It needs a new member on `INodeModel` (`NodeEditor.Core`)
plus rendering in `NodeEditor.UI`, so it **moves the NodeEdit Core (208) and UI (131) gates** — the
reason three handoffs made it the stop point. The picker's `(local)` suffix disambiguates at pick time;
on the canvas the two still render identically.

📌 **Housekeeping the coordinator must do:** delete `claude/batch39-locals-preserved` (fully merged;
this session gets HTTP 403 on branch deletion).
