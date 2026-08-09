# HANDOFF — Batch 25: the authoring-path matrix, then the string nodes

> **Read in full. Self-contained.** You are an implementation session; a coordinator session owns the
> plan and reviews your diff.
>
> 🔄 **The user is running a visual check in parallel** on a different machine and checkout. Nothing you
> push blocks them; anything they find lands in a **later** batch. **Do not widen to chase reports.**

---

## 0. ⚡ How to work

**You are on Opus. Delegate to Sonnet everything that does not need Opus-level reasoning.** Split stated
per item.

⚠ **Sub-agents share ONE working tree.** Sequential only:
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```

| | |
|---|---|
| **Push to** | `claude/blueprint-macro-feature-sdmspn` — merge the coordinator branch first |
| **New finding IDs** | **BP-120+**; the tracker/detail docs are **yours** this batch |
| **Revert-goes-red** | Every fix, **never delegated** |
| **Commit per item** | So a later failure does not lose earlier work |
| **Stop cleanly at a boundary** | Three finished items beat five half-finished |
| **No pull request** | Not in any batch so far |

---

## 1. ✅ Batch 24 verified — and two things you got right that the coordinator got wrong

Gates on the merged tree, all eight green: build **0 errors** · Blueprints **2958 / 0 / 10 skipped**
(+21, reconciling exactly to your 16 + 5) · everything else at baseline.

**BP-116 and BP-117 are good work.** Two calls worth naming:

- ⭐ **`ReturnsDefault` as a flag rather than a new `IrTerm_ReturnDefault` type** — because both switches
  over `IrTerminator` end in a catch-all, so a new kind could have been *silently* mis-emitted. That is
  trap #5, and spotting it in the middle of fixing something else is exactly right.
- ⭐ **Declaring the peer inside the existing `RecordPropertyEdit`** so one gesture stays one undo step.

**You were right and the handoff was wrong, twice:**

1. **The BP-118 deferral.** The coordinator's ordering put it second. You deferred it because
   `SmokePatrol` is the *peer-call* sample, so shipping it under `Assets/Blueprints/` makes it
   generator-compiled and it **could not compile until BP-116/117 landed**. Correct; the ordering was
   wrong.
2. **The "+1/+1 count offset."** The coordinator checked this suspecting sloppiness and was wrong: a
   looser regex catches two **struck-through** rows (`~~BP-46~~` refuted, `~~Squad-quartet~~` abandoned),
   one `[x]` and one `[ ]`. 💡 **Small ask: tighten the check to skip `~~` rows** so it is exact again —
   a reconciliation check with a standing known offset is a weaker check.

✅ **The zero-alloc attribution is confirmed** — the user did rule that directly to you. §3b of the
design note stands as binding.

---

## 2. 🟢 Item 1 — flip `BP1657` to **Warning**. Do this first; it unblocks Item 2.

⚖️ **User decision:** *"warning+return default is a perfect solution."*

You chose `Error`, and flagged yourself that **Unreal silently returns defaults** on such a path so
`Error` is stricter than a designer arriving from Unreal expects. The user agrees with your alternative.

⭐ **This is not just ergonomics — it is what makes Item 2 able to do its job.** Your own note:

> *"the `return default;` emit is currently defense-in-depth only: BP1657 being an Error means the
> pipeline never reaches emit — the Roslyn-level proof belongs to the Item-0 matrix."*

**As an Error, that code path can never be proven.** As a Warning the pipeline reaches emit, so the
matrix in Item 2 can compile it through Roslyn and prove `return default;` is valid for both a scalar
and a `ValueTuple`. **Right now a path shipped last batch has no Roslyn-level proof at all.**

**Delegation:** 🟢 Sonnet for the severity flip + test updates. 🔴 Opus confirms the emit path is now
actually reached — that is the whole point of the change.

---

## 3. 🔴🔴 Item 2 — **the authoring-path compile matrix.** The headline. Do not defer it again.

This was Item 0 of Batch 24. You fixed the three defects it was designed to find but did not build it,
so **the class of bug remains open** — and BP-117 has now added a shipped, unproven code path to it.

> User: *"i still dont understand why i need to test the stuff that can be tested headlessly … the AI
> agent should be able to compose such a blueprint set and compile it automatically."*

**Four consecutive batches have had a human find what a headless test should have.** This is the fix.

### The four rules — each one is load-bearing

| | |
|---|---|
| **1 · Compose through the editor's own APIs** | ⭐ **The whole point.** Build assets the way the editor does — `BlueprintNewAssetService`, the node-create path, the peer picker, `RetypeParameter`. **NOT** by writing JSON. **BP-116 was invisible to every test that wrote `CallablePeers` itself**, and you proved that: `SmokePatrol.bp.json` carries one literally |
| **2 · Compile through the real generator** | Not `CompileAndLoad`. BP-112 showed in-memory does not treat warnings as errors; BP-116/117 are generator-path failures |
| **3 · Assert 0 diagnostics, not `Succeeded`** | `BlueprintCompiler.Compile(...).Succeeded` never invokes Roslyn — that is how BP-104 and BP-110 both hid |
| **4 · Sweep a matrix** | dispatch {Instance, Library} × outputs {0,1,2,3} × {explicit Return, chain ends without one} × {no call, local call, peer call} × arg types {int, float, bool, ushort, FixedString32} |

### ⭐ The check that decides whether this is worth anything

**Build it first against the tree with BP-116/117 reverted, and confirm those cells go red.**

If the matrix passes on a tree where BP-116 and BP-117 are reverted, **it is not composing through the
authoring path** and is worth nothing until it does. That is the single most important verification in
this batch — more than any individual fix.

⚠ **When it goes red on new combinations, that is the deliverable succeeding.** Expect it. **Register
each as its own row (BP-120+); do not fix them all here.** Report what it found.

**Delegation:** 🔴 **Opus** — the harness and the authoring-API composition. Getting this wrong
reproduces the exact blind spot it exists to close, so it is not delegable. 🟢 **Sonnet** — the case
table once the harness shape is settled.

---

## 4. 🟢 Item 3 — BP-118, now unblocked

📄 [detail](Blueprint_Issues_Detail.md#bp-118) · your dependency call was right, and **BP-116/117 have
now landed**, so it is clear to proceed.

Ship `SmokePatrol` / `SmokeGuard` / `SmokeMathLib` under `Assets/Blueprints/` as well. ⚠ That makes them
`<AdditionalFiles>` and therefore **generator-compiled, so they must be clean** — which is the point.
It also means a regression in any of them fails the solution build before a test runs.

**Delegation:** 🟢 Sonnet.

---

## 5. 🟠 Item 4 — `FixedString128` **+ the span constructors**

⚠ **`FixedString` has only a `(string)` ctor — verified.** Both halves are needed:

- `Fdp.Core/FixedString128.cs` — mirror of `FixedString64.cs` (`Size = 128`, `MaxLength = 127`)
- ⭐ **`ReadOnlySpan<char>` ctors on 32 / 64 / 128** — **load-bearing, not optional.** Without them
  `new FixedString128(...)` still needs a managed string and the zero-alloc ruling buys nothing

⚠ **~10 production sites reference the family** — full list in
[HANDOFF_Batch24](HANDOFF_Batch24_DebugPrint.md) §2. ⚠ **`FDP/ExtDeps/GizmoMap` has its own separate
`FixedString32` — leave it alone.** ⚠ Mirror the `Fdp.Core.Tests` cases **including truncation**.

**Delegation:** 🟢 **Sonnet**, entirely — mirror-an-existing-pattern work.

---

## 6. 🟠 Item 5 — `Print String` + `Format String`

📄 **Design: [PrintString_Node_Design.md](PrintString_Node_Design.md) rev 3 — read it first.**
📄 Detailed sites + traps: [HANDOFF_Batch24](HANDOFF_Batch24_DebugPrint.md) §3 (still current).

Unchanged from Batch 24. The three things that invert earlier instructions, restated because they are
easy to cargo-cult:

| | |
|---|---|
| **F1** | No optional data-in pins exist — `BP4001` + `default(T)` for every unwired one. Pins are **derived by parsing the format string**, Unreal-style. **No `ArgCount` property** |
| **F2** | ⭐ **NO `Stage0_Rehydrate` case.** `BuiltInNodeRegistry` is the single source; these nodes' pins derive purely from their own `Format` property. Batch 23's "both projections must move together" was right for `CallPeerBlueprint` and is **wrong here** |
| **F3** | The sink helper goes in **`Fdp.Core.Logging`** — `BehaviorLog` has the right logger name but the wrong assembly (`CS0246` on hot reload only) |

⭐ **Zero-alloc is binding** (§3b): emit a **compile-time interpolated string**, `stackalloc` +
`TryWrite` into a span, then the `FixedString` span ctor from Item 4. **Not `string.Format`.**

⚠ **Cover the hot-reload path, not only `CompileAndLoad`** — F3 is a defect that appears only there.

**Delegation:** 🔴 Opus — the format parser, registry shapes, Stage5, emit. 🟢 Sonnet — node models,
palette, drawers, detail-panel UI, test bodies.

---

## 7. 🟢 Fillers — only if blocked or with room

- **BP-119** — undo of a peer-node *add* leaves the peer declared. Your own row; explicit deletion
  already retracts.
- **BP-111** — the wall-clock perf flake. Three consecutive gate runs, **a different member each time**
  ⇒ it is the whole class, not the two named tests. Calibrated budget or opt-in category.
  ⚠ **"Fix, don't disable"** — do not just delete the assertions.
- **BP-115** — peer name needing sanitizing. ⚠ **Item 2's matrix may cover it for free** — check first.

---

## 8. Gates

```bash
dotnet build IOS-IG-SimHost.sln -v q --nologo
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj -v q --nologo --logger "console;verbosity=normal"
dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj -v q --nologo --logger "console;verbosity=normal"
dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Hrot.BTree.Editor.Tests.csproj -v q --nologo --logger "console;verbosity=normal"
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj -v q --nologo --logger "console;verbosity=normal"
dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj -v q --nologo --logger "console;verbosity=normal"
dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj -v q --nologo --logger "console;verbosity=normal"
dotnet test Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/Hrot.AiEditor.Generators.Tests.csproj -v q --nologo --logger "console;verbosity=normal"
```

**Baseline — coordinator-measured on the merged Batch-24 tree:** build **0 errors** ·
Blueprints **2958 / 0 / 10 skipped** (2968 total) · AiShared **1213 / 0** · BTree **612 / 0** ·
Breakpoints **130 / 0** · NodeEdit Core **208 / 0** · UI **131 / 0** · Generators **193 / 0**.

⚠ **Item 4 touches `FDP/Engine/Fdp.Core`** — that rebuilds nearly everything. Run all eight, plus
`Fdp.Core.Tests`.
⚠ Classify any failure with `git stash` → re-run → `git stash pop`.

---

## 9. Reporting back

1. **Per-suite gate numbers you actually ran** — not "gates green".
2. **What you reverted and confirmed went red**, per item.
3. ⭐ **For Item 2 specifically: did the matrix go red with BP-116/117 reverted?** State it plainly. If
   it stayed green, say so — that is the most important negative result this batch could produce.
4. ⭐ **Everything else the matrix found**, as BP-120+ rows.
5. **What you delegated to Sonnet, what you kept.**
6. Anything in this handoff that is wrong against the code. **You have been right against it four times
   now** — BP-118's dependency and the count offset are just the latest two. Keep doing that.

⚠ **Register what you leave behind as a tracker row, not a note inside a `DONE` block.**

**Done =** gates green vs baseline · tracker rows `[x]` with `DONE` notes · counts reconciled three ways
· committed per item · pushed to `claude/blueprint-macro-feature-sdmspn`. **No PR.**
