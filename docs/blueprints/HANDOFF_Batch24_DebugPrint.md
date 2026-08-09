# HANDOFF — Batch 24: the **Debug Print** node (BP-108) + BP-114

> **Read in full. Self-contained.** You are an implementation session; a coordinator session owns the
> plan and reviews your diff.
>
> 🔄 **Runs in parallel with the user's Windows visual-test session.** Different machine, different
> checkout. Nothing you push blocks it; anything it finds lands in a **later** batch. **Do not widen
> this batch to chase incoming reports.**

---

## 0. ⚡ How to work

**You are on Opus. Delegate to Sonnet everything that does not need Opus-level reasoning** — tokens are
the binding constraint. Split stated per item below.

⚠ **Sub-agents share ONE working tree.** Sequential only. Before the next agent:
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```
⚠ **Gate every commit on the fix being in the tree**, not on an agent reporting success.

| | |
|---|---|
| **Push to** | `claude/blueprint-macro-feature-sdmspn` — merge the coordinator branch first |
| **Do not push to** | `claude/blueprint-authoring-status-6sr5ld` |
| **New finding IDs** | **BP-116+** — the tracker/detail docs are **yours** for this batch |
| **Revert-goes-red** | Every fix, no exceptions, **never delegated** |
| **No pull request** | Not in any batch so far |

---

## 1. 🟢 Item 1 — BP-114 (quick, do it first)

📄 [detail](Blueprint_Issues_Detail.md#bp-114) · **RW-L**

`ParameterRowsView` picks the Type combo's selected index by **exact string match**. The list offers
aliases (`int`); most shipped assets store the canonical FQN (`System.Int32`). Neither matches, the
fallback fires, and the combo **displays `bool` for an `int` parameter**.

⚠ **Mis-display only until touched — but "correcting" the visibly-wrong entry silently retypes the
parameter for real.** The user is doing a visual check right now with this as hazard #1 on their guide;
landing the fix makes their *next* pass clean.

**Fix:** match on the **resolved** type (`IrTypeRef.FullName`) rather than the raw string, so `int` and
`System.Int32` collapse to one entry. `StaticTypeRegistry` is already reachable from the editor —
you established that yourself in BP-87 item 5.

⚠ Pre-existing, **not** introduced by BP-87 — BP-87 only made it visible by putting a correct list in
front of it. Do not record it as a regression.

**Delegation:** 🟢 **Sonnet**, entirely.

---

## 2. 🔴 Item 2 — BP-108, the Debug Print node

📄 **Design: [PrintString_Node_Design.md](PrintString_Node_Design.md) rev 2 — read it first.**
📄 Open architect questions: [Architect_Question_26_Print_Node.md](Architect_Question_26_Print_Node.md)

### ⚠ rev 2 overturned three things rev 1 and the Batch-23 handoff told you

**Read these before writing any code — two of them change the work, one removes work.**

| | |
|---|---|
| **F1 · "Optional arg pins" do not exist** | `Stage5.ResolveDataPin:2126-2159` emits **`BP4001` + a `default(T)` statement for every unwired data-in.** Three fixed pins ⇒ **two warnings on every one-argument Print.** ⇒ **arity is author-declared (`ArgCount`), not "optional pins"** |
| **F2 · ⭐ Trap #9 does NOT apply here — this removes work** | `BuiltInNodeRegistry` is the single source. `NodePinSchema` delegates via `FromRegistry:226-228`; Stage0 builds *"the canonical ordered pin list from **static registry shapes**"* and its switch **enriches only kinds whose pins depend on data OUTSIDE the node**. Print's pins depend solely on its own properties ⇒ **register once. NO `Stage0_Rehydrate` case.** ⚠ The Batch-23 handoff and rev 1 both told you to move two projections; **that was wrong for this node** |
| **F3 · The sink accessor is wrong again** | `BehaviorLog` lives in `Hrot.AI.Behaviors`, which is **not guaranteed loaded** when `MetadataReferenceResolver.ForRuntimeAssemblies` snapshots the AppDomain ⇒ `CS0246` on the in-memory / hot-reload path, unattributable — **[BP-62](Blueprint_Issues_Detail.md#bp-62)'s shape recurring.** ⇒ **build the helper in `Fdp.Core.Logging`**, beside the sink |

⚠ **F5 · `BuiltInNodeRegistry:194` ends `_ => Array.Empty<PinSchema>()`** — a kind missing from that
switch gets **zero pins and no diagnostic**. Trap #5's shape. The registry entry *is* the node existing.

### The shape

```
exec In ──▶ [ Debug Print ] ──▶ exec Out
            Arg0 : <declared TypeId>     ← exactly ArgCount pins
            Arg1 : <declared TypeId>
 properties: Format : literal   Level : Trace|Debug|Info|Warn|Error
             ArgCount : 0..4    ArgTypes : TypeId per arg
