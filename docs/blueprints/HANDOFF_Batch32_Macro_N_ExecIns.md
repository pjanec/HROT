# HANDOFF — Batch 32: `N` exec-ins for macros (Q26-A3), the prerequisite for collapse

> 📌 **Dispatched at `<pending>`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⭐ **Rule 7 is yours:** branch from this branch, and re-sync from it at the **start** of your run.
> ⭐ **Rule 4 is yours:** pull it again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** `BP-74`, `BP-82` are *referenced* existing rows.
> `BP1664`/`BP1666` are the unused **diagnostic codes** in the reserved macro block — **you allocate.**
>
> 📄 **[Q26](Architect_Question_26_Collapse_Selection.md) is the design and it is SETTLED.**
> ⛔ **Q26-A3 supersedes [Q25](Architect_Question_25_Macros.md)-D3** — a macro now has **N exec-ins**,
> not one. Q25 carries a banner saying so.
>
> ⚠ **Everything here is headless.**

---

## 0. ⭐ Why this batch is not collapse

The user asked for **collapse-a-selection-into-a-Function/Macro** (`BP-74`). **This batch is the
prerequisite, deliberately split off.** Reasoning, so nobody re-litigates it:

- Q26-A settled on **A3, N exec-ins** — precisely so that a selection entered from two places
  **becomes a two-entry macro** instead of being refused.
- ⇒ Collapse's boundary analysis must be able to *emit* N exec-ins. **Writing that analysis against a
  macro model being changed in the same run** is how the subtle ones get in — this programme has the
  scars (the four-projection-halves lesson; `BP1661` shipping with an inverted gate).
- Both halves are large. Focused batches have gone materially better here than crowded ones.

⇒ **Batch 32 = the model. Batch 33 = the gesture.** §6 previews 33 so the shape is visible.

---

## 0a. ⚡ How to work — the standing rules

**You are on Opus. Delegate to Sonnet everything that does not need Opus-level reasoning.**

| Item | 🔴 Opus keeps | 🟢 Sonnet takes |
|---|---|---|
| **1** model | — | ⭐ **entirely** — mirror of `ExecOutDecl` |
| **2** projections | the entry node's N exec-out shape | the call node's N exec-in half + parity tests |
| **3** splice rule 1 | ⭐ **yes** — indexing an entry is where a wrong pairing hides | the golden tests |
| **4** the purity mirror | ⭐ **yes, all of it** — ⚠ **it is NOT a copy of `BP1663`** (§4) | the negative fixtures |

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

## 1. 🟢 The model — a mirror of `ExecOutDecl`

```csharp
public sealed class ExecInDecl { public Guid Id; public string Name = ""; public string? Tooltip; }
public sealed class Graph { /* … */ public List<ExecInDecl> ExecInputs { get; set; } = new(); }
```

