# Architect Question #26 — the Debug Print node

> **For the architect.** Design note: [PrintString_Node_Design.md](PrintString_Node_Design.md) (rev 2).
> Everything not asked here is grounded in shipped prior art and is already being built.
>
> **Context in one line:** a blueprint author currently has **no way to see what a graph is doing**
> short of attaching a debugger. Unreal's *Print String* is the first node most designers reach for.
>
> ⚠ **Four of these are genuinely open. One (E) is a naming nod.** Each carries Claude's lean and the
> reuse-vs-build tradeoff, so a one-word answer per question is enough.

---

## A · Which layer owns the blueprint-facing log helper?

Generated blueprint C# must call *something* to emit a line. The sink is settled —
`Fdp.Core/Logging/AiBehaviorLogTarget.cs`, already an NLog `Target` + `IMessageLogSource`, already wired
to the editor's "AI Behaviors" tab. **The question is which assembly the emitter-facing helper lives in.**

| Option | Reuse vs build | Consequence |
|---|---|---|
| **A1 · `Fdp.Core.Logging`**, beside the target | **Build** — a ~30-line static | Engine layer gains a helper named for an AI concern |
| **A2 · `Hrot.AI.Behaviors.Logging.BehaviorLog`** (exists today) | **Pure reuse** — already correct logger name, already carries entity context | 🔴 **Does not work.** `MetadataReferenceResolver.ForRuntimeAssemblies` references only assemblies **already loaded**; `Hrot.AI.Behaviors` is not in the emitted usings and is not guaranteed loaded on the in-memory / hot-reload path ⇒ `CS0246` with no diagnostic explaining it — [BP-62](Blueprint_Issues_Detail.md#bp-62)'s shape recurring |
| **A3 · a new `Hrot.Blueprints.Runtime` helper** | Build + a new reference edge | Same load-order risk as A2 unless the emitter also guarantees the reference |

> 💡 **Claude's lean: A1.** Not merely convenient — *self-consistent*. `CSharpEmitter.EmitUsings:133`
> emits `using Fdp.Core;` **unconditionally for every dispatch**, and `Fdp.Core` already hosts the sink.
> ⇒ **if the sink can capture the line, the helper that writes it is loadable.** No other assembly
> gives that guarantee. The cost is one engine-layer type with an `AI.Behavior` logger name.

---

## B · What does a Print inside a **Function Library** print?

`EmissionContext.HasSelfInScope` is **false for `Dispatch: Library`** — the generated method is a
stateless static with **no `self` / `view` local**. So the entity context that makes two entities'
output distinguishable **cannot be filled there**.

| Option | Reuse vs build | Consequence |
|---|---|---|
| **B1 · Allow it, no entity context** | Reuse — one extra overload | Library lines read `Behavior:[SmokeMathLib] | …` with no entity. Honest but weaker |
| **B2 · Forbid Print in Library dispatch**, diagnostic at Stage 2 | Reuse — mirrors **BP1101**, which already forbids `Delay` in a library for the same "no instance context" reason | Consistent with a precedent the codebase already sets; costs the designer the ability to debug shared functions — **which is where a shared bug is hardest to find** |
| **B3 · Pass the caller's entity through the library call** | 🔴 **Build** — changes the Library calling convention | Real capability, real blast radius. Almost certainly not now |

> 💡 **Claude's lean: B1.** B2 is the tidier precedent but removes debugging from exactly the code that
> most needs it — a shared function called by many entities. A line without entity context is still
> vastly better than no line. ⚠ If B2 is chosen, say so early: it changes what the smoke test can assert.

---

## C · Format string — node **literal**, or a **pin**?

⭐ **This premise changed last night.** The literal-only decision existed to dodge `System.String` being
**managed** (BP1503 forbids it in a `State` struct). But **BP-87 shipped `FixedString32/64` into the
type picker**, and those are **unmanaged** — so a string *variable* is now representable.

| Option | Reuse vs build | Consequence |
|---|---|---|
| **C1 · Literal property only** | Reuse — mirrors `LiteralNode.ValueJson` | Simplest. Cannot print a *computed* or *stored* message |
| **C2 · Literal, plus an optional `Format` data-in pin** typed `FixedString32/64` | Build — one more pin, plus precedence rules | Lets a message come from a variable. ⚠ 32/64 chars is short for a format string |
| **C3 · Pin only** | Build | Every Print needs a wired literal node ⇒ **worse UX than Unreal**, which defaults the string inline |

> 💡 **Claude's lean: C1 now, C2 later if asked.** The `FixedString` lengths (32/64) are short for
> formats, and C1 covers the stated need. ⚠ **But this is the one place where I would most like to be
> overruled** — if designers will want message text driven by state, C2 is far cheaper to design in now
> than to retrofit once assets exist.

---

## D · Arity model

⚠ **"Optional pins" are not available.** `Stage5.ResolveDataPin:2126-2159` emits **`BP4001` warning +
`default(T)`** for *every* unwired data-in. Three always-present arg pins ⇒ **two warnings on every
one-argument Print** — on the most-used node in the graph.

| Option | Reuse vs build | Consequence |
|---|---|---|
| **D1 · Author-declared `ArgCount` (0..3)** | Build — property-driven pin **count** is new; `ArrayMakePins` drives pin **types** from a property but **hardcodes 2 pins** | Exactly the declared pins exist ⇒ **zero spurious BP4001**, and BP4001 keeps meaning "you forgot to wire something" |
| **D2 · Fixed 3 pins** | Pure reuse — copies `ArrayMakePins` verbatim | 🔴 Warning spam, and it trains designers to ignore BP4001 |
| **D3 · True variadic / wildcard pins** | 🔴 **Build a new pin mechanism** | No node in the repo has one; this is the "new mechanism" that would need its own round |
| **D4 · `ArgCount` with a higher cap (0..8?)** | Same build as D1 | Costs nothing extra mechanically |

> 💡 **Claude's lean: D1, and the only sub-question is the cap.** I lean **0..4**: Unreal's Format Text
> is unbounded, but four covers essentially every debug line, and the cap is one constant to raise later.
> ⚠ D2 is the tempting "pure reuse" answer and I think it is a trap — the warning noise is permanent.

---

## E · Name — a nod, not a decision

The user said *"debugPrint"*; Unreal calls it **Print String**. Lean: class `DebugPrintNode`, palette
**"Debug Print"** — honours the user's word, and *"Debug"* correctly signals it is not
shipping-gameplay output. Say if you would rather match Unreal exactly.

---

## What is already being built without an answer

| ✅ | Grounding |
|---|---|
| `BuiltInNodeRegistry` as the **only** pin projection — no `Stage0_Rehydrate` case | Stage0 builds pins from *"static registry shapes"* and enriches only kinds whose pins depend on data **outside** the node. Print's do not ⇒ **trap #9 is structurally impossible here** |
| All five verbosity levels | Round-out rule |
| Level probe before formatting | `FdpLog`'s own doc prescribes it |
| Per-arg `TypeId` from `BlueprintTypeChoices` | Mirrors `ArrayMakeNode.ElementTypeId`; picker shipped in Batch 23 |
| Test asserts a **captured log line**, registering the NLog rule itself | `Program.cs` never runs headless |
