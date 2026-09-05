# HANDOFF — Batch 36: `Expand Node` — ⚠ **not a greyed menu item, a corrupting one**

> 📌 **Dispatched at `a4986bed`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⭐ **Rule 7 is yours:** branch from this branch, and re-sync from it at the **start** of your run.
> ⭐ **Rule 4 is yours:** pull it again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** `BP-76`, `BP-82` are *referenced*. **You allocate**
> anything new (rule 5).
>
> ⭐ **This closes the macro programme.** After it: only `BP1664`, which is **unbuildable until `BP-57`**.
>
> ⚠⚠ **Read §1 before touching the gate.** `BP-76` reads like *"two boolean expressions are wrong."*
> **It is not.** The gate is currently the only thing preventing a **corrupting** path.

---

## 0a. ⚡ How to work — the standing rules

**You are on Opus. Delegate to Sonnet everything that does not need Opus-level reasoning.**

| Item | 🔴 Opus keeps | 🟢 Sonnet takes |
|---|---|---|
| **1** the corrupting path | ⭐ **all of it** — read §1 first | — |
| **2** the shared splice | ⭐ **the reuse decision** (§2.2) | the tests |
| **3** Go to Definition | — | ⭐ **entirely** |
| **4** shared-UI cleanup | the B2 pattern | the edit |
| **5** `BP-82`'s two rails | — | ⭐ **entirely** |

⚠ **Sub-agents share ONE working tree** — sequential only:
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```
⚠ **Gate every commit on the fix being in the tree**, not on an agent reporting success.

| | |
|---|---|
| **Push to** | your implementation branch, **branched from this one** (rule 7) |
| **Rule 6** | The **tracker + detail docs are yours** for this batch |
| **Revert-goes-red** | Every fix, **never delegated** |
| **Commit per item** · **stop cleanly at a boundary** · **no PR** | |

---

## 1. ⛔⛔ Why `BP-76` is not what its row says

✅ **All coordinator-verified at this head.** `CanvasRenderer.cs:770-810`:

```csharp
bool canExpand = node != null && (node.Kind.Id == "Function.Call" ||
                                  node.Kind.Id == "Macro.Call" || node.Title == "ScaleBy");
