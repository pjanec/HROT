# RESUME — implementation session (Batches 20–22 shipped; Batch 23 items 1–3 shipped, item 4 open)

> **Self-contained; assumes no prior conversation.**
> **You are an *implementation* session**; a coordinator session owns the tracker and writes handoffs.

## 🛑 WHERE BATCH 23 STOPPED — read this first

**Items 1, 2 and 3 are DONE, gated, committed separately and pushed. Item 4 (BP-108, the Print/Log
node) has NOT been started** — beyond its design note, which IS written and committed.

Stopped **at the item boundary on purpose**, per the handoff's own rule ("three finished items beat
four half-finished ones"). Item 4 is bigger than items 1–3 combined: a new node type, **two** pin
projections, Stage5 lowering, emit, a drawer, a detail-panel property UI, and a test that must register
the NLog rule itself.

### ⇒ Next session: start at [`PrintString_Node_Design.md`](PrintString_Node_Design.md)

It records the ⚖️ D4+D5 pre-approved shape **and one correction you must not undo**:

🔴 **D5 names the wrong accessor.** `FdpLog<T>` derives its logger name from `typeof(T).FullName`, so a
generated blueprint class logs under `Hrot.AI.Behaviors.Generated.…` — which the `AI.Behavior*` NLog
rule **does not match**. It would compile, reach the rolling file, and never reach the AI Behaviors tab
or the test. **Use `Hrot.AI.Behaviors.Logging.BehaviorLog`** (`LogManager.GetLogger("AI.Behavior")`),
which is the shipped helper for exactly this. ⚠ It has no `Info` tier — the five-level round-out adds
one, mirroring its existing four.

The *sink* decision (D5: reuse `AiBehaviorLogTarget`, do not invent `IBlueprintLogSink`) is **correct
and verified** — `SharedInstance`, `GetMessages()`, `Clear()` and `OnMessageAdded` all exist and are
already wired to the editor's "AI Behaviors" tab.

---

---

## 0. State right now

| | |
|---|---|
| **Repo** | `pjanec/HROT` |
| **Implementation branch (PUSH HERE)** | `claude/blueprint-macro-feature-sdmspn` |
| **Coordinator branch (do NOT push)** | `claude/blueprint-authoring-status-6sr5ld` · at `041a455a` |
| **HEAD** | Batch 23 items 1–3 on top of `041a455a` (Batches 20/21/22 merged + coordinator-verified) |
| **Next task** | [HANDOFF_Batch23_Overnight_Autonomous.md](HANDOFF_Batch23_Overnight_Autonomous.md) **item 4 only** (BP-108) |
| **Counts** | **63 open · 55 fixed · 1 refuted**, reconciled three ways |

**⚠ Do not create a pull request.** Not in any batch so far.

---

## 1. Batch 23 — outcomes

| # | Item | Outcome |
|---|---|---|
| 1 | 🔴 **BP-112** — CS9191 broke the build for every Library asset | ✅ **Done.** Reproduced first: **BP-110 had NOT fixed it**, unrelated defects sharing the Library surface. `MemoryMarshal.Write<T>` takes `in`, the emitter passed `ref`. ⭐ Durable half: `LibraryFunctionsDemo.bp.json`, the **first Library asset ever compiled by the real generator** — it is an `AdditionalFiles` entry, so a regression fails the **build**. Revert → 4 × CS9191 |
| 2 | 🔴 **BP-87** — picker offered 8 unresolvable types | ✅ **Items 1–5 done; row stays open for item 6 only** (⚖️ D3). List is blueprint-local, projected from `StaticTypeRegistry` — **AiShared untouched**, and its 1213/0 proves it. Item 4 shipped *with* item 3: C#'s full implicit ladder, 35 rungs, widening only |
| 3 | 🟠 **BP-113** — `CallPeerBlueprint` showed only `Outputs[0]` | ✅ **Done.** ⚠ **THREE** sites, not the two the handoff named — Stage5's lowering still collapsed to the first pin. ⚖️ D1 honoured (not unified) |
| 4 | 🟠 **BP-108** — the Print/Log node | 🛑 **NOT STARTED** (design note only). See the banner at the top |