⚠ **A new list, for the same reason `ExecOutputs` was one** (design F5). **Coordinator-measured on this
tree: `Graph.Inputs.Count` is load-bearing arithmetic at 16 executable sites across 7 files** —
`InstanceEmitter` ×5 · `Stage2_Validate` ×4 (`BP1652`'s arity check among them) · `CSharpEmitter` ×2 ·
`NodePinSchema` ×2 · `Stage0_Rehydrate` · `Stage5_Schedule` · **`Stage2_5_ExpandMacros:354`**.
⛔ **Do not put exec entries in `Graph.Inputs`** — it would silently move every one of them, including
the splice you are about to edit.

📌 **Empty means today's behaviour.** `ExecInputs.Count == 0` ⇒ the single implicit entry, exactly as
now. ⭐ **Every existing asset must round-trip byte-identically** — test that.

⚠ **`Stage3_Normalize` rebuilds `Graph`.** Batch 31 replaced both hand-rolled copies with
`Graph.WithNodesAndLinks` **plus a reflection test that fails when any member is not carried across**.
⇒ adding `ExecInputs` should make that test **go red until you add it**. If it does not, the guard is
broken and that is a finding.

---

## 2. 🟠 Projections — the same four halves, on the entry side

| Boundary | Editor half | Compiler half | Delta |
|---|---|---|---|
| **Entry** (`EventEntryNode`) | `NodePinSchema.EventEntryNodePins` | `Stage0_Rehydrate.EnrichEventEntryPins` | ⭐ **N exec-OUT pins** from `ExecInputs`, replacing today's single `MakeExec("Out","Out")` |
| **Call** (`MacroCallNode`) | `NodePinSchema.MacroCallPins` | `Stage0_Rehydrate.EnrichMacroCallPins` (`:39`) | ⭐ **N exec-IN pins** from the target's `ExecInputs`, replacing today's single `MakePin("In","In",isExec:true)` |

⭐ **You have already done this exact shape once**, in the other direction: Batch 29 gave `ReturnNode`
N exec-**ins** from `ExecOutputs`, and `EnrichMacroCallPins:49-53` gives the call node N exec-**outs**.
**Mirror your own code.**

⚠ **Order is load-bearing** — Stage 5 and the splice pair pins **positionally** with the declaration
list, exactly as `ExecOutputs` does. ⚠ **Both halves move together**; every batch that moved one and
not the other produced a silent shape mismatch.

---

## 3. 🔴 Splice rule 1 becomes indexed

Today (`Stage2_5_ExpandMacros.SpliceExecIn`, `:154`): one `ExecInPin` (`:421`), all incoming exec links
re-tied to the successor of the entry clone's single exec-out.

**Becomes:** `X.out → C.execIn[k]` ⇒ `X.out → succ(In′.execOut[k])` — the exact mirror of rule 2
(`:189-212`), which already indexes `returnExecIns[k]` and already tolerates several `Z` feeding one
entry.

| ⚠ | |
|---|---|
| **stale-asset guard** | rule 2 has `if (k >= returnExecIns.Count) break;` — **rule 1 needs its own.** Mirror it |
| **an unwired entry** | `execIn[k]` with nothing arriving is fine — that entry is simply unreachable. ⛔ **Not `BP1667`**: the body is not empty, one door is unused |
| **convergence** | several entries reaching the same body block is already handled — `ComputeMergePoints` allocates one shared block for in-degree ≥ 2 |
| **`BP1667`** | *"empty body"* must still fire on a genuinely empty macro. ⚠ Its current test may assume a single entry — check it |

---

## 4. 🔴 The purity mirror — ⚠ **NOT a copy of `BP1663`**

Q26-A3 records the cost: with **≥ 2 exec-ins**, a data **input** fed by an **impure** producer is
definitely-assigned only on the entering path ⇒ **`CS0165`** in generated code, naming a synthesized
local and pointing at the consumer.

### ⚠⚠ The one thing to get right: it is a different graph

`BP1663` (`ValidateMultiExecOutPurity`) walks **backwards from the `ReturnNode`'s data-in pins, inside
the macro body**, because a macro's *outputs* are produced inside it.

⭐ **The mirror must walk the HOST graph**, backwards from the **`MacroCallNode`'s data-in pins**,
because a macro's *inputs* are supplied **at the call site**. `FindImpureProducer` is reusable — the
graph you hand it is not.

⇒ **Consequences that follow from that, and they are not cosmetic:**

| | |
|---|---|
| **Per call site, not per declaration** | the same macro can be safe at one call site and unsafe at another. `BP1663` is per-macro; this one is **per `MacroCallNode`** |
| **The diagnostic names the call node** | ⭐ the same reasoning that fixed `BP1661` in Batch 31 — blame a node the designer placed |
| 📐 **Your call: gate on *declared* ≥ 2 entries, or *wired* ≥ 2?** | ⭐ **Wired is strictly better** — a call site using only one entry has one path and is provably safe, so gating on *declared* rejects code that cannot fail. Both are equally cheap at Stage 2. **State which and why** |

⚠ Inherits F2's recorded caveat verbatim: purity is **conservative** and rejects impure producers that
genuinely dominate every entry. The precise check is dominance-based, but dominance exists only at
Stage 5, *after* expansion, where the diagnostic would name synthesized nodes nobody placed.
**Conservative wins: a false rejection is explainable, a `CS0165` about `__t5` is not.**

📌 Codes: `BP1664` (reserved for macro-declares-a-local, ⛔ **still unbuildable** — `Graph` has no
`LocalVariables`, that is **BP-57**) and `BP1666` are the unused numbers. **You allocate** (rule 3).

---

## 5. Gates

The eight, `--logger "console;verbosity=normal"`. Solution is **`IOS-IG-SimHost.sln`** (⚠ not `Hrot.sln`).
⚠⚠ **The two NodeEdit gates take NO `--no-build`** (see `RESUME_START_HERE.md` §3).
⭐ **Also run `python3 scripts/tracker-counts.py --check`** before your final commit — the done column
has been wrong in three consecutive batches.

**Baseline — coordinator-RUN on this tree, all eight:**

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** |
| BP diagnostics | **10 distinct**, all `BP3010`, all **authored** |
| Blueprints | **3145** passed / 0 failed / 10 skipped ⚠ *(total 3155 — `BP-111` filters 7 host-timing tests out of the default run)* |
| AiShared **1213** · BTree **612** · Breakpoints **130** | 0 failed |
| NodeEdit Core **208** · UI **131** · Generators **193** | 0 failed |

⚠ **Known flake, not yours:** `WhenNodePerfTests` is now behind `Category=HostTimingSensitive`. If a
timing test appears in your default run, the filter regressed.

### Tests

| Layer | |
|---|---|
| **Round-trip** | every existing asset unchanged byte-for-byte with `ExecInputs` absent |
| **Projection parity** | editor and compiler halves agree, for 0, 1 and N entries |
| **Splice** | two entries, two distinct host paths, both reaching the body ⇒ golden node/link counts **+ the `LinkedToIds` mirror** |
| ⭐ **Run** | a two-entry macro, **compiled through real Roslyn**, entered from **both** paths in different ticks, asserting a **different value** per entry. ⚠ `.Succeeded` never invokes Roslyn |
| **Purity negative** | ≥ 2 entries + an impure host producer feeding a data-in ⇒ the new code, asserting the **code** and that it names the **call node** |
| **Purity positive** | the same shape with a **pure** producer ⇒ accepted, and it **runs** |
| ⭐ **Regression** | a **one**-entry macro with an impure data-in producer still compiles — that is today's legal case and must not be swept up |

---

## 6. 📌 Batch 33 preview — collapse (`BP-74`). **Not this batch; do not start it.**

So the shape is visible while you work. All settled in [Q26](Architect_Question_26_Collapse_Selection.md):

| | |
|---|---|
| **D1** | the boundary analysis lives in **`.Compiler`**. ⭐ Reachable: `.Editor → .Core → .Compiler`, the path `BlueprintClipboard` already uses to call `GraphFragmentCloner` |
| **B2** | offer every form; **refuse on invoke with a message naming the offending nodes**. ⛔ **No greyed-out menu items** — the tracker files those as defects (`BP-76`, `BP-77`) |
| **F** | ⭐ **a selection containing a latent node MAY be collapsed.** Unreal refuses; that refusal forbids by gesture what its own capability permits. Reuse `BP1661` for the real rule |
| **E1** | ⭐ **collapse ∘ expand is a required, test-locked structural invariant** — needs a canonical graph comparator (kinds + topology; **not** ids, positions or pin ids) |
| ⚠ | `BlueprintCommandSink` has **no case** for either collapse ⇒ `default:` returns **success** and does nothing. Trap #5, already sitting there |

---

## 7. Reporting

Per-suite numbers · the **BP-warning count and composition** · `tracker-counts.py --check` clean ·
revert-goes-red per item · ⭐ **every BP id and diagnostic code you allocated** (rule 5) · your ruling
on **§4's declared-vs-wired** gate · ⭐ **whether the two-entry macro actually ran, and the two values
you asserted** · anything here **wrong against the code**.

⭐ **You have corrected the coordinator in every batch so far and been right each time** — most recently
`BP1661`, which I had reviewed and passed. If something above does not match the tree, say so plainly.
