# DESIGN — the variable Details panel, live values, and the access stack

> **Coordinator, `2026-08-15`.** ⭐ **The consolidated design.** Rulings and their derivations live in
> 📄 **[`Architect_Question_32_…_ANSWERS.md`](Architect_Question_32_Variable_Details_And_Values_ANSWERS.md)**;
> **this document is what gets built.**
>
> ⛔ **Standing constraint over everything below (ruling 9):**
> ***"a clean solution — no keeping two implementations for the same concept."***

---

## 1 · The model — one cell

| kind | is | |
|---|---|---|
| `Variable` · `WorkingState` | ⭐⭐ **ONE cell — `(State, Asset)`** | same backing `VariableDecl`, same `DefaultValueJson`, **already emitted identically** |
| `Parameter` | `(Input, Asset)` | *"a genuinely different thing"* — supplied at spawn. ⛔ **not unified** |

⇒ **The panel treats `Variable` and `WorkingState` identically. Everywhere.**

---

## 2 · The access stack

![access stack](DESIGN_Variable_Access_Stack.svg)

### ⭐⭐⭐ The rule the whole design turns on

> **GENERATE THE DATA. HAND-WRITE ONE GENERIC ACCESSOR. NEVER GENERATE PER-VARIABLE CODE.**

This is not new — it is the convention already in the repo, made explicit:

| | generated (data) | hand-written (one, generic) |
|---|---|---|
| **ships today** | `StateFields: name → (ClrType, OffsetBytes, SizeBytes)` `CSharpEmitter:413` · `DebugMap.StateLayout` `:83` | `BlueprintStateView.TryGetField<T>` · `MarshalFromBytes(byte[], Type)` |
| **to build** | ⭐ **user-struct sizes — emit `Unsafe.SizeOf<T>()`** (§4) | `TrySetFieldRaw` · `MarshalToBytes(object, Type)` |

⛔ **`grep` finds no per-variable generated accessor anywhere. Do not introduce the first one.**

### Why the tiers are split where they are

| tier | why it exists |
|---|---|
| **1 · UI ⇄ object** | StructEdit already edits **any** blittable struct by reflection ⇒ reuse, do not rebuild |
| **2 · object ⇄ bytes** | ⭐⭐ **The UI holds a `Type` at run time, never a `T` at compile time** ⇒ the API here **must be non-generic**. `TryGetField<T>` is unreachable from a panel iterating descriptors — ⛔ **and a *generated* `SetHealth(float)` would be equally unreachable, so generating accessors does not solve this; it moves the dispatch and adds N methods** |
| **3 · bytes ⇄ blackboard** | offset + size + hash guard. ⭐ **`TryGetField<T>`/`TrySetField<T>` survive as one-line wrappers over the raw pair** ⇒ one implementation, two faces |

---

## 3 · Writes

### 3.1 When

| state | value shown | write |
|---|---|---|
| **not running** | the **initial** value | ⭐ → `DefaultValueJson` in the asset JSON |
| **running, free** | the current value | ⛔ **disabled** |
| ⭐ **paused on a breakpoint · deterministic stepping** | the current value | ✅ **enabled** |
| **replay** | the current value | ⛔ **refused, naming the reason** — a write diverges the run from the recording |

⭐ **Because writes happen only while paused, nothing else is mutating the blackboard** ⇒ no race to
design against, and ⛔ **the command buffer is not required** for this path.

### 3.2 Where — ⭐⭐ **both copies, always**

```
edit ──▶ _preTickSnapshot   (ActiveView while paused — what the panels display)
     └─▶ _liveRepo          (what the sim resumes from)
```

⭐ **Writing both makes the resume-sync direction irrelevant** — the two already agree, so it does not
matter which way `SyncFrom` runs. ⚠ **Still assert it: edit while paused → resume → the value survives.**

### 3.3 How — ⭐ **surgical, never whole-component**

⛔ **`Blackboard1024` is ONE component shared by BTree, HSM and Blueprint** — *"each subsystem projects
at a disjoint byte offset."* ⇒ **a whole-component write clobbers other subsystems' state.**