**New rows registered this batch:** **BP-114** (Type combo displays `bool` for any asset storing an FQN
`TypeId` — pre-existing, mis-display only) · **BP-115** (no test covers a peer whose *name* needs
sanitizing — raised by the coordinator in §1 and registered so it is not lost).

### ⚠ Where a ⚖️ decision was wrong against the code
**D5's accessor** — see the banner. Everything else in §2 of the handoff held up.

## 2. What I shipped (all merged and coordinator-verified)

- **Batch 20** — **BP-92** dispatch choice at create (table-driven blank templates: `Empty`→Instance,
  `Function Library`→Library) · **BP-89** function outputs on the Return node (new `ReturnNodeDrawer`;
  `DrawParameterRows` *extracted* into `ParameterRowsView`; optional undo-recorder seam on
  `GraphSignatureEditModel`).
- **Batch 21** — **BP-103** 🔴 blank templates had zero graphs (crashed on open *and* broke
  `dotnet build`) · **BP-104** 🔴 Library declared-outputs ignored · **BP-105** inert Status combo ·
  re-ticked **BP-92**.
- **Batch 22** — **BP-109** 🔴 end-to-end smoke test (two entities, two blueprints, one shared Library;
  runs in **2 s**) → which found **BP-110** 🔴 (below). Registered **BP-111**.

### ⭐ BP-110 — the biggest find so far

**A `CallPeerBlueprint` had never compiled, anywhere.** `StatementEmitter` emitted
`__Peer_{id:X8}_Bp.Method(...)` while every emitter *declares* `{SanitizedName}_{BlueprintId:X8}_Bp`.
Nothing bridged them ⇒ `CS0103`. **Reproduced with caller and peer in the SAME merged compilation**,
which disproves the `NodeCoverageTests` comment claiming production resolves it by compiling siblings
together. Fixed by resolving the real class name at the call site from the sibling `BlueprintSignature`
(`SiblingSignatures` threaded via `Stage7_Emit` → `EmissionContext`). `__AiPrim_` had the same defect.

⚠ **A `using` alias does NOT work** — I tried it first. The test fixture's `MergeGeneratedSources` wraps
generated types in a block-scoped namespace while production leaves them global, so no single alias form
is correct for both (`CS0400`). **Do not "simplify" the fix back into an alias.**

---

## 3. Decisions already settled — do not silently re-litigate

1. **Dispatch choice lives in the recipe picker**, not a combo — there is no create dialog in
   production (`NewAssetLauncher` → recipe tree picker → vendored `SaveAsBrowserDialog` → `CreateNew`).
