# Print String + Format String nodes — design note · **rev 3**

> **rev 1** (Batch 23) recorded the coordinator's ⚖️ D4/D5 decisions.
> **rev 2** (2026-08-09) re-grounded them against the code — six findings, three of which broke rev 1.
> **rev 3** (2026-08-09) records the user's decisions and **a correction to my own D1 that came out of
> checking what Unreal actually does.**

---

## 0. ✅ Settled by the user

| | |
|---|---|
| **Name** | **`Print String`** — match Unreal, not the working title "Debug Print" |
| **Sink layer** | Q26-**A1** — helper in `Fdp.Core.Logging` |
| **Library dispatch** | Q26-**B1** — allowed, no entity context |
| **Arity** | Q26-**D** — see the ⭐ correction below; the user's Format-String request changed this |
| **⭐ New: a second node** | **`Format String`** — same formatting, but **writes to an output pin** instead of logging |
| **⭐ New: `FixedString128`** | Add it — 64 is too small for a formatted message |

---

## 1. ⭐ Unreal check — and my D1 lean was worse than Unreal's answer

The user guessed Unreal has a non-printing format node. **It does: `Format Text`.** Checking how it
works overturned my own recommendation:

> *"an input parameter is created for each `{}` delimiter found in the Format parameter"* — the pin set
> is **derived by parsing the format string**. Placeholders are **named** (`{Name}`, `{Score}`), and the
> name is arbitrary. Arg types may be Byte, Integer, Float, Text, String, Name, Boolean, Object.

| | My D1 lean (rev 2) | Unreal | Verdict |
|---|---|---|---|
| Arity | an `ArgCount : 0..4` property the author sets | **derived from the format string** | 🔴 **Unreal's is better** — you type `"threat={Threat}"` and the pin appears. No second control to keep in sync with the text |
| Pin names | `Arg0`, `Arg1` | `Threat`, `Score` | 🔴 **Unreal's is better** — self-documenting on the canvas |
| Cap | 0..4 | unbounded | Unbounded is free here |

⭐ **Adopt Unreal's model.** It satisfies **F1** (only declared pins exist ⇒ **zero spurious BP4001**)
*better* than `ArgCount` did, and it deletes a property. **`ArgCount` is dropped.**

⚠ **The one place we cannot follow Unreal:** its arg pins are wildcards resolved at connect time. We
have no wildcard mechanism, so each placeholder still carries a **declared `TypeId`** — a
`name → TypeId` map on the node, populated from `BlueprintTypeChoices`.

---

## 2. Two nodes, one machinery

The user's instinct that these belong together is right — they differ only in what they do with the
formatted result, so **one parser, one pin-derivation function, one emit path.**

```
[ Print String ]  exec In ──▶ ──▶ exec Out          [ Format String ]   (pure — no exec)
   Threat : float      ← derived                       Threat : float   ← derived
   Squad  : int        ← derived                       Squad  : int     ← derived
                                                       Result : FixedString64  ← declared
 properties: Format, Level                           properties: Format, ResultType
```

| | Print String | Format String |
|---|---|---|
| Exec pins | ✅ In / Out | ❌ **pure**, like Unreal's Format Text |
| Result | → the log | → a **data-out** pin |
| Extra property | `Level` (all five) | `ResultType` : `FixedString32\|64\|128` |

⭐ **They compose without any new mechanism.** A `Format String` output is a `FixedString`, and
`FixedString` is a legal arg type — so wiring one into a `Print String` placeholder prints a computed
message. **No "optional string pin" on Print String is needed**, which is what kept Q26-C awkward.

### Format grammar

| | |
|---|---|
| Placeholder | `{Name}` — letters/digits/underscore. **First-appearance order** fixes pin order |
| Repeats | `{Name}` twice ⇒ **one** pin, used twice |
| Escape | `{{` and `}}` emit a literal brace |
| Emit | ⭐ **a compile-time C# interpolated string**, not `string.Format` — see §3b |
| Malformed | unclosed `{`, empty `{}`, or an invalid name ⇒ **a Stage 2 diagnostic naming the node**, never a silent drop |

⚠ **Renaming a placeholder renames a pin**, which can drop a link. Same class BP-113 hit. Acceptable —
but the drawer should make the pin set visibly follow the text so it is never a surprise.

---

## 3b. ⭐ Allocation — raised by the implementation session, and they were right

> Them: *"Format String is pure, so unlike Print String it has no level probe to hide behind, and a pure
> node in a Tick graph would allocate a managed string every tick for every entity. The design note does
> not address it."*

**Correct, and rev 3 as first written would have shipped that.** `Print String` hides its allocation
behind `if (IsInfoEnabled)`; `Format String` is pure and always runs. `string.Format` allocates, and
`new FixedString128(string)` needs a managed string to convert **from** — so the naive emit allocates
**twice per node per entity per tick.**

### The fix — and it is available only because the format is known at generation time

⭐ **The format literal is a compile-time constant of the generated C#.** So emit a **real interpolated
string**, not a runtime `string.Format` call. That unlocks the zero-allocation path:

```csharp
// Format String — no managed allocation at all
Span<char> __b = stackalloc char[128];
__b.TryWrite($"threat={__t3} squad={__t4}", out int __n);
__result = new global::Fdp.Core.FixedString128(__b[..__n]);
```

