# RESUME — Coordinator session (2026-08-08)

> **Read this first on resume. Self-contained.** You are the **coordinator**: you own the tracker,
> write handoffs, review returned diffs, re-run gates, and pick the next batch. You do **not** write
> feature code.

---

## 0. ✅ Batch 22 is VERIFIED — gates run 2026-08-08 on the merged tree

| Suite | Result |
|---|---|
| **Solution build** `IOS-IG-SimHost.sln` | ✅ **0 errors** |
| Blueprints | ✅ **2907 passed / 0 failed / 10 skipped** (2917 total) |
| AiShared | ✅ **1213 / 0** |
| BTree editor | ✅ **612 / 0** |
| Breakpoints | ✅ **130 / 0** |
| NodeEdit Core | ✅ **208 / 0** |
| NodeEdit UI | ✅ **131 / 0** |
| Generators | ✅ **189 / 0** |

**+2 net tests vs the Batch-21 baseline of 2905** — exactly the two Batch 22 added
(`CallPeerBlueprintRoslynTests`, `BP109_SmokeTestEndToEndTests`, one `[Fact]` each). Reconciled.
**No flakes fired this run.**

⚠ **Run gates with `--logger "console;verbosity=normal"`.** `-v q` prints counts but not the failing
test's *name* — that is why Batch 22 had to register BP-111.

<details><summary>The gate command list</summary>

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
</details>

Known flakes: `PdbEmbeddedSourceTests`, `WhenNodePerfTests.WhenNode_ValueChanged_Under100ns_perTick`.
⚠ Classify any failure with `git stash` → re-run → `git stash pop`.

---

## 0b. 🌙 Batch 23 is an **overnight autonomous** batch — in flight

📄 **[HANDOFF_Batch23_Overnight_Autonomous.md](HANDOFF_Batch23_Overnight_Autonomous.md)**

Four items, ordered, independently committable: **BP-112** (CS9191 + the missing `Dispatch: Library`
generator fixture) → **BP-87** (type picker / registry / coercions) → **BP-113** (peer call N outputs) →
**BP-108** (Print/Log node). Expect it to stop at an item boundary; that is the designed outcome.

**Six ⚖️ decisions were pre-made so the session never has to ask overnight** — §2 of the handoff.
The two that took real research:

1. ⚠ **BP-87 must NOT widen `BlackboardTypeHelper.DefaultKnownTypeNames`.** That array lives in
   `Hrot.Editor.AiShared` and is shared by the **BTree and HSM** editors as well as blueprints
   (`BehaviorTreeAssetMapper:453`, `HsmAssetMapper:473`, `BlackboardTypeChoiceBuilder:46`). The detail
   entry said "add to the dropdown" without saying *which*, and the obvious answer is the wrong one.
   ✅ Verified the clean path: the consumer `ParameterRowsView` is already blueprint-local, and
   `Hrot.Blueprints.Editor` → `Core` → `Compiler`, so `StaticTypeRegistry` **is** reachable from the
   editor ⇒ a blueprint-local list derived from the registry is feasible without AiShared ever learning
   about the compiler.
2. ⭐ **BP-108 needs no new log-sink abstraction.** `Fdp.Core/Logging/AiBehaviorLogTarget.cs` is already
   an NLog `Target` **and** an `IMessageLogSource` with a shared singleton and `OnMessageAdded`, already
   wired to the editor's "AI Behaviors" tab. Its design point *"the sink must be interceptable"* is
   satisfied by shipped code. Shape pre-approved as fixed-arity `Arg0..Arg2` + literal format + all five
   verbosity levels — conservative enough to defend without the architect **because every piece reuses
   existing machinery**.

---

## 1. Branches

| | |
|---|---|
| **Coordinator** | `claude/blueprint-authoring-status-6sr5ld` — docs/tracker; **you push here** |
| **Implementation** | `claude/blueprint-macro-feature-sdmspn` — merged in as of this commit |
| **Do not** | create a pull request. Not in any batch so far |

**Counts:** **63 open · 53 done**, reconciled three ways (checkbox tally · header total ·
complexity-column sum). Verify with the snippet in §6 after any tracker edit.

