# HANDOFF — Batch 26: make authoring actually work

> 📌 **Dispatched at `872c60e1`.** Per `.claude/CLAUDE.md` → *Two-session protocol* rule 1, **this file is
> frozen from here.** Anything found later goes in the next handoff, never back into this one.
> ⭐ **Rule 4 is yours:** before your final commit, pull the coordinator branch again and read any
> handoff or design file that changed.
>
> **Read in full. Self-contained.** You are an implementation session; a coordinator session owns the
> plan and reviews your diff.
>
> ⚠ **Supersedes `HANDOFF_Batch25_Addendum_VisualCheck.md`** — that file's BP-120…126 collided with the
> IDs you allocated in Batch 25. **Renumbered here to BP-125…131.** The collision was the coordinator's
> fault, twice now: the rule is BP-200+ while a batch is in flight, and it was not followed.

---

## 0. ⚡ How to work

**You are on Opus. Delegate to Sonnet everything that does not need Opus-level reasoning.**

⚠ **Sub-agents share ONE working tree** — sequential only:
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```

| | |
|---|---|
| **Push to** | `claude/blueprint-macro-feature-sdmspn`, built on the coordinator branch as usual |
| ⭐ **Rule 4 — before your FINAL commit** | **Pull the coordinator branch again** and read any handoff/design file that changed. You build linearly on whatever existed when you started, so late coordinator changes are invisible to you otherwise. **This is what the last two ID collisions cost us** |
| ⭐ **Rule 5 — in your report** | **List every BP id you allocated**, so a collision surfaces at merge |
| **New IDs** | **BP-200+** while this batch is in flight; the coordinator renumbers on merge |
| **Rule 6** | The **tracker + detail docs are yours** for this batch. The coordinator will not touch them |
| **Revert-goes-red** | Every fix, **never delegated** |
| **Commit per item** · **stop cleanly at a boundary** · **no PR** | |

⚠ ⭐ **BP-125…131 do not exist as rows yet.** The coordinator could not add them without violating
rule 6. **Create them from the descriptions below as your first act**, keeping these numbers.

---

## 1. ✅ Batch 25 verified — all eight gates green

build **0 errors** · Blueprints **2999 / 0 / 10 skipped** (3009 total, **+41**) · AiShared **1213 / 0** ·
BTree **612 / 0** · Breakpoints **130 / 0** · NodeEdit Core **208 / 0** · UI **131 / 0** ·
Generators **193 / 0**.

⭐ **The matrix is the real thing, and you proved it rather than asserting it.** Reverting BP-116/117
turns exactly **4 of 9 cells red and reproduces the field errors verbatim** — `BP1300` and `CS0126` for
`int`, `(int, int)`, `(int, int, int)` — while the 5 unaffected cells stay green, with a guard against
vacuous passes. ⭐ **Catching that an empty `NodeKindRegistry` silently degrades every unknown kind to
`FunctionCallNode`** is the detail that would have made the whole thing worthless; finding it yourself
is the difference between a test and a decoration.

⭐ **And it immediately earned its keep**: BP-121 shows the *previous item in the same batch* was
neutered. That is exactly what it is for.

⭐ **BP-124 is exemplary.** You shipped a feature and registered, unprompted, that its most important
seam is untested. Do not stop doing that.

---

## 2. 🔴 Item 1 — **BP-121**: the generator swallows every warning on a successful compile

**Highest value in this batch.** Your own finding, and it is worse than it first reads.

`BP1657` was made a Warning *in the same batch* precisely so it would warn — and it cannot. But it is
not only BP1657: **`BP4001` (unwired data pin), `BP3010` (orphan node) and every other warning are
equally invisible in a real `dotnet build`.**

⇒ **A designer authoring in the editor gets *no* warnings from the real build. Ever.** Everything the
compiler knows and chooses not to fail on is thrown away.

### Suggested solution

Report diagnostics on **both** paths, not just the failure path. In `BlueprintIncrementalGenerator`,
the diagnostic drain currently sits inside the `if (!result.Succeeded)` branch; hoist it so the sink is
drained **unconditionally**, before the success/failure decision, and map severity through to Roslyn
(`DiagnosticSeverity.Warning` for our Warnings, `Error` for Errors).

🔴 ⚠ **CORRECTION — the coordinator had this backwards, verified 2026-08-09.**
`Hrot/Subsystems/Hrot.AI.Behaviors/Hrot.AI.Behaviors.csproj:4` **does set
`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.**

