# HANDOFF — Batch 35: authoring a macro by hand — exec-pin declarations, and `BP-77`

> 📌 **Dispatched at `<pending>`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⭐ **Rule 7 is yours:** branch from this branch, and re-sync from it at the **start** of your run.
> ⭐ **Rule 4 is yours:** pull it again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** `BP-75`, `BP-77`, `BP-80` are *referenced* existing
> rows. **You allocate** anything new (rule 5).
>
> ⭐ **Why this batch exists.** Collapse works, so a designer can *reach* a macro — but **cannot author
> one from scratch with more than one entry or exit**, because nothing in the editor declares
> `ExecInputs`/`ExecOutputs`. ⭐ **Coordinator-verified: every reference to either list in `.Editor` is
> in `NodePinSchema` — projection only.** `GraphSignatureWindow` covers data `Inputs`/`Outputs` and has
> no exec sections at all. **Today the only ways to get exec pins on a macro are collapse, or editing
> JSON by hand.**
>
> ⭐⭐ **Both halves are far more headless than they look** — see §0b. **Two clicks of real UI, once.**

---

## 0b. ⭐ What is actually headless here

| Seam | Headless? |
|---|---|
| `GraphSignatureEditModel` | ✅ the window's docs say the state lives there **"so tests can drive"** it; `ResolveEditModels()` is the hook |
| `BlueprintMyBlueprintModel` | ✅ a **model** — section contents are a pure function of the asset |
| `editor.create-macro` | ✅ a registered command — `commands.Invoke(...)`, as `ClipboardCommandTests` does |
| the palette catalog | ✅ `BlueprintNodeCatalog` is data |
| ⚠ ImGui rendering of the new rows | ❌ **the only visual part** |

---

## 0a. ⚡ How to work — the standing rules

**You are on Opus. Delegate to Sonnet everything that does not need Opus-level reasoning.**

| Item | 🔴 Opus keeps | 🟢 Sonnet takes |
|---|---|---|
| **1** exec-decl editing | ⭐ **the destructive-edit semantics** (§1.2) — this is the whole risk | the rows view + the model |
| **2** `create-macro` + section | — | ⭐ **entirely** — mirror `editor.create-function` |
| **3** the section-filter defect | — | ⭐ **entirely** (§3) |
| **4** palette + drag | the shared `BP-75`/`BP-77` shape | the catalog entries |

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

## 1. 🔴 Declaring a macro's exec pins

### 1.1 Where it goes

`GraphSignatureWindow` owns **two** `GraphSignatureEditModel`s per selected graph — Inputs and Outputs
— drawn by `ParameterRowsView.Draw` (`:218-235`), and it already branches on graph kind
(`isEventGraph`, `:213`). ⇒ **Add exec sections for `GraphKind.Macro`.**

⚠ **`ExecInDecl`/`ExecOutDecl` are `{ Id, Name, Tooltip }` — they have NO `Type`.** `ParameterRowsView`
is built around a type combo. 📐 **Your call:** a slimmer rows view, or reuse with the type column
suppressed. ⚖️ Reuse keeps add/remove/rename/reorder behaviour identical for free; a separate view
avoids a mode flag in a shared widget. **State which and why.**

### 1.2 ⚠⚠ The real risk — **editing an exec declaration is destructive, silently**

⭐ **Exec declarations are paired POSITIONALLY.** `Stage2_5_ExpandMacros` splices `execIn[k]` against
`entryExecOuts[k]` and `execOut[k]` against `returnExecIns[k]`; the projections build pins in
declaration order on **both** the boundary node **and every call site**.

⇒ **Removing or reordering an entry silently re-targets live wires.** Delete entry 0 of three and every
call site's wire to entry 1 now means entry 0 — ⭐ **a graph that still compiles and runs the wrong
path.** That is a wrong-VALUES defect, the class this programme exists to remove.

📐 **Decide and state the policy.** Options, with the tradeoff:

| | | ⚖️ |
|---|---|---|
| **a** | ⭐ **Re-map wires by declaration `Id`, not index** | ✅ reorder/delete become safe; `ExecInDecl` already carries a `Guid Id` ⇒ the information is there · ⚠ the splice still pairs by index, so the **projection** must stay index-ordered while the **rewire** keys off `Id` |
| **b** | Refuse to delete a declaration that has wires, naming them | ✅ trivial, honest · ⚠ the designer must unwire by hand |
| **c** | Allow it and warn afterwards | 🔴 the wires are already wrong by then |

📐 **My lean: (a), falling back to (b) where (a) is ambiguous** — but ⭐ **whichever you choose, a test
must prove that reordering or deleting a declaration does not silently re-point a call site's wires.**
⛔ **Do not ship this without that test.** It is the one way this feature can corrupt a working graph.

⚠ **Both projections move together** — `NodePinSchema` **and** `Stage0_Rehydrate`.

---

## 2. 🟢 `BP-77` — the "Macros +" button, mirroring a handler that already works

⭐ **`editor.create-function` is registered and working** — `BlueprintDocumentFactory:1750,1778`
(BP-24), as a **quick-add**: one click appends a graph, no dialog. **`editor.create-macro` is the same
shape**, creating a `GraphKind.Macro` graph. `CommandCatalog.CreateMacro` already exists (`:54`), and
`MyBlueprintPanel:92` already draws *"+ Macro"* — **only the handler is missing.**

