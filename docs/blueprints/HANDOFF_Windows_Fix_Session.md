# HANDOFF — Windows fix session (2026-08-08)

> **Read this first, in full. It is self-contained.** You are picking up a live programme mid-flight;
> everything you need is below or one link away.

---

## 1. Who you are and where you are

You are continuing the **Blueprint gaps & QoL programme** in the `HROT` repo — an Unreal-style visual
scripting subsystem (compiler + ImGui editor) written in C#/.NET 8.

| | |
|---|---|
| **Repo** | `pjanec/HROT` |
| **Branch** | `claude/blueprint-authoring-status-6sr5ld` — **develop, commit and push only here** |
| **HEAD at handoff** | see `git log -1`; the last commit is docs-only |
| **Source of truth** | [Blueprint_Issues_Tracker.md](Blueprint_Issues_Tracker.md) (checklist, every row deep-links) · [Blueprint_Issues_Detail.md](Blueprint_Issues_Detail.md) (evidence per issue) |
| **Orientation** | [Blueprint_Gaps_Programme_RESUME.md](Blueprint_Gaps_Programme_RESUME.md) — batches shipped, traps, test baseline |

**Why you are on Windows.** Batches 9–18 shipped and were verified only by headless tests. A visual
check was started on Windows and **found three real defects in the first ten minutes** — the class of
bug no test in this repo can see. You are here to fix those three and confirm them in the running
editor, because that is the only place they are observable.

### The working agreement (from the user — these are binding)

- **Verify claims against code.** Nine audit claims and two architect statements have been wrong so far.
  Do not trust a doc — including this one — over the source.