```csharp
// Print String — allocates only when the level is on, which is the point of the probe
if (BlueprintLog.IsInfoEnabled) BlueprintLog.Info($"threat={__t3} squad={__t4}");
```

| Requirement | Status |
|---|---|
| `MemoryExtensions.TryWrite` + interpolated handler | ✅ .NET 6+; `Hrot.AI.Behaviors` and `Fdp.Core` are both **net8.0** (verified) |
| ⚠ **`FixedString` needs a `ReadOnlySpan<char>` constructor** | 🔴 **It has only `(string)`** (verified). **Add span ctors to 32/64/128** as part of item 2 — without it the whole exercise still allocates |
| Stack buffer size | Match the declared `ResultType` (32/64/128). Truncate on overflow, same as the string ctor |

⇒ **Named → positional mapping disappears too.** `{Threat}` becomes `{__t3}` directly in the
interpolated string; there is no `"{0}"` intermediate to build.

---

## 3. `FixedString128`

⚠ **It does not exist.** `Fdp.Core` has **`FixedString32` and `FixedString64` only** (verified). Adding
128 is a **mechanical mirror of `FixedString64.cs`** (`Size = 128`, `MaxLength = 127`) — but it is
**wider than it sounds**: ~10 production sites reference the family, including serialization converters,
the ImGui field editor, `StaticTypeRegistry`, `Stage3_Normalize`, `BlueprintTypeSystem` and
`BlueprintPinModel`. Full list in the handoff.

⚠ **Truncation is silent.** `FixedString64(string)` truncates in its constructor. A formatted result
longer than the chosen `ResultType` is **cut, at runtime, with no diagnostic** — Stage 2 cannot know the
length. **State this in the node's tooltip**; it is the first thing that will confuse someone.

---

## 4. Verified findings that still stand (rev 2)

| # | |
|---|---|
| **F1** | No such thing as an optional data-in pin — `Stage5.ResolveDataPin:2126-2159` emits **`BP4001` + `default(T)`** for each unwired one. ⇒ derived pins only, no placeholders you did not write |
| **F2** | ⭐ **Trap #9 does not apply.** `BuiltInNodeRegistry` is the single source (`NodePinSchema` delegates via `FromRegistry:226-228`; Stage0 builds from *"static registry shapes"* and enriches only kinds depending on data **outside** the node). Both nodes' pins derive purely from their own `Format`/`ResultType` ⇒ **register once, NO `Stage0_Rehydrate` case** |
| **F3** | The sink helper must live in **`Fdp.Core.Logging`**. `BehaviorLog` (`Hrot.AI.Behaviors`) is **not guaranteed loaded** when `MetadataReferenceResolver.ForRuntimeAssemblies` snapshots the AppDomain ⇒ `CS0246` on hot reload, unattributable — [BP-62](Blueprint_Issues_Detail.md#bp-62)'s shape. `CSharpEmitter.EmitUsings:133` emits `using Fdp.Core;` **unconditionally**, and `Fdp.Core` hosts the sink ⇒ **if the sink can capture, the helper is loadable** |
| **F4** | `HasSelfInScope` is **false for Library dispatch** ⇒ no entity context there. Q26-B1: allow it anyway |
| **F5** | ⚠ `BuiltInNodeRegistry:194` ends `_ => Array.Empty<PinSchema>()` — a missing kind gets **zero pins, silently**. Trap #5's shape |
| **F6** | `ArrayMakePins:299` drives pin **types** from a property but **hardcodes two pins** — property-driven pin **count** is new either way |

---

## 5. The sink

✅ Unchanged: `Fdp.Core/Logging/AiBehaviorLogTarget.cs` — NLog `Target` **and** `IMessageLogSource`,
`SharedInstance` / `GetMessages()` / `OnMessageAdded`, wired to the editor's **"AI Behaviors"** tab by
`Hrot.ClusterRunner/Program.cs:124` with the rule `"AI.Behavior*"`. **Do not invent an
`IBlueprintLogSink`.**

⛔ Two accessors ruled out: **`FdpLog<T>`** (logger name is `typeof(T).FullName` ⇒
`Hrot.AI.Behaviors.Generated.…`, which the prefix-anchored rule does not match) and **`BehaviorLog`**
(right name, **wrong assembly** — F3).

**Test interception:** `AiBehaviorLogTarget.SharedInstance.GetMessages()`. ⚠ `Program.cs` never runs
headless ⇒ **the test registers the NLog rule itself.**

---

## 6. Out of scope

| | |
|---|---|
| Wildcard / type-inferring arg pins | No mechanism exists; each placeholder carries a declared `TypeId` |
| `System.String` as a blueprint **variable** | ⚖️ D3 — open on [BP-87](Blueprint_Issues_Detail.md#bp-87) item 6 |
| Printing to screen as well as the log | The MessageLog tab is the shipped surface |
| A `Concat` / `Append` node | `Format String` covers it — `"{A}{B}"` |

Sources for the Unreal comparison: [Format Text — UE 5.8 docs](https://dev.epicgames.com/documentation/en-us/unreal-engine/BlueprintAPI/Utilities/Text/FormatText) · [Format Text — UE 5.6 docs](https://docs.unrealengine.com/en-US/BlueprintAPI/Utilities/Text/FormatText/index.html) · [UE Tip: The Format Text Node](https://unrealdirective.com/tips/format-text-node/)
