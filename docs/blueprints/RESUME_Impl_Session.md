# RESUME — implementation session · **Batch 24 (Print String + Format String) is next**

> **Written immediately before a context compaction. Self-contained; assumes no prior conversation.**
> **You are an *implementation* session**; a coordinator session owns the tracker and writes handoffs.

## 0. Start here

1. Read **[HANDOFF_Batch24_DebugPrint.md](HANDOFF_Batch24_DebugPrint.md)** (205 lines) in full.
2. Read **[PrintString_Node_Design.md](PrintString_Node_Design.md) rev 3** — *the handoff says before any
   code, and it means it: rev 2/rev 3 overturned three things rev 1 got wrong.*
3. Skim **[Architect_Question_26_Print_Node.md](Architect_Question_26_Print_Node.md)** for what is still open.

| | |
|---|---|
| **Repo** | `pjanec/HROT` |
| **Implementation branch (PUSH HERE)** | `claude/blueprint-macro-feature-sdmspn` |
| **Coordinator branch (do NOT push)** | `claude/blueprint-authoring-status-6sr5ld` · at `e77fa473` |
| **HEAD** | already reset onto `e77fa473`; **Batch 23 is merged and coordinator-verified** |
| **Counts** | **63 open · 55 fixed · 1 refuted**, reconciled three ways |
| **New finding IDs** | **BP-116+** (tracker/detail docs are yours for the batch) |

⚠ **Do not create a pull request.** None in any batch so far.

---

## 1. Batch 24 — three items, in this order