---

## 2. ⚠ Process failure this session — a numbering collision. Fix the rule.

**Both sessions independently allocated `BP-110` and `BP-111` for different defects.** Resolved by
renumbering **mine**, because theirs are shipped and their commit messages name them:

| ID | Owner | Subject |
|---|---|---|
| **BP-110** | theirs (`[x]` done) | a `CallPeerBlueprint` has **never compiled** |
| **BP-111** | theirs (open) | wall-clock perf flakes; incomplete flake list |
| **BP-112** | mine (open) | CS9191 `ref`/`in` in the generated Library adapter — *was BP-110* |
| **BP-113** | mine (open) | `CallPeerBlueprint` projects only `Outputs[0]` — *was BP-111* |

⭐ **Root cause: I violated my own shared-file protocol.** That protocol says the tracker and detail
files belong to the **implementation session for the duration of a batch**. I allocated new IDs while
Batch 22 was in flight.

**The rule to enforce from now on:** while a batch is in flight, the coordinator **records new findings
in conversation and in a scratch note — not as tracker rows**, and allocates IDs only after merging.
If a finding must be registered immediately, allocate from a **reserved high block (BP-200+)** and
renumber on merge.

---

## 3. Batch 22 — DELIVERED, NOT YET VERIFIED

Commits `e214c4dc`, `83f7b8a1`, `466ea11a`, `853ace7b`.

| Item | State |
|---|---|
| **BP-109** end-to-end smoke test | `[x]` — 3 recipe assets (`SmokePatrol`, `SmokeGuard`, `SmokeMathLib`) + `BP109_SmokeTestEndToEndTests.cs` |
| **BP-110** peer call never compiled | `[x]` — plus `CallPeerBlueprintRoslynTests.cs` |
| **BP-111** perf flakes / flake list | `[ ]` registered |

⭐ **Their headline finding: a `CallPeerBlueprint` has NEVER compiled.** They reproduced it with caller
and peer in the **same merged compilation**, which **disproves the comment in `NodeCoverageTests`**
claiming production works because siblings compile together. **That is the eleventh wrong audit claim.**

### ⚠ Two corrections they made to my handoff — I was wrong, they were right

1. **Recipes cannot break the solution build.** `Recipes/Blueprints` are `Content`; only
   `Assets/Blueprints` are generator-compiled `AdditionalFiles`. My handoff warned them otherwise.
   ⚠ **My BP-103 analysis is still correct** — the user's `FuncLib1` was in `Assets/`, which *does*
   break the build. Do not "correct" BP-103 on the strength of this.
2. **Two entities in one world needed no code changes** — the risk I flagged did not materialise.

### ✅ Verification complete

- [x] **Gates run — all eight green** (§0).
- [x] **Diff of `e214c4dc` reviewed.** It touches compiler surface (`CSharpEmitter`,
      `EmissionContext`, `StatementEmitter`, `Stage7_Emit`, `BlueprintCompiler`), not test scaffolding.
      **The fix is sound.** `ResolveSiblingClassName` matches on `BlueprintId` and rebuilds
      `{SanitizedName}_{BlueprintId:X8}_Bp`; the reasoning for resolving the real name rather than
      emitting a `using` alias (production emits into the global namespace, the test fixture's
      `MergeGeneratedSources` wraps in one, so no single alias form is correct for both) is correct.
      Falling back to the old `__Peer_` name on an unresolved id correctly keeps Stage 2's BP1301/BP1302
      as the author-facing diagnostic.
- [x] ⭐ **Checked the one way it could have been subtly wrong — it is not.** Both *production*
      producers of a `BlueprintSignature` derive `SanitizedName` through `Sanitizer.SanitizeName`
      (`BlueprintSignatureParser.cs:39`, `BlueprintSignatureBuilder.cs:19`) — the same function
      `Stage5_Schedule.cs:58` uses for the asset's own name. **The two names cannot drift.**
- [x] Test count reconciles: **+2 vs baseline**, one `[Fact]` in each new file.