📌 *(It is not a size problem: `ByteSize == 1024` and the ECB check is `> 1024`, so it would fit. The
sharing is the reason.)*

**Every write is `(offset, size)` bounded**, through tier 3, after the `StructureHash` matches.

---

## 4 · User-defined structs — ⭐⭐ **MEASURED end to end**

📌 **33 struct-typed declarations ship today, across ALL THREE kinds** — `EqsSensorHandle` ×26 as
**`Variable`**, `Entity` ×4 as **`Parameter`**, `MemberSlotList` ×2 + `WaveState` ×1 as `WorkingState`.
⇒ ⭐ **structs are not a WorkingState speciality; they reinforce the one-cell model.**

| # | surface | struct support | the mechanism |
|---|---|---|---|
| 1 | `StaticTypeRegistry.TryResolve` | 🟠 **PARTIAL** | 3 hardcoded entries with hand-computed sizes; ⛔⛔ **the `global::` fallback ALWAYS GUESSES `SizeBytes = 4`** (`:286-296`) |
| 2 | **the type picker** | 🟠 **PARTIAL — and there are TWO** | ⭐ `SelectableTypeIds` (`BlueprintTypeSystem:286-319`) **does** offer `[BlackboardDtoStruct]` structs → used by `VariableCreateModal`. ⛔ **`EditorOfferableTypeIds` (primitives only) drives PARAMETERS** ⇒ **a Parameter can never be given a struct through the picker — yet 4 ship** |
| 3 | wire compatibility | ✅ **yes** (by falling back to raw string equality when a type will not resolve) |
| 4 | 🔴 **`MarshalFromBytes`** | ⛔ **NO** | no struct arm; and `ResolveType` uses `Type.GetType(fqn)` **without an assembly qualifier** ⇒ never finds a game struct ⇒ the field is **skipped** |
| 5 | emitters | 🟠 **PARTIAL** | ⭐ the `layoutFromRuntime` safety net exists **for `asset.Variables` ONLY — not `WorkingState`** |
| 6 | ⭐⭐ **`StructSizeResolver`** | ✅ **FULLY GENERAL** | Roslyn-symbol based, recursive, handles nested structs/enums/`[StructLayout]` — ⛔ **but it lives in `Hrot.AiEditor.Generators` and the Blueprint compiler NEVER CALLS IT** |
| 7 | `VariablesPanelControl` | 🟠 Type/Bytes fine; **Value depends on #4** ⇒ `—` |
| 8 | `TryGetField<T>` | ✅ generic (`T : unmanaged`) — ⛔ **but gated on `StateFields`, see #10** |
| 9 | fixed lists of structs | ⛔ **NO** | the fallback **drops `Capacity`** ⇒ a declared list silently degrades to a **scalar** |
| 10 | 🔴🔴 **AiPrimitive debug metadata** | ⛔⛔ **NONE AT ALL** | `CSharpEmitter:77-86` populates `StateLayout` **only for `Instance`**, and `EmitAiPrimitiveRegistration` emits **no `StateFields`, `StateSize = 0`** ⇒ **WorkingState has no metadata for ANY type — struct or primitive** |

### 🔴🔴 The two blockers, named

| | |
|---|---|
| **B1 — no general size-accurate registration** | ⛔ an unregistered struct resolves at **4 bytes**, and `FieldLayout.TypeAlignment` then computes alignment from that lie. ⭐⭐⭐ **The general mechanism ALREADY EXISTS — `StructSizeResolver` — in the BTree/HSM generator.** ⇒ **ruling 9 again: two size paths, one general and unused, one hardcoded and load-bearing.** 📐 **Reuse it; do not write a third** |
| **B2 — AiPrimitive emits no state metadata** | ⛔ **the Details/Watch value column cannot work for AiPrimitive assets at all today.** ⭐ Not a struct problem — a **whole dispatch kind** is invisible. 📐 **`EmitAiPrimitiveRegistration` must emit `StateFields`/`StateLayout` like the Instance path** |

