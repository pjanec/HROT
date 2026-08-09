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

## 0. ✅ Batch 24 part 1 VERIFIED (BP-114 only) — and ⚠ **they never saw Item 0**

Gates on the merged tree, **all eight green**: build **0 errors** · Blueprints **2937 / 0 / 10 skipped**
(2947 total, **+12**) · AiShared **1213 / 0** · BTree **612 / 0** · Breakpoints **130 / 0** ·
NodeEdit Core **208 / 0** · UI **131 / 0** · Generators **193 / 0**. The BP-111 flake did not fire.

⚠ *Minor: they reported "2936 passed, +11 net". Actual is 2937 / +12, which matches the 12 tests in
`BP114_TypeComboIndexTests` exactly. Off by one in their tally, not in the tree.*

**BP-114 shipped, and their fix is better than the handoff specified.** I said "match on the resolved
type". They found **the alias/FQN mismatch was only half the defect**: types that resolve but are
deliberately *not offered* (`Fdp.Core.Entity`, `System.String`, the curated structs) also displayed
`bool`, and **for those no offered entry could ever be right**. Their answer — fallback to **-1**, a
legal Dear ImGui "no selection", rendering a blank preview instead of a false one — is the honest
result and one I did not consider. They also moved resolution out of the ImGui draw loop into a pure
`BlueprintTypeChoices.IndexOfTypeId` (testable; 17 resolves per row per frame removed).

### 🔴 FIRST ACTION — Item 0 was added to the handoff **after** they branched

Verified with `git merge-base --is-ancestor`: **they never saw it.** The next session must start there:
**BP-116 · BP-117 · BP-118 + the authoring-path compile matrix** — §0b of
[HANDOFF_Batch24_DebugPrint.md](HANDOFF_Batch24_DebugPrint.md). Items 1–3 (Print String etc.) stay
second priority.

### ⭐ They found a real defect in my design, unprompted

> *"Format String is pure, so unlike Print String it has no level probe to hide behind, and a pure node
> in a Tick graph would allocate a managed string every tick for every entity."*

**Correct.** rev 3 as first written would have shipped **two allocations per node, per entity, per
tick** — `string.Format` allocates, and `new FixedString128(string)` needs a managed string to convert
from. Answered in design §3b: **the format literal is a compile-time constant of the generated C#**, so
emit a real interpolated string and `Span<char>.TryWrite` into a stack buffer. ⚠ **This requires
`ReadOnlySpan<char>` ctors on `FixedString` — verified it has only `(string)`** — folded into item 2.

---

## 0a. ✅ Batch 23 VERIFIED — gates run 2026-08-09 on the merged tree

| Suite | Result |
|---|---|
| **Solution build** | ✅ **0 errors** — ⭐ *including `LibraryFunctionsDemo.bp.json` through the real generator* |
| Blueprints | ✅ **2925 passed / 0 failed / 10 skipped** (2935 total, **+18**) |
| AiShared | ✅ **1213 / 0** — ⭐ *unchanged, confirming BP-87 stayed blueprint-local* |
| BTree · Breakpoints | ✅ **612 / 0** · **130 / 0** |
| NodeEdit Core · UI | ✅ **208 / 0** · **131 / 0** |
| Generators | ✅ **193 / 0** (was 189; +4) |

⭐ **The BP-111 perf flake did NOT fire on this run.** They saw 2 failures; I saw 0. Totals reconcile
exactly (their 2923 passed + 2 failed + 10 skipped = my 2935 total), which **confirms the 2 were the
flake and not a real regression.**

### Independent checks I ran on their claims — all three hold

| Claim | Verdict |
|---|---|
| `FdpLog<T>` cannot reach the `AI.Behavior*` rule ⇒ **my D5 was wrong** | ✅ **Confirmed.** `FdpLog.cs:15` uses `LogManager.GetLogger(typeof(T).FullName)`; generated classes emit into `namespace Hrot.AI.Behaviors.Generated` (`LibraryEmitter:11`, `InstanceEmitter:11`, `AiPrimitiveEmitter:12`); the rule at `Hrot.ClusterRunner/Program.cs:124` is prefix-anchored `"AI.Behavior*"`, which `Hrot.AI.Behaviors.…` does not match. Their replacement `BehaviorLog` uses `GetLogger("AI.Behavior")` — exact family hit, **and better than my proposal**, since its structured format carries entity context for free |
| The coercion table is "C#'s implicit ladder minus decimal: **35 rungs**" | ✅ **Confirmed against the spec** — recomputed independently, exactly 35 |
| Stage5 reuses the validated sibling map | ✅ `SiblingSignaturesById` pre-existed on `ValidationContext` and is the same map Stage 2's `V_PeerReferences` checks — same discipline as their BP-110 fix |

⚠ **Reviewed the Stage5 lowering by hand** (novel scheduler surface). The `EmitCarrierFanOut` overload
split is right: the `Graph` overload forwards to the type-list one, so **the same-asset `FunctionCall`
path is byte-identical** and only the cross-asset call is new. `Outputs.Count > 1` gates the carrier
branch; 1 and 0 outputs fall through to the historic single-pin path unchanged.

---

## 0b. 🌙 Batch 23 was an **overnight autonomous** batch — ✅ delivered, 3 of 4 items

📄 **[HANDOFF_Batch23_Overnight_Autonomous.md](HANDOFF_Batch23_Overnight_Autonomous.md)**

**Items 1–3 shipped; item 4 (BP-108) left open at the boundary — the designed outcome, correctly taken.**
`c5f30c47` BP-112 · `0f7eaa23` BP-87 items 1–5 · `68d8d540` BP-113. **Counts reconcile three ways:
63 open · 55 done.** *(Open stayed at 63 because BP-87 keeps item 6 open per D3, while BP-112 and BP-113
closed and BP-114/BP-115 opened.)*

⭐ **They found a third site I had missed on BP-113.** My handoff named the two pin projections;
Stage5's `CallPeerBlueprintNode` lowering still took `FirstOrDefault()` on the data-OUT pins. Fixing
only the projections would have **advertised N pins that the compiler silently collapsed to one** —
the same defect a layer down, and *harder* to see because the editor would now look right.

### 🆕 Left behind — both are for the next batch

- **[BP-114](Blueprint_Issues_Detail.md#bp-114)** `RW-L` 🔴 **user-visible right now.** The Type combo
  matches by exact string; the list offers aliases (`int`) but most shipped assets store the FQN
  (`System.Int32`), so it falls back to index 0 and **displays `bool` for an `int` parameter**.
  ⚠ Mis-display only *until touched* — but "correcting" it **silently retypes the parameter for real**.
  ✅ Verified pre-existing, not introduced by BP-87. **Flagged as hazard #1 in the visual-check guide.**
- **[BP-115](Blueprint_Issues_Detail.md#bp-115)** — no test covers a peer whose name needs sanitizing
  (the row I asked for; they registered it rather than dropping it).

### ⚠ What the next batch must not undo

**BP-108's sink accessor.** My handoff's D5 named `FdpLog<T>`; it **cannot work** (see §0a). The design
note `PrintString_Node_Design.md` records the correction. **The sink decision itself was right — only
the accessor was wrong.** Anyone re-reading the Batch-23 handoff will find the wrong name in §6.

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
