# Architect question #28 — the cross-host binding mechanism (BTree · HSM · Blueprint)

> **Raised 2026-08-14** by the cross-host variable/call design session, branch
> `claude/cross-host-variable-model-3k8cfh`, after the prior-art sweep in
> [`PriorArt_Cross_Host_Variable_Model.md`](PriorArt_Cross_Host_Variable_Model.md).
>
> ⭐⭐ **This is the MECHANICAL half** — how a binding is identified, where its offset comes from, and
> which layout is true. [#29](Architect_Question_29_Cross_Host_Variable_Semantics.md) carries the
> semantic half.
>
> ⚠ **Every "ground truth" row below was read out of the code or measured on 2026-08-14.** Nothing is
> inherited from the HSM session's §1 or the bootstrap's §4 without re-checking — three of their rows
> did not survive.

---

## Ground truth

### The three projection paths that exist today

| # | emitter | key | offset comes from | projection |
|---|---|---|---|---|
| 1 | `AiPrimitiveEmitter.cs:267` (blueprint standalone) | `"{Ns}.{Class}.BTreeTick@0"` — ⛔ **always `@0`** | ⛔ **nothing** | `bb.BehaviorParameters[paramIndex * SizeOf<Params>()]` — **stride** |
| 2 | `BTreeBridgeEmitCore.cs:488` (BTree managed) | `"{MethodFqn}@{ByteOffset}"` | ⭐ **the asset** (editor packer) | `Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], offset)` |
| 3 | ⭐⭐ `HsmActionGenerator.cs:261` (**HSM, already ships**) | `"{MethodName}@{offset}"` → `ComputeHash` | ⛔ **a source attribute** | **same as 2** |

⇒ ⭐⭐ **The HSM session's central proposal (`Q-A`) is already implemented.** The convention is not in
question. **Two things about it are.**

### 🔴 Path 1's multiplier is not a parameter slot

`Interpreter.cs:655` passes `node.PayloadIndex`; `NodeDefinition.cs:28` says that indexes
`MethodNames[]`; `TreeCompiler.cs:212 GetOrAddMethodName` **dedups by name**. Each generated class
multiplies that ordinal by **its own** `sizeof(Params)`. ⇒ **no allocator reserves these regions.**
⭐ **Safe today only because every path-1 key is `@0`.**

### 🔴 The id space is 16 bits, with three unguarded hazards

| | |
|---|---|
| `ComputeHash` | FNV-1a-32 → `(ushort)(hash & 0xFFFF)`. **Char-identical** in `HsmFlattener.cs:385` and `HsmActionGenerator.cs:802`; ~10 copies repo-wide |
| **collisions** | `RegisterAction` does `ActionTable[id] = ptr` — **silent overwrite**. The static-initialiser path uses `Add` — **throws**. Two failure modes, one hazard. ⚠ **~300 distinct keys ⇒ ~50 % chance of at least one collision** |
| ⭐ **reserved values** | `HsmKernelCore.cs:304,448,669,714` treat **both `0` and `0xFFFF`** as *"no action"*. ⛔ **`ComputeHash` excludes neither.** A key hashing to either is **silently never dispatched** |
| 🔴 **`HSM-016` on top** | `HsmBridgeEmitCore.cs:119` registers no-op stubs at `actionId = 100++` / `200++` while the flattener uses `ComputeHash(name)` ⇒ **a stub can overwrite a real action** |

⭐⭐ **Prior art for the fix already ships:** `UtilityInputGenerator.cs:173` +
`SharedUtilityDiagnostics.UT0103_HashCollision` — the same FNV-1a-16 gate, for utility input names.

### 🔴 The packers disagree with the compiled struct — measured

All three packers use `align = min(size, 8)`
(`BlackboardBinPacker.cs:102`, `BTreeBlackboardPackHelper.cs:22`, `HsmActionGenerator.GetTypeAlign`).

```
struct S { byte B; Vector3 V; }        // measured, .NET 8
Marshal.OffsetOf<S>("V")  = 4          // the DD calls this AUTHORITATIVE
packers compute min(12,8) = 8          // and BlackboardBinPackerTests.cs:131 ASSERTS the 8
```

⭐ The rule is right whenever `size ≤ 8`. It is wrong for **any type whose size exceeds its natural
alignment** — `Vector3` (12/4), `Vector4`/`Quaternion` (16/4). All are in every packer's `KnownSizes`.
📌 **No shipped asset hits it yet** — it needs such a variable preceded by a field ending off an
8-byte boundary. ⭐ **The `bool`/`[MarshalAs(I1)]` rule (`BTreeEmitCore.cs:112`) is the same class of
hazard, already handled. This one is not.**

### ROM constraints that bound the answers

`StateDef` = **32 B**, `[FieldOffset]`-explicit, **one spare byte** (`Reserved29`).
`TransitionDef` = **16 B**, none spare. ⇒ ⛔ **widening an action id from `ushort` to `uint` costs a
ROM change on both.**

---

## Q28-A — What identity does a binding carry?

| | Option | ⚖️ |
|---|---|---|
| **A1** | ⭐ Keep both conventions as they are — BTree `{MethodFqn}@{offset}`, HSM `{MethodName}@{offset}` | ✅ zero work · 🔴 **simple-name keying collides across types**; two conventions is the "two copies of an ordering that must agree" shape `BP-226` was about |
| **A2** | ⭐⭐ **Unify on `{MethodFqn}@{offset}`; HSM adopts the FQN form** | ✅ one convention, and the FQN removes the cross-type collision entirely · ⚠ changes every HSM `CompoundKey`, so every hashed id moves — **a one-time re-bake, headlessly checkable** |
| **A3** | Drop hashing: allocate dense ids at flatten time, store a side table | ✅ no collisions by construction · 🔴 **breaks hot-reload registration** — the registrar is a separate compilation that cannot see the allocator |
| **A4** | Keep `{MethodName}` but widen the id to `uint` | ✅ collision pressure ~vanishes · 🔴 **ROM change on `StateDef` AND `TransitionDef`** (16 B, nothing spare) |

⭐ **Independent of the choice, three gate items follow from the ground truth:**
**(i)** reject duplicate ids at generate time (mirror `UT0103`); **(ii)** reject any key hashing to
`0` or `0xFFFF`; **(iii)** ⛔ **`HSM-016`'s counter-allocated stubs must go** — they can overwrite.

📐 **Claude's lean: A2 + all three gate items.** ⭐ **A4 is the one to avoid**: it spends a ROM change
to buy what a compile-time gate gives free, and the 16-bit space is not actually the problem — the
*absence of a gate* is. ⚠ **A3 is worth a sanity check from you**: it is the only option that makes
collisions structurally impossible, and I may be over-weighting the hot-reload objection.

---

## Q28-B — Who computes the offset? *(this is the real `Q-A`)*

| | Option | ⚖️ |
|---|---|---|
| **B1** | **Source-driven** — the offset comes from `[SharedAiAction(typeof(Dto), "Field")]`, as HSM does today | ✅ already built; the analyzer can type-check the field · 🔴 **the editor cannot bind a variable to an offset no attribute declared** ⇒ HSM authoring stays broken |
| **B2** | ⭐⭐ **Asset-driven** — the editor's packer picks the offset, the bridge emits one thunk per `(method, offset)` pair found in the asset. **BTree path 2, generalised** | ✅ the editor can bind anything; **one mechanism instead of three** · ⚠ needs an HSM twin of `EmitManagedActionThunks` — **which is what `HSM-016` already is** |
| **B3** | Both, asset wins where present | ✅ no migration · 🔴 **two sources of truth for one number** — precisely the shape that produced `BP-226` |

📐 **Claude's lean: B2.** ⭐ **It is nearly forced:** B1 cannot express editor-chosen bindings at all,
and the work B2 needs is work `HSM-016` already scopes. ⚠ **The thing I want your ruling on is
whether B1 survives alongside B2 for hand-written library actions** — `BlueprintLifecycleLibrary`'s
three `[SharedAiAction]` methods are source-declared and work fine. **My lean is yes: B1 stays as the
hand-authored path, B2 is the authored-asset path, and they never describe the same binding.**

---

## Q28-C — Which layout is true?

| | Option | ⚖️ |
|---|---|---|
| **C1** | ⭐⭐ **Make the packer true by construction** — emit the blackboard struct with explicit `[FieldOffset]` at the packed offsets | ✅ **byte-stable for the existing corpus** (today packer == CLR everywhere it is exercised); eliminates the whole drift class, `bool` included · ⚠ `LayoutKind.Explicit` on every generated blackboard |
| **C2** | Fix the packers to compute **true natural alignment** (`min(size, 8)` → the struct's own max field alignment) | ✅ restores the DD's stated contract · 🔴 **offsets move ⇒ every key moves ⇒ the golden corpus moves**, and the DD's *"advisory"* packer stays advisory — the drift class returns for the next type |
| **C3** | Keep `min(size, 8)` and **refuse** variable types where `size > naturalAlign` | ✅ cheapest; a diagnostic, not a layout change · 🔴 **bans `Vector3`/`Vector4`/`Quaternion`** — all three are in `KnownSizes`, i.e. intended |

📐 **Claude's lean: C1.** ⭐ **The argument that decides it:** the DD says the packer *"must replicate
C# sequential layout exactly"* — C1 removes the obligation to replicate anything, because the packer's
number **becomes** the layout. C2 leaves a rule that must be re-verified for every future type; C1
leaves nothing to verify. ⚠ **And C1 is the only one that is byte-stable**, which matters with `U-12`
in flight and the golden corpus as the gate.

⚠⚠ **Whichever you pick, one gate is independent of the choice:** ⭐ **add a `U-1` Tier-1 corpus asset
with a `Vector3` preceded by a `byte`.** Today the corpus cannot see this class at all — a green that
means nothing.

---

## Q28-D — Does the standalone stride path (path 1) survive?

| | Option | ⚖️ |
|---|---|---|
| **D1** | ⭐ **Retire it** — route the standalone `BTreeTick` through the offset form with a baked `@offset` | ✅ **one projection formula repo-wide**; removes an unallocated region scheme · ⚠ touches `AiPrimitiveEmitter` + `CSharpEmitter`, and `U-12` is in flight |
| **D2** | Keep it, and **constrain it to `@0`** with a rail that refuses anything else | ✅ minimal; matches what ships today · ⚠ freezes a path that cannot host two actions |
| **D3** | Keep it and make the stride real — allocate slots properly | 🔴 **builds a second allocator** to sit beside the packer |

📐 **Claude's lean: D1, sequenced AFTER `U-12`.** ⭐ **The reason it matters beyond tidiness:** the
HSM session is about to adopt "the BTree convention", and BTree currently has **two** — if we answer
`Q-A` without saying which, we export the ambiguity. ⚠ **D2 is an acceptable interim** if you would
rather not open `AiPrimitiveEmitter` during the store flip; it is honest and costs one rail.
⛔ **D3 is the one to avoid** — it is `BP-226`'s shape a third time.

---

## Answers — ⭐⭐ ARCHITECT RULING, `2026-08-14`

> ⭐ **Provenance, stated plainly:** the user's NotebookLM architect is **unavailable**, and the user
> designated this session as the architect of record. ⇒ **These are rulings, not leans.**
> ⚠ **They are Claude-authored** — weaker provenance than the NotebookLM rounds that redirected four
> of the last nine batches. ⭐ **Every ruling below therefore names the measurement it rests on**, so a
> later architect can overturn it by attacking evidence rather than authority.
>
> ⭐⭐ **Four questions the first draft deferred have since been SETTLED BY MEASUREMENT.** Two of them
> moved the answer; one killed an option outright. See §*Measurements* below.

### ⭐⭐ Measurements taken to close this round

| # | question the draft deferred | result |
|---|---|---|
| **M1** | *"Does `C1` survive `[InlineArray]` / `fixed` buffers?"* | ✅ **YES — discharged.** A `LayoutKind.Explicit` struct holding an InlineArray-bearing DTO, a `fixed`-buffer DTO, a `Vector3` and a `bool` lays out **exactly** at the declared offsets, and ⭐⭐ **managed offsets == `Marshal.OffsetOf`**, which is the property that matters because `Unsafe.AddByteOffset` uses managed layout |
| **M2** | *"Is the hot-reload objection to `A3` over-weighted?"* | ⛔ **No — it is UNDER-weighted, and it kills A3.** `AiHotReloadCoordinator.ApplyReload:309` does `HsmActionDispatcher.ClearAll()` then re-registers from the **new** assembly — and ⭐⭐ **never re-flattens the ROM.** Live `StateDef.OnEntryActionId` values must still resolve afterwards |
| **M3** | *"How big is `Q29-B2`'s migration?"* | ⭐ **Near zero.** `[SharedAiCondition]`/`[SharedAiHeavyCondition]` have **0 production usages** — 4 usages, all test fixtures |
| **M4** | *"Do slot keys cross a compilation boundary?"* | ⛔ **Yes.** The stateful key is `{MethodFqn}@{offset}@{slotKey}`, baked as a `const` in the thunk, and the source says it *"must stay in lockstep with the topology blob key"* (`BTreeBridgeEmitCore.cs:610–622`) |

⭐ **And the collision numbers, computed rather than asserted** (78 `[HsmAction]`/`[HsmGuard]` sites today):

| distinct keys | P(≥1 collision) | | P(some key hits `0`/`0xFFFF`) |
|---|---|---|---|
| **78 (today)** | ⚠ **4.5 %** | 78 | 0.24 % |
| 120 | 10.3 % | | |
| 200 | 26.2 % | | |
| 300 | 🔴 **49.6 %** | 300 | 0.91 % |

⇒ ⭐⭐ **This is not a future risk. At today's population there is already a ~1-in-22 chance of a
silent collision**, and nothing would report it.

### The rulings

**A → A2, with all three gate items. ⭐⭐ The gate ships FIRST and ALONE.**

⭐ The framing that decides it: **the collision risk is not created by adding offsets to keys — it is
merely revealed by it.** A 16-bit space with silent-overwrite registration was always unsafe; it
survived because the population was small. ⚠ **`M4`'s numbers show it is not small enough even
today — 4.5 %.** ⇒ **the gate is not a precondition for A2, it is a defect fix that A2 happens to
need.** Ship it on its own, before the re-bake, so the re-bake has a green baseline to move against.

⛔⛔ **A3 REJECTED — and `M2` makes the reason much stronger than the draft's "separate compilation".**

> `ApplyReload` does `ClearAll()` → re-register from the **new** assembly → commit.
> ⭐⭐ **It never re-flattens the ROM.** Live `StateDef.OnEntryActionId` values were hashed at flatten
> time and must still resolve against a registry rebuilt from *different source*.

⇒ ⭐⭐ **The load-bearing property of the id is not "no collisions" — it is that ROM OUTLIVES THE
REGISTRY, and only a CONTENT-ADDRESSED id survives that.** A dense allocator renumbers whenever the
action set changes; every live entity's ROM would then point at the wrong thunk — ⛔ **silently, since
`ExecuteAction` does `TryGetValue` and no-ops on a miss.** ⭐ **Hashing is not a shortcut here; it is
the mechanism that makes reload safe.** Keep content-addressing; add the gate.

⚠ **A4 (`uint` ids) also rejected, and now for a second reason:** beyond the `StateDef`/`TransitionDef`
ROM cost, ⭐ **widening the id changes every persisted ROM blob** — the same
outlives-the-registry problem, one level up. **A gate is cheaper and does not touch stored data.**

**B → B2 for authored assets, B1 retained for hand-written library actions — and say so in one rule.**
The rule: ⭐ **an offset is authored where the binding is authored.** A `[SharedAiAction]` method
declares its own binding in source; an asset declares its bindings in the asset. **They are two
authoring surfaces, not two sources of truth for one number** — B3's defect is that it lets *both*
describe *the same* binding. ⇒ **the rail to build is not "asset wins", it is "a method that carries
`[SharedAiAction]` may not also be asset-bound"** — refusable at generate time, and it makes the
distinction structural rather than a precedence rule.

**C → C1. ⭐⭐ Its one precondition is DISCHARGED (`M1`), so this is now unconditional.**

⭐ The argument is about **who is allowed to be wrong**. Under C2/C3 the packer must *predict* the
CLR, and a prediction can drift silently with any future type or runtime. Under C1 the packer
*dictates* and the CLR obeys — ⛔ **there is no oracle left to disagree with.** A recurring
correctness obligation becomes a one-time emit change.

**`M1`, measured:**

```
[StructLayout(LayoutKind.Explicit)] struct Bb {
  [FieldOffset(0)] byte B;  [FieldOffset(8)]  Vector3 V;        // packer's 8, honoured
  [FieldOffset(24)] DtoWithInlineArray D;                        // InlineArray inside a field
  [FieldOffset(48)] DtoWithFixedBuffer F;  [FieldOffset(64)] bool Flag;
}
Marshal.OffsetOf  →  V=8  D=24  F=48  Flag=64
MANAGED offsets   →  V=8  D=24  F=48  Flag=64      ⭐ identical — and managed is what AddByteOffset uses
```

⭐ **Two things this settles that the draft did not anticipate:**

| | |
|---|---|
| ⭐ **The `[InlineArray]` fear was mis-scoped** | InlineArray/`fixed` appear **inside** DTO *field types* (`InstanceEmitter`), never as fields of the **blackboard** struct (`BTreeEmitCore` emits primitives and struct-DTOs). C1 touches only the blackboard struct; DTO internals stay `Sequential` and are untouched |
| ⭐⭐ **C1 SUBSUMES the `bool`/`[MarshalAs(I1)]` rule** | under Explicit, every offset is dictated, so **no arithmetic downstream of a `bool` depends on its marshalled size.** The rule becomes belt-and-braces rather than load-bearing — ⚠ **keep emitting it, but it stops being a silent-corruption risk** |

⚠ **One real constraint remains:** Explicit refuses **managed references at overlapping offsets**.
✅ **Not reachable here** — blackboard variables live in a `fixed byte[100]` region and are unmanaged
by construction. ⭐ **Add a rail that refuses a managed-typed blackboard variable**, so this stays true.

⭐ **The corpus asset (`Vector3` after `byte`) is still required** — it is the only thing that makes
this answer falsifiable, and today no gate can see the class at all.

**D → D2 now, D1 once `U-12` lands, and the interim rail is not optional.**
⭐ Claude's sequencing is right, but the framing should be sharper: **path 1 is not "a second
convention", it is a path with no allocator at all.** A rail that refuses any standalone key other
than `@0` is therefore not a stopgap — it is **the statement of the invariant that currently holds by
accident**, which is the thing worth writing down regardless of when D1 happens.
⇒ **Build the rail in the same batch as the `Q28-A` gate** (both are generate-time diagnostics over
keys), and let D1 follow the store flip.

### 📌 What the ruling changes about the questions

| | |
|---|---|
| ⭐⭐ **A gate batch appears that no question asked for** | duplicate-id + reserved-value + standalone-`@0` rails are one coherent, headless deliverable. ⚠ **At 4.5 % today it is a defect fix, not a preparation** — ship it first and alone |
| ✅ **C1's precondition is discharged** | `M1` measured it; C1 is unconditional, and it **subsumes the `bool` rule** |
| ⭐ **A3 died on a better argument than the draft had** | ROM outlives the registry (`M2`); content-addressing is the mechanism, not a shortcut |
| ⭐ **B's rail changed shape** | from *"asset wins"* to *"a method may not be bound both ways"* — structural, not precedence |
| ⛔ **`Q29-C` is now confirmed to ride with `Q28-A`** | `M4`: the slot key is inside the registry key and baked as a `const`. **Widening it to carry `SlotKind` IS this re-bake** |

### ⚠ Ordering, ruled

⭐ **Relay/execute `#28` and `#29` TOGETHER — one round.** The dependency between them is a **single
edge**: `Q29-C`'s `FNV(AssetId, StableId, SlotKind)` widens the same key `Q28-A` rules on (`M4`).
Everything else in `#29` is independent. ⇒ **splitting would mean answering `Q29-C` twice.**

⭐ **Build order, which is NOT the document order:**

| # | | why |
|---|---|---|
| **1** | ⭐⭐ **the gate** (`Q28-A` items i–iii) + the `Vector3`-after-`byte` corpus asset | headless; fixes a live 4.5 % hazard; gives every later step a green baseline |
| **2** | 🔴 **kill `HsmBridgeEmitCore`'s counter-allocated stubs** | they can overwrite real actions **now**, independent of every ruling here |
| **3** | **C1** (explicit layout) | byte-stable; the corpus asset from step 1 proves it |
| **4** | **A2 re-bake + `Q29-C`'s `SlotKind`** together | one key change, one verification |
| **5** | **B2** (asset-driven HSM thunks) — i.e. `HSM-016` proper | needs 1–4 green first |
| **6** | **D1** (retire the stride path) — ⚠ **after `U-12`** | touches `AiPrimitiveEmitter` during the store flip otherwise |
