# RESUME — implementation session (Batches 20 & 21 shipped; Batch 22 next)

> **Written immediately before a context compaction.** Everything needed to resume with no prior
> conversation. **You are an *implementation* session**; a coordinator session owns the tracker and
> writes the handoffs.

---

## 0. Where things stand — read this first

| | |
|---|---|
| **Repo** | `pjanec/HROT` |
| **Implementation branch (PUSH HERE)** | `claude/blueprint-macro-feature-sdmspn` · at `ef6e0864` |
| **Coordinator branch (do NOT push)** | `claude/blueprint-authoring-status-6sr5ld` · at `3d92f7a1` |
| **Next task** | `docs/blueprints/HANDOFF_Batch22_EndToEnd_Smoke.md` — **BP-109**, an end-to-end smoke test. ⚠ **Not yet in the working tree** — see §1 |
| **Truth** | [Tracker](Blueprint_Issues_Tracker.md) · [Detail](Blueprint_Issues_Detail.md) — both were **mine** for batch 21 and are fully up to date |
| **Counts as I left them** | **59 open · 51 fixed · 1 refuted**, reconciled three ways |

**⚠ Do not create a pull request.** Not in any batch so far.

---

## 1. FIRST ACTION on resuming — get the Batch 22 handoff

The coordinator has advanced past my branch point. Batch 22's handoff exists **only on the coordinator
branch**:

```bash
git fetch origin claude/blueprint-authoring-status-6sr5ld
git log --oneline -3 origin/claude/blueprint-authoring-status-6sr5ld
#   3d92f7a1 docs(blueprints): Batch 22 handoff (BP-109 smoke test); BP-108 marked design-first
#   a5b12b12 docs(blueprints): BP-108 (Print/Log node) + BP-109 (end-to-end smoke test)
#   7010dc79 docs(blueprints): Batch 21 coordinator verification — accepted   ← my batch 21 was ACCEPTED
git reset --hard origin/claude/blueprint-authoring-status-6sr5ld
```

Every batch so far has started with exactly this reset. My own commits are already merged into the
coordinator branch each time, so the reset is a fast-forward and loses nothing — **verify** with
`git merge-base --is-ancestor <my HEAD> origin/claude/blueprint-authoring-status-6sr5ld` before doing it.
Then read `HANDOFF_Batch22_EndToEnd_Smoke.md` **in full**.

Also newly registered by the coordinator and *not* mine yet: **BP-108** (Print/Log node, marked
design-first).

---

## 2. What I shipped

### Batch 20 — `893b2ec9`, `737a8402`, `1c5a7201`

- **BP-92** — dispatch choice at create. `BlueprintNewAssetService` is table-driven: one blank-template
  recipe per dispatch (`Empty`→Instance, `Function Library`→Library). `MacroLibrary` is one more row.
- **BP-89** — function outputs on the Return node. New `ReturnNodeDrawer`; `DrawParameterRows`
  **extracted** (not copied) into `Windows/ParameterRowsView.cs`; `GraphSignatureEditModel` gained an
  optional undo-recorder seam.

### Batch 21 — `84a05ca6`, `3d4150e4`, `ef6e0864` — **accepted by the coordinator** (`7010dc79`)

- **BP-103** 🔴 — blank templates had **zero graphs**: crashed on open *and* broke `dotnet build`
  (empty Library → BP5001 → fails an MSBuild step of `Hrot.AI.Behaviors`). Now seeds a Function graph
  via `BlueprintDocumentFactory.CreateFunctionGraph`.
- **BP-104** 🔴 — a `Library` function's declared outputs were ignored. Terminator is now
  outputs-driven for Library at **both** sites (`BuildReturnTerminator`, `SealFallThrough`).
- **BP-105** — the Return panel showed an inert control; now renders only what the dispatch reads.
- **BP-92 re-ticked** (the coordinator had reopened it pending BP-103).

---

## 3. Decisions already made — do not silently re-litigate these

1. **The dispatch choice lives in the recipe picker, not a combo.** There is **no create dialog** in
   production: `NewAssetLauncher` → recipe tree picker → vendored `SaveAsBrowserDialog` (name+folder)
   → `CreateNew`. `NewAssetDialog` is a headless model nothing constructs. One entry per dispatch is
   also exactly Unreal's shape.