⭐ **`BlueprintCollapse` already generates `NewMacro`-style names** (`:60`, explicitly *"mirroring
`editor.create-function`"*) — reuse it rather than writing a second namer.

**And make the section real.** `BlueprintMyBlueprintModel:116` still reads:

```csharp
SectionMacros => Array.Empty<MyBlueprintItem>(),   // faked/empty v1
```

⇒ point it at `BuildGraphItems`, exactly as `SectionFunctions` does. ⚠ **But read §3 first** — that
method cannot express three kinds as written.

---

## 3. 🔴 A live defect you will hit on the way — **macros currently list under "Graphs"**

✅ **Coordinator-verified.** `BuildGraphItems(string sectionId, bool functionGraphs)` (`:141`) filters:

```csharp
if ((g.Kind == GraphKind.Function) != functionGraphs) continue;
```

`SectionGraphs` passes `functionGraphs: false`, so it keeps **every graph that is not Function** — Event,
Construction **and `Macro`**. ⇒ ⭐ **A macro created by collapse appears under "Graphs" today**, while
the "Macros" section sits hardcoded empty.

⚠ **This became reachable the moment collapse shipped** (Batches 33-34) — before that, no macro graph
existed in any ordinary workflow. ⭐ **A boolean discriminating what is really a three-way choice**; the
fix is to key on `GraphKind`, not a flag. **File it and fix it** (rule 3 — you allocate the id).

⚠ **Check where `Construction` graphs land** while you are there, and say what you found — the same
filter governs them and nobody has looked.

---

## 4. 🟠 Palette entry + drag — ⚠ **this is `BP-75` and `BP-77` together, one fix**

⚠ **Do not build a macro-only palette path.** `BlueprintNodeCatalog` mints per-asset entries for custom
events (`CustomEvent.{Name}`) and peers (`CallPeer.{guid}`) and **never iterates `asset.Graphs` at
all** — which is why **`BP-75` (a Function graph has no palette entry either)** is still open.
`CreateDynamicNode` likewise has no case for either.

⇒ ⭐ **One iteration over `asset.Graphs` serves both**: a `Function` graph mints a `FunctionCall` entry,
a `Macro` graph mints a `MacroCallNode` entry. **Doing macros alone would leave the same hole half
open and guarantee a second visit.**

⚠ **Drop-created nodes carry `TargetGraphId` and nothing else** (F4). ⛔ Do not bake pin names, types or
counts onto the dropped node — that is `CallablePeers`/`ArgTypes`, which has bitten twice.

📌 **Out of scope, still tracked:** `BP-76`'s greyed *Go to Definition* / *Expand Node*, and `BP-82`'s
`BP1664` + two library rails. ⭐ **`BP-76` is the last macro-adjacent gesture** — Unreal ships *Expand*
and our machinery exists (`Stage2_5`), so it is worth its own batch, not a rushed tail on this one.

---

## 5. Gates

The eight, `--logger "console;verbosity=normal"`. Solution is **`IOS-IG-SimHost.sln`** (⚠ not `Hrot.sln`).
⚠⚠ **The two NodeEdit gates take NO `--no-build`** (`RESUME_START_HERE.md` §3) — and this batch may
touch `NodeEditor.UI`, so they are load-bearing.
⭐ **Run `python3 scripts/tracker-counts.py --check`** — clean on arrival for three batches running.

**Baseline — coordinator-RUN on this tree, all eight:**

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** |
| BP diagnostics | **10 distinct**, all `BP3010`, all **authored** |
| Blueprints | **3192** / 0 / 10 skipped ⚠ *(total 3202 — `BP-111` filters 7 host-timing tests out)* |
| AiShared **1213** · BTree **612** · Breakpoints **130** | 0 failed |
| NodeEdit Core **208** · UI **131** · Generators **193** | 0 failed |

### Tests

| | |
|---|---|
| ⭐ **The destructive-edit guard** | reorder and delete an exec declaration on a macro **with live call sites** ⇒ wires still point at the same **declaration**, not the same index. ⛔ **The batch does not ship without this** |
| **Round-trip** | declare exec pins through the model ⇒ both projections agree ⇒ **expand and run**, asserting the right path fires |
| **Section membership** | a `Macro` graph appears under **Macros** and **not** under Graphs; Function under Functions; say where Construction lands |
| **`create-macro`** | `commands.Invoke(CommandCatalog.CreateMacro)` ⇒ a `GraphKind.Macro` graph exists and the panel lists it |
| **Palette** | ⭐ **both** a Function and a Macro graph mint an entry, and dropping one yields a call node carrying **only** `TargetGraphId` |

⚠ **The one visual check:** *"Macros +"* creates and lists a macro, and a macro drags from the palette.
**Say whether you did it.**

---

## 6. Reporting

Per-suite numbers · **BP-warning count and composition** · `tracker-counts.py --check` clean ·
revert-goes-red per item · ⭐ **every id you allocated** (rule 5) · **your exec-decl edit policy**
(§1.2) and the test that proves it · **where `Construction` graphs list** (§3) · ⭐ **whether you did
the visual check** · anything here **wrong against the code**.

⭐ **The coordinator's handoffs have been wrong in each of the last two batches** — `BP-223`'s
never-drained queue and `BP-221`'s second hole. **Both were found by confirming a claim rather than
building on it.** Keep doing that.