if (ImGui.MenuItem("Expand Node", null, false, canExpand))
{
    …
    var n1Id = IdGenerator.DeterministicNodeId(target.Node.Value.ToString() + "_exp1");
    var n2Id = IdGenerator.DeterministicNodeId(target.Node.Value.ToString() + "_exp2");
    invs.Add(new GraphCommand.RemoveNodes(new[] { n1Id, n2Id }));      // ⚠ "exact inverse"
    …
    view.Execute(fwd, new GraphCommand.Batch("Undo Expand", invs), "Expand Node");
}
```

⭐ **`_exp1`/`_exp2` occur in exactly TWO files in the repository:**

| | |
|---|---|
| `NodeEditor.Demo/FakeCommandSink.cs:520,526` | **mints** them — the demo backend expands to exactly two nodes |
| `NodeEditor.UI/Canvas/CanvasRenderer.cs:791,792` | **predicts** them — shared production UI |

⇒ ⭐⭐ **Shared UI hardcodes the demo backend's id-minting scheme and builds an "exact inverse" out of
it.** For a real macro, expansion produces **N** nodes with entirely different ids.

**And the forward does nothing.** ✅ Verified: `BlueprintCommandSink` has **no `ExpandNode` case**, so
it reaches the *"unknown commands are silently accepted"* `default:` and **returns success** — the
third member of that family after the clipboard commands and collapse.

⇒ ⛔ **Ungating this without fixing it gives you: a forward that changes nothing, and an undo that
removes two nodes which never existed.** ⭐ **The greyed gate is accidentally load-bearing.**
**Do not "just fix BP-76's gate."**

⚠ **Say this in the row.** `BP-76` is filed as a wiring nuisance; it is a latent corruption.

---

## 2. 🔴 The real expand — one splice, shared

### 2.1 Where it goes

⭐ **The pattern is already established and it is not negotiable-by-accident.** `Compiler/Transform/`
holds the editor-facing transforms and **they are `public`** — `CollapseAnalysis`, `CollapseEmitter`,
`GraphFragmentCloner`, `CanonicalGraphShape`. ✅ **`.Compiler`'s `InternalsVisibleTo` lists
`Hrot.Blueprints.Tests`, `Hrot.Blueprints.Core`, `Hrot.Blueprints.Compiler.Tests` — NOT `.Editor`**, and
`Stage2_5_ExpandMacros` is `internal static`. **The editor cannot reach the compiler pass, by design.**

⇒ Add a **public single-call splice** in `Compiler/Transform/`, mirroring `CollapseEmitter` — the exact
inverse living beside its forward.

### 2.2 📐 The reuse decision — yours, with a strong lean

`Stage2_5_ExpandMacros` already implements the five splice rules over a whole asset.

📐 **Lean: extract the single-call splice, and have the Stage 2.5 pass call it** — one algorithm, two
callers. ⚠ **The alternative is a second implementation of the splice**, and this repo has the receipt:
**BP-69** duplicated `ResolveCustomEventDecl` across this exact boundary and **the two copies drifted**;
Batch 30 moved the clipboard cloner *down* rather than copy it, for the same reason.

⚠ **Honest caveat:** Stage 2.5 splices a **post-Stage-2 compile-time** asset; the editor splices the
**live authored** asset. **Check that the rules are genuinely identical before merging them** — if they
are not, say where they differ rather than forcing it. ⛔ But **do not silently duplicate.**

### 2.3 ⭐ The proof — the round-trip runs the other way

Batch 33 locked **collapse → expand → canonically equal**. ⇒ **Expand gives you the mirror for free:**

> **expand → collapse → canonically equal**

`CanonicalGraphShape` is already built and already proven non-vacuous. ⭐ **Use it. This is the
strongest evidence available and it costs almost nothing.**

### 2.4 ⚠ Undo — the same lesson as Batch 34, one level sharper

⛔ **Do not predict the ids the backend will mint.** That is precisely what `CanvasRenderer` does today
and precisely why it is broken. **Snapshot the host graph; the inverse restores it** — the shape Batch
34 landed for collapse, and the reason is the same: expansion **mints fresh ids**, so a predicted
inverse is wrong the moment the backend changes.

⚠ Assert **identity**, not shape: expand → undo → the host graph is canonically equal **and the call
node is back with its original node and pin ids.**

---

## 3. 🟢 `Go to Definition` — small, and the seam exists

`CommandCatalog.GoToDefinition` (`:68`) is declared and **not registered host-side**.
⭐ **`CommandCatalog.GoToGraph` IS registered** — `BlueprintDocumentFactory:872`, and
`BlueprintMyBlueprintWindow:179` already invokes it on double-click.

⇒ **Register `GoToDefinition`**: resolve the selected call node's target graph
(`MacroCallNode.TargetGraphId` / `FunctionCallNode.TargetGraphId`) and delegate to the existing
`GoToGraph`. ⛔ Do not write a second navigation path.

⚠ A `CallCustomEvent` resolves by **name**, not id — decide what it navigates to, or refuse for it and
say so.

---

## 4. 🟢 The shared-UI cleanup — apply the pattern that already worked

Remove from `CanvasRenderer`: the **kind-id gates** (`"Function.Call"`, `"Macro.Call"`,
`node.Title == "ScaleBy"`) **and** the `_exp1`/`_exp2` inverse construction.

⭐ **Use Q26-B2, which Batch 34 proved out on collapse:** offer the item whenever a node is selected,
**let the host refuse on invoke and say why** through `IEditorIndicators.Notify` — the surface Batch 34
had to supply because nothing drained it (`BP-223`).

⇒ ⭐ **`NodeEditor.UI` then contains no blueprint vocabulary and no demo vocabulary for these two items.**
That is the same architectural payoff collapse got, applied to the row that motivated it.

⚠ **The demo must keep working** — `NodeEditor.Demo` is a consumer of this UI. **Run the NodeEdit gates
and check the demo still expands its `ScaleBy` node**, or say why that is acceptable to lose.

---

## 5. 🟢 `BP-82`'s last two rails

`BP9001` narrowing to **Function** graphs, and `BP5001` accepting **Macro-only** assets — the
forward-compat rails for a macro-library asset (Q25-C2), which *declares* macros with no call sites.

📌 ⛔ **`BP1664` stays unbuilt** — `Graph` has no `LocalVariables` (**`BP-57`**), so a macro cannot
declare a local and the rail has nothing to check. ⭐ **Leave it reserved and say so in the row**, so a
future session does not rediscover this.

---

## 6. Gates

The eight, `--logger "console;verbosity=normal"`. Solution is **`IOS-IG-SimHost.sln`** (⚠ not `Hrot.sln`).
⚠⚠ **The two NodeEdit gates take NO `--no-build`** — ⭐ **and this batch edits `NodeEditor.UI`, so they
are the ones most likely to catch you.**
⭐ **Run `python3 scripts/tracker-counts.py --check`** — clean on arrival four batches running.

**Baseline — coordinator-RUN on this tree, all eight:**

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** |
| BP diagnostics | **10 distinct**, all `BP3010`, all **authored** |
| Blueprints | **3217** / 0 / 10 skipped ⚠ *(total 3227 — `BP-111` filters 7 host-timing tests out)* |
| AiShared **1213** · BTree **612** · Breakpoints **130** | 0 failed |
| NodeEdit Core **208** · UI **131** · Generators **193** | 0 failed |

### Tests

| | |
|---|---|
| ⭐ **Round-trip, the other way** | expand → collapse → **canonically equal** (§2.3) |
| ⭐ **Run it** | expand a macro call in a tick graph ⇒ **through real Roslyn** ⇒ tick ⇒ the value the un-expanded graph produced ⚠ (`.Succeeded` never invokes Roslyn) |
| **Undo** | one entry; call node back with **original node and pin ids** |
| **The silent-success guard** | dispatch `ExpandNode` and assert **the graph changed** — ⭐ the test that would have caught the `default:` arm |
| **Latent** | expand a macro containing `Delay` ⇒ still suspends and resumes across frames |
| **Go to Definition** | on a `MacroCall` and a `FunctionCall` ⇒ the canvas is on the target graph |
| **Demo** | `NodeEditor.Demo` still expands `ScaleBy`, or a stated reason it need not |

---

## 7. Reporting

Per-suite numbers · **BP-warning count and composition** · `tracker-counts.py --check` clean ·
revert-goes-red per item · ⭐ **every id you allocated** (rule 5) · **your reuse decision** (§2.2) and,
if you did not merge the two splices, **where the rules differ** · what `Go to Definition` does for
`CallCustomEvent` · ⭐ **whether the demo still works** · anything here **wrong against the code**.

⭐ **You have corrected this coordinator in four consecutive batches** — `BP1661`'s inverted gate,
`BP-221`'s second hole, `BP-223`'s never-drained queue, and Batch 35's reorder premise, which was
**wrong in the opposite direction from the real hazard**. ⭐ **Confirming a claim rather than building
on it is what found every one of them.** Keep doing exactly that here — §1 is my reading of a menu
block, and it deserves the same treatment.