⇒ **A naive unconditional hoist turns every `BP4001` (unwired data pin) and every `BP3010` (orphan
node) in the repo into a hard build error.** That is a repo-wide break, and it is the exact opposite of
the intent — the goal is that designers *see* warnings, not that the build stops.

**Do it in this order:**

1. **Hoist the drain**, mapping our Warning → `DiagnosticSeverity.Warning`.
2. ⭐ **Measure before you commit.** Build the solution and **count what surfaces**, by diagnostic id.
   The shipped assets under `Assets/Blueprints/` have never been warning-checked, so assume non-zero.
3. **Add `<WarningsNotAsErrors>` for the BP warning ids** to `Hrot.AI.Behaviors.csproj` — `BP1657`,
   `BP4001`, `BP3010` and any others step 2 surfaces. ⭐ **This is the right lever:** the warnings stay
   **visible in build output**, which is the whole point, while `TreatWarningsAsErrors` keeps protecting
   against *C#* warnings, which is why it is set.
   ⚠ **Do NOT lower our severities to `Info` to dodge this** — that hides them in normal builds and
   re-creates the bug one level down.
4. ⭐ **Report the counts from step 2.** Every one is a real authoring defect nobody has ever seen. **Do
   not fix them in this batch** — register what is interesting as rows and move on.
5. ⭐ **Assert it in the matrix, not just a unit test.** The matrix already compiles through the real
   generator; add a case whose expected result is *"compiles, **and** emits exactly this warning"*. A
   unit test on the drain would have passed all along.

**Delegation:** 🔴 Opus for the hoist and the severity mapping. 🟢 Sonnet for the matrix case + tests.

---

## 3. 🔴 Item 2 — **BP-124**: prove a `Print String` reaches the log

Your row: *"nothing asserts a Print String message actually reaches the log — zero tests reference
`AiBehaviorLogTarget`."* Trap #9's exact shape, **on the seam that motivated moving the helper between
assemblies in the first place.**

### Suggested solution

A single end-to-end test that closes the seam:

1. **Register the NLog rule in the test** — `Program.cs:124` never runs headless, so
   `AiBehaviorLogTarget.SharedInstance` receives nothing until the test adds
   `AddRule(LogLevel.Trace, LogLevel.Fatal, AiBehaviorLogTarget.SharedInstance, "AI.Behavior*")`.
2. Compile a blueprint containing a `Print String` **through real Roslyn**, load it, tick it.
3. Assert `AiBehaviorLogTarget.SharedInstance.GetMessages()` contains the **formatted** line — with the
   argument values substituted, not the raw format.
4. ⭐ **Revert-goes-red must be: change `BlueprintLog`'s logger name to something outside
   `AI.Behavior*`.** If the test still passes, it is not testing the seam — it is testing that a string
   was formatted, which the existing 19 tests already cover.

⚠ Restore NLog config in a `finally`; the target is a process-wide singleton and will leak across tests.

**Delegation:** 🔴 Opus designs the assertion (point 4 is the whole test). 🟢 Sonnet writes it.

---

## 4. 🔴 Item 3 — **BP-125**: Graph Signature edits never re-project pins