⇒ ⭐ **Struct support is not one feature — it is B1 + B2 + the `MarshalFromBytes` struct arm (#4) +
the `Capacity` fix (#9) + one picker instead of two (#2).** **All five are named work items, §8.**

---

## 4a · Why generation is still DATA, not accessors

🔴 **Today only three are supported, hardcoded with hand-computed sizes** (`StaticTypeRegistry:75-81`),
and the file names its own gap: *"a general curated-struct registration mechanism … is future work."*

⭐⭐⭐ **The compiler does not know the layout either — it emits code that ASKS:**

```csharp
// CSharpEmitter:412 — already shipping, for !SizeReliable types
(int)Marshal.OffsetOf<{className}.State>("{f.Name}")
```

⇒ **The `netstandard2.0` generator cannot reflect, so it emits C# that Roslyn resolves where the type
IS loaded.** ⭐ **Generalise exactly this** — emit `Unsafe.SizeOf<TheUserStruct>()` for any struct-typed
variable instead of requiring a registry entry. ⛔ **Still data, still no accessor.**

| | |
|---|---|
| ⭐ **read/write of a user struct's VALUE** | ⛔ **needs nothing new** — at run time the CLR type is loaded, so `Marshal.PtrToStructure` / `StructureToPtr` and StructEdit's reflection cover **every** blittable struct in one arm |
| ⭐ **its SIZE/OFFSET in `State`** | **the actual gap**, and it is solved by emitting a `sizeof`, not by generating setters |

---

## 5 · The panels

| | Details | Watch |
|---|---|---|
| **is** | an **authoring** surface that also shows runtime | a **runtime** surface only |
| **before the run** | ⭐ the **initial** value, editable | ⭐⭐ **nothing** |
| **while running** | current value | current value |
| **editing** | ✅ | ✅ **same dialog, same path** |
| **structure** | ⭐ **chameleon — one provider per selection kind**; ⛔ **never a `switch` in the panel** | — |

> ⛔⛔ **COORDINATOR ERROR, CORRECTED (4th).** An earlier draft said the chameleon *"is already modular,
> dispatched through the existing `DrawerRegistry`."* **That was a NAME COLLISION.**
> `BlueprintDetailsWindow._drawerRegistry` is a **`BlueprintNodeDrawerRegistry`** — graph **node**
> drawers, a different class in a different namespace. ⛔ **It has nothing to do with
> `Inspector.DrawerRegistry`.** ⇒ ⭐ **the provider/dispatch structure for the Details panel must be
> BUILT, not extended.** *(`BP-205`'s panel-level `PushID` scoping is real and does still apply.)*

⚠ **The pre-run asymmetry is deliberate. Do not "unify" it** — ruling 9 forbids two implementations of
one concept, not two behaviours of two different concepts. **A Watch on an entity that has not spawned
has nothing to show, and printing the JSON default there would invent a "current" value.**

**Selection routing:** a **global** clicked in the outline ⇒ the globals/working-state list · a **local**
clicked ⇒ the locals **of the current graph**.

**The cell:** read-only, pretty-printed tooltip at full size. **Three-dot button *and* double-click**
open the StructEdit dialog (OK / Cancel), initialised to the current value.

---

## 6 · Rails — each one exists because something silently failed without it

| rail | catches |
|---|---|
| 🔴🔴 **`Marshal.OffsetOf<State>(name) == descriptor.OffsetBytes`, asserted at runtime for every field of every corpus asset** | ⛔ **the compiler and the CLR disagreeing on layout** — `TypeAlignment` gives `Vector3` align 8, the CLR packs at 4. ⭐⭐ **Golden Tier 1 CANNOT catch this: it records the COMPUTED offset, so both sides agree while the real field moves** |
| ⭐⭐ **marshaller pinned to `EditorOfferableTypeIds` by reflection test** | ⛔ **`BP-01`** — 7 of 18 offerable types fall through `MarshalFromBytes` to raw bytes. **Extends `U-8` from *"every offered type compiles"* to *"…and can be shown and edited"*** |
| **`StructureHash` match before any decode or write** | a stale layout reading or writing the wrong bytes |
| **size match + bounds check** | ⛔ **out-of-range is memory corruption, not a wrong value — fail loudly** |
| **edit-while-paused → resume → value survives** | the snapshot/live divergence in §3.2 |
| **`+8` header offset owned in exactly ONE place** | a header offset computed twice |

---

## 7 · Reuse ledger

| piece | |
|---|---|
| the table + its columns | ✅ **exists** — `VariablesPanelControl` |
| initial-value storage + emission | ✅ **exists for both kinds** |
| initial-value setter | ✅ interface exists; **HSM + BTree implement it, blueprint does not** |
| live-value read | ✅ column + `ILiveValueProvider`; **blueprints never supply it** |
| StructEdit dialog | ✅ **exists** |
| generic field read | ✅ `TryGetField<T>` + `MarshalFromBytes` |
| **generic field write** | ✚ `TrySetFieldRaw` + `MarshalToBytes` |
| **user-struct size registration** | ✚ emit `Unsafe.SizeOf<T>()` |
| **Watch panel edit + refresh** | ✚ — ⛔ **`HandlePinValueChanged` is an EMPTY BODY today** |

---

## 8 · Sequencing

| | |
|---|---|
| **56** | emitter/access unification — `Variable ∪ WorkingState` *(dispatched)* |
| **57** | Details hosts the shared control + selection routing |
| **58** | the value column + blueprint's `ILiveValueProvider` and `UpdateVariableDefaultValueJson` |
| **59** | StructEdit dialog (button + double-click) · the **not-running** write |
| **59b** | Watch: real refresh · editing · nothing before the run |
| ⭐ **59c** | tier 2 + tier 3 write halves · **both-copies** write · the §6 rails |
| 🔴🔴 **S1** | **B2 — `EmitAiPrimitiveRegistration` emits `StateFields`/`StateLayout`** ⛔ *without it the value column is dead for every AiPrimitive asset* |
| 🔴 **S2** | **B1 — reuse `StructSizeResolver` for blueprint struct sizes**; retire the 4-byte guess; extend `layoutFromRuntime` to `WorkingState` |
| **S3** | `MarshalFromBytes`: one generic struct arm (`PtrToStructure`) + assembly-qualified `ResolveType` ⇒ closes **`BP-01`** |
| **S4** | fixed lists: stop dropping `Capacity` in the fallback |
| **S5** | ⭐ **ONE picker** — `EditorOfferableTypeIds` ∪ `SelectableTypeIds`; ⛔ a Parameter cannot be struct-typed by picker today while 4 ship |
| **60** | `U-16` — retire `BlueprintVariablesWindow` |
| **61** | ⭐ **the shared outline** across HSM / BTree / Blueprint — ⛔ **separate, and only after Details works for blueprints** |

---

## 9 · Previously open — ⭐ **now MEASURED and CLOSED**

### 9.1 ✅ The two struct-editing surfaces are **NOT duplicates** — one is **dead code**

| | verdict |
|---|---|
| **FDP-level `IComponentEditService` / StructEdit** | ⭐⭐ **The live one.** Reflection-driven `EditDocument`/`IEditSession` over **any** blittable struct, non-blittable struct or managed class; JSON round-trip; validation; custom editors. **9+ production call sites** |
| **Blueprint-local `IStructEditDrawer` / `DrawerRegistry` / `PrimitiveDrawers`** | ⛔⛔ **DEAD.** All four drawer bodies are stubs (*"`// ImGui.InputFloat(...) would go here`"*, `return false`). `DrawerRegistry.Register<T>` is called **only from tests**. Its one consumer takes the registry and **never reads the field**. That consumer is built only by `BlueprintWindowRegistrar`, which is **retired** — `EditorSubsystem:522-525`, `[Obsolete("Retired by AIE-015")] … => null` |

⇒ ⭐ **Build on StructEdit. ⛔ And DELETE the dead chain — it is not an implementation to reconcile,
it is a corpse that reads like one.** *(`ImGuiPropertyTree` is a read-only display tree, not a
competitor; `ComponentReflector` is a **consumer** of StructEdit.)*

### 9.2 ⭐⭐⭐ Ruling 5's **stopped-half already exists** — `DefaultValueAuthoring`

`Hrot/Editor/Hrot.Editor.AiShared/Inspector/DefaultValueAuthoring.cs` — **headless-testable, generic
over any CLR type, already shipping:**

```
Hydrate(Type fieldType, string? defaultValueJson) → boxed instance   (JSON → object)
OpenSession(IComponentEditService, BlackboardVariableEntry) → IEditSession
CommitAndSerialize(IEditSession, Type) → string                      (object → JSON)
```

⭐ `JsonOptions { IncludeFields = true }` ⇒ **public struct fields round-trip.** ⇒ **the "edit the
initial value in a StructEdit dialog" flow is BUILT** — what is missing is (a) blueprints implementing
`UpdateVariableDefaultValueJson`, (b) surfacing it from Details via the three-dot / double-click.

### 9.3 ⚠ `BlueprintStateView` — **do NOT promote it. The read path already has TWO implementations.**

| | measured |
|---|---|
| production callers | ⛔ **ZERO.** Tests only; ctor is `internal` and `Hrot.Blueprints.Editor` is **not** in `Fdp.Toolkits`' `InternalsVisibleTo` |
| `BlueprintDefinition` in the editor process | ✅ **live** — `CgfSubsystem:266` → `EditorSubsystem:1010-1016` → production `BlueprintDebugSession`. **Not a blocker** |
| 🔴 **raw `byte*`, no lifetime guarantee** | valid only synchronously; dangles across a frame or a structural change ⇒ **needs a discipline layer the type does not provide** |
| 🔴🔴 **the decisive one** | ⛔ **a SECOND production reader already exists** — `BlueprintDebugSession.MarshalFromBytes` + hand-rolled offset slicing at `:1308-1322` / `:1381-1396`, doing the same job |

⇒ ⭐⭐ **Corrected decision: do not "promote the test one". EXTRACT ONE accessor and make BOTH call it**
— the production debug-session reader and `TryGetField<T>` become faces of it. ⛔ **Ruling 9 already
applies *inside* the read path; promotion would leave the duplication untouched.**

### 9.4 ⭐ The variable surfaces: **FOUR mechanisms, and the count corrects the plan**

| # | mechanism | shared control? | edits | live values |
|---|---|---|---|---|
| **1** | `VariablesPanelControl` | — *(is it)* | ✅ | ⭐ **optional — wired for BTree/HSM, NOT for blueprints** |
| **2** | `BTree/LiveBlackboardPanel` | ⛔ own ImGui + own unsafe reads | read-only | ✅ | 
| **3** | `InspectorWindow` "Static Parameters" | ⛔ StructEdit session | ✅ one default value | — |
| **4** | `InspectorWindow` "Parameter Synchronization" | ⛔ own table | ✅ binding metadata | — |

⭐⭐ **`BlueprintVariablesWindow` ALREADY renders through the shared `VariablesPanelControl`** ⇒
**`U-16` is smaller than assumed: the redundancy is a MISSING live-value wiring, not duplicate
rendering.** ⛔ **The two `InspectorWindow` classes are NOT duplicates** — the blueprint one is a
76-line metadata stub with no variables at all.
⭐⭐⭐ **And a live-value provider ALREADY EXISTS and already marshals structs generically:**
`LiveBlackboardValueProvider` reads `BrainBlackboard` at `BehaviorParameters + byteOffset` via
`Marshal.PtrToStructure` ⇒ **blueprints need their own, modelled on it — not a new concept.**
📌 **`LiveBlackboardPanel` has no `new` call site anywhere** ⇒ **possibly dead; confirm, then retire.**