2. **The Instance blank template seeds `Tick`/*Function*, never `Tick`/Event.**
   `InstanceEmitter.EmitTickMethod` matches `Kind == Function && Name == "Tick"`. An Event graph of
   that name is emitted as an event handler ⇒ **the blueprint silently never ticks.** Three shipped
   assets do use `Tick`/Event; they are *not* the model to copy.
3. **`AiPrimitive` returns `NodeStatus` unconditionally; `Library` only when it declares no outputs.**
   Zero-output Library→`NodeStatus` is deliberate and test-locked
   (`BPC_ImplicitReturnTests.Library_NoReturn_EmitsImplicitSuccessReturn`, and `LibraryMath` ships it).
   Only *Library **with** outputs* was unimplemented.
4. **No existing asset has had its dispatch migrated.** Retagging e.g. `SquadState` stays a separate,
   reviewable change.

---

## 4. Gate commands + the baseline I measured (clean tree, `ef6e0864`)

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

| Suite | Result |
|---|---|
| build | **0 errors** |
| Blueprints | **0 failed / 2905 passed / 10 skipped** |
| AiShared | 0 / 1213 |
| BTree.Editor | 0 / 612 |
| Breakpoints | 0 / 130 |
| NodeEditor.Core | 0 / 208 |
| NodeEditor.UI | 0 / 131 |
| AiEditor.Generators | 0 / 189 |

**Zero failures anywhere; neither known flake fired** (`PdbEmbeddedSourceTests`,
`WhenNodePerfTests.WhenNode_ValueChanged_Under100ns_perTick`). `Fdp.Presentation.Tests` is **not** in
the gate list — batch 19's 34 failures there were never re-measured by me.

---

## 5. ⚠ The trap that cost me the most time this session — READ BEFORE DELEGATING

**Subagents share this one working tree.** Two independent failures came from that, both mine:

1. **Concurrent `dotnet build` corrupts state.** Two agents building at once produced a phantom
   "missing method" error that vanished on retry.
2. **An agent doing revert-and-watch-it-go-red leaves the tree *intentionally broken* for minutes.**
   I twice observed the fix absent from `git status` and once nearly reported six phantom failures
   from a stale test DLL as real regressions.

**Mitigations that worked:**
- **Run agents strictly sequentially.** Wait for `ps aux | grep -c "[d]otnet build\|[d]otnet test"`
  to reach `0` *and* the expected files to be present before touching anything.
- **Gate every commit on the fix actually being in the tree** (`git status` shows the source file
  modified), never on the agent merely reporting success.
- **Better still: give each agent `isolation: "worktree"`.** I did not, and should have.

Two further notes for whoever reads the subagent reports:
- Both batch-21 agents claimed a commit was "harness auto-commit" / "outer-session automation".
  **It was me committing their finished work.** Do not let that claim reach a doc as fact.
- `codebase-memory-mcp` is **not connected** in this cloud session and cannot be made to connect
  mid-session (MCP servers spawn at session start). CLAUDE.md's fallback applies: use Grep/Glob/Read
  and say so.

---

## 6. Working agreement (from the handoffs, and enforced every batch)

- **Delegate to Sonnet** anything not needing Opus: mirror-an-existing-pattern work, mechanical
  plumbing, test scaffolding from a stated contract. **Keep on Opus:** design calls, compiler
  semantics, diff review, gate runs. Tokens are the binding constraint.
- **Never delegate verification.** Re-run the gates yourself.
- **Revert your fix and confirm the test goes red** — required, and report *which* tests and the
  actual failure messages.
- **Fix, don't disable.** Never weaken an assertion to make it pass.
- **Verify claims against code** — the audit register has been wrong ten times, and I corrected the
  handoff in **both** batches. Corrections are worth more than compliance.
- **Anything you knowingly leave behind gets a tracker ROW**, not a note inside a `DONE` block. This
  is the BP-102 lesson: the batch-20 session buried a gap in a DONE note and it appeared in no count
  and no priority list.
- **Reconcile counts three ways**: checkbox tally, complexity-column sums, header total. Two rows
  match neither pattern — the refuted **BP-46** and an abandoned *"Squad-quartet & dispatcher
  lowering"* row. They are the permanent ±1 against the header; do not "fix" them.
- Report back: **actual gate numbers** (not "gates green"), what went red under revert, and what was
  delegated vs kept.

---

## 7. Open items I created or touched (not mine to close silently)

| Item | Note |
|---|---|
| **BP-106** | An `AiPrimitive` graph's declared Outputs are still silently dropped — correct for the hosting contract, but now the **only silent case left** in this family. Wants a **Stage 2 error**. ⚠ Every new `BPxxxx` needs a `[CoversDiagnosticCode]` test or `V_AllValidatorsCoverageTests` fails the build |
| **BP-107** 📐 | `Return.Status` is a compile-time constant ⇒ **`Running` is inexpressible**. The user's data-in-pin instinct is right. **Architect round required** — changes the AiPrimitive hosting contract. Deliberately not built |
| **BP-102** | Graph Signature window edits are still **not undoable** (it holds a `DirtyTracker`, no `IEditService`). The identical gesture from the Return node *is* undoable |
| **BP-108** | Print/Log node — registered by the coordinator, marked design-first. Not mine yet |

---

## 8. 🔴 The largest outstanding risk — unchanged for five batches

**The T-series (T1–T7, [BP-73](Blueprint_Issues_Detail.md#bp-73) N function outputs) is performable
but STILL UNPERFORMED.** BP-89 removed the gate that blocked it; it did not perform the check.

More broadly: **every defect in batch 21 came from the user driving the editor after batch 20's gates
were green and its code reviewed clean.** A green suite has now twice failed to find what ten minutes
at the UI found immediately. Batch 22 (BP-109, an end-to-end smoke test) is aimed at exactly that gap
— treat it as the point of the batch, not a chore.