2. **The Instance blank template seeds `Tick`/*Function*, never `Tick`/Event.**
   `InstanceEmitter.EmitTickMethod` matches `Kind == Function && Name == "Tick"`; an Event graph of that
   name is emitted as an event handler ⇒ **the blueprint silently never ticks.**
3. **`AiPrimitive` returns `NodeStatus` unconditionally; `Library` only when it declares no outputs.**
   Zero-output Library→`NodeStatus` is deliberate and test-locked.
4. **No existing asset has had its dispatch migrated.**
5. **Peer-call fix = resolve the real name, not an alias** (see §2).

---

## 4. Gates + last measured baseline (clean tree)

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

build **0 errors** · Blueprints **2907 / 0 / 10 skipped** · AiShared **1213 / 0** · BTree **612 / 0** ·
Breakpoints **130 / 0** · NodeEditor.Core **208 / 0** · NodeEditor.UI **131 / 0** · Generators **189 / 0**.

⚠ **`Fdp.Presentation.Tests` is NOT in the gate list** — batch 19's 34 failures there were never
re-measured by me.

### 🔴 Flaky-test tax — [BP-111](Blueprint_Issues_Detail.md#bp-111), and it WILL bite you tonight

- Wall-clock perf assertions fail under full-suite CPU load and pass in isolation
  (`WhenNodePerfTests` — I measured **5/5 pass** isolated). **`WhenNode_EqsResult_Under150ns_perTick`
  is NOT on the documented flake list**; only its sibling `WhenNode_ValueChanged_Under100ns_perTick` is.
- ⚠ **`-v q` prints counts but NOT the failing test's name.** Re-run with
  `--logger "console;verbosity=normal"` to identify it. **This cost time twice in Batch 22.**
- Classify with `git stash` → re-run → `git stash pop`.

---

## 5. ⚠ Sub-agent hazards — these cost the most time across three batches

**One shared working tree.** Run agents **strictly sequentially**:
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```

1. **Concurrent `dotnet build` corrupts obj/bin** — produced a phantom "missing method" that vanished on
   retry.
2. **An agent doing revert-to-red leaves the tree intentionally broken for minutes.** I twice saw the
   fix absent from `git status` and once nearly reported six phantom failures from a stale test DLL.
   **Gate every commit on the fix being present in the diff, never on the agent's report.**
3. 🔴 **An agent can DIE SILENTLY mid-edit.** One did, leaving a test file that would not compile
   (`CS0246`), and I waited ~15 min before checking. **Detect it:** the transcript at
   `/root/.claude/projects/-home-user-HROT/<session>/subagents/agent-<id>.jsonl` stops growing *and* no
   `dotnet` runs. Do not read that file (it will blow up context) — only `stat` its size.
   Tonight, with nobody watching, **poll liveness rather than assuming progress**.
4. **Agents misreport git history.** Three separate agents claimed a commit was a "harness
   auto-commit" / "outer-session automation". **It was me committing their work.** Do not let that
   claim reach a doc.
5. `codebase-memory-mcp` is **not connected** and cannot be connected mid-session. CLAUDE.md's fallback
   applies: Grep/Glob/Read, and say so.

---

## 6. Working agreement

- **Delegate to Sonnet** anything not needing Opus. **Keep on Opus:** design calls, compiler semantics,
  diff review, gate runs, revert-to-red.
- **Never delegate verification.** Re-run gates yourself.
- **Fix, don't disable.** Never weaken an assertion to make it pass.
- **Verify claims against code** — the audit register has now been wrong **eleven** times, and I
  corrected the handoff in *every* batch. Corrections are worth more than compliance.
- **Anything knowingly left behind gets a tracker ROW**, not a note inside a `DONE` block (the BP-102
  lesson).
- **Reconcile counts three ways**: checkbox tally, complexity-column sums, header total. Two rows match
  neither pattern — refuted **BP-46** and an abandoned *"Squad-quartet & dispatcher lowering"* row.
  They are the permanent ±1; do not "fix" them.
- Report: **actual gate numbers** (not "gates green"), what went red under revert, what was delegated.

---

## 7. Open items I own or created

| Item | Note |
|---|---|
| **BP-111** | Flake list incomplete + gate hides failing test names. **Cheap, and it taxes every session** |
| **BP-106** | An `AiPrimitive` graph's declared Outputs are silently dropped — the only silent case left in that family. Wants a Stage 2 error (⚠ needs a `[CoversDiagnosticCode]` test) |
| **BP-107** 📐 | `Return.Status` is a compile-time constant ⇒ `Running` inexpressible. **Architect round required** |
| **BP-102** | Graph Signature window edits still not undoable (holds `DirtyTracker`, no `IEditService`) |

---

## 8. 🔴 The largest outstanding risk — unchanged for six batches

**The T-series (T1–T7, [BP-73](Blueprint_Issues_Detail.md#bp-73)) is performable but STILL UNPERFORMED.**

And the pattern that keeps repeating: **every defect in Batches 21 and 22 came from running the thing,
not from the suite.** Batch 21's three came from the user at the UI after Batch 20's gates were green
and its code reviewed clean; Batch 22's BP-110 came from the first test that ever executed the feature.
A green suite has now repeatedly failed to find what one real execution found immediately.
