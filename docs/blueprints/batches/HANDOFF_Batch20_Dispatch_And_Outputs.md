# HANDOFF — Batch 20: BP-92 (dispatch at create) + BP-89 (outputs on the Return node)

> **Read in full. Self-contained.** You are an implementation session; a coordinator session owns the
> plan and will review your diff.

---

## 0. ⚡ Model delegation — read this first

You are running on **Opus**. **Delegate to Sonnet sub-agents anything that does not need Opus-level
reasoning.** Tokens are the binding constraint on this programme; this is a requirement, not a
preference.

**The split for this batch, stated per item:**

| Work | Model | Why |
|---|---|---|
| **BP-89** `ReturnNodeDrawer` — a new drawer mirroring `FunctionCallNodeDrawer` | 🟢 **Sonnet** | Pure mirror-an-existing-pattern. The registration line and the drawer interface are both established |
| **BP-89** parameter add/remove UI rows | 🟢 **Sonnet** | `GraphSignatureWindow.DrawParameterRows` is the reference implementation to reuse |
| **BP-92** `Dispatch` enum plumbing through the create path | 🟢 **Sonnet** | Mechanical; one hardcoded value becomes a parameter |
| **BP-92** create-dialog UI (a combo + label) | 🟢 **Sonnet** | Mirrors the existing custom-event create modal |
| Test scaffolding once the contract is stated | 🟢 **Sonnet** | Fixtures and assertions from a written contract |
| **Deciding what `Dispatch` does to an existing asset** (migration / defaults) | 🔴 **Opus** | Design call with data-loss potential — see §2 |
| **Editor↔compiler pin-projection parity** if you touch `NodePinSchema` | 🔴 **Opus** | Two halves that must agree; trap #9 lives here |
| **Diff review + gate runs + revert-goes-red** | 🔴 **Opus** | Never delegate verification |

⚠ **Delegation does not transfer the verification duty.** Re-run the gates yourself and apply the
revert-and-watch-it-go-red discipline to Sonnet's work exactly as to your own.

---

## 1. Context

| | |
|---|---|
| **Repo** | `pjanec/HROT` |
| **Implementation branch** | `claude/blueprint-macro-feature-sdmspn` — cut from the coordinator branch; **push here** |
| **Coordinator branch** | `claude/blueprint-authoring-status-6sr5ld` — docs/tracker only; do **not** push here |
| **Plan** | [Blueprint_Functions_Macros_UX_Plan.md](Blueprint_Functions_Macros_UX_Plan.md) — this batch is steps **1 and 2** |
| **Truth** | [Tracker](Blueprint_Issues_Tracker.md) · [Detail](Blueprint_Issues_Detail.md) |
| **Orientation** | [RESUME](Blueprint_Gaps_Programme_RESUME.md) — traps, gates, working agreement |

**Why these two, in this order.** Every downstream label lies until BP-92 lands: the editor can only
create `Dispatch: Instance`, so a functions-only asset is *forced* to misdescribe itself. And BP-89
alone unblocks the **T-series** verification of `BP-73` — the programme's largest unverified item, stuck
four batches because the user could not find where to add function outputs.

**Standing rules that have each cost real time:** verify claims against code (ten audit claims have been
wrong) · fix, don't disable · record findings in the detail doc · **revert your fix and confirm the test
goes red**.

---

## 2. BP-92 — choose dispatch when creating a blueprint

