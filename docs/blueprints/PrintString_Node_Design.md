# Debug Print node — design note · **rev 2**

> **rev 1** (Batch 23) recorded the coordinator's ⚖️ D4/D5 decisions plus the implementation session's
> correction to the log accessor. **rev 2 (2026-08-09) is a full re-grounding against the code.**
> Six things were verified; **three of them break rev 1**, including a claim the coordinator put in the
> Batch-23 handoff without checking.
>
> ⚠ **Architect status:** the novel parts are collected in
> [Architect_Question_26_Print_Node.md](Architect_Question_26_Print_Node.md). Everything marked ✅ below
> is grounded in shipped prior art and can be built now.

---

## 1. Why the node exists

> User: *"we are still missing a node allowing us to write a formatted string to log or console, which
> could be testable. That one is really useful and should be added."*

Unreal's **Print String** is the first node most designers reach for. Today a blueprint author has no
way to see what a graph is doing short of attaching a debugger — BP-109's smoke test had to assert
through `BlueprintStateView.TryGetField<T>` for exactly this reason.

---

## 2. ⭐ What verification changed

| # | rev 1 said | The code says | Impact |
|---|---|---|---|
| **F1** | `Arg0..Arg2`, *"optional data-in pins"* | **There is no such thing as an optional data-in pin.** `Stage5.ResolveDataPin:2126-2159` emits **`BP4001` warning + a `default(T)` statement** for every unwired data-in | 🔴 **Breaks the shape.** A 1-arg Print would raise **2 warnings, every time**, on the most-used node in the graph |
| **F2** | *"Both pin projections must move together — `NodePinSchema` **and** `Stage0_Rehydrate`. Trap #9's home."* | **False for this node.** `BuiltInNodeRegistry` is the single source: `NodePinSchema` delegates to it via `FromRegistry:226-228` (*"single source of truth"*), and Stage0 builds *"the canonical ordered pin list from **static registry shapes** + dynamic asset state"* — its switch **enriches only kinds whose pins depend on data OUTSIDE the node** | 🟢 **Simplifies.** Print's pins depend only on its own properties ⇒ **register once, no Stage0 case, no second projection.** Trap #9 becomes structurally impossible |
| **F3** | Sink accessor `BehaviorLog` (`Hrot.AI.Behaviors.Logging`) | **Wrong assembly.** `MetadataReferenceResolver.ForRuntimeAssemblies` references only assemblies **already loaded** in the AppDomain. `Hrot.AI.Behaviors` is **not** in the emitted usings and is not guaranteed loaded on the in-memory / hot-reload path | 🔴 **`CS0246`, unattributable — this is [BP-62](Blueprint_Issues_Detail.md#bp-62)'s shape recurring (trap #3)** |
| **F4** | — *(not considered)* | `EmissionContext.HasSelfInScope` is **false for `Dispatch: Library`** — no `self`/`view` local exists in the generated stateless static method | 🟠 **A Print inside a library function cannot log entity context.** Needs a stated answer |
| **F5** | — | `BuiltInNodeRegistry:194` ends `_ => Array.Empty<PinSchema>()` | ⚠ **Trap #5's shape.** A node kind not added there gets **zero pins, silently** |
| **F6** | — | `ArrayMakePins:299-307` drives pin **types** from a node property (`ElementTypeId`) but **hardcodes two element pins** | 🟡 Property-driven pin **count** is genuinely new. Small, but it is the one novel mechanism ⇒ architect |

---

## 3. The revised shape

```
exec In  ──▶ [ Debug Print ] ──▶ exec Out
             Arg0 : <declared TypeId>     ← exactly ArgCount pins, no more
             Arg1 : <declared TypeId>
  properties:
     Format   : string literal      "threat={0} squad={1}"
     Level    : Trace|Debug|Info|Warn|Error
     ArgCount : 0..3                ← declared by the author; drives the pin count
     ArgTypes : TypeId per arg      ← from BlueprintTypeChoices (shipped in Batch 23)
```

| Aspect | Decision | Grounding |
|---|---|---|
| **Arity** | ⭐ **Author-declared `ArgCount`**, not "optional pins" | **F1.** Only declared pins exist ⇒ **zero spurious BP4001**, and `BP4001` keeps meaning *"you forgot to wire something"* |
| **Pin projection** | ⭐ **`BuiltInNodeRegistry` only. No `Stage0_Rehydrate` case.** | **F2.** One source ⇒ the two projections cannot diverge |
| **Arg typing** | Per-arg `TypeId` from `BlueprintTypeChoices` | Mirrors `ArrayMakeNode.ElementTypeId`; the picker shipped in Batch 23 (BP-87) |
| **Format** | String **literal property**, not a pin | Dodges managed `System.String` in a `State` struct (BP1503). ⚠ **But see Q26-C — BP-87 just shipped `FixedString32/64`, which are *unmanaged*, so this premise is now weaker than when it was decided** |
| **Composite** | `string.Format`-style `{0} {1}` | Standard; degrades sanely |
| **Level** | **All five** — Trace/Debug/Info/Warn/Error | Round-out rule: an enum-keyed node ships the whole enum |
| **Hot path** | Probe the level **before** formatting | Skips boxing the `object` args when the level is off |

---

## 4. The sink — decision stands, **accessor changes again**

✅ **Unchanged and correct:** `FDP/Engine/Fdp.Core/Logging/AiBehaviorLogTarget.cs` is both an NLog
`Target` and an `IMessageLogSource`, with a `SharedInstance`, `GetMessages()` and `OnMessageAdded`,
already wired to the editor's **"AI Behaviors"** tab by `Hrot.ClusterRunner/Program.cs:124`:

```csharp
logConfig.AddRule(LogLevel.Debug, LogLevel.Fatal, AiBehaviorLogTarget.SharedInstance, "AI.Behavior*");
```

**Do not invent an `IBlueprintLogSink`.** That part of D5 was right and remains right.

### ⛔ Two accessors have now been ruled out

| Accessor | Why not |
|---|---|
| `FdpLog<T>` *(the Batch-23 handoff's D5)* | Derives its name from `typeof(T).FullName`; generated classes emit into `namespace Hrot.AI.Behaviors.Generated`, which the prefix-anchored `"AI.Behavior*"` rule **does not match**. Would log to the rolling file and never reach the tab or the test |
| `BehaviorLog` *(rev 1's correction)* | Right logger name (`GetLogger("AI.Behavior")`) but **wrong assembly** — see **F3**. `Hrot.AI.Behaviors` is not guaranteed loaded when the in-memory compiler snapshots `AppDomain.CurrentDomain.GetAssemblies()` |

### ✅ The accessor to build

A small static in **`Fdp.Core.Logging`**, beside `AiBehaviorLogTarget`, with a logger name in the
`AI.Behavior` family and the five level probes.

⭐ **Why `Fdp.Core` is the correct layer, not merely a convenient one:** `CSharpEmitter.EmitUsings:133`
emits **`using Fdp.Core;` unconditionally, for every dispatch** — so `Fdp.Core` is loaded in every path
by construction. And `Fdp.Core` already hosts the **sink itself**. The dependency is therefore
self-consistent: **if the sink can capture the line, the helper that writes it is loadable.** No other
assembly gives that guarantee.

⚠ **F4 — entity context.** `BehaviorLog`'s `Entity:[…] Behavior:[…] Node:[…]` format needs `self`,
which **does not exist in a Library-dispatch method**. The helper needs a no-context overload, and
Q26-B settles what a library Print actually prints.

### Test interception

Read `AiBehaviorLogTarget.SharedInstance.GetMessages()` or subscribe `OnMessageAdded`.
⚠ **The rule is registered by `Program.cs`, which a headless test never runs** ⇒ **the test must add the
NLog rule itself.** That is expected, not a reason to invent a sink.

⭐ **Assert on a captured log line**, never on "the graph ticked".

---

## 5. Where the node must be registered

⚠ **F5 — `BuiltInNodeRegistry:194` ends `_ => Array.Empty<PinSchema>()`.** A kind missing from that
switch gets **zero pins and no diagnostic** — trap #5's shape. Adding the registry entry is not
optional bookkeeping; it is the thing that makes the node exist.

| Site | Why |
|---|---|
| `Assets/Nodes.cs` | the `DebugPrintNode : Node` model |
| `Compiler/Catalogs/BuiltInNodeRegistry.cs` | ⭐ **the pin shape — the single source both projections read** |
| `Compiler/Stages/Stage5_Schedule.cs` | lowering → the log call op |
| `Compiler/Emit/StatementEmitter.cs` | the emitted `string.Format` + level-probe guard |
| `Editor/NodeDrawers/BlueprintNodePaletteEntries.cs` | palette entry |
| `Editor/NodeDrawers/` | drawer + the detail-panel properties (Format, Level, ArgCount, ArgTypes) |
| ~~`Stage0_Rehydrate.cs`~~ | ⭐ **NOT needed — F2.** Pins derive only from the node's own properties |

---

## 6. Deliberately out of scope

| | |
|---|---|
| A variadic / wildcard pin mechanism | `ArgCount` instead — no new pin mechanism |
| A `ToString` / `Format` / `Concat` node, or string coercion | None exists; a new node *vocabulary* ⇒ architect |
| Printing to screen as well as the log | Not asked for; the MessageLog tab is the shipped surface |
| `System.String` as a blueprint **variable** | ⚖️ D3 — open on [BP-87](Blueprint_Issues_Detail.md#bp-87) item 6 |