📐 **Root cause of four separate user reports *and* of [BP-102](Blueprint_Issues_Detail.md#bp-102),
open since Batch 20.**

`ReturnNodeDrawer.cs:142-143` routes edits through the edit service:

```csharp
apply: () => { apply(); _editService.NotifyStructureChanged(_parent); },
undo:  () => { undo();  _editService.NotifyStructureChanged(_parent); });
```

→ `EditServiceContext.OnStructureChanged` → `BlueprintDocumentFactory.cs:211` →
`graphModel.RebuildAndNotify()` → pins re-projected. **That is why adding an output from the Return
node's `Details` works.**

⭐ **`GraphSignatureWindow` calls only `_dirtyTracker.MarkDirty(assetId)` (`:299`, `:303`)** — no
`NotifyStructureChanged`, no undo record. The model changes; **the pins never appear.**

| User report | Actual cause |
|---|---|
| *"added output to Graph Signature… NOT shown as pin on Return node"* | never projected |
| *"added bool output, added bool literal, could not wire them"* | ⭐ **not a type bug — there was no pin.** `bool`→`bool` was never the problem |
| *"can't wire, can't test"* | same |
| `BP3010` orphan + `BP1657` | the Return node could not be connected to anything |
| **BP-102** — signature edits do not undo | **same root cause** |

### Suggested solution

Route `GraphSignatureWindow`'s edits through `EditService` exactly as `ReturnNodeDrawer` does —
`recordUndoable` **and** `NotifyStructureChanged`, apply **and** undo.

⚠ **One gesture must stay one undo step.** `GraphSignatureEditModel` currently fires a bare `MarkDirty`
callback per field change; wrap the whole add/remove/rename/retype operation, not each keystroke. Mirror
`ReturnNodeDrawer`'s record shape so the two paths produce identical history entries — **they edit the
same state and must be indistinguishable in the undo stack.**

⭐ **Closes BP-102 as well. Tick both.**

**Delegation:** 🔴 Opus — the undo-record shape. 🟢 Sonnet — tests.

---

## 5. 🔴 Item 4 — **BP-126**: a new Function graph has no `Return` node

> *"newly created function contains just output node, no return node"* — and the user's JSON shows
> exactly one `EventEntry`.

In Unreal a new function gives you **entry + return, already wired.** Here the author must find `Return`
in the palette, place it, and wire it — and missing the wire produces `BP3010` + `BP1657`, which is
exactly what happened.

### Suggested solution

Seed a new **Function** graph with an `EventEntry` **and** a `Return`, **exec-wired**, positioned apart.
BP-103 established the mechanism for a new *asset*; this is the same idea one level down, for a new
*graph* — find the create-graph path used by My Blueprint's `+` under `Functions`.

⚠ **Function graphs only.** An Event graph has no Return node, and a Construction graph is not a
function. ⚠ **Do not seed the peer/library asset templates twice** — BP-103 already seeds the asset's
first graph; make sure the two paths agree rather than both adding a Return.

**Delegation:** 🟢 **Sonnet** — mirrors BP-103.

---

## 6. 🟢 Item 5 — **BP-130**: `BP3010` orphan node → **Warning**

> `CSC : error BP3010: Orphan node '49eca277…' in graph 'NewFunction' was eliminated.`

A disconnected node is **normal while authoring**, and Unreal simply ignores one. Failing the whole
solution build for it is disproportionate — especially here, where the node was disconnected *because of
BP-125*.

⚖️ **Same call the user made for `BP1657`.** ⚠ **Depends on Item 1** — as a Warning it is invisible until
the generator stops swallowing warnings, so do Item 1 first or this silently does nothing.

**Delegation:** 🟢 Sonnet.

---

## 7. 📐 Item 6 — **BP-128**: fold `Graph Signature` into `Details`. **Design note only — do not build.**

> User: *"i do not understand why we are setting inputs and outputs in graph signature. Way more
> intuitive would be to set Detail on Event node (inputs) and Details on Return node (outputs). … The
> whole Graph Signature seems redundant."* · *"there should be one context-sensitive Detail for
> anything."*

⭐ **This is exactly how Unreal works**, and the coordinator agrees it is the right end state.

| Surface | Shows |
|---|---|
| `EventEntry` node selected | the graph's **Inputs** — ✅ the user confirmed this already works today |
| `Return` node selected | the graph's **Outputs** — ✅ already exists (BP-89) and already projects correctly |
| **Empty canvas clicked** | **graph + asset properties** instead of *"no node selected"* — ⭐ and this is where **graph rename (BP-127)** belongs |
| `Graph Signature` window | retires once both halves are covered |

⭐ **It dissolves BP-125 structurally rather than patching it.** If outputs are only ever edited on the
Return node, the projection path that *works* becomes the only path. **A second editor for the same
state is what created the divergence in the first place.**

⚠ **Write `docs/blueprints/Graph_Signature_Into_Details_Design.md` and stop.** The coordinator runs it
past the architect. **BP-125 is the tactical fix and ships now; this is the structural one.**
**BP-127 (graph rename) waits on this decision** — it has no home until the empty-canvas Details exists.

**Delegation:** 🔴 Opus — it is a design note.

---

## 8. 📐 Item 7 — **BP-131**: the Return node's `Status` combo. **Design note only.**

> User: *"a combo with fixed values for Success, Error, In progress — meaningless … the status must be
> an input data pin of the return node, not a fixed value to select in a combo."*

BP-105 already hides `Status` for Instance and Library-with-outputs (the user confirmed that works). What
remains is **zero-output Library** and **AiPrimitive**, and the user's point is sharper than visibility:
**a status chosen at author time from a combo is a constant, so it cannot express a runtime outcome.**

⚠ **Test-locked** — `BPC_ImplicitReturnTests.Library_NoReturn_EmitsImplicitSuccessReturn` and the
AiPrimitive `NodeStatus` hosting contract both depend on the current shape. ⚠ **AiPrimitive is the hard
case:** its `NodeStatus` return *is* the BTree/HSM contract, so "make it a pin" has consequences beyond
the editor. **Note the options, do not pick one.**

---

## 9. ⏸ Deferred — reasons stated

| | |
|---|---|
| **BP-129** (was 124) — no `ushort`/`uint` literal, no conversion node | ⚠ **Re-scope after BP-125.** Some of the user's *"cannot wire"* was the missing pin, so the true size is unknown |
| **BP-122** (yours) — remaining matrix axes | Real, but Items 1–4 are user-blocking |
| **BP-123** (yours) — `Fdp.Core.Tests` red on a clean tree | ⚠ **Environment-specific; the coordinator's Linux runs are green on all eight suites.** Confirm it is your environment before spending on it |
| **BP-127** — graph rename | Blocked on BP-128's design |

---

## 10. Gates

Same eight as Batch 25, with `--logger "console;verbosity=normal"`.
**Baseline (coordinator-measured, merged Batch-25 tree):** build **0 errors** ·
Blueprints **2999 / 0 / 10 skipped** (3009 total) · AiShared **1213 / 0** · BTree **612 / 0** ·
Breakpoints **130 / 0** · NodeEdit Core **208 / 0** · UI **131 / 0** · Generators **193 / 0**.

⚠ Item 1 changes what the generator reports — **expect new warnings to surface across the repo.** If any
existing asset starts warning, that is the fix working; **register what it finds, do not silence it.**

---

## 11. Reporting back

1. Per-suite gate numbers you actually ran.
2. What you reverted and confirmed went red, per item. ⭐ **For Item 2 the revert is specifically the
   logger-name change** — anything else does not test the seam.
3. ⭐ **What Item 1 surfaced repo-wide** once warnings stopped being swallowed.
4. What you delegated to Sonnet, what you kept.
5. ⭐ **Every BP id you allocated** (rule 5) — including the BP-125…131 rows you create.
6. Anything here wrong against the code. **You have been right against these handoffs five times now** —
   and the coordinator got `TreatWarningsAsErrors` backwards in this very document before catching it,
   so keep checking.

**Done =** gates green vs baseline · rows `[x]` with `DONE` notes · counts reconciled · commit per item ·
pushed. **No PR.**
