# HANDOFF — Batch 23: **overnight, autonomous**. Types, cross-asset outputs, Print node

> **Read in full. Self-contained.** You are an implementation session. A coordinator session owns the
> plan and reviews your diff.
>
> 🌙 **THE USER IS ASLEEP. Nobody will answer a question tonight.** Everything below is scoped so you
> never need to ask. Where a judgement call could have blocked you, **the coordinator has already made
> it** and marked it ⚖️. Those are decisions, not suggestions — but if one turns out to be *wrong
> against the code*, the code wins: do the right thing and say so in your report.

---

## 0. ⚡ Read first — how to work tonight

### Model delegation

You are on **Opus**. **Delegate to Sonnet sub-agents everything that does not need Opus-level
reasoning.** Tokens are the binding constraint and this is a long batch.

⚠ **Sub-agents share ONE working tree.** Run them **sequentially, never concurrently** — two
`dotnet build`s in the same tree corrupt each other's obj/bin. Before starting the next agent:

```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```

⚠ **Gate every commit on the fix being in the tree**, not on an agent reporting success. Read the diff.

### The autonomous rules

| Rule | |
|---|---|
| **Never block** | If something is genuinely undecidable, register a tracker row, add a `⏸ COORDINATOR DECISION NEEDED` note with the two options and your lean, and **move to the next item**. Do not stop the batch. |
| **Commit per item** | Each item is independently committable. Commit + gate after each, so item 4 failing does not lose items 1–3. |
| **Stop cleanly at a boundary** | Three finished items beat four half-finished ones. If you are running out of room, stop **between** items and say where you stopped. |
| **Never widen** | Do not chase adjacent defects. Register them as rows. |
| **Revert-goes-red** | Every fix: revert it, confirm the new test fails, restore. For a new test: break what it covers. **No exceptions, and do not delegate this.** |
| **ID allocation is yours** | The tracker and detail docs are **yours** for this batch. New findings take **BP-114+**. The coordinator will not touch those files and will allocate from BP-200+ if it must. *(This rule exists because Batch 22 collided on BP-110/111 — the coordinator's fault, now fixed.)* |
| **No pull request** | Not in any batch so far. |

### Branches

| | |
|---|---|
| **Implementation** | `claude/blueprint-macro-feature-sdmspn` — **push here**; merge the coordinator branch first |
| **Coordinator** | `claude/blueprint-authoring-status-6sr5ld` — do **not** push here |
| **Truth** | [Tracker](Blueprint_Issues_Tracker.md) · [Detail](Blueprint_Issues_Detail.md) |

---

## 1. 🎉 Batch 22 verified — your BP-110 fix is good

The coordinator reviewed `e214c4dc` and re-ran the gates on the merged tree. **Solution build: 0
errors.** The `ResolveSiblingClassName` approach is right, and the reasoning in the commit message for
preferring name resolution over a `using` alias (production emits global, the test fixture wraps in a
namespace, so no single alias form is correct for both) is correct.

✅ **Verified the part that could have silently broken it:** both production producers of a
`BlueprintSignature` derive `SanitizedName` through `Sanitizer.SanitizeName` —
`BlueprintSignatureParser.cs:39` and `BlueprintSignatureBuilder.cs:19` — the *same* function
`Stage5_Schedule.cs:58` uses for the asset's own name. **The two names cannot drift.** That was the one
way the fix could have been subtly wrong.

⚠ **One latent trap you left, and it is small — item 5 below.** Several tests construct signatures with
`SanitizedName: peer.Name` (raw, unsanitized): `CallPeerBlueprintRoslynTests.cs:171`,
`BP109_SmokeTestEndToEndTests.cs:85`, `RecipeIntegrityTests.cs:64`, `NodeCoverageTests.cs:534`. It works
only because every fixture name is already sanitizer-clean. It fails **loudly** (CS0103), not silently,
so it is low-risk — but it means no test covers a peer whose name needs sanitizing.

---

## 2. ⚖️ Coordinator decisions made for you — so you never have to ask

Read this section before starting. Each of these would otherwise have blocked you at 3 a.m.

| # | Question that would have blocked you | ⚖️ Decision |
|---|---|---|
| **D1** | BP-113 says it *"pairs naturally with BP-95 (one call node for local and peer functions) — worth checking whether unifying makes this fix free"*. | **Do NOT unify tonight.** Node unification is an architectural change and needs an architect round. **Fix BP-113 in place**, mirroring what BP-73 did for the same-asset `FunctionCall`. If you find unification would genuinely be less work, **write that in the report and still do the in-place fix** — the coordinator will weigh it. |
| **D2** | BP-87 item 1 says "add `FixedString32/64` to the dropdown". Which dropdown? | ⚠ **NOT the shared one.** See §4 — this is the single most important correction in this handoff. |
| **D3** | BP-87 item 6, `System.String` as a *variable* (not a parameter), "needs a deliberate call". | **Out of scope tonight. Leave the row open.** It is a real semantic decision (managed type in a `State` struct) and it is not blocking anything. |
| **D4** | BP-108 Print node: variadic vs fixed-arity args? | **Fixed arity.** See §5 for the full pre-approved shape. Do not invent a variadic pin mechanism. |
| **D5** | BP-108 log sink: invent an `IBlueprintLogSink`? | **No — one already exists.** See §5. |
| **D6** | Anything that would need the architect (a new node *vocabulary*, a new pin *mechanism*, a dispatch/graph-kind change). | **Stop, register a row, move on.** Nothing in this batch should need it — that is deliberate. |

---

## 3. 🔴 Item 1 — **BP-112**: CS9191 breaks the full build for every Library asset

📄 [detail](Blueprint_Issues_Detail.md#bp-112) · **RW-L** · ⭐ **Do this first — it unblocks the user.**

A freshly created, otherwise-empty `Function Library` hot-reloads fine but **fails the full build**:

```
FuncLib1_632F0EA6_Bp.g.cs(45,93): error CS9191: The 'ref' modifier for argument 2
corresponding to 'in' parameter is equivalent to 'in'. Consider using 'in' instead.
```

*(It is a warning that becomes an error — `Hrot.Blueprints.Compiler.csproj` sets
`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, and so do its siblings.)*

### Two halves. **The second is the one that matters.**

1. Emit `in` (or drop the modifier) at the adapter call site.
2. ⭐ **Add a `Dispatch: Library` fixture to `Hrot.AiEditor.Generators.Tests`.**

⚠ **Why half 2 is the real deliverable.** The failure is in the **Roslyn source-generator path**
(`BlueprintIncrementalGenerator`), which neither `BlueprintCompiler.Compile` nor
`BlueprintTestFixture.CompileAndLoad` runs. That generator **is** gated — it is one of the eight suites
— but **every fixture in it is an `AiPrimitive` HillAssault2 asset. Not one is `Dispatch: Library`.**
So no Library asset has ever gone through the generator at all.

⇒ This is the **fourth** "both halves tested, seam never crossed" instance in four batches (BP-104,
BP-109, BP-110, this). This one is the sharpest: **the gate existed and simply had no fixture of that
shape.** Fixing only half 1 leaves the next Library-only codegen defect equally invisible.

### 🔍 Reproduce it first

The user hit this on a Library with **no peer call**, so it is very probably distinct from BP-110 — but
your emitter changes touched this surface, so **confirm it still reproduces before fixing it.** If
BP-110 incidentally fixed it, say so and tick the row; do not invent a fix for a live bug that is not.

**Delegation:** 🟢 Sonnet for the fixture + the mechanical emit change once you have located the site.
🔴 Opus to locate the emit site and decide `in` vs no-modifier.

---

## 4. 🔴 Item 2 — **BP-87**: the type picker offers 8 types the compiler cannot resolve

📄 [detail](Blueprint_Issues_Detail.md#bp-87) · **RW-M** · **the architect question is already closed** —
the user settled scope: *register and support, do not remove.*

| Offered by the editor | Resolves? |
|---|---|
| `bool` `byte` `short` `int` `long` `float` `double` | ✅ |
| `sbyte` `ushort` `uint` `ulong` | ❌ no entry under either name |
| `Vector2` `Vector3` `Vector4` `Quaternion` | ❌ registered as FQN only; bare alias unmapped |

### ⚠ D2 — the coordinator's correction. **Read this before touching anything.**

The detail entry says "add to the dropdown" and "derive the dropdown from the registry". **It does not
say where that list lives, and the obvious place is the wrong one.**

`BlackboardTypeHelper.DefaultKnownTypeNames` (`Hrot/Editor/Hrot.Editor.AiShared/Blackboard/`) is
**shared by three editors**:

| Consumer | |
|---|---|
| `Hrot.Blueprints.Editor/Windows/ParameterRowsView.cs:61,93` | blueprints |
| `Hrot.BTree.Editor/Persistence/BehaviorTreeAssetMapper.cs:453` | behaviour trees |
| `Hrot.Hsm.Editor/Persistence/HsmAssetMapper.cs:473` | HSM |
| `Hrot.Editor.AiShared/Blackboard/BlackboardTypeChoiceBuilder.cs:46` | the shared Add-Variable dropdown |

⇒ **Widening that array changes the BTree and HSM blackboard pickers too**, and puts you into the
1213-test AiShared gate for a blueprint-only problem.

### ⚖️ Do this instead

**Add a blueprint-local type list in `Hrot.Blueprints.Editor`** and point `ParameterRowsView` at it.
Leave `Hrot.Editor.AiShared` untouched.

✅ **Verified this is clean:** the consumer (`ParameterRowsView`) is *already* blueprint-local; only the
*list* is shared. And `Hrot.Blueprints.Editor` → `Hrot.Blueprints.Core` → `Hrot.Blueprints.Compiler`, so
**`StaticTypeRegistry` is reachable from the editor**. That means the durable fix (item 5 below) is
feasible blueprint-locally — you do **not** need a new abstraction, and AiShared never learns about the
blueprint compiler.

### The work, in order

| # | Item | Note |
|---|---|---|
| 1 | Add `Fdp.Core.FixedString32` / `FixedString64` to the **blueprint-local** list | Already registered in `StaticTypeRegistry:62-63`. **The one the user actually asked for** |
| 2 | Map the bare vector aliases → FQNs | `Vector2/3/4`, `Quaternion`. Registered at `StaticTypeRegistry:38-42` under FQN only |
| 3 | Register `sbyte`/`ushort`/`uint`/`ulong` in the alias table | Mechanical |
| 4 | ⭐ **Add unsigned coercion entries** | **The gate on item 3** |
| 5 | ⭐ Derive the blueprint list from `StaticTypeRegistry` | The durable fix; kills the two-list drift for good |
| 6 | `System.String` as a *variable* | ⚖️ **D3 — out of scope. Leave the row open.** |

### ⚠ Item 4 is not optional, and here is why

The user's condition for keeping the unsigned types was explicit:

> *"Uint/ushort is not an issue **as long as it can be seamlessly converted to ints** (wiring possible
> between uint ↔ ushort ↔ int pins)."*

`CoercionTable:96-103` has exactly **8 entries and every one is signed**:

```
Byte→Int32, Byte→Single, Int16→Int32, Int16→Single,
Int32→Int64, Int32→Single, Int32→Double, Single→Double
```

**No `UInt16→Int32`, no `UInt32→Int64` — nothing unsigned at all.** Doing item 3 without item 4 produces
types that *resolve* but *cannot be wired* — **a worse failure than BP1500**, because it fails later and
less legibly. ⚠ **If you can only finish part of BP-87, ship items 1+2+5 and leave 3 out**, rather than
shipping 3 without 4.

💡 Widening coercions is where the round-out rule applies: add the **full** unsigned ladder
(`Byte→UInt16/UInt32/UInt64`, `UInt16→Int32/UInt32/…`, and the unsigned→float/double rungs), not only
the two the user named. Follow C#'s own implicit-conversion table — it is the obvious correct set and it
is already the shape the signed half follows. ⚠ **Widening-only** — do not add a lossy rung
(`Int32→UInt32`, `Int64→Int32`) that C# itself requires a cast for.

**Delegation:** 🟢 Sonnet for items 1–3 (table edits, from the exact shape above) and for the coercion
rows once you have fixed the pattern. 🔴 Opus for item 5 and for deciding the coercion set's boundary.

---

## 5. 🟠 Item 3 — **BP-113**: `CallPeerBlueprint` shows only `Outputs[0]`

📄 [detail](Blueprint_Issues_Detail.md#bp-113) · **RW-M** · user-reproduced

> User: *"I tried CallPeerBlueprint, selected FuncLib1 → NewFunction, but it keeps showing just a
> single output data pin — the first returned value."*

`NodePinSchema.CallPeerBlueprintPins:552-556` — the comment says it outright:

```csharp
// Data-OUT: Return pin typed from Outputs[0] (or System.Object when no outputs).
var returnTypeId = funcSig.Outputs.Count > 0 ? funcSig.Outputs[0].TypeId : "System.Object";
pins.Add(MakeData("Return", "Out", returnTypeId));
```

⚠ **BP-73 gave N outputs to the same-asset `FunctionCall` and never touched the cross-asset node.** So
N-output functions work locally and are **unreachable from another blueprint** — which is the entire
point of a Function Library. Two halves of one feature, built in different batches, never met.

### Before you build

- ⚠ **Both projections must move together** — `NodePinSchema` **and** `Stage0_Rehydrate`. That is
  trap #9's home and it is exactly how this class of defect keeps shipping.
- ⚠ **Confirm `BlueprintSignature.ExportedFunctions` actually carries N outputs first.** The shape is
  there (`BlueprintFunctionSig(Name, Inputs, Outputs)`) — but check the **parser** populates more than
  one, or you will fix the editor half against a signature that cannot express the answer.
- ⚖️ **D1: do not unify with `FunctionCall` tonight.** Mirror BP-73's projection in place.
- ⭐ **This is now downstream of your own BP-110 fix** — a peer call can finally execute, so for the
  first time you can write a test that asserts **two different values** come back across an asset
  boundary. Do that. A pin-count assertion alone repeats the mistake that let this ship.

**Delegation:** 🔴 Opus for the two projections agreeing (trap #9). 🟢 Sonnet for the test once the shape
is fixed.

---

## 6. 🟠 Item 4 — **BP-108**: the Print/Log node

📄 [detail](Blueprint_Issues_Detail.md#bp-108) · **RW-M**

> User: *"we are still missing a node allowing us to write a formatted string to log or console, which
> could be testable. That one is really useful and should be added."*

Unreal's **Print String** is the first node most designers reach for. Without it there is no way to see
what a graph is doing short of attaching a debugger.

### ⚖️ D4 + D5 — the shape is pre-approved. Do not redesign it.

The repo rule is that a non-trivial node gets a design note + an architect pass. **The architect is
unreachable tonight.** The coordinator has therefore pre-approved a deliberately **conservative** shape
whose every piece **reuses existing machinery** — that is what makes it defensible without the architect.

| Aspect | ⚖️ Decision | Grounding |
|---|---|---|
| **Sink** | ~~**NLog via `FdpLog<T>`**~~ ⛔ **WRONG — see the correction box below. Use `Hrot.AI.Behaviors.Logging.BehaviorLog`.** The *sink* (`AiBehaviorLogTarget`, `AI.Behavior*`) is right; only this accessor was wrong | ⭐ `FDP/Engine/Fdp.Core/Logging/AiBehaviorLogTarget.cs` already exists: an NLog `Target` **and** an `IMessageLogSource`, with a **shared singleton** and an `OnMessageAdded` event, already wired to the editor's **"AI Behaviors" MessageLog tab**. **Do not invent `IBlueprintLogSink`.** |
| **Test interception** | Read `AiBehaviorLogTarget.SharedInstance` entries (or subscribe `OnMessageAdded`) | The detail entry's design point 1 — *"the log sink must be interceptable"* — **is already satisfied by shipped code.** ⚠ Verify the target is registered under a headless test run; if not, **the test registers it**. That is fine and is *not* a reason to invent a new sink. |
| **Format** | A **literal string property on the node**, not a pin | Sidesteps the managed-`System.String` problem (BP1503) entirely: a literal is fine, a string *variable* is not. Matches Unreal's Print String, whose `In String` is a literal in practice. |
| **Arity** | ⭐ **Fixed: `Arg0..Arg2`**, optional data-in pins. **No variadic/wildcard mechanism.** | No node in the repo has a variadic pin shape, and inventing one is exactly the "new pin mechanism" that would need the architect (⚖️ D6). |
| **Arg typing** | Per-pin declared `TypeId`, chosen in the detail panel | Feeds off item 2's blueprint-local type list — **the two items converge; do BP-87 first.** |
| **Composite** | `string.Format`-style `{0} {1} {2}` | Standard, and it degrades sanely when an arg is unwired. |
| **Verbosity** | ⭐ **All five levels** — Trace/Debug/Info/Warn/Error — as a node property | The **round-out rule**: an enum-keyed node ships the whole enum, not just the one value the task needs. |
| **Hot path** | Guard with `FdpLog<T>.Is{Level}Enabled` before formatting | `FdpLog.cs` exposes these flags **precisely for this** — its own doc comment says to use them to avoid allocating interpolated strings. |

> ### ⛔ D5 correction — verified 2026-08-09. **The handoff named the wrong accessor.**
> `FdpLog<T>` derives its logger name from `typeof(T).FullName` (`FdpLog.cs:15`). Generated blueprint
> classes emit into `namespace Hrot.AI.Behaviors.Generated`, and the NLog rule
> (`Hrot.ClusterRunner/Program.cs:124`) is the prefix-anchored `"AI.Behavior*"` — which
> `Hrot.AI.Behaviors.…` **does not match**. Printing through `FdpLog<T>` would compile, reach the
> rolling file via the catch-all rule, and **never reach the AI Behaviors tab or the test.**
> ⇒ **Use `Hrot.AI.Behaviors.Logging.BehaviorLog`** — `GetLogger("AI.Behavior")`, an exact family hit,
> and it carries `Entity:[…] Behavior:[…] Node:[…]` context for free. It lacks an `Info` tier; the
> five-level round-out adds one mirroring the existing four.
> **The sink decision (D5) stands. Only the accessor was wrong.** Full reasoning:
> [PrintString_Node_Design.md](PrintString_Node_Design.md) §3.

### Deliverables

1. `docs/blueprints/PrintString_Node_Design.md` — ✅ **already written in Batch 23.** Record the table above, **plus anything you had to
   change and why**. Mark it *coordinator-decided; architect to confirm before any widening.*
2. The node itself, both pin projections in agreement (`NodePinSchema` **and** `Stage0_Rehydrate`).
3. A test that **asserts on a captured log line**, not that the graph ticked.

⭐ **Then, if there is room:** make `BP109_SmokeTestEndToEndTests` print through it. That test currently
asserts via `TryGetField<T>` — the fallback its own handoff anticipated. Making it read like the
scenario it verifies is the payoff for building this node at all. **Optional; do not start it if you
are near a stopping point.**

**Delegation:** 🔴 Opus for the design note, the two projections, and the emit. 🟢 Sonnet for the drawer,
the detail-panel property UI (mirror an existing typed-property node), and the test.

---

## 7. Gates

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

⚠ **Add `--logger "console;verbosity=normal"`.** `-v q` prints counts but **not the failing test's
name** — that is exactly why you had to register BP-111.

**Baseline — measured by the coordinator on the merged Batch-22 tree** (§8 of the coordinator's RESUME
carries the final numbers; solution build was **0 errors**).
Known flakes: `PdbEmbeddedSourceTests`, `WhenNodePerfTests.WhenNode_ValueChanged_Under100ns_perTick`.

⚠ Classify any failure with `git stash` → re-run → `git stash pop`.

⚠ **Do not commit scratch `.bp.json` under `Assets/Blueprints`** — those are the generator's
`AdditionalFiles`, so a malformed one **breaks the solution build for everyone who pulls**. That is
BP-103's lesson. `Recipes/Blueprints` is `Content` and cannot; you established that yourself in Batch 22
and you were right to correct the coordinator on it.

---

## 8. Reporting back

Write the report into the tracker/detail docs **and** state plainly in your final message:

1. **The gate numbers you actually ran** — per suite, not "gates green".
2. **What you reverted and confirmed went red**, per item.
3. **Which items you finished and where you stopped.** If you stopped mid-batch, say so at the item
   boundary — that is the expected outcome, not a failure.
4. **What you delegated to Sonnet and what you kept on Opus.**
5. ⭐ **Did BP-112 still reproduce**, or had your BP-110 emitter change already fixed it?
6. ⭐ **Anything where a ⚖️ decision in §2 turned out wrong against the code.** Say it plainly. The
   coordinator has been wrong to you twice already this programme and both times you were right to push
   back.
7. Any `⏸ COORDINATOR DECISION NEEDED` rows you left.

⚠ **Register what you leave behind as a tracker row, not a note inside a `DONE` block** — that is the
BP-102 lesson.

**Definition of done for each item:** gates green vs baseline · tracker row `[x]` with a `DONE` note ·
counts reconciled **three ways** · committed separately · pushed to
`claude/blueprint-macro-feature-sdmspn`.

⚠ **Do not create a pull request.**