- **Fix, don't disable.** No skipping tests.
- **Record findings in the detail doc**, not just commit messages.
- **Ask in plain prose. Never use a multiple-choice widget.**
- **Revert your fix and confirm the test goes red.** This is *required*, not optional — it has caught a
  test that passed against the bug twice (see trap #9).

---

## 2. What is already confirmed WORKING — do not re-litigate

| Feature | Evidence |
|---|---|
| **BP-71** — a Function graph can return a value | On `SquadState1`, the Return node showed a value pin **named after the declared output**, as an **input**, and it **accepted a wire** from `GetVariable`. The saved `.bp.json` contains the link with `ToNodeId` = the Return node |
| EventEntry pin projection | One `Out` exec pin on a 0-input Function graph |
| Function graph create + open | `Functions +` creates the graph and the canvas switches to it |
| Save / reload round-trip | Two authored function graphs survived a close-and-reopen intact |

### Four things that LOOK like bugs and are not

Recorded so you don't burn time on them:

1. **`"Header": {}` on save is correct** — the `$meta` envelope superseded those fields (D-021,
   `GraphTypes.cs:162`).
2. **`"VariableId": "var:<guid>"` is a tolerated form by design** —
   `BlueprintDocumentFactory.cs:1083-1085` writes it deliberately; every reader strips the prefix.
3. **New-from-Recipe writes the file before you save** — by design; `NewFromRecipeService` returns an
   unregistered asset "ready for the host to save and register".
4. **Adding *Input* params leaves the Return node unchanged** — correct. Inputs surface as data-**out**
   pins on the **entry** node; the Return node reflects **Outputs**. This confusion cost the user real
   time and is the entire argument for BP-85.

---

## 3. The three bugs to fix

Ordered by *confidence × cheapness*. **BP-86 first** — it is fully diagnosed and it is data corruption.

### 🔴 BP-86 — corrupted parameter names (**diagnosed, fix known**) → [detail](Blueprint_Issues_Detail.md#bp-86)

**Symptom.** Add function input params, rename them to something **shorter** than the generated default
(`Param0` → `P1`). The name becomes `P1␀am0`, rendered `P1?am0`. It is **persisted** to `.bp.json` and
reaches the compiler as an identifier containing a NUL.

**Root cause** — `Windows/GraphSignatureWindow.cs:343-345`:

```csharp
var newName = System.Text.Encoding.UTF8
    .GetString(nameBuf)      // decodes ALL 256 bytes
    .TrimEnd('\0');          // strips only TRAILING nulls
```

`ImGui.InputText` writes the new text plus a terminator and **leaves the rest of the buffer untouched**:

```
offset:  0    1    2    3    4    5    6
before: 'P'  'a'  'r'  'a'  'm'  '0'  \0     "Param0"
after:  'P'  '1'  \0   'a'  'm'  '0'  \0     wrote "P1\0"; bytes 3-5 are stale
                  ↑ terminator
```

`GetString` over the whole buffer → `"P1\0am0\0…"`; `TrimEnd('\0')` removes only trailing nulls ⇒
`"P1\0am0"`. Fires **whenever the new value is shorter than the old one**.

**The fix — truncate at the FIRST null:**

```csharp
int len = Array.IndexOf(nameBuf, (byte)0);
if (len < 0) len = nameBuf.Length;
var newName = System.Text.Encoding.UTF8.GetString(nameBuf, 0, len);
```

**⚠ Do not fix only this site. There are seven, all the same latent defect:**

| # | Site |
|---|---|
| 1 | `Hrot.Blueprints.Editor/Windows/GraphSignatureWindow.cs:345` ← the reported one |
| 2 | `Hrot.Hsm.Editor/Windows/HsmEventsWindow.cs:110` |
| 3 | `Hrot.Editor.AiShared/Windows/InspectorWindow.cs:231` |
| 4 | `Hrot.Editor.AiShared/Blackboard/VariablesPanelControl.cs:561` |
| 5 | `Hrot.Editor.AiShared/Blackboard/VariablesPanelControl.cs:562` |
| 6 | `Hrot.Editor.AiShared/Blackboard/VariablesPanelControl.cs:640` |
| 7 | `Fdp.Presentation/ImGui/Panels/ImGuiFileDialogService.cs:184` |

Sites 4–6 chain `.Trim()`, which does **not** help — `Trim()` does not remove an interior NUL.
**Put one shared helper somewhere both trees can reach and route all seven through it.** Suggested:
`Hrot.Editor.AiShared` for sites 1–6; site 7 is in `FDP/` and may need its own copy (the dependency runs
Hrot → FDP, so a shared helper cannot live in Hrot and be used by FDP — check before assuming).

**Tests to add.** One per behaviour, not one per site:
- decode a buffer seeded `"Param0"` then overwritten with `"P1\0"` ⇒ expect exactly `"P1"`;
- longer-than-original and exactly-equal-length cases (no regression);
- an empty buffer ⇒ empty string, not a throw;
- **a round-trip through `RenameParameter` asserting `ParameterDecl.Name` contains no `\0`.**

### 🔴 BP-84 — undo restores a node without its pins (**diagnosed**) → [detail](Blueprint_Issues_Detail.md#bp-84)

**Symptom.** Delete a `GetVariable` node in a Function graph, **Ctrl+Z** — the node returns **without its
`Value` output pin** and can never be rewired.

**Already ruled out. Do not re-search these:**

| Suspect | Why it's out |
|---|---|
| The undo lost the node's data | `DeleteNodeCommand.Undo` (`GraphEditor/GraphCommands.cs:57-62`) re-adds the **same `Node` object**. Nothing is reconstructed |
| The pin projection failed | `NodePinSchema.GetVariablePins:670-679` returns one `Value` out-pin **unconditionally** — even an unresolvable id yields a pin |
| **Node ordering not restored** | ✅ **Experimentally excluded.** The user added a second `GetVariable`, wired it, deleted the first, Ctrl+Z: **only the restored node lost pins**; the sibling was untouched. So do **not** change `Undo`'s `Nodes.Add` to restore an index — that was the expensive theory and it is dead |

⇒ **The node view-model is not rebuilt (or its pin list not invalidated) on the undo path.** Look at how
`BlueprintGraphModel` rebuilds and what the undo path notifies — `NotifyChanged()` fires
`GraphChangeKind.Wholesale`; check whether the undo route actually reaches it.

**Open question worth 10 seconds first:** does closing and reopening the asset heal the node? Pins are
stripped on save and re-projected on load, so it probably does — which would mean this is
render-until-reopen rather than durable damage. **Confirm before you write the fix's severity into the
docs.**

### BP-85 — the canvas never says which graph you are editing → [detail](Blueprint_Issues_Detail.md#bp-85)

**Symptom.** The tab shows only the asset name (`SquadState1`). Creating a function correctly switches
the canvas, but with nothing labelled it reads as *"my graph has been emptied"* — a false data-loss
scare. Nothing states the asset's dispatch either, so *"is this an Instance blueprint?"* has no on-screen
answer.

**Fix.** Show the active graph's **name and kind** in the canvas tab/breadcrumb, plus the asset's
dispatch next to the asset name. BP-72 already routes canvas-switch events via `BlueprintGraphSwitcher`,
so the signal exists. Consider surfacing the active graph's **signature summary** too — that also answers
the "why does my Return node have no pin" confusion in item 4 of §2.

---

## 4. Build and test

```bash
# build
dotnet build IOS-IG-SimHost.sln -v q --nologo

# the eight gates (all headless) — run at least the first three for these fixes
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj -v q --nologo
dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj -v q --nologo
dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Hrot.BTree.Editor.Tests.csproj -v q --nologo
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj -v q --nologo
dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj -v q --nologo
dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj -v q --nologo
dotnet test Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/Hrot.AiEditor.Generators.Tests.csproj -v q --nologo
```

⚠ **BP-86 touches `Hrot.Editor.AiShared` and `Fdp.Presentation`** — both are shared by the HSM and BTree
editors. Run the AiShared and BTree gates, not just blueprints.

**Baseline to beat (batch 16, Linux):** blueprints **2845 / 2835 passed / 10 skipped** · NodeEdit core
**208** + UI **131** · AiShared **1204** · BTree editor **612** · breakpoints **130** · generators
**189** · solution build **0 errors / 58 pre-existing warnings**.

**Two known flakes** — re-run the single filter before calling either a regression:
`PdbEmbeddedSourceTests` (real Roslyn+PDB emission) and
`WhenNodePerfTests.WhenNode_ValueChanged_Under100ns_perTick` (a wall-clock benchmark; reds under load).

**To classify a failure:** `git stash` → re-run the same filter → `git stash pop`. If it fails
identically without your changes, it predates you.

---

## 5. Verify in the editor — the whole point of being on Windows

### Fixture setup (there is almost nothing shipped to use)

⚠ **Exactly one shipped asset anywhere has a Function graph with an output.** All 12 other recipes and
every asset under `Assets/Blueprints` have **zero** outputs on every graph.

- **`Assets/Blueprints`** = the asset browser (browse/save).
- **`Recipes/Blueprints`** = **New from Recipe** only.

⇒ Use **New from Recipe → `SquadState`** ("Squad Shared State (template)", category *Shared*). It gives
you an Instance blueprint with graph `GetThreatLevel` (`Kind=Function`, 1 output `ThreatLevel`) and three
nodes. **Open it via My Blueprint → Functions → double-click `GetThreatLevel`** (single-click and drag
both do nothing — that is BP-75, separately tracked and expected).

### Verification steps

**BP-86:**
1. Graph Signature → **Inputs +** three times → they appear as `Param0/1/2`.
2. Rename each to something **shorter**: `P1`, `P2`, `P3`.
3. ✅ The entry node's pins read exactly `P1`, `P2`, `P3` — no `?`.
4. **Save, then open the `.bp.json`** and confirm `Graphs[].Inputs[].Name` is exactly `"P1"`.
5. Repeat with a **longer** rename (`P1` → `LongerName`) — must also be exact.
6. Do the same in the **HSM events window** and the **Blackboard variables panel** (sites 2–6), and in
   the **file dialog** (site 7): rename an existing entry to something shorter.

**BP-84:**
1. Place two `GetVariable` nodes, wire one.
2. Delete one → **Ctrl+Z**.
3. ✅ The node returns **with** its `Value` pin and accepts a new wire.
4. Ctrl+Y then Ctrl+Z again — still correct.
5. Save, reopen — still correct.

**BP-85:**
1. With `GetThreatLevel` open, confirm the canvas shows the **graph name** and its kind.
2. Press **Functions +** → the canvas switches and **plainly says** you are now on the new graph.
3. Double-click back → the label follows.

### Then continue the visual check — the T-series is the biggest untested thing

The full checklist is
[Blueprint_Gaps_Programme_RESUME.md § Visual check](Blueprint_Gaps_Programme_RESUME.md). **T1–T7 (BP-73,
N function outputs) is the highest-value unverified item.** On any function graph, add **three
Outputs** in Graph Signature (Outputs, *not* Inputs) and check the Return node grows three left-side
pins, that a call node shows three data-outs, and that it compiles.

⚠ **T7 especially:** adding a **second** output must now **compile cleanly**. BP-73 retired `BP1656`. Any
surviving "multiple outputs not supported" diagnostic is a **leftover and a defect** — an older revision
of the checklist told the checker to *expect* BP1656, which was wrong and is struck through.

---

## 6. Traps that have each cost real time here

Full list in the RESUME doc; these four are the ones live in this work.

| # | Trap |
|---|---|
| **5** | **`default:`-returns-success.** `BlueprintCommandSink.Apply`'s `default:` arm returns `new GraphCommandResult(true, null)` for any unhandled command. A feature can be fully built and silently do nothing while reporting success. **A test asserting `Success` proves nothing — assert the effect.** Four instances so far |
| **9** | **Two halves of a contract, each tested alone, never together.** BP-71 survived a 2788-test suite because no test performed the *designer's gesture*. **Revert your fix and confirm the test goes red** — this caught BP-69's own test passing against the bug |
| **new** | **An idiom that looks defensive and isn't.** `TrimEnd('\0')` reads like careful buffer handling and is the BP-86 bug — seven times over. `Trim()` does not save it either. When you see a defensive-looking idiom repeated, check that it actually defends |
| — | **Absence claims must be checked across BOTH trees** (`Hrot/` *and* `FDP/`), and **against the tracker's DONE rows.** Seven "nothing exists" claims have been overturned; the most recent was our own code, shipped by this programme (`BlueprintClipboard.Rehydrate`) |

---

## 7. Definition of done

- [ ] All seven `TrimEnd('\0')` sites fixed behind a shared helper (or documented why one cannot share).
- [ ] BP-84's view-model rebuild fixed; the reopen-heals question answered in the detail doc.
- [ ] BP-85 shows the active graph name + kind.
- [ ] New tests for each, and **each one confirmed to go red when the fix is reverted**.
- [ ] The gates above green against the batch-16 baseline; any failure classified via `git stash`.
- [ ] **Verified in the running editor** per §5 — not only headless.
- [ ] Tracker rows moved to `[x]`, detail entries given a `DONE` note, **header counts reconciled**
      (currently **46 open / 43 done**; the checkbox tally and the complexity table must agree).
- [ ] Committed and pushed to `claude/blueprint-authoring-status-6sr5ld`.
