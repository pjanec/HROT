# Print / Log node — design note

> **Status:** ⚖️ **coordinator-decided** (Batch 23 handoff §6, decisions **D4** + **D5**).
> **The architect has NOT seen this.** Every element below reuses shipped machinery precisely so it is
> defensible without an architect round. ⚠ **Any widening — a variadic pin mechanism, a new
> `ToString`/`Format` node vocabulary, a string *variable* type — needs the architect first.**

---

## 1. Why the node exists

> User: *"we are still missing a node allowing us to write a formatted string to log or console, which
> could be testable. That one is really useful and should be added."*

Unreal's **Print String** is the first node most designers reach for. Today a blueprint author has no
way to see what a graph is doing short of attaching a debugger — [BP-109](Blueprint_Issues_Detail.md#bp-109)'s
smoke test had to assert through `BlueprintStateView.TryGetField<T>` for exactly this reason.

---

## 2. The shape

| Aspect | Decision | Grounding |
|---|---|---|
| **Format** | A **literal string property on the node**, not a pin | Sidesteps the managed-`System.String` problem (BP1503) entirely: a literal is fine, a string *variable* is not. Matches Unreal's Print String, whose `In String` is a literal in practice. |
| **Arity** | **Fixed: `Arg0..Arg2`**, optional data-in pins | ⚖️ **D4.** No node in the repo has a variadic pin shape, and inventing one is the "new pin mechanism" that would need the architect (⚖️ D6). |
| **Arg typing** | Per-pin declared `TypeId`, chosen in the detail panel | Feeds off [BP-87](Blueprint_Issues_Detail.md#bp-87)'s blueprint-local type list (`BlueprintTypeChoices`), shipped in this same batch |
| **Composite** | `string.Format`-style `{0} {1} {2}` | Standard, and it degrades sanely when an arg is unwired |
| **Verbosity** | **All five levels** — Trace/Debug/Info/Warn/Error — as a node property | The **round-out rule**: an enum-keyed node ships the whole enum, not just the one value the task needs |
| **Hot path** | Guard on the level probe **before** formatting | Skips the boxing of the `object` args when the level is off |

### Pin shape

```
exec In  ──▶ [ Print ] ──▶ exec Out
             Arg0 : <declared TypeId>   (data-in, optional)
             Arg1 : <declared TypeId>   (data-in, optional)
             Arg2 : <declared TypeId>   (data-in, optional)

  properties:  Format : string literal      Level : Trace|Debug|Info|Warn|Error
```

⚠ **Both pin projections must move together** — `NodePinSchema` **and** `Stage0_Rehydrate`. That is
trap #9's home, and it is exactly how [BP-113](Blueprint_Issues_Detail.md#bp-113) shipped half-built.

---

## 3. The sink

⚖️ **D5: do not invent an `IBlueprintLogSink`.** One already exists, and it is already wired to the
editor's **"AI Behaviors" MessageLog tab**:

`FDP/Engine/Fdp.Core/Logging/AiBehaviorLogTarget.cs` is an NLog `Target` **and** an
`IMessageLogSource`, with a process-wide `SharedInstance`, `GetMessages()`, `Clear()` and an
`OnMessageAdded` event. `Hrot.ClusterRunner/Program.cs:124` routes to it:

```csharp
logConfig.AddRule(LogLevel.Debug, LogLevel.Fatal, AiBehaviorLogTarget.SharedInstance, "AI.Behavior*");
```

### ⚠ Correction to D5 — the handoff named the wrong accessor

> D5 says *"NLog via `FdpLog<T>`, logger name in the **`AI.Behavior*`** family"*.

**`FdpLog<T>` cannot produce a logger name in that family.** It derives the name from the type:

```csharp
private static readonly Logger _logger = LogManager.GetLogger(typeof(T).FullName);
```

A generated blueprint class is `Hrot.AI.Behaviors.Generated.{Name}_{Id:X8}_Bp`, so its logger name is
`Hrot.AI.Behaviors.Generated.…` — which the `AI.Behavior*` rule **does not match** (NLog wildcards
anchor at the start). Emitting through `FdpLog<T>` would compile, log to the rolling file via the global
catch-all rule, and **never reach the AI Behaviors tab** — the one place the user would look. It would
also be invisible to the test, which is the deliverable that matters.

**⇒ Use `Hrot.AI.Behaviors.Logging.BehaviorLog` instead.** It is the shipped helper for precisely this
and it is already correct:

```csharp
private static readonly Logger s_log = LogManager.GetLogger("AI.Behavior");
```

It also already carries the level probes, and its structured format
(`Entity:[…] Behavior:[…] Node:[…] | {UserMessage}`) gives the printed line entity context for free —
better than a bare string, and the reason a designer can tell two entities' output apart.

⚠ **One gap to close:** `BehaviorLog` exposes **Debug / Trace / Warn / Error** but **no `Info`**. The
five-level round-out therefore adds an `Info` tier to `BehaviorLog`, mirroring the existing four exactly.
That is a same-shape addition to a shipped helper, not a new vocabulary.

The sink decision itself (D5) is **correct and unchanged** — only the accessor named in the handoff is
wrong against the code.

---

## 4. Test interception

Read `AiBehaviorLogTarget.SharedInstance.GetMessages()` (or subscribe `OnMessageAdded`). The detail
entry's design point 1 — *"the log sink must be interceptable"* — **is already satisfied by shipped
code**.

⚠ **The target is registered by `Program.cs`, which a headless test run never executes**, so `Write`
would never fire and `GetMessages()` would stay empty. **The test must add the NLog rule itself.** D5
anticipates this ("if not, the test registers it") and it is *not* a reason to invent a new sink.

⭐ **Assert on a captured log line**, never on "the graph ticked".

---

## 5. Deliberately out of scope

| | |
|---|---|
| A variadic / wildcard pin mechanism | ⚖️ D4 — fixed arity instead |
| A `ToString` / `Format` / `Concat` node, or string coercion | None exists; adding one is a new node *vocabulary* ⇒ architect (⚖️ D6) |
| `System.String` as a blueprint **variable** | ⚖️ D3 — still open on [BP-87](Blueprint_Issues_Detail.md#bp-87) item 6 |
| Printing to the console/screen as well as the log | Not asked for; the MessageLog tab is the shipped surface |
