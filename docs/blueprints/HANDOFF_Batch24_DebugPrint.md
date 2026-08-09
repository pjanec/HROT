# HANDOFF — Batch 24: **Print String** + **Format String** (BP-108), `FixedString128`, BP-114

> **Read in full. Self-contained.** You are an implementation session; a coordinator session owns the
> plan and reviews your diff.
>
> 🔄 **Runs in parallel with the user's Windows visual-test session.** Different machine, different
> checkout. Nothing you push blocks it; anything it finds lands in a **later** batch. **Do not widen
> this batch to chase incoming reports.**

---

## 0. ⚡ How to work

**You are on Opus. Delegate to Sonnet everything that does not need Opus-level reasoning** — tokens are
the binding constraint. Split stated per item.

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
| **Commit per item** | So a later failure does not lose earlier work |
| **No pull request** | Not in any batch so far |

📄 **Design: [PrintString_Node_Design.md](PrintString_Node_Design.md) rev 3 — read it before any code.**
📄 Decisions + what is still open: [Architect_Question_26_Print_Node.md](Architect_Question_26_Print_Node.md)

---

## 1. 🟢 Item 1 — BP-114 (quick, first)

📄 [detail](Blueprint_Issues_Detail.md#bp-114) · **RW-L**

`ParameterRowsView` picks the Type combo's index by **exact string match**. The list offers aliases
(`int`); most shipped assets store the FQN (`System.Int32`). Neither matches ⇒ fallback ⇒ the combo
**displays `bool` for an `int` parameter**.

⚠ **Mis-display only until touched — but "correcting" the wrong entry silently retypes the parameter for
real.** The user is mid-visual-check with this as hazard #1; landing it makes their next pass clean.

**Fix:** match on the **resolved** type (`IrTypeRef.FullName`) so `int` and `System.Int32` collapse to
one entry. `StaticTypeRegistry` is already reachable from the editor — you established that in BP-87
item 5. ⚠ Pre-existing, **not** a BP-87 regression.

**Delegation:** 🟢 **Sonnet**, entirely.

---

## 2. 🟢 Item 2 — `FixedString128`

⚠ **It does not exist.** `Fdp.Core` ships **32 and 64 only** (verified). The user wants 128 because 64
is too small for a formatted message, and **`Format String` (item 3) needs it.**

The struct itself is a **straight mirror of `FDP/Engine/Fdp.Core/FixedString64.cs`** — `Size = 128`,
`MaxLength = 127`, same `fixed byte` + UTF-8 encoder body.

⚠ **Wider than it sounds — ~10 production sites reference the family.** Mirror it through:

| | |
|---|---|
| `Fdp.Core/FixedString128.cs` | the struct |
| `Fdp.Core/Serialization/Converters/FixedStringConverters.cs` | JSON |
| `Fdp.Core/Serialization/FdpJsonOptionsRegistry.cs` | converter registration |
| `Fdp.Presentation/ImGui/Editing/FixedStringFieldEditors.cs` | the field editor |
| `Fdp.Presentation/ImGui/Utils/ComponentReflector.cs` | reflection over the family |
| `Hrot.Blueprints.Compiler/Compiler/Catalogs/StaticTypeRegistry.cs` | type table **+ `EditorOfferableTypeIds`** so the picker offers it |
| `Hrot.Blueprints.Compiler/Compiler/Stages/Stage3_Normalize.cs` | normalization |
| `Hrot.Blueprints.Editor/Host/BlueprintTypeSystem.cs`, `BlueprintPinModel.cs`, `BlueprintDocumentFactory.cs`, `Windows/VariableCreateModal.cs` | editor-side |

⚠ **`FDP/ExtDeps/GizmoMap` has its own separate `FixedString32` — leave it alone.** Different assembly,
different contract; it is not part of this family.

⚠ **`Fdp.Core.Tests/FixedStringTests.cs`** — mirror the 64 cases, including the **truncation** case.

**Delegation:** 🟢 **Sonnet**, entirely — this is mirror-an-existing-pattern work, which is exactly what
the delegation rule is for. 🔴 Opus reviews the diff and runs the gates.

---

## 3. 🔴 Item 3 — `Print String` + `Format String`

### ⚠ Three things rev 2/rev 3 overturned. Two change the work; one removes work.

| | |
|---|---|
| **F1 · "Optional arg pins" do not exist** | `Stage5.ResolveDataPin:2126-2159` emits **`BP4001` + `default(T)` for every unwired data-in.** ⇒ pins must be **derived**, never speculative |
| **F2 · ⭐ Trap #9 does NOT apply — this removes work** | `BuiltInNodeRegistry` is the single source: `NodePinSchema` delegates via `FromRegistry:226-228`, and Stage0 builds *"the canonical ordered pin list from **static registry shapes**"*, enriching **only** kinds whose pins depend on data **outside** the node. Both new nodes derive pins purely from their own properties ⇒ **register once. NO `Stage0_Rehydrate` case.** ⚠ The Batch-23 handoff told you to move two projections; **that was wrong for these nodes** |
| **F3 · The sink accessor is wrong again** | `BehaviorLog` lives in `Hrot.AI.Behaviors`, **not guaranteed loaded** when `MetadataReferenceResolver.ForRuntimeAssemblies` snapshots the AppDomain ⇒ `CS0246` on hot reload, unattributable — **[BP-62](Blueprint_Issues_Detail.md#bp-62)'s shape recurring.** ⇒ **build the helper in `Fdp.Core.Logging`** |

⚠ **F5 · `BuiltInNodeRegistry:194` ends `_ => Array.Empty<PinSchema>()`** — a kind missing from that
switch gets **zero pins and no diagnostic**. Trap #5's shape. The registry entry *is* the node existing.

### ⭐ Arity: pins are derived by **parsing the format string** — no `ArgCount`

**This overturns the coordinator's own earlier lean.** Unreal's `Format Text` creates *"an input
parameter for each `{}` delimiter found in the Format parameter"*, with **named** placeholders. That is
better than an `ArgCount` property on every axis, and it satisfies F1 more cleanly. **Adopted.**

```
[ Print String ]  exec In ─▶ ─▶ exec Out       [ Format String ]  (pure — no exec)
   Threat : float   ← derived                     Threat : float  ← derived
   Squad  : int     ← derived                     Result : FixedString64  ← declared
 props: Format, Level                           props: Format, ResultType
```

| | |
|---|---|
| Placeholder | `{Name}` — letters/digits/underscore. **First-appearance order** fixes pin order |
| Repeats | `{Name}` twice ⇒ **one** pin, used twice |
| Escape | `{{` / `}}` ⇒ literal brace |
| Emit | named → positional: `"{Threat}"` ⇒ `string.Format("{0}", …)` |
| Malformed | unclosed `{`, empty `{}`, bad name ⇒ **a Stage 2 diagnostic naming the node.** Never a silent drop — that is trap #5 |
| Arg types | declared per placeholder (`name → TypeId`), from `BlueprintTypeChoices`. **We have no wildcard mechanism; do not invent one** |

⭐ **The two nodes compose with no new mechanism:** a `Format String` result is a `FixedString`, and
`FixedString` is a legal arg type ⇒ wiring one into a `Print String` placeholder prints a computed
message. **Print String needs no string-input special case.**

⭐ **One parser, one pin-derivation function, one emit path**, shared by both nodes. They differ only in
what they do with the result. Do not write it twice.

### Sites

| Site | |
|---|---|
| `Assets/Nodes.cs` | `PrintStringNode`, `FormatStringNode` |
| `Compiler/Catalogs/BuiltInNodeRegistry.cs` | ⭐ **the pin shapes — the single source both projections read.** Mirror `ArrayMakePins:299`, but derive the **count** from the parsed format |
| `Compiler/Stages/Stage2_Validate.cs` | the malformed-format diagnostic |
| `Compiler/Stages/Stage5_Schedule.cs` | lowering |
| `Compiler/Emit/StatementEmitter.cs` | `string.Format` **inside** the level-probe guard (Print) / assigned to the result value (Format) |
| `FDP/Engine/Fdp.Core/Logging/` | the helper — `AI.Behavior` logger family, five levels, **and an overload without entity context** (**F4**: `HasSelfInScope` is false for Library dispatch, and Q26-**B1** allows Print there anyway) |
| `Editor/NodeDrawers/` + `BlueprintNodePaletteEntries.cs` | palette + drawers + detail-panel properties |
| ~~`Stage0_Rehydrate.cs`~~ | ⭐ **NOT needed — F2** |

⚠ **Truncation is silent** — `FixedString64(string)` cuts in its constructor, and Stage 2 cannot know a
runtime length. **Say so in the `Format String` tooltip.** It is the first thing that will confuse someone.

### The test is the deliverable

⭐ **Assert on a captured log line** — `AiBehaviorLogTarget.SharedInstance.GetMessages()` /
`OnMessageAdded`. Never on "the graph ticked".

⚠ **`Program.cs:124` registers the NLog rule and a headless test never runs it** ⇒ **the test registers
the rule itself.** Expected; *not* a reason to invent a sink abstraction.

⚠ **Cover the hot-reload path, not only `CompileAndLoad`** — **F3 is a defect that appears only there.**
A test that exercises just the generator path would miss the entire reason the accessor moved.

⭐ **Then, if there is room:** make `BP109_SmokeTestEndToEndTests` print through it, replacing the
`TryGetField<T>` assertions its own handoff called a fallback. **Optional; not near a stopping point.**

**Delegation:** 🔴 **Opus** — the format parser, the registry shapes, Stage5, emit, and the `Fdp.Core`
helper's layering. 🟢 **Sonnet** — the node models, palette entries, drawers + detail-panel UI, and the
test bodies once the shapes are fixed.

---

## 4. Gates

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

Known flake: the **wall-clock perf class** (BP-111); it did not fire on the coordinator's run.
⚠ Classify any failure with `git stash` → re-run → `git stash pop`.

⚠ ⚠ **Items 2 and 3 both touch `FDP/Engine/Fdp.Core`** — a change there rebuilds nearly everything and
can break suites far from blueprints. **Run all eight, and add `Fdp.Core.Tests` for item 2.**

---

## 5. Reporting back

1. **Per-suite gate numbers** you actually ran — not "gates green".
2. **What you reverted and confirmed went red**, per item. ⚠ For the nodes specifically: confirm the
   test fails when the **helper's logger name** is moved outside `AI.Behavior*` — that is the failure
   mode F3 is about, and a test that survives it is not testing the thing that matters.
3. **What you delegated to Sonnet, what you kept.**
4. ⭐ **Anything in rev 3's F1–F6 that turns out wrong against the code.** Say it plainly — the
   coordinator has been wrong to you three times and each time you were right to push back.
5. Any `⏸ COORDINATOR DECISION NEEDED` rows.

⚠ **Register what you leave behind as a tracker row, not a note inside a `DONE` block** (BP-102's lesson).

**Done =** gates green vs baseline · tracker rows `[x]` with `DONE` notes · counts reconciled three ways ·
committed per item · pushed to `claude/blueprint-macro-feature-sdmspn`. **No PR.**