⚠ **Small latent trap left behind, worth a row but not a fix:** four tests construct signatures with
`SanitizedName: peer.Name` **raw** — `CallPeerBlueprintRoslynTests:171`,
`BP109_SmokeTestEndToEndTests:85`, `RecipeIntegrityTests:64`, `NodeCoverageTests:534`. It works only
because every fixture name is already sanitizer-clean, so **no test covers a peer whose name needs
sanitizing**. It fails loudly (CS0103) rather than silently ⇒ low risk. Handed to Batch 23 as §1 of its
handoff.

- [ ] **BP-112** (CS9191) — still open; handed to Batch 23 as item 1, with an explicit instruction to
      **confirm it still reproduces first** in case the emitter changes fixed it incidentally.

---

## 4. The user's visual check — stopped at section B

Guide: RESUME → *🎯 Batches 20+21 — DO THIS FIRST*. **Sections C–F, including the T-series, were never
reached.** The T-series is unverified for a **sixth** batch.

| Result | |
|---|---|
| ✅ | `Function Library` template exists; created + opened without throwing ⇒ BP-103 fixed, BP-92 re-tickable |
| ✅ | in-memory hot reload OK |
| 🔴 | **CS9191** on full build ⇒ **BP-112** |
| 🔴 | `ushort` → **BP1500** ⇒ **BP-87 confirmed live** |
| 🔴 | `CallPeerBlueprint` shows one output pin ⇒ **BP-113** |

⚠ **Tell the user to delete `FuncLib1.bp.json`** if still present — while it contains `ushort` it
breaks the solution build.

---

## 5. Next batch — ✅ issued as Batch 23 (see §0b)

**BP-112 + BP-113**, and they converge:

- **BP-112** (CS9191) needs a **`Dispatch: Library` fixture in `Hrot.AiEditor.Generators.Tests`** —
  every fixture there is an `AiPrimitive` HillAssault2 asset, so **no Library asset has ever gone
  through the generator**. That blind spot is why the user found it and the suite did not.
- **BP-113** (peer call, one output pin) is now **downstream of their BP-110 fix** — re-check whether
  multi-output across assets works before scoping it.

Also open and worth sequencing: **BP-108** 📐 (Print/Log node — design-note-first; no `ToString`/
`Format`/`Concat` node and no string coercion exists, so it needs a format-literal + typed-args pin
shape), **BP-85 + BP-100** (breadcrumb + kind icons, step 3 of the UX plan), **BP-101** (F2 rename).

---

## 6. Standing rules — do not re-derive

- **Model delegation:** implementation sessions are Opus and **must** delegate to Sonnet what does not
  need Opus. State the split **per item** in every handoff. ⚠ **Subagents share one working tree** —
  run them sequentially, wait for `ps aux | grep -c "[d]otnet build\|[d]otnet test"` to hit 0, and gate
  every commit on the fix being in the tree, not on an agent reporting success.
- **Never delegate verification.** Diff review, gates and revert-goes-red stay on Opus.
- **Ask in plain prose**, never the multiple-choice widget.
- **Verify claims against code** — the audit register has now been wrong **eleven** times.
- **Anything left behind gets a tracker row**, not a note inside a `DONE` block (the BP-102 lesson).

**Count check after any tracker edit:**

```bash
cd docs/blueprints && python3 -c "
import re; from collections import Counter
t=open('Blueprint_Issues_Tracker.md').read()
op=len(re.findall(r'^- \[ \] \*\*\[BP-',t,re.M)); dn=len(re.findall(r'^- \[x\] \*\*\[BP-',t,re.M))
h=re.search(r'\| \*\*Total\*\* \| \*\*(\d+)\*\* \| \*\*(\d+)\*\* \|',t)
c=Counter()
for l in t.split('\n'):
    m=re.match(r'^- \[([ x])\] \*\*\[BP-',l); k=re.search(r'\`(WIRING|RW-L|RW-M|RW-H)\`',l)
    if m and k: c[(k.group(1),m.group(1))]+=1
print('tally',op,dn,'header',h.group(1),h.group(2))
print({k:c[k] for k in sorted(c)})"
```