```

### ⭐ Build on these leans now — every one is the **reversible** direction

The architect has **not** answered Q26. You are not blocked, because each lean can be revised without
undoing work:

| Q | Lean to build | Why it is safe to start |
|---|---|---|
| **A** | Helper in **`Fdp.Core.Logging`** | If overruled, it is a ~30-line type to move |
| **B** | **Allow** Print in a Library function, no entity context | Forbidding it later is a Stage 2 validator **added**; building the ban first and being overruled means **removing** a diagnostic |
| **C** | **Literal format only**, no format pin | Adding an optional pin later is additive; a pin-only design would have to be walked back |
| **D** | **`ArgCount` 0..4** | The cap is one constant |
| **E** | `DebugPrintNode`, palette **"Debug Print"** | Rename is mechanical |

⚠ **If an answer arrives mid-batch, the coordinator will relay it. Do not guess at Q26 yourself, and do
not widen beyond these leans** — a variadic pin mechanism, a `Format`/`Concat` node, or string coercion
all need the architect (that is exactly what Q26 exists to gate).

### Sites to touch

| Site | |
|---|---|
| `Assets/Nodes.cs` | `DebugPrintNode : Node` |
| `Compiler/Catalogs/BuiltInNodeRegistry.cs` | ⭐ **the pin shape — the single source both projections read.** Mirror `ArrayMakePins:299`, but drive the **count** from `ArgCount` |
| `Compiler/Stages/Stage5_Schedule.cs` | lower to the log call |
| `Compiler/Emit/StatementEmitter.cs` | emit `string.Format` **inside** a level-probe guard |
| `FDP/Engine/Fdp.Core/Logging/` | the new helper — logger name in the **`AI.Behavior`** family, five levels, an overload **without** entity context (**F4**: `HasSelfInScope` is false for Library dispatch) |
| `Editor/NodeDrawers/BlueprintNodePaletteEntries.cs` + a drawer | palette entry, detail-panel properties |
| ~~`Stage0_Rehydrate.cs`~~ | ⭐ **NOT needed — F2** |

### The test is the deliverable

⭐ **Assert on a captured log line** — `AiBehaviorLogTarget.SharedInstance.GetMessages()` or
`OnMessageAdded`. Never on "the graph ticked".

⚠ **`Program.cs:124` registers the NLog rule and a headless test never runs it** ⇒ **the test adds the
rule itself.** That is expected and is *not* a reason to invent a sink abstraction.

⚠ **Cover the hot-reload path too**, not only `CompileAndLoad` — **F3 is precisely a defect that only
appears there.** A test that only exercises the generator path would miss the whole reason the accessor
moved.

⭐ **Then, if there is room:** make `BP109_SmokeTestEndToEndTests` print through it. That test asserts
via `TryGetField<T>` — the fallback its own handoff anticipated. Making it read like the scenario it
verifies is the payoff for building this node. **Optional; do not start it near a stopping point.**

**Delegation:** 🔴 **Opus** — the registry shape, Stage5 lowering, emit, and the `Fdp.Core` helper's
layering. 🟢 **Sonnet** — the node model, palette entry, drawer + detail-panel UI (mirror an existing
property node), and the test body once the shape is fixed.

---

## 3. Gates

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

**Baseline — coordinator-measured on the merged Batch-23 tree:** build **0 errors** ·
Blueprints **2925 / 0 / 10 skipped** (2935 total) · AiShared **1213 / 0** · BTree **612 / 0** ·
Breakpoints **130 / 0** · NodeEdit Core **208 / 0** · UI **131 / 0** · Generators **193 / 0**.

Known flake: the **wall-clock perf class** (BP-111) — it did not fire on the coordinator's run.
⚠ Classify any failure with `git stash` → re-run → `git stash pop`.

⚠ ⚠ **You are touching `FDP/Engine/Fdp.Core`** — a change there rebuilds nearly everything and can break
suites far from blueprints. **Run the full eight, not just the blueprint ones.**

---

## 4. Reporting back

1. **Per-suite gate numbers** you actually ran — not "gates green".
2. **What you reverted and confirmed went red**, per item. ⚠ For the Print node specifically: confirm
   the test fails when the **helper's logger name** is changed to one outside `AI.Behavior*` — that is
   the failure mode F3 is about, and a test that survives it is not testing the thing that matters.
3. **What you delegated to Sonnet, what you kept.**
4. ⭐ **Anything in rev 2's F1–F6 that turns out wrong against the code.** Say it plainly — the
   coordinator has been wrong to you three times now and each time you were right to push back.
5. Any `⏸ COORDINATOR DECISION NEEDED` rows you left.

⚠ **Register what you leave behind as a tracker row, not a note inside a `DONE` block** (BP-102's lesson).

**Done =** gates green vs baseline · tracker rows `[x]` with `DONE` notes · counts reconciled three ways ·
committed per item · pushed to `claude/blueprint-macro-feature-sdmspn`. **No PR.**