| # | Item | Model | Note |
|---|---|---|---|
| 1 | 🟢 **BP-114** — Type combo shows `bool` for any asset storing an FQN `TypeId` | 🟢 **Sonnet, entirely** | **Do it first.** The user is mid-visual-check with this as hazard #1. Match on resolved `IrTypeRef.FullName` so `int` and `System.Int32` collapse to one entry. *I registered this row in Batch 23* |
| 2 | 🟢 **`FixedString128`** | 🟢 **Sonnet, entirely** | Straight mirror of `FixedString64.cs` (`Size = 128`, `MaxLength = 127`) — but **~10 production sites** reference the family; handoff §2 lists every one. ⚠ **`FDP/ExtDeps/GizmoMap` has its own separate `FixedString32` — leave it alone** |
| 3 | 🔴 **`Print String` + `Format String`** (BP-108) | 🔴 **Opus** for the parser, registry shapes, Stage5, emit and the `Fdp.Core` layering; 🟢 Sonnet for node models, palette, drawers, test bodies | The batch. Item 3 **needs item 2** (`Format String`'s `ResultType`) |

---

## 2. 🔴 The three things that changed since rev 1 — do not re-derive them wrongly

### F2 ⭐ **NO `Stage0_Rehydrate` case. This removes work.**

`BuiltInNodeRegistry` is the **single source**: `NodePinSchema` delegates to it (`FromRegistry:226-228`),
and Stage0 builds the canonical list *from static registry shapes*, enriching **only** kinds whose pins
depend on data **outside** the node. Both new nodes derive their pins purely from their own `Format`
property ⇒ **register once.**

⚠ **The Batch-23 handoff's "both projections must move together" was right for `CallPeerBlueprint`
(whose pins come from a *sibling's* signature) and is WRONG for these nodes.** Do not cargo-cult my
BP-113 fix here. `ArrayMakePins:299` is the shape to mirror — it already reads a node property — except
you derive the pin **count**, which is new.

### F3 ⭐ **The sink helper goes in `Fdp.Core.Logging`. Two accessors are ruled out.**

| Ruled out | Why |
|---|---|
| `FdpLog<T>` | logger name is `typeof(T).FullName` ⇒ `Hrot.AI.Behaviors.Generated.…`, which the prefix-anchored `"AI.Behavior*"` rule does not match. **This was my Batch-23 finding and it stands** |
| `BehaviorLog` | right logger name, **wrong assembly** — `Hrot.AI.Behaviors` is not guaranteed loaded when `MetadataReferenceResolver.ForRuntimeAssemblies` snapshots the AppDomain ⇒ `CS0246` on hot reload, unattributable. **[BP-62](Blueprint_Issues_Detail.md#bp-62)'s shape recurring** |

✅ The **sink itself is unchanged and verified**: `Fdp.Core/Logging/AiBehaviorLogTarget.cs` — an NLog
`Target` *and* `IMessageLogSource`, with `SharedInstance` / `GetMessages()` / `OnMessageAdded`, wired to
the editor's "AI Behaviors" tab by `Hrot.ClusterRunner/Program.cs:124`. **Do not invent an
`IBlueprintLogSink`.** `CSharpEmitter.EmitUsings:133` emits `using Fdp.Core;` unconditionally, so a
helper there is always reachable from generated code.

### Arity ⭐ **Derived by parsing the format string. There is no `ArgCount` property.**

Unreal's `Format Text` creates one input per `{}` found in the format text, with **named** placeholders.
Adopted — it beats an `ArgCount` property on every axis and satisfies **F1** (there is no such thing as
an optional data-in pin: `Stage5.ResolveDataPin:2126-2159` emits **`BP4001` + `default(T)`** for every
unwired one, so a speculative pin is a guaranteed diagnostic).

```
[ Print String ]  exec In --> --> exec Out      [ Format String ]  (pure -- no exec)
   Threat : float   <- derived                     Threat : float  <- derived
   Squad  : int     <- derived                     Result : FixedString64  <- declared
 props: Format, Level (all five)                 props: Format, ResultType (32|64|128)
```

`{Name}` letters/digits/underscore · **first-appearance order fixes pin order** · a repeat is **one**
pin used twice · `{{`/`}}` escape · named→positional at emit (`"{Threat}"` ⇒ `string.Format("{0}", …)`) ·
malformed ⇒ **a Stage 2 diagnostic naming the node, never a silent drop**.

⭐ **One parser, one pin-derivation function, one emit path, shared by both nodes. Do not write it twice.**
⭐ They compose with no new mechanism: a `Format String` result is a `FixedString`, which is a legal arg
type ⇒ **`Print String` needs no string-input special case.**
⚠ **No wildcard mechanism exists — each placeholder carries a declared `TypeId`. Do not invent one.**

---

## 3. ⚠ Raise these before/while building — I have NOT verified them

1. 🔴 **`Format String` is *pure*, so nothing guards its allocation.** `Print String` wraps
   `string.Format` in a level probe, so it costs nothing when the level is off. **`Format String` has no
   level to probe** — a pure node in a `Tick` graph would allocate a managed string *every tick, for
   every entity*, forever. The design note does not address this. Worth a
   `⏸ COORDINATOR DECISION NEEDED` row rather than silently shipping a per-tick allocation into a hot
   path. (Options: accept and document it; format straight into the `FixedString`'s bytes; or restrict
   where the node may appear.)
2. **Verify F2 against `BuiltInNodeRegistry` before relying on it.** The handoff explicitly invites this
   ("anything in F1–F6 that turns out wrong against the code — say it plainly"). The coordinator has
   been wrong three times and each time pushing back was correct.
3. ⚠ **F5 is a live trap:** `BuiltInNodeRegistry:194` ends `_ => Array.Empty<PinSchema>()`, so a kind
   missing from that switch gets **zero pins and no diagnostic** (trap #5's shape). The registry entry
   *is* the node existing.
4. **Silent truncation:** `FixedString64(string)` cuts in its constructor and Stage 2 cannot know a
   runtime length ⇒ **say so in the `Format String` tooltip.**
5. **Renaming a placeholder renames a pin, which can drop a link** — the same class BP-113 hit. The
   drawer should make the pin set visibly follow the text so it is never a surprise.

---

## 4. Gates + baseline

```bash
dotnet build IOS-IG-SimHost.sln -v q --nologo
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj --nologo --logger "console;verbosity=normal"
dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj --nologo --logger "console;verbosity=normal"
dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Hrot.BTree.Editor.Tests.csproj --nologo --logger "console;verbosity=normal"
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj --nologo --logger "console;verbosity=normal"
dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj --nologo --logger "console;verbosity=normal"
dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj --nologo --logger "console;verbosity=normal"
dotnet test Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/Hrot.AiEditor.Generators.Tests.csproj --nologo --logger "console;verbosity=normal"
```

**Baseline (coordinator, merged Batch-23 tree):** build **0 errors** · Blueprints **2925 / 0 / 10 skipped**
(2935 total) · AiShared **1213 / 0** · BTree **612 / 0** · Breakpoints **130 / 0** ·
NodeEditor.Core **208 / 0** · NodeEditor.UI **131 / 0** · Generators **193 / 0**.

⚠ ⚠ **Items 2 and 3 both touch `FDP/Engine/Fdp.Core`** — that rebuilds nearly everything and can break
suites far from blueprints. **Run all eight, plus `Fdp.Core.Tests` for item 2.**

⚠ **`-v q` prints counts but NOT the failing test's name** — always pass
`--logger "console;verbosity=normal"`. That is what BP-111 is half about, and it cost time twice.

### 🔴 BP-111 flake tax — it fired on me in Batch 23

**It is the whole wall-clock perf class, not the two names on the documented list.** In a full run
`WhenNode_EqsResult_Under150ns_perTick` *and* `ReadEqsResultNode_Under80ns_perInvocation` failed; both
then passed 3/3 in isolation, and a fourth isolated run failed a *different* member
(`WhenNode_ConditionMet_Under200ns_perTick`). **A different test failing on each run is timing, not
logic.** Classify by isolation or `git stash` → re-run → `git stash pop`; do not chase it.

---

## 5. What Batch 23 shipped (all merged + coordinator-verified)

| | |
|---|---|
| **BP-112** 🔴 | `MemoryMarshal.Write<T>` takes `in`; the emitter passed `ref` ⇒ CS9191 broke the **build** for every Library asset. ⭐ Durable half: `Assets/Blueprints/LibraryFunctionsDemo.bp.json`, the **first Library asset ever compiled by the real generator** — an `AdditionalFiles` entry, so a regression fails the build before any test runs |
| **BP-87** items 1–5 | Blueprint-local picker list projected from `StaticTypeRegistry.EditorOfferableTypeIds`; **AiShared untouched**. Coercion table is now C#'s full implicit ladder, 35 rungs, **widening only**. ⚠ **Batch 24 item 2 must add `FixedString128` to `EditorOfferableTypeIds` too**, or the picker will not offer it |
| **BP-113** 🟠 | `CallPeerBlueprint` projected only `Outputs[0]`. ⚠ **THREE** sites, not the two the handoff named — Stage5's lowering also collapsed to the first pin |

**Registered by me:** **BP-114** (item 1 of this batch), **BP-115** (no test covers a peer whose *name*
needs sanitizing).

### Lessons that keep paying

- ⚠ **The in-memory suites cannot catch a warnings-as-errors defect** — `CompileAndLoad` does not treat
  warnings as errors. That is why BP-112's fixture had to go into the **build**, not into a test.
  **Batch 24's F3 is the same shape**: a hot-reload-only defect the generator path would miss — which is
  exactly why the handoff insists the test cover the hot-reload path.
- ⚠ **Test-locked defects exist.** `CallPeerBlueprint_WithLookup_…_ProjectsTypedPins` asserted the
  one-pin-called-`Return` shape *that was the bug*. Updating such an assertion is correct — but do it
  deliberately and say so.
- **Every test that hands the compiler explicit pins bypasses `Stage0` entirely.** To cover Stage0, give
  the node **no** pins and address them via `DeterministicIds.PinId(nodeId, name, direction)`.
  *(Not needed in Batch 24 — F2 — but this is how you would prove it if F2 turns out wrong.)*

---

## 6. ⚠ Sub-agent hazards — these cost the most time across four batches

**One shared working tree. Run agents strictly sequentially:**
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```

1. 🔴 **An agent can DIE SILENTLY mid-edit**, leaving code that will not compile. **Detect it:** its
   transcript under `/tmp/claude-*/…/tasks/<id>.output` stops growing *and* no `dotnet` is running.
   **Do not read that file — it will blow up your context. Only `stat` its size.**
2. **Concurrent `dotnet build` corrupts obj/bin** — it once produced a phantom "missing method".
3. **Gate every commit on the fix being in the diff, never on the agent's report.** Reading the Batch-23
   agent's diff myself is how I found the `Stage0` coverage gap it could not have known to cover.
4. **Agents misreport git history** — three have claimed my commits were "harness automation". Do not let
   that reach a doc.
5. `codebase-memory-mcp` is **not connected** and cannot be connected mid-session. CLAUDE.md's fallback
   applies: Grep/Glob/Read, and say so.

---

## 7. Working agreement

- **Delegate to Sonnet** anything not needing Opus. **Never delegate verification** — gate runs and
  revert-to-red are yours.
- **Revert-goes-red on every fix.** ⚠ For item 3 the handoff names the required one:
  **move the helper's logger name outside `AI.Behavior*` and confirm the test fails.** A test that
  survives that is not testing the thing that matters.
- **Commit per item**; stop cleanly **between** items if running out of room — three finished beat four
  half-finished. *(That is why Batch 23 stopped at its item 3.)*
- **Fix, don't disable.** **Never widen** — register adjacent defects as rows.
- **Anything left behind gets a tracker ROW**, not a note inside a `DONE` block (BP-102's lesson).
- **Reconcile counts three ways**: checkbox tally, complexity-column sums, header total. ⚠ The checkbox
  tally runs **+1 open / +1 done** against the header — refuted **BP-46** and an abandoned
  *"Squad-quartet"* row. **That offset is permanent; do not "fix" it.**
- Report **actual per-suite numbers**, what went red under revert, and what was delegated.

---

## 8. Still open, and the largest risk

| | |
|---|---|
| **BP-107** 📐 | `Return.Status` is a compile-time constant ⇒ `Running` inexpressible. **Architect round required** |
| **BP-106** | An `AiPrimitive` graph's declared Outputs are silently dropped — the last silent case in that family |
| **BP-102** | Graph Signature window edits still not undoable |
| **BP-111** | The perf-flake tax above — cheap, and it taxes every session |
| **BP-115** | No test covers a peer whose name needs sanitizing |
| **BP-87** item 6 | `System.String` as a *variable* — ⚖️ D3, deliberately open |

🔴 **The T-series (T1–T7, [BP-73](Blueprint_Issues_Detail.md#bp-73)) is performable but STILL
UNPERFORMED** — unchanged for seven batches.

⭐ **And the pattern that keeps repeating: every defect in Batches 21–23 came from running the thing, not
from the suite.** Batch 21's three came from the user at the UI after Batch 20's gates were green and its
code reviewed clean; BP-110 came from the first test that ever executed the feature; BP-112 came from the
first Library asset ever put through the real generator. **A green suite has repeatedly failed to find
what one real execution found immediately.**