📄 [detail](Blueprint_Issues_Detail.md#bp-92)

**The whole defect is one line.** `BlueprintNewAssetService.cs:96`:

```csharp
Dispatch = BlueprintDispatchKind.Instance,
```

There is **no dispatch choice anywhere in the editor UI**, so every asset the editor creates is an
Instance blueprint.

**⭐ Nothing needs designing — `Library` already works end to end:**

| Layer | Evidence |
|---|---|
| Data model | `BlueprintDispatchKind { Library, AiPrimitive, Instance }` (`BlueprintAsset.cs:34`) |
| Compiler | `LibraryEmitter.cs:20` emits every Function graph as a `static` method |
| Editor pins | `NodePinSchema.cs:574,598` already branches on Library (no self/view params) |
| Shipped assets | `LibraryMath.bp.json`, `with-callable-peer.bp.json` |

### Build

1. A dispatch choice in the create flow. ⚠ **Build it as an extensible list, not an
   Instance/Library toggle** — a fourth value (`MacroLibrary`) is already designed in
   [Q25](Architect_Question_25_Macros.md) and must slot in without a data migration.
2. Offer **`Instance`** and **`Library`**. ⚠ **Do not offer `AiPrimitive`** unless you first confirm
   what else an AiPrimitive asset requires (`Primitive` field, hostings) — out of scope here.
3. Surface the dispatch in the UI: the [BP-85](Blueprint_Issues_Detail.md#bp-85) breadcrumb already has
   the slot (`Asset [Dispatch] > Graph (Kind)`).

### 🔴 Opus decisions — do not delegate

- **Existing assets.** Every asset on disk says `Instance`. Does this batch change any of them?
  **Recommended: no.** Adding a chooser for *new* assets is additive and safe; retagging `SquadState`
  as `Library` is a separate, reviewable change. **If you disagree, say so — do not silently migrate.**
- **`LibraryLowering` guards will now be reachable by users for the first time.** `BP5001` errors when a
  Library has no Function graph, and `BP9001` rejects any latent op in a Library. Both are correct
  today, but a designer creating an empty Library will now hit `BP5001` immediately. **Check the
  wording reads as guidance, not as an internal error** — `BP9001`'s message literally says *"Stage 2
  should have caught this"*, which is not designer-facing English.

### Done when

- [ ] A new blueprint can be created as `Library`, and it round-trips.
- [ ] The dispatch is visible somewhere the user will see it.
- [ ] Creating an empty Library produces a **comprehensible** diagnostic, not an internal-sounding one.
- [ ] No existing asset changed dispatch.

---

## 3. BP-89 — declare outputs where the designer already is

📄 [detail](Blueprint_Issues_Detail.md#bp-89)

> User: *"I have no idea how to add 3 function outputs. Where? Return node detail panel always shows
> Success and nothing else."*

**⚠ This blocked the T-series.** Fixing it is what makes `BP-73` verifiable.

**✅ Unreal validates the design, so this is parity work, not invention.** Unreal's Details panel carries
**Inputs** and **Outputs** with `+` buttons and opens from **three** places — the My Blueprint item, the
**entry node**, *or* the **result node**. Input params surface as data-**out** pins on the entry node and
outputs as data-**in** pins on the Return node: **exactly our shape**, which BP-71/BP-73 already got
right. We have **one** entry point, in a separate window, and it is none of the three.

### Build

1. **A `ReturnNodeDrawer`** — there is **none today**. Register it in `BlueprintEditorBootstrap.cs`
   alongside the others (`registry.Register(typeof(ReturnNode), new ReturnNodeDrawer(...))`, see
   `:51-68`). 🟢 **Sonnet** — mirror `FunctionCallNodeDrawer`.
2. It must list the **containing graph's declared Outputs** with add / remove / retype, reusing
   `GraphSignatureWindow.DrawParameterRows` rather than duplicating it. 🟢 **Sonnet**.
3. **With zero outputs declared, say so in words.** Today a Return node with no value pin reads as
   broken; it is correct. One line — *"This function declares no outputs. Add one to return a value."*
4. Keep `Status` — it stays useful (and is [BP-14](Blueprint_Issues_Detail.md#bp-14)'s home).

### ⚠ Traps specific to this item

- **`DrawParameterRows` was the BP-86 site.** It now routes through
  `Fdp.Presentation.Utils.ImGuiBufferText.Decode`. **Do not reintroduce `TrimEnd('\0')`** — that was
  seven sites of data corruption, and this is the file it was found in.
- **Adding an output must go through the undo stack** (`BlueprintEditCommand` / `EditService`), like
  every other panel edit. BP-02 spent a batch removing undo bypasses; do not add one.
- **Editing outputs changes pin projection.** If you touch `NodePinSchema`, the compiler half
  (`Stage0_Rehydrate`) must move with it — 🔴 **Opus**, trap #9.

### Done when

- [ ] Selecting a Return node shows the graph's outputs, with add/remove that works.
- [ ] Zero outputs renders an explanation, not a bare node.
- [ ] Every edit is one undo step.
- [ ] **T1–T7 in the [RESUME visual check](Blueprint_Gaps_Programme_RESUME.md) can now be performed.**

---

## 4. Gates

```bash
dotnet build IOS-IG-SimHost.sln -v q --nologo
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj -v q --nologo
dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj -v q --nologo
dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Hrot.BTree.Editor.Tests.csproj -v q --nologo
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj -v q --nologo
dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj -v q --nologo
dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj -v q --nologo
dotnet test Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/Hrot.AiEditor.Generators.Tests.csproj -v q --nologo
```

**Known flakes** — re-run the single filter before calling either a regression:
`PdbEmbeddedSourceTests`, `WhenNodePerfTests.WhenNode_ValueChanged_Under100ns_perTick`.
**Classify a failure** with `git stash` → re-run → `git stash pop`.

⚠ Batch 19 reported 2 pre-existing Blueprints failures (`WhenNodePerfTests`, `AllocationFreeTests`) and
34 in `Fdp.Presentation.Tests`, all failing identically under stash. **Confirm they are still
pre-existing rather than inheriting the claim.**

---

## 5. Reporting back

The coordinator reviews the **diff**, not the summary. In your report state:

1. What you delegated to Sonnet and what you kept — so the split can be tuned next batch.
2. **Which tests you reverted the fix against, and that they went red.**
3. Anything you found that contradicts this handoff — the docs have been wrong ten times, and
   corrections are worth more than compliance.
4. Anything you deliberately did *not* do, and why.

**Definition of done:** gates green vs the batch-19 baseline · tracker rows `[x]` with `DONE` notes in
the detail file · **counts reconciled three ways** (checkbox tally, header total, complexity-column sum)
· committed and pushed to `claude/blueprint-authoring-status-6sr5ld`.

⚠ **Do not create a pull request.**

---

## 6. ⚠ Shared-file protocol (added after BP-92 landed)

Two branches are live: yours (code) and the coordinator's (docs/tracker). To avoid a merge conflict on
the files **both** roles write:

| File | Who writes it during a batch |
|---|---|
| `Blueprint_Issues_Tracker.md` · `Blueprint_Issues_Detail.md` | **You** — mark rows `[x]`, add `DONE` notes, reconcile the counts |
| Everything else in `docs/blueprints/` | **Coordinator** — will not touch the two files above while a batch is in flight |

⇒ Own the tracker and detail files for the duration of this batch. The coordinator holds off on them
until your work lands, then integrates.
